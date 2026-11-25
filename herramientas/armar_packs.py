# © Angel — Arma los packs de texturas para publicar (Ultra 4K y Normal 2K) a partir de texturas_ia/.
# Solo copia lo que usa indice.tsv (más especiales/ y huellas64.tsv); las versiones viejas reemplazadas quedan fuera.
# Normal: a las texturas de más de 2048 se les quitan los primeros niveles de mipmap (misma textura, a 2048).
# Uso: armar_packs.py ORIGEN SALIDA   → SALIDA/ultra/... y SALIDA/normal/... con la estructura BepInEx del mod
import os, sys, struct, shutil

ORIG, SAL = sys.argv[1], sys.argv[2]
RUTA = os.path.join("BepInEx", "plugins", "MortuaryRealista", "TexturasIA")
TOPE_NORMAL = 2048


def bytes_hasta(W, H, k):
    b = 0
    for i in range(k):
        b += max(1, ((W >> i) + 3) // 4) * max(1, ((H >> i) + 3) // 4) * 16
    return b


def reducir(origen, destino, tope):
    """Copia un .mtex quitando niveles hasta que ancho y alto queden <= tope. Devuelve (W, H) final."""
    with open(origen, "rb") as f:
        d = f.read()
    magia, ver, W, H, fmt, mips, largo = struct.unpack_from("<4sIIIIII", d, 0)
    assert magia in (b"MTX1",), (origen, magia)
    assert largo == len(d) - 28, (origen, largo, len(d))
    k = 0
    while ((W >> k) > tope or (H >> k) > tope) and mips - k > 1:
        k += 1
    if k == 0:
        shutil.copyfile(origen, destino)
        return W, H
    salto = bytes_hasta(W, H, k)
    datos = d[28 + salto:]
    cab = struct.pack("<4sIIIIII", magia, ver, W >> k, H >> k, fmt, mips - k, len(datos))
    with open(destino, "wb") as f:
        f.write(cab)
        f.write(datos)
    return W >> k, H >> k


def armar(tipo):
    dest = os.path.join(SAL, tipo, RUTA)
    os.makedirs(os.path.join(dest, "especiales"), exist_ok=True)
    lineas = open(os.path.join(ORIG, "indice.tsv"), encoding="utf-8").read().splitlines()
    salida, hechos, total = [], {}, 0
    for ln in lineas:
        if not ln or ln.startswith("#"):
            salida.append(ln)
            continue
        c = ln.split("\t")
        arch = c[4]
        if arch not in hechos:
            src = os.path.join(ORIG, arch)
            if tipo == "ultra":
                shutil.copyfile(src, os.path.join(dest, arch))
                hechos[arch] = (arch, int(c[5]), int(c[6]))
            else:
                nombre_tmp = os.path.join(dest, arch + ".tmp")
                W, H = reducir(src, nombre_tmp, TOPE_NORMAL)
                nuevo = arch
                if (W, H) != (int(c[5]), int(c[6])):
                    nuevo = arch.replace(f"_{c[5]}x{c[6]}.mtex", f"_{W}x{H}.mtex")
                os.replace(nombre_tmp, os.path.join(dest, nuevo))
                hechos[arch] = (nuevo, W, H)
            total += os.path.getsize(os.path.join(dest, hechos[arch][0]))
        nuevo, W, H = hechos[arch]
        c[4], c[5], c[6] = nuevo, str(W), str(H)
        salida.append("\t".join(c))
    with open(os.path.join(dest, "indice.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(salida) + "\n")
    shutil.copyfile(os.path.join(ORIG, "huellas64.tsv"), os.path.join(dest, "huellas64.tsv"))
    for e in os.listdir(os.path.join(ORIG, "especiales")):
        src = os.path.join(ORIG, "especiales", e)
        dst = os.path.join(dest, "especiales", e)
        if tipo == "normal" and e.endswith(".mtex"):
            reducir(src, dst, TOPE_NORMAL)
        else:
            shutil.copyfile(src, dst)
        total += os.path.getsize(dst)
    print(f"{tipo}: {len(hechos)} texturas, {total / 2**30:.2f} GB")


for t in ("ultra", "normal"):
    armar(t)
