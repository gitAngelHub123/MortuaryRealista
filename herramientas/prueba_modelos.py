# © Angel — Compara modelos de escalado IA sobre texturas reales del juego
import os, sys, glob, time
import numpy as np
from PIL import Image, ImageDraw
import torch
from escalador import Escalador

BASE = os.path.dirname(os.path.abspath(__file__))
MODELOS = ["4x-UltraSharp.safetensors", "4x-UltraSharpV2_Lite.safetensors", "4x-UltraSharpV2.safetensors",
           "4x_foolhardy_Remacri.pth", "4xNomos8kDAT.safetensors"]
muestras, salida = sys.argv[1], sys.argv[2]
os.makedirs(salida, exist_ok=True)
imgs = [(os.path.basename(r).split("__")[0], np.array(Image.open(r).convert("RGB")))
        for r in sorted(glob.glob(os.path.join(muestras, "*.png")))]
C = 96
paneles = {n: [("bicubico", np.array(Image.fromarray(im[im.shape[0]//3:im.shape[0]//3 + C, im.shape[1]//3:im.shape[1]//3 + C]).resize((C*4, C*4), Image.BICUBIC)))] for n, im in imgs}
for m in MODELOS:
    t0 = time.time()
    esc = Escalador(os.path.join(BASE, "modelos_pt", m), mosaico=192 if "DAT" in m or "V2.s" in m else 256)
    print(m, "cargado", esc.desc.architecture.name, "medio" if esc.medio else "fp32", f"{time.time()-t0:.1f}s")
    for n, im in imgs:
        t1 = time.time()
        r = esc.escalar_rgb(im)
        dt = time.time() - t1
        Image.fromarray(r).save(os.path.join(salida, f"{n}__{m.split('.')[0]}.png"))
        cy, cx = im.shape[0] // 3, im.shape[1] // 3
        paneles[n].append((m.split(".")[0], r[cy*4:(cy+C)*4, cx*4:(cx+C)*4]))
        print(f"  {n} {im.shape[1]}x{im.shape[0]} -> {dt:.1f}s")
    del esc
    torch.cuda.empty_cache()
for n, ps in paneles.items():
    lienzo = Image.new("RGB", (len(ps) * (C*4 + 4), C*4 + 24), (20, 20, 20))
    d = ImageDraw.Draw(lienzo)
    for i, (et, p) in enumerate(ps):
        lienzo.paste(Image.fromarray(p), (i * (C*4 + 4), 24))
        d.text((i * (C*4 + 4) + 4, 4), et, fill=(255, 255, 0))
    lienzo.save(os.path.join(salida, f"COMPARA_{n}.png"))
print("fin")
