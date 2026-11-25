// © Angel — Herramientas de prueba: capturas, botón automático, cámara de prueba con A/B y prueba de rendimiento
using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal static class Pruebas
    {
        internal static ConfigEntry<Key> TeclaCaptura;
        internal static ConfigEntry<float> CapturaAutoSegundos, SegundosPorEstado;
        internal static ConfigEntry<string> BotonAuto, EscenaCaptura, VistasPrueba;
        internal static ConfigEntry<bool> AbrirMenu;
        internal static ConfigEntry<bool> ListarBotones, SecuenciaAB, PruebaRendimiento, EncenderLuces, MoverJugador, DiagnosticoEscena;
        internal static ConfigEntry<string> ModuloAB;

        static int firmaEscena = int.MinValue;
        static float momentoCaptura = -1f, momentoBoton = -1f;
        static bool botonPulsado;
        static readonly HashSet<string> botonesVistos = new HashSet<string>();
        static string escenaActual = "";

        // secuencia A/B
        static readonly List<string> vistas = new List<string>();
        static int vistaIdx = -1, paso = -1, cuadroPaso;
        static Camera camPrueba, camPrincipal;
        // prueba de rendimiento
        static int estadoRend = -1; static float finEstado; static int cuadrosR; static float tiempoR;
        static readonly string[] nombresEstado = { "todo activo", "sin realismo", "sin texturas IA", "sin post-proceso", "todo apagado", "todo activo (otra vez)" };

        public static void Configurar(ConfigFile cfg)
        {
            ComandosEnVivo = cfg.Bind("Pruebas", "ComandosEnVivo", false, "Lee órdenes de Capturas/comandos.txt (solo para desarrollo)");
            TeclaCaptura = cfg.Bind("Pruebas", "TeclaCaptura", Key.F10, "Guarda una captura del juego en la carpeta Capturas del juego");
            CapturaAutoSegundos = cfg.Bind("Pruebas", "CapturaAutoSegundos", 0f, "Si es > 0, captura sola N segundos después de cada cambio de escena");
            EscenaCaptura = cfg.Bind("Pruebas", "EscenaCaptura", "", "Si no está vacío, lo automático solo corre en escenas cuyo nombre contenga este texto");
            BotonAuto = cfg.Bind("Pruebas", "BotonAuto", "", "Texto exacto de un botón del menú para pulsarlo solo (vacío = desactivado)");
            ListarBotones = cfg.Bind("Pruebas", "ListarBotones", false, "Escribe en el log los textos de los botones visibles");
            SecuenciaAB = cfg.Bind("Pruebas", "SecuenciaAB", false, "En la captura automática, saca pares con/sin mejoras desde la cámara del jugador y desde VistasPrueba");
            VistasPrueba = cfg.Bind("Pruebas", "VistasPrueba", "", "Materiales a encuadrar con una cámara de prueba, separados por coma (ej. SlabCartProps,BathroomSet)");
            PruebaRendimiento = cfg.Bind("Pruebas", "PruebaRendimiento", false, "Tras la secuencia A/B, mide FPS apagando cada parte por turnos");
            ModuloAB = cfg.Bind("Pruebas", "ModuloAB", "todo", "Qué se apaga en la foto B: todo = el mod entero; materiales = solo las mejoras de materiales");
            EncenderLuces = cfg.Bind("Pruebas", "EncenderLuces", false, "Antes de la secuencia A/B, acciona todos los interruptores de luz del juego");
            MoverJugador = cfg.Bind("Pruebas", "MoverJugador", false, "En las vistas de prueba, mueve al jugador (en vez de crear una cámara aparte)");
            AbrirMenu = cfg.Bind("Pruebas", "AbrirMenu", false, "Al terminar la secuencia A/B abre el menú de opciones y lo captura");
            DiagnosticoEscena = cfg.Bind("Pruebas", "DiagnosticoEscena", false, "Al iniciar la secuencia, lista cuerpos visibles, interruptores y jugador");
            LuzPruebaLumenes = cfg.Bind("Pruebas", "LuzPruebaLumenes", 40f, "Luz de foco que acompaña a las vistas cuerpoN (0 = sin luz)");
            SegundosPorEstado = cfg.Bind("Pruebas", "SegundosPorEstado", 20f, "Duración de cada estado en la prueba de rendimiento");
        }

        // ---------- comandos en vivo: Capturas/comandos.txt ----------
        // líneas: "Seccion.Clave=valor" | "esperar 1.5" | "captura nombre" (se ejecutan en orden y el archivo se borra)
        static readonly Queue<string> comandos = new Queue<string>();
        static float proximoComando, revisarArchivo;
        static bool prestado;
        static readonly Dictionary<int, (Color disp, float escala)> perfilOriginal = new Dictionary<int, (Color, float)>();
        static readonly List<(SkinnedMeshRenderer r, int i, Material m)> peloOriginal = new List<(SkinnedMeshRenderer, int, Material)>();
        static readonly List<GameObject> encendidos = new List<GameObject>();
        static readonly Dictionary<int, float> propOriginal = new Dictionary<int, float>();
        static readonly List<Light> lucesApagadas = new List<Light>();
        static readonly List<CustomPassVolume> pasesApagados = new List<CustomPassVolume>();
        static float giroPorSeg, giroHasta; static MFPP.Player giroJugador;
        internal static ConfigEntry<bool> ComandosEnVivo;
        static void Comandos()
        {
            if (ComandosEnVivo == null || !ComandosEnVivo.Value) return;
            string ruta = Path.Combine(Paths.GameRootPath, "Capturas", "comandos.txt");
            if (Time.unscaledTime >= revisarArchivo)
            {
                revisarArchivo = Time.unscaledTime + 0.5f;
                if (File.Exists(ruta))
                {
                    foreach (var l in File.ReadAllLines(ruta)) if (!string.IsNullOrWhiteSpace(l)) comandos.Enqueue(l.Trim());
                    File.Delete(ruta);
                }
            }
            if (comandos.Count == 0 || Time.unscaledTime < proximoComando) return;
            string c = comandos.Dequeue();
            try
            {
                if (c.StartsWith("esperar ")) { proximoComando = Time.unscaledTime + float.Parse(c.Substring(8), System.Globalization.CultureInfo.InvariantCulture); return; }
                if (c.StartsWith("preset ")) { Opciones.Preset(c.Substring(7).Trim()); proximoComando = Time.unscaledTime + 0.5f; return; }
                if (c.StartsWith("girar "))
                {
                    // girar GRADOS SEGUNDOS: gira al jugador mientras la cola sigue (para probar el movimiento)
                    var p2 = c.Split(' ');
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    giroPorSeg = float.Parse(p2[1], ci) / float.Parse(p2[2], ci); giroHasta = Time.unscaledTime + float.Parse(p2[2], ci);
                    giroJugador = UnityEngine.Object.FindObjectOfType<MFPP.Player>();
                    Plugin.Log.LogInfo("Comando: " + c + " mirada=" + (giroJugador != null ? giroJugador.TargetLookAngles.ToString() : "-"));
                    return;
                }
                if (c.StartsWith("portapapeles"))
                {
                    var pi = UnityEngine.Object.FindObjectOfType<PlayerInteraction>();
                    var inv = UnityEngine.Object.FindObjectOfType<InventoryManager>();
                    if (pi == null) { Plugin.Log.LogWarning("Comando: no hay jugador"); return; }
                    if (c == "portapapeles_estado") { }
                    else if (c == "portapapeles_textos")
                    {
                        var cb = pi.clipboard; var r0 = cb != null ? cb.GetComponentInChildren<Renderer>() : null;
                        Vector3 centro = r0 != null ? r0.bounds.center : (cb != null ? cb.transform.position : Vector3.zero);
                        int n = 0;
                        foreach (var t in Resources.FindObjectsOfTypeAll<TMPro.TMP_Text>())
                        {
                            if (t == null || !t.gameObject.scene.IsValid()) continue;
                            float dist = Vector3.Distance(t.transform.position, centro);
                            if (dist > 1.5f) continue;
                            string camino = t.name; var tr = t.transform.parent; int k = 0;
                            while (tr != null && k++ < 8) { camino = tr.name + "/" + camino; tr = tr.parent; }
                            var cv = t.canvas;
                            Plugin.Log.LogInfo($"  texto d={dist:0.00} activo={t.gameObject.activeInHierarchy} tipo={t.GetIl2CppType().Name} col={t.color} mat={(t.fontSharedMaterial != null ? t.fontSharedMaterial.name + "/" + t.fontSharedMaterial.shader.name : "-")} canvas={(cv != null ? cv.name + ":" + cv.renderMode : "-")} '{(t.text ?? "").Replace("\n", " ").Substring(0, Math.Min(30, (t.text ?? "").Length))}' {ruta}");
                            n++;
                        }
                        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                        {
                            if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy) continue;
                            if (Vector3.Distance(r.bounds.center, centro) > 0.6f) continue;
                            string mats = ""; foreach (var m in r.sharedMaterials) if (m != null) mats += m.name + "(" + m.shader.name + ",q" + m.renderQueue + ") ";
                            string camino = r.name; var tr = r.transform.parent; int k = 0;
                            while (tr != null && k++ < 6) { camino = tr.name + "/" + camino; tr = tr.parent; }
                            Plugin.Log.LogInfo($"  render {r.GetIl2CppType().Name} {camino}: {mats}");
                        }
                        Plugin.Log.LogInfo("Comando: " + n + " textos cerca del portapapeles");
                    }
                    else if (c == "portapapeles_dar") { if (inv != null && !inv.hasClipboard) { inv.hasClipboard = true; prestado = true; } }
                    else if (c == "portapapeles_quitar") { if (inv != null && prestado) { inv.hasClipboard = false; prestado = false; } }
                    else if (c == "portapapeles_cerrar") pi.CloseClipboard();
                    else if (c == "portapapeles_tomado") pi.ToggleClipboard(true);
                    else pi.ToggleClipboard(false);
                    Plugin.Log.LogInfo("Comando: " + c + " (tiene=" + (inv != null && inv.hasClipboard) + ", en mano=" + (pi.clipboard != null && pi.clipboard.activeInHierarchy) + ", bloqueo=" + pi.clipboardAnimlock + ")");
                    proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c == "botones")
                {
                    foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
                    {
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        var tmp = b.GetComponentInChildren<TMPro.TMP_Text>();
                        Plugin.Log.LogInfo($"  botón '{(tmp != null ? tmp.text.Trim() : "-")}' ({b.name})");
                    }
                    proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("boton "))
                {
                    string objetivo = c.Substring(6).Trim();
                    foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
                    {
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        var tmp = b.GetComponentInChildren<TMPro.TMP_Text>();
                        if (objetivo.StartsWith("#") ? b.name == objetivo.Substring(1) : tmp != null && string.Equals(tmp.text.Trim(), objetivo, StringComparison.OrdinalIgnoreCase)) { b.onClick.Invoke(); Plugin.Log.LogInfo("Comando: botón " + objetivo); break; }
                    }
                    proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("luz_off ") || c.StartsWith("luz_on "))
                {
                    // luz_off NOMBRE / luz_on NOMBRE: apaga o enciende las luces activas con ese nombre (o cuyo padre se llama así)
                    bool on = c.StartsWith("luz_on "); string nom = c.Substring(on ? 7 : 8).Trim(); int n = 0;
                    foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                    {
                        if (l == null || !l.gameObject.scene.IsValid()) continue;
                        bool coincide = l.name == nom || (l.transform.parent != null && l.transform.parent.name == nom);
                        if (!coincide) continue;
                        if (!on && l.enabled && l.gameObject.activeInHierarchy) { l.enabled = false; lucesApagadas.Add(l); n++; }
                        if (on && lucesApagadas.Contains(l)) { l.enabled = true; lucesApagadas.Remove(l); n++; }
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + " luces)"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("luz_prop "))
                {
                    // luz_prop NOMBRE clave=valor: sombras=0/1, normalBias, slopeBias, res, mult (multiplica la intensidad), info
                    var pz = c.Substring(9).Trim().Split(' '); var ci = System.Globalization.CultureInfo.InvariantCulture;
                    foreach (var hd in Resources.FindObjectsOfTypeAll<HDAdditionalLightData>())
                    {
                        if (hd == null || !hd.gameObject.scene.IsValid() || hd.name != pz[0]) continue;
                        var l = hd.GetComponent<Light>();
                        if (pz.Length > 1 && pz[1] != "info")
                        {
                            var kv = pz[1].Split('='); float v = float.Parse(kv[1], ci);
                            if (kv[0] == "sombras") hd.EnableShadows(v > 0.5f);
                            else if (kv[0] == "normalBias") hd.normalBias = v;
                            else if (kv[0] == "slopeBias") hd.slopeBias = v;
                            else if (kv[0] == "res") { hd.SetShadowResolutionOverride(true); hd.SetShadowResolution((int)v); }
                            else if (kv[0] == "mult") hd.intensity = hd.intensity * v;
                            else if (kv[0] == "radio") hd.shapeRadius = v;
                        }
                        Plugin.Log.LogInfo($"  luz_prop {hd.name}: sombras={(l != null ? l.shadows.ToString() : "-")} int={hd.intensity:0.###} unidad={hd.lightUnit} normalBias={hd.normalBias:0.###} slopeBias={hd.slopeBias:0.###} res={hd.shadowResolution.@override} radio={hd.shapeRadius:0.###} ángulo={(l != null ? l.spotAngle : 0):0} alcance={(l != null ? l.range : 0):0.0} contacto={hd.useContactShadow.@override}/{hd.useContactShadow.useOverride} filtro={hd.shadowUpdateMode} pos={hd.transform.position}");
                    }
                    proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("perfil_piel"))
                {
                    // perfil_piel info | perfil_piel disp=x2 (multiplica la distancia de dispersión del original) | perfil_piel escala=x2
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    foreach (var d in Resources.FindObjectsOfTypeAll<DiffusionProfileSettings>())
                    {
                        if (d == null || d.profile == null) continue;
                        var pr = d.profile; int id = d.GetInstanceID();
                        if (!perfilOriginal.ContainsKey(id)) perfilOriginal[id] = (pr.scatteringDistance, pr.worldScale);
                        var o = perfilOriginal[id];
                        if (c.Contains("disp=x")) { float f = float.Parse(c.Substring(c.IndexOf("disp=x") + 6).Split(' ')[0], ci); pr.scatteringDistance = new Color(o.disp.r * f, o.disp.g * f, o.disp.b * f, o.disp.a); }
                        if (c.Contains("escala=x")) { float f = float.Parse(c.Substring(c.IndexOf("escala=x") + 8).Split(' ')[0], ci); pr.worldScale = o.escala * f; }
                        if (c != "perfil_piel info") { pr.Validate(); pr.UpdateKernel(); d.UpdateCache(); }
                        Plugin.Log.LogInfo($"  perfil {d.name}: dispersión={pr.scatteringDistance} escala={pr.worldScale:0.###} tinte={pr.transmissionTint} radioFiltro={pr.filterRadius:0.##} hash={pr.hash}");
                    }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("pelo_hdrp "))
                {
                    // pelo_hdrp on|off: pasa el pelo HDRP/Lit al shader de pelo del juego (HDRP/Hair), clonando un material de pelo ya configurado
                    bool on = c.EndsWith(" on"); int n = 0;
                    if (on)
                    {
                        Material refPelo = null;
                        foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                            if (m != null && m.shader != null && m.shader.name == "HDRP/Hair" && m.name.Contains("Merge_Pbr")) { refPelo = m; break; }
                        if (refPelo == null) { Plugin.Log.LogWarning("pelo_hdrp: no hay material de referencia HDRP/Hair"); return; }
                        foreach (var r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
                        {
                            if (r == null || !r.gameObject.scene.IsValid()) continue;
                            var mats = r.sharedMaterials; bool cambio = false;
                            for (int i = 0; i < mats.Length; i++)
                            {
                                var m = mats[i];
                                if (m == null || m.shader == null || m.shader.name != "HDRP/Lit") continue;
                                string nm = m.name;
                                if (!(nm.Contains("Hair") || nm.Contains("HAIR")) || nm.Contains("Scalp") || nm.Contains("Cap")) continue;
                                var t = m.HasProperty("_BaseColorMap") ? m.GetTexture("_BaseColorMap") : null;
                                if (t == null) continue;
                                var nuevo = new Material(refPelo) { name = nm + " (pelo HDRP)" };
                                nuevo.SetTexture("_BaseColorMap", t);
                                nuevo.SetTexture("_NormalMap", m.HasProperty("_NormalMap") ? m.GetTexture("_NormalMap") : null);
                                nuevo.SetTexture("_MaskMap", m.HasProperty("_MaskMap") ? m.GetTexture("_MaskMap") : null);
                                if (m.HasProperty("_BaseColor")) nuevo.SetColor("_BaseColor", m.GetColor("_BaseColor"));
                                if (m.HasProperty("_AlphaCutoff") && nuevo.HasProperty("_AlphaClipThreshold")) nuevo.SetFloat("_AlphaClipThreshold", Mathf.Clamp(m.GetFloat("_AlphaCutoff"), 0.05f, 0.6f));
                                nuevo.hideFlags = HideFlags.DontUnloadUnusedAsset;
                                peloOriginal.Add((r, i, m)); mats[i] = nuevo; cambio = true; n++;
                                Plugin.Log.LogInfo($"  pelo_hdrp: {r.name} / {nm} -> HDRP/Hair (corte {m.GetFloat("_AlphaCutoff"):0.00})");
                            }
                            if (cambio) r.sharedMaterials = mats;
                        }
                    }
                    else
                    {
                        foreach (var (r, i, m) in peloOriginal) { if (r == null) continue; var mats = r.sharedMaterials; if (i < mats.Length) { mats[i] = m; r.sharedMaterials = mats; n++; } }
                        peloOriginal.Clear();
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("vista "))
                {
                    // vista MATERIAL: lleva al jugador frente al objeto más chico (>0,3 m) con ese material
                    bool antes = MoverJugador.Value; MoverJugador.Value = true;
                    try { PrepararVista(c.Substring(6).Trim()); } finally { MoverJugador.Value = antes; }
                    proximoComando = Time.unscaledTime + 1f; return;
                }
                if (c.StartsWith("objeto "))
                {
                    // objeto NOMBRE on|off: enciende o apaga (solo para mirar) los objetos con ese nombre; "off" solo apaga los que encendió este comando
                    var pz = c.Substring(7).Trim(); int sp = pz.LastIndexOf(' '); string nom = pz.Substring(0, sp); bool on = pz.Substring(sp + 1) == "on"; int n = 0;
                    if (on)
                    {
                        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                            if (t != null && t.gameObject.scene.IsValid() && t.name == nom && !t.gameObject.activeInHierarchy)
                            {
                                for (var a = t; a != null; a = a.parent) if (!a.gameObject.activeSelf) { a.gameObject.SetActive(true); encendidos.Add(a.gameObject); }
                                n++;
                            }
                        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                            if (t != null && t.gameObject.scene.IsValid() && t.name == nom)
                            {
                                foreach (var v in t.GetComponentsInChildren<UnityEngine.VFX.VisualEffect>(true)) { v.enabled = true; v.Reinit(); v.Play(); }
                                string cadena = ""; for (var a = t; a != null; a = a.parent) cadena += a.name + (a.gameObject.activeSelf ? "" : "(apagado)") + " < ";
                                Plugin.Log.LogInfo("  cadena: " + cadena);
                            }
                    }
                    else { foreach (var g in encendidos) if (g != null && g.name == nom) { g.SetActive(false); n++; } encendidos.RemoveAll(g => g == null || g.name == nom); }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("escena "))
                {
                    // escena NOMBRE: carga esa escena del juego (p. ej. Mortuary_Tutorial) para revisarla
                    string nom = c.Substring(7).Trim(); Plugin.Log.LogInfo("Comando: escena " + nom);
                    SceneManager.LoadScene(nom); proximoComando = Time.unscaledTime + 3f; return;
                }
                if (c.StartsWith("traer "))
                {
                    // traer NOMBRE [dist]: pone el objeto con ese nombre (p. ej. un cuerpo) delante de la cámara, apoyado en el suelo, para mirarlo
                    var pz = c.Substring(6).Trim(); float dist = 1.6f; int sp = pz.LastIndexOf(' ');
                    if (sp > 0 && float.TryParse(pz.Substring(sp + 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float dd)) { dist = dd; pz = pz.Substring(0, sp); }
                    var cam = Camera.main; if (cam == null) foreach (var cc in Camera.allCameras) if (cc.isActiveAndEnabled) { cam = cc; break; } int n = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || !t.gameObject.scene.IsValid() || t.name != pz || n++ > 0) continue;
                        var fw = cam.transform.forward; fw.y = 0; fw.Normalize();
                        var p0 = cam.transform.position + fw * dist;
                        if (Physics.Raycast(p0, Vector3.down, out var hit, 5f)) p0.y = hit.point.y + 0.9f; else p0.y -= 0.7f;
                        Vector3 desfase = Vector3.zero;   // la malla puede estar lejos del origen del objeto (cuerpo dentro de un cajón)
                        foreach (var sk in t.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (sk != null && sk.name.Contains("Body") && sk.rootBone != null) { desfase = sk.rootBone.position - t.position; desfase.y = 0; break; }
                        t.position = p0 - desfase; t.gameObject.SetActive(true);
                        foreach (var sk in t.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        {
                            if (sk == null) continue;
                            var rb = sk.rootBone;
                            Plugin.Log.LogInfo($"  traer: {sk.name} raíz={(rb != null ? rb.name : "-")} dentro={(rb != null && rb.IsChildOf(t))} centro={sk.bounds.center} activo={sk.gameObject.activeInHierarchy}");
                            if (rb != null && !rb.IsChildOf(t)) rb.position += p0 - sk.bounds.center;
                            sk.updateWhenOffscreen = true;
                        }
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("cinematica"))
                {
                    // cinematicas: lista las cinemáticas (PlayableDirector); cinematica N [segundo]: la reproduce desde ese segundo
                    var lista = new List<UnityEngine.Playables.PlayableDirector>();
                    foreach (var d in Resources.FindObjectsOfTypeAll<UnityEngine.Playables.PlayableDirector>()) if (d != null && d.gameObject.scene.IsValid()) lista.Add(d);
                    if (c == "cinematicas")
                        for (int i = 0; i < lista.Count; i++) { var d = lista[i]; Plugin.Log.LogInfo($"  cinemática {i + 1}: {d.name} padre={(d.transform.parent != null ? d.transform.parent.name : "-")} activa={d.isActiveAndEnabled} asset={(d.playableAsset != null ? d.playableAsset.name : "-")} dura={d.duration:0.0} s estado={d.state} t={d.time:0.0}"); }
                    else
                    {
                        var pz = c.Substring(11).Trim().Split(' '); int k = int.Parse(pz[0]) - 1; var d = lista[k];
                        d.gameObject.SetActive(true); d.enabled = true;
                        d.time = pz.Length > 1 ? double.Parse(pz[1], System.Globalization.CultureInfo.InvariantCulture) : 0; d.Play();
                        Plugin.Log.LogInfo($"Comando: cinemática {d.name} desde {d.time:0.0} s");
                    }
                    proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("tiempo "))
                {
                    // tiempo X: escala de tiempo del juego (0 = congelar para capturar)
                    Time.timeScale = float.Parse(c.Substring(7).Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("posicion "))
                {
                    // posicion x,y,z x,y,z: pone la cámara del jugador en el primer punto mirando al segundo
                    var ci = System.Globalization.CultureInfo.InvariantCulture; var pz = c.Substring(9).Trim().Split(' ');
                    Vector3 V(string t) { var q = t.Split(','); return new Vector3(float.Parse(q[0], ci), float.Parse(q[1], ci), float.Parse(q[2], ci)); }
                    Vector3 pos = V(pz[0]), centro = V(pz[1]);
                    var jug = UnityEngine.Object.FindObjectOfType<MFPP.Player>();
                    if (jug != null && Camera.main != null)
                    {
                        var cc2 = jug.GetComponent<CharacterController>(); if (cc2 != null) cc2.enabled = false;
                        jug.transform.position += pos - Camera.main.transform.position;
                        if (cc2 != null) cc2.enabled = true;
                        var dir = (centro - pos).normalized;
                        var ang = new Vector2(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Asin(dir.y) * Mathf.Rad2Deg);
                        jug.TargetLookAngles = ang; jug.LookAngles = ang;
                        Plugin.Log.LogInfo($"Comando: posicion (MFPP) cámara {Camera.main.transform.position} ángulos {ang}"); proximoComando = Time.unscaledTime + 0.5f; return;
                    }
                    var cam = Camera.main.transform; var raiz = cam.root; var cc = raiz.GetComponentInChildren<CharacterController>();
                    if (cc != null) cc.enabled = false;
                    var plano = centro - pos; plano.y = 0; if (plano.sqrMagnitude > 0.01f) raiz.rotation = Quaternion.LookRotation(plano.normalized);
                    raiz.position += pos - cam.position; cam.LookAt(centro);
                    if (cc != null) cc.enabled = true;
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("ir "))
                {
                    // ir NOMBRE [dist] [altura]: lleva al jugador frente al objeto con ese nombre (aunque esté apagado), mirándolo
                    var pz = c.Substring(3).Trim().Split('|'); string nom = pz[0].Trim(); var ci = System.Globalization.CultureInfo.InvariantCulture;
                    float dist = pz.Length > 1 ? float.Parse(pz[1], ci) : 2f, alto = pz.Length > 2 ? float.Parse(pz[2], ci) : 0.3f; int n = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || !t.gameObject.scene.IsValid() || t.name != nom || n++ > 0) continue;
                        var cam = Camera.main.transform; var raiz = cam.root; var cc = raiz.GetComponentInChildren<CharacterController>();
                        Vector3 centro = t.position, mejor = Vector3.forward; float md = -1;
                        for (int i = 0; i < 8; i++) { var dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward; float d = Physics.Raycast(centro + Vector3.up * 0.5f, dir, out var h, 4f) ? h.distance : 4f; if (d > md) { md = d; mejor = dir; } }
                        var pos = centro + mejor * Mathf.Min(dist, md * 0.9f) + Vector3.up * alto;
                        MoverCamara(pos, centro);
                        Plugin.Log.LogInfo($"Comando: ir {nom}: jugador en {pos} mirando {centro}");
                    }
                    if (n == 0) Plugin.Log.LogInfo("Comando: ir: no encontré " + nom);
                    proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c == "inspeccion_objetos" || c.StartsWith("inspeccionar_obj ") || c == "desinspeccionar")
                {
                    // inspeccion_objetos: lista los ItemInspection (malla del mundo y malla que se muestra al inspeccionar) con sus materiales
                    // inspeccionar_obj NOMBRE: el jugador inspecciona ese objeto como si lo tomara; desinspeccionar: lo suelta
                    string MatInfo(Renderer r)
                    {
                        if (r == null) return "-";
                        string t = r.name + " activo=" + r.gameObject.activeInHierarchy + " bloque=" + r.HasPropertyBlock() + " capa=" + r.gameObject.layer + ":";
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            string f(string k) => m.HasProperty(k) ? m.GetFloat(k).ToString("0.##") : "-";
                            t += $" {m.name}[{m.shader.name}] met={f("_Metallic")} metMax={f("_MetallicRemapMax")} lisoMin={f("_SmoothnessRemapMin")} lisoMax={f("_SmoothnessRemapMax")} liso={f("_Smoothness")} mask={(m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null ? m.GetTexture("_MaskMap").name : "-")}";
                        }
                        return t;
                    }
                    if (c == "inspeccion_objetos")
                    {
                        foreach (var ii in Resources.FindObjectsOfTypeAll<ItemInspection>())
                        {
                            if (ii == null || !ii.gameObject.scene.IsValid()) continue;
                            Plugin.Log.LogInfo($"  ItemInspection {ii.name} id={ii.itemID} activo={ii.gameObject.activeInHierarchy} | mio: {MatInfo(ii.myObject)} | muestra: {MatInfo(ii.swapObject)}");
                        }
                    }
                    else
                    {
                        var pi2 = UnityEngine.Object.FindObjectOfType<PlayerInteraction>();
                        if (c == "desinspeccionar") { if (pi2 != null) pi2.UnInspect(false, false); }
                        else
                        {
                            string q = c.Substring(17).Trim(); GameObject go = null;
                            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>()) if (t != null && t.gameObject.scene.IsValid() && t.name == q) { go = t.gameObject; break; }
                            if (go == null || pi2 == null) Plugin.Log.LogWarning("inspeccionar_obj: no encontrado " + q + " (jugador " + (pi2 != null) + ")");
                            else
                            {
                                for (var tr = go.transform; tr != null; tr = tr.parent) if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                                pi2.InspectItem(go);
                            }
                        }
                    }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 1f; return;
                }
                if (c.StartsWith("mats_de "))
                {
                    // mats_de NOMBRE: materiales de todas las mallas bajo el objeto con ese nombre, con metal y brillo
                    string q = c.Substring(8).Trim(); int n = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || !t.gameObject.scene.IsValid() || t.name != q || n++ > 0) continue;
                        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                            foreach (var m in r.sharedMaterials)
                            {
                                if (m == null) { Plugin.Log.LogInfo($"  {r.name}: (material vacío)"); continue; }
                                string f(string k) => m.HasProperty(k) ? m.GetFloat(k).ToString("0.##") : "-";
                                Plugin.Log.LogInfo($"  {r.name} activo={r.gameObject.activeInHierarchy} / {m.name} [{m.shader.name}] met={f("_Metallic")} metMax={f("_MetallicRemapMax")} lisoMax={f("_SmoothnessRemapMax")} matID={f("_MaterialID")} normal={f("_NormalScale")}");
                            }
                    }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("malla "))
                {
                    string q = c.Substring(6).Trim();
                    foreach (var mf in Resources.FindObjectsOfTypeAll<MeshFilter>())
                        if (mf != null && mf.gameObject.scene.IsValid() && mf.name == q && mf.sharedMesh != null)
                        {
                            var r = mf.GetComponent<Renderer>(); string mats = ""; if (r != null) foreach (var m in r.sharedMaterials) mats += (m != null ? m.name : "null") + ",";
                            Plugin.Log.LogInfo($"  malla {q}: legible={mf.sharedMesh.isReadable} vértices={mf.sharedMesh.vertexCount} submallas={mf.sharedMesh.subMeshCount} mats={mats} col={(mf.GetComponent<Collider>() != null ? mf.GetComponent<Collider>().GetType().Name : "-")}");
                        }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("rotar "))
                {
                    // rotar NOMBRE x,y,z: suma esa rotación (grados, ejes locales) al objeto activo con ese nombre
                    var pz = c.Substring(6).Trim(); int k = pz.LastIndexOf(' '); var ci = System.Globalization.CultureInfo.InvariantCulture;
                    string q = pz.Substring(0, k); var v = pz.Substring(k + 1).Split(',');
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                        if (t != null && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.name == q)
                        { t.Rotate(float.Parse(v[0], ci), float.Parse(v[1], ci), float.Parse(v[2], ci), Space.Self); Plugin.Log.LogInfo($"  rotar {q}: {t.localEulerAngles}"); }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("diag_render "))
                {
                    // diag_render NOMBRE: capa, máscara de capas de luz y pases personalizados (CustomPassVolume) de la escena
                    string q = c.Substring(12).Trim();
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                        if (r != null && r.gameObject.scene.IsValid() && r.name == q)
                            Plugin.Log.LogInfo($"  {r.name} activo={r.gameObject.activeInHierarchy} capa={r.gameObject.layer}({LayerMask.LayerToName(r.gameObject.layer)}) renderingLayerMask={r.renderingLayerMask} sombras={r.shadowCastingMode} recibe={r.receiveShadows} sondas={r.reflectionProbeUsage}/{r.lightProbeUsage}");
                    foreach (var v in Resources.FindObjectsOfTypeAll<CustomPassVolume>())
                    {
                        if (v == null || !v.gameObject.scene.IsValid()) continue;
                        string pases = ""; foreach (var cp in v.customPasses) if (cp != null) pases += cp.name + "[" + cp.GetIl2CppType().Name + (cp.enabled ? "" : ",off") + "] ";
                        Plugin.Log.LogInfo($"  CustomPassVolume {v.name} activo={v.isActiveAndEnabled} global={v.isGlobal} punto={v.injectionPoint} pases: {pases}");
                    }
                    foreach (var cam in Resources.FindObjectsOfTypeAll<Camera>())
                        if (cam != null && cam.gameObject.scene.IsValid() && cam.isActiveAndEnabled) Plugin.Log.LogInfo($"  cámara {cam.name} mascara={cam.cullingMask} prof={cam.depth} destino={(cam.targetTexture != null ? cam.targetTexture.name : "pantalla")}");
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("mat_color "))
                {
                    // mat_color TEXTO _Prop r,g,b[,a]: pone ese color en los materiales cuyo nombre contiene TEXTO
                    var pz = c.Substring(10).Trim().Split(' '); var ci = System.Globalization.CultureInfo.InvariantCulture; var v = pz[2].Split(','); int n = 0;
                    var col = new Color(float.Parse(v[0], ci), float.Parse(v[1], ci), float.Parse(v[2], ci), v.Length > 3 ? float.Parse(v[3], ci) : 1f);
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.name.Contains(pz[0]) && m.HasProperty(pz[1])) { m.SetColor(pz[1], col); n++; }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + " materiales)"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c == "luces_on" || c == "luces_info")
                {
                    // luces_info: estado de cada interruptor; luces_on: acciona los que están apagados (toggle = false)
                    int n = 0;
                    foreach (var ls in UnityEngine.Object.FindObjectsOfType<LightSwitch>())
                    {
                        if (ls == null) continue;
                        Plugin.Log.LogInfo($"  interruptor {ls.name} zona={ls.zone} encendido={ls.toggle}");
                        if (c == "luces_on" && !ls.toggle) { try { ls.ToggleLight(); n++; } catch (Exception ex) { Plugin.Log.LogWarning("Interruptor: " + ex.Message); } }
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + " accionados)"); proximoComando = Time.unscaledTime + 1f; return;
                }
                if (c.StartsWith("donde ") || c == "camara")
                {
                    // donde NOMBRE: posición y dirección de todos los objetos con ese nombre; camara: posición y dirección de la cámara
                    if (c == "camara") { var ct = Camera.main.transform; Plugin.Log.LogInfo($"  cámara pos={ct.position.x:0.00},{ct.position.y:0.00},{ct.position.z:0.00} mira={ct.forward.x:0.00},{ct.forward.y:0.00},{ct.forward.z:0.00}"); }
                    else
                    {
                        string q = c.Substring(6).Trim();
                        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                            if (t != null && t.gameObject.scene.IsValid() && t.name == q)
                            {
                                var r = t.GetComponentInChildren<Renderer>(); string caja = r != null ? $" caja={r.bounds.center} tam={r.bounds.size}" : "";
                                Plugin.Log.LogInfo($"  donde {q} padre={(t.parent != null ? t.parent.name : "-")} pos={t.position.x:0.00},{t.position.y:0.00},{t.position.z:0.00}{caja}");
                            }
                    }
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("limites "))
                {
                    // limites NOMBRE: caja (bounds) de las mallas con ese nombre
                    string q = c.Substring(8).Trim();
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                        if (r != null && r.gameObject.scene.IsValid() && r.name == q) Plugin.Log.LogInfo($"  {r.name} padre={(r.transform.parent != null ? r.transform.parent.name : "-")} activo={r.gameObject.activeInHierarchy} centro={r.bounds.center} tamaño={r.bounds.size} pos={r.transform.position} escala={r.transform.lossyScale}");
                    Plugin.Log.LogInfo("Comando: " + c); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("buscar_obj "))
                {
                    // buscar_obj TEXTO: objetos cuyo nombre contiene TEXTO (sin distinguir mayúsculas), con sus materiales
                    string q = c.Substring(11).Trim(); int n = 0;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    {
                        if (t == null || !t.gameObject.scene.IsValid() || t.name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 || n++ > 40) continue;
                        var r = t.GetComponent<Renderer>(); string mats = "";
                        if (r != null) foreach (var m in r.sharedMaterials) if (m != null) mats += m.name + " [" + m.shader.name + "] ";
                        Plugin.Log.LogInfo($"  objeto {t.name} padre={(t.parent != null ? t.parent.name : "-")} activo={t.gameObject.activeInHierarchy} {mats}");
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("vfx_suelto "))
                {
                    // vfx_suelto NOMBRE: saca ese efecto de su escena de cinemática (sin activar al padre) y lo reproduce solo
                    string nom = c.Substring(11).Trim(); int n = 0; bool delante = nom.EndsWith(" delante"); if (delante) nom = nom.Substring(0, nom.Length - 8);
                    string padre = null; int bar = nom.IndexOf('/'); if (bar > 0) { padre = nom.Substring(0, bar); nom = nom.Substring(bar + 1); }   // PADRE/NOMBRE
                    foreach (var v in Resources.FindObjectsOfTypeAll<UnityEngine.VFX.VisualEffect>())
                    {
                        if (v == null || !v.gameObject.scene.IsValid() || v.name != nom || (padre != null && (v.transform.parent == null || v.transform.parent.name != padre)) || n++ > 0) continue;
                        var t = v.transform; var pos = t.position; var rot = t.rotation;
                        t.SetParent(null, true); t.position = pos; t.rotation = rot;
                        if (delante && Camera.main != null) { var ct = Camera.main.transform; t.position = ct.position + ct.forward * 0.35f - ct.up * 0.05f; t.rotation = Quaternion.identity; }
                        v.gameObject.SetActive(true); v.enabled = true; v.Reinit(); v.Play(); v.SendEvent("OnPlay");
                        if (delante) t.localScale = Vector3.one * 6f;   // llama pequeña (encendedor): más grande para verla
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("cerca"))
                {
                    // cerca [R]: mallas a menos de R m de la cámara con sus materiales, shader, metal y brillo
                    float R = c.Length > 6 ? float.Parse(c.Substring(6).Trim(), System.Globalization.CultureInfo.InvariantCulture) : 4f;
                    var cam = Camera.main; Vector3 pc = cam != null ? cam.transform.position : Vector3.zero; int n = 0;
                    var lista = new List<(float d, string t)>();
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    {
                        if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                        float d = Vector3.Distance(r.bounds.ClosestPoint(pc), pc); if (d > R) continue;
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null) continue;
                            string f(string k) => m.HasProperty(k) ? m.GetFloat(k).ToString("0.##") : "-";
                            lista.Add((d, $"  d={d:0.0} {r.GetType().Name} {r.name} / {m.name} id {m.GetInstanceID()} [{m.shader.name}] met={f("_Metallic")} metMax={f("_MetallicRemapMax")} liso={f("_Smoothness")} lisoMin={f("_SmoothnessRemapMin")} lisoMax={f("_SmoothnessRemapMax")} matID={f("_MaterialID")} mask={(m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null)}"));
                        }
                    }
                    lista.Sort((a, b) => a.d.CompareTo(b.d));
                    foreach (var x in lista) if (n++ < 60) Plugin.Log.LogInfo(x.t);
                    Plugin.Log.LogInfo("Comando: " + c + " (" + lista.Count + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("props "))
                {
                    // props MATERIAL: todas las propiedades del shader de los materiales con ese nombre exacto (o id)
                    string q = c.Substring(6).Trim(); int n = 0;
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    {
                        if (m == null || (m.name != q && m.GetInstanceID().ToString() != q) || n++ > 0) continue;
                        var sh = m.shader; var sb = new System.Text.StringBuilder($"props {m.name} id {m.GetInstanceID()} [{sh.name}] claves: {string.Join(",", m.shaderKeywords)}\n");
                        for (int i = 0; i < sh.GetPropertyCount(); i++)
                        {
                            string pn = sh.GetPropertyName(i); var ty = sh.GetPropertyType(i);
                            string v = ty == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(pn) != null ? m.GetTexture(pn).name : "null")
                                : ty == UnityEngine.Rendering.ShaderPropertyType.Color ? m.GetColor(pn).ToString()
                                : ty == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(pn).ToString() : m.GetFloat(pn).ToString("0.###");
                            sb.Append($"    {pn} ({sh.GetPropertyDescription(i)}) = {v}\n");
                        }
                        Plugin.Log.LogInfo(sb.ToString());
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c == "efectos")
                {
                    // efectos: VFX Graph y sistemas de partículas de la escena (fuego, humo...) con su distancia
                    var cam = Camera.main; Vector3 pc = cam != null ? cam.transform.position : Vector3.zero; int n = 0;
                    foreach (var v in Resources.FindObjectsOfTypeAll<UnityEngine.VFX.VisualEffect>())
                    {
                        if (v == null || !v.gameObject.scene.IsValid()) continue; n++;
                        string ex = "";
                        foreach (var k in new[] { "Intensity", "intensity", "Size", "Rate", "SpawnRate", "Color", "Lifetime", "Speed", "Alpha", "Scale" })
                            if (v.HasFloat(k)) ex += k + "=" + v.GetFloat(k).ToString("0.##") + " "; else if (v.HasVector4(k)) ex += k + "=" + v.GetVector4(k) + " ";
                        Plugin.Log.LogInfo($"  vfx {v.name} padre={(v.transform.parent != null ? v.transform.parent.name : "-")} activo={v.isActiveAndEnabled} d={Vector3.Distance(v.transform.position, pc):0.0} asset={(v.visualEffectAsset != null ? v.visualEffectAsset.name : "-")} vivas={v.aliveParticleCount} expuestas: {ex}");
                    }
                    foreach (var ps in Resources.FindObjectsOfTypeAll<ParticleSystem>())
                    {
                        if (ps == null || !ps.gameObject.scene.IsValid()) continue; n++;
                        var pr = ps.GetComponent<ParticleSystemRenderer>();
                        Plugin.Log.LogInfo($"  partículas {ps.name} padre={(ps.transform.parent != null ? ps.transform.parent.name : "-")} activo={ps.gameObject.activeInHierarchy} d={Vector3.Distance(ps.transform.position, pc):0.0} mat={(pr != null && pr.sharedMaterial != null ? pr.sharedMaterial.name + " [" + pr.sharedMaterial.shader.name + "]" : "-")}");
                    }
                    Plugin.Log.LogInfo("Comando: efectos (" + n + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("buscar_mat "))
                {
                    // buscar_mat TEXTO: objetos (mallas y calcomanías) cuyo material o shader contiene TEXTO, con distancia a la cámara
                    string q = c.Substring(11).Trim(); var cam = Camera.main; Vector3 pc = cam != null ? cam.transform.position : Vector3.zero; int n = 0;
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    {
                        if (r == null || !r.gameObject.scene.IsValid()) continue;
                        foreach (var m in r.sharedMaterials)
                            if (m != null && (m.name.Contains(q) || m.shader.name.Contains(q)) && n++ < 40)
                                Plugin.Log.LogInfo($"  malla {r.name} padre={(r.transform.parent != null ? r.transform.parent.name : "-")} activa={r.gameObject.activeInHierarchy && r.enabled} d={Vector3.Distance(r.bounds.center, pc):0.0} / {m.name} [{m.shader.name}]");
                    }
                    foreach (var dp in Resources.FindObjectsOfTypeAll<DecalProjector>())
                    {
                        if (dp == null || !dp.gameObject.scene.IsValid() || dp.material == null) continue;
                        var m = dp.material;
                        if ((m.name.Contains(q) || m.shader.name.Contains(q) || q == "*") && n++ < 80)
                            Plugin.Log.LogInfo($"  calcomanía {dp.name} padre={(dp.transform.parent != null ? dp.transform.parent.name : "-")} activa={dp.isActiveAndEnabled} d={Vector3.Distance(dp.transform.position, pc):0.0} / {m.name} opacidad={dp.fadeFactor:0.00}");
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + ")"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("mat_prop "))
                {
                    // mat_prop TEXTO _Propiedad=valor (o x1.5 para multiplicar el valor original): todos los materiales cuyo nombre contiene TEXTO
                    var pz = c.Substring(9).Trim().Split(' '); var kv = pz[1].Split('='); var ci = System.Globalization.CultureInfo.InvariantCulture; int n = 0;
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    {
                        if (m == null || !m.name.Contains(pz[0]) || !m.HasProperty(kv[0])) continue;
                        int id = m.GetInstanceID() * 31 + kv[0].GetHashCode();
                        if (!propOriginal.ContainsKey(id)) propOriginal[id] = m.GetFloat(kv[0]);
                        m.SetFloat(kv[0], kv[1].StartsWith("x") ? propOriginal[id] * float.Parse(kv[1].Substring(1), ci) : float.Parse(kv[1], ci)); n++;
                    }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + " materiales)"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("piel_prop "))
                {
                    // piel_prop _Propiedad=valor: cambia esa propiedad en todos los materiales de piel cargados (solo pruebas)
                    var kv = c.Substring(10).Split('='); float val = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture); int n = 0;
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                        if (m != null && (m.name.Contains("Skin") || m.name == "SKIN") && m.HasProperty(kv[0])) { m.SetFloat(kv[0], val); n++; }
                    Plugin.Log.LogInfo("Comando: " + c + " (" + n + " materiales)"); proximoComando = Time.unscaledTime + 0.3f; return;
                }
                if (c.StartsWith("inspec"))
                {
                    // inspecciones = lista; inspeccionar N = entra; inspeccion_salir = sale; inspeccion_info = materiales y luces cerca
                    var jug = UnityEngine.Object.FindObjectOfType<MFPP.Player>();
                    Vector3 pj = jug != null ? jug.transform.position : Vector3.zero;
                    var lista = new List<Inspect>();
                    foreach (var ins in Resources.FindObjectsOfTypeAll<Inspect>())
                        if (ins != null && ins.gameObject.scene.IsValid() && ins.gameObject.activeInHierarchy) lista.Add(ins);
                    lista.Sort((a, b) => Vector3.Distance(a.transform.position, pj).CompareTo(Vector3.Distance(b.transform.position, pj)));
                    if (c == "inspecciones")
                        for (int i = 0; i < lista.Count && i < 25; i++)
                            Plugin.Log.LogInfo($"  inspección {i + 1}: {lista[i].name} padre={(lista[i].transform.parent != null ? lista[i].transform.parent.name : "-")} d={Vector3.Distance(lista[i].transform.position, pj):0.0} tipo={lista[i].inspectType} nodoCuerpo={lista[i].bodyInspectionNode}");
                    else if (c.StartsWith("inspeccionar ")) { string arg = c.Substring(13).Trim(); int k = int.TryParse(arg, out int kn) ? kn - 1 : lista.FindIndex(x => x.name == arg); lista[k].RunEvent(); Plugin.Log.LogInfo("Comando: inspeccionar " + lista[k].name); }
                    else if (c == "inspeccion_salir") { foreach (var ins in lista) ins.LeaveInteract(); }
                    else if (c == "inspeccion_pases" || c == "inspeccion_pases_off" || c == "inspeccion_pases_on")
                    {
                        foreach (var v in Resources.FindObjectsOfTypeAll<CustomPassVolume>())
                        {
                            if (v == null || !v.gameObject.scene.IsValid()) continue;
                            if (c == "inspeccion_pases_off" && v.enabled) { v.enabled = false; pasesApagados.Add(v); }
                            if (c == "inspeccion_pases_on" && pasesApagados.Contains(v)) v.enabled = true;
                            string ps = "";
                            foreach (var cp in v.customPasses) if (cp != null) ps += cp.name + "(" + cp.GetIl2CppType().Name + (cp.enabled ? "" : ",off") + ") ";
                            Plugin.Log.LogInfo($"  pase {v.name} padre={(v.transform.parent != null ? v.transform.parent.name : "-")} activo={v.isActiveAndEnabled} global={v.isGlobal} inyección={v.injectionPoint} {ps}");
                        }
                        if (c == "inspeccion_pases_on") pasesApagados.Clear();
                    }
                    else if (c == "inspeccion_info")
                    {
                        var cam = Camera.main; Vector3 pc = cam != null ? cam.transform.position : pj;
                        foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                        {
                            if (r == null || !r.gameObject.scene.IsValid() || !r.gameObject.activeInHierarchy || !r.enabled) continue;
                            if (Vector3.Distance(r.bounds.ClosestPoint(pc), pc) > 2.5f) continue;
                            string s0 = "";
                            foreach (var m in r.sharedMaterials)
                            {
                                if (m == null) continue;
                                if (!m.name.Contains("Skin")) { Plugin.Log.LogInfo($"  material {r.name} ({r.GetIl2CppType().Name}, capa {r.gameObject.layer}) / {m.name} shader={m.shader.name} q={m.renderQueue}"); continue; }
                                s0 = m.shader.name;
                                string s = r.name + " / " + m.name + " [" + s0 + "]:";
                                foreach (var p in new[] { "_Metallic", "_Smoothness", "_SmoothnessRemapMin", "_SmoothnessRemapMax", "_MaterialID", "_DetailSmoothnessScale", "_DetailNormalScale", "_ReceivesSSR" })
                                    if (m.HasProperty(p)) s += $" {p}={m.GetFloat(p):0.00}";
                                s += " mask=" + (m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null ? m.GetTexture("_MaskMap").name : "-");
                                s += " kw=" + string.Join(",", m.shaderKeywords);
                                for (int pi2 = 0; pi2 < m.shader.GetPropertyCount(); pi2++)
                                {
                                    string pn = m.shader.GetPropertyName(pi2);
                                    if (pn.IndexOf("Emiss", StringComparison.OrdinalIgnoreCase) < 0 && pn.IndexOf("Rim", StringComparison.OrdinalIgnoreCase) < 0 && pn.IndexOf("Fresnel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                                    var tp = m.shader.GetPropertyType(pi2);
                                    s += " " + pn + "=" + (tp == UnityEngine.Rendering.ShaderPropertyType.Color || tp == UnityEngine.Rendering.ShaderPropertyType.Vector ? m.GetVector(pn).ToString() : tp == UnityEngine.Rendering.ShaderPropertyType.Texture ? (m.GetTexture(pn) != null ? m.GetTexture(pn).name : "-") : m.GetFloat(pn).ToString("0.###"));
                                }
                                var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb);
                                s += " bloque=" + (mpb.isEmpty ? "vacío" : "CON VALORES emis=" + mpb.GetVector("_EmissiveColor"));
                                Plugin.Log.LogInfo("  piel " + s);
                            }
                        }
                        foreach (var l in Resources.FindObjectsOfTypeAll<Light>())
                        {
                            if (l == null || !l.gameObject.scene.IsValid() || !l.isActiveAndEnabled) continue;
                            float d = Vector3.Distance(l.transform.position, pc);
                            if (d > 6f) continue;
                            Plugin.Log.LogInfo($"  luz {l.name} padre={(l.transform.parent != null ? l.transform.parent.name : "-")} tipo={l.type} int={l.intensity:0.0} d={d:0.0} color={l.color}");
                        }
                    }
                    proximoComando = Time.unscaledTime + 0.5f; return;
                }
                if (c.StartsWith("captura ")) { Capturar(c.Substring(8)); proximoComando = Time.unscaledTime + 0.4f; return; }
                int ig = c.IndexOf('=');
                var e = Entrada(c.Substring(0, ig));
                if (e == null) { Plugin.Log.LogWarning("Comando: no existe " + c); return; }
                e.SetSerializedValue(c.Substring(ig + 1));
                Opciones.AplicarTodo();
                Plugin.Log.LogInfo("Comando: " + c);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Comando '" + c + "': " + ex.Message); }
        }

        public static void Tick()
        {
            try
            {
                Comandos();
                if (giroJugador != null && Time.unscaledTime < giroHasta)
                {
                    var d = new Vector2(0f, giroPorSeg * Time.unscaledDeltaTime);
                    giroJugador.TargetLookAngles = giroJugador.TargetLookAngles + d;
                    giroJugador.LookAngles = giroJugador.LookAngles + d;
                }
                var activa = SceneManager.GetActiveScene();
                int firma = SceneManager.sceneCount * 7919 + activa.handle;
                if (firma != firmaEscena)
                {
                    firmaEscena = firma;
                    escenaActual = activa.name;
                    Plugin.Log.LogInfo("Escena: " + escenaActual + " (escenas cargadas: " + SceneManager.sceneCount + ")");
                    momentoCaptura = CapturaAutoSegundos.Value > 0 ? Time.unscaledTime + CapturaAutoSegundos.Value : -1f;
                    momentoBoton = Time.unscaledTime + 4f;
                }
                var kb = Keyboard.current;
                if (kb != null && kb[TeclaCaptura.Value].wasPressedThisFrame) Capturar("manual");
                bool escenaOk = string.IsNullOrEmpty(EscenaCaptura.Value) || escenaActual.Contains(EscenaCaptura.Value);
                if (momentoCaptura > 0 && Time.unscaledTime >= momentoCaptura)
                {
                    momentoCaptura = -1f;
                    if (escenaOk)
                    {
                        if (SecuenciaAB.Value) IniciarSecuencia();
                        else if (PruebaRendimiento.Value) { estadoRend = 0; AplicarEstado(0); }
                        else Capturar("auto");
                    }
                }
                if (paso >= 0) AvanzarSecuencia();
                if (menuCapturas > 0 && Time.frameCount >= cuadroMenu) { Opciones.PestañaPrueba(4 - menuCapturas); Capturar("menu" + (4 - menuCapturas)); menuCapturas--; cuadroMenu = Time.frameCount + 15; }
                if (estadoRend >= 0) AvanzarRendimiento();
                if (momentoBoton > 0 && Time.unscaledTime >= momentoBoton)
                {
                    momentoBoton = Time.unscaledTime + 5f;
                    RevisarBotones();
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Pruebas: " + e); paso = -1; RestaurarCamara(); }
        }

        internal static void CapturaPublica(string tipo) => Capturar(tipo);
        static void Capturar(string tipo)
        {
            string dir = Path.Combine(Paths.GameRootPath, "Capturas");
            Directory.CreateDirectory(dir);
            string ruta = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + tipo + "_" + escenaActual + ".png");
            ScreenCapture.CaptureScreenshot(ruta);
            Plugin.Log.LogInfo("Captura guardada: " + ruta);
        }

        // ModuloAB = "cfg:Seccion.Clave=valorB" → la foto B usa ese valor; la A, el valor configurado
        static string cfgValorA;
        static string valorOriginal;
        static ConfigEntryBase Entrada(string clave)
        {
            int pt = clave.IndexOf('.');
            foreach (var kv in Plugin.Instancia.Config) if (kv.Key.Section == clave.Substring(0, pt) && kv.Key.Key == clave.Substring(pt + 1)) return kv.Value;
            return null;
        }
        // estado: 1 = valor A, 0 = valor B, -1 = valor original
        static void PonerOpcion(string spec, int estado)
        {
            int ig = spec.IndexOf('=');
            string clave = spec.Substring(0, ig); var ab = spec.Substring(ig + 1).Split('>');
            var e = Entrada(clave);
            if (e == null) { Plugin.Log.LogWarning("Pruebas: no existe la opción " + clave); return; }
            if (estado == 1 && valorOriginal == null) valorOriginal = e.GetSerializedValue();
            if (estado == -1) { if (valorOriginal != null) e.SetSerializedValue(valorOriginal); valorOriginal = null; }
            else e.SetSerializedValue(estado == 1 ? ab[0] : ab[1]);
            Plugin.Log.LogInfo($"Pruebas: {clave} = {e.GetSerializedValue()}");
            Opciones.AplicarTodo();
        }
        static string OpcionActual => vistaIdx >= 0 && vistaIdx < opcionesVista.Count ? opcionesVista[vistaIdx] : null;
        static void Mejoras(bool on)
        {
            if (OpcionActual != null) { PonerOpcion(OpcionActual, on ? 1 : 0); return; }
            if (ModuloAB.Value.StartsWith("cfg:"))
            {
                var def = ModuloAB.Value.Substring(4);
                int ig = def.IndexOf('=');
                string clave = def.Substring(0, ig), valorB = def.Substring(ig + 1);
                int pt = clave.IndexOf('.');
                var cd = new ConfigDefinition(clave.Substring(0, pt), clave.Substring(pt + 1));
                if (!Plugin.Instancia.Config.TryGetEntry<string>(cd, out _)) { }
                ConfigEntryBase entrada = null;
                foreach (var kv in Plugin.Instancia.Config) if (kv.Key.Section == cd.Section && kv.Key.Key == cd.Key) entrada = kv.Value;
                if (entrada == null) { Plugin.Log.LogWarning("Pruebas: no existe la opción " + clave); return; }
                if (!on) { cfgValorA = entrada.GetSerializedValue(); entrada.SetSerializedValue(valorB); }
                else if (cfgValorA != null) entrada.SetSerializedValue(cfgValorA);
                Plugin.Log.LogInfo($"Pruebas: {clave} = {entrada.GetSerializedValue()}");
                Opciones.AplicarTodo();
                return;
            }
            if (ModuloAB.Value == "materiales") { Materiales.Alternar(on); return; }
            if (ModuloAB.Value == "texturas") { TexturasIA.Alternar(on); return; }
            if (ModuloAB.Value == "realismo") { Realismo.Alternar(on); return; }
            if (ModuloAB.Value == "post") { Runner.Actual?.PostProceso(on); return; }
            if (ModuloAB.Value == "luz") { Realismo.AlternarLuz(on); return; }
            if (ModuloAB.Value == "piel" || ModuloAB.Value == "microdetalle" || ModuloAB.Value == "relieve" || ModuloAB.Value == "brillo" || ModuloAB.Value == "pasto")
            { Materiales.Alternar(on, ModuloAB.Value); return; }
            TexturasIA.Alternar(on);
            Realismo.Alternar(on);
            Materiales.Alternar(on);
            Runner.Actual?.PostProceso(on);
        }

        // ---------- secuencia A/B ----------
        static readonly List<string> opcionesVista = new List<string>();   // opción "Sec.Clave=A>B" de cada vista (modo cfgs)
        static void IniciarSecuencia()
        {
            vistas.Clear(); opcionesVista.Clear();
            if (ModuloAB.Value.StartsWith("cfgs:"))
            {
                foreach (var item in ModuloAB.Value.Substring(5).Split(';'))
                {
                    if (item.Trim().Length == 0) continue;
                    var partes = item.Trim().Split('@');
                    var vs = partes.Length > 1 ? partes[1].Split('+') : new[] { "Tile" };
                    foreach (var v in vs) { vistas.Add(v.Trim()); opcionesVista.Add(partes[0].Trim()); }
                }
            }
            else
            {
                vistas.Add("");   // vista del jugador
                foreach (var v in VistasPrueba.Value.Split(',')) if (v.Trim().Length > 0) vistas.Add(v.Trim());
            }
            if (DiagnosticoEscena.Value) DiagnosticarEscena();
            if (EncenderLuces.Value)
            {
                int n = 0;
                foreach (var ls in UnityEngine.Object.FindObjectsOfType<LightSwitch>())
                {
                    try { ls.ToggleLight(); n++; } catch (Exception e) { Plugin.Log.LogWarning("Interruptor: " + e.Message); }
                }
                Plugin.Log.LogInfo("Pruebas: " + n + " interruptores accionados");
            }
            vistaIdx = 0; paso = 0; cuadroPaso = Time.frameCount + 60;
            escalaTiempo = Time.timeScale;
            Plugin.Log.LogInfo("Pruebas: secuencia A/B con " + vistas.Count + " vistas");
        }

        static void AvanzarSecuencia()
        {
            int espera = 12;   // cuadros entre pasos (deja asentar TAA/DLSS y exposición)
            if (Time.frameCount - cuadroPaso < espera) return;
            cuadroPaso = Time.frameCount;
            string v = vistas[vistaIdx];
            string etiqueta = v.Length == 0 ? "jugador" : v;
            if (OpcionActual != null) etiqueta = OpcionActual.Substring(0, OpcionActual.IndexOf('=')).Replace('.', '-') + "__" + v.Replace(' ', '_');
            switch (paso)
            {
                case 0:
                    if (v.Length > 0 && !PrepararVista(v)) { SiguienteVista(); return; }
                    paso = 5; return;
                case 5:
                    bool necesitaTiempo = OpcionActual != null && (OpcionActual.Contains("ExposicionAutomatica") || OpcionActual.Contains("DesenfoqueMovimiento"));
                    Time.timeScale = necesitaTiempo ? escalaTiempo : 0f; paso = 1; cuadroPaso = Time.frameCount + (necesitaTiempo ? 150 : 25);
                    if (OpcionActual != null) PonerOpcion(OpcionActual, 1);
                    return;   // congela jugador y lluvia: A y B idénticas salvo el cambio
                case 1: DiagMaterialVista("A"); Capturar("A_con_" + etiqueta); paso = 2; return;
                case 2: Mejoras(false); paso = 3; cuadroPaso = Time.frameCount + (Time.timeScale > 0 ? 150 : 20); return;   // espera extra
                case 3: DiagMaterialVista("B"); Capturar("B_sin_" + etiqueta); paso = 4; return;
                case 4:
                    if (OpcionActual != null) PonerOpcion(OpcionActual, -1); else Mejoras(true);
                    SiguienteVista(); return;
            }
        }

        static float escalaTiempo = 1f;
        static int menuCapturas, cuadroMenu;
        static void SiguienteVista()
        {
            Time.timeScale = escalaTiempo;   // deja moverse al juego entre vistas
            vistaIdx++;
            if (vistaIdx >= vistas.Count)
            {
                paso = -1;
                RestaurarCamara();
                Plugin.Log.LogInfo("Pruebas: secuencia A/B terminada");
                if (AbrirMenu.Value) { Opciones.AbrirCerrar(); menuCapturas = 4; cuadroMenu = Time.frameCount + 20; }
                if (PruebaRendimiento.Value) { estadoRend = 0; AplicarEstado(0); }
                return;
            }
            paso = 0;
        }

        static Light luzPrueba;
        internal static ConfigEntry<float> LuzPruebaLumenes;

        // "cuerpoN": encuadra la cara del N-ésimo cadáver (SkinnedMeshRenderer con piel), desde arriba
        static bool PrepararCuerpo(int n)
        {
            var lista = new List<SkinnedMeshRenderer>();
            foreach (var r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.name != "CC_Base_Body") continue;
                lista.Add(r);
            }
            lista.Sort((a, b) => a.bounds.center.z.CompareTo(b.bounds.center.z));
            if (n < 1 || n > lista.Count) { Plugin.Log.LogInfo("Pruebas: no hay cuerpo " + n + " (hay " + lista.Count + ")"); return false; }
            var cuerpo = lista[n - 1];
            Transform cabeza = null, pecho = null; Vector3 cabezaPos = Vector3.zero;
            foreach (var b in cuerpo.bones)
            {
                if (b == null) continue;
                string bn = b.name.ToLowerInvariant();
                if (cabeza == null && bn.EndsWith("head")) cabeza = b;
                if (pecho == null && (bn.EndsWith("spine02") || bn.EndsWith("spine2") || bn.EndsWith("chest"))) pecho = b;
            }
            if (cabeza == null)
            {
                // sin huesos con nombre: la cabeza es donde está el pelo más cercano
                float mejor = 1.6f;
                foreach (var r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
                {
                    if (r == null || r == cuerpo || !r.enabled || r.bounds.size.magnitude > 0.7f) continue;
                    var m = r.sharedMaterial; if (m == null) continue;
                    string mn = m.name.ToLowerInvariant();
                    if (!mn.Contains("hair") && !mn.Contains("scalp") && !r.name.ToLowerInvariant().Contains("cut") && !r.name.Contains("blowback") && !r.name.Contains("fringe")) continue;
                    float d = Vector3.Distance(r.bounds.center, cuerpo.bounds.center);
                    if (d < mejor) { mejor = d; cabeza = r.transform; cabezaPos = r.bounds.center; }
                }
                if (cabeza != null) Plugin.Log.LogInfo("Pruebas: cabeza del cuerpo " + n + " por el pelo " + cabeza.name);
                else { string hs = ""; int k = 0; foreach (var b in cuerpo.bones) if (b != null && k++ < 40) hs += b.name + ","; Plugin.Log.LogInfo("Pruebas: huesos " + hs); }
            }
            else cabezaPos = cabeza.position;
            if (cabeza == null) { Plugin.Log.LogInfo("Pruebas: cuerpo " + n + " sin hueso de cabeza"); return false; }
            Vector3 ojo = cabezaPos + Vector3.up * 0.04f;
            Vector3 haciaPies = pecho != null ? (pecho.position - cabezaPos) : (cuerpo.bounds.center - cabezaPos);
            haciaPies.y = 0; haciaPies = haciaPies.sqrMagnitude > 0.001f ? haciaPies.normalized : Vector3.right;
            var pos = ojo + Vector3.up * 0.26f + haciaPies * 0.22f;
            return Encuadrar(pos, ojo, "cuerpo" + n + " (" + cuerpo.sharedMaterial?.name + ")", true);
        }

        static bool Encuadrar(Vector3 pos, Vector3 mira, string nombre, bool conLuz)
        {
            if (camPrincipal == null) camPrincipal = Camera.main;
            if (camPrincipal == null) return false;
            var cam = camPrincipal.transform;
            var raiz = cam.root;
            var cc = raiz.GetComponentInChildren<CharacterController>();
            if (cc != null) cc.enabled = false;
            raiz.position += pos - cam.position;
            cam.LookAt(mira);
            if (conLuz && LuzPruebaLumenes.Value > 0)
            {
                if (luzPrueba == null)
                {
                    var go = new GameObject("LuzPruebaRealista");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    luzPrueba = go.AddComponent<Light>();
                    luzPrueba.type = LightType.Spot;
                    var hd = go.AddComponent<HDAdditionalLightData>();
                    hd.SetIntensity(LuzPruebaLumenes.Value, LightUnit.Lumen);
                    hd.SetRange(4f);
                    hd.SetSpotAngle(60f);
                    hd.EnableShadows(true);
                }
                luzPrueba.transform.position = pos + cam.right * 0.35f + Vector3.up * 0.1f;
                luzPrueba.transform.LookAt(mira);
                luzPrueba.enabled = true;
            }
            Plugin.Log.LogInfo($"Pruebas: vista {nombre} en {pos} mirando {mira}");
            return true;
        }

        static Renderer rendVista;
        static void DiagMaterialVista(string cuando)
        {
            if (rendVista == null) return;
            foreach (var m in rendVista.sharedMaterials)
            {
                if (m == null) continue;
                string v = "";
                foreach (var p in new[] { "_SmoothnessRemapMin", "_SmoothnessRemapMax", "_Smoothness", "_NormalScale" })
                    if (m.HasProperty(p)) v += $" {p}={m.GetFloat(p):0.00}";
                Plugin.Log.LogInfo($"DIAG vista {cuando}: {rendVista.name} mat '{m.name}' id {m.GetInstanceID()}{v} claves=[{string.Join(" ", m.shaderKeywords)}]");
            }
        }

        // mueve al jugador (controlador MFPP del juego) para que su cámara quede en pos mirando a centro
        static void MoverCamara(Vector3 pos, Vector3 centro)
        {
            var jug = UnityEngine.Object.FindObjectOfType<MFPP.Player>();
            if (jug == null || Camera.main == null) return;
            var cc = jug.GetComponent<CharacterController>(); if (cc != null) cc.enabled = false;
            jug.transform.position += pos - Camera.main.transform.position;
            if (cc != null) cc.enabled = true;
            var dir = (centro - pos).normalized;
            var ang = new Vector2(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Asin(dir.y) * Mathf.Rad2Deg);
            jug.TargetLookAngles = ang; jug.LookAngles = ang;
        }

        static bool PrepararVista(string material)
        {
            if (material.StartsWith("cuerpo") && int.TryParse(material.Substring(6), out int nc)) return PrepararCuerpo(nc);
            if (luzPrueba != null) luzPrueba.enabled = false;
            Renderer objetivo = null;
            float mejorTam = float.MaxValue;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                bool porObjeto = material.StartsWith("obj:");   // vista obj:NOMBRE = por nombre del objeto
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || (porObjeto ? r.name != material.Substring(4) : !m.name.Contains(material))) continue;
                    float tam = r.bounds.size.magnitude;
                    if (tam > 0.3f && tam < mejorTam) { mejorTam = tam; objetivo = r; }
                }
            }
            if (objetivo == null) { Plugin.Log.LogInfo("Pruebas: no encontré '" + material + "'"); return false; }
            rendVista = objetivo;
            var b = objetivo.bounds;
            Vector3 centro = b.center;
            float mejorDist = -1f; Vector3 mejorDir = Vector3.forward;
            for (int i = 0; i < 8; i++)
            {
                var dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                var origen = centro + Vector3.up * 0.7f;
                float d = Physics.Raycast(origen, dir, out var hit, 4f) ? hit.distance : 4f;
                if (d > mejorDist) { mejorDist = d; mejorDir = dir; }
            }
            float dist = Mathf.Clamp(mejorDist * 0.85f, 1.0f, 3.2f);
            var pos = centro + Vector3.up * 0.9f + mejorDir * dist;
            if (MoverJugador.Value && Camera.main != null)
            {
                var cam = Camera.main.transform;
                var raiz = cam.root;
                var cc = raiz.GetComponentInChildren<CharacterController>();
                if (cc != null) cc.enabled = false;
                var plano = centro - pos; plano.y = 0;
                if (plano.sqrMagnitude > 0.01f) raiz.rotation = Quaternion.LookRotation(plano.normalized);
                raiz.position += pos - cam.position;
                cam.LookAt(centro);
                if (cc != null) cc.enabled = true;
                Plugin.Log.LogInfo($"Pruebas: jugador movido a {cam.position} mirando {centro} (objeto {objetivo.name}, tamaño {mejorTam:0.0} m)");
                return true;
            }
            if (camPrincipal == null) camPrincipal = Camera.main;
            if (camPrueba == null)
            {
                var go = new GameObject("CamPruebaRealista");
                UnityEngine.Object.DontDestroyOnLoad(go);
                camPrueba = go.AddComponent<Camera>();
                go.AddComponent<HDAdditionalCameraData>();
            }
            if (camPrincipal != null)
            {
                camPrueba.fieldOfView = camPrincipal.fieldOfView;
                camPrueba.cullingMask = camPrincipal.cullingMask;
                camPrincipal.enabled = false;
            }
            camPrueba.nearClipPlane = 0.03f;
            camPrueba.transform.position = pos;
            camPrueba.transform.LookAt(centro);
            camPrueba.enabled = true;
            Plugin.Log.LogInfo($"Pruebas: vista '{material}' en {pos} mirando {centro} (objeto {objetivo.name}, tamaño {mejorTam:0.0} m)");
            return true;
        }

        static void DiagnosticarEscena()
        {
            var cam = Camera.main;
            Plugin.Log.LogInfo("DIAG: cámara " + (cam != null ? cam.transform.position + " raíz " + cam.transform.root.name : "ninguna"));
            foreach (var r in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                string mats = "";
                foreach (var m in r.sharedMaterials) if (m != null) mats += m.name + "/" + (m.shader != null ? m.shader.name : "?") + " ";
                Plugin.Log.LogInfo($"DIAG cuerpo: {r.gameObject.name} en {r.bounds.center} tamaño {r.bounds.size.magnitude:0.0} | {mats}");
            }
            int n = 0; foreach (var ls in UnityEngine.Object.FindObjectsOfType<LightSwitch>()) { n++; Plugin.Log.LogInfo("DIAG interruptor: " + ls.gameObject.name + " en " + ls.transform.position); }
            Plugin.Log.LogInfo("DIAG: " + n + " interruptores");
        }

        static void RestaurarCamara()
        {
            if (luzPrueba != null) luzPrueba.enabled = false;
            if (camPrueba != null) camPrueba.enabled = false;
            if (camPrincipal != null) camPrincipal.enabled = true;
        }

        // ---------- prueba de rendimiento ----------
        static void AplicarEstado(int e)
        {
            TexturasIA.Alternar(e != 2 && e != 4);
            Realismo.Alternar(e != 1 && e != 4);
            Runner.Actual?.PostProceso(e != 3 && e != 4);
            finEstado = Time.unscaledTime + SegundosPorEstado.Value;
            cuadrosR = 0; tiempoR = 0;
        }

        static void AvanzarRendimiento()
        {
            float restante = finEstado - Time.unscaledTime;
            if (restante < SegundosPorEstado.Value * 0.6f) { cuadrosR++; tiempoR += Time.unscaledDeltaTime; }   // ignora el inicio del estado
            if (restante > 0) return;
            Plugin.Log.LogInfo($"RENDIMIENTO [{nombresEstado[estadoRend]}]: {(tiempoR > 0 ? cuadrosR / tiempoR : 0):0.0} FPS");
            Capturar("R" + estadoRend + "_" + nombresEstado[estadoRend].Replace(" ", "_").Replace("(", "").Replace(")", ""));
            estadoRend++;
            if (estadoRend >= nombresEstado.Length) { estadoRend = -1; AplicarEstadoFinal(); return; }
            AplicarEstado(estadoRend == 5 ? 0 : estadoRend);
        }

        static void AplicarEstadoFinal()
        {
            TexturasIA.Alternar(true); Realismo.Alternar(true); Runner.Actual?.PostProceso(true);
            Plugin.Log.LogInfo("Pruebas: prueba de rendimiento terminada");
        }

        // ---------- botones ----------
        static void RevisarBotones()
        {
            bool listar = ListarBotones.Value;
            string objetivo = BotonAuto.Value;
            if (!listar && (string.IsNullOrEmpty(objetivo) || botonPulsado)) return;
            foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
            {
                if (b == null || !b.gameObject.activeInHierarchy || !b.interactable) continue;
                string texto = "";
                var tmp = b.GetComponentInChildren<TMPro.TMP_Text>();
                if (tmp != null) texto = tmp.text;
                else
                {
                    var t = b.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (t != null) texto = t.text;
                }
                texto = (texto ?? "").Trim();
                if (listar && botonesVistos.Add(escenaActual + "|" + texto + "|" + b.gameObject.name))
                    Plugin.Log.LogInfo("  botón: '" + texto + "' (" + b.gameObject.name + ") en " + escenaActual);
                if (!botonPulsado && !string.IsNullOrEmpty(objetivo) && string.Equals(texto, objetivo, StringComparison.OrdinalIgnoreCase))
                {
                    botonPulsado = true;
                    Plugin.Log.LogInfo("Pruebas: pulsando botón '" + texto + "'");
                    b.onClick.Invoke();
                    return;
                }
            }
        }
    }
}
