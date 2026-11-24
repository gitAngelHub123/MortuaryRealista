// © Angel — Mejoras de materiales HDRP/Lit: piel (microdetalle y dispersión), microdetalle general, brillo por tipo de material
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal static class Materiales
    {
        internal static ConfigEntry<bool> Activar, Piel, Diagnostico, Microdetalle, Relieve, Brillo, Pasto;
        internal static ConfigEntry<float> PielFuerzaDetalle, PielEscalaDetalle, PielSuavidadDetalle;
        internal static ConfigEntry<float> MicroFuerza, MicroEscala, RelieveFactor, BrilloAzulejo, BrilloVidrio, BrilloMetal, PastoCapas, PastoDistancia;

        internal static bool Encendido = true;

        // Registro de cada cambio para poder volver al original (A/B y tecla)
        class Cambio
        {
            public Material M;
            public int Id;
            public string Cat = "piel";
            public readonly List<(int id, float antes, float despues)> Floats = new List<(int, float, float)>();
            public readonly List<(int id, Vector4 antes, Vector4 despues)> Vectores = new List<(int, Vector4, Vector4)>();
            public readonly List<(int id, Texture antes, Texture despues)> Texturas = new List<(int, Texture, Texture)>();
            public readonly List<(string kw, bool antes, bool despues)> Claves = new List<(string, bool, bool)>();

            public void Float(string p, float v)
            {
                int id = Shader.PropertyToID(p);
                if (!M.HasProperty(id)) return;
                float a = M.GetFloat(id);
                if (Math.Abs(a - v) < 1e-5f) return;
                Floats.Add((id, a, v)); M.SetFloat(id, v);
            }
            public void Vector(string p, Vector4 v)
            {
                int id = Shader.PropertyToID(p);
                if (!M.HasProperty(id)) return;
                var a = M.GetVector(id);
                Vectores.Add((id, a, v)); M.SetVector(id, v);
            }
            public void Textura(string p, Texture t)
            {
                int id = Shader.PropertyToID(p);
                if (!M.HasProperty(id)) return;
                Texturas.Add((id, M.GetTexture(id), t)); M.SetTexture(id, t);
            }
            public void Clave(string kw, bool on)
            {
                bool a = M.IsKeywordEnabled(kw);
                if (a == on) return;
                Claves.Add((kw, a, on));
                if (on) M.EnableKeyword(kw); else M.DisableKeyword(kw);
            }
            public void Aplicar(bool nuevo)
            {
                if (M == null) return;
                foreach (var f in Floats) M.SetFloat(f.id, nuevo ? f.despues : f.antes);
                foreach (var v in Vectores) M.SetVector(v.id, nuevo ? v.despues : v.antes);
                foreach (var t in Texturas) M.SetTexture(t.id, nuevo ? t.despues : t.antes);
                foreach (var k in Claves) { if (nuevo ? k.despues : k.antes) M.EnableKeyword(k.kw); else M.DisableKeyword(k.kw); }
            }
            // true si el material no tiene los valores que debería (p. ej. recargado por el juego)
            public bool Desajustado(bool nuevo)
            {
                if (M == null) return false;
                foreach (var f in Floats) if (Math.Abs(M.GetFloat(f.id) - (nuevo ? f.despues : f.antes)) > 1e-4f) return true;
                foreach (var t in Texturas) if (M.GetTexture(t.id) != (nuevo ? t.despues : t.antes)) return true;
                foreach (var k in Claves) if (M.IsKeywordEnabled(k.kw) != (nuevo ? k.despues : k.antes)) return true;
                return false;
            }
            public bool Vacio => Floats.Count + Vectores.Count + Texturas.Count + Claves.Count == 0;
        }

        static readonly List<Cambio> cambios = new List<Cambio>();
        static readonly HashSet<int> vistos = new HashSet<int>();
        static readonly HashSet<string> registrados = new HashSet<string>();   // "id|categoría" ya aplicados
        static bool Hecho(Material m, string cat) => registrados.Contains(m.GetInstanceID() + "|" + cat);
        static Texture2D detallePiel;
        static int firmaEscena = int.MinValue;
        static float proximoBarrido = 5f;
        static bool perfilesListados;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("Materiales", "Activar", true, "Mejoras de materiales (piel, microdetalle, brillo)");
            Piel = cfg.Bind("Materiales", "Piel", true, "Piel: microdetalle de poros en todos los cuerpos y más fuerza donde el juego lo dejó casi apagado");
            PielFuerzaDetalle = cfg.Bind("Materiales", "PielFuerzaDetalle", 0.6f, "Fuerza mínima del relieve de poros en la piel (0 a 2)");
            PielEscalaDetalle = cfg.Bind("Materiales", "PielEscalaDetalle", 128f, "Repeticiones del mapa de poros en los cuerpos que no lo tenían");
            PielSuavidadDetalle = cfg.Bind("Materiales", "PielSuavidadDetalle", 0.5f, "Variación de brillo de los poros (0 a 2)");
            Microdetalle = cfg.Bind("Materiales", "Microdetalle", false, "Punto 2 (NO FUNCIONA: el juego no trae compilada la variante del shader con mapa de detalle; probado 2026-11-23): grano fino (poros, rugosidad) en paredes, suelos, azulejos, madera y piedra que no lo tienen");
            MicroFuerza = cfg.Bind("Materiales", "MicroFuerza", 0.5f, "Fuerza del relieve fino del microdetalle (0 a 2)");
            MicroEscala = cfg.Bind("Materiales", "MicroEscala", 6f, "Repeticiones del microdetalle sobre la textura base");
            Relieve = cfg.Bind("Materiales", "Relieve", true, "Punto 3: refuerza los mapas de normales de las superficies");
            RelieveFactor = cfg.Bind("Materiales", "RelieveFactor", 2.0f, "Multiplicador del relieve de superficies (1 = como el juego)");
            Brillo = cfg.Bind("Materiales", "Brillo", true, "Punto 4: brillo y metal según el tipo de material");
            BrilloAzulejo = cfg.Bind("Materiales", "BrilloAzulejo", 0.85f, "Brillo máximo de azulejos, cerámica y loza (0 a 1)");
            BrilloVidrio = cfg.Bind("Materiales", "BrilloVidrio", 0.97f, "Brillo de vidrios (0 a 1)");
            BrilloMetal = cfg.Bind("Materiales", "BrilloMetal", 0.1f, "Brillo extra de metales (se suma, 0 a 0,5)");
            Pasto = cfg.Bind("Materiales", "Pasto", true, "Punto 5: pasto más tupido y visible más lejos");
            PastoCapas = cfg.Bind("Materiales", "PastoCapas", 1.6f, "Multiplicador de capas del pasto (más tupido, cuesta GPU)");
            PastoDistancia = cfg.Bind("Materiales", "PastoDistancia", 2f, "Multiplicador de la distancia a la que se ve el pasto");
            Diagnostico = cfg.Bind("Materiales", "Diagnostico", false, "Escribe en el log los perfiles de piel y cada material cambiado");
        }

        static readonly HashSet<string> catApagadas = new HashSet<string>();
        public static void Alternar(bool on, string cat = null)
        {
            if (cat == null) { Encendido = on; catApagadas.Clear(); }
            else if (on) catApagadas.Remove(cat); else catApagadas.Add(cat);
            int n = 0;
            foreach (var c in cambios)
            {
                if (cat != null && c.Cat != cat) continue;
                c.Aplicar(Encendido && Activar.Value && !catApagadas.Contains(c.Cat)); n++;
            }
            Plugin.Log.LogInfo("Materiales " + (cat ?? "todos") + " " + (on ? "ACTIVOS" : "ORIGINALES") + " (" + n + " cambios)");
        }

        // Deshace todo y vuelve a calcular con la configuración actual (lo usa el menú)
        public static void Rehacer()
        {
            foreach (var c in cambios) { if (c.M != null) c.Aplicar(false); }
            cambios.Clear(); registrados.Clear(); vistos.Clear();
            proximoBarrido = 0f;
            if (Activar.Value) { try { Barrer(); } catch (Exception e) { Plugin.Log.LogWarning("Materiales.Rehacer: " + e.Message); } }
        }

        public static void Tick()
        {
            if (!Activar.Value) return;
            try
            {
                var activa = SceneManager.GetActiveScene();
                int firma = SceneManager.sceneCount * 7919 + activa.handle;
                if (firma != firmaEscena) { firmaEscena = firma; proximoBarrido = Time.unscaledTime + 3f; vistos.Clear(); }
                if (Time.unscaledTime < proximoBarrido) return;
                proximoBarrido = Time.unscaledTime + 4f;   // también capta materiales creados más tarde (instancias)
                Barrer();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Materiales: " + e); proximoBarrido = Time.unscaledTime + 30f; }
        }

        static void Barrer()
        {
            if (!perfilesListados) ListarPerfiles();
            if (detallePiel == null)
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && t.name == "skin_detail_map") { detallePiel = t; break; }
            // El juego recarga materiales desde disco al cambiar de escena (mismo ID, valores originales):
            // se busca el material vivo por su ID y se le vuelven a poner los valores en cada barrido.
            var todos = Resources.FindObjectsOfTypeAll<Material>();
            var porId = new Dictionary<int, Material>();
            foreach (var m in todos) if (m != null) porId[m.GetInstanceID()] = m;
            int reaplicados = 0;
            foreach (var c in cambios)
            {
                if (!porId.TryGetValue(c.Id, out var vivo)) continue;
                bool debe = Encendido && Activar.Value && !catApagadas.Contains(c.Cat);
                c.M = vivo;
                if (c.Desajustado(debe)) { c.Aplicar(debe); reaplicados++; }
            }
            if (reaplicados > 0) Plugin.Log.LogInfo("Materiales: " + reaplicados + " cambios reaplicados (el juego recargó esos materiales)");
            int nuevos = 0;
            foreach (var m in todos)
            {
                if (m == null || m.shader == null || !m.shader.isSupported || !m.HasProperty("_BaseColor") && !m.HasProperty("_Color")) continue;   // material aún sin cargar: se reintenta
                int id = m.GetInstanceID();
                if (vistos.Contains(id)) continue;
                vistos.Add(id);
                string sh = m.shader.name;
                if (sh.StartsWith("BruteForce"))
                {
                    if (Pasto.Value && !Hecho(m, "pasto")) nuevos += Registrar(MejorarPasto(new Cambio { M = m, Cat = "pasto" }));
                    continue;
                }
                if (sh != "HDRP/Lit") continue;
                if (Piel.Value && EsPiel(m)) { if (!Hecho(m, "piel")) { var c = new Cambio { M = m }; MejorarPiel(c); nuevos += Registrar(c); } continue; }
                string tipo = Clasificar(m.name);
                if (tipo == null) continue;
                if (Microdetalle.Value && EsSuperficie(tipo) && !Hecho(m, "microdetalle")) nuevos += Registrar(MejorarMicro(new Cambio { M = m, Cat = "microdetalle" }));
                if (Relieve.Value && EsSuperficie(tipo) && !Hecho(m, "relieve")) nuevos += Registrar(MejorarRelieve(new Cambio { M = m, Cat = "relieve" }));
                if (Brillo.Value && !Hecho(m, "brillo")) nuevos += Registrar(MejorarBrillo(new Cambio { M = m, Cat = "brillo" }, tipo));
            }
            if (nuevos > 0) Plugin.Log.LogInfo("Materiales: " + nuevos + " materiales mejorados (total " + cambios.Count + ")");
        }

        static int Registrar(Cambio c)
        {
            if (c == null || c.Vacio) return 0;
            if (!Encendido || catApagadas.Contains(c.Cat)) c.Aplicar(false);
            c.Id = c.M.GetInstanceID();
            cambios.Add(c);
            registrados.Add(c.M.GetInstanceID() + "|" + c.Cat);
            if (Diagnostico.Value) Plugin.Log.LogInfo($"  {c.Cat} {c.M.name} id {c.M.GetInstanceID()}: {c.Floats.Count + c.Texturas.Count + c.Vectores.Count} cambios");
            return 1;
        }

        // ---------- clasificación por nombre ----------
        static readonly (string tipo, string[] claves)[] tipos =
        {
            ("vidrio", new[] { "Glass", "Window" }),
            ("azulejo", new[] { "Tile", "Ceramic", "BathroomSet", "TableWare" }),
            ("metal", new[] { "Metal", "Gold", "Retort", "Trim_", "MetalTrim" }),
            ("madera", new[] { "Wood", "FloorBoard", "Door", "Ladder" }),
            ("piedra", new[] { "Stone", "Rock", "Gravel", "Brick", "Concrete", "SideWalk", "Street", "Shingles", "Cave", "Dirt" }),
            ("pared", new[] { "Wall", "Ceiling", "SubFloor", "Modules", "Outsidetrim", "PaintedWalls" }),
            ("tela", new[] { "Carpet", "Sofa", "SoFa", "Pillow", "Rug" }),
        };
        static string Clasificar(string n)
        {
            if (n.Contains("Skin") || n.Contains("Hair") || n.Contains("Teeth") || n.Contains("EYES") || n.Contains("Eye") || n.Contains("_Pbr")) return null;
            foreach (var (tipo, claves) in tipos)
                foreach (var k in claves) if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return tipo;
            return null;
        }
        static bool EsSuperficie(string t) => t == "azulejo" || t == "madera" || t == "piedra" || t == "pared" || t == "tela";

        // ---------- punto 2: microdetalle ----------
        static Texture2D detalleGeneral;
        static Texture2D DetalleGeneral()
        {
            if (detalleGeneral != null) return detalleGeneral;
            const int N = 256;
            var alt = new float[N * N];
            var rnd = new System.Random(1234);
            // ruido de valor periódico en 4 octavas
            float amp = 1f, total = 0f;
            for (int oct = 0, celdas = 8; oct < 5; oct++, celdas *= 2, amp *= 0.55f)
            {
                var g = new float[celdas * celdas];
                for (int i = 0; i < g.Length; i++) g[i] = (float)rnd.NextDouble();
                for (int y = 0; y < N; y++)
                    for (int x = 0; x < N; x++)
                    {
                        float fx = x * celdas / (float)N, fy = y * celdas / (float)N;
                        int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
                        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                        int x1 = (x0 + 1) % celdas, y1 = (y0 + 1) % celdas;
                        float a = g[y0 * celdas + x0], b = g[y0 * celdas + x1], c = g[y1 * celdas + x0], d = g[y1 * celdas + x1];
                        alt[y * N + x] += amp * ((a + (b - a) * tx) + ((c + (d - c) * tx) - (a + (b - a) * tx)) * ty);
                    }
                total += amp;
            }
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float h = alt[y * N + x] / total;
                    float dx = (alt[y * N + (x + 1) % N] - alt[y * N + (x + N - 1) % N]) / total * 6f;
                    float dy = (alt[((y + 1) % N) * N + x] - alt[((y + N - 1) % N) * N + x]) / total * 6f;
                    float nz = 1f / Mathf.Sqrt(1 + dx * dx + dy * dy);
                    float nx = -dx * nz, ny = -dy * nz;
                    byte r = (byte)Mathf.Clamp(128 + (h - 0.5f) * 60f, 0, 255);   // albedo: 0,5 = neutro
                    byte gg = (byte)Mathf.Clamp(128 + ny * 127f, 0, 255);
                    byte bb = (byte)Mathf.Clamp(128 + (h - 0.5f) * 80f, 0, 255);  // suavidad: 0,5 = neutro
                    byte aa = (byte)Mathf.Clamp(128 + nx * 127f, 0, 255);
                    px[y * N + x] = new Color32(r, gg, bb, aa);
                }
            detalleGeneral = new Texture2D(N, N, TextureFormat.RGBA32, true, true) { name = "MicrodetalleRealista", wrapMode = TextureWrapMode.Repeat, anisoLevel = 8 };
            detalleGeneral.SetPixels32(px);
            detalleGeneral.Apply(true, true);
            detalleGeneral.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return detalleGeneral;
        }

        static Cambio MejorarMicro(Cambio c)
        {
            var m = c.M;
            if (!m.HasProperty("_DetailMap") || m.GetTexture("_DetailMap") != null) return c;
            c.Textura("_DetailMap", DetalleGeneral());
            float e = MicroEscala.Value;
            c.Vector("_DetailMap_ST", new Vector4(e, e, 0, 0));
            c.Vector("_UVDetailsMappingMask", new Vector4(1, 0, 0, 0));
            c.Float("_DetailAlbedoScale", 0.6f);
            c.Float("_DetailNormalScale", MicroFuerza.Value);
            c.Float("_DetailSmoothnessScale", 0.6f);
            c.Clave("_DETAIL_MAP", true);
            return c;
        }

        // ---------- punto 3: relieve ----------
        static Cambio MejorarRelieve(Cambio c)
        {
            var m = c.M;
            if (!m.HasProperty("_NormalMap") || m.GetTexture("_NormalMap") == null) return c;
            float f = RelieveFactor.Value;
            if (Math.Abs(f - 1f) < 0.01f) return c;
            c.Float("_NormalScale", Mathf.Clamp(m.GetFloat("_NormalScale") * f, 0f, 4f));
            return c;
        }

        // ---------- punto 4: brillo y metal ----------
        static Cambio MejorarBrillo(Cambio c, string tipo)
        {
            var m = c.M;
            bool mascara = m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null;
            string prop = mascara ? "_SmoothnessRemapMax" : "_Smoothness";
            if (!m.HasProperty(prop)) return c;
            float v = m.GetFloat(prop);
            if (mascara && m.HasProperty("_SmoothnessRemapMin"))
            {
                // con máscara el brillo sale de la textura: se sube el piso del rango (remapMin)
                float vmin = m.GetFloat("_SmoothnessRemapMin");
                float vmax = m.GetFloat("_SmoothnessRemapMax");
                switch (tipo)
                {
                    case "azulejo": c.Float("_SmoothnessRemapMax", Mathf.Max(vmax, BrilloAzulejo.Value)); c.Float("_SmoothnessRemapMin", Mathf.Max(vmin, BrilloAzulejo.Value - 0.35f)); return c;
                    case "vidrio": c.Float("_SmoothnessRemapMax", Mathf.Max(vmax, BrilloVidrio.Value)); c.Float("_SmoothnessRemapMin", Mathf.Max(vmin, BrilloVidrio.Value - 0.1f)); return c;
                    case "metal": c.Float("_SmoothnessRemapMin", Mathf.Min(vmax, vmin + BrilloMetal.Value * 3f)); return c;
                    case "tela": if (vmax > 0.35f) c.Float("_SmoothnessRemapMax", 0.35f); return c;
                    default: return c;
                }
            }
            switch (tipo)
            {
                case "azulejo": if (v < BrilloAzulejo.Value) c.Float(prop, BrilloAzulejo.Value); break;
                case "vidrio": if (v < BrilloVidrio.Value) c.Float(prop, BrilloVidrio.Value); break;
                case "metal": c.Float(prop, Mathf.Min(1f, v + BrilloMetal.Value)); break;
                case "tela": if (v > 0.35f) c.Float(prop, 0.35f); break;   // la tela no brilla
            }
            return c;
        }

        // ---------- punto 5: pasto ----------
        static Cambio MejorarPasto(Cambio c)
        {
            var m = c.M;
            if (m.HasProperty("_NumberOfStacks")) c.Float("_NumberOfStacks", Mathf.Min(17f, m.GetFloat("_NumberOfStacks") * PastoCapas.Value));
            if (m.HasProperty("_MinimumNumberStacks")) c.Float("_MinimumNumberStacks", Mathf.Min(17f, Mathf.Max(4f, m.GetFloat("_MinimumNumberStacks") * PastoCapas.Value)));
            float d = PastoDistancia.Value;
            foreach (var p in new[] { "_FadeDistanceStart", "_FadeDistanceEnd", "_FadeDistance" })
                if (m.HasProperty(p)) c.Float(p, m.GetFloat(p) * d);
            return c;
        }

        static bool EsPiel(Material m)
        {
            if (!m.HasProperty("_MaterialID")) return false;
            if ((int)m.GetFloat("_MaterialID") != 0) return false;   // 0 = dispersión subsuperficial
            string n = m.name;
            return n.Contains("Skin") || n.Contains("SKIN");
        }

        static void MejorarPiel(Cambio c)
        {
            var m = c.M;
            bool tieneDetalle = m.HasProperty("_DetailMap") && m.GetTexture("_DetailMap") != null;
            if (!tieneDetalle)
            {
                if (detallePiel == null) return;   // se reintenta: el material no queda marcado como mejorado
                c.Textura("_DetailMap", detallePiel);
                float e = PielEscalaDetalle.Value;
                c.Vector("_DetailMap_ST", new Vector4(e, e, 0, 0));
                c.Vector("_UVDetailsMappingMask", new Vector4(1, 0, 0, 0));
                c.Float("_DetailAlbedoScale", 0f);
                c.Float("_DetailNormalScale", PielFuerzaDetalle.Value);
                c.Float("_DetailSmoothnessScale", PielSuavidadDetalle.Value);
                c.Clave("_DETAIL_MAP", true);
            }
            else
            {
                if (m.GetFloat("_DetailNormalScale") < PielFuerzaDetalle.Value) c.Float("_DetailNormalScale", PielFuerzaDetalle.Value);
                if (m.GetFloat("_DetailSmoothnessScale") < PielSuavidadDetalle.Value) c.Float("_DetailSmoothnessScale", PielSuavidadDetalle.Value);
                c.Clave("_DETAIL_MAP", true);
            }
            if (Diagnostico.Value && !c.Vacio)
                Plugin.Log.LogInfo($"  piel {m.name}: {(tieneDetalle ? "detalle reforzado" : "detalle añadido")} ({c.Floats.Count + c.Texturas.Count} cambios)");
            if (detallePiel == null && !tieneDetalle) vistos.Remove(m.GetInstanceID());
        }

        static void ListarPerfiles()
        {
            perfilesListados = true;
            if (!Diagnostico.Value) return;
            try
            {
                foreach (var d in Resources.FindObjectsOfTypeAll<DiffusionProfileSettings>())
                {
                    if (d == null) continue;
                    var p = d.profile;
                    Plugin.Log.LogInfo($"PERFIL PIEL '{d.name}': dispersión={p.scatteringDistance} tinteTransmisión={p.transmissionTint} escalaMundo={p.worldScale} ior={p.ior} grosor={p.thicknessRemap} modoTextura={p.texturingMode} modoTransmisión={p.transmissionMode} hash={p.hash}");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Perfiles de piel: " + e.Message); }
        }
    }
}
