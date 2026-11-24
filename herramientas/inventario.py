# © Angel — Inventario de texturas y materiales de The Mortuary Assistant (solo lectura)
import json, os, sys, collections
import UnityPy

DATA = os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
SALIDA = sys.argv[1] if len(sys.argv) > 1 else "inventario.json"

env = UnityPy.load(DATA)

texturas = {}   # clave (archivo, path_id) -> datos
for obj in env.objects:
    if obj.type.name != "Texture2D":
        continue
    try:
        t = obj.read()
        clave = f"{obj.assets_file.name}:{obj.path_id}"
        texturas[clave] = {
            "nombre": t.m_Name,
            "ancho": t.m_Width,
            "alto": t.m_Height,
            "formato": t.m_TextureFormat.name if hasattr(t.m_TextureFormat, "name") else str(t.m_TextureFormat),
            "mips": getattr(t, "m_MipCount", 1),
            "espacio": getattr(t, "m_ColorSpace", None),
            "legible": getattr(t, "m_IsReadable", None),
            "lightmap": getattr(t, "m_LightmapFormat", None),
            "archivo": obj.assets_file.name,
            "path_id": obj.path_id,
            "usos": [],
        }
    except Exception as e:
        print("error textura", obj.path_id, e, file=sys.stderr)

def clave_pptr(pptr):
    try:
        o = pptr.deref()
        return f"{o.assets_file.name}:{o.path_id}"
    except Exception:
        return None

materiales = 0
shaders = collections.Counter()
for obj in env.objects:
    if obj.type.name != "Material":
        continue
    try:
        m = obj.read()
        materiales += 1
        nombre_shader = "?"
        try:
            sh = m.m_Shader.deref().read()
            nombre_shader = sh.m_ParsedForm.m_Name if hasattr(sh, "m_ParsedForm") and sh.m_ParsedForm else getattr(sh, "m_Name", "?")
        except Exception:
            pass
        shaders[nombre_shader] += 1
        for nombre_prop, env_tex in m.m_SavedProperties.m_TexEnvs:
            if env_tex.m_Texture.m_PathID == 0:
                continue
            k = clave_pptr(env_tex.m_Texture)
            if k and k in texturas:
                texturas[k]["usos"].append([nombre_prop, nombre_shader, m.m_Name])
    except Exception as e:
        print("error material", obj.path_id, e, file=sys.stderr)

json.dump({"texturas": texturas, "materiales": materiales, "shaders": shaders.most_common(40)},
          open(SALIDA, "w"), ensure_ascii=False, indent=1)
print("texturas:", len(texturas), "materiales:", materiales)
