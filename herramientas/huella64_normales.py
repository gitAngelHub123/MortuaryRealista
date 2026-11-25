# © Angel — Huellas 64x64 de los RELIEVES (normales) que comparten nombre y tamaño (pieles de cada cadáver).
# huella64.py solo cubre el color. Aquí se sacan de la textura ORIGINAL del juego (lo mismo que el plugin
# lee con Graphics.Blit), en orden de filas nativo de Unity, y se añaden a texturas_ia/huellas64.tsv.
# Ejecutar después de generar_indice.py y huella64.py.
import os, sys, json
from collections import defaultdict
import numpy as np
from PIL import Image
import UnityPy

BASE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
carpeta = sys.argv[1] if len(sys.argv) > 1 else os.path.join(BASE, "..", "texturas_ia")

filas = [l.rstrip("\n").split("\t") for l in open(os.path.join(carpeta, "indice.tsv")) if l.strip() and not l.startswith("#")]
grupos = defaultdict(list)
for r in filas:
    grupos[(r[0], r[1], r[2])].append(r)
buscados = {r[4] for g in grupos.values() if len(g) > 1 for r in g if r[3] == "normal"}

clave_de = {}
for l in open(os.path.join(carpeta, "manifiesto.jsonl")):
    e = json.loads(l)
    if e["archivo"] in buscados:
        clave_de[e["clave"]] = e["archivo"]
print("relieves ambiguos:", len(buscados), "| con clave:", len(set(clave_de.values())), flush=True)

ruta = os.path.join(carpeta, "huellas64.tsv")
ya = {l.split("\t", 1)[0] for l in open(ruta) if "\t" in l} if os.path.exists(ruta) else set()
env = UnityPy.load(DATA)
nuevas, hechos = [], set()
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    archivo = clave_de.get(f"{obj.assets_file.name}:{obj.path_id}")
    if not archivo or archivo in hechos or archivo in ya:
        continue
    t = obj.read()
    rgba = np.ascontiguousarray(np.array(t.image.convert("RGBA"))[::-1])   # orden nativo, como procesar.py
    im = Image.fromarray(rgba, "RGBA").resize((64, 64), Image.BOX)
    nuevas.append(archivo + "\t" + im.tobytes().hex())
    hechos.add(archivo)
with open(ruta, "a") as f:
    for l in nuevas:
        f.write(l + "\n")
print("huellas64.tsv: +", len(nuevas), "relieves", flush=True)
