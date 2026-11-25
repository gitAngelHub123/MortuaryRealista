# © Angel — Genera indice.tsv (lo que lee el plugin) a partir de manifiesto.jsonl
import os, sys, json

args = [x for x in sys.argv[1:] if not x.startswith("--")]
carpeta = args[0] if args else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "texturas_ia")
FORMATO = {"color": 1, "normal": 3}
vistos, lineas, faltan = set(), [], 0
# texturas con versión "pelo" (alfa por IA y cobertura conservada): la versión vieja de esa misma textura se descarta
entradas = [json.loads(l) for l in open(os.path.join(carpeta, "manifiesto.jsonl"))]
# lo mismo para los relieves con microdetalle horneado ("detalle"), salvo que se pida sin detalle
sin_detalle = "--sin-detalle" in sys.argv
def mejorado(e):
    return e.get("pelo") or (e.get("detalle") and not sin_detalle)
con_pelo = {(e.get("clave"), e["rol"]) for e in entradas if mejorado(e) and os.path.exists(os.path.join(carpeta, e["archivo"]))}
descartadas = 0
# hojas de árbol: original de 128 px con recorte por alfa; ampliada deja un contorno claro alrededor de cada hoja (visto 2026-11-24)
NO_IA = ("TreeLeaf_",)
for e in entradas:
    if e["nombre"].startswith(NO_IA):
        continue
    if e.get("detalle") and sin_detalle:
        continue
    if not mejorado(e) and (e.get("clave"), e["rol"]) in con_pelo:
        descartadas += 1
        continue
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
print(f"indice.tsv: {len(lineas)} entradas ({faltan} aún sin archivo, {descartadas} versiones viejas reemplazadas por las de pelo o microdetalle)")
