# © Angel — Verifica archivos .mtex: decodifica el nivel 0 y compara con el original (color y relieve)
import os, sys, json, struct
import numpy as np
from PIL import Image, ImageDraw
import texture2ddecoder as t2d

def leer_mtex(ruta):
    with open(ruta, "rb") as f:
        assert f.read(4) == b"MTX1"
        ver, W, H, fmt, mips, n = struct.unpack("<6i", f.read(24))
        datos = f.read(n)
    # tamaño del nivel 0 (bloques 4x4 de 16 bytes en BC7 y BC5)
    n0 = ((W + 3) // 4) * ((H + 3) // 4) * 16
    dec = t2d.decode_bc7 if fmt == 1 else t2d.decode_bc5
    bgra = np.frombuffer(dec(datos[:n0], W, H), np.uint8).reshape(H, W, 4)
    # tamaño total esperado con todos los mips
    total, w, h = 0, W, H
    for i in range(mips):
        total += ((max(w, 1) + 3) // 4) * ((max(h, 1) + 3) // 4) * 16
        w, h = max(1, w // 2), max(1, h // 2)
    return fmt, W, H, mips, len(datos), total, bgra[..., [2, 1, 0, 3]]

def sombreado(nx, ny, nz):
    L = np.array([0.6, 0.6, 0.53]); L /= np.linalg.norm(L)
    return np.clip(nx * L[0] + ny * L[1] + nz * L[2], 0, 1)

carpeta, muestras, salida = sys.argv[1], sys.argv[2], sys.argv[3]
man = [json.loads(l) for l in open(os.path.join(carpeta, "manifiesto.jsonl"))]
vistos = set()
for e in man:
    if e["archivo"] in vistos: continue
    vistos.add(e["archivo"])
    fmt, W, H, mips, n, total, rgba = leer_mtex(os.path.join(carpeta, e["archivo"]))
    ok = "OK" if n == total else f"MAL tamaño {n} != {total}"
    print(f'{e["nombre"]:28s} {e["rol"]:6s} {W}x{H} mips={mips} {ok}')
    orig_p = [f for f in os.listdir(muestras) if f.startswith(e["nombre"] + "__")]
    if not orig_p: continue
    o = np.array(Image.open(os.path.join(muestras, orig_p[0])).convert("RGBA"))   # derecho
    nuevo = rgba[::-1]                                                              # a derecho
    c = 256; y0, x0 = H // 3, W // 3; fy, fx = o.shape[0] / H, o.shape[1] / W
    if e["rol"] == "color":
        a = Image.fromarray(o[..., :3]).resize((W, H), Image.BICUBIC).crop((x0, y0, x0 + c, y0 + c))
        b = Image.fromarray(nuevo[..., :3]).crop((x0, y0, x0 + c, y0 + c))
    else:
        ox = o[..., 3] / 127.5 - 1; oy = o[..., 1] / 127.5 - 1; oz = np.sqrt(np.clip(1 - ox**2 - oy**2, 0, 1))
        so = (sombreado(ox, oy, oz) * 255).astype(np.uint8)
        a = Image.fromarray(so).resize((W, H), Image.BICUBIC).crop((x0, y0, x0 + c, y0 + c)).convert("RGB")
        nx = nuevo[..., 0] / 127.5 - 1; ny = nuevo[..., 1] / 127.5 - 1; nz = np.sqrt(np.clip(1 - nx**2 - ny**2, 0, 1))
        b = Image.fromarray((sombreado(nx, ny, nz) * 255).astype(np.uint8)).crop((x0, y0, x0 + c, y0 + c)).convert("RGB")
    L = Image.new("RGB", (2 * c + 4, c + 16), (20, 20, 20)); d = ImageDraw.Draw(L)
    L.paste(a, (0, 16)); L.paste(b, (c + 4, 16))
    d.text((3, 2), "original (bicubico)", fill=(255, 255, 0)); d.text((c + 7, 2), "IA + BC", fill=(255, 255, 0))
    L.save(os.path.join(salida, f"VER_{e['nombre']}_{e['archivo'][:8]}.png"))
