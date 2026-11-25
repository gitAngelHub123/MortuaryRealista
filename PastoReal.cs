// © Angel — Pasto realista hecho solo con código: hojas 3D (no capas planas como el pasto del juego).
// Cada mata tiene ~10 hojas curvas con ancho que se afina hacia la punta; las matas se reparten con ruido (zonas tupidas,
// zonas peladas, alturas distintas) solo sobre la tierra del patio (malla pPlane2), nunca sobre calle, veredas ni edificios.
// El color sale de una textura generada aquí (base oscura -> punta clara; variantes verdes, secas y amarillas).
// Material: copia del de las hojas de los árboles del juego (doble cara y luz que traspasa la hoja).
// Rendimiento: el patio se divide en cuadros de 4 m con dos versiones (completa cerca, 1/4 de hojas a media distancia, nada lejos).
// Sin viento: el juego no permite compilar shaders nuevos y el HDRP/Lit no mueve vértices.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal static class PastoReal
    {
        internal static ConfigEntry<bool> Activar;
        internal static ConfigEntry<float> Densidad, Altura, DistanciaCerca, DistanciaLejos;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("Materiales", "PastoRealista", false, "Pasto de hojas 3D hecho por el mod (reemplaza el pasto de capas del juego; pone tierra debajo)");
            Densidad = cfg.Bind("Materiales", "PastoRealistaDensidad", 1f, "Cantidad de matas de pasto (0,3 a 2; más = más tupido y más carga para la tarjeta)");
            Altura = cfg.Bind("Materiales", "PastoRealistaAltura", 1f, "Altura del pasto (0,5 a 2)");
            DistanciaCerca = cfg.Bind("Materiales", "PastoRealistaDistancia", 14f, "Metros hasta donde se dibuja el pasto completo");
            DistanciaLejos = cfg.Bind("Materiales", "PastoRealistaDistanciaLejos", 45f, "Metros hasta donde se dibuja el pasto (más lejos solo tierra)");
        }

        internal static bool Encendido => Activar.Value;

        class Cuadro { public GameObject Cerca, Lejos; public Vector3 Centro; }
        static readonly List<Cuadro> cuadros = new List<Cuadro>();
        static GameObject raiz;
        static Material mat;
        static Texture2D tex;
        static int firmaHecha = int.MinValue;
        static string ajustesHechos = "";
        static float proximoLod;
        // construcción repartida en varios cuadros de juego para no congelar la imagen
        static Queue<Vector2Int> pendientes;
        static Bounds zona; static Collider sueloCol; static Renderer sueloRen;

        public static void Tick(bool on)
        {
            on &= Activar.Value;
            var esc = SceneManager.GetActiveScene();
            int firma = SceneManager.sceneCount * 7919 + esc.handle;
            string ajustes = $"{Densidad.Value:0.00}|{Altura.Value:0.00}";
            if (!on) { if (raiz != null) Quitar(); firmaHecha = int.MinValue; return; }
            if (firma != firmaHecha || ajustes != ajustesHechos)
            {
                Quitar();
                if (!Preparar()) return;   // escena sin patio: se reintenta
                firmaHecha = firma; ajustesHechos = ajustes;
            }
            if (pendientes != null && pendientes.Count > 0)
            {
                float t0 = Time.realtimeSinceStartup;
                while (pendientes.Count > 0 && Time.realtimeSinceStartup - t0 < 0.006f) ConstruirCuadro(pendientes.Dequeue());
                if (pendientes.Count == 0) Plugin.Log.LogInfo($"Pasto realista: {cuadros.Count} cuadros de 4 m listos");
            }
            if (Time.unscaledTime >= proximoLod) { proximoLod = Time.unscaledTime + 0.25f; ActualizarDistancias(); }
        }

        static void Quitar()
        {
            if (raiz != null) UnityEngine.Object.Destroy(raiz);
            raiz = null; cuadros.Clear(); pendientes = null;
        }

        static bool Preparar()
        {
            sueloCol = null; sueloRen = null;
            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                if (t != null && t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.name == "pPlane2")
                {
                    sueloRen = t.GetComponent<Renderer>(); sueloCol = t.GetComponent<Collider>();
                    if (sueloRen != null && sueloCol != null) break;
                }
            if (sueloRen == null || sueloCol == null) return false;
            if (mat == null && !CrearMaterial()) return false;
            zona = sueloRen.bounds;
            raiz = new GameObject("Pasto realista (Mortuary Realista)");
            pendientes = new Queue<Vector2Int>();
            int nx = Mathf.CeilToInt(zona.size.x / 4f), nz = Mathf.CeilToInt(zona.size.z / 4f);
            for (int z = 0; z < nz; z++) for (int x = 0; x < nx; x++) pendientes.Enqueue(new Vector2Int(x, z));
            if (clumps == null) CrearMatas();
            return true;
        }

        // ---------- material y textura de color ----------
        static bool CrearMaterial()
        {
            Material hoja = null;
            foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                if (m != null && m.name == "TreeLeafTranslucent" && m.shader != null && m.shader.name == "HDRP/Lit") { hoja = m; break; }
            if (hoja == null) return false;
            tex = CrearTextura();
            mat = new Material(hoja) { name = "Pasto realista (Mortuary Realista)" };
            mat.SetTexture("_BaseColorMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_NormalMap", null); mat.DisableKeyword("_NORMALMAP"); mat.DisableKeyword("_NORMALMAP_TANGENT_SPACE");
            mat.SetFloat("_AlphaCutoffEnable", 0f); mat.DisableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.32f);
            mat.SetFloat("_Thickness", 0.25f);
            mat.enableInstancing = true;
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return true;
        }

        // columnas = variantes de hoja; filas = de la base (v=0) a la punta (v=1)
        static readonly Color[,] variantes =
        {
            { new Color(0.08f, 0.12f, 0.04f), new Color(0.24f, 0.38f, 0.10f), new Color(0.42f, 0.52f, 0.18f) },   // verde
            { new Color(0.07f, 0.11f, 0.04f), new Color(0.19f, 0.32f, 0.08f), new Color(0.33f, 0.45f, 0.14f) },   // verde oscuro
            { new Color(0.10f, 0.13f, 0.05f), new Color(0.32f, 0.40f, 0.13f), new Color(0.55f, 0.56f, 0.24f) },   // verde amarillento
            { new Color(0.12f, 0.10f, 0.05f), new Color(0.34f, 0.29f, 0.14f), new Color(0.55f, 0.47f, 0.27f) },   // seca
        };
        static Texture2D CrearTextura()
        {
            const int W = 64, H = 128;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, true, false) { name = "pasto (Mortuary Realista)", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[W * H];
            var rnd = new System.Random(7);
            for (int x = 0; x < W; x++)
            {
                int var = x / (W / 4);
                float vena = 0.92f + 0.16f * (float)rnd.NextDouble();       // vetas a lo largo de la hoja
                for (int y = 0; y < H; y++)
                {
                    float v = y / (float)(H - 1);
                    Color c = v < 0.5f ? Color.Lerp(variantes[var, 0], variantes[var, 1], v / 0.5f) : Color.Lerp(variantes[var, 1], variantes[var, 2], (v - 0.5f) / 0.5f);
                    float centro = 1f - Mathf.Abs(((x % (W / 4)) + 0.5f) / (W / 4f) - 0.5f) * 0.25f;   // nervio central algo más claro
                    c *= vena * centro;
                    if (v > 0.93f && var != 1) c = Color.Lerp(c, new Color(0.40f, 0.33f, 0.18f), (v - 0.93f) / 0.07f * 0.6f);   // punta algo seca
                    c.a = 1f;
                    px[y * W + x] = c;
                }
            }
            t.SetPixels32(px); t.Apply(true, true);
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return t;
        }

        // ---------- matas: 6 modelos distintos de ~10 hojas curvas ----------
        class Mata { public Vector3[] V; public Vector3[] N; public Vector2[] U; public int[] T; }
        static Mata[] clumps;
        static void CrearMatas()
        {
            clumps = new Mata[6];
            var r = new System.Random(31);
            for (int k = 0; k < clumps.Length; k++)
            {
                var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var T = new List<int>();
                int hojas = 10 + r.Next(7);
                for (int b = 0; b < hojas; b++)
                {
                    float ang = (float)(r.NextDouble() * Math.PI * 2);
                    float rad = (float)Math.Sqrt(r.NextDouble()) * 0.09f;
                    var base0 = new Vector3(Mathf.Cos(ang) * rad, 0f, Mathf.Sin(ang) * rad);
                    float h = 0.16f + (float)r.NextDouble() * 0.28f;
                    float ancho = 0.008f + (float)r.NextDouble() * 0.008f;
                    float dirAng = (float)(r.NextDouble() * Math.PI * 2);
                    var dir = new Vector3(Mathf.Cos(dirAng), 0, Mathf.Sin(dirAng));
                    float lean = 0.15f + (float)r.NextDouble() * 0.45f;           // cuánto se dobla
                    float giro = (float)(r.NextDouble() * Math.PI);
                    var lado = new Vector3(Mathf.Cos(giro), 0, Mathf.Sin(giro));
                    int var = r.NextDouble() < 0.18 ? 3 : r.Next(3);             // ~18 % hojas secas
                    float u0 = var * 0.25f + 0.03f, u1 = var * 0.25f + 0.22f;
                    int i0 = V.Count;
                    const int S = 4;
                    for (int s = 0; s <= S; s++)
                    {
                        float t = s / (float)S;
                        var p = base0 + Vector3.up * (h * t * (1f - lean * 0.35f * t)) + dir * (lean * h * t * t);
                        var tang = (Vector3.up * (1f - lean * 0.7f * t) + dir * (2f * lean * t)).normalized;
                        var nf = Vector3.Cross(lado, tang).normalized;
                        var n = (nf * 0.45f + Vector3.up * 0.55f).normalized;   // normales hacia arriba: luz suave como pasto real
                        if (s < S)
                        {
                            float w = ancho * Mathf.Pow(1f - t, 0.6f);
                            V.Add(p - lado * w); V.Add(p + lado * w);
                            N.Add(n); N.Add(n);
                            U.Add(new Vector2(u0, t)); U.Add(new Vector2(u1, t));
                        }
                        else { V.Add(p); N.Add(n); U.Add(new Vector2((u0 + u1) * 0.5f, 1f)); }
                    }
                    for (int s = 0; s < S - 1; s++)
                    {
                        int a = i0 + s * 2;
                        T.Add(a); T.Add(a + 2); T.Add(a + 1); T.Add(a + 1); T.Add(a + 2); T.Add(a + 3);
                    }
                    int last = i0 + (S - 1) * 2;
                    T.Add(last); T.Add(last + 2); T.Add(last + 1);
                }
                clumps[k] = new Mata { V = V.ToArray(), N = N.ToArray(), U = U.ToArray(), T = T.ToArray() };
            }
        }

        // ruido de valor suave (sin depender de nada del juego)
        static float Ruido(float x, float z) => Mathf.PerlinNoise(x * 0.13f + 31.7f, z * 0.13f + 7.3f) * 0.65f + Mathf.PerlinNoise(x * 0.47f + 3.1f, z * 0.47f + 91.2f) * 0.35f;

        static void ConstruirCuadro(Vector2Int c)
        {
            float x0 = zona.min.x + c.x * 4f, z0 = zona.min.z + c.y * 4f;
            var rnd = new System.Random(c.x * 73856093 ^ c.y * 19349663 ^ 1944);
            int intentos = Mathf.RoundToInt(4f * 4f * 24f * Mathf.Clamp(Densidad.Value, 0.2f, 3f));   // ~24 intentos de mata por m²
            var cerca = new List<(int k, Vector3 p, float rot, float esc)>();
            for (int i = 0; i < intentos; i++)
            {
                float x = x0 + (float)rnd.NextDouble() * 4f, z = z0 + (float)rnd.NextDouble() * 4f;
                float n = Ruido(x, z);
                if (rnd.NextDouble() > Mathf.Clamp01((n - 0.18f) * 3f)) continue;   // zonas peladas y zonas tupidas
                var arriba = new Vector3(x, zona.max.y + 20f, z);
                var golpes = Physics.RaycastAll(arriba, Vector3.down, 60f);
                RaycastHit suelo = default; bool hay = false;
                foreach (var g in golpes) if (g.collider == sueloCol) { suelo = g; hay = true; break; }
                if (!hay) continue;
                bool tapado = false;
                foreach (var g in golpes) if (g.collider != null && g.collider != sueloCol && !g.collider.isTrigger && g.point.y > suelo.point.y - 0.25f) { tapado = true; break; }
                if (tapado) continue;
                float esc = (0.55f + n * 0.9f) * Mathf.Clamp(Altura.Value, 0.3f, 3f) * (0.8f + (float)rnd.NextDouble() * 0.4f);
                cerca.Add((rnd.Next(clumps.Length), suelo.point - new Vector3(0, 0.01f, 0), (float)rnd.NextDouble() * 360f, esc));
            }
            if (cerca.Count == 0) return;
            var centro = new Vector3(x0 + 2f, cerca[0].p.y, z0 + 2f);
            var cu = new Cuadro { Centro = centro };
            cu.Cerca = CrearMalla(cerca, centro, 1, "cerca");
            cu.Lejos = CrearMalla(cerca, centro, 4, "lejos");
            cuadros.Add(cu);
        }

        static GameObject CrearMalla(List<(int k, Vector3 p, float rot, float esc)> matas, Vector3 centro, int cadaN, string nombre)
        {
            int nv = 0, nt = 0;
            for (int i = 0; i < matas.Count; i += cadaN) { nv += clumps[matas[i].k].V.Length; nt += clumps[matas[i].k].T.Length; }
            var V = new Vector3[nv]; var N = new Vector3[nv]; var U = new Vector2[nv]; var T = new int[nt];
            int ov = 0, ot = 0;
            for (int i = 0; i < matas.Count; i += cadaN)
            {
                var m = matas[i]; var cl = clumps[m.k];
                var q = Quaternion.Euler(0, m.rot, 0);
                float esc = cadaN > 1 ? m.esc * 1.25f : m.esc;   // a media distancia menos matas pero algo más grandes
                var escV = new Vector3(esc * (cadaN > 1 ? 1.6f : 1f), esc, esc * (cadaN > 1 ? 1.6f : 1f));
                for (int j = 0; j < cl.V.Length; j++)
                {
                    V[ov + j] = q * Vector3.Scale(cl.V[j], escV) + (m.p - centro);
                    N[ov + j] = q * cl.N[j];
                    U[ov + j] = cl.U[j];
                }
                for (int j = 0; j < cl.T.Length; j++) T[ot + j] = cl.T[j] + ov;
                ov += cl.V.Length; ot += cl.T.Length;
            }
            var mesh = new Mesh { name = "pasto " + nombre, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = V; mesh.normals = N; mesh.uv = U; mesh.triangles = T;
            mesh.RecalculateBounds();
            var go = new GameObject("pasto " + nombre);
            go.transform.SetParent(raiz.transform, false);
            go.transform.position = centro;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
            go.SetActive(false);
            return go;
        }

        static void ActualizarDistancias()
        {
            var cam = Camera.main; if (cam == null || cuadros.Count == 0) return;
            var pc = cam.transform.position;
            float dc = DistanciaCerca.Value, dl = Mathf.Max(dc, DistanciaLejos.Value);
            foreach (var c in cuadros)
            {
                float d = Vector2.Distance(new Vector2(pc.x, pc.z), new Vector2(c.Centro.x, c.Centro.z));
                bool cerca = d < dc, lejos = !cerca && d < dl;
                if (c.Cerca.activeSelf != cerca) c.Cerca.SetActive(cerca);
                if (c.Lejos.activeSelf != lejos) c.Lejos.SetActive(lejos);
            }
        }
    }
}
