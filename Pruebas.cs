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
                if (c.StartsWith("boton "))
                {
                    string objetivo = c.Substring(6).Trim();
                    foreach (var b in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Button>())
                    {
                        if (b == null || !b.gameObject.activeInHierarchy) continue;
                        var tmp = b.GetComponentInChildren<TMPro.TMP_Text>();
                        if (tmp != null && string.Equals(tmp.text.Trim(), objetivo, StringComparison.OrdinalIgnoreCase)) { b.onClick.Invoke(); Plugin.Log.LogInfo("Comando: botón " + objetivo); break; }
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

        static bool PrepararVista(string material)
        {
            if (material.StartsWith("cuerpo") && int.TryParse(material.Substring(6), out int nc)) return PrepararCuerpo(nc);
            if (luzPrueba != null) luzPrueba.enabled = false;
            Renderer objetivo = null;
            float mejorTam = float.MaxValue;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.name.Contains(material)) continue;
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
