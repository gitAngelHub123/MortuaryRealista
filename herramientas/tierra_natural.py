# © Angel — Suelo de tierra SIN repetición visible para la opción "Tierra en vez de pasto".
# La textura de tierra del juego (TexturesCom_Rock_Soil, versión IA 4K) se repite cada 2,5 m en cuadrícula y se nota.
# Aquí se arma una textura nueva de 4096 px que cubre 5 m: trozos de la original tomados de lugares al azar, girados
# y espejados, unidos por el borde de las piedras (la costura sigue el relieve, no una línea recta), más variación
# grande de color, zonas más oscuras/mojadas y charcos con más brillo. Es mosaico (sus bordes empalman) y los
# relieves se giran junto con el color para que la luz siga cayendo bien.
# Salida: texturas_ia/especiales/tierra_{color,normal,mascara}.mtex (MTX1: BC7 / BC5 / BC7 lineal).
import os, sys, struct
import numpy as np, UnityPy
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, BASE)
from microdetalle import leer_mtex, desenfocar, escribir_normal
from procesar import comprimir_nivel, reducir2

IA = os.path.join(BASE, "..", "texturas_ia")
SAL = os.path.join(IA, "especiales"); os.makedirs(SAL, exist_ok=True)
PREV = os.path.join(BASE, "..", "trabajo", "tierra"); os.makedirs(PREV, exist_ok=True)
DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
N = 4096                    # salida: 4096 px = 5 m
FUENTE = 2048               # la original (2,5 m) se lleva a 2048 px para tener la misma escala (820 px/m)
rng = np.random.default_rng(1944)

def cargar():
    filas = [l.rstrip("\n").split("\t") for l in open(os.path.join(IA, "indice.tsv")) if not l.startswith("#")]
    arch = {r[0]: r[4] for r in filas}
    _, _, col = leer_mtex(os.path.join(IA, arch["TexturesCom_Rock_Soil_2K_albedo"]))
    _, _, nor = leer_mtex(os.path.join(IA, arch["TexturesCom_Rock_Soil_2K_normal"]))
    col = np.array(Image.fromarray(np.ascontiguousarray(col[..., :3])).resize((FUENTE, FUENTE), Image.LANCZOS)).astype(np.float32) / 255
    nor = np.array(Image.fromarray(np.ascontiguousarray(nor[..., :3])).resize((FUENTE, FUENTE), Image.LANCZOS)).astype(np.float32)
    nx, ny = nor[..., 0] / 127.5 - 1, nor[..., 1] / 127.5 - 1
    mask = None
    for o in UnityPy.load(DATA).objects:
        if o.type.name == "Texture2D" and o.peek_name() == "RockySoil_MaskMap":
            m = np.array(o.read().image.convert("RGBA"))[::-1]   # orden de filas de Unity, como los .mtex
            mask = np.array(Image.fromarray(np.ascontiguousarray(m)).resize((FUENTE, FUENTE), Image.LANCZOS)).astype(np.float32) / 255
            break
    if mask is None:
        mask = np.zeros((FUENTE, FUENTE, 4), np.float32); mask[..., 1] = 1; mask[..., 3] = 0.35
    return col, nx, ny, mask

def transformar(a, k, espejo):
    if espejo: a = a[:, ::-1]
    return np.rot90(a, k)

def rotar_normal(nx, ny, k, espejo):
    # mismo cambio que la imagen: espejo en u invierte x; cada giro np.rot90 (antihorario en índices) hace x' = y, y' = -x
    nx, ny = transformar(nx, k, espejo), transformar(ny, k, espejo)
    if espejo: nx = -nx   # (se aplica antes de girar: el signo se invierte en la componente que luego se gira)
    for _ in range(k % 4): nx, ny = ny, -nx
    return nx, ny

