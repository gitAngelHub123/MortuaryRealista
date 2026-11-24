# © Angel — Huellas de 64x64 para las texturas IA que comparten nombre y tamaño (pieles de cada cadáver, etc.)
# Se sacan del nivel de mip de 64 px de cada .mtex de color (BC7) → texturas_ia/huellas64.tsv
import os, sys, struct
from collections import defaultdict
import texture2ddecoder
from PIL import Image

carpeta = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "texturas_ia")
filas = [l.rstrip("\n").split("\t") for l in open(os.path.join(carpeta, "indice.tsv")) if l.strip() and not l.startswith("#")]
grupos = defaultdict(list)
for r in filas:
    grupos[(r[0], r[1], r[2])].append(r)

def nivel64(ruta):
    d = open(ruta, "rb").read()
    _, W, H, fmt, n, L = struct.unpack("<6i", d[4:28])
    off, w, h = 28, W, H
    for i in range(n):
        bw, bh = max(4, w), max(4, h)
        sz = (bw // 4) * (bh // 4) * 16
        if w <= 64 and h <= 64 or i == n - 1:
            raw = texture2ddecoder.decode_bc7(d[off:off + sz], bw, bh)
            im = Image.frombytes("RGBA", (bw, bh), raw, "raw", "BGRA")
            return im.resize((64, 64), Image.BOX)
        off += sz; w, h = max(1, w // 2), max(1, h // 2)

salida, n = [], 0
for k, lista in grupos.items():
    if len(lista) < 2:
        continue
    for r in lista:
        if r[3] != "color":
            continue
        im = nivel64(os.path.join(carpeta, r[4]))
        salida.append(r[4] + "\t" + im.tobytes().hex())
        n += 1
with open(os.path.join(carpeta, "huellas64.tsv"), "w") as f:
    f.write("\n".join(salida) + "\n")
print("huellas64.tsv:", n, "entradas")
