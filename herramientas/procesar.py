# © Angel — Pipeline de texturas IA para The Mortuary Assistant
# selección → extracción (UnityPy) → escalado IA (UltraSharp) → corrección por retroproyección
# → mips → compresión BC7 (color) / BC5 (normales) → archivos .mtex + indice.tsv para el plugin
import os, sys, json, hashlib, argparse, time, struct, traceback
from concurrent.futures import ProcessPoolExecutor
import numpy as np
from PIL import Image

BASE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")

# Superficies grandes que se repiten (paredes, suelos, hierba…): hasta 4096
CLAVE_4K = ("WallPaintNew_", "TileFloorBW_", "FlooringMat_", "BlueTile_", "Brick_", "SideWalk_", "Street_",
            "OutsideTrim_", "WoodTrim1_", "Shingles_", "GrassColor02", "Grass_Flat_", "RealGrassPattern", "Cliffv2",
            "TreeBark_", "BFW180123_Subject_FloorBoard_", "BFW180123_Wood_Brown_", "Frames_", "BrushedMetal_",
            "FrontDoorMat_", "BFW180123_Carpet_")
FASE2 = ("Skin_Body", "Skin_Head", "ClothesAndArms", "2_meshes_Merge_Pbr_Diffuse")
PROPS_COLOR = {"_BaseColorMap", "_MainTex", "_BaseMap", "_UnlitColorMap", "_GrassTex", "_GroundTex"}
PROPS_NORMAL = {"_NormalMap", "_BumpMap"}
FORMATO = {"color": 1, "normal": 3}   # 1 = BC7, 3 = BC5 (el plugin elige sRGB/UNorm según el original)


def excluido(t):
    n = t["nombre"]
    if any(s in n for s in ("SDF", "Atlas", "Font", "Emoji")):
        return True
    for _, sh, _ in t["usos"]:
        if sh.startswith(("GUI/", "TextMeshPro", "Sprites/", "UI/", "Hidden/")):
            return True
    return False


def rol_de(t):
    props = {u[0] for u in t["usos"]}
    if not props or excluido(t):
        return None
    if props <= PROPS_NORMAL:
        return "normal"
    if props <= PROPS_COLOR:
        return "color"
    return None


def destino(t, maximo_general):
    w, h = t["ancho"], t["alto"]
    m = max(w, h)
    tope = 4096 if t["nombre"].startswith(CLAVE_4K) else maximo_general
    f = min(4.0, tope / m)
    if f <= 1.0:
        return None
    W = max(4, int(round(w * f / 4)) * 4)
    H = max(4, int(round(h * f / 4)) * 4)
    return W, H


def seleccionar(inv, minimo, maximo_fuente, maximo_general, solo):
    sel = {}
    for clave, t in inv.items():
        r = rol_de(t)
        if r is None:
            continue
        if solo and not any(t["nombre"].startswith(s) for s in solo):
            continue
        m = max(t["ancho"], t["alto"])
        if m < minimo or m > maximo_fuente:
            continue
        d = destino(t, maximo_general)
        if d:
            sel[clave] = (r, d)
    return sel


# ---------- conversión de color ----------
_LUT = ((np.arange(256) / 255.0) ** 2.2).astype(np.float32)   # aproximación sRGB→lineal suficiente para mips

def a_lineal(u8):
    x = u8.astype(np.float32) / 255.0
    return np.where(x <= 0.04045, x / 12.92, ((x + 0.055) / 1.055) ** 2.4).astype(np.float32)

def a_srgb(f):
    f = np.clip(f, 0, 1)
    s = np.where(f <= 0.0031308, f * 12.92, 1.055 * np.power(f, 1 / 2.4) - 0.055)
    return np.clip(np.round(s * 255), 0, 255).astype(np.uint8)


def reducir2(a):
    """Promedio 2x2 (impares: replica borde)."""
    h, w = a.shape[:2]
    if h % 2: a = np.concatenate([a, a[-1:]], 0)
    if w % 2: a = np.concatenate([a, a[:, -1:]], 1)
    return 0.25 * (a[0::2, 0::2] + a[1::2, 0::2] + a[0::2, 1::2] + a[1::2, 1::2])


def redim_f(a, W, H):
    """Redimensiona float HxWxC con Lanczos canal por canal."""
    return np.stack([np.array(Image.fromarray(a[..., c].astype(np.float32), mode="F").resize((W, H), Image.LANCZOS))
                     for c in range(a.shape[2])], -1)


def retroproyectar(up, orig, e):
    """Corrige el resultado IA para que al reducirlo coincida con el original (quita derivas de color)."""
    h, w = orig.shape[:2]
    red = up.astype(np.float32).reshape(h, e, w, e, -1).mean((1, 3))
    res = orig.astype(np.float32) - red
    res_up = redim_f(res, w * e, h * e)
    return np.clip(up.astype(np.float32) + res_up, 0, 255)


