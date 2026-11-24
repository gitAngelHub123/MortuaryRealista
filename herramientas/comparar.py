# © Angel — Arma una comparación antes/después a partir de dos capturas del juego
# Uso: comparar.py antes.png despues.png salida.png [x y ancho alto]  (recorte opcional a tamaño real)
import sys
from PIL import Image, ImageDraw

a, b, salida = Image.open(sys.argv[1]).convert("RGB"), Image.open(sys.argv[2]).convert("RGB"), sys.argv[3]
if len(sys.argv) >= 8:
    x, y, w, h = map(int, sys.argv[4:8])
    a, b = a.crop((x, y, x + w, y + h)), b.crop((x, y, x + w, y + h))
else:
    a, b = a.resize((a.width // 2, a.height // 2), Image.LANCZOS), b.resize((b.width // 2, b.height // 2), Image.LANCZOS)
L = Image.new("RGB", (a.width * 2 + 6, a.height + 22), (15, 15, 15))
L.paste(a, (0, 22)); L.paste(b, (a.width + 6, 22))
d = ImageDraw.Draw(L)
d.text((6, 4), "ANTES (juego original)", fill=(255, 230, 0))
d.text((a.width + 12, 4), "DESPUÉS (Mortuary Realista)", fill=(255, 230, 0))
L.save(salida)
print(salida, L.size)
