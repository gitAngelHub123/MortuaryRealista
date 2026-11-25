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
        internal static ConfigEntry<bool> Activar, Piel, PielMate, VidrioReflejos, SueloTierra, Personajes, OjosHumedos, SangreReal, PeloRealista, Diagnostico, Microdetalle, Relieve, Brillo, Pasto;
        internal static ConfigEntry<float> TierraEscala, PielBrilloMax, RelievePersonajes, SangreFactor, DispersionPiel, TonoPiel, TarjetaBrillo, TarjetaLuz, PielFuerzaDetalle, PielEscalaDetalle, PielSuavidadDetalle;
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
            public readonly List<(string pase, bool antes, bool despues)> Pases = new List<(string, bool, bool)>();
            public void Pase(string p, bool on) { Pases.Add((p, M.GetShaderPassEnabled(p), on)); M.SetShaderPassEnabled(p, on); }

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
                var antes = M.GetTexture(id);
                if (antes != null) antes.hideFlags |= HideFlags.DontUnloadUnusedAsset;   // que la limpieza de memoria no lo borre: hace falta para volver al original
                Texturas.Add((id, antes, t)); M.SetTexture(id, t);
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
                foreach (var p in Pases) M.SetShaderPassEnabled(p.pase, nuevo ? p.despues : p.antes);
            }
            // true si el material no tiene los valores que debería (p. ej. recargado por el juego)
            public bool Desajustado(bool nuevo)
            {
                if (M == null) return false;
                foreach (var f in Floats) if (Math.Abs(M.GetFloat(f.id) - (nuevo ? f.despues : f.antes)) > 1e-4f) return true;
                foreach (var t in Texturas) if (M.GetTexture(t.id) != (nuevo ? t.despues : t.antes)) return true;
                foreach (var v in Vectores) if ((M.GetVector(v.id) - (nuevo ? v.despues : v.antes)).sqrMagnitude > 1e-6f) return true;
                foreach (var k in Claves) if (M.IsKeywordEnabled(k.kw) != (nuevo ? k.despues : k.antes)) return true;
                return false;
            }
            public bool Vacio => Floats.Count + Vectores.Count + Texturas.Count + Claves.Count + Pases.Count == 0;
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
            Personajes = cfg.Bind("Materiales", "Personajes", true, "Más relieve en todos los personajes animados (cadáveres, protagonista, familiares, demonios) y su ropa: músculos, venas, arrugas, tela");
            RelievePersonajes = cfg.Bind("Materiales", "RelievePersonajes", 1.75f, "Multiplicador del relieve de los personajes (1 = como el juego; más de 2,5 ensucia la piel)");
            DispersionPiel = cfg.Bind("Materiales", "DispersionPiel", 1f, "Cuánto viaja la luz bajo la piel (perfil Skin del juego). Menos = poros y pliegues más definidos; más = piel cerosa (1 = como el juego)");
            TarjetaBrillo = cfg.Bind("Materiales", "TarjetaBrillo", 0.45f, "Brillo de las tarjetas de acceso de Raymond y Rebeca (plástico sin metal; 0 = mate, 1 = espejo como el juego)");
            TarjetaLuz = cfg.Bind("Materiales", "TarjetaLuz", 0.06f, "Luz propia muy suave de las tarjetas de acceso (su propio color): de noche la tarjeta blanca en penumbra parecía una placa de metal. 0 = como el juego; 0,15 ya brilla");
            TonoPiel = cfg.Bind("Materiales", "TonoPiel", 1f, "Tono de la piel de los cuerpos y personajes: menos de 1 la oscurece (los cadáveres del juego son casi blancos y bajo las lámparas de la sala se ven quemados). Solo cambia la piel, no la luz de la sala (1 = como el juego)");
            PeloRealista = cfg.Bind("Materiales", "PeloRealista", true, "Pelo con el shader de pelo del juego (HDRP/Hair): hebras finas y brillo de pelo real en vez de tiras planas");
            SangreReal = cfg.Bind("Materiales", "SangreReal", true, "Sangre del drenaje en la mesa: rojo oscuro y mojada en vez de rosado transparente");
            SangreFactor = cfg.Bind("Materiales", "SangreFactor", 3f, "Cuánto más densa se ve la sangre de la mesa (1 = como el juego; 5 = opaca, pierde el brillo)");
            OjosHumedos = cfg.Bind("Materiales", "OjosHumedos", true, "Ojos con brillo húmedo (reflejo de luz como un ojo real); no toca los ojos estilizados de demonios");
            PielMate = cfg.Bind("Materiales", "PielMate", true, "Piel sin aspecto metálico: quita el metal que el juego dejó en algunas pieles y limita su brillo (se nota sobre todo al inspeccionar el cuerpo)");
            VidrioReflejos = cfg.Bind("Materiales", "VidrioReflejos", false, "Reflejos en pantalla también en vidrios transparentes. Medido 2026-11-24: sin diferencia visible (el borroso de la puerta es el vidrio mojado con lluvia del juego); apagado para no gastar GPU");
            SueloTierra = cfg.Bind("Materiales", "SueloTierra", false, "Exterior: cambia el pasto 3D del juego por suelo de tierra y piedras fotográfico (el mismo material escaneado del sótano)");
            TierraEscala = cfg.Bind("Materiales", "TierraEscala", 5f, "Metros que cubre cada repetición de la textura de tierra (la natural del pack cubre 5 m sin patrón)");
            PielBrilloMax = cfg.Bind("Materiales", "PielBrilloMax", 0.3f, "Brillo máximo de la piel con PielMate (0 = mate total, 1 = como el juego)");
            PielFuerzaDetalle = cfg.Bind("Materiales", "PielFuerzaDetalle", 1.2f, "Fuerza mínima del relieve de poros en la piel (0 a 2)");
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
            if (cat == null) { Encendido = on; catApagadas.Clear(); if (on) BarrerPelo(); else RevertirPelo(); AjustarPerfilPiel(on); }
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
            foreach (var cp in tarjetaMat.Values) if (cp != null) PlasticoTarjeta(cp);   // brillo de tarjetas en vivo
            foreach (var c in cambios) { if (c.M != null) c.Aplicar(false); }
            RevertirPelo();
            AjustarPerfilPiel(false);
            cambios.Clear(); registrados.Clear(); vistos.Clear();
            proximoBarrido = 0f;
            if (Activar.Value) { try { Barrer(); } catch (Exception e) { Plugin.Log.LogWarning("Materiales.Rehacer: " + e.Message); } }
        }

        // Sangre de la mesa (Shader Graphs/BloodFlow): el parámetro Vector1_9D2F18A4 es su densidad/opacidad (medido 2026-11-24).
        // Se multiplica el valor que pone el juego en cada cuadro, así no se rompe si el juego lo anima.
        const string PropSangre = "Vector1_9D2F18A4";
        static readonly List<Material> sangre = new List<Material>();
        static readonly Dictionary<int, (float juego, float puesto)> estadoSangre = new Dictionary<int, (float, float)>();
        static void AjustarSangre()
        {
            bool on = Activar.Value && Encendido && SangreReal.Value;
            for (int i = sangre.Count - 1; i >= 0; i--)
            {
                var m = sangre[i];
                if (m == null) { sangre.RemoveAt(i); continue; }
                int id = m.GetInstanceID(); float v = m.GetFloat(PropSangre);
                if (!estadoSangre.TryGetValue(id, out var e) || Math.Abs(v - e.puesto) > 1e-4f) e = (v, v);   // el juego puso un valor nuevo
                float obj = on ? e.juego * Mathf.Max(0.1f, SangreFactor.Value) : e.juego;
                if (Math.Abs(v - obj) > 1e-4f) m.SetFloat(PropSangre, obj);
                estadoSangre[id] = (e.juego, obj);
            }
        }

        public static void Tick()
        {
            try { PastoReal.Tick(Encendido && Activar.Value); } catch (Exception e) { Plugin.Log.LogWarning("Pasto realista: " + e); }
            try { Arboles.Tick(Encendido && Activar.Value); } catch (Exception e) { Plugin.Log.LogWarning("Árboles: " + e.Message); }
            try { Fuego.Animar(); } catch (Exception e) { Plugin.Log.LogWarning("Fuego (animar): " + e.Message); }
            try { if (sangre.Count > 0) AjustarSangre(); } catch (Exception e) { Plugin.Log.LogWarning("Sangre: " + e.Message); sangre.Clear(); }
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
                // la sangre usa un shader propio sin _BaseColor: se detecta antes del filtro de carga
                if (m != null && m.shader != null && m.shader.name == "Shader Graphs/BloodFlow" && m.HasProperty(PropSangre))
                {
                    if (!sangre.Contains(m)) { sangre.Add(m); Plugin.Log.LogInfo($"Sangre: material {m.name} (id {m.GetInstanceID()}) valor del juego {m.GetFloat(PropSangre):0.##}"); }
                    continue;
                }
                // cuerpos "BodyMorph" y vidrios de lluvia (shaders propios sin _BaseColor): también antes del filtro de carga
                if (m != null && m.shader != null && m.shader.isSupported && m.shader.name.StartsWith("Shader Graphs/"))
                {
                    if (PielMate.Value && m.shader.name.Contains("BodyMorph") && EsPielNombre(m.name) && !Hecho(m, "pielmate"))
                    { var cm = MejorarPielMorph(new Cambio { M = m, Cat = "pielmate" }); if (cm != null) nuevos += Registrar(cm); }
                    if (VidrioReflejos.Value && EsVidrioTransparente(m) && !Hecho(m, "vidrio")) nuevos += Registrar(MejorarVidrio(new Cambio { M = m, Cat = "vidrio" }));
                    continue;
                }
                if (m == null || m.shader == null || !m.shader.isSupported || !m.HasProperty("_BaseColor") && !m.HasProperty("_Color")) continue;   // material aún sin cargar: se reintenta
                if (m.name.EndsWith("(tarjeta)")) continue;   // copia plástica de las tarjetas: no se vuelve a ajustar
                int id = m.GetInstanceID();
                if (vistos.Contains(id)) continue;
                vistos.Add(id);
                string sh = m.shader.name;
                if (sh.StartsWith("BruteForce"))
                {
                    if (Pasto.Value && !Hecho(m, "pasto")) nuevos += Registrar(MejorarPasto(new Cambio { M = m, Cat = "pasto" }));
                    continue;
                }
                if (VidrioReflejos.Value && EsVidrioTransparente(m) && !Hecho(m, "vidrio")) nuevos += Registrar(MejorarVidrio(new Cambio { M = m, Cat = "vidrio" }));
                if (sh != "HDRP/Lit") continue;
                if (OjosHumedos.Value && EsOjo(m) && !Hecho(m, "ojos")) { nuevos += Registrar(MejorarOjo(new Cambio { M = m, Cat = "ojos" })); continue; }
                if (PielMate.Value && EsPielNombre(m.name) && !Hecho(m, "pielmate")) nuevos += Registrar(MejorarPielMate(new Cambio { M = m, Cat = "pielmate" }));
                if (EsPielNombre(m.name) && Mathf.Abs(TonoPiel.Value - 1f) > 0.001f && !Hecho(m, "tonopiel")) nuevos += Registrar(TonoDePiel(new Cambio { M = m, Cat = "tonopiel" }));
                if (Piel.Value && EsPiel(m)) { if (!Hecho(m, "piel")) { var c = new Cambio { M = m }; MejorarPiel(c); nuevos += Registrar(c); } continue; }
                string tipo = Clasificar(m.name);
                if (tipo == null) continue;
                if (Microdetalle.Value && EsSuperficie(tipo) && !Hecho(m, "microdetalle")) nuevos += Registrar(MejorarMicro(new Cambio { M = m, Cat = "microdetalle" }));
                if (Relieve.Value && EsSuperficie(tipo) && !Hecho(m, "relieve")) nuevos += Registrar(MejorarRelieve(new Cambio { M = m, Cat = "relieve" }));
                if (Brillo.Value && !Hecho(m, "brillo")) nuevos += Registrar(MejorarBrillo(new Cambio { M = m, Cat = "brillo" }, tipo));
            }
            if (Personajes.Value) nuevos += BarrerPersonajes();
            AjustarTarjetas(Encendido && Activar.Value && PielMate.Value);
            AjustarSuelo(Encendido && Activar.Value && (SueloTierra.Value || PastoReal.Activar.Value));   // el pasto realista va sobre tierra
            Fuego.Revisar(Encendido && Activar.Value);
            BarrerPelo();
            AjustarPerfilPiel(Encendido);
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

        // personajes = materiales en mallas con esqueleto (SkinnedMeshRenderer); se revisa en cada barrido porque los cuerpos aparecen durante la partida
        static int BarrerPersonajes()
        {
            int n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid()) continue;
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || m.shader == null || m.shader.name != "HDRP/Lit" || Hecho(m, "personaje")) continue;
                    string nm = m.name;
                    if (nm.Contains("Hair") || nm.Contains("HAIR") || nm.Contains("Transparency") || nm.Contains("Eye") || nm.Contains("EYE") || nm.Contains("Teeth") || nm.Contains("Teeh") || nm.Contains("Lash")) continue;
                    if (!m.HasProperty("_NormalMap") || m.GetTexture("_NormalMap") == null) continue;
                    var c = new Cambio { M = m, Cat = "personaje" };
                    c.Float("_NormalScale", Mathf.Clamp(m.GetFloat("_NormalScale") * RelievePersonajes.Value, 0f, 4f));
                    if (Diagnostico.Value) Plugin.Log.LogInfo($"  personaje {r.name} / {nm}: relieve ×{RelievePersonajes.Value:0.00}");
                    n += Registrar(c);
                }
            }
            return n;
        }

        static bool EsOjo(Material m)
        {
            string n = m.name;
            if (!(n.Contains("Eye") || n.Contains("EYES")) || n.IndexOf("lash", StringComparison.OrdinalIgnoreCase) >= 0 || n.Contains("Glow") || n.Contains("Black") || n.Contains("Gone")) return false;
            if (m.HasProperty("_Metallic") && m.GetFloat("_Metallic") > 0.5f) return false;   // ojos estilizados (metal) a propósito
            return true;
        }

        // ojo real = superficie mojada (brillo ~0,9): aparece el reflejo de luz; los de cadáver quedan algo menos (0,85)
        static Cambio MejorarOjo(Cambio c)
        {
            var m = c.M;
            float obj = m.name.Contains("Dead") ? 0.85f : 0.92f;
            bool mascara = m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null;
            if (mascara)
            {
                if (m.HasProperty("_SmoothnessRemapMax") && m.GetFloat("_SmoothnessRemapMax") < obj + 0.03f) c.Float("_SmoothnessRemapMax", Mathf.Min(1f, obj + 0.03f));
                if (m.HasProperty("_SmoothnessRemapMin") && m.GetFloat("_SmoothnessRemapMin") < obj - 0.3f) c.Float("_SmoothnessRemapMin", obj - 0.3f);
            }
            else if (m.HasProperty("_Smoothness") && m.GetFloat("_Smoothness") < obj) c.Float("_Smoothness", obj);
            if (Diagnostico.Value && !c.Vacio) Plugin.Log.LogInfo($"  ojo {m.name}: {c.Floats.Count} cambios");
            return c;
        }

        // ---------- perfil de difusión de la piel ("Skin") ----------
        // Medido 2026-11-24: ×0,5 marca poros y pliegues; ×2–3 deja la piel cerosa y borrosa.
        static DiffusionProfileSettings perfilPiel; static Color dispOriginal; static float dispAplicada = -1f;
        static void AjustarPerfilPiel(bool on)
        {
            try
            {
                if (perfilPiel == null)
                {
                    foreach (var d in Resources.FindObjectsOfTypeAll<DiffusionProfileSettings>())
                        if (d != null && d.name == "Skin" && d.profile != null) { perfilPiel = d; dispOriginal = d.profile.scatteringDistance; break; }
                    if (perfilPiel == null) return;
                }
                float f = on && Activar.Value ? Mathf.Clamp(DispersionPiel.Value, 0.2f, 3f) : 1f;
                if (Math.Abs(f - dispAplicada) < 1e-4f) return;
                var pr = perfilPiel.profile;
                pr.scatteringDistance = new Color(dispOriginal.r * f, dispOriginal.g * f, dispOriginal.b * f, dispOriginal.a);
                pr.Validate(); pr.UpdateKernel(); perfilPiel.UpdateCache();
                dispAplicada = f;
                Plugin.Log.LogInfo($"Piel: dispersión ×{f:0.00} ({pr.scatteringDistance})");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Perfil de piel: " + e.Message); }
        }

        // ---------- pelo: HDRP/Lit (tiras planas) -> HDRP/Hair del juego ----------
        // Se clona un material de pelo HDRP/Hair ya configurado por el juego (estado de render y claves correctas)
        // y se le ponen las texturas de cada peinado. Probado 2026-11-24: hebras finas y brillo de pelo real.
        static Material refPelo;
        static readonly Dictionary<int, Material> clonPelo = new Dictionary<int, Material>();
        static readonly List<(SkinnedMeshRenderer r, int i, Material orig, Material clon)> pelos = new List<(SkinnedMeshRenderer, int, Material, Material)>();
        static bool EsPeloLit(Material m)
        {
            if (m == null || m.shader == null || m.shader.name != "HDRP/Lit") return false;
            string n = m.name;
            if (!(n.Contains("Hair") || n.Contains("HAIR")) || n.Contains("Scalp") || n.Contains("Cap")) return false;
            return m.HasProperty("_BaseColorMap") && m.GetTexture("_BaseColorMap") != null;
        }
        static void BarrerPelo()
        {
            if (!PeloRealista.Value || !Activar.Value || !Encendido) return;
            if (refPelo == null)
                foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                    if (m != null && m.shader != null && m.shader.name == "HDRP/Hair" && m.name.Contains("Merge_Pbr") && !m.name.Contains("(pelo")) { refPelo = m; break; }
            if (refPelo == null) return;   // el material de referencia aún no está cargado: se reintenta en el próximo barrido
            int n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid()) continue;
                var mats = r.sharedMaterials; bool cambio = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!EsPeloLit(m)) continue;
                    if (!clonPelo.TryGetValue(m.GetInstanceID(), out var clon) || clon == null)
                    {
                        clon = new Material(refPelo) { name = m.name + " (pelo HDRP)" };
                        clon.SetTexture("_BaseColorMap", m.GetTexture("_BaseColorMap"));
                        clon.SetTexture("_NormalMap", m.HasProperty("_NormalMap") ? m.GetTexture("_NormalMap") : null);
                        clon.SetTexture("_MaskMap", m.HasProperty("_MaskMap") ? m.GetTexture("_MaskMap") : null);
                        if (m.HasProperty("_BaseColor")) clon.SetColor("_BaseColor", m.GetColor("_BaseColor"));
                        if (m.HasProperty("_AlphaCutoff") && clon.HasProperty("_AlphaClipThreshold")) clon.SetFloat("_AlphaClipThreshold", Mathf.Clamp(m.GetFloat("_AlphaCutoff"), 0.05f, 0.6f));
                        // la referencia es pelirroja: brillo y transmisión neutros para que sirva a cualquier color de pelo
                        if (clon.HasProperty("_SpecularColor")) clon.SetColor("_SpecularColor", new Color(0.55f, 0.55f, 0.55f, 1f));
                        if (clon.HasProperty("_TransmissionColor")) clon.SetColor("_TransmissionColor", new Color(0.6f, 0.55f, 0.5f, 1f));
                        clon.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        clonPelo[m.GetInstanceID()] = clon;
                    }
                    m.hideFlags |= HideFlags.DontUnloadUnusedAsset;   // si no, la limpieza de memoria lo borra y al volver queda magenta
                    pelos.Add((r, i, m, clon)); mats[i] = clon; cambio = true; n++;
                }
                if (cambio) r.sharedMaterials = mats;
            }
            if (n > 0) Plugin.Log.LogInfo("Pelo: " + n + " peinados con shader de pelo real");
        }
        static void RevertirPelo()
        {
            int n = 0;
            foreach (var (r, i, orig, clon) in pelos)
            {
                if (r == null) continue;
                var mats = r.sharedMaterials;
                if (orig == null) continue;   // original perdido: mejor dejar el pelo mejorado que un material vacío
                if (i < mats.Length && mats[i] == clon) { mats[i] = orig; r.sharedMaterials = mats; n++; }
            }
            pelos.Clear();
            if (n > 0) Plugin.Log.LogInfo("Pelo: " + n + " peinados devueltos al original");
        }

        static bool EsPielNombre(string n) =>
            (n.Contains("Skin") || n.Contains("SKIN") || n.Contains("Arms")) && !n.Contains("Hair") && !n.Contains("Eye") && !n.Contains("EYES") && !n.Contains("Teeth");

        // la piel del juego trae _Metallic = 1 en varios cuerpos y brillo de hasta 0,9: bajo la luz de inspección parece plástico o metal
        static Cambio MejorarPielMate(Cambio c)
        {
            var m = c.M;
            float tope = Mathf.Clamp01(PielBrilloMax.Value);
            if (m.HasProperty("_Metallic") && m.GetFloat("_Metallic") > 0f) c.Float("_Metallic", 0f);
            if (m.HasProperty("_MetallicRemapMax") && m.GetFloat("_MetallicRemapMax") > 0f) c.Float("_MetallicRemapMax", 0f);
            bool mascara = m.HasProperty("_MaskMap") && m.GetTexture("_MaskMap") != null;
            if (mascara)
            {
                if (m.HasProperty("_SmoothnessRemapMax") && m.GetFloat("_SmoothnessRemapMax") > tope) c.Float("_SmoothnessRemapMax", tope);
                if (m.HasProperty("_SmoothnessRemapMin") && m.GetFloat("_SmoothnessRemapMin") > tope * 0.5f) c.Float("_SmoothnessRemapMin", tope * 0.5f);
            }
            else if (m.HasProperty("_Smoothness") && m.GetFloat("_Smoothness") > tope) c.Float("_Smoothness", tope);
            if (Diagnostico.Value && !c.Vacio) Plugin.Log.LogInfo($"  piel mate {m.name}: {c.Floats.Count} cambios");
            return c;
        }

        // Oscurece el color base de la piel (multiplica _BaseColor); el original se guarda en el Cambio para volver atrás
        static Cambio TonoDePiel(Cambio c)
        {
            var m = c.M; float f = Mathf.Clamp(TonoPiel.Value, 0.3f, 1.2f);
            string p = m.HasProperty("_BaseColor") ? "_BaseColor" : m.HasProperty("_Color") ? "_Color" : null;
            if (p == null) return c;
            Color a = m.GetColor(p);
            c.Vector(p, new Vector4(a.r * f, a.g * f, a.b * f, a.a));
            if (Diagnostico.Value) Plugin.Log.LogInfo($"  tono piel {m.name}: {a} ×{f:0.##}");
            return c;
        }

        // Cuerpos con el shader "BodyMorph" (los que se deforman en los sustos, p. ej. la anciana del tutorial): no tienen
        // control de brillo; el brillo sale del canal alfa de su mapa de máscara. Se usa una copia de la máscara con ese canal
        // multiplicado por PielBrilloMax, igual que lo que hace el tope de brillo en las pieles HDRP/Lit.
        static readonly Dictionary<int, Texture2D> mascarasMate = new Dictionary<int, Texture2D>();
        static Cambio MejorarPielMorph(Cambio c)
        {
            var m = c.M; var sh = m.shader; bool alguna = false;
            for (int i = 0; i < sh.GetPropertyCount(); i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string d = sh.GetPropertyDescription(i);
                if (d != "Mask Map" && d != "Mask Map2") continue;
                var t = m.GetTexture(sh.GetPropertyName(i));
                if (t == null) continue;
                var mate = MascaraMate(t, Mathf.Clamp01(PielBrilloMax.Value));
                if (mate == null) return null;   // se reintenta en el próximo barrido
                c.Textura(sh.GetPropertyName(i), mate); alguna = true;
            }
            if (Diagnostico.Value && alguna) Plugin.Log.LogInfo($"  piel mate (BodyMorph) {m.name}");
            return c;
        }

        static Texture2D MascaraMate(Texture src, float factor)
        {
            int clave = src.GetInstanceID();
            if (mascarasMate.TryGetValue(clave, out var hecha) && hecha != null) return hecha;
            if (src.name.EndsWith("(mate)")) return src as Texture2D;
            int w = Math.Min(src.width, 2048), h = Math.Min(src.height, 2048);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var antes = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, true, true) { name = src.name + " (mate)", wrapMode = src.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                var px = tex.GetPixels32(0);
                int n0 = px.Length;
                for (int i = 0; i < n0; i++) { var q = px[i]; q.a = (byte)(q.a * factor + 0.5f); px[i] = q; }
                tex.SetPixels32(px, 0);
                tex.Apply(true, true);   // mips recalculados y sin copia en la memoria del CPU
                mascarasMate[clave] = tex;
                Plugin.Log.LogInfo($"Piel mate: máscara {src.name} {src.width}x{src.height} -> copia {w}x{h} con brillo ×{factor:0.##}");
                return tex;
            }
            catch (Exception e) { Plugin.Log.LogWarning("Piel mate (máscara): " + e.Message); mascarasMate[clave] = null; return null; }
            finally { RenderTexture.active = antes; RenderTexture.ReleaseTemporary(rt); }
        }

        // ---------- vidrios: reflejos en pantalla también en superficies transparentes ----------
        // El juego deja los vidrios sin SSR transparente (_DISABLE_SSR_TRANSPARENT): solo reflejan la sonda de reflejos, borrosa.
        static bool EsVidrioTransparente(Material m)
        {
            if (!m.HasProperty("_ReceivesSSRTransparent") || !m.HasProperty("_SurfaceType") || m.GetFloat("_SurfaceType") < 0.5f) return false;
            string n = m.name;
            return n.Contains("Glass") || n.Contains("Window") || n.Contains("glass");
        }
        static Cambio MejorarVidrio(Cambio c)
        {
            var m = c.M;
            c.Float("_ReceivesSSRTransparent", 1f);
            c.Clave("_DISABLE_SSR_TRANSPARENT", false);
            if (m.HasProperty("_TransparentDepthPrepassEnable")) c.Float("_TransparentDepthPrepassEnable", 1f);
            c.Pase("TransparentDepthPrepass", true);
            // bit 8 del stencil = "trazar rayo de reflejo" (HDRP StencilUsage.TraceReflectionRay), lo escribe el pase de profundidad
            if (m.HasProperty("_StencilRefDepth")) c.Float("_StencilRefDepth", (float)((int)m.GetFloat("_StencilRefDepth") | 8));
            if (m.HasProperty("_StencilWriteMaskDepth")) c.Float("_StencilWriteMaskDepth", (float)((int)m.GetFloat("_StencilWriteMaskDepth") | 8));
            if (Diagnostico.Value) Plugin.Log.LogInfo($"  vidrio con reflejos {m.name} [{m.shader.name}]");
            return c;
        }

        // ---------- suelo exterior: pasto 3D (BruteForce) -> tierra fotográfica (material DirtNoFlow del sótano) ----------
        static readonly List<(Renderer r, Material orig)> suelos = new List<(Renderer, Material)>();
        static readonly Dictionary<int, Material> tierraPorPasto = new Dictionary<int, Material>();
        static int sueloFirma = int.MinValue; static bool sueloOn; static float sueloEscala;
        static void AjustarSuelo(bool on)
        {
            if (sueloFirma != firmaEscena) { sueloFirma = firmaEscena; suelos.RemoveAll(x => x.r == null); sueloOn = false; foreach (var x in suelos) if (x.orig != null) x.r.sharedMaterial = x.orig; suelos.Clear(); }
            if (on == sueloOn && (!on || Math.Abs(sueloEscala - TierraEscala.Value) < 1e-4f)) return;
            if (!on)
            {
                foreach (var x in suelos) if (x.r != null && x.orig != null) x.r.sharedMaterial = x.orig;
                suelos.Clear(); sueloOn = false; Plugin.Log.LogInfo("Suelo: pasto original"); return;
            }
            Material plantilla = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>()) if (m != null && m.name == "DirtNoFlow" && m.shader != null && m.shader.name == "HDRP/Lit") { plantilla = m; break; }
            if (plantilla == null) return;   // esta escena no la trae: se reintenta
            foreach (var x in suelos) if (x.r != null && x.orig != null) x.r.sharedMaterial = x.orig;
            suelos.Clear();
            foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (r == null || !r.gameObject.scene.IsValid() || r.sharedMaterial == null || r.sharedMaterial.shader == null || !r.sharedMaterial.shader.name.StartsWith("BruteForce")) continue;
                var orig = r.sharedMaterial;
                orig.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                var tierra = new Material(plantilla) { name = "Tierra (Mortuary Realista)" };
                CargarTierraNatural();
                if (tierraColor != null) { tierra.SetTexture("_BaseColorMap", tierraColor); tierra.SetTexture("_NormalMap", tierraNormal); tierra.SetTexture("_MaskMap", tierraMascara); }
                var b = r.bounds; float esc = Mathf.Max(0.5f, tierraColor != null ? TierraEscala.Value : 2.5f);
                tierra.SetVector("_BaseColorMap_ST", new Vector4(Mathf.Max(1f, b.size.x / esc), Mathf.Max(1f, b.size.z / esc), 0, 0));
                r.sharedMaterial = tierra;
                suelos.Add((r, orig));
            }
            sueloOn = true; sueloEscala = TierraEscala.Value;
            Plugin.Log.LogInfo($"Suelo: {suelos.Count} zonas de pasto cambiadas por tierra fotográfica");
        }

        // tierra sin repetición visible (herramientas/tierra_natural.py): trozos al azar girados, unidos por el borde de las piedras
        static Texture2D tierraColor, tierraNormal, tierraMascara; static bool tierraIntentada;
        static void CargarTierraNatural()
        {
            if (tierraIntentada) return; tierraIntentada = true;
            try
            {
                string dir = System.IO.Path.Combine(TexturasIA.carpetaReal ?? "", "especiales");
                Texture2D L(string f, TextureFormat fmt, bool lineal)
                {
                    var d = System.IO.File.ReadAllBytes(System.IO.Path.Combine(dir, f));
                    int w = BitConverter.ToInt32(d, 8), h = BitConverter.ToInt32(d, 12), niv = BitConverter.ToInt32(d, 20), largo = BitConverter.ToInt32(d, 24);
                    var datos = new byte[largo]; Buffer.BlockCopy(d, 28, datos, 0, largo);
                    var t = new Texture2D(w, h, fmt, niv, lineal) { name = f, anisoLevel = 8, wrapMode = TextureWrapMode.Repeat };
                    t.LoadRawTextureData(datos); t.Apply(false, true); t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    return t;
                }
                if (!System.IO.File.Exists(System.IO.Path.Combine(dir, "tierra_color.mtex"))) return;
                tierraColor = L("tierra_color.mtex", TextureFormat.BC7, false);
                tierraNormal = L("tierra_normal.mtex", TextureFormat.BC5, true);
                tierraMascara = L("tierra_mascara.mtex", TextureFormat.BC7, true);
                Plugin.Log.LogInfo("Suelo: tierra natural sin repetición cargada");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Suelo (tierra natural): " + e.Message); tierraColor = null; }
        }

        // ---------- tarjetas (credencial de Delver y otras) ----------
        // Comparten atlas con otros objetos (PropSheet1, GenProps1) con metal y brillo altos en la máscara: la tarjeta parecía metal.
        static readonly List<Renderer> tarjetas = new List<Renderer>();
        static int tarjetasFirma = int.MinValue; static float tarjetasProxima; static bool tarjetasOn;
        // Cada tarjeta recibe su PROPIA copia del material (el atlas GenProps1/PropSheet1 lo comparten monedas y collares, que sí
        // son de metal). Así la copia que el juego crea al inspeccionar (Instantiate → "GenProps1_CardFull(Clone)") hereda el
        // material de plástico. Antes se usaba MaterialPropertyBlock y el clon salía con metal 1 y brillo 1 = espejo negro.
        static readonly Dictionary<int, Material> tarjetaMat = new Dictionary<int, Material>();       // id material original → copia plástica
        static readonly Dictionary<Renderer, Material[]> tarjetaOrig = new Dictionary<Renderer, Material[]>();
        static void PlasticoTarjeta(Material copia)
        {
            float b = Mathf.Clamp01(TarjetaBrillo.Value);
            if (copia.HasProperty("_Metallic")) copia.SetFloat("_Metallic", 0f);
            if (copia.HasProperty("_MetallicRemapMin")) copia.SetFloat("_MetallicRemapMin", 0f);
            if (copia.HasProperty("_MetallicRemapMax")) copia.SetFloat("_MetallicRemapMax", 0f);
            if (copia.HasProperty("_SmoothnessRemapMin")) copia.SetFloat("_SmoothnessRemapMin", 0f);
            if (copia.HasProperty("_SmoothnessRemapMax")) copia.SetFloat("_SmoothnessRemapMax", b);
            if (copia.HasProperty("_Smoothness")) copia.SetFloat("_Smoothness", b);
            // emisión solo en las credenciales (atlas PropSheet1 = Raymond, GenProps1 = Rebeca), no en las tarjetas de visita
            if (copia.name.StartsWith("PropSheet1") || copia.name.StartsWith("GenProps1"))
            {
                float e = Mathf.Clamp(TarjetaLuz.Value, 0f, 1f);
                if (copia.HasProperty("_AlbedoAffectEmissive")) copia.SetFloat("_AlbedoAffectEmissive", e > 0f ? 1f : 0f);
                if (copia.HasProperty("_EmissiveExposureWeight")) copia.SetFloat("_EmissiveExposureWeight", 1f);
                // GenProps1 trae mapa de emisión (negro en la tarjeta) que anula la luz: la copia es solo de las tarjetas, se quita
                if (e > 0f && copia.HasProperty("_EmissiveColorMap") && copia.GetTexture("_EmissiveColorMap") != null)
                { copia.SetTexture("_EmissiveColorMap", null); copia.DisableKeyword("_EMISSIVE_COLOR_MAP"); }
                var col = new Color(e, e, e, 1f);
                if (copia.HasProperty("_EmissiveColor")) copia.SetColor("_EmissiveColor", col);
                if (copia.HasProperty("_EmissiveColorLDR")) copia.SetColor("_EmissiveColorLDR", col);
            }
        }
        static void AjustarTarjetas(bool on)
        {
            if (tarjetasFirma != firmaEscena || Time.unscaledTime > tarjetasProxima)
            {
                tarjetasFirma = firmaEscena; tarjetasProxima = Time.unscaledTime + 20f;
                tarjetas.RemoveAll(r => r == null);
                foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>())
                    if (r != null && r.gameObject.scene.IsValid() && r.name.IndexOf("card", StringComparison.OrdinalIgnoreCase) >= 0 && !tarjetas.Contains(r)) { tarjetas.Add(r); tarjetasOn = !on; }
            }
            if (on == tarjetasOn) return;
            tarjetasOn = on;
            int n = 0;
            foreach (var r in tarjetas)
            {
                if (r == null) continue;
                r.SetPropertyBlock(null);
                if (!on)
                {
                    if (tarjetaOrig.TryGetValue(r, out var o)) { r.sharedMaterials = o; tarjetaOrig.Remove(r); }
                    continue;
                }
                var mats = r.sharedMaterials; bool cambio = false;
                var nuevos = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; nuevos[i] = m;
                    if (m != null && m.name.EndsWith("(tarjeta)")) { PlasticoTarjeta(m); continue; }
                    if (m == null || m.shader == null || m.shader.name != "HDRP/Lit") continue;
                    if (!tarjetaMat.TryGetValue(m.GetInstanceID(), out var copia) || copia == null)
                    {
                        copia = new Material(m) { name = m.name + " (tarjeta)" };
                        copia.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                        tarjetaMat[m.GetInstanceID()] = copia;
                    }
                    PlasticoTarjeta(copia);
                    nuevos[i] = copia; cambio = true;
                }
                if (!cambio) continue;
                if (!tarjetaOrig.ContainsKey(r)) tarjetaOrig[r] = mats;
                r.sharedMaterials = nuevos; n++;
            }
            Plugin.Log.LogInfo($"Tarjetas: {n} de {tarjetas.Count} con material de plástico {(on ? "aplicado" : "quitado")}");
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
