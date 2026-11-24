# © Angel — Genera indice.tsv (lo que lee el plugin) a partir de manifiesto.jsonl
import os, sys, json

carpeta = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "texturas_ia")
FORMATO = {"color": 1, "normal": 3}
vistos, lineas, faltan = set(), [], 0
for l in open(os.path.join(carpeta, "manifiesto.jsonl")):
    e = json.loads(l)
    clave = (e["nombre"], e["ancho"], e["alto"], e["archivo"])
    if clave in vistos:
        continue
    vistos.add(clave)
    if not os.path.exists(os.path.join(carpeta, e["archivo"])):
        faltan += 1
        continue
    if "\t" in e["nombre"]:
        continue
    lineas.append("\t".join(map(str, [e["nombre"], e["ancho"], e["alto"], e["rol"], e["archivo"],
                                       e["W"], e["H"], FORMATO[e["rol"]], e["huella"]])))
with open(os.path.join(carpeta, "indice.tsv"), "w") as f:
    f.write("# nombre\tancho\talto\trol\tarchivo\tW\tH\tformato\thuella16x16\n")
    f.write("\n".join(lineas) + "\n")
print(f"indice.tsv: {len(lineas)} entradas ({faltan} aún sin archivo)")