# ---------- compresión ----------
def comprimir_nivel(rgba_u8, fmt):
    import etcpak
    h, w = rgba_u8.shape[:2]
    if w < 4 or h < 4:   # niveles diminutos: un bloque 4x4
        rgba_u8 = np.array(Image.fromarray(rgba_u8, "RGBA").resize((max(4, w), max(4, h)), Image.NEAREST))
        h, w = rgba_u8.shape[:2]
    partes = []
    for y in range(0, h, 64):   # por franjas: etcpak falla con imágenes grandes de una vez
        b = np.ascontiguousarray(rgba_u8[y:y + 64])
        if fmt == 1:
            partes.append(etcpak.compress_bc7(b.tobytes(), w, b.shape[0], etcpak.BC7CompressBlockParams()))
        else:
            partes.append(etcpak.compress_bc5(b.tobytes(), w, b.shape[0]))
    return b"".join(partes)


def postproceso(trabajo):
    """Corre en un proceso aparte: retroproyección, redimensión, mips, compresión y escritura."""
    try:
        rol, up, orig, alfa, srgb, W, H, ruta = trabajo
        h, w = orig.shape[:2]
        e = up.shape[0] // h
        up = retroproyectar(up, orig, e)              # float 0..255, (h*e, w*e, 3)
        if rol == "color":
            lin = a_lineal(np.clip(np.round(up), 0, 255).astype(np.uint8)) if srgb else up / 255.0
            if (W, H) != (w * e, h * e):
                lin = np.clip(redim_f(lin, W, H), 0, 1)
            if alfa is not None:
                a = np.array(Image.fromarray(alfa, "L").resize((W, H), Image.LANCZOS)).astype(np.float32) / 255.0
            else:
                a = np.ones((H, W), np.float32)
            niveles = []
            cur, ca = lin, a
            while True:
                rgb8 = a_srgb(cur) if srgb else np.clip(np.round(cur * 255), 0, 255).astype(np.uint8)
                niveles.append(np.dstack([rgb8, np.clip(np.round(ca * 255), 0, 255).astype(np.uint8)]))
                if cur.shape[0] == 1 and cur.shape[1] == 1:
                    break
                cur = reducir2(cur); ca = reducir2(ca[..., None])[..., 0]
        else:
            n = up / 127.5 - 1.0
            if (W, H) != (w * e, h * e):
                n = redim_f(n, W, H)
            def norm(v):
                v = v.copy(); v[..., 2] = np.maximum(v[..., 2], 0.0)
                return v / np.maximum(np.linalg.norm(v, axis=-1, keepdims=True), 1e-6)
            cur = norm(n)
            niveles = []
            while True:
                x8 = np.clip(np.round((cur[..., 0] + 1) * 127.5), 0, 255).astype(np.uint8)
                y8 = np.clip(np.round((cur[..., 1] + 1) * 127.5), 0, 255).astype(np.uint8)
                niveles.append(np.dstack([x8, y8, np.zeros_like(x8), np.full_like(x8, 255)]))
                if cur.shape[0] == 1 and cur.shape[1] == 1:
                    break
                cur = norm(reducir2(cur))
        fmt = FORMATO[rol]
        datos = b"".join(comprimir_nivel(nv, fmt) for nv in niveles)
        if fmt == 1:   # BC7 de etcpak deja alfa 254 en zonas opacas: rompe materiales con recorte a 1,0 (pieles)
            from alfa255 import arreglar_bloques
            datos, _ = arreglar_bloques(datos)
        tmp = ruta + ".tmp"
        with open(tmp, "wb") as f:
            f.write(b"MTX1")
            f.write(struct.pack("<6i", 1, W, H, fmt, len(niveles), len(datos)))
            f.write(datos)
        os.replace(tmp, ruta)
        return ruta, None
    except Exception:
        return trabajo[-1], traceback.format_exc()


