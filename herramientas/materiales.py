# © Angel — Inventario de materiales HDRP/Lit (solo lectura): ranuras, valores y palabras clave
import UnityPy, os, sys, json
DATA=os.path.expanduser("~/.local/share/Steam/steamapps/common/The Mortuary Assistant/The Mortuary Assistant_Data")
env=UnityPy.load(DATA)
texnom={}
for obj in env.objects:
    if obj.type.name=="Texture2D":
        try: texnom[(obj.assets_file.name,obj.path_id)]=obj.read().m_Name
        except Exception: pass
out=[]
for obj in env.objects:
    if obj.type.name!="Material": continue
    try: m=obj.read()
    except Exception: continue
    try: shn=m.m_Shader.deref().read().m_ParsedForm.m_Name
    except Exception: shn="?"
    tx={}
    for k,v in m.m_SavedProperties.m_TexEnvs:
        if v.m_Texture.m_PathID:
            try:
                o=v.m_Texture.deref(); tx[k]=[texnom.get((o.assets_file.name,o.path_id),"?"), [v.m_Scale.x,v.m_Scale.y]]
            except Exception: tx[k]=["?",None]
    fl={k:v for k,v in m.m_SavedProperties.m_Floats}
    co={k:[v.r,v.g,v.b,v.a] for k,v in m.m_SavedProperties.m_Colors}
    kw=getattr(m,"m_ValidKeywords",None) or getattr(m,"m_ShaderKeywords","")
    out.append({"nombre":m.m_Name,"shader":shn,"archivo":obj.assets_file.name,"tex":tx,"floats":fl,"colores":co,"kw":kw if isinstance(kw,str) else " ".join(kw)})
json.dump(out,open(sys.argv[1],"w"),ensure_ascii=False)
print(len(out))
