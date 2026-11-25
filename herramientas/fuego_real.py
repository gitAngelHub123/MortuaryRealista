# © Angel — Llama realista para el fuego del crematorio y el encendedor.
# El juego dibuja esos fuegos (VFX Graph) con la textura "flame": un óvalo borroso de 512 px; muchas copias sumadas
# dan un disco blanco. Aquí se arma una llama con forma real a partir de FlameParticleSheet (fotos de fuego del propio
# juego): el alfa sale del brillo de varias lenguas de fuego y el color guarda el degradado del fuego (centro claro,
# bordes rojizos). Salida: texturas_ia/especiales/flame_512.dxt5 (DXT5 con mips, filas en orden de Unity) que el
# plugin copia sobre la textura original en la GPU.
import os, sys, struct
import numpy as np, UnityPy, etcpak
from PIL import Image, ImageFilter

BASE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
SAL = os.path.join(BASE, "..", "texturas_ia", "especiales"); os.makedirs(SAL, exist_ok=True)
PREV = os.path.join(BASE, "..", "trabajo", "fuego")

hoja = None
for o in UnityPy.load(DATA).objects:
    if o.type.name == "Texture2D" and o.peek_name() == "FlameParticleSheet":
        hoja = np.array(o.read().image.convert("RGB")).astype(np.float32) / 255; break
N = 6; T = hoja.shape[0] // N
cuadros = [hoja[r * T:(r + 1) * T, c * T:(c + 1) * T] for r in range(N) for c in range(N)]
area = [(q.max(-1) > 0.15).sum() for q in cuadros]
orden = np.argsort(area)[::-1]

def recorte(q):
    m = q.max(-1) > 0.05; ys, xs = np.where(m)
    return q[ys.min():ys.max() + 1, xs.min():xs.max() + 1]

S = 512
lienzo = np.zeros((S, S, 3), np.float32)
# tres lenguas: la central más alta, dos laterales más bajas y algo superpuestas -> silueta de fuego, no óvalo
for k, (cx, alto, ancho) in enumerate([(0.50, 0.92, 0.50), (0.33, 0.70, 0.40), (0.67, 0.74, 0.40)]):
    q = recorte(cuadros[orden[k]])
    h, w = int(S * alto), int(S * ancho)
    im = np.array(Image.fromarray((q * 255).astype(np.uint8)).resize((w, h), Image.LANCZOS)).astype(np.float32) / 255
    x0 = int(S * cx - w / 2); y0 = int(S * 0.97) - h
    lienzo[y0:y0 + h, x0:x0 + w] = np.maximum(lienzo[y0:y0 + h, x0:x0 + w], im)
lum = lienzo.max(-1)
# borde suave para que el cuadro de la partícula nunca se note
yy, xx = np.mgrid[0:S, 0:S] / (S - 1)
ventana = np.clip(np.minimum.reduce([xx, 1 - xx, yy, 1 - yy]) / 0.06, 0, 1)
alfa = np.clip(lum ** 1.4 * 0.5, 0, 1) * ventana   # misma energía total o menos que el óvalo original (alfa medio 0,07): menos blanco al sumarse
alfa *= 0.065 / alfa.mean()   # energía total igual a la del óvalo original (0,071) o algo menos
color = lienzo / np.maximum(lum[..., None], 1e-3)           # tono del fuego normalizado (sin oscurecer)
color = np.clip(color, 0, 1)
color[lum < 0.02] = (1.0, 0.55, 0.2)
print("alfa medio %.3f (original 0,071), tope %.2f" % (alfa.mean(), alfa.max()))
rgba = np.dstack([color, alfa])
Image.fromarray((rgba * 255).astype(np.uint8), "RGBA").save(os.path.join(PREV, "flame_real.png"))
vista = Image.new("RGB", (S, S)); vista.paste(Image.fromarray((color * alfa[..., None] * 255).astype(np.uint8)))
vista.save(os.path.join(PREV, "flame_real_vista.png"))

