# © Angel — Extrae texturas del juego a PNG (solo lectura de los assets)
# Uso: extraer.py SALIDA nombre1 nombre2 ...   (todas las copias con ese nombre)
import os, sys, json
import UnityPy

DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
salida = sys.argv[1]
nombres = set(sys.argv[2:])
os.makedirs(salida, exist_ok=True)
env = UnityPy.load(DATA)
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    t = obj.read()
    if t.m_Name not in nombres:
        continue
    img = t.image
    ruta = os.path.join(salida, f"{t.m_Name}__{t.m_Width}x{t.m_Height}__{obj.path_id}.png")
    img.save(ruta)
    print(ruta, img.mode, t.m_TextureFormat)
