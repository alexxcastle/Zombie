using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// "PERFORMANCE" graphics quality (settings): the lowest Unity quality level plus HDRP features switched off on
// every camera (volumetric fog, SSAO, SSR, contact and screen-space shadows, subsurface scattering, refraction,
// distortion, depth of field, motion blur, chromatic aberration, film grain, lens flares, volumetric clouds),
// shorter shadow distance and coarser LODs. Meant for slow computers, together with the 854x480 resolution.
public sealed class ElderboomPerformanceMode : MonoBehaviour
{
    static readonly string[] Off =
    {
        "Volumetrics", "SSAO", "SSR", "TransparentSSR", "SSGI", "ContactShadows", "ScreenSpaceShadows", "SubsurfaceScattering",
        "Transmission", "Refraction", "Distortion", "DepthOfField", "MotionBlur", "ChromaticAberration", "FilmGrain",
        "LensFlareScreenSpace", "LensFlareDataDriven", "VolumetricClouds", "LowResTransparent"
    };

    static ElderboomPerformanceMode instance;
    public static bool Enabled { get; private set; }
    float lodBias = 1f, nextScan;
    Volume volume;
    readonly System.Collections.Generic.Dictionary<HDAdditionalCameraData, (bool custom, FrameSettingsOverrideMask mask, FrameSettings settings)> saved =
        new System.Collections.Generic.Dictionary<HDAdditionalCameraData, (bool, FrameSettingsOverrideMask, FrameSettings)>();

    public static void Set(bool on)
    {
        if (on == Enabled && (instance != null || !on)) return;
        Enabled = on;
        if (on && instance == null)
        {
            var go = new GameObject("ElderboomPerformanceMode");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<ElderboomPerformanceMode>();
        }
        if (instance != null) instance.Apply();
        if (!on && instance != null) { Destroy(instance.gameObject); instance = null; }
    }

    void Awake() { lodBias = QualitySettings.lodBias; }

    void Update()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f; // new cameras (scene loads, menu stage) get the same settings
        Apply();
    }

    void Apply()
    {
        QualitySettings.lodBias = Enabled ? .45f : lodBias;
        foreach (var cam in Camera.allCameras)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd == null) continue;
            if (Enabled) { if (!saved.ContainsKey(hd)) saved[hd] = (hd.customRenderingSettings, hd.renderingPathCustomFrameSettingsOverrideMask, hd.renderingPathCustomFrameSettings); DisableFeatures(hd); }
        }
        if (!Enabled)
        {
            // Put back each camera's own settings.
            foreach (var kv in saved)
            {
                if (kv.Key == null) continue;
                kv.Key.customRenderingSettings = kv.Value.custom;
                kv.Key.renderingPathCustomFrameSettingsOverrideMask = kv.Value.mask;
                kv.Key.renderingPathCustomFrameSettings = kv.Value.settings;
            }
            saved.Clear();
        }
        if (Enabled)
        {
            if (volume == null)
            {
                volume = gameObject.AddComponent<Volume>();
                volume.isGlobal = true; volume.priority = 10000;
                volume.sharedProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                volume.sharedProfile.Add<HDShadowSettings>(true).maxShadowDistance.Override(35f);
            }
        }
        else if (volume != null) { Destroy(volume); volume = null; }
    }

    static void DisableFeatures(HDAdditionalCameraData hd)
    {
        hd.customRenderingSettings = true;
        var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
        var settings = hd.renderingPathCustomFrameSettings;
        foreach (var name in Off)
        {
            if (!Enum.TryParse(name, out FrameSettingsField field)) continue;
            mask.mask[(uint)field] = true;
            settings.SetEnabled(field, false);
        }
        hd.renderingPathCustomFrameSettingsOverrideMask = mask;
        hd.renderingPathCustomFrameSettings = settings;
    }
}