def huella(rgba):
    return np.array(Image.fromarray(rgba, "RGBA").resize((16, 16), Image.BOX)).tobytes().hex()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--salida", default=os.path.join(BASE, "..", "texturas_ia"))
    ap.add_argument("--modelo", default=os.path.join(BASE, "modelos_pt", "4x-UltraSharp.safetensors"))
    ap.add_argument("--minimo", type=int, default=64)
    ap.add_argument("--maximo-fuente", type=int, default=1024)
    ap.add_argument("--maximo-general", type=int, default=2048)
    ap.add_argument("--solo", nargs="*", default=None)
    ap.add_argument("--procesos", type=int, default=3)
    ap.add_argument("--contar", action="store_true")
    ap.add_argument("--fase2", action="store_true", help="piel de cadáveres y brazos del jugador: color 2048 -> 4096")
    ap.add_argument("--fase3", action="store_true", help="el resto de texturas de 2048 (color y relieve): 2048 -> 4096")
    a = ap.parse_args()

    inv = json.load(open(os.path.join(BASE, "inventario.json")))["texturas"]
    if a.fase2:
        sel = {}
        for clave, t in inv.items():
            if rol_de(t) == "color" and max(t["ancho"], t["alto"]) == 2048 and any(k in t["nombre"] for k in FASE2):
                sel[clave] = ("color", (t["ancho"] * 2, t["alto"] * 2))
    elif a.fase3:
        sel = {}
        for clave, t in inv.items():
            r = rol_de(t)
            if r and max(t["ancho"], t["alto"]) == 2048 and not any(k in t["nombre"] for k in FASE2) \
                    and "Hair" not in t["nombre"] and (not a.solo or any(t["nombre"].startswith(x) for x in a.solo)):
                sel[clave] = (r, (t["ancho"] * 2, t["alto"] * 2))
    else:
        sel = seleccionar(inv, a.minimo, a.maximo_fuente, a.maximo_general, a.solo)
    os.makedirs(a.salida, exist_ok=True)
    vram = sum(W * H * 4 / 3 for (_, (W, H)) in sel.values()) / 2**20
    print(f"seleccionadas: {len(sel)} | VRAM si todas estuvieran cargadas a la vez: {vram:.0f} MB", flush=True)
    if a.contar:
        import collections
        print(collections.Counter((r, f"{inv[k]['ancho']}->{d[0]}") for k, (r, d) in sel.items()).most_common())
        return

    import UnityPy
    import torch
    from escalador import Escalador
    esc = Escalador(a.modelo, mosaico=256)
    env = UnityPy.load(DATA)
    manifiesto = open(os.path.join(a.salida, "manifiesto.jsonl"), "a")
    hechos = set()
    pool = ProcessPoolExecutor(a.procesos)
    pendientes = []
    t0 = time.time(); n = 0
    for obj in env.objects:
        if obj.type.name != "Texture2D":
            continue
        clave = f"{obj.assets_file.name}:{obj.path_id}"
        if clave not in sel:
            continue
        rol, (W, H) = sel[clave]
        t = obj.read()
        # UnityPy voltea la imagen para verla derecha; Unity guarda las filas de abajo hacia arriba.
        # Se vuelve al orden nativo para que LoadRawTextureData y la huella coincidan con el juego.
        rgba = np.ascontiguousarray(np.array(t.image.convert("RGBA"))[::-1])
        sha = hashlib.sha1(rgba.tobytes()).hexdigest()[:20]
        archivo = f"{sha}_{rol}_{W}x{H}.mtex"
        ruta = os.path.join(a.salida, archivo)
        srgb = inv[clave]["espacio"] == 1
        entrada = {"nombre": t.m_Name, "ancho": t.m_Width, "alto": t.m_Height, "rol": rol, "archivo": archivo,
                   "W": W, "H": H, "srgb": srgb, "huella": huella(rgba), "clave": clave}
        manifiesto.write(json.dumps(entrada) + "\n"); manifiesto.flush()
        if archivo in hechos or os.path.exists(ruta):
            continue
        hechos.add(archivo)
        if rol == "color":
            rgb = np.ascontiguousarray(rgba[..., :3])
            alfa = rgba[..., 3] if rgba[..., 3].min() < 250 else None
        else:
            x = rgba[..., 3].astype(np.float32) / 127.5 - 1
            y = rgba[..., 1].astype(np.float32) / 127.5 - 1
            z = np.sqrt(np.clip(1 - x * x - y * y, 0, 1))
            rgb = np.clip(np.round((np.dstack([x, y, z]) + 1) * 127.5), 0, 255).astype(np.uint8)
            alfa = None
        # si el destino es exactamente 2x, supermuestreo en GPU (4x y promedio 2x2)
        red = 2 if (W == rgb.shape[1] * 2 and H == rgb.shape[0] * 2) else 1
        up = esc.escalar_rgb(rgb, reducir=red)
        pendientes.append(pool.submit(postproceso, (rol, up, rgb, alfa, srgb, W, H, ruta)))
        n += 1
        print(f"[{n}] {t.m_Name} {t.m_Width}x{t.m_Height} -> {W}x{H} {rol} ({time.time()-t0:.0f}s)", flush=True)
        # limitar memoria: no acumular más de 2x procesos en cola
        while len([p for p in pendientes if not p.done()]) > a.procesos * 2:
            time.sleep(0.5)
        for p in [p for p in pendientes if p.done()]:
            r, err = p.result()
            if err: print("ERROR", r, err, flush=True)
            pendientes.remove(p)
    for p in pendientes:
        r, err = p.result()
        if err: print("ERROR", r, err, flush=True)
    pool.shutdown()
    manifiesto.close()
    print(f"fin: {n} texturas nuevas en {time.time()-t0:.0f}s", flush=True)


if __name__ == "__main__":
    main()
