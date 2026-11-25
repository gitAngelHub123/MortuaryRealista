# © Angel — Microdetalle "horneado" en los relieves IA de superficies (paredes, suelos, madera, piedra...).
# El juego no trae compilada la variante de shader con mapa de detalle (_DETAIL_MAP), así que el grano fino
# se mete directo en el mapa de normales: se saca del propio color (paso alto de la luminancia = manchas,
# vetas, rayones, poros del yeso), se convierte en relieve y se mezcla con el relieve IA (Reoriented Normal Mapping).
# Salida: <sha>_normaldet_WxH.mtex + entrada en manifiesto.jsonl con "detalle": true (generar_indice.py la prefiere).
import os, sys, json, struct, hashlib, argparse
import numpy as np
import texture2ddecoder
from PIL import Image, ImageFilter

BASE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, BASE)
from procesar import comprimir_nivel, reducir2   # mismas funciones de compresión y mips

SUP = ["Wall", "Floor", "Tile", "Ceiling", "plane_", "Modules", "Brick", "Street", "SideWalk", "Wood", "Door", "Shingles",
       "Trim", "Carpet", "Concrete", "Stone", "Rock", "Gravel", "Cliff", "Bark"]


def leer_mtex(ruta):
    d = open(ruta, "rb").read()
    _, W, H, fmt, n, L = struct.unpack("<6i", d[4:28])
    sz = max(1, (W + 3) // 4) * max(1, (H + 3) // 4) * 16
    raw = d[28:28 + sz]
    if fmt == 1:
        im = np.frombuffer(texture2ddecoder.decode_bc7(raw, W, H), np.uint8).reshape(H, W, 4)[..., [2, 1, 0, 3]]
    else:
        im = np.frombuffer(texture2ddecoder.decode_bc5(raw, W, H), np.uint8).reshape(H, W, 4)[..., [2, 1, 0, 3]]
    return W, H, im


def desenfocar(a, sigma):
    """Gaussiano separable con relleno circular (las superficies se repiten en mosaico)."""
    import torch
    import torch.nn.functional as F
    r = max(1, int(round(sigma * 3)))
    x = torch.arange(-r, r + 1, dtype=torch.float32)
    k = torch.exp(-x * x / (2 * sigma * sigma)); k /= k.sum()
    t = torch.from_numpy(np.ascontiguousarray(a, np.float32))[None, None]
    t = F.conv2d(F.pad(t, (r, r, 0, 0), mode="circular"), k.view(1, 1, 1, -1))
    t = F.conv2d(F.pad(t, (0, 0, r, r), mode="circular"), k.view(1, 1, -1, 1))
    return t[0, 0].numpy()


def detalle_desde_color(rgb, W, H, radio, fuerza):
    lum = (0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]).astype(np.float32) / 255.0
    if lum.shape != (H, W):
        lum = np.array(Image.fromarray(lum, "F").resize((W, H), Image.BICUBIC))
    borroso = desenfocar(lum, radio)
    alto = lum - borroso                                  # solo lo fino: manchas, vetas, rayones
    alto = np.clip(alto / (np.std(alto) * 4 + 1e-6), -1, 1)   # misma intensidad en texturas claras y oscuras
    gy, gx = np.gradient(alto)                            # filas en orden nativo de Unity: eje 0 = +v
    n = np.dstack([-gx * fuerza, -gy * fuerza, np.ones_like(gx)])
    return n / np.linalg.norm(n, axis=-1, keepdims=True)


def mezclar_rnm(base, det):
    t = base + np.array([0, 0, 1], np.float32)
    u = det * np.array([-1, -1, 1], np.float32)
    r = t * (np.sum(t * u, -1, keepdims=True) / t[..., 2:3]) - u
    return r / np.linalg.norm(r, axis=-1, keepdims=True)


