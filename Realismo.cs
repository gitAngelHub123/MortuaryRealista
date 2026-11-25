// © Angel — Iluminación más realista en HDRP: sombras suaves (PCSS), sombras de contacto, SSAO, SSR, SSGI
using System;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace MortuaryRealista
{
    internal static class Realismo
    {
        internal static ConfigEntry<bool> Activar, SombrasContacto, SSR, SSGI, RegistroFPS, MicroSombras, LuzInterior;
        internal static ConfigEntry<int> Tonemapping;
        internal static ConfigEntry<string> ColorOriginalCine;
        static bool enCineOriginal; static float proximaCine;
        internal static ConfigEntry<float> LuzIndirecta, LuzReflejos, SSGILimite, SSGIRadio;
        internal static ConfigEntry<bool> SSGIFiltro;
        internal static ConfigEntry<int> FiltroSombras, CalidadAO, MuestrasPiel;
        internal static float FpsActual;
        internal static ConfigEntry<float> LongitudContacto, IntensidadAO, RadioAO, AOLuzDirecta;

        static AmbientOcclusion ao;
        static ContactShadows contacto;
        static ScreenSpaceReflection ssr;
        static GlobalIllumination ssgi;
        static MicroShadowing micro;
        static Tonemapping tone;
        static IndirectLightingController indirecta;
        internal static bool LuzEncendida = true;
        public static void AlternarLuz(bool on) { LuzEncendida = on; Aplicar(); Plugin.Log.LogInfo("Luz interior " + (on ? "ACTIVA" : "APAGADA")); }
        static int firmaEscena = int.MinValue;
        static float proximaLuces = -1f;
        static bool diagnosticoHecho, frameHecho;
        // medidor de FPS
        static int cuadros; static float tiempo, peor; static float proximoFps = 10f;

        public static void Configurar(ConfigFile cfg)
        {
            Activar = cfg.Bind("Realismo", "Activar", true, "Ajustes de iluminación realista");
            FiltroSombras = cfg.Bind("Realismo", "FiltroSombras", -1, "-1 = no tocar (el juego usa Medio); 2 = Alto (sombras suaves PCSS: muy caro, baja de ~70 a ~18 FPS en la morgue)");
            SombrasContacto = cfg.Bind("Realismo", "SombrasContacto", false, "Sombras finas de contacto. OJO: en este juego dejan un tramado/moaré en zonas iluminadas");
            LongitudContacto = cfg.Bind("Realismo", "LongitudContacto", 0.25f, "Largo de las sombras de contacto en metros");
            IntensidadAO = cfg.Bind("Realismo", "IntensidadAO", 1.0f, "-1 = no tocar; oclusión ambiental (oscurece esquinas y rincones)");
            AOLuzDirecta = cfg.Bind("Realismo", "AOLuzDirecta", 0.5f, "-1 = no tocar; cuánto oscurece la oclusión también la luz de las lámparas (0 a 1). En HDRP la oclusión solo afecta la luz ambiental, y estos interiores se iluminan casi todo con lámparas");
            RadioAO = cfg.Bind("Realismo", "RadioAO", 1.5f, "-1 = no tocar; radio de la oclusión ambiental en metros");
            CalidadAO = cfg.Bind("Realismo", "CalidadAO", 2, "-1 = no tocar; 0 Baja, 1 Media, 2 Alta");
            SSR = cfg.Bind("Realismo", "ReflejosSSR", true, "Reflejos en pantalla de alta calidad (suelos, azulejos, metal)");
            SSGI = cfg.Bind("Realismo", "IluminacionGlobalSSGI", false, "Luz rebotada en pantalla (más real, cuesta FPS)");
            RegistroFPS = cfg.Bind("Realismo", "RegistroFPS", false, "Escribe los FPS medios en el log cada 10 s");
            MuestrasPiel = cfg.Bind("Realismo", "MuestrasPiel", 80, "Calidad de la dispersión de luz en la piel (muestras por píxel; el juego usa su nivel de calidad). 0 = no tocar");
            LuzInterior = cfg.Bind("Realismo", "LuzInterior", true, "Punto 6: tonemapping y más luz rebotada en interiores");
            ColorOriginalCine = cfg.Bind("Realismo", "ColorOriginalCinematicas", "Tutorial", "Cinemáticas (nombre del asset de PlayableDirector, separadas por coma) en las que se quita el mapeo tonal para conservar los colores cálidos del juego (piel de la protagonista en la llegada en auto). Vacío = nunca");
            Tonemapping = cfg.Bind("Realismo", "Tonemapping", 1, "0 = ninguno (como el juego), 1 = Neutral (recupera luces quemadas), 2 = ACES (cine, más contraste)");
            LuzIndirecta = cfg.Bind("Realismo", "LuzIndirecta", 1.35f, "Multiplicador de la luz rebotada (ambiente) en interiores; 1 = como el juego");
            SSGIFiltro = cfg.Bind("Realismo", "FiltroRuidoSSGI", true, "Filtro de ruido reforzado para la luz rebotada (quita los puntitos brillantes)");
            SSGILimite = cfg.Bind("Realismo", "LimiteBrilloSSGI", 1.5f, "Tope de brillo de cada muestra de luz rebotada: más bajo = menos puntitos");
            SSGIRadio = cfg.Bind("Realismo", "RadioFiltroSSGI", 1f, "Radio del filtro de ruido de la luz rebotada (0,001–1)");
            LuzReflejos = cfg.Bind("Realismo", "LuzReflejos", 1.15f, "Multiplicador de los reflejos indirectos; 1 = como el juego");
            MicroSombras = cfg.Bind("Realismo", "MicroSombras", true, "Sombras finas a partir del relieve de las normales");
        }

        // Se llama antes de reconstruir el pipeline (junto con DLSS)
        public static bool AjustarAsset(ref RenderPipelineSettings st)
        {
            if (!Activar.Value) return false;
            bool antes = st.supportSSAO, antesSsrT = st.supportSSRTransparent, antesSsr = st.supportSSR, antesSsgi = st.supportSSGI;
            var antesFiltro = st.hdShadowInitParams.shadowFilteringQuality;
            Plugin.Log.LogInfo("ASSET ANTES: SSAO=" + st.supportSSAO + " SSR=" + st.supportSSR + " SSGI=" + st.supportSSGI +
                " volumétricos=" + st.supportVolumetrics + " filtroSombras=" + st.hdShadowInitParams.shadowFilteringQuality +
                " atlasPuntual=" + st.hdShadowInitParams.punctualLightShadowAtlas.shadowAtlasResolution +
                " maxSombraPuntual=" + st.hdShadowInitParams.maxPunctualShadowMapResolution +
                " sssMuestras=" + st.sssSampleBudget.ToString());
            st.supportSSAO = true;
            st.supportSSR = true;
            if (Materiales.VidrioReflejos.Value) st.supportSSRTransparent = true;   // reflejos en vidrios (sin efecto visible medido)
            st.supportSSGI = true;   // siempre soportado: así la luz rebotada se puede encender en vivo desde el menú
            Plugin.Log.LogInfo("ASSET PIEL: sss=" + st.supportSubsurfaceScattering);
            if (FiltroSombras.Value >= 0)
            {
                var sp = st.hdShadowInitParams;
                sp.shadowFilteringQuality = (HDShadowFilteringQuality)Math.Min(3, FiltroSombras.Value);
                st.hdShadowInitParams = sp;
            }
            return antes != st.supportSSAO || antesSsr != st.supportSSR || antesSsrT != st.supportSSRTransparent || antesSsgi != st.supportSSGI ||
                   antesFiltro != st.hdShadowInitParams.shadowFilteringQuality;
        }

        public static void CrearOverrides(VolumeProfile profile)
        {
            ao = profile.Add<AmbientOcclusion>(false);
            contacto = profile.Add<ContactShadows>(false);
            ssr = profile.Add<ScreenSpaceReflection>(false);
            ssgi = profile.Add<GlobalIllumination>(false);
            micro = profile.Add<MicroShadowing>(false);
            tone = profile.Add<Tonemapping>(false);
            indirecta = profile.Add<IndirectLightingController>(false);
            Aplicar();
        }

        internal static bool Encendido = true;
        public static void Alternar(bool on) { Encendido = on; Aplicar(); Plugin.Log.LogInfo("Realismo " + (on ? "ACTIVO" : "APAGADO")); }

        public static void Aplicar()
        {
            if (ao == null) return;
            bool on = Activar.Value && Encendido;
            ao.active = on && CalidadAO.Value >= 0;
            if (IntensidadAO.Value >= 0) ao.intensity.Override(IntensidadAO.Value);
            if (RadioAO.Value >= 0) ao.radius.Override(RadioAO.Value);
            if (AOLuzDirecta.Value >= 0) ao.directLightingStrength.Override(Mathf.Clamp01(AOLuzDirecta.Value));
            if (CalidadAO.Value >= 0) ao.quality.Override(Math.Min(2, CalidadAO.Value));
            contacto.active = on && SombrasContacto.Value;
            contacto.enable.Override(SombrasContacto.Value);
            contacto.length.Override(LongitudContacto.Value);
            contacto.quality.Override(2);
            ssr.active = on;                       // activo siempre: así "desactivado" también apaga los reflejos que trae el juego
            ssr.enabled.Override(SSR.Value);
            ssr.m_MinSmoothness.Override(0.4f);          // HDRP solo refleja con brillo ≥ 0,9 por defecto; los suelos del juego están en 0,5–0,85
            ssr.m_SmoothnessFadeStart.Override(0.6f);
            ssr.quality.Override(Math.Clamp(Opciones.CalidadSSR.Value, 0, 2));
            ssgi.active = on && SSGI.Value;
            ssgi.enable.Override(SSGI.Value);
            ssgi.quality.Override(Math.Clamp(Opciones.CalidadSSGI.Value, 0, 2));
            if (SSGIFiltro.Value)
            {
                ssgi.m_DenoiseSS.Override(true);
                ssgi.m_HalfResolutionDenoiserSS.Override(false);
                ssgi.m_DenoiserRadiusSS.Override(Mathf.Clamp(SSGIRadio.Value, 0.001f, 1f));
                ssgi.m_SecondDenoiserPassSS.Override(true);
                ssgi.m_ClampValue.Override(Mathf.Clamp(SSGILimite.Value, 0.001f, 10f));
            }
            else
            {
                ssgi.m_DenoiseSS.overrideState = false; ssgi.m_HalfResolutionDenoiserSS.overrideState = false;
                ssgi.m_DenoiserRadiusSS.overrideState = false; ssgi.m_SecondDenoiserPassSS.overrideState = false; ssgi.m_ClampValue.overrideState = false;
            }
            micro.active = on && MicroSombras.Value;
            micro.enable.Override(true);
            micro.opacity.Override(1f);
            bool luz = on && LuzEncendida && LuzInterior.Value;
            tone.active = on && LuzEncendida && Tonemapping.Value > 0 && !enCineOriginal;
            tone.mode.Override(Tonemapping.Value == 2 ? TonemappingMode.ACES : Tonemapping.Value == 1 ? TonemappingMode.Neutral : TonemappingMode.None);
            indirecta.active = luz;
            indirecta.indirectDiffuseLightingMultiplier.Override(LuzIndirecta.Value);
            indirecta.reflectionLightingMultiplier.Override(LuzReflejos.Value);
        }

        public static void Tick()
        {
            if (RegistroFPS.Value) MedirFps();
            if (!Activar.Value) return;
            try
            {
                var activa = SceneManager.GetActiveScene();
                int firma = SceneManager.sceneCount * 7919 + activa.handle;
                if (firma != firmaEscena) { firmaEscena = firma; proximaLuces = Time.unscaledTime + 3f; }
                if (proximaLuces > 0 && Time.unscaledTime >= proximaLuces) { proximaLuces = -1f; AjustarLuces(); }
                if (!frameHecho) AjustarFrameSettings();
                if (Time.unscaledTime >= proximaCine) { proximaCine = Time.unscaledTime + 0.25f; RevisarCine(); }
                if (!diagnosticoHecho && Camera.main != null) Diagnostico();
            }
            catch (Exception e) { Plugin.Log.LogWarning("Realismo: " + e.Message); proximaLuces = -1f; frameHecho = true; }
        }

        // Mientras corre una cinemática de la lista, el mapeo tonal se apaga (colores del juego); al terminar vuelve solo
        static void RevisarCine()
        {
            bool ahora = false;
            string lista = ColorOriginalCine.Value;
            if (!string.IsNullOrWhiteSpace(lista))
                foreach (var d in UnityEngine.Object.FindObjectsOfType<UnityEngine.Playables.PlayableDirector>())
                {
                    if (d == null || d.state != UnityEngine.Playables.PlayState.Playing || d.playableAsset == null) continue;
                    foreach (var n in lista.Split(',')) if (n.Trim().Length > 0 && d.playableAsset.name == n.Trim()) { ahora = true; break; }
                    if (ahora) break;
                }
            if (ahora == enCineOriginal) return;
            enCineOriginal = ahora; Aplicar();
            Plugin.Log.LogInfo("Realismo: colores originales en cinemática " + (ahora ? "ACTIVOS" : "terminados"));
        }

        static bool frameOrigGuardado; static SssQualityMode sssModoOrig; static int sssMuestrasOrig;
        public static void AplicarFrame() { if (frameHecho) AjustarFrameSettings(); }
        static void AjustarFrameSettings()
        {
            var gs = HDRenderPipelineGlobalSettings.instance;
            if (gs == null) return;
            frameHecho = true;
            var fs = gs.m_RenderingPathDefaultCameraFrameSettings;
            Plugin.Log.LogInfo("FRAME ANTES: SSAO=" + fs.IsEnabled(FrameSettingsField.SSAO) + " SSR=" + fs.IsEnabled(FrameSettingsField.SSR) +
                " SSGI=" + fs.IsEnabled(FrameSettingsField.SSGI) + " contacto=" + fs.IsEnabled(FrameSettingsField.ContactShadows) +
                " volumétricos=" + fs.IsEnabled(FrameSettingsField.Volumetrics));
            fs.SetEnabled(FrameSettingsField.SSAO, true);
            if (SSR.Value) { fs.SetEnabled(FrameSettingsField.SSR, true); if (Materiales.VidrioReflejos.Value) fs.SetEnabled(FrameSettingsField.TransparentSSR, true); }
            if (SSGI.Value) fs.SetEnabled(FrameSettingsField.SSGI, true);
            if (SombrasContacto.Value) fs.SetEnabled(FrameSettingsField.ContactShadows, true);
            Plugin.Log.LogInfo("PIEL ANTES: sss=" + fs.IsEnabled(FrameSettingsField.SubsurfaceScattering) + " transmisión=" + fs.IsEnabled(FrameSettingsField.Transmission) +
                " modo=" + fs.sssQualityMode + " nivel=" + fs.sssQualityLevel + " muestras=" + fs.sssCustomSampleBudget);
            if (!frameOrigGuardado)
            {
                string l = "FRAME POST:";
                foreach (var f in new[] { FrameSettingsField.DepthOfField, FrameSettingsField.MotionBlur, FrameSettingsField.ExposureControl, FrameSettingsField.Tonemapping,
                    FrameSettingsField.LensDistortion, FrameSettingsField.ChromaticAberration, FrameSettingsField.FilmGrain, FrameSettingsField.Vignette,
                    FrameSettingsField.Volumetrics, FrameSettingsField.ReprojectionForVolumetrics, FrameSettingsField.Postprocess, FrameSettingsField.AfterPostprocess, FrameSettingsField.MotionVectors })
                    l += " " + f + "=" + fs.IsEnabled(f);
                Plugin.Log.LogInfo(l);
                // los efectos de cine del menú necesitan su paso de post-proceso encendido (si el efecto no se usa, no cuesta nada)
                foreach (var f in new[] { FrameSettingsField.DepthOfField, FrameSettingsField.MotionBlur, FrameSettingsField.ExposureControl, FrameSettingsField.Tonemapping, FrameSettingsField.LensDistortion })
                    fs.SetEnabled(f, true);
            }
            if (!frameOrigGuardado) { frameOrigGuardado = true; sssModoOrig = fs.sssQualityMode; sssMuestrasOrig = fs.sssCustomSampleBudget; }
            if (MuestrasPiel.Value > 0 && Activar.Value && Encendido)
            {
                fs.SetEnabled(FrameSettingsField.SubsurfaceScattering, true);
                fs.SetEnabled(FrameSettingsField.Transmission, true);
                fs.sssQualityMode = SssQualityMode.OverrideQualitySettings;
                fs.sssCustomSampleBudget = MuestrasPiel.Value;
            }
            else { fs.sssQualityMode = sssModoOrig; fs.sssCustomSampleBudget = sssMuestrasOrig; }
            gs.m_RenderingPathDefaultCameraFrameSettings = fs;
            var fs2 = gs.m_RenderingPathDefaultCameraFrameSettings;
            Plugin.Log.LogInfo("FRAME DESPUÉS: SSAO=" + fs2.IsEnabled(FrameSettingsField.SSAO) + " SSR=" + fs2.IsEnabled(FrameSettingsField.SSR) +
                " SSGI=" + fs2.IsEnabled(FrameSettingsField.SSGI) + " contacto=" + fs2.IsEnabled(FrameSettingsField.ContactShadows));
        }

        static void AjustarLuces()
        {
            int n = 0, contactoOn = 0, res = 0;
            foreach (var l in UnityEngine.Object.FindObjectsOfType<HDAdditionalLightData>())
            {
                if (l == null) continue;
                n++;
                if (SombrasContacto.Value)
                {
                    var uc = l.useContactShadow;
                    uc.useOverride = true;
                    uc.@override = true;
                    contactoOn++;
                }
            }
            Plugin.Log.LogInfo("Realismo: " + n + " luces; sombras de contacto en " + contactoOn);
        }

        static void Diagnostico()
        {
            diagnosticoHecho = true;
            var hd = Camera.main.GetComponent<HDAdditionalCameraData>();
            if (hd != null) Plugin.Log.LogInfo("Cámara: frameSettingsPropios=" + hd.customRenderingSettings);
            var st = VolumeManager.instance.stack;
            var a = st.GetComponent<AmbientOcclusion>();
            var c = st.GetComponent<ContactShadows>();
            var r = st.GetComponent<ScreenSpaceReflection>();
            var g = st.GetComponent<GlobalIllumination>();
            var tm = st.GetComponent<Tonemapping>();
            var ex = st.GetComponent<Exposure>();
            Plugin.Log.LogInfo("PILA: AO int=" + a.intensity.value + " radio=" + a.radius.value + " calidad=" + a.quality.value +
                " | contacto=" + c.enable.value + " largo=" + c.length.value +
                " | SSR=" + r.enabled.value + " calidad=" + r.quality.value +
                " | SSGI=" + g.enable.value +
                " | tonemapping=" + tm.mode.value + " | exposición=" + ex.mode.value);
        }

        static void MedirFps()
        {
            float dt = Time.unscaledDeltaTime;
            cuadros++; tiempo += dt; if (dt > peor) peor = dt;
            if (Time.unscaledTime >= proximoFps)
            {
                proximoFps = Time.unscaledTime + 10f;
                if (cuadros > 0 && tiempo > 0) FpsActual = cuadros / tiempo;
                if (cuadros > 0 && tiempo > 0)
                    Plugin.Log.LogInfo($"FPS: medio {cuadros / tiempo:0.0} | peor cuadro {peor * 1000f:0} ms | escena {SceneManager.GetActiveScene().name}");
                cuadros = 0; tiempo = 0; peor = 0;
            }
        }
    }
}