def ruido(escala_px, semilla):
    r = np.random.default_rng(semilla).standard_normal((N // 8, N // 8)).astype(np.float32)
    r = desenfocar(r, escala_px / 8)
    r = np.array(Image.fromarray(r, "F").resize((N, N), Image.BICUBIC))
    return (r - r.mean()) / (r.std() + 1e-6)

def main():
    col, nx, ny, mask = cargar()
    alt = col.mean(-1)                                      # "altura" aproximada: las piedras son más claras
    alt = (alt - alt.mean()) / (alt.std() + 1e-6)
    P = 1280; R = P / 2; paso = 640
    yy, xx = np.mgrid[0:P, 0:P].astype(np.float32)
    borde = np.clip(1 - np.hypot(yy - R, xx - R) / R, 0, 1)   # 1 en el centro, 0 en el borde del trozo
    puntaje = np.full((N, N), -1e9, np.float32)
    o_col = np.zeros((N, N, 3), np.float32); o_nx = np.zeros((N, N), np.float32); o_ny = np.zeros((N, N), np.float32)
    o_mask = np.zeros((N, N, 4), np.float32)
    centros = [(y, x) for y in range(0, N, paso) for x in range(0, N, paso)]
    for (cy, cx) in centros:
        cy += int(rng.integers(-paso // 3, paso // 3)); cx += int(rng.integers(-paso // 3, paso // 3))
        sy, sx = int(rng.integers(0, FUENTE)), int(rng.integers(0, FUENTE))
        k, esp = int(rng.integers(0, 4)), bool(rng.integers(0, 2))
        iy = (np.arange(P) + sy) % FUENTE; ix = (np.arange(P) + sx) % FUENTE      # la original también es mosaico
        c = transformar(col[np.ix_(iy, ix)], k, esp)
        h = transformar(alt[np.ix_(iy, ix)], k, esp)
        m = transformar(mask[np.ix_(iy, ix)], k, esp)
        a, b = rotar_normal(nx[np.ix_(iy, ix)], ny[np.ix_(iy, ix)], k, esp)
        s = borde * 3.0 + h * 0.45 + rng.normal(0, 0.05)     # la costura sigue el contorno de las piedras
        oy = (np.arange(P) + cy - P // 2) % N; ox = (np.arange(P) + cx - P // 2) % N
        sel = np.ix_(oy, ox)
        gana = (s > puntaje[sel]) & (borde > 0)
        for dst, src in ((o_col, c), (o_mask, m)):
            reg = dst[sel]; reg[gana] = src[gana]; dst[sel] = reg
        for dst, src in ((o_nx, a), (o_ny, b)):
            reg = dst[sel]; reg[gana] = src[gana]; dst[sel] = reg
        reg = puntaje[sel]; reg[gana] = s[gana]; puntaje[sel] = reg
    print("sin cubrir:", int((puntaje < -1e8).sum()), "píxeles", flush=True)

    # variación grande: manchas de color (más rojiza / más gris) y zonas mojadas más oscuras con charcos brillantes
    g1, g2, g3 = ruido(520, 1), ruido(260, 2), ruido(900, 3)
    tono = 1 + 0.10 * g1 + 0.05 * g2
    o_col *= tono[..., None]
    o_col[..., 0] *= 1 + 0.04 * g3; o_col[..., 2] *= 1 - 0.04 * g3
    mojado = np.clip((g2 * 0.7 + g1 * 0.5 - 0.6) / 1.2, 0, 1)            # ~20 % del suelo algo más mojado
    charco = np.clip((g2 * 0.6 + g3 * 0.6 - 1.55) / 0.35, 0, 1)          # pocos charcos chicos
    o_col *= (1 - 0.25 * mojado - 0.15 * charco)[..., None]
    o_mask[..., 3] = np.clip(o_mask[..., 3] + 0.25 * mojado + 0.45 * charco, 0, 0.9)
    plano = 1 - 0.85 * charco                                            # en el charco el agua tapa el relieve
    o_nx *= plano; o_ny *= plano
    o_mask[..., 0] = 0                                                   # sin metal

    Image.fromarray((np.clip(o_col, 0, 1)[::-1] * 255).astype(np.uint8)).resize((1024, 1024)).save(os.path.join(PREV, "tierra_color.jpg"), quality=90)
    vista = np.tile(np.clip(o_col, 0, 1)[::4, ::4], (2, 2, 1))           # 2x2: para ver que no hay costura ni patrón
    Image.fromarray((vista[::-1] * 255).astype(np.uint8)).save(os.path.join(PREV, "tierra_mosaico_2x2.jpg"), quality=88)

    def escribir(img, ruta):
        niveles, cur = [], img
        while True:
            niveles.append(np.clip(np.round(cur * 255), 0, 255).astype(np.uint8))
            if cur.shape[0] == 1: break
            cur = reducir2(cur)
        datos = b"".join(comprimir_nivel(nv, 1) for nv in niveles)
        with open(ruta + ".tmp", "wb") as f:
            f.write(b"MTX1"); f.write(struct.pack("<6i", 1, N, N, 1, len(niveles), len(datos))); f.write(datos)
        os.replace(ruta + ".tmp", ruta)
    escribir(np.dstack([np.clip(o_col, 0, 1), np.ones((N, N), np.float32)]), os.path.join(SAL, "tierra_color.mtex"))
    escribir(np.clip(o_mask, 0, 1), os.path.join(SAL, "tierra_mascara.mtex"))
    nz = np.sqrt(np.clip(1 - o_nx ** 2 - o_ny ** 2, 0, 1))
    escribir_normal(np.dstack([o_nx, o_ny, nz]).astype(np.float32), os.path.join(SAL, "tierra_normal.mtex"))
    print("listo:", SAL, flush=True)

if __name__ == "__main__":
    main()
