// © Angel — The Mortuary Assistant: gráficos realistas (texturas IA, DLSS, calidad y post-proceso)
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace MortuaryRealista
{
    [BepInPlugin("angel.mortuary.terrorcine", "Mortuary Realista", "1.0.0")]
    public class Plugin : BasePlugin
    {
        internal static new ManualLogSource Log;
        internal static ConfigEntry<bool> Activo;
        internal static ConfigEntry<float> Grano, Viñeteo, SuavizadoViñeteo, Aberracion, Exposicion, Contraste, Saturacion, Gamma, TintePorcentaje;
        internal static ConfigEntry<bool> DlssActivar;
        internal static ConfigEntry<int> DlssCalidad;
        internal static ConfigEntry<float> DlssNitidez;
        internal static ConfigEntry<int> TexLimite;
        internal static ConfigEntry<bool> Aniso, SinStreaming;
        internal static ConfigEntry<float> SesgoLod;
        internal static ConfigEntry<Key> TeclaActivar, TeclaRecargar;

        internal static Plugin Instancia;
        internal static void Recargar() { Instancia.Config.Reload(); }

        public override void Load()
        {
            Log = base.Log;
            Instancia = this;
            Activo = Config.Bind("General", "Activo", true, "Enciende o apaga todo el efecto");
            TeclaActivar = Config.Bind("General", "TeclaActivar", Key.F8, "Alterna el efecto en vivo");
            TeclaRecargar = Config.Bind("General", "TeclaRecargar", Key.F9, "Relee este archivo de configuración en vivo");
            Grano = Config.Bind("Efectos", "Grano", 0f, "Intensidad del grano de película (0 a 1)");
            Viñeteo = Config.Bind("Efectos", "Viñeteo", 0.15f, "Oscurecimiento de los bordes (0 a 1)");
            SuavizadoViñeteo = Config.Bind("Efectos", "SuavizadoViñeteo", 0.7f, "Suavidad del viñeteo (0 a 1)");
            Aberracion = Config.Bind("Efectos", "AberracionCromatica", 0f, "Franjas de color en los bordes (0 a 1)");
            Exposicion = Config.Bind("Efectos", "Exposicion", 0.35f, "Ajuste de exposición en EV (negativo = más oscuro)");
            Contraste = Config.Bind("Efectos", "Contraste", 5f, "Contraste (-100 a 100)");
            Saturacion = Config.Bind("Efectos", "Saturacion", 0f, "Saturación (-100 a 100)");
            Gamma = Config.Bind("Efectos", "Gamma", 0.05f, "Oscurece los tonos medios (negativo = más denso)");
            TintePorcentaje = Config.Bind("Efectos", "TinteFrio", 0f, "Cuánto tinte azul verdoso (0 a 1)");

            TexLimite = Config.Bind("Calidad", "LimiteTexturas", 0, "0 = texturas a resolución completa; -1 = no tocar lo del juego");
            Aniso = Config.Bind("Calidad", "Anisotropico16x", true, "Fuerza filtrado anisotrópico 16x (texturas nítidas en el suelo y paredes lejanas)");
            SinStreaming = Config.Bind("Calidad", "DesactivarStreamingMip", true, "Evita que el juego baje la resolución de texturas para ahorrar memoria");
            SesgoLod = Config.Bind("Calidad", "SesgoLOD", 2f, "Sube el nivel de detalle a distancia (1 = normal, más alto = más detalle lejano); -1 = no tocar");

            DlssActivar = Config.Bind("DLSS", "Activar", false, "Fuerza DLSS (el juego lo trae apagado). OJO: bajo Proton deja bordes punteados y no sube los FPS");
            DlssCalidad = Config.Bind("DLSS", "Calidad", 2, "-1 = la del juego; 0 = MaxRendimiento, 1 = Equilibrado, 2 = Calidad, 3 = UltraCalidad (solo si el juego lo permite)");
            DlssNitidez = Config.Bind("DLSS", "Nitidez", -1f, "0 a 1; -1 = la del juego");

            TexturasIA.Configurar(Config);
            Realismo.Configurar(Config);
            Materiales.Configurar(Config);
            Pruebas.Configurar(Config);
            Opciones.Configurar(Config);
            Idioma.Configurar(Config);
            Interfaz.Configurar(Config);
            Portapapeles.Configurar(Config);
            TexturasIA.Iniciar();

            ClassInjector.RegisterTypeInIl2Cpp<Runner>();
            var go = new GameObject("MortuaryTerrorCine");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>();
            Log.LogInfo("Mortuary Realista 1.0.0 cargado. Opciones → MOD GRÁFICO, F8 post-proceso, F9 recarga la configuración, F10 captura.");
        }
    }

    public class Runner : MonoBehaviour
    {
        public Runner(IntPtr ptr) : base(ptr) { }
        internal static Runner Actual;
        internal static bool PostEncendido = true;
        internal void PostProceso(bool on) { PostEncendido = on; Aplicar(); }

        GameObject volGo;
        Volume vol;
        VolumeProfile profile;
        FilmGrain grano;
        Vignette vin;
        ChromaticAberration ab;
        ColorAdjustments col;
        LiftGammaGain lgg;
        float proximaRevision;
        bool avisoCapa;
        bool dlssHecho;
        bool diagHecho;

        static bool Pulsada(Key k)
        {
            var kb = Keyboard.current;
            return kb != null && kb[k].wasPressedThisFrame;
        }

        void Update()
        {
            try
            {
                if (Time.unscaledTime >= proximaRevision)
                {
                    proximaRevision = Time.unscaledTime + 1f;
                    if (volGo == null) Crear();
                    else { AjustarCapa(); if (!dlssHecho) AplicarDlss(); }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Volumen: " + e);
                proximaRevision = Time.unscaledTime + 5f;
            }
            try
            {
                if (Pulsada(Plugin.TeclaActivar.Value))
                {
                    Plugin.Activo.Value = !Plugin.Activo.Value;
                    Plugin.Log.LogInfo("Efecto " + (Plugin.Activo.Value ? "ACTIVO" : "APAGADO"));
                    Aplicar();
                }
                if (Pulsada(Plugin.TeclaRecargar.Value))
                {
                    Plugin.Recargar();
                    Aplicar();
                    Realismo.Aplicar();
                    AplicarCalidad();
                    AplicarDlss();
                    Plugin.Log.LogInfo("Configuración recargada");
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Teclas: " + e.Message);
            }
            TexturasIA.Tick();
            Realismo.Tick();
            Materiales.Tick();
            Pruebas.Tick();
            Opciones.Tick();
            Interfaz.Tick();
            Portapapeles.Tick();
        }

        internal void AplicarPublico() => Aplicar();
        internal void AplicarCalidadPublico() => AplicarCalidad();

        void Crear()
        {
            try { HDDynamicResolutionPlatformCapabilities.ActivateDLSS(); Plugin.Log.LogInfo("ActivateDLSS temprano: detectado=" + HDDynamicResolutionPlatformCapabilities.DLSSDetected); }
            catch (Exception e) { Plugin.Log.LogWarning("ActivateDLSS temprano: " + e.Message); }
            Actual = this;
            volGo = new GameObject("TerrorCineVolume");
            DontDestroyOnLoad(volGo);
            volGo.hideFlags = HideFlags.HideAndDontSave;
            vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 500f;
            vol.weight = 1f;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.HideAndDontSave;
            grano = profile.Add<FilmGrain>(true);
            vin = profile.Add<Vignette>(true);
            ab = profile.Add<ChromaticAberration>(true);
            col = profile.Add<ColorAdjustments>(true);
            lgg = profile.Add<LiftGammaGain>(true);
            Realismo.CrearOverrides(profile);
            Opciones.CrearOverrides(profile);
            vol.sharedProfile = profile;
            AjustarCapa();
            Aplicar();
            Plugin.Log.LogInfo("Volume de terror creado");
            AplicarCalidad();
            AplicarDlss();
        }

        // El Volume solo afecta a la cámara si su capa está dentro de la máscara de volúmenes de la cámara.
        void AjustarCapa()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;
            int mask = hd.volumeLayerMask.value;
            if ((mask & (1 << volGo.layer)) != 0) return;
            for (int i = 0; i < 32; i++)
                if ((mask & (1 << i)) != 0) { volGo.layer = i; Plugin.Log.LogInfo("Volume movido a la capa " + i); return; }
            if (!avisoCapa) { avisoCapa = true; Plugin.Log.LogWarning("La cámara no ve ninguna capa de volúmenes"); }
        }

        void DiagnosticoDlss()
        {
            try
            {
                var asset = HDRenderPipeline.currentAsset;
                if (asset != null)
                {
                    var r = asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
                    Plugin.Log.LogInfo("HDRP ASSET: resDinamica=" + r.enabled + " enableDLSS=" + r.enableDLSS +
                        " dlssCalidad=" + r.DLSSPerfQualitySetting + " optimo=" + r.DLSSUseOptimalSettings +
                        " tipo=" + r.dynResType + " minPct=" + r.minPercentage + " maxPct=" + r.maxPercentage);
                }
                else Plugin.Log.LogWarning("HDRP ASSET: no encontrado");
                Plugin.Log.LogInfo("API gráfica: " + SystemInfo.graphicsDeviceType + " | GPU: " + SystemInfo.graphicsDeviceName + " | versión: " + SystemInfo.graphicsDeviceVersion);
                HDDynamicResolutionPlatformCapabilities.ActivateDLSS();
                Plugin.Log.LogInfo("Tras ActivateDLSS: detectado=" + HDDynamicResolutionPlatformCapabilities.DLSSDetected);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Diagnóstico DLSS: " + e.Message); }
        }

        bool antesLog;
        bool dlssConfirmado;
        bool assetForzado;
        void ForzarAssetDlss()
        {
            if (assetForzado) return;
            assetForzado = true;
            try
            {
                var asset = HDRenderPipeline.currentAsset;
                if (asset == null) { Plugin.Log.LogWarning("ForzarAsset: sin asset"); return; }
                var st = asset.m_RenderPipelineSettings;
                var dr = st.dynamicResolutionSettings;
                if (Plugin.DlssActivar.Value)
                {
                    dr.enabled = true;
                    dr.enableDLSS = true;
                    dr.DLSSUseOptimalSettings = true;
                    dr.DLSSPerfQualitySetting = (uint)(Plugin.DlssCalidad.Value >= 0 ? Plugin.DlssCalidad.Value : 2);
                    dr.dynResType = DynamicResolutionType.Hardware;
                    dr.upsampleFilter = DynamicResUpscaleFilter.CatmullRom;
                    st.dynamicResolutionSettings = dr;
                }
                bool cambiado = Plugin.DlssActivar.Value;
                cambiado |= Realismo.AjustarAsset(ref st);
                if (!cambiado) { Plugin.Log.LogInfo("ForzarAsset: nada que cambiar, no se reconstruye el pipeline"); return; }
                asset.m_RenderPipelineSettings = st;
                var r2 = asset.currentPlatformRenderPipelineSettings.dynamicResolutionSettings;
                Plugin.Log.LogInfo("ForzarAsset: ahora resDinamica=" + r2.enabled + " enableDLSS=" + r2.enableDLSS + " calidad=" + r2.DLSSPerfQualitySetting);
                asset.OnValidate();
                Plugin.Log.LogInfo("ForzarAsset: OnValidate llamado (reconstruye el pipeline)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("ForzarAsset: " + e); }
        }

        void AplicarDlss()
        {
            var cam = Camera.main;
            if (cam == null) return;
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) return;
            if (!diagHecho) { diagHecho = true; DiagnosticoDlss(); }
            if (!antesLog) Plugin.Log.LogInfo("DLSS ANTES: detectado=" + HDDynamicResolutionPlatformCapabilities.DLSSDetected +
                " puedeRenderizar=" + hd.cameraCanRenderDLSS +
                " permitido=" + hd.allowDeepLearningSuperSampling +
                " dinamica=" + hd.allowDynamicResolution +
                " calidad=" + hd.deepLearningSuperSamplingQuality +
                " optimo=" + hd.deepLearningSuperSamplingUseOptimalSettings +
                " nitidez=" + hd.deepLearningSuperSamplingSharpening +
                " pantalla=" + Screen.width + "x" + Screen.height);
            ForzarAssetDlss();
            if (!Plugin.DlssActivar.Value) { dlssHecho = true; antesLog = true; return; }
            try
            {
                var gm = UnityEngine.Object.FindObjectOfType<GameManager>();
                if (gm != null)
                {
                    int q = Plugin.DlssCalidad.Value >= 0 ? Plugin.DlssCalidad.Value : 2;
                    gm.SetAllCameraDlss(true, true, q);
                    dlssHecho = true;
                    Plugin.Log.LogInfo("GameManager.SetAllCameraDlss(true,true," + q + ") llamado; puedeRenderizar=" + hd.cameraCanRenderDLSS + " permitido=" + hd.allowDeepLearningSuperSampling + " calidad=" + hd.deepLearningSuperSamplingQuality + " dinamica=" + hd.allowDynamicResolution);
                }
                else { }
            }
            catch (Exception e) { Plugin.Log.LogWarning("SetAllCameraDlss: " + e.Message); }
            hd.allowDynamicResolution = true;
            hd.allowDeepLearningSuperSampling = true;
            if (Plugin.DlssCalidad.Value >= 0)
            {
                hd.deepLearningSuperSamplingUseCustomQualitySettings = true;
                hd.deepLearningSuperSamplingQuality = (uint)Plugin.DlssCalidad.Value;
            }
            if (Plugin.DlssNitidez.Value >= 0f) hd.deepLearningSuperSamplingSharpening = Plugin.DlssNitidez.Value;
            hd.deepLearningSuperSamplingUseOptimalSettings = true;
            if (!antesLog) Plugin.Log.LogInfo("DLSS DESPUÉS: puedeRenderizar=" + hd.cameraCanRenderDLSS + " permitido=" + hd.allowDeepLearningSuperSampling + " calidad=" + hd.deepLearningSuperSamplingQuality);
            antesLog = true;
            if (hd.cameraCanRenderDLSS && !dlssConfirmado)
            {
                dlssConfirmado = true;
                string escala = "?";
                try { escala = DynamicResolutionHandler.instance.GetCurrentScale().ToString(); } catch { }
                Plugin.Log.LogInfo("DLSS ACTIVO: calidad=" + hd.deepLearningSuperSamplingQuality + " escalaRender=" + escala + " salida=" + Screen.width + "x" + Screen.height);
            }
        }

        bool calidadGuardada; AnisotropicFiltering anisoOrig; float lodOrig = 1f;
        void AplicarCalidad()
        {
            Plugin.Log.LogInfo("CALIDAD ANTES: nivel=" + QualitySettings.GetQualityLevel() +
                " texLimite=" + QualitySettings.masterTextureLimit +
                " aniso=" + QualitySettings.anisotropicFiltering +
                " lodBias=" + QualitySettings.lodBias +
                " maxLod=" + QualitySettings.maximumLODLevel +
                " streamingMip=" + QualitySettings.streamingMipmapsActive +
                " mipBudget=" + QualitySettings.streamingMipmapsMemoryBudget +
                " sombraDist=" + QualitySettings.shadowDistance +
                " vsync=" + QualitySettings.vSyncCount +
                " pantalla=" + Screen.width + "x" + Screen.height +
                " fpsMax=" + Application.targetFrameRate);
            if (!calidadGuardada) { calidadGuardada = true; anisoOrig = QualitySettings.anisotropicFiltering; lodOrig = QualitySettings.lodBias; }
            if (Plugin.TexLimite.Value >= 0) QualitySettings.masterTextureLimit = Plugin.TexLimite.Value;
            if (Plugin.Aniso.Value) { QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable; Texture.SetGlobalAnisotropicFilteringLimits(16, 16); }
            else { QualitySettings.anisotropicFiltering = anisoOrig; Texture.SetGlobalAnisotropicFilteringLimits(1, 16); }
            if (Plugin.SinStreaming.Value) QualitySettings.streamingMipmapsActive = false;
            QualitySettings.lodBias = Plugin.SesgoLod.Value > 0f ? Plugin.SesgoLod.Value : lodOrig;
            Plugin.Log.LogInfo("CALIDAD DESPUÉS: texLimite=" + QualitySettings.masterTextureLimit + " aniso=" + QualitySettings.anisotropicFiltering + " lodBias=" + QualitySettings.lodBias + " streamingMip=" + QualitySettings.streamingMipmapsActive);
        }

        void Aplicar()
        {
            if (vol == null) return;
            vol.enabled = true;   // el volumen también lleva los efectos de realismo; solo se apagan los de color
            bool post = Plugin.Activo.Value && PostEncendido;
            grano.active = post && Plugin.Grano.Value > 0f;
            vin.active = post && Plugin.Viñeteo.Value > 0f;
            ab.active = post && Plugin.Aberracion.Value > 0f;
            col.active = post;
            lgg.active = post;
            grano.type.Override(FilmGrainLookup.Medium3);
            grano.intensity.Override(Plugin.Grano.Value);
            grano.response.Override(0.8f);
            vin.mode.Override(VignetteMode.Procedural);
            vin.intensity.Override(Plugin.Viñeteo.Value);
            vin.smoothness.Override(Plugin.SuavizadoViñeteo.Value);
            vin.roundness.Override(1f);
            ab.intensity.Override(Plugin.Aberracion.Value);
            col.postExposure.Override(Plugin.Exposicion.Value);
            col.contrast.Override(Plugin.Contraste.Value);
            col.saturation.Override(Plugin.Saturacion.Value);
            float t = Plugin.TintePorcentaje.Value;
            col.colorFilter.Override(Color.Lerp(Color.white, new Color(0.72f, 0.88f, 0.9f), t));
            lgg.gamma.Override(new Vector4(1f, 1f, 1f, Plugin.Gamma.Value));
        }
    }
}