def escribir_normal(n, ruta):
    def norm(v):
        v = v.copy(); v[..., 2] = np.maximum(v[..., 2], 0.0)
        return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-6)
    cur, niveles = norm(n), []
    while True:
        x8 = np.clip(np.round((cur[..., 0] + 1) * 127.5), 0, 255).astype(np.uint8)
        y8 = np.clip(np.round((cur[..., 1] + 1) * 127.5), 0, 255).astype(np.uint8)
        niveles.append(np.dstack([x8, y8, np.zeros_like(x8), np.full_like(x8, 255)]))
        if cur.shape[0] == 1 and cur.shape[1] == 1:
            break
        cur = norm(reducir2(cur))
    datos = b"".join(comprimir_nivel(nv, 3) for nv in niveles)
    H, W = n.shape[:2]
    with open(ruta + ".tmp", "wb") as f:
        f.write(b"MTX1"); f.write(struct.pack("<6i", 1, W, H, 3, len(niveles), len(datos))); f.write(datos)
    os.replace(ruta + ".tmp", ruta)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--carpeta", default=os.path.join(BASE, "..", "texturas_ia"))
    ap.add_argument("--fuerza", type=float, default=2.0)
    ap.add_argument("--radio", type=float, default=3.0, help="radio del paso alto en píxeles de la textura final")
    ap.add_argument("--solo", nargs="*", default=None, help="nombres de relieve (prefijos) a procesar")
    ap.add_argument("--etiqueta", default="normaldet")
    a = ap.parse_args()

    mats = json.load(open(os.path.join(BASE, "materiales.json")))
    man = [json.loads(l) for l in open(os.path.join(a.carpeta, "manifiesto.jsonl"))]
    vivos = [e for e in man if os.path.exists(os.path.join(a.carpeta, e["archivo"])) and not e.get("detalle")]
    por_nombre = {}
    for e in vivos:
        por_nombre.setdefault((e["nombre"], e["rol"]), []).append(e)

    pares = {}
    for m in mats:
        if m["shader"] != "HDRP/Lit" or "Skin" in m["nombre"] or "Pbr" in m["nombre"]:
            continue
        c, nr = m["tex"].get("_BaseColorMap"), m["tex"].get("_NormalMap")
        if not c or not nr or not any(k.lower() in (m["nombre"] + c[0]).lower() for k in SUP):
            continue
        if a.solo and not any(nr[0].startswith(s) for s in a.solo):
            continue
        if (c[0], "color") in por_nombre and (nr[0], "normal") in por_nombre:
            pares.setdefault(nr[0], c[0])
    print("relieves de superficie a tratar:", len(pares), flush=True)

    salida = open(os.path.join(a.carpeta, "manifiesto.jsonl"), "a")
    for nombre_n, nombre_c in pares.items():
        col = por_nombre[(nombre_c, "color")][0]
        for archivo_n in sorted({e["archivo"] for e in por_nombre[(nombre_n, "normal")]}):
            W, H, nim = leer_mtex(os.path.join(a.carpeta, archivo_n))
            x = nim[..., 0].astype(np.float32) / 127.5 - 1; y = nim[..., 1].astype(np.float32) / 127.5 - 1
            base = np.dstack([x, y, np.sqrt(np.clip(1 - x * x - y * y, 0, 1))]).astype(np.float32)
            _, _, cim = leer_mtex(os.path.join(a.carpeta, col["archivo"]))
            det = detalle_desde_color(cim[..., :3], W, H, a.radio * W / 4096, a.fuerza)
            res = mezclar_rnm(base, det)
            sha = hashlib.sha1((archivo_n + f"{a.fuerza}{a.radio}").encode()).hexdigest()[:20]
            nuevo = f"{sha}_{a.etiqueta}_{W}x{H}.mtex"
            escribir_normal(res, os.path.join(a.carpeta, nuevo))
            for e in por_nombre[(nombre_n, "normal")]:
                if e["archivo"] == archivo_n:
                    e2 = dict(e); e2["archivo"] = nuevo; e2["detalle"] = True; e2.pop("pelo", None)
                    salida.write(json.dumps(e2) + "\n")
            salida.flush()
            print(f"  {nombre_n} ({W}x{H}) + detalle de {nombre_c} -> {nuevo}", flush=True)
    salida.close()


if __name__ == "__main__":
    main()
