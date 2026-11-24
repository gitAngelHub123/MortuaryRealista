// © Angel — Menú de opciones gráficas dentro del juego (F6) y opciones nuevas: niebla volumétrica, haces de luz,
// sombras, antialiasing, distancia de dibujado, exposición automática, profundidad de campo, desenfoque de movimiento,
// distorsión de lente. Todo se aplica en vivo y se guarda en el .cfg.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MortuaryRealista
{
    internal static class Opciones
    {
        internal static ConfigEntry<int> CalidadSombras, NieblaVolumetrica, Antialiasing, CalidadSSR, CalidadSSGI;
        internal static ConfigEntry<float> HacesLuz, DistanciaDibujado, DesenfoqueMovimiento, DistorsionLente, CompensacionAuto;
        internal static ConfigEntry<bool> ExposicionAuto, Profundidad;

        static Fog niebla;
        static Exposure exposicion;
        static DepthOfField dof;
        static MotionBlur movimiento;
        static LensDistortion lente;
        static HDShadowSettings sombrasVol;

        public static void Configurar(ConfigFile cfg)
        {
            CalidadSombras = cfg.Bind("Opciones", "CalidadSombras", 0, "Resolución de sombra de cada luz: 0 = la del juego, 1 = Alta (1024), 2 = Ultra (2048)");
            NieblaVolumetrica = cfg.Bind("Opciones", "NieblaVolumetrica", 0, "Niebla volumétrica: 0 = la del juego (apagada), 1 = Ligera, 2 = Densa");
            HacesLuz = cfg.Bind("Opciones", "HacesLuz", 1f, "Intensidad de los haces de luz en la niebla (1 = como el juego, 0 = sin haces)");
            Antialiasing = cfg.Bind("Opciones", "Antialiasing", -1, "-1 = el del juego, 0 = ninguno, 1 = FXAA, 2 = SMAA, 3 = TAA");
            CalidadSSR = cfg.Bind("Opciones", "CalidadReflejos", 2, "Calidad de los reflejos en pantalla: 0 Baja, 1 Media, 2 Alta");
            CalidadSSGI = cfg.Bind("Opciones", "CalidadIluminacionGlobal", 1, "Calidad de la luz rebotada (SSGI): 0 Baja, 1 Media, 2 Alta");
            DistanciaDibujado = cfg.Bind("Opciones", "DistanciaDibujado", 1f, "Multiplicador de la distancia de dibujado y de sombras (1 = como el juego)");
            ExposicionAuto = cfg.Bind("Opciones", "ExposicionAutomatica", false, "Los ojos se adaptan a la oscuridad y a la luz (el juego usa exposición fija)");
            CompensacionAuto = cfg.Bind("Opciones", "CompensacionExposicionAuto", -2.5f, "Ajuste de la exposición automática en EV");
            Profundidad = cfg.Bind("Opciones", "ProfundidadDeCampo", false, "Desenfoque de lo muy cercano y lo lejano, como una cámara de cine");
            DesenfoqueMovimiento = cfg.Bind("Opciones", "DesenfoqueMovimiento", 0f, "Desenfoque al mover la cámara (0 = apagado, 1 = máximo)");
            DistorsionLente = cfg.Bind("Opciones", "DistorsionLente", 0f, "Curvatura de lente (0 = apagada; negativo = cojín, positivo = barril)");
        }

        public static void CrearOverrides(VolumeProfile profile)
        {
            niebla = profile.Add<Fog>(false);
            exposicion = profile.Add<Exposure>(false);
            dof = profile.Add<DepthOfField>(false);
            movimiento = profile.Add<MotionBlur>(false);
            lente = profile.Add<LensDistortion>(false);
            sombrasVol = profile.Add<HDShadowSettings>(false);
        }

        // ---------- aplicar todo en vivo ----------
        public static void AplicarTodo()
        {
            try
            {
                Runner.Actual?.AplicarPublico();
                Realismo.Aplicar();
                Realismo.AplicarFrame();
                AplicarVolumen();
                AplicarLucesYCamara();
                Runner.Actual?.AplicarCalidadPublico();
                Materiales.Rehacer();
                if (TexturasIA.ConservarOriginales.Value) TexturasIA.Alternar(TexturasIA.Activar.Value);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Opciones: " + e); }
        }

        static float sombraBase = -1f;
        public static void AplicarVolumen()
        {
            if (niebla == null) return;
            bool post = Plugin.Activo.Value && Runner.PostEncendido;
            int nv = NieblaVolumetrica.Value;
            // el juego trae la niebla apagada: Ligera / Densa la encienden con volumen (la luz se ve atravesarla)
            niebla.active = nv > 0;
            niebla.enabled.Override(true);
            niebla.enableVolumetricFog.Override(true);
            niebla.meanFreePath.Override(nv >= 2 ? 45f : 140f);
            niebla.albedo.Override(new Color(0.82f, 0.84f, 0.86f));
            niebla.anisotropy.Override(0.6f);
            niebla.quality.Override(2);
            exposicion.active = post && ExposicionAuto.Value;
            exposicion.mode.Override(ExposureMode.Automatic);
            exposicion.meteringMode.Override(MeteringMode.CenterWeighted);
            exposicion.adaptationMode.Override(AdaptationMode.Progressive);
            exposicion.adaptationSpeedDarkToLight.Override(2.5f);
            exposicion.adaptationSpeedLightToDark.Override(0.8f);
            exposicion.limitMin.Override(-3f);
            exposicion.limitMax.Override(12f);
            exposicion.compensation.Override(CompensacionAuto.Value);
            dof.active = post && Profundidad.Value;
            dof.focusMode.Override(DepthOfFieldMode.Manual);
            dof.nearFocusStart.Override(0f); dof.nearFocusEnd.Override(0.35f);
            dof.farFocusStart.Override(3.5f); dof.farFocusEnd.Override(12f);
            dof.quality.Override(1);
            movimiento.active = post && DesenfoqueMovimiento.Value > 0f;
            movimiento.intensity.Override(DesenfoqueMovimiento.Value);
            movimiento.quality.Override(1);
            lente.active = post && Math.Abs(DistorsionLente.Value) > 0.001f;
            lente.intensity.Override(DistorsionLente.Value);
            // distancia de sombras: se parte del valor del juego
            if (sombraBase < 0f && Camera.main != null)
            {
                var st = VolumeManager.instance.stack.GetComponent<HDShadowSettings>();
                if (st != null) sombraBase = st.maxShadowDistance.value;
            }
            sombrasVol.active = sombraBase > 0f && Math.Abs(DistanciaDibujado.Value - 1f) > 0.01f;
            if (sombraBase > 0f) sombrasVol.maxShadowDistance.Override(sombraBase * DistanciaDibujado.Value);
        }

        // ---------- luces y cámara (se reaplica en cada escena) ----------
        class LuzOriginal { public IntScalableSettingValue Res; public float Dimmer; }
        static readonly Dictionary<int, LuzOriginal> luces = new Dictionary<int, LuzOriginal>();
        static readonly Dictionary<int, float> lejanias = new Dictionary<int, float>();
        static readonly Dictionary<int, HDAdditionalCameraData.AntialiasingMode> aaOriginal = new Dictionary<int, HDAdditionalCameraData.AntialiasingMode>();

        public static void AplicarLucesYCamara()
        {
            int n = 0;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<HDAdditionalLightData>())
            {
                if (l == null) continue;
                int id = l.GetInstanceID();
                if (!luces.TryGetValue(id, out var o))
                {
                    var r = l.shadowResolution;
                    o = new LuzOriginal { Res = new IntScalableSettingValue { useOverride = r.useOverride, @override = r.@override, level = r.level }, Dimmer = l.volumetricDimmer };
                    luces[id] = o;
                }
                if (CalidadSombras.Value > 0)
                {
                    l.SetShadowResolutionOverride(true);
                    l.SetShadowResolution(CalidadSombras.Value >= 2 ? 2048 : 1024);
                }
                else
                {
                    l.SetShadowResolutionOverride(o.Res.useOverride);
                    if (o.Res.useOverride) l.SetShadowResolution(o.Res.@override); else l.SetShadowResolutionLevel(o.Res.level);
                }
                // varias lámparas del juego traen el aporte a la niebla en 0: se parte de 1 para que los haces aparezcan
                float baseD = o.Dimmer > 0.01f ? o.Dimmer : 1f;
                l.volumetricDimmer = Math.Abs(HacesLuz.Value - 1f) < 0.01f ? o.Dimmer : Mathf.Clamp(baseD * HacesLuz.Value, 0f, 16f);
                n++;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                int cid = cam.GetInstanceID();
                if (!lejanias.ContainsKey(cid)) lejanias[cid] = cam.farClipPlane;
                cam.farClipPlane = lejanias[cid] * Mathf.Max(1f, DistanciaDibujado.Value);
                var hd = cam.GetComponent<HDAdditionalCameraData>();
                if (hd != null)
                {
                    if (!aaOriginal.ContainsKey(cid)) aaOriginal[cid] = hd.antialiasing;
                    int aa = Antialiasing.Value;
                    hd.antialiasing = aa < 0 ? aaOriginal[cid] :
                        aa == 0 ? HDAdditionalCameraData.AntialiasingMode.None :
                        aa == 1 ? HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing :
                        aa == 2 ? HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing :
                                  HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                }
            }
            if (Materiales.Diagnostico.Value) Plugin.Log.LogInfo("Opciones: " + n + " luces y cámara ajustadas");
        }

        static int firmaEscena = int.MinValue; static float proxima = -1f;
        public static void Tick()
        {
            try
            {
                var activa = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                int firma = UnityEngine.SceneManagement.SceneManager.sceneCount * 7919 + activa.handle;
                if (firma != firmaEscena) { firmaEscena = firma; proxima = Time.unscaledTime + 3.5f; }
                if (proxima > 0 && Time.unscaledTime >= proxima) { proxima = -1f; AplicarLucesYCamara(); AplicarVolumen(); }
                if (abierto) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Opciones.Tick: " + e.Message); proxima = -1f; }
        }

        // ---------- presets ----------
        // valores de cada preset (los usa Preset y PresetActual)
        static List<(ConfigEntryBase, object)> ValoresPreset(string nombre)
        {
            bool original = nombre == "original", maximo = nombre == "maximo";
            return new List<(ConfigEntryBase, object)>
            {
                (TexturasIA.Activar, !original), (Realismo.Activar, !original), (Materiales.Activar, !original), (Plugin.Activo, !original),
                (Realismo.SSR, !original), (CalidadSSR, 2), (Realismo.SSGI, false), (CalidadSSGI, maximo ? 2 : 1),
                (CalidadSombras, maximo ? 2 : 0), (NieblaVolumetrica, maximo ? 2 : 0), (HacesLuz, maximo ? 1.4f : 1f),
                (Antialiasing, maximo ? 3 : -1), (Realismo.CalidadAO, original ? -1 : 2), (Plugin.SesgoLod, original ? -1f : maximo ? 3f : 2f),
                (DistanciaDibujado, maximo ? 2f : 1f), (Realismo.MuestrasPiel, original ? 0 : maximo ? 160 : 80),
                (Realismo.Tonemapping, original ? 0 : maximo ? 2 : 1), (Realismo.LuzInterior, !original),
                (Materiales.Pasto, !original), (Materiales.PastoCapas, maximo ? 2f : 1.6f), (Materiales.Relieve, !original),
                (Materiales.Brillo, !original), (Materiales.Piel, !original), (ExposicionAuto, false), (Profundidad, false),
                (DesenfoqueMovimiento, maximo ? 0.25f : 0f), (DistorsionLente, 0f),
            };
        }
        public static void Preset(string nombre)
        {
            foreach (var (e, v) in ValoresPreset(nombre)) e.BoxedValue = v;
            Plugin.Instancia.Config.Save();
            AplicarTodo();
            Plugin.Log.LogInfo("Preset aplicado: " + nombre);
        }
        // nombre del preset que coincide con la configuración actual (null si ninguno)
        public static string PresetActual()
        {
            foreach (var n in new[] { "original", "equilibrado", "maximo" })
            {
                bool igual = true;
                foreach (var (e, v) in ValoresPreset(n))
                {
                    object a = e.BoxedValue;
                    if (a is float fa && v is float fv) { if (Math.Abs(fa - fv) > 0.001f) { igual = false; break; } }
                    else if (!Equals(a, v)) { igual = false; break; }
                }
                if (igual) return n;
            }
            return null;
        }

        // ---------- interfaz ----------
        static bool abierto;
        static float escalaAntes = 1f; static CursorLockMode bloqueoAntes; static bool cursorAntes;
        static int pestaña;
        static GUIStyle sFondo, sTitulo, sSeccion, sEtiqueta, sAyuda, sBoton, sBotonSel, sPestaña, sPestañaSel, sNoDisp;
        static readonly string[] pestañas = { "Principal", "Avanzado", "Cine y color", "No disponible" };

        internal static bool Abierto => abierto;
        internal static void PestañaPrueba(int i) { pestaña = Math.Clamp(i, 0, pestañas.Length - 1); scroll = 0; }
        internal static void AbrirCerrar()
        {
            abierto = !abierto;
            if (abierto)
            {
                escalaAntes = Time.timeScale; bloqueoAntes = Cursor.lockState; cursorAntes = Cursor.visible;
                Time.timeScale = 0f;
            }
            else
            {
                Time.timeScale = escalaAntes; Cursor.lockState = bloqueoAntes; Cursor.visible = cursorAntes;
                Plugin.Instancia.Config.Save();
            }
            Plugin.Log.LogInfo("Menú de opciones " + (abierto ? "abierto" : "cerrado"));
        }

        static Texture2D Color1(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); t.hideFlags = HideFlags.HideAndDontSave; return t; }
        static void Estilos()
        {
            if (sFondo != null) return;
            sFondo = new GUIStyle(GUI.skin.box); sFondo.normal.background = Color1(new Color(0.06f, 0.07f, 0.08f, 0.94f));
            sTitulo = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold }; sTitulo.normal.textColor = new Color(0.86f, 0.25f, 0.22f);
            sSeccion = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold }; sSeccion.normal.textColor = new Color(0.92f, 0.86f, 0.7f);
            sEtiqueta = new GUIStyle(GUI.skin.label) { fontSize = 16 }; sEtiqueta.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            sAyuda = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true }; sAyuda.normal.textColor = new Color(0.6f, 0.63f, 0.66f);
            sNoDisp = new GUIStyle(sAyuda) { fontSize = 14 }; sNoDisp.normal.textColor = new Color(0.75f, 0.6f, 0.55f);
            sBoton = new GUIStyle(GUI.skin.button) { fontSize = 14 };
            sBoton.normal.background = Color1(new Color(0.17f, 0.18f, 0.2f)); sBoton.hover.background = Color1(new Color(0.25f, 0.26f, 0.29f));
            sBoton.normal.textColor = sBoton.hover.textColor = new Color(0.85f, 0.85f, 0.85f);
            sBotonSel = new GUIStyle(sBoton) { fontStyle = FontStyle.Bold };
            sBotonSel.normal.background = sBotonSel.hover.background = Color1(new Color(0.62f, 0.16f, 0.14f));
            sBotonSel.normal.textColor = sBotonSel.hover.textColor = Color.white;
            sPestaña = new GUIStyle(sBoton) { fontSize = 16 }; sPestañaSel = new GUIStyle(sBotonSel) { fontSize = 16 };
        }

        // ----- disposición manual (el juego no trae GUILayout, sliders ni scroll de IMGUI) -----
        static float y, anchoCont, alturaCont, scroll;
        static bool cambio;
        const float COL = 340f, FILA = 30f;

        static void Texto(float x, float alto, float ancho, string t, GUIStyle st) { if (y + alto > -40 && y < alturaCont + 40) GUI.Label(new Rect(x, y, ancho, alto), t, st); }
        static void Ayuda(string t)
        {
            if (string.IsNullOrEmpty(t)) { y += 6; return; }
            int lineas = 1 + t.Length / 125;
            Texto(0, 22 * lineas, anchoCont, t, sAyuda); y += 22 * lineas + 8;
        }
        static bool Visible => y + FILA > 0 && y < alturaCont;
        static bool Boton(float x, float ancho, string t, bool sel) => Visible && GUI.Button(new Rect(x, y, ancho, FILA - 2), t, sel ? sBotonSel : sBoton);

        static void Interruptor(string nombre, ConfigEntry<bool> e, string ayuda)
        {
            Texto(0, FILA, COL, nombre, sEtiqueta);
            if (Boton(COL, 130, "Activado", e.Value) && !e.Value) { e.Value = true; cambio = true; }
            if (Boton(COL + 136, 130, "Desactivado", !e.Value) && e.Value) { e.Value = false; cambio = true; }
            y += FILA + 2; Ayuda(ayuda);
        }

        static void Selector(string nombre, ConfigEntry<int> e, string[] textos, int[] valores, string ayuda)
        {
            Texto(0, FILA, COL, nombre, sEtiqueta);
            float an = Math.Max(90f, Math.Min(130f, (anchoCont - COL - 10) / textos.Length - 6));
            for (int i = 0; i < textos.Length; i++)
                if (Boton(COL + i * (an + 6), an, textos[i], e.Value == valores[i]) && e.Value != valores[i]) { e.Value = valores[i]; cambio = true; }
            y += FILA + 2; Ayuda(ayuda);
        }

        static void Deslizador(string nombre, ConfigEntry<float> e, float min, float max, string formato, string ayuda)
        {
            Texto(0, FILA, COL, nombre, sEtiqueta);
            float paso = (max - min) / 20f;
            if (Boton(COL, 34, "−", false)) { e.Value = (float)Math.Round(Math.Max(min, e.Value - paso), 2); cambio = true; }
            float bx = COL + 40, bw = 360;
            if (Visible)
            {
                GUI.Box(new Rect(bx, y + 7, bw, FILA - 16), "", sBarra);
                float t = Mathf.Clamp01((e.Value - min) / (max - min));
                GUI.Box(new Rect(bx, y + 7, Math.Max(4, bw * t), FILA - 16), "", sBarraLlena);
                if (GUI.Button(new Rect(bx, y, bw, FILA - 2), "", sInvisible))
                {
                    float mx = Event.current.mousePosition.x;
                    e.Value = (float)Math.Round(min + Mathf.Clamp01((mx - bx) / bw) * (max - min), 2); cambio = true;
                }
            }
            if (Boton(bx + bw + 6, 34, "+", false)) { e.Value = (float)Math.Round(Math.Min(max, e.Value + paso), 2); cambio = true; }
            Texto(bx + bw + 48, FILA, 90, e.Value.ToString(formato), sEtiqueta);
            y += FILA + 2; Ayuda(ayuda);
        }

        static void NoDisponible(string nombre, string motivo)
        {
            Texto(0, FILA, COL, nombre, sEtiqueta);
            Texto(COL, 40, anchoCont - COL, motivo, sNoDisp);
            y += 46;
        }

        static void Seccion(string t) { Texto(0, 28, anchoCont, t, sSeccion); y += 34; }

        static GUIStyle sBarra, sBarraLlena, sInvisible;

        public static void Dibujar()
        {
            if (!abierto) return;
            Estilos();
            if (sBarra == null)
            {
                sBarra = new GUIStyle(); sBarra.normal.background = Color1(new Color(0.2f, 0.21f, 0.23f));
                sBarraLlena = new GUIStyle(); sBarraLlena.normal.background = Color1(new Color(0.62f, 0.16f, 0.14f));
                sInvisible = new GUIStyle();
            }
            float w = Math.Min(1100, Screen.width - 40), h = Math.Min(900, Screen.height - 40);
            var r = new Rect((Screen.width - w) / 2, (Screen.height - h) / 2, w, h);
            GUI.Box(r, "", sFondo);
            float x0 = r.x + 26, ancho = w - 52;
            GUI.Label(new Rect(x0, r.y + 14, ancho, 36), "MORTUARY REALISTA — Opciones gráficas", sTitulo);
            if (GUI.Button(new Rect(r.xMax - 26 - 140, r.y + 16, 140, 32), "Cerrar (F6)", sBoton)) { AbrirCerrar(); return; }
            float yp = r.y + 62;
            GUI.Label(new Rect(x0, yp, 80, 30), "Preset:", sSeccion);
            if (GUI.Button(new Rect(x0 + 84, yp, 190, 30), "Juego original", sBoton)) Preset("original");
            if (GUI.Button(new Rect(x0 + 280, yp, 250, 30), "Equilibrado (recomendado)", sBoton)) Preset("equilibrado");
            if (GUI.Button(new Rect(x0 + 536, yp, 280, 30), "Terror hiperrealista (máximo)", sBoton)) Preset("maximo");
            yp += 44;
            for (int i = 0; i < pestañas.Length; i++)
                if (GUI.Button(new Rect(x0 + i * 212, yp, 206, 36), pestañas[i], i == pestaña ? sPestañaSel : sPestaña)) { pestaña = i; scroll = 0; }
            yp += 48;
            anchoCont = ancho; alturaCont = r.yMax - 40 - yp;
            var ev = Event.current;
            if (ev != null && ev.type == EventType.ScrollWheel) scroll = Mathf.Clamp(scroll - ev.delta.y * 30f, -3000f, 0f);
            GUI.BeginGroup(new Rect(x0, yp, ancho, alturaCont));
            y = scroll; cambio = false;
            switch (pestaña)
            {
                case 0: Principal(); break;
                case 1: Avanzado(); break;
                case 2: Cine(); break;
                default: NoDisp(); break;
            }
            float alto = y - scroll;
            GUI.EndGroup();
            if (scroll < -(alto - alturaCont)) scroll = Math.Min(0, -(alto - alturaCont));
            GUI.Label(new Rect(x0, r.yMax - 32, ancho, 24), "Rueda del mouse: desplazar.   Los cambios se aplican al instante y se guardan.   FPS: " + Realismo.FpsActual.ToString("0"), sAyuda);
            if (cambio) AplicarTodo();
        }

        static readonly string[] JAM = { "Juego", "Alta", "Ultra" };
        static readonly int[] V012 = { 0, 1, 2 };
        static readonly string[] BMA = { "Baja", "Media", "Alta" };

        static void Principal()
        {
            Seccion("Configuración gráfica principal");
            Interruptor("Texturas IA 4K", TexturasIA.Activar, "Paredes, suelos, piel, ropa, madera y objetos escalados ×4 con IA (hasta 4096 px).");
            Interruptor("Iluminación global (luz rebotada)", Realismo.SSGI, "Simula cómo rebota la luz en paredes y suelos. Cuesta FPS. (Una sola opción: incluye 'Global Illumination' de la lista avanzada.)");
            Selector("   Calidad de iluminación global", CalidadSSGI, BMA, V012, "");
            Selector("Sombras", CalidadSombras, JAM, V012, "Resolución de la sombra de cada luz: Juego = como viene; Alta = 1024; Ultra = 2048.");
            Interruptor("Reflejos en pantalla", Realismo.SSR, "Reflejos en charcos, ventanas, metal y suelos (el juego los traía apagados). Incluye 'Screen Space Reflections'.");
            Selector("   Calidad de reflejos", CalidadSSR, BMA, V012, "");
            Selector("Niebla volumétrica", NieblaVolumetrica, new[] { "Juego", "Ligera", "Densa" }, V012, "Niebla en el aire por la que se ve pasar la luz de lámparas y linterna (el juego no trae).");
            Deslizador("Haces de luz volumétricos", HacesLuz, 0f, 3f, "0.00", "Cuánto se ven los haces de las lámparas en la niebla (1 = normal). Necesita niebla Ligera o Densa. Incluye 'Volumetric Lighting'.");
        }

        static void Avanzado()
        {
            Seccion("Opciones gráficas avanzadas");
            Selector("Anti-aliasing", Antialiasing, new[] { "Juego", "Ninguno", "FXAA", "SMAA", "TAA" }, new[] { -1, 0, 1, 2, 3 }, "Suaviza los bordes. TAA es el más limpio en movimiento.");
            Interruptor("Filtrado anisotrópico 16x", Plugin.Aniso, "Texturas nítidas en suelos y paredes vistos en ángulo.");
            Selector("Oclusión ambiental", Realismo.CalidadAO, new[] { "Juego", "Baja", "Media", "Alta" }, new[] { -1, 0, 1, 2 }, "Oscurece rincones y uniones (es la técnica GTAO de HDRP).");
            Deslizador("Nivel de detalle (LOD)", Plugin.SesgoLod, 1f, 4f, "0.0", "Modelos detallados a más distancia (1 = como el juego).");
            Deslizador("Distancia de dibujado y sombras", DistanciaDibujado, 1f, 3f, "0.0", "Multiplica cuán lejos se dibujan objetos y sombras.");
            Selector("Dispersión en la piel (SSS)", Realismo.MuestrasPiel, new[] { "Juego", "Alta", "Ultra", "Máxima" }, new[] { 0, 40, 80, 160 }, "Luz que atraviesa la piel: muestras por píxel.");
            Interruptor("Detalle de poros en la piel", Materiales.Piel, "Refuerza el microrelieve de la piel en todos los cuerpos.");
            Interruptor("Relieve de superficies", Materiales.Relieve, "Refuerza el relieve de paredes, suelos, piedra y madera.");
            Deslizador("   Fuerza del relieve", Materiales.RelieveFactor, 1f, 4f, "0.0", "");
            Interruptor("Brillo por tipo de material", Materiales.Brillo, "Azulejos y cerámica más brillantes, vidrio pulido, metal más pulido, tela mate.");
            Interruptor("Sombras de contacto", Realismo.SombrasContacto, "Sombras finas donde los objetos tocan superficies. OJO: en este juego dejan un tramado en zonas iluminadas.");
            Interruptor("Microsombras", Realismo.MicroSombras, "Sombras diminutas a partir del relieve de las texturas.");
            Interruptor("Vegetación (pasto)", Materiales.Pasto, "Pasto más tupido y visible a más distancia.");
            Deslizador("   Densidad del pasto", Materiales.PastoCapas, 1f, 2f, "0.0", "");
        }

        static void Cine()
        {
            Seccion("Efectos cinematográficos");
            Interruptor("Efectos de color y cine", Plugin.Activo, "Interruptor general de todo lo de esta pestaña (también con F8).");
            Selector("Mapeo tonal (estilo HDR)", Realismo.Tonemapping, new[] { "Ninguno", "Neutral", "ACES (cine)" }, V012, "Recupera luces quemadas y da colores más naturales. El juego no usa ninguno.");
            Interruptor("Luz interior realista", Realismo.LuzInterior, "Más luz rebotada y reflejos en interiores.");
            Interruptor("Exposición automática", ExposicionAuto, "Los ojos se adaptan a la oscuridad y a la luz. Puede aclarar demasiado las zonas oscuras.");
            Deslizador("   Ajuste de exposición automática", CompensacionAuto, -2f, 2f, "0.00", "");
            Deslizador("Brillo (exposición)", Plugin.Exposicion, -1f, 1.5f, "0.00", "Más claro o más oscuro, en EV.");
            Deslizador("Contraste", Plugin.Contraste, -30f, 40f, "0", "");
            Deslizador("Saturación", Plugin.Saturacion, -50f, 30f, "0", "");
            Interruptor("Profundidad de campo", Profundidad, "Desenfoca lo muy cercano y lo lejano, como una cámara de cine. Puede molestar al leer.");
            Deslizador("Desenfoque de movimiento", DesenfoqueMovimiento, 0f, 1f, "0.00", "0 = apagado. Solo se ve al girar la cámara rápido.");
            Deslizador("Grano de película", Plugin.Grano, 0f, 1f, "0.00", "0 = apagado.");
            Deslizador("Aberración cromática", Plugin.Aberracion, 0f, 1f, "0.00", "Franjas de color en los bordes. 0 = apagada.");
            Deslizador("Distorsión de lente", DistorsionLente, -0.5f, 0.5f, "0.00", "0 = apagada.");
            Deslizador("Viñeteo", Plugin.Viñeteo, 0f, 0.6f, "0.00", "Oscurece los bordes de la pantalla.");
        }

        static void NoDisp()
        {
            Seccion("Opciones de la lista que este juego no permite");
            Ayuda("El juego corre en DirectX 11 (bajo Proton) con HDRP de Unity 2021.2. Esto deja fuera lo siguiente; no se muestran interruptores que no harían nada.");
            NoDisponible("Ray tracing / Path tracing / RTAO", "Requiere DirectX 12 y un juego compilado con ray tracing. El juego usa DirectX 11.");
            NoDisponible("DLSS / DLAA", "El juego trae DLSS, pero bajo Proton deja bordes punteados y no sube los FPS (probado 2026-11-17).");
            NoDisponible("HDR de pantalla", "Unity 2021.2 bajo Proton en Linux no entrega salida HDR. Se ofrece mapeo tonal Neutral/ACES.");
            NoDisponible("Teselado", "Necesita el shader 'LitTessellation', que el juego no incluye.");
            NoDisponible("Parallax Occlusion Mapping", "El juego no trae mapas de altura ni la variante del shader con POM.");
            NoDisponible("Microdetalle general", "Probado: el juego no trae compilada la variante del shader con mapa de detalle para paredes y suelos.");
            NoDisponible("Sombras en pantalla (Screen Space Shadows)", "En HDRP 12 solo existen con ray tracing.");
            NoDisponible("Refracción en pantalla", "Los vidrios del juego no usan modelo de refracción.");
            NoDisponible("Calidad de partículas / agua", "El juego no tiene ajustes de partículas ni superficies de agua.");
            NoDisponible("Clima dinámico / superficies mojadas", "La lluvia y el suelo mojado los controla el juego; no hay sistema que ajustar.");
            NoDisponible("Animaciones faciales / física", "No son opciones gráficas: dependen del código y las animaciones del juego.");
        }
    }
}