# mips con promedio premultiplicado y compresión DXT5 (filas en orden de Unity: abajo hacia arriba)
niveles, cur = [], rgba[::-1].copy()
while True:
    u8 = np.clip(np.round(cur * 255), 0, 255).astype(np.uint8)
    niveles.append(u8)
    if cur.shape[0] == 1: break
    a = cur[..., 3:4]; pm = cur[..., :3] * a
    h2 = cur.shape[0] // 2
    a2 = a.reshape(h2, 2, h2, 2, 1).mean((1, 3)); pm2 = pm.reshape(h2, 2, h2, 2, 3).mean((1, 3))
    cur = np.concatenate([pm2 / np.maximum(a2, 1e-4), a2], -1); cur[..., :3] = np.where(a2 > 1e-4, cur[..., :3], 1)
datos = b""
for nv in niveles:
    h, w = nv.shape[:2]
    if w < 4: nv = np.array(Image.fromarray(nv, "RGBA").resize((4, 4), Image.NEAREST)); h = w = 4
    for y in range(0, h, 64):   # por franjas, como procesar.py
        b = np.ascontiguousarray(nv[y:y + 64])
        datos += etcpak.compress_bc3(b.tobytes(), w, b.shape[0])
with open(os.path.join(SAL, "flame_512.dxt5"), "wb") as f:
    f.write(b"MTX1"); f.write(struct.pack("<6i", 1, S, S, 2, len(niveles), len(datos))); f.write(datos)
# comprobación del orden de canales
import texture2ddecoder
dec = np.frombuffer(texture2ddecoder.decode_bc3(datos[:S * S], S, S), np.uint8).reshape(S, S, 4)[..., [2, 1, 0, 3]]
print("niveles", len(niveles), "bytes", len(datos), "| canal decodificado (centro):", dec[S // 2, S // 2], "esperado RGBA≈", niveles[0][S // 2, S // 2])

# ---- hoja animada de 6x6 llamas (FlameParticleSheet) para el fuego de partículas que reemplaza al del crematorio ----
H = (hoja * 255).astype(np.uint8)
lumh = H.max(-1).astype(np.float32) / 255
rgbh = np.clip(hoja / np.maximum(lumh[..., None], 1e-3), 0, 1); rgbh[lumh < 0.02] = (1.0, 0.5, 0.15)
ah = np.clip(lumh ** 1.1, 0, 1)
sheet = np.dstack([rgbh, ah]).astype(np.float32)
niveles, cur = [], sheet[::-1].copy()
while True:
    niveles.append(np.clip(np.round(cur * 255), 0, 255).astype(np.uint8))
    if cur.shape[0] == 1: break
    a = cur[..., 3:4]; pm = cur[..., :3] * a; h2 = cur.shape[0] // 2
    a2 = a.reshape(h2, 2, h2, 2, 1).mean((1, 3)); pm2 = pm.reshape(h2, 2, h2, 2, 3).mean((1, 3))
    cur = np.concatenate([np.where(a2 > 1e-4, pm2 / np.maximum(a2, 1e-4), 1), a2], -1)
datos = b""
for nv in niveles:
    h, w = nv.shape[:2]
    if w < 4: nv = np.array(Image.fromarray(nv, "RGBA").resize((4, 4), Image.NEAREST)); h = w = 4
    for y in range(0, h, 64):
        datos += etcpak.compress_bc3(np.ascontiguousarray(nv[y:y + 64]).tobytes(), w, nv[y:y + 64].shape[0])
Wh = hoja.shape[0]
with open(os.path.join(SAL, "llamas_6x6.dxt5"), "wb") as f:
    f.write(b"MTX1"); f.write(struct.pack("<6i", 1, Wh, Wh, 2, len(niveles), len(datos))); f.write(datos)
print("hoja de llamas", Wh, "niveles", len(niveles), "bytes", len(datos))
