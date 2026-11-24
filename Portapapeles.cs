// © Angel — Luz suave que ilumina el portapapeles mientras el jugador lo sostiene (en el juego no se lee a oscuras)
using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace MortuaryRealista
{
    internal static class Portapapeles
    {
        internal static ConfigEntry<bool> Activar;
        internal static ConfigEntry<float> Lumenes;
        internal static ConfigEntry<bool> AplanarPapel;
        internal static ConfigEntry<int> Modo;
        internal static ConfigEntry<bool> TextoNegro;
        internal static ConfigEntry<float> Separacion;
        internal static ConfigEntry<bool> SmaaAlLeer;
        static HDAdditionalCameraData camAA; static HDAdditionalCameraData.AntialiasingMode aaAntes; static bool aaCambiado;
        static readonly System.Collections.Generic.Dictionary<IntPtr, (Transform t, Vector3 pos)> laminas = new System.Collections.Generic.Dictionary<IntPtr, (Transform, Vector3)>();
        internal static ConfigEntry<float> EscalaBrillo;
        static readonly System.Collections.Generic.List<Material> papeles = new System.Collections.Generic.List<Material>();
        static readonly System.Collections.Generic.Dictionary<int, (Material m, float n)> relieveOriginal = new System.Collections.Generic.Dictionary<int, (Material, float)>();
        static GameObject cbRevisado;
        static readonly System.Collections.Generic.List<(TMPro.TMP_Text t, Color c)> textos = new System.Collections.Generic.List<(TMPro.TMP_Text, Color)>();
        static bool oscurecido;

        static PlayerInteraction jugador;
        static GameObject luzGo;
        static HDAdditionalLightData hd;
        static float proximaBusqueda;
        static bool ultimoVisible;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("Portapapeles", "LuzPortapapeles", true, "Luz pequeña que ilumina el portapapeles mientras lo sostienes");
            AplanarPapel = cfg.Bind("Portapapeles", "AplanarPapel", true, "Quita el relieve de las letras del papel mientras lo sostienes (evita que se vean claras, como grabadas)");
            TextoNegro = cfg.Bind("Portapapeles", "TextoNegro", true, "Pinta casi negro el texto del portapapeles mientras lo sostienes: el texto no recibe sombras, y con gris se veía más claro que el papel sombreado");
            Separacion = cfg.Bind("Portapapeles", "SeparacionTexto", 0.05f, "Metros que se adelanta el texto del portapapeles hacia la cámara: la hoja es curva y tapaba las letras");
            SmaaAlLeer = cfg.Bind("Portapapeles", "SMAAAlLeer", true, "Mientras sostienes el portapapeles usa SMAA en vez de TAA: TAA borra el texto al mover la cámara");
            Modo = cfg.Bind("Portapapeles", "Modo", 1, "0 = luz pequeña frente a la cámara, 1 = el papel brilla un poco (no marca el relieve ni deslava el texto)");
            EscalaBrillo = cfg.Bind("Portapapeles", "EscalaBrillo", 0.05f, "Multiplicador interno del brillo del papel (modo 1)");
            Lumenes = cfg.Bind("Portapapeles", "IntensidadLumenes", 0.8f, "Intensidad de esa luz en lúmenes");
        }

        public static void Tick()
        {
            try
            {
                if (jugador == null && Time.unscaledTime >= proximaBusqueda)
                {
                    proximaBusqueda = Time.unscaledTime + 2f;
                    jugador = UnityEngine.Object.FindObjectOfType<PlayerInteraction>();
                }
                var cam = Camera.main;
                GameObject cb = jugador != null ? jugador.clipboard : null;
                bool enMano = cb != null && cb.activeInHierarchy && cam != null;
                bool visible = Activar.Value && enMano;
                if (visible != ultimoVisible) { ultimoVisible = visible; Plugin.Log.LogInfo("Portapapeles: luz " + (visible ? "encendida" : "apagada")); }
                if (cb != null && cb != cbRevisado) { cbRevisado = cb; Revisar(cb); }
                Aplanar(visible && AplanarPapel.Value);
                Oscurecer(enMano && TextoNegro.Value);
                Separar(enMano ? Separacion.Value : 0f, cam);
                CambiarAA(enMano && SmaaAlLeer.Value, cam);
                Brillar(visible && Modo.Value == 1);
                if (!visible || Modo.Value == 1) { if (luzGo != null) luzGo.SetActive(false); return; }
                if (luzGo == null) Crear();
                luzGo.SetActive(true);
                hd.SetIntensity(Lumenes.Value, LightUnit.Lumen);
                // apunta al centro visible del portapapeles desde un poco arriba y a la derecha de la cámara
                Vector3 centro = cb.transform.position;
                var r = cb.GetComponentInChildren<Renderer>();
                if (r != null) centro = r.bounds.center;
                var ct = cam.transform;
                luzGo.transform.position = ct.position + ct.up * 0.03f - ct.forward * 0.1f;
                luzGo.transform.LookAt(centro);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Portapapeles: " + e.Message); jugador = null; proximaBusqueda = Time.unscaledTime + 5f; }
        }

        // registra los materiales del portapapeles (para diagnóstico) y guarda su relieve original
        static void Revisar(GameObject cb)
        {
            foreach (var r in cb.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string info = r.name + " / " + m.name + " (" + (m.shader != null ? m.shader.name : "-") + ")";
                    if (m.HasProperty("_NormalScale")) info += " relieve=" + m.GetFloat("_NormalScale").ToString("0.00");
                    if (m.HasProperty("_NormalMap") && m.GetTexture("_NormalMap") != null) info += " normal=" + m.GetTexture("_NormalMap").name;
                    if (m.HasProperty("_Smoothness")) info += " suavidad=" + m.GetFloat("_Smoothness").ToString("0.00");
                    Plugin.Log.LogInfo("Portapapeles: " + info);
                    if (m.HasProperty("_NormalScale") && !relieveOriginal.ContainsKey(m.GetInstanceID()))
                        relieveOriginal[m.GetInstanceID()] = (m, m.GetFloat("_NormalScale"));
                }
        }

        // el texto del portapapeles es interfaz (no recibe luz): con el papel iluminado se ve deslavado → se oscurece y se vuelve opaco
        static void Oscurecer(bool on)
        {
            if (on == oscurecido) return;
            oscurecido = on;
            if (on)
            {
                textos.Clear();
                if (cbRevisado != null)
                    foreach (var t in cbRevisado.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (t != null) textos.Add((t, t.color));
                foreach (var m in UnityEngine.Object.FindObjectsOfType<ClipBoardManager>())
                    foreach (var t in m.GetComponentsInChildren<TMPro.TMP_Text>(true)) if (t != null && !textos.Exists(x => x.t == t)) textos.Add((t, t.color));
            }
            foreach (var (t, c) in textos)
            {
                if (t == null) continue;
                t.color = on ? new Color(0.035f, 0.03f, 0.025f, 1f) : c;
            }
            Plugin.Log.LogInfo("Portapapeles: " + textos.Count + " textos " + (on ? "oscurecidos" : "restaurados"));
        }

        // modo 1: emisión en el papel, multiplicada por su propio color (lo impreso sigue oscuro)
        static float ultimoBrillo = -1f;
        static void Brillar(bool on)
        {
            float v = on ? Lumenes.Value * EscalaBrillo.Value : 0f;
            if (Math.Abs(v - ultimoBrillo) < 1e-5f) return;
            ultimoBrillo = v;
            if (papeles.Count == 0 && cbRevisado != null)
                foreach (var r in cbRevisado.GetComponentsInChildren<Renderer>(true))
                {
                    var sm = r.sharedMaterials; bool tiene = false;
                    foreach (var m in sm) if (m != null && m.name.IndexOf("Sheet", StringComparison.OrdinalIgnoreCase) >= 0) tiene = true;
                    if (!tiene) continue;
                    foreach (var m in r.materials)   // copia propia: no hace brillar otras hojas del mundo
                        if (m != null && m.name.IndexOf("Sheet", StringComparison.OrdinalIgnoreCase) >= 0) papeles.Add(m);
                }
            var col = new Color(1f, 0.94f, 0.84f) * v;
            foreach (var m in papeles)
            {
                if (m == null) continue;
                if (m.HasProperty("_AlbedoAffectEmissive")) m.SetFloat("_AlbedoAffectEmissive", 1f);
                if (m.HasProperty("_EmissiveExposureWeight")) m.SetFloat("_EmissiveExposureWeight", 1f);
                m.SetColor("_EmissiveColor", col);
                m.SetColor("_EmissiveColorLDR", col);
            }
            Plugin.Log.LogInfo($"Portapapeles: brillo del papel {v:0.###} en {papeles.Count} materiales");
        }

        // acerca las láminas (Canvas) del texto a la cámara por la línea de vista y las achica en la misma proporción:
        // en pantalla quedan idénticas, pero por delante de la hoja curva que tapaba las letras
        static readonly System.Collections.Generic.Dictionary<IntPtr, Vector3> escalas = new System.Collections.Generic.Dictionary<IntPtr, Vector3>();
        static void Separar(float sep, Camera cam)
        {
            if (sep > 0f && laminas.Count == 0)
                foreach (var (t, _) in textos)
                {
                    if (t == null || t.canvas == null) continue;
                    var tr = t.canvas.transform;
                    if (!laminas.ContainsKey(tr.Pointer)) { laminas[tr.Pointer] = (tr, tr.localPosition); escalas[tr.Pointer] = tr.localScale; }
                }
            foreach (var kv in laminas)
            {
                var (tr, pos) = kv.Value;
                if (tr == null) continue;
                var esc = escalas[kv.Key];
                if (sep <= 0f || cam == null || tr.parent == null) { tr.localPosition = pos; tr.localScale = esc; continue; }
                Vector3 ojo = cam.transform.position, orig = tr.parent.TransformPoint(pos);
                float dist = Vector3.Distance(ojo, orig);
                float k = Mathf.Clamp(1f - sep / Mathf.Max(dist, 0.05f), 0.3f, 1f);
                tr.localPosition = tr.parent.InverseTransformPoint(ojo + (orig - ojo) * k);
                tr.localScale = esc * k;
            }
            if (sep <= 0f) { laminas.Clear(); escalas.Clear(); }
        }

        static void CambiarAA(bool on, Camera cam)
        {
            if (on)
            {
                var hd = cam != null ? cam.GetComponent<HDAdditionalCameraData>() : null;
                if (hd == null) return;
                if (!aaCambiado || hd != camAA) { camAA = hd; aaAntes = hd.antialiasing; aaCambiado = true; Plugin.Log.LogInfo("Portapapeles: SMAA mientras se lee (antes " + aaAntes + ")"); }
                if (aaAntes == HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing && hd.antialiasing != HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing)
                    hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            }
            else if (aaCambiado)
            {
                aaCambiado = false;
                if (camAA != null && camAA.antialiasing == HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing) camAA.antialiasing = aaAntes;
                Plugin.Log.LogInfo("Portapapeles: anti-aliasing restaurado (" + aaAntes + ")");
            }
        }

        static bool aplanado;
        static void Aplanar(bool on)
        {
            if (on == aplanado) return;
            aplanado = on;
            foreach (var kv in relieveOriginal)
            {
                var (m, n) = kv.Value;
                if (m == null) continue;
                // solo el papel: las piezas de madera y metal conservan su relieve
                bool papel = m.name.IndexOf("paper", StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("page", StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("sheet", StringComparison.OrdinalIgnoreCase) >= 0 || m.name.IndexOf("clip", StringComparison.OrdinalIgnoreCase) >= 0 && m.name.IndexOf("board", StringComparison.OrdinalIgnoreCase) < 0;
                if (!papel) continue;
                m.SetFloat("_NormalScale", on ? Math.Min(n, 0.15f) : n);
            }
            Plugin.Log.LogInfo("Portapapeles: relieve del papel " + (on ? "aplanado" : "original"));
        }

        static void Crear()
        {
            luzGo = new GameObject("LuzPortapapelesRealista");
            UnityEngine.Object.DontDestroyOnLoad(luzGo);
            var l = luzGo.AddComponent<Light>();
            l.type = LightType.Spot;
            hd = luzGo.AddComponent<HDAdditionalLightData>();
            hd.SetIntensity(Lumenes.Value, LightUnit.Lumen);
            hd.SetRange(1.3f);          // alcanza el portapapeles y la mano, casi nada del cuarto
            hd.SetSpotAngle(70f);
            hd.EnableShadows(false);
            hd.volumetricDimmer = 0f;   // sin haz visible en la niebla
            hd.affectSpecular = false;  // sin reflejo blanco que tape el texto
            hd.color = new Color(1f, 0.95f, 0.86f);
        }
    }
}
