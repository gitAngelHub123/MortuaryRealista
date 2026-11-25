// © Angel — Fuego más realista (crematorio, encendedor): llama con forma real en vez del óvalo borroso del juego.
// Los fuegos son VFX Graph y leen la textura "flame" (512, DXT5) directo desde su asset: no hay material que cambiar.
// Se copia en la GPU (Graphics.CopyTexture) una llama armada con fotos de fuego del propio juego (herramientas/fuego_real.py)
// sobre la textura original. Para volver al original se guarda antes una copia en la GPU.
using System;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace MortuaryRealista
{
    internal static class Fuego
    {
        internal static ConfigEntry<bool> Activar;
        internal static ConfigEntry<float> Intensidad, LuzLumenes;
        static Texture2D nueva, copiaOriginal, destino;
        static bool aplicado, sinArchivo;
        static float proxima;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("Materiales", "FuegoRealista", true, "Fuego del crematorio y del encendedor con llamas de forma real (no un disco blanco). Necesita el pack de texturas");
            LuzLumenes = cfg.Bind("Materiales", "FuegoLuz", 1200f, "Luz naranja que parpadea alrededor del fuego del crematorio, en lúmenes (0 = sin luz)");
            Intensidad = cfg.Bind("Materiales", "FuegoIntensidad", 3f, "Brillo de las llamas del crematorio (1 = tenue, 6 = muy brillante)");
        }

        public static void Revisar(bool on)
        {
            if (Time.unscaledTime < proxima) return;
            proxima = Time.unscaledTime + 2f;
            try { Crematorio(on && Activar.Value); } catch (Exception e) { Plugin.Log.LogWarning("Fuego (crematorio): " + e.Message); }
            if (sinArchivo) return;
            try
            {
                on &= Activar.Value;
                if (destino == null)
                {
                    aplicado = false;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                        if (t != null && t.name == "flame" && t.width == 512 && t.height == 512 && t.format == TextureFormat.DXT5) { destino = t; break; }
                    if (destino == null) return;
                }
                if (on == aplicado) return;
                if (nueva == null && !Cargar()) return;
                if (copiaOriginal == null)
                {
                    copiaOriginal = new Texture2D(512, 512, TextureFormat.DXT5, destino.mipmapCount, false) { name = "flame (original)" };
                    Graphics.CopyTexture(destino, copiaOriginal);
                }
                Graphics.CopyTexture(on ? nueva : copiaOriginal, destino);
                aplicado = on;
                Plugin.Log.LogInfo("Fuego: llama " + (on ? "realista aplicada" : "original devuelta") + " (" + destino.mipmapCount + " niveles)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Fuego: " + e.Message); proxima = Time.unscaledTime + 60f; }
        }

        // ---------- crematorio: el efecto "BurnBody" (bolas brillantes que se suman en un disco blanco) se cambia por llamas ----------
        static UnityEngine.VFX.VisualEffect burn;
        static GameObject nuestro;
        static Material matLlamas;
        static Texture2D hojaLlamas;
        static bool sinHoja;
        static void Crematorio(bool on)
        {
            if (burn == null)
            {
                nuestro = null;
                foreach (var v in Resources.FindObjectsOfTypeAll<UnityEngine.VFX.VisualEffect>())
                    if (v != null && v.gameObject.scene.IsValid() && v.name == "BurnBody" && v.visualEffectAsset != null && v.visualEffectAsset.name == "BurnBody") { burn = v; break; }
                if (burn == null) return;
            }
            var rend = burn.GetComponent<Renderer>();
            if (!on)
            {
                if (nuestro != null) { UnityEngine.Object.Destroy(nuestro); nuestro = null; llamas.Clear(); luzFuego = null; burn.enabled = true; if (rend != null) rend.enabled = true; Plugin.Log.LogInfo("Fuego: crematorio original"); }
                return;
            }
            if (nuestro != null && burn.enabled) { burn.enabled = false; if (rend != null) rend.enabled = false; }   // el juego puede volver a encenderlo
            if (matLlamas != null) { float k = Intensidad.Value; matLlamas.SetColor("_UnlitColor", new Color(k, k * 0.85f, k * 0.7f, 1f)); }
            if (nuestro != null || sinHoja) return;
            if (hojaLlamas == null)
            {
                string ruta = Path.Combine(TexturasIA.carpetaReal ?? "", "especiales", "llamas_6x6.dxt5");
                if (!File.Exists(ruta)) { sinHoja = true; return; }
                var d = File.ReadAllBytes(ruta);
                int w = BitConverter.ToInt32(d, 8), niveles = BitConverter.ToInt32(d, 20), largo = BitConverter.ToInt32(d, 24);
                var datos = new byte[largo]; Buffer.BlockCopy(d, 28, datos, 0, largo);
                hojaLlamas = new Texture2D(w, w, TextureFormat.DXT5, niveles, false) { name = "llamas 6x6 (Mortuary Realista)", wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
                hojaLlamas.LoadRawTextureData(datos); hojaLlamas.Apply(false, true);
            }
            if (matLlamas == null)
            {
                var sh = Shader.Find("HDRP/Unlit");
                if (sh == null) { sinHoja = true; Plugin.Log.LogWarning("Fuego: no está el shader HDRP/Unlit"); return; }
                var m = new Material(sh) { name = "Llamas (Mortuary Realista)" };
                // transparente aditivo (SrcAlpha, One): suma luz donde hay llama, sin tapar lo de atrás
                m.SetFloat("_SurfaceType", 1); m.SetFloat("_BlendMode", 1); m.SetFloat("_RenderQueueType", 4);
                m.SetFloat("_SrcBlend", 5); m.SetFloat("_DstBlend", 1); m.SetFloat("_AlphaSrcBlend", 1); m.SetFloat("_AlphaDstBlend", 1);
                m.SetFloat("_ZWrite", 0); m.SetFloat("_TransparentZWrite", 0); m.SetFloat("_ZTestTransparent", 4);
                m.SetFloat("_CullMode", 0); m.SetFloat("_CullModeForward", 0); m.SetFloat("_DoubleSidedEnable", 1);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_BLENDMODE_ADD"); m.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                m.SetTexture("_UnlitColorMap", hojaLlamas);
                float k = Intensidad.Value; m.SetColor("_UnlitColor", new Color(k, k * 0.85f, k * 0.7f, 1f));
                m.renderQueue = 3000;
                matLlamas = m;
            }
            nuestro = new GameObject("Llamas (Mortuary Realista)");
            nuestro.transform.SetParent(burn.transform, false);
            nuestro.transform.localPosition = new Vector3(0f, -0.4f, 0f);
            if (quad == null) quad = CrearQuad();
            llamas.Clear();
            for (int i = 0; i < NLlamas; i++)
            {
                var g = new GameObject("llama");
                g.transform.SetParent(nuestro.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = matLlamas;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                var l = new Llama { T = g.transform, R = mr };
                Renacer(l, UnityEngine.Random.value);   // repartidas en el tiempo desde el inicio
                llamas.Add(l);
            }
            if (LuzLumenes.Value > 0f)
            {
                var lgo = new GameObject("Luz del fuego (Mortuary Realista)");
                lgo.transform.SetParent(nuestro.transform, false);
                lgo.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                var lz = lgo.AddComponent<Light>(); lz.type = LightType.Point;
                luzFuego = lgo.AddComponent<HDAdditionalLightData>();
                luzFuego.SetIntensity(LuzLumenes.Value, LightUnit.Lumen);
                luzFuego.SetRange(5f);
                luzFuego.EnableShadows(false);
                luzFuego.shapeRadius = 0.3f;          // fuente ancha: brillos suaves
                luzFuego.volumetricDimmer = 0.3f;
                luzFuego.color = new Color(1f, 0.52f, 0.2f);
            }
            burn.enabled = false; if (rend != null) rend.enabled = false;   // se apagan las bolas brillantes del juego; el humo (Smoke) sigue igual
            Plugin.Log.LogInfo("Fuego: crematorio con " + NLlamas + " llamas animadas");
        }

        // ---------- llamas propias: planos que miran a la cámara con un cuadro de la hoja 6x6, suben, crecen y se apagan ----------
        const int NLlamas = 55;
        class Llama { public Transform T; public MeshRenderer R; public Vector3 P, V; public float Edad, Vida, Tam, Cuadro; }
        static readonly System.Collections.Generic.List<Llama> llamas = new System.Collections.Generic.List<Llama>();
        static Mesh quad;
        static MaterialPropertyBlock mpb;
        static HDAdditionalLightData luzFuego;
        static Mesh CrearQuad()
        {
            var m = new Mesh { name = "llama" };
            m.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            m.RecalculateNormals(); m.RecalculateBounds();
            m.bounds = new Bounds(new Vector3(0, 0.5f, 0), new Vector3(2, 2, 2));
            return m;
        }
        static void Renacer(Llama l, float edad01)
        {
            l.Vida = UnityEngine.Random.Range(0.6f, 1.1f);
            l.Edad = edad01 * l.Vida;
            l.P = new Vector3(UnityEngine.Random.Range(-0.38f, 0.38f), 0f, UnityEngine.Random.Range(-0.3f, 0.3f));
            l.V = new Vector3(UnityEngine.Random.Range(-0.05f, 0.05f), UnityEngine.Random.Range(0.25f, 0.55f), UnityEngine.Random.Range(-0.05f, 0.05f));
            l.Tam = UnityEngine.Random.Range(0.5f, 0.9f);
            l.Cuadro = UnityEngine.Random.Range(0, 36);
        }

        // cada cuadro (desde Plugin.Update vía Materiales.Tick)
        public static void Animar()
        {
            if (nuestro == null || !nuestro.activeInHierarchy || llamas.Count == 0) return;
            var cam = Camera.main; if (cam == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var fw = cam.transform.forward; fw.y = 0; if (fw.sqrMagnitude < 1e-4f) fw = Vector3.forward;
            var rot = Quaternion.LookRotation(fw.normalized);
            float k = Intensidad.Value;
            if (luzFuego != null)
            {
                // parpadeo de fuego: dos ruidos de distinta velocidad (no un seno regular)
                float t = Time.time;
                float f = 0.78f + 0.16f * Mathf.PerlinNoise(t * 7.3f, 0.3f) + 0.1f * Mathf.PerlinNoise(t * 19f, 4.1f) - 0.05f;
                luzFuego.intensity = LuzLumenes.Value * f;
                luzFuego.transform.localPosition = new Vector3((Mathf.PerlinNoise(t * 3f, 9f) - 0.5f) * 0.15f, 0.55f + (Mathf.PerlinNoise(t * 4f, 2f) - 0.5f) * 0.1f, 0f);
            }
            foreach (var l in llamas)
            {
                l.Edad += dt;
                if (l.Edad >= l.Vida) Renacer(l, 0f);
                float u = l.Edad / l.Vida;
                l.P += l.V * dt;
                float tam = l.Tam * (u < 0.2f ? Mathf.Lerp(0.4f, 1f, u / 0.2f) : Mathf.Lerp(1f, 0.55f, (u - 0.2f) / 0.8f));
                float alfa = u < 0.15f ? u / 0.15f : u > 0.6f ? Mathf.Max(0f, (1f - u) / 0.4f) : 1f;
                l.T.localPosition = l.P; l.T.rotation = rot; l.T.localScale = new Vector3(tam * 0.75f, tam, tam);
                int c = ((int)(l.Cuadro + l.Edad * 24f)) % 36, fx = c % 6, fy = c / 6;
                mpb.SetVector("_UnlitColorMap_ST", new Vector4(1f / 6f, 1f / 6f, fx / 6f, 1f - (fy + 1) / 6f));
                mpb.SetColor("_UnlitColor", new Color(k * alfa, k * 0.72f * alfa, k * 0.48f * alfa, alfa));
                l.R.SetPropertyBlock(mpb);
            }
        }

        static bool Cargar()
        {
            string ruta = Path.Combine(TexturasIA.carpetaReal ?? "", "especiales", "flame_512.dxt5");
            if (!File.Exists(ruta)) { sinArchivo = true; Plugin.Log.LogInfo("Fuego: sin " + ruta + " (pack de texturas no instalado)"); return false; }
            var d = File.ReadAllBytes(ruta);
            int w = BitConverter.ToInt32(d, 8), h = BitConverter.ToInt32(d, 12), niveles = BitConverter.ToInt32(d, 20), largo = BitConverter.ToInt32(d, 24);
            if (niveles != destino.mipmapCount || w != 512 || h != 512) { sinArchivo = true; Plugin.Log.LogWarning($"Fuego: el archivo tiene {niveles} niveles y el juego {destino.mipmapCount}"); return false; }
            var datos = new byte[largo]; Buffer.BlockCopy(d, 28, datos, 0, largo);
            nueva = new Texture2D(w, h, TextureFormat.DXT5, niveles, false) { name = "flame (realista)" };
            nueva.LoadRawTextureData(datos);
            nueva.Apply(false, true);
            return true;
        }
    }
}
