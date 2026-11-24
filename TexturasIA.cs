// © Angel — Reemplazo de texturas por versiones escaladas con IA (archivos .mtex + indice.tsv)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal class EntradaTex
    {
        public string Nombre, Rol, Archivo;
        public int Ancho, Alto, W, H, Formato;
        public byte[] Huella;
    }

    internal static class TexturasIA
    {
        internal static ConfigEntry<string> Excluir;
        internal static ConfigEntry<bool> Activar, UsarColor, UsarNormales, LogDetallado, ConservarOriginales;
        internal static ConfigEntry<string> Carpeta;
        internal static ConfigEntry<int> PresupuestoMB, MsPorCuadro;
        internal static ConfigEntry<float> UmbralAmbiguo, UmbralZona, Umbral;
        internal static ConfigEntry<UnityEngine.InputSystem.Key> TeclaAlternar;
        internal static bool Encendidas = true;
        struct Ranura { public Material M; public int Prop; public Texture Original; public Texture2D Nueva; }
        static readonly List<Ranura> ranuras = new List<Ranura>();

        static Dictionary<string, List<EntradaTex>> indice;
        static Dictionary<string, byte[]> huellas64;
        static Texture2D lector64S, lector64L;
        static readonly HashSet<int> vistos = new HashSet<int>();
        static readonly Queue<KeyValuePair<Texture2D, EntradaTex>> cola = new Queue<KeyValuePair<Texture2D, EntradaTex>>();
        static readonly Dictionary<int, Texture2D> sustitutas = new Dictionary<int, Texture2D>();   // plan B: id original -> nueva
        static readonly List<Texture2D> retener = new List<Texture2D>();
        static bool barridoPendiente;
        static string carpetaReal;
        static long bytesAñadidos;
        static int reemplazadas, sinCoincidencia, fallos, enPlanB, omitidasPresupuesto;
        static float proximoEscaneo = 3f;
        static int escaneosPendientes = 1;
        static int firmaEscena = int.MinValue;
        static bool resumenPendiente;
        static float proximoBarridoPeriodico = 30f, momentoLiberar = -1f;
        static Texture2D lectorS, lectorL;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("TexturasIA", "Activar", true, "Reemplaza texturas del juego por versiones escaladas con IA");
            UsarColor = cfg.Bind("TexturasIA", "Color", true, "Reemplaza texturas de color (albedo)");
            UsarNormales = cfg.Bind("TexturasIA", "Normales", true, "Reemplaza mapas de normales (relieve)");
            Carpeta = cfg.Bind("TexturasIA", "Carpeta", "", "Carpeta con indice.tsv y los .mtex; vacío = BepInEx/plugins/MortuaryRealista/TexturasIA");
            PresupuestoMB = cfg.Bind("TexturasIA", "PresupuestoMB", -1, "Máximo de VRAM extra (MB) para las texturas nuevas; -1 = automático según la VRAM de la tarjeta");
            MsPorCuadro = cfg.Bind("TexturasIA", "MsPorCuadro", 15, "Tiempo máximo por cuadro dedicado a cargar texturas (evita tirones)");
            Umbral = cfg.Bind("TexturasIA", "UmbralHuella", 22f, "Diferencia máxima (0-255) entre la miniatura del juego y la de la textura IA");
            Excluir = cfg.Bind("TexturasIA", "Excluir", "", "Partes de nombre de texturas que NO se reemplazan, separadas por coma");
            UmbralZona = cfg.Bind("TexturasIA", "UmbralZona", 10f, "Con texturas del mismo nombre: diferencia máxima en la peor zona de la huella 64x64 (entre cadáveres distintos es 17 o más)");
            UmbralAmbiguo = cfg.Bind("TexturasIA", "UmbralAmbiguo", 8f, "Diferencia máxima cuando hay varias texturas IA con el mismo nombre y tamaño (evita pieles cruzadas entre cadáveres)");
            LogDetallado = cfg.Bind("TexturasIA", "LogDetallado", false, "Escribe en el log cada textura reemplazada o descartada");
            TeclaAlternar = cfg.Bind("TexturasIA", "TeclaAlternar", UnityEngine.InputSystem.Key.F7, "Alterna en vivo entre texturas IA y originales (requiere ConservarOriginales = true)");
            ConservarOriginales = cfg.Bind("TexturasIA", "ConservarOriginales", false, "Mantiene las texturas originales en memoria para comparar con F7 (usa ~1 GB más de VRAM)");
        }

        public static void Iniciar()
        {
            try
            {
                carpetaReal = string.IsNullOrWhiteSpace(Carpeta.Value)
                    ? Path.Combine(Paths.PluginPath, "MortuaryRealista", "TexturasIA") : Carpeta.Value;
                string idx = Path.Combine(carpetaReal, "indice.tsv");
                if (!File.Exists(idx)) { Plugin.Log.LogInfo("Texturas IA: pack de texturas no instalado (opcional): " + idx); return; }
                indice = new Dictionary<string, List<EntradaTex>>();
                int n = 0;
                foreach (var linea in File.ReadAllLines(idx))
                {
                    if (linea.Length == 0 || linea[0] == '#') continue;
                    var c = linea.Split('\t');
                    if (c.Length < 9) continue;
                    var e = new EntradaTex
                    {
                        Nombre = c[0], Ancho = int.Parse(c[1]), Alto = int.Parse(c[2]), Rol = c[3], Archivo = c[4],
                        W = int.Parse(c[5]), H = int.Parse(c[6]), Formato = int.Parse(c[7]), Huella = Convert.FromHexString(c[8])
                    };
                    string clave = e.Nombre + "|" + e.Ancho + "|" + e.Alto;
                    if (!indice.TryGetValue(clave, out var lista)) indice[clave] = lista = new List<EntradaTex>();
                    lista.Add(e);
                    n++;
                }
                huellas64 = new Dictionary<string, byte[]>();
                string h64 = Path.Combine(carpetaReal, "huellas64.tsv");
                if (File.Exists(h64))
                    foreach (var linea in File.ReadAllLines(h64))
                    {
                        var c = linea.Split('\t');
                        if (c.Length == 2 && c[1].Length == 32768) huellas64[c[0]] = Convert.FromHexString(c[1]);
                    }
                lector64S = new Texture2D(64, 64, TextureFormat.RGBA32, false, false);
                lector64L = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
                lector64S.hideFlags = lector64L.hideFlags = HideFlags.HideAndDontSave;
                lectorS = new Texture2D(16, 16, TextureFormat.RGBA32, false, false);
                lectorL = new Texture2D(16, 16, TextureFormat.RGBA32, false, true);
                lectorS.hideFlags = lectorL.hideFlags = HideFlags.HideAndDontSave;
                Plugin.Log.LogInfo("Texturas IA: índice con " + n + " texturas (" + indice.Count + " nombres) en " + carpetaReal);
            }
            catch (Exception e) { Plugin.Log.LogError("Texturas IA: no se pudo leer el índice: " + e); indice = null; }
        }

        public static void Tick()
        {
            if (indice == null || !Activar.Value) return;
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb[TeclaAlternar.Value].wasPressedThisFrame)
                {
                    if (ConservarOriginales.Value) Alternar(!Encendidas);
                    else Plugin.Log.LogInfo("Texturas IA: para comparar con F7 pon ConservarOriginales = true");
                }
                var activa = SceneManager.GetActiveScene();
                int firma = SceneManager.sceneCount * 7919 + activa.handle;
                if (firma != firmaEscena)
                {
                    firmaEscena = firma;
                    escaneosPendientes = 4;
                    proximoEscaneo = Time.unscaledTime + 1f;
                }
                if (escaneosPendientes > 0 && Time.unscaledTime >= proximoEscaneo)
                {
                    Escanear();
                    escaneosPendientes--;
                    proximoEscaneo = Time.unscaledTime + (escaneosPendientes == 3 ? 3f : escaneosPendientes == 2 ? 8f : 20f);
                }
                Procesar();
                if (barridoPendiente && cola.Count == 0) BarrerMateriales();
                if (Time.unscaledTime >= proximoBarridoPeriodico)
                {
                    proximoBarridoPeriodico = Time.unscaledTime + 30f;   // objetos que aparecen más tarde (cadáveres, etc.)
                    if (cola.Count == 0 && Encendidas && sustitutas.Count > 0) BarrerMateriales();
                }
                if (momentoLiberar > 0 && Time.unscaledTime >= momentoLiberar && cola.Count == 0)
                {
                    momentoLiberar = -1f;
                    var swL = Stopwatch.StartNew();
                    Resources.UnloadUnusedAssets();   // libera las originales que ya no usa ningún material
                    Plugin.Log.LogInfo($"Texturas IA: originales liberadas de la memoria ({swL.ElapsedMilliseconds} ms en el cuadro)");
                }
                if (resumenPendiente && cola.Count == 0)
                {
                    resumenPendiente = false;
                    Plugin.Log.LogInfo("Texturas IA: " + reemplazadas + " reemplazadas (" + enPlanB + " por plan B), +" + (bytesAñadidos >> 20) +
                        " MB de VRAM, " + sinCoincidencia + " sin coincidencia, " + fallos + " fallos, " + omitidasPresupuesto + " fuera de presupuesto");
                }
            }
            catch (Exception e) { Plugin.Log.LogError("Texturas IA: " + e); escaneosPendientes = 0; }
        }

        static void Escanear()
        {
            var sw = Stopwatch.StartNew();
            var todas = Resources.FindObjectsOfTypeAll<Texture2D>();
            int total = 0, nuevas = 0;
            foreach (var t in todas)
            {
                total++;
                if (t == null) continue;
                int id = t.GetInstanceID();
                if (vistos.Contains(id)) continue;
                vistos.Add(id);
                if (!indice.TryGetValue(t.name + "|" + t.width + "|" + t.height, out var lista)) continue;
                if (EsExcluida(t.name)) continue;
                bool srgb = GraphicsFormatUtility.IsSRGBFormat(t.graphicsFormat);
                byte[] h = Huella(t, srgb);
                EntradaTex mejor = null;
                double dMejor = double.MaxValue;
                foreach (var e in lista)
                {
                    if (e.Rol == "color" && !UsarColor.Value) continue;
                    if (e.Rol == "normal" && !UsarNormales.Value) continue;
                    double d = Math.Min(Distancia(h, e.Huella, false), Distancia(h, e.Huella, true));
                    if (d < dMejor) { dMejor = d; mejor = e; }
                }
                // Con varias texturas del mismo nombre y tamaño (pieles de cada cadáver, pelo, dientes) la miniatura
                // se parece mucho entre ellas: se exige más parecido para no pintar la piel de otro cuerpo.
                double umbral = lista.Count > 1 ? Math.Min(Umbral.Value, UmbralAmbiguo.Value) : Umbral.Value;
                if (lista.Count > 1 && huellas64 != null)
                {
                    // Desempate fino: huella 64x64 comparada por zonas de 8x8 (la peor zona manda: una cara distinta se nota)
                    byte[] h64 = null;
                    EntradaTex mejor64 = null; double z64 = double.MaxValue; int conHuella = 0;
                    foreach (var e in lista)
                    {
                        if (!huellas64.TryGetValue(e.Archivo, out var ref64)) continue;
                        if (h64 == null) h64 = Huella(t, srgb, 64);
                        conHuella++;
                        double z = Math.Min(PeorZona(h64, ref64, false), PeorZona(h64, ref64, true));
                        if (z < z64) { z64 = z; mejor64 = e; }
                    }
                    if (conHuella > 0)
                    {
                        if (LogDetallado.Value) Plugin.Log.LogInfo($"  zonas {t.name} {t.width}x{t.height}: mejor {mejor64?.Archivo} peor zona {z64:0.0} (16x16 dif {dMejor:0.0} {mejor?.Archivo})");
                        if (mejor64 != null && z64 <= UmbralZona.Value) { mejor = mejor64; dMejor = 0; }
                        else mejor = null;
                    }
                }
                if (mejor != null && dMejor <= umbral)
                {
                    cola.Enqueue(new KeyValuePair<Texture2D, EntradaTex>(t, mejor));
                    nuevas++;
                    if (LogDetallado.Value) Plugin.Log.LogInfo($"  coincide {t.name} {t.width}x{t.height} -> {mejor.Archivo} (dif {dMejor:0.0})");
                }
                else
                {
                    sinCoincidencia++;
                    if (LogDetallado.Value) Plugin.Log.LogInfo($"  sin coincidencia {t.name} {t.width}x{t.height} (mejor dif {dMejor:0.0})");
                }
            }
            if (nuevas > 0) { resumenPendiente = true; Ordenar(); }
            if (sustitutas.Count > 0) barridoPendiente = true;
            Plugin.Log.LogInfo($"Texturas IA: escaneo de {total} texturas, {nuevas} nuevas en cola ({sw.ElapsedMilliseconds} ms)");
        }

        static string[] excluidas; static string excluidasTexto;
        static bool EsExcluida(string n)
        {
            if (excluidasTexto != Excluir.Value)
            {
                excluidasTexto = Excluir.Value;
                var l = new List<string>();
                foreach (var x in excluidasTexto.Split(',')) if (x.Trim().Length > 0) l.Add(x.Trim());
                excluidas = l.ToArray();
            }
            foreach (var x in excluidas) if (n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static double PeorZona(byte[] a, byte[] b, bool voltear)
        {
            double peor = 0;
            for (int by = 0; by < 8; by++)
                for (int bx = 0; bx < 8; bx++)
                {
                    long s = 0;
                    for (int y = 0; y < 8; y++)
                    {
                        int ya = by * 8 + y, yb = voltear ? 63 - ya : ya;
                        for (int x = 0; x < 8; x++)
                        {
                            int ia = (ya * 64 + bx * 8 + x) * 4, ib = (yb * 64 + bx * 8 + x) * 4;
                            s += Math.Abs(a[ia] - b[ib]) + Math.Abs(a[ia + 1] - b[ib + 1]) + Math.Abs(a[ia + 2] - b[ib + 2]);
                        }
                    }
                    double z = s / 192.0;
                    if (z > peor) peor = z;
                }
            return peor;
        }

        static byte[] Huella(Texture2D t, bool srgb, int lado = 16)
        {
            var rt = RenderTexture.GetTemporary(lado, lado, 0, RenderTextureFormat.ARGB32,
                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            var previa = RenderTexture.active;
            Graphics.Blit(t, rt);
            RenderTexture.active = rt;
            var lector = lado == 64 ? (srgb ? lector64S : lector64L) : (srgb ? lectorS : lectorL);
            lector.ReadPixels(new Rect(0, 0, lado, lado), 0, 0, false);
            RenderTexture.active = previa;
            RenderTexture.ReleaseTemporary(rt);
            var px = lector.GetPixels32();
            var b = new byte[lado * lado * 4];
            for (int i = 0; i < lado * lado; i++)
            {
                var c = px[i];
                b[i * 4] = c.r; b[i * 4 + 1] = c.g; b[i * 4 + 2] = c.b; b[i * 4 + 3] = c.a;
            }
            return b;
        }

        static double Distancia(byte[] a, byte[] b, bool voltear)
        {
            long s = 0;
            for (int y = 0; y < 16; y++)
            {
                int yb = voltear ? 15 - y : y;
                for (int x = 0; x < 64; x++) s += Math.Abs(a[y * 64 + x] - b[yb * 64 + x]);
            }
            return s / 1024.0;
        }

        // presupuesto efectivo: el juego sin texturas IA usa ~4,35 GB (medido a 1080p); se dejan ~0,9 GB libres (más lo que ocupe el escritorio)
        const int ReservaMB = 5500;
        static int presupuestoAuto = int.MinValue;
        internal static int Presupuesto
        {
            get
            {
                if (PresupuestoMB.Value >= 0) return PresupuestoMB.Value;
                if (presupuestoAuto == int.MinValue)
                {
                    int vram = SystemInfo.graphicsMemorySize;
                    presupuestoAuto = Math.Max(0, vram - ReservaMB);
                    Plugin.Log.LogInfo($"Texturas IA: presupuesto automático {presupuestoAuto} MB (VRAM de la tarjeta {vram} MB)");
                }
                return presupuestoAuto;
            }
        }

        // prioridad de carga: lo que más se ve primero, así el presupuesto deja fuera lo que menos se nota
        static int Prioridad(EntradaTex e)
        {
            string n = e.Nombre;
            bool normal = e.Rol == "normal";
            if (n.Contains("Skin") || n.Contains("ClothesAndArms") || n.Contains("2_meshes_Merge_Pbr")) return normal ? 1 : 0;
            foreach (var k in new[] { "Wall", "Floor", "Tile", "Ceiling", "plane_", "Modules", "Brick", "Street", "SideWalk", "Grass", "Wood", "Door", "Shingles", "Trim", "Carpet", "Concrete" })
                if (n.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return normal ? 3 : 2;
            return normal ? 5 : 4;
        }

        static void Ordenar()
        {
            var l = new List<KeyValuePair<Texture2D, EntradaTex>>(cola);
            l.Sort((a, b) =>
            {
                int pa = Prioridad(a.Value), pb = Prioridad(b.Value);
                if (pa != pb) return pa.CompareTo(pb);
                return ((long)a.Value.W * a.Value.H).CompareTo((long)b.Value.W * b.Value.H);   // más texturas por MB
            });
            cola.Clear();
            foreach (var x in l) cola.Enqueue(x);
        }

        static void Procesar()
        {
            if (cola.Count == 0) return;
            var sw = Stopwatch.StartNew();
            while (cola.Count > 0 && sw.ElapsedMilliseconds < MsPorCuadro.Value)
            {
                var par = cola.Dequeue();
                if (par.Key == null) continue;   // destruida mientras esperaba
                Reemplazar(par.Key, par.Value);
            }
        }

        static long TamañoAprox(int w, int h, GraphicsFormat gf)
        {
            long b = GraphicsFormatUtility.ComputeMipmapSize(w, h, gf);
            return b + b / 3;
        }

        static unsafe void Reemplazar(Texture2D t, EntradaTex e)
        {
            string nombre = t.name;
            try
            {
                byte[] datos = File.ReadAllBytes(Path.Combine(carpetaReal, e.Archivo));
                if (datos.Length < 28 || datos[0] != 'M' || datos[1] != 'T' || datos[2] != 'X' || datos[3] != '1')
                    throw new Exception("archivo .mtex inválido");
                int W = BitConverter.ToInt32(datos, 8), H = BitConverter.ToInt32(datos, 12);
                int fmt = BitConverter.ToInt32(datos, 16), mips = BitConverter.ToInt32(datos, 20), len = BitConverter.ToInt32(datos, 24);
                if (28 + len > datos.Length) throw new Exception("archivo .mtex truncado");
                var original = t.graphicsFormat;
                bool srgb = GraphicsFormatUtility.IsSRGBFormat(original);
                GraphicsFormat gf = fmt == 1 ? (srgb ? GraphicsFormat.RGBA_BC7_SRGB : GraphicsFormat.RGBA_BC7_UNorm) : GraphicsFormat.RG_BC5_UNorm;
                long extra = len - TamañoAprox(t.width, t.height, original);
                if ((bytesAñadidos + extra) >> 20 > Presupuesto) { omitidasPresupuesto++; return; }

                var wrapU = t.wrapModeU; var wrapV = t.wrapModeV; var filtro = t.filterMode; int aniso = t.anisoLevel; float sesgo = t.mipMapBias;
                bool hecho = false;
                try
                {
                    if (t.Reinitialize(W, H, gf, mips > 1))
                    {
                        fixed (byte* p = datos) t.LoadRawTextureData((IntPtr)(p + 28), len);
                        t.Apply(false, true);
                        t.wrapModeU = wrapU; t.wrapModeV = wrapV; t.filterMode = filtro; t.anisoLevel = aniso; t.mipMapBias = sesgo;
                        hecho = true;
                    }
                }
                catch (Exception ex)
                {
                    if (LogDetallado.Value) Plugin.Log.LogInfo("  en sitio no se pudo (" + nombre + "): " + ex.Message);
                }
                if (!hecho)
                {
                    // Plan B: textura nueva y se cambia en los materiales
                    var nt = new Texture2D(W, H, fmt == 1 ? TextureFormat.BC7 : TextureFormat.BC5, mips > 1, !(fmt == 1 && srgb));
                    fixed (byte* p = datos) nt.LoadRawTextureData((IntPtr)(p + 28), len);
                    nt.Apply(false, true);
                    nt.name = nombre; nt.wrapModeU = wrapU; nt.wrapModeV = wrapV; nt.filterMode = filtro; nt.anisoLevel = aniso; nt.mipMapBias = sesgo;
                    nt.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    sustitutas[t.GetInstanceID()] = nt;
                    retener.Add(nt);
                    barridoPendiente = true;
                    enPlanB++;
                }
                bytesAñadidos += (hecho || !ConservarOriginales.Value) ? extra : len;
                reemplazadas++;
                if (LogDetallado.Value) Plugin.Log.LogInfo($"  reemplazada {nombre} -> {W}x{H} {gf}{(hecho ? "" : " (plan B)")}");
            }
            catch (Exception ex)
            {
                fallos++;
                Plugin.Log.LogWarning("Texturas IA: no se pudo reemplazar " + nombre + ": " + ex.Message);
            }
        }

        public static void Alternar(bool on)
        {
            Encendidas = on;
            int n = 0;
            foreach (var r in ranuras)
            {
                if (r.M == null) continue;
                r.M.SetTexture(r.Prop, on ? (Texture)r.Nueva : r.Original);
                n++;
            }
            Plugin.Log.LogInfo("Texturas IA " + (on ? "ACTIVAS" : "ORIGINALES") + " (" + n + " ranuras)");
        }

        static void BarrerMateriales()
        {
            barridoPendiente = false;
            if (sustitutas.Count == 0) return;
            var sw = Stopwatch.StartNew();
            int cambios = 0, mats = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
            {
                if (m == null) continue;
                mats++;
                var ids = m.GetTexturePropertyNameIDs();
                foreach (int id in ids)
                {
                    var tx = m.GetTexture(id);
                    if (tx == null) continue;
                    if (sustitutas.TryGetValue(tx.GetInstanceID(), out var nt))
                    {
                        if (ConservarOriginales.Value) ranuras.Add(new Ranura { M = m, Prop = id, Original = tx, Nueva = nt });
                        if (Encendidas) m.SetTexture(id, nt);
                        cambios++;
                    }
                }
            }
            if (cambios > 0 || sw.ElapsedMilliseconds > 30)
                Plugin.Log.LogInfo($"Texturas IA: barrido de {mats} materiales, {cambios} cambios ({sw.ElapsedMilliseconds} ms)");
            if (cambios > 0)
            {
                Plugin.Log.LogInfo("Texturas IA: plan B aplicado en " + cambios + " ranuras de materiales");
                if (!ConservarOriginales.Value) momentoLiberar = Time.unscaledTime + 2f;
            }
        }
    }
}
