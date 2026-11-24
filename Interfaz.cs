// © Angel — Opciones del mod dentro de la pantalla OPCIONES del propio juego.
// Agrega la pestaña "MOD GRÁFICO" junto a SETTINGS / CONTROLS y arma su contenido clonando las casillas,
// listas, barras y botones del juego (mismos sprites, fuentes, colores y marco de selección).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MortuaryRealista
{
    internal static class Interfaz
    {
        internal static ConfigEntry<bool> VolcarJerarquia, PruebaCapturas;

        public static void Configurar(ConfigFile cfg)
        {
            VolcarJerarquia = cfg.Bind("Pruebas", "VolcarJerarquiaOpciones", false, "Escribe la jerarquía de la pantalla de opciones del juego en Capturas/jerarquia_opciones.txt");
            PruebaCapturas = cfg.Bind("Pruebas", "CapturasPestañaMod", false, "Abre Opciones → MOD GRÁFICO en el menú principal y saca capturas recorriéndola");
        }

        // ---------------- definición de las opciones ----------------
        enum Tipo { Seccion, Casilla, Lista, Barra, Presets, Texto }
        class Def
        {
            public Tipo T; public string Nombre, Ayuda;
            public ConfigEntryBase E; public string[] Textos; public int[] Valores;
            public float Min, Max; public string Formato;
        }
        static Def Sec(string n) => new Def { T = Tipo.Seccion, Nombre = n };
        static Def Cas(string n, ConfigEntry<bool> e, string a) => new Def { T = Tipo.Casilla, Nombre = n, E = e, Ayuda = a };
        static Def Lis(string n, ConfigEntry<int> e, string[] t, int[] v, string a) => new Def { T = Tipo.Lista, Nombre = n, E = e, Textos = t, Valores = v, Ayuda = a };
        static Def Bar(string n, ConfigEntry<float> e, float min, float max, string f, string a) => new Def { T = Tipo.Barra, Nombre = n, E = e, Min = min, Max = max, Formato = f, Ayuda = a };
        static readonly int[] V012 = { 0, 1, 2 };

        // texto según el idioma del juego (español si el juego está en español; inglés en los demás)
        static string T(string es, string en) => Idioma.Es ? es : en;
        static string[] Tl(string[] es, string[] en) => Idioma.Es ? es : en;
        static string[] BMA => Tl(new[] { "BAJA", "MEDIA", "ALTA" }, new[] { "LOW", "MEDIUM", "HIGH" });
        static string JUEGO => T("JUEGO", "GAME");

        static List<Def> Definiciones() => new List<Def>
        {
            Sec(T("AJUSTES RÁPIDOS", "QUICK PRESETS")),
            new Def { T = Tipo.Presets, Ayuda = T("Cambia todas las opciones del mod de una vez. El ajuste que coincide con tu configuración actual queda marcado en rojo.",
                                                  "Changes every mod option at once. The preset that matches your current settings is highlighted in red.") },

            Sec(T("PRINCIPAL", "MAIN")),
            Lis(T("CALIDAD DE LUZ REBOTADA", "BOUNCE LIGHT QUALITY"), CalidadSSGI(), BMA, V012, T("Calidad de la iluminación global (necesita la casilla ILUMINACIÓN GLOBAL).", "Global illumination quality (needs the GLOBAL ILLUMINATION checkbox).")),
            Lis(T("SOMBRAS", "SHADOWS"), Opciones.CalidadSombras, new[] { JUEGO, T("ALTA", "HIGH"), "ULTRA" }, V012, T("Resolución de la sombra de cada luz: Juego = como viene; Alta = 1024; Ultra = 2048.", "Shadow resolution of each light: Game = default; High = 1024; Ultra = 2048.")),
            Lis(T("CALIDAD DE REFLEJOS", "REFLECTION QUALITY"), Opciones.CalidadSSR, BMA, V012, T("Calidad de los reflejos en pantalla (necesita la casilla REFLEJOS EN PANTALLA).", "Screen-space reflection quality (needs the SCREEN REFLECTIONS checkbox).")),
            Lis(T("NIEBLA DEL MOD", "MOD FOG"), Opciones.NieblaVolumetrica, Tl(new[] { "NINGUNA", "LIGERA", "DENSA" }, new[] { "NONE", "LIGHT", "DENSE" }), V012, T("Niebla en el aire por la que se ve pasar la luz de lámparas y linterna (el juego no trae).", "Fog in the air that lamp and flashlight beams shine through (the game has none).")),
            Bar(T("LUZ DEL PORTAPAPELES", "CLIPBOARD LIGHT"), Portapapeles.Lumenes, 0.1f, 10f, "0.0", T("Cuánto brilla el papel del portapapeles mientras lo sostienes.", "How much the clipboard paper glows while you hold it.")),
            Bar(T("HACES DE LUZ", "LIGHT SHAFTS"), Opciones.HacesLuz, 0f, 3f, "0.00", T("Cuánto se ven los haces de las lámparas en la niebla (1 = normal). Necesita NIEBLA DEL MOD ligera o densa.", "How visible lamp beams are in the fog (1 = normal). Needs MOD FOG light or dense.")),
            Cas(T("TEXTURAS IA 4K", "AI 4K TEXTURES"), TexturasIA.Activar, T("Paredes, suelos, piel, ropa, madera y objetos escalados con IA (hasta 4096 px). Necesita el pack de texturas.", "Walls, floors, skin, clothes, wood and props upscaled with AI (up to 4096 px). Needs the texture pack.")),
            Cas(T("ILUMINACIÓN GLOBAL", "GLOBAL ILLUMINATION"), Realismo.SSGI, T("Luz rebotada en paredes y suelos. En este juego deja puntitos brillantes que parpadean: mejor apagada.", "Bounce light on walls and floors. In this game it leaves flickering bright dots: better left off.")),
            Cas(T("REFLEJOS EN PANTALLA", "SCREEN REFLECTIONS"), Realismo.SSR, T("Reflejos en charcos, ventanas, metal y suelos (el juego los traía apagados).", "Reflections on puddles, windows, metal and floors (off in the base game).")),
            Cas(T("FILTRO DE RUIDO", "NOISE FILTER"), Realismo.SSGIFiltro, T("Reduce los puntitos brillantes que deja la luz rebotada en paredes y pilares.", "Reduces the bright dots bounce light leaves on walls and pillars.")),
            Cas(T("LUZ DEL PORTAPAPELES", "CLIPBOARD LIGHT"), Portapapeles.Activar, T("El papel del portapapeles brilla un poco mientras lo sostienes, para leerlo aunque esté oscuro.", "The clipboard paper glows slightly while you hold it, so you can read it in the dark.")),

            Sec(T("AVANZADO", "ADVANCED")),
            Lis("ANTI-ALIASING", Opciones.Antialiasing, new[] { JUEGO, T("NINGUNO", "NONE"), "FXAA", "SMAA", "TAA" }, new[] { -1, 0, 1, 2, 3 }, T("Suaviza los bordes. TAA es el más limpio en movimiento (con el portapapeles en la mano se usa SMAA).", "Smooths edges. TAA is the cleanest in motion (SMAA is used while holding the clipboard).")),
            Lis(T("OCLUSIÓN AMBIENTAL", "AMBIENT OCCLUSION"), Realismo.CalidadAO, new[] { JUEGO, T("BAJA", "LOW"), T("MEDIA", "MEDIUM"), T("ALTA", "HIGH") }, new[] { -1, 0, 1, 2 }, T("Oscurece rincones y uniones entre superficies.", "Darkens corners and contact areas between surfaces.")),
            Lis(T("DISPERSIÓN EN LA PIEL", "SKIN SCATTERING"), Realismo.MuestrasPiel, new[] { JUEGO, T("ALTA", "HIGH"), "ULTRA", T("MÁXIMA", "MAX") }, new[] { 0, 40, 80, 160 }, T("Luz que atraviesa la piel: muestras por píxel.", "Light passing through skin: samples per pixel.")),
            Bar(T("NIVEL DE DETALLE", "LEVEL OF DETAIL"), Plugin.SesgoLod, 1f, 4f, "0.0", T("Modelos detallados a más distancia (1 = como el juego).", "Detailed models at a greater distance (1 = game default).")),
            Bar(T("DISTANCIA DE DIBUJADO", "DRAW DISTANCE"), DistanciaDibujado(), 1f, 3f, "0.0", T("Multiplica cuán lejos se dibujan objetos y sombras.", "Multiplies how far objects and shadows are drawn.")),
            Bar(T("FUERZA DEL RELIEVE", "SURFACE DETAIL STRENGTH"), Materiales.RelieveFactor, 1f, 4f, "0.0", T("Cuánto se refuerza el relieve (necesita la casilla RELIEVE DE SUPERFICIES).", "How much surface relief is boosted (needs the SURFACE RELIEF checkbox).")),
            Bar(T("DENSIDAD DEL PASTO", "GRASS DENSITY"), Materiales.PastoCapas, 1f, 2f, "0.0", T("Cuánto más tupido se ve el pasto (necesita la casilla PASTO).", "How much denser the grass looks (needs the GRASS checkbox).")),
            Cas(T("FILTRADO ANISOTRÓPICO", "ANISOTROPIC FILTERING"), Plugin.Aniso, T("Texturas nítidas en suelos y paredes vistos en ángulo (16x).", "Sharp textures on floors and walls seen at an angle (16x).")),
            Cas(T("DETALLE DE PIEL", "SKIN DETAIL"), Materiales.Piel, T("Refuerza el microrelieve de la piel en todos los cuerpos.", "Boosts fine skin detail on every body.")),
            Cas(T("RELIEVE DE SUPERFICIES", "SURFACE RELIEF"), Materiales.Relieve, T("Refuerza el relieve de paredes, suelos, piedra y madera.", "Boosts the relief of walls, floors, stone and wood.")),
            Cas(T("BRILLO POR MATERIAL", "MATERIAL SHINE"), Materiales.Brillo, T("Azulejos y cerámica más brillantes, vidrio pulido, metal más pulido, tela mate.", "Shinier tiles and ceramics, polished glass and metal, matte fabric.")),
            Cas(T("MICROSOMBRAS", "MICRO SHADOWS"), Realismo.MicroSombras, T("Sombras diminutas a partir del relieve de las texturas.", "Tiny shadows from texture relief.")),
            Cas(T("PASTO", "GRASS"), Materiales.Pasto, T("Pasto más tupido y visible a más distancia.", "Denser grass, visible farther away.")),
            Cas(T("SOMBRAS DE CONTACTO", "CONTACT SHADOWS"), Realismo.SombrasContacto, T("Sombras finas donde los objetos tocan superficies. Ojo: en este juego dejan un tramado en zonas iluminadas.", "Fine shadows where objects touch surfaces. Note: in this game they leave a dotted pattern in lit areas.")),

            Sec(T("CINE Y COLOR", "CINEMATIC & COLOR")),
            Lis(T("MAPEO TONAL", "TONE MAPPING"), Realismo.Tonemapping, Tl(new[] { "NINGUNO", "NEUTRAL", "ACES" }, new[] { "NONE", "NEUTRAL", "ACES" }), V012, T("Recupera luces quemadas y da colores más naturales. El juego no usa ninguno.", "Recovers blown-out highlights and gives more natural colors. The game uses none.")),
            Bar(T("EXPOSICIÓN", "EXPOSURE"), Plugin.Exposicion, -1f, 1.5f, "0.00", T("Más claro o más oscuro, en EV (se suma al BRILLO del juego).", "Brighter or darker, in EV (added to the game's BRIGHTNESS).")),
            Bar(T("CONTRASTE", "CONTRAST"), Plugin.Contraste, -30f, 40f, "0", T("Contraste de la imagen.", "Image contrast.")),
            Bar(T("SATURACIÓN", "SATURATION"), Plugin.Saturacion, -50f, 30f, "0", T("Intensidad de los colores.", "Color intensity.")),
            Bar(T("VIÑETEO", "VIGNETTE"), Plugin.Viñeteo, 0f, 0.6f, "0.00", T("Oscurece los bordes de la pantalla.", "Darkens the screen edges.")),
            Bar(T("GRANO DE PELÍCULA", "FILM GRAIN"), Plugin.Grano, 0f, 1f, "0.00", T("0 = apagado.", "0 = off.")),
            Bar(T("ABERRACIÓN CROMÁTICA", "CHROMATIC ABERRATION"), Plugin.Aberracion, 0f, 1f, "0.00", T("Franjas de color en los bordes. 0 = apagada.", "Color fringes at the edges. 0 = off.")),
            Bar(T("DISTORSIÓN DE LENTE", "LENS DISTORTION"), DistorsionLente(), -0.5f, 0.5f, "0.00", T("Curvatura de lente. 0 = apagada.", "Lens curvature. 0 = off.")),
            Bar(T("DESENFOQUE DE MOVIMIENTO", "MOTION BLUR"), Opciones.DesenfoqueMovimiento, 0f, 1f, "0.00", T("0 = apagado. Solo se ve al girar la cámara rápido.", "0 = off. Only visible when turning the camera quickly.")),
            Bar(T("AJUSTE DE EXPOSICIÓN AUTO", "AUTO EXPOSURE OFFSET"), Opciones.CompensacionAuto, -4f, 2f, "0.00", T("Ajuste fino de la exposición automática (necesita la casilla EXPOSICIÓN AUTOMÁTICA).", "Fine-tunes auto exposure (needs the AUTO EXPOSURE checkbox).")),
            Cas(T("EFECTOS DE COLOR", "COLOR EFFECTS"), Plugin.Activo, T("Interruptor general de esta sección (también con F8).", "Master switch for this section (also F8).")),
            Cas(T("LUZ INTERIOR REALISTA", "REALISTIC INDOOR LIGHT"), Realismo.LuzInterior, T("Más luz rebotada y reflejos en interiores.", "More bounce light and reflections indoors.")),
            Cas(T("EXPOSICIÓN AUTOMÁTICA", "AUTO EXPOSURE"), Opciones.ExposicionAuto, T("Los ojos se adaptan a la oscuridad y a la luz. Puede aclarar demasiado las zonas oscuras.", "Your eyes adapt to darkness and light. May brighten dark areas too much.")),
            Cas(T("DESENFOQUE DE CINE", "CINEMATIC BLUR"), Opciones.Profundidad, T("Desenfoca lo muy cercano y lo lejano, como una cámara de cine. Puede molestar al leer.", "Blurs very near and far objects like a film camera. Can get in the way when reading.")),

            Sec(T("NO DISPONIBLE EN ESTE JUEGO", "NOT AVAILABLE IN THIS GAME")),
            new Def { T = Tipo.Texto, Nombre = T(
                "RAY TRACING Y PATH TRACING: necesitan DirectX 12; el juego usa DirectX 11.\n" +
                "DLSS: usa la opción propia del juego (en Linux con Proton deja bordes punteados).\n" +
                "HDR DE PANTALLA: el juego no lo trae (usa MAPEO TONAL).\n" +
                "TESELADO Y PARALLAX: el juego no trae esos shaders ni mapas de altura.\n" +
                "MICRODETALLE DE PAREDES: el juego no trae compilada esa variante del shader.",
                "RAY TRACING AND PATH TRACING: need DirectX 12; the game uses DirectX 11.\n" +
                "DLSS: use the game's own option (on Linux with Proton it shows dotted edges).\n" +
                "HDR OUTPUT: not supported by the game (use TONE MAPPING).\n" +
                "TESSELLATION AND PARALLAX: the game lacks those shaders and height maps.\n" +
                "WALL MICRO DETAIL: the game doesn't include that shader variant.") },
        };
        static ConfigEntry<int> CalidadSSGI() => Opciones.CalidadSSGI;
        static ConfigEntry<float> DistanciaDibujado() => Opciones.DistanciaDibujado;
        static ConfigEntry<float> DistorsionLente() => Opciones.DistorsionLente;

        // ---------------- estado de cada pantalla de opciones inyectada ----------------
        class Fila { public RectTransform Rt; public string Ayuda; public GameObject Go; }
        class Control
        {
            public Def D; public Toggle Tg; public Dropdown Dd; public Slider Sl; public TextMeshProUGUI Valor;
        }
        class Pantalla
        {
            public OptionsManager M;
            public GameObject Panel, Pagina, Contenido;
            public List<GameObject> PaginasJuego = new List<GameObject>();
            public Button Pestaña;
            public ColorBlock ColoresPestaña;
            public ScrollRect Scroll;
            public TextMeshProUGUI Ayuda;
            public List<Fila> Filas = new List<Fila>();
            public List<Control> Controles = new List<Control>();
            public Button[] Presets = new Button[3];
            public bool Activa;
        }
        static readonly List<Pantalla> pantallas = new List<Pantalla>();
        static readonly HashSet<IntPtr> vistos = new HashSet<IntPtr>();
        static float proximaBusqueda, aplicarEn = -1f;
        static bool actualizando;

        public static void Tick()
        {
            try
            {
                if (Time.unscaledTime >= proximaBusqueda)
                {
                    proximaBusqueda = Time.unscaledTime + 1.5f;
                    if (Idioma.Cambio()) Rearmar(); else Buscar();
                }
                for (int i = pantallas.Count - 1; i >= 0; i--)
                {
                    var p = pantallas[i];
                    if (p.Panel == null || p.Pagina == null) { pantallas.RemoveAt(i); continue; }
                    Vigilar(p);
                }
                if (aplicarEn > 0 && Time.unscaledTime >= aplicarEn)
                {
                    aplicarEn = -1f;
                    Plugin.Instancia.Config.Save();
                    Opciones.AplicarTodo();
                    foreach (var p in pantallas) MarcarPreset(p);
                }
                PruebaTick();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Interfaz: " + e); proximaBusqueda = Time.unscaledTime + 10f; }
        }

        // el idioma del juego cambió: se quita la pestaña y la página y se vuelven a crear en el otro idioma
        static void Rearmar()
        {
            foreach (var p in pantallas)
            {
                if (p.Pagina != null) { p.Pagina.name = "MR_Borrada"; UnityEngine.Object.Destroy(p.Pagina); }
                if (p.Pestaña != null) { p.Pestaña.gameObject.name = "MR_Borrada"; UnityEngine.Object.Destroy(p.Pestaña.gameObject); }
            }
            pantallas.Clear(); vistos.Clear();
        }

        static void Buscar()
        {
            foreach (var m in Resources.FindObjectsOfTypeAll<OptionsManager>())
            {
                if (m == null || !m.gameObject.scene.IsValid() || m.volumetricFog == null) continue;
                if (!vistos.Add(m.Pointer)) continue;
                if (VolcarJerarquia.Value) Volcar(m);
                try { Inyectar(m); }
                catch (Exception e) { Plugin.Log.LogError("Interfaz: no se pudo agregar la pestaña: " + e); }
            }
            // limpiar punteros de pantallas destruidas
            if (vistos.Count > 50) vistos.Clear();
        }

        // ---------------- construcción ----------------
        static Toggle srcCasilla; static Dropdown srcLista; static Slider srcBarra; static Button srcReset, srcBoton;
        static TextMeshProUGUI srcTitulo;

        static void Inyectar(OptionsManager m)
        {
            var settings = m.volumetricFog.transform.parent;
            var panel = settings.parent;
            if (panel.Find("MR_Pagina") != null) return;
            var p = new Pantalla { M = m, Panel = panel.gameObject };

            // piezas del juego que se clonan
            srcCasilla = m.volumetricFog;
            srcLista = m.qualityLevelDropdown;
            srcBarra = m.brightness;
            srcReset = null; srcBoton = null; srcTitulo = null;
            var pestañas = new List<Button>();
            for (int i = 0; i < panel.childCount; i++)
            {
                var hijo = panel.GetChild(i);
                var b = hijo.GetComponent<Button>();
                if (b != null && hijo.name != "MR_Borrada" && Metodos(b).Contains("SwitchTo")) pestañas.Add(b);
            }
            for (int i = 0; i < settings.childCount; i++)
            {
                var hijo = settings.GetChild(i);
                var b = hijo.GetComponent<Button>();
                if (b != null && srcReset == null && Metodos(b).Contains("Reset")) srcReset = b;
                var t = hijo.GetComponent<TextMeshProUGUI>();
                if (t != null && srcTitulo == null && hijo.name.EndsWith("LBL") && hijo.name != "framerateLBL") srcTitulo = t;
            }
            if (pestañas.Count == 0) { Plugin.Log.LogWarning("Interfaz: no encontré las pestañas en " + Ruta(panel)); return; }
            srcBoton = pestañas[pestañas.Count - 1];
            if (srcTitulo == null) srcTitulo = srcCasilla.GetComponentInChildren<TextMeshProUGUI>();

            // páginas del juego (lo que cambian las pestañas)
            foreach (var nombre in new[] { settings.name, "Controls", "GameSettingWindow" })
            {
                var t = panel.Find(nombre);
                if (t != null) p.PaginasJuego.Add(t.gameObject);
            }

            // ---- pestaña nueva y reparto de las pestañas ----
            var tabGo = Clonar(srcBoton.gameObject, panel, "MR_Pestaña");
            p.Pestaña = tabGo.GetComponent<Button>();
            p.Pestaña.onClick = new Button.ButtonClickedEvent();
            p.Pestaña.onClick.AddListener((UnityAction)(() => Mostrar(p)));
            p.ColoresPestaña = p.Pestaña.colors;
            PonerTexto(tabGo, T("MOD GRÁFICO", "GRAPHICS MOD"));
            pestañas.Add(p.Pestaña);
            var holder = panel.Find("TabHolder");
            float anchoTotal = holder != null ? holder.GetComponent<RectTransform>().sizeDelta.x + 40f : 1300f;
            float slot = Math.Min(330f, anchoTotal / pestañas.Count);
            p.Pestaña.targetGraphic.CrossFadeColor(p.ColoresPestaña.normalColor, 0f, true, true);
            float y = pestañas[0].GetComponent<RectTransform>().anchoredPosition.y;
            for (int i = 0; i < pestañas.Count; i++)
            {
                var rt = pestañas[i].GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2((i - (pestañas.Count - 1) / 2f) * slot, y);
                rt.sizeDelta = new Vector2(Math.Min(rt.sizeDelta.x, slot - 8f), rt.sizeDelta.y);
            }
            foreach (var b in pestañas) if (b != p.Pestaña) b.onClick.AddListener((UnityAction)(() => Ocultar(p)));

            // ---- página ----
            var pag = new GameObject("MR_Pagina");
            var prt = pag.AddComponent<RectTransform>();
            prt.SetParent(panel, false);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f); prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero; prt.sizeDelta = new Vector2(100, 100);
            p.Pagina = pag;

            // zona con desplazamiento: x −650..650, y −190..335
            const float VX = 1300f, VY = 525f, VCY = 72f;
            var vp = new GameObject("MR_Vista");
            var vrt = vp.AddComponent<RectTransform>(); vrt.SetParent(prt, false);
            vrt.anchorMin = vrt.anchorMax = new Vector2(0.5f, 0.5f); vrt.pivot = new Vector2(0.5f, 0.5f);
            vrt.anchoredPosition = new Vector2(-12f, VCY); vrt.sizeDelta = new Vector2(VX, VY);
            var vimg = vp.AddComponent<Image>(); vimg.color = new Color(1, 1, 1, 0.004f);
            vp.AddComponent<RectMask2D>();
            var cont = new GameObject("MR_Contenido");
            var crt = cont.AddComponent<RectTransform>(); crt.SetParent(vrt, false);
            crt.anchorMin = new Vector2(0.5f, 1f); crt.anchorMax = new Vector2(0.5f, 1f); crt.pivot = new Vector2(0.5f, 1f);
            crt.anchoredPosition = Vector2.zero;
            p.Contenido = cont;
            var sr = pag.AddComponent<ScrollRect>();
            sr.viewport = vrt; sr.content = crt; sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped; sr.scrollSensitivity = 45f; sr.inertia = false;
            p.Scroll = sr;
            sr.verticalScrollbar = CrearBarraDesplazamiento(prt, new Vector2(VX / 2f + 4f, VCY), VY);
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;

            // línea de ayuda sobre el botón inferior
            p.Ayuda = Texto(prt, "MR_Ayuda", "", 36f, new Vector2(0, -222f), new Vector2(1260f, 44f), TextAlignmentOptions.Center);
            p.Ayuda.enableAutoSizing = true; p.Ayuda.fontSizeMax = 36f; p.Ayuda.fontSizeMin = 22f; p.Ayuda.enableWordWrapping = false;
            p.Ayuda.color = new Color(0.73f, 0.69f, 0.63f, 1f);

            Construir(p, crt);
            pag.SetActive(false);
            pantallas.Add(p);
            Plugin.Log.LogInfo($"Interfaz: pestaña MOD GRÁFICO agregada en {Ruta(panel)} ({p.Controles.Count} opciones, {pestañas.Count} pestañas)");
        }

        const float COLX = 420f, ANCHO = 390f;
        static void Construir(Pantalla p, RectTransform cont)
        {
            float y = -10f;
            var defs = Definiciones();
            int col = 0; Tipo ultimo = Tipo.Seccion;
            void SaltoFila(float alto) { if (col > 0) { y -= alto; col = 0; } }
            float AltoDe(Tipo t) => t == Tipo.Casilla ? 62f : 108f;
            foreach (var d in defs)
            {
                if (d.T != ultimo && (ultimo == Tipo.Casilla || ultimo == Tipo.Lista || ultimo == Tipo.Barra) &&
                    !((ultimo == Tipo.Lista || ultimo == Tipo.Barra) && (d.T == Tipo.Lista || d.T == Tipo.Barra)))
                    SaltoFila(AltoDe(ultimo));
                switch (d.T)
                {
                    case Tipo.Seccion:
                        SaltoFila(AltoDe(ultimo));
                        if (y < -20f) y -= 14f;
                        var t = Texto(cont, "MR_Sec", d.Nombre, 50f, new Vector2(0, y - 26f), new Vector2(1240f, 52f), TextAlignmentOptions.Center);
                        t.color = new Color(1f, 0.76f, 0.35f, 1f);
                        var linea = new GameObject("MR_Linea"); var lrt = linea.AddComponent<RectTransform>(); lrt.SetParent(cont, false);
                        Centro(lrt, new Vector2(0, y - 56f), new Vector2(1180f, 3f));
                        linea.AddComponent<Image>().color = new Color(0.55f, 0.45f, 0.32f, 0.8f);
                        y -= 70f;
                        break;
                    case Tipo.Presets:
                        string[] np = { "ORIGINAL", T("EQUILIBRADO", "BALANCED"), T("MÁXIMO", "MAXIMUM") }, ip = { "original", "equilibrado", "maximo" };
                        string[] ap = { T("Juego original: el mod no cambia nada.", "Original game: the mod changes nothing."), T("Equilibrado (recomendado): todas las mejoras sin bajar mucho los FPS.", "Balanced (recommended): every improvement without a big FPS hit."), T("Máximo: sombras Ultra, niebla densa, TAA y todo el detalle. Cuesta FPS.", "Maximum: Ultra shadows, dense fog, TAA and full detail. Costs FPS.") };
                        for (int i = 0; i < 3; i++)
                        {
                            var go = Clonar(srcBoton.gameObject, cont, "MR_Preset_" + ip[i]);
                            var b = go.GetComponent<Button>();
                            b.onClick = new Button.ButtonClickedEvent();
                            string nombre = ip[i];
                            b.onClick.AddListener((UnityAction)(() => { Opciones.Preset(nombre); RefrescarTodo(); }));
                            PonerTexto(go, np[i]);
                            var rt = go.GetComponent<RectTransform>();
                            Centro(rt, new Vector2((i - 1) * COLX, y - 34f), new Vector2(330f, 60f));
                            p.Presets[i] = b;
                            p.Filas.Add(new Fila { Rt = rt, Ayuda = ap[i], Go = go });
                        }
                        y -= 80f;
                        break;
                    case Tipo.Casilla:
                    {
                        var go = Clonar(srcCasilla.gameObject, cont, "MR_" + d.E.Definition.Key);
                        var tg = go.GetComponent<Toggle>();
                        tg.onValueChanged = new Toggle.ToggleEvent();
                        tg.group = null;
                        PonerTexto(go, d.Nombre);
                        var lab = go.GetComponentInChildren<TextMeshProUGUI>(true);
                        if (lab != null) { lab.enableAutoSizing = true; lab.fontSizeMax = 38f; lab.fontSizeMin = 20f; lab.enableWordWrapping = false; }
                        var rt = go.GetComponent<RectTransform>();
                        Centro(rt, new Vector2((col - 1) * COLX + 10f, y - 31f), new Vector2(ANCHO - 20f, 60f));
                        var e = (ConfigEntry<bool>)d.E;
                        tg.SetIsOnWithoutNotify(e.Value);
                        tg.onValueChanged.AddListener((UnityAction<bool>)(v => { if (actualizando) return; e.Value = v; Cambio(); }));
                        p.Controles.Add(new Control { D = d, Tg = tg });
                        p.Filas.Add(new Fila { Rt = rt, Ayuda = d.Ayuda, Go = go });
                        if (++col == 3) { col = 0; y -= 62f; }
                        break;
                    }
                    case Tipo.Lista:
                    case Tipo.Barra:
                    {
                        var celda = new GameObject("MR_Celda_" + d.E.Definition.Key);
                        var cr = celda.AddComponent<RectTransform>(); cr.SetParent(cont, false);
                        Centro(cr, new Vector2((col - 1) * COLX, y - 54f), new Vector2(ANCHO, 106f));
                        var tit = Clonar(srcTitulo.gameObject, cr, "MR_Titulo").GetComponent<TextMeshProUGUI>();
                        tit.text = d.Nombre; tit.alignment = TextAlignmentOptions.Center;
                        tit.enableAutoSizing = true; tit.fontSizeMax = 44f; tit.fontSizeMin = 18f; tit.enableWordWrapping = false;
                        Centro(tit.rectTransform, new Vector2(0, 28f), new Vector2(ANCHO - 10f, 42f));
                        var c = new Control { D = d };
                        if (d.T == Tipo.Lista)
                        {
                            var go = Clonar(srcLista.gameObject, cr, "MR_Lista");
                            var dd = go.GetComponent<Dropdown>();
                            dd.onValueChanged = new Dropdown.DropdownEvent();
                            dd.ClearOptions();
                            var lista = new Il2CppSystem.Collections.Generic.List<string>();
                            foreach (var s in d.Textos) lista.Add(s);
                            dd.AddOptions(lista);
                            Centro(go.GetComponent<RectTransform>(), new Vector2(0, -20f), new Vector2(300f, 44f));
                            var e = (ConfigEntry<int>)d.E; var vals = d.Valores;
                            dd.SetValueWithoutNotify(Math.Max(0, Array.IndexOf(vals, e.Value)));
                            dd.onValueChanged.AddListener((UnityAction<int>)(i => { if (actualizando) return; e.Value = vals[Math.Clamp(i, 0, vals.Length - 1)]; Cambio(); }));
                            c.Dd = dd;
                        }
                        else
                        {
                            var go = Clonar(srcBarra.gameObject, cr, "MR_Barra");
                            var sl = go.GetComponent<Slider>();
                            sl.onValueChanged = new Slider.SliderEvent();
                            sl.minValue = d.Min; sl.maxValue = d.Max; sl.wholeNumbers = false;
                            Centro(go.GetComponent<RectTransform>(), new Vector2(-70f, -20f), new Vector2(230f, 32f));
                            var e = (ConfigEntry<float>)d.E;
                            sl.SetValueWithoutNotify(e.Value);
                            var val = Texto(cr, "MR_Valor", e.Value.ToString(d.Formato), 36f, new Vector2(100f, -20f), new Vector2(90f, 40f), TextAlignmentOptions.Center);
                            val.color = new Color(0.83f, 0.76f, 0.65f, 1f);
                            string fmt = d.Formato; float paso = (d.Max - d.Min) / 100f;
                            sl.onValueChanged.AddListener((UnityAction<float>)(v =>
                            {
                                v = (float)Math.Round(v / paso) * paso;
                                val.text = v.ToString(fmt);
                                if (actualizando) return;
                                e.Value = (float)Math.Round(v, 3); Cambio();
                            }));
                            if (srcReset != null)
                            {
                                var rgo = Clonar(srcReset.gameObject, cr, "MR_Reset");
                                var rb = rgo.GetComponent<Button>();
                                rb.onClick = new Button.ButtonClickedEvent();
                                rb.onClick.AddListener((UnityAction)(() => { e.Value = (float)e.DefaultValue; sl.value = e.Value; }));
                                var rrt = rgo.GetComponent<RectTransform>();
                                rrt.anchorMin = rrt.anchorMax = new Vector2(0.5f, 0.5f);
                                rrt.anchoredPosition = new Vector2(172f, -20f);
                            }
                            c.Sl = sl; c.Valor = val;
                        }
                        p.Controles.Add(c);
                        p.Filas.Add(new Fila { Rt = cr, Ayuda = d.Ayuda, Go = celda });
                        if (++col == 3) { col = 0; y -= 108f; }
                        break;
                    }
                    case Tipo.Texto:
                    {
                        int lineas = d.Nombre.Split('\n').Length;
                        var t2 = Texto(cont, "MR_NoDisp", d.Nombre, 34f, new Vector2(0, y - lineas * 23f), new Vector2(1180f, lineas * 46f), TextAlignmentOptions.TopLeft);
                        t2.color = new Color(0.73f, 0.66f, 0.56f, 1f);
                        y -= lineas * 46f + 20f;
                        break;
                    }
                }
                ultimo = d.T;
            }
            SaltoFila(AltoDe(ultimo));
            cont.sizeDelta = new Vector2(1280f, -y + 20f);
            MarcarPreset(p);
        }

        static Scrollbar CrearBarraDesplazamiento(RectTransform padre, Vector2 pos, float alto)
        {
            var fondoSrc = srcBarra.transform.Find("Background")?.GetComponent<Image>();
            var llenoSrc = srcBarra.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
            var go = new GameObject("MR_Desplazamiento");
            var rt = go.AddComponent<RectTransform>(); rt.SetParent(padre, false);
            Centro(rt, pos, new Vector2(16f, alto));
            var img = go.AddComponent<Image>();
            if (fondoSrc != null) { img.sprite = fondoSrc.sprite; img.type = fondoSrc.type; img.color = fondoSrc.color; }
            var area = new GameObject("Area"); var art = area.AddComponent<RectTransform>(); art.SetParent(rt, false);
            art.anchorMin = Vector2.zero; art.anchorMax = Vector2.one; art.offsetMin = Vector2.zero; art.offsetMax = Vector2.zero;
            var h = new GameObject("Handle"); var hrt = h.AddComponent<RectTransform>(); hrt.SetParent(art, false);
            hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
            var himg = h.AddComponent<Image>();
            if (llenoSrc != null) { himg.sprite = llenoSrc.sprite; himg.type = llenoSrc.type; }
            himg.color = Color.white;
            var sb = go.AddComponent<Scrollbar>();
            sb.handleRect = hrt; sb.targetGraphic = himg; sb.direction = Scrollbar.Direction.BottomToTop;
            var cb = sb.colors; cb.normalColor = new Color(0.88f, 0.78f, 0.64f); cb.highlightedColor = new Color(1f, 0.73f, 0.33f);
            cb.pressedColor = new Color(0.4f, 0.31f, 0.16f); cb.selectedColor = cb.normalColor; sb.colors = cb;
            var nav = sb.navigation; nav.mode = Navigation.Mode.None; sb.navigation = nav;
            return sb;
        }

        // ---------------- comportamiento ----------------
        static void Mostrar(Pantalla p)
        {
            foreach (var g in p.PaginasJuego) if (g != null) g.SetActive(false);
            p.Pagina.SetActive(true);
            p.Activa = true;
            RefrescarControles(p);
            p.Scroll.verticalNormalizedPosition = 1f;
            var cb = p.ColoresPestaña; cb.normalColor = cb.selectedColor; p.Pestaña.colors = cb;
        }

        static void Ocultar(Pantalla p)
        {
            if (p.Pagina != null) p.Pagina.SetActive(false);
            p.Activa = false;
            if (p.Pestaña != null) { p.Pestaña.colors = p.ColoresPestaña; p.Pestaña.targetGraphic.CrossFadeColor(p.ColoresPestaña.normalColor, 0f, true, true); }
        }

        static void Vigilar(Pantalla p)
        {
            if (!p.Activa) return;
            // el juego volvió a mostrar una de sus páginas, o se cerró Opciones
            bool juego = false;
            foreach (var g in p.PaginasJuego) if (g != null && g.activeSelf) juego = true;
            if (juego || !p.Panel.activeInHierarchy) { Ocultar(p); return; }
            // ayuda de la fila bajo el puntero (o seleccionada con mando)
            string ayuda = null;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                if (RectTransformUtility.RectangleContainsScreenPoint(p.Scroll.viewport, pos, null))
                    foreach (var f in p.Filas)
                        if (f.Rt != null && RectTransformUtility.RectangleContainsScreenPoint(f.Rt, pos, null)) { ayuda = f.Ayuda; break; }
            }
            if (ayuda == null && EventSystem.current != null)
            {
                var sel = EventSystem.current.currentSelectedGameObject;
                if (sel != null)
                    foreach (var f in p.Filas)
                        if (f.Go != null && (sel == f.Go || sel.transform.IsChildOf(f.Go.transform))) { ayuda = f.Ayuda; break; }
            }
            p.Ayuda.text = ayuda ?? T("Pasa el puntero sobre una opción para ver qué hace. Los cambios se aplican al instante y se guardan.", "Hover over an option to see what it does. Changes apply instantly and are saved.");
        }

        static void Cambio() { aplicarEn = Time.unscaledTime + 0.3f; }

        static void RefrescarTodo() { foreach (var p in pantallas) RefrescarControles(p); }

        static void RefrescarControles(Pantalla p)
        {
            actualizando = true;
            try
            {
                foreach (var c in p.Controles)
                {
                    if (c.Tg != null) c.Tg.SetIsOnWithoutNotify(((ConfigEntry<bool>)c.D.E).Value);
                    if (c.Dd != null) { c.Dd.SetValueWithoutNotify(Math.Max(0, Array.IndexOf(c.D.Valores, ((ConfigEntry<int>)c.D.E).Value))); c.Dd.RefreshShownValue(); }
                    if (c.Sl != null) { float v = ((ConfigEntry<float>)c.D.E).Value; c.Sl.SetValueWithoutNotify(v); c.Valor.text = v.ToString(c.D.Formato); }
                }
            }
            finally { actualizando = false; }
            MarcarPreset(p);
        }

        // el preset que coincide con la configuración actual queda en rojo
        static void MarcarPreset(Pantalla p)
        {
            string actual = Opciones.PresetActual();
            string[] ip = { "original", "equilibrado", "maximo" };
            for (int i = 0; i < 3; i++)
            {
                var b = p.Presets[i]; if (b == null) continue;
                var img = b.targetGraphic;
                bool sel = ip[i] == actual;
                img.color = sel ? new Color(1f, 0.32f, 0.26f, 1f) : Color.white;
                var t = b.GetComponentInChildren<TextMeshProUGUI>();
                if (t != null) t.color = sel ? new Color(1f, 0.93f, 0.85f, 1f) : Color.black;
            }
        }

        // ---------------- utilidades ----------------
        static GameObject Clonar(GameObject src, Transform padre, string nombre)
        {
            var go = UnityEngine.Object.Instantiate(src, padre, false);
            go.name = nombre;
            go.SetActive(true);
            return go;
        }

        static void PonerTexto(GameObject go, string texto)
        {
            var t = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null) t.text = texto;
        }

        static TextMeshProUGUI Texto(Transform padre, string nombre, string texto, float tam, Vector2 pos, Vector2 tamaño, TextAlignmentOptions al)
        {
            var go = Clonar(srcTitulo.gameObject, padre, nombre);
            var t = go.GetComponent<TextMeshProUGUI>();
            t.text = texto; t.enableAutoSizing = false; t.fontSize = tam; t.alignment = al; t.enableWordWrapping = true;
            t.raycastTarget = false;
            Centro(t.rectTransform, pos, tamaño);
            return t;
        }

        static void Centro(RectTransform rt, Vector2 pos, Vector2 tam)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, rt.parent != null && rt.parent.name == "MR_Contenido" ? 1f : 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = tam; rt.localScale = Vector3.one;
        }

        static string Metodos(Button b)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++) sb.Append(b.onClick.GetPersistentMethodName(i)).Append(';');
            return sb.ToString();
        }

        // ---------------- prueba automática con capturas ----------------
        static int pasoPrueba = -1; static float momentoPrueba; static Pantalla pruebaP; static bool enPartida;
        static Button BotonPorMetodo(string metodo, bool activo)
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Button>())
                if (b != null && b.gameObject.scene.IsValid() && (!activo || b.gameObject.activeInHierarchy) && Metodos(b).Contains(metodo)) return b;
            return null;
        }
        static void PruebaTick()
        {
            if (!PruebaCapturas.Value || Time.unscaledTime < momentoPrueba) return;
            momentoPrueba = Time.unscaledTime + 1.2f;
            if (pasoPrueba < 0)
            {
                if (!enPartida)
                {
                    if (pantallas.Count == 0 || BotonPorMetodo("LoadOptions", true) == null) return;
                    pruebaP = pantallas[0];
                }
                else
                {
                    pruebaP = null;
                    foreach (var x in pantallas) if (x.Panel.scene.name != "MainMenu" && x.Panel.scene.name != "Menu") pruebaP = x;
                    var uh0 = UnityEngine.Object.FindObjectOfType<UiHandler>();
                    if (pruebaP == null || uh0 == null) return;
                    if (esperaPartida < 0) { esperaPartida = Time.unscaledTime + 25f; return; }
                    if (Time.unscaledTime < esperaPartida) return;
                }
                pasoPrueba = 0; momentoPrueba = Time.unscaledTime + 3f;
                Plugin.Log.LogInfo("Interfaz: prueba en " + (enPartida ? "partida" : "menú principal") + " (" + pruebaP.Panel.scene.name + ")");
                return;
            }
            var p = pruebaP;
            string pre = enPartida ? "partida_" : "menu_";
            var uh = enPartida ? UnityEngine.Object.FindObjectOfType<UiHandler>() : null;
            switch (pasoPrueba)
            {
                case 0:
                    if (enPartida) { uh.LoadPauseMenu(); }
                    else BotonPorMetodo("LoadOptions", true)?.onClick.Invoke();
                    break;
                case 1: if (enPartida) uh.OptionsMenu(true); break;
                case 2: Pruebas.CapturaPublica(pre + "opciones_juego"); break;
                case 3: p.Pestaña.onClick.Invoke(); break;
                case 4: Pruebas.CapturaPublica(pre + "mod_1"); break;
                case 5:
                {
                    // pulsar casillas como lo haría el jugador: deben marcarse y cambiar la configuración
                    var c = p.Controles.Find(k => k.D.E == Realismo.SSGI);
                    Plugin.Log.LogInfo("Interfaz: prueba casilla SSGI antes=" + Realismo.SSGI.Value);
                    c.Tg.isOn = !c.Tg.isOn;
                    Plugin.Log.LogInfo("Interfaz: prueba casilla SSGI después=" + Realismo.SSGI.Value + " (casilla " + c.Tg.isOn + ")");
                    var l = p.Controles.Find(k => k.D.E == Opciones.CalidadSombras);
                    l.Dd.value = 2;
                    Plugin.Log.LogInfo("Interfaz: prueba lista Sombras=" + Opciones.CalidadSombras.Value);
                    break;
                }
                case 6: Pruebas.CapturaPublica(pre + "mod_casilla_pulsada"); break;
                case 7: p.Presets[1].onClick.Invoke(); break;
                case 8: p.Scroll.verticalNormalizedPosition = 0.5f; break;
                case 9: Pruebas.CapturaPublica(pre + "mod_2"); break;
                case 10: p.Scroll.verticalNormalizedPosition = 0f; break;
                case 11: Pruebas.CapturaPublica(pre + "mod_3"); break;
                case 12: BotonPorMetodo("SwitchToSettings", true)?.onClick.Invoke(); break;
                case 13: Pruebas.CapturaPublica(pre + "vuelta_ajustes"); break;
                case 14:
                    if (enPartida) { uh.OptionsMenu(false); uh.UnloadPauseMenu(); }
                    else { BotonPorMetodo("SaveSettings", true)?.onClick.Invoke(); }
                    break;
                case 15:
                    if (!enPartida)
                    {
                        Plugin.Log.LogInfo("Interfaz: prueba del menú terminada; entrando a la partida");
                        enPartida = true; pasoPrueba = -1; momentoPrueba = Time.unscaledTime + 3f;
                        BotonPorMetodo("StartGame", true)?.onClick.Invoke();
                        return;
                    }
                    PruebaCapturas.Value = false; Plugin.Log.LogInfo("Interfaz: prueba de capturas terminada");
                    break;
            }
            pasoPrueba++;
        }
        static float esperaPartida = -1f;

        // ---------------- diagnóstico: volcado de jerarquía ----------------
        static void Volcar(OptionsManager m)
        {
            var sb = new StringBuilder();
            sb.AppendLine("OptionsManager en: " + Ruta(m.transform));
            Transform raiz = m.volumetricFog.transform;
            while (raiz.parent != null) raiz = raiz.parent;
            Nodo(raiz, 0, sb);
            string ruta = Path.Combine(Paths.GameRootPath, "Capturas", "jerarquia_" + m.gameObject.scene.name + ".txt");
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllText(ruta, sb.ToString());
            Plugin.Log.LogInfo("Interfaz: jerarquía volcada en " + ruta);
        }

        static string Ruta(Transform t)
        {
            if (t == null) return "(null)";
            string s = t.name;
            while (t.parent != null) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }

        static void Nodo(Transform t, int nivel, StringBuilder sb)
        {
            if (nivel > 14) return;
            var go = t.gameObject;
            sb.Append(new string(' ', nivel * 2)).Append(go.activeSelf ? "+ " : "- ").Append(t.name);
            var rt = t.TryCast<RectTransform>();
            if (rt != null)
                sb.Append($"  [anc {rt.anchorMin.x:0.##},{rt.anchorMin.y:0.##}-{rt.anchorMax.x:0.##},{rt.anchorMax.y:0.##} pos {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0} tam {rt.sizeDelta.x:0},{rt.sizeDelta.y:0} esc {rt.localScale.x:0.##}]");
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                string tn = c.GetIl2CppType().Name;
                if (tn == "RectTransform" || tn == "Transform" || tn == "CanvasRenderer") continue;
                sb.Append(" {").Append(tn);
                var img = c.TryCast<Image>();
                if (img != null) sb.Append($" spr={(img.sprite != null ? img.sprite.name : "-")}");
                var tmp = c.TryCast<TMP_Text>();
                if (tmp != null) sb.Append($" txt='{(tmp.text ?? "").Replace("\n", "⏎")}' tam={tmp.fontSize:0.#}");
                var btn = c.TryCast<Button>();
                if (btn != null) sb.Append(" click=").Append(Metodos(btn));
                sb.Append('}');
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Nodo(t.GetChild(i), nivel + 1, sb);
        }
    }
}
