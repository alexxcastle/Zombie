using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// "ULTRA" graphics quality (Settings > Video): Unity's HIGH level plus the heavy HDRP features the other presets
// leave off. LOW / MEDIUM / HIGH / PERFORMANCE look and cost exactly what they did before.
//  - screen-space reflections (wet ground, metal, windows) and screen-space global illumination (lamp light
//    bouncing off walls and ground);
//  - stronger ambient occlusion and longer contact shadows; more lamps keep their shadows (ElderboomPerfTuning);
//  - high-quality temporal antialiasing; full render resolution up to 1440 rows;
//  - camera motion blur, a touch of chromatic aberration, and while aiming the rear of the weapon goes out of focus.
// The HDRP asset has SSR and SSGI support switched on for this (Settings/Elderboom_HDRP.asset); a global volume keeps
// both off outside ULTRA.
public sealed class ElderboomUltraGraphics : MonoBehaviour
{
    public static bool Active => ElderboomDisplaySettings.Quality == ElderboomDisplaySettings.Ultra;

    static ElderboomUltraGraphics instance;
    Volume look, aim;
    ScreenSpaceReflection ssr; GlobalIllumination gi;
    bool applied, first = true;
    float nextScan, aimWeight;
    HDRPSlicePlayer player;
    readonly Dictionary<HDAdditionalCameraData, (bool custom, FrameSettingsOverrideMask mask, FrameSettings settings, HDAdditionalCameraData.TAAQualityLevel taa)> saved
        = new Dictionary<HDAdditionalCameraData, (bool, FrameSettingsOverrideMask, FrameSettings, HDAdditionalCameraData.TAAQualityLevel)>();
    static readonly FrameSettingsField[] Fields = { FrameSettingsField.SSR, FrameSettingsField.SSGI };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ElderboomUltraGraphics"); DontDestroyOnLoad(go);
        instance = go.AddComponent<ElderboomUltraGraphics>();
    }

    void Awake()
    {
        // Always on: decides SSR / SSGI for every preset (above the atmosphere volume, below the video options).
        var gate = gameObject.AddComponent<Volume>(); gate.isGlobal = true; gate.priority = 54f;
        var gp = ScriptableObject.CreateInstance<VolumeProfile>(); gate.sharedProfile = gp;
        ssr = gp.Add<ScreenSpaceReflection>(false);
        ssr.enabled.Override(false); ssr.enabledTransparent.Override(false);
        gi = gp.Add<GlobalIllumination>(false);
        gi.enable.Override(false);

        // ULTRA look (weight 1 only in ULTRA).
        var lookGo = new GameObject("UltraLook"); lookGo.transform.SetParent(transform, false);
        look = lookGo.AddComponent<Volume>(); look.isGlobal = true; look.priority = 55f; look.weight = 0f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>(); look.sharedProfile = p;
        var ao = p.Add<ScreenSpaceAmbientOcclusion>(false);
        ao.intensity.Override(.75f); ao.radius.Override(2f); ao.directLightingStrength.Override(.35f);
        var contact = p.Add<ContactShadows>(false);
        contact.enable.Override(true); contact.length.Override(.2f); contact.maxDistance.Override(50f);
        var blur = p.Add<MotionBlur>(false);
        blur.intensity.Override(.35f); blur.maximumVelocity.Override(120f); blur.minimumVelocity.Override(4f);
        var ca = p.Add<ChromaticAberration>(false);
        ca.intensity.Override(.07f);

        // Aiming: the part of the weapon closest to the eye goes soft (weight follows the aim).
        var aimGo = new GameObject("UltraAimFocus"); aimGo.transform.SetParent(transform, false);
        aim = aimGo.AddComponent<Volume>(); aim.isGlobal = true; aim.priority = 56f; aim.weight = 0f;
        var ap = ScriptableObject.CreateInstance<VolumeProfile>(); aim.sharedProfile = ap;
        var dof = ap.Add<DepthOfField>(false);
        dof.focusMode.Override(DepthOfFieldMode.Manual);
        dof.nearFocusStart.Override(0f); dof.nearFocusEnd.Override(.32f);
        dof.farFocusStart.Override(900f); dof.farFocusEnd.Override(1000f);
    }

    void Update()
    {
        bool on = Active;
        if (on != applied || first) { first = false; applied = on; Apply(on); nextScan = Time.unscaledTime + 1f; }
        else if (on && Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; Cameras(true); } // cameras created later

        // Aim focus.
        if (player == null && on && Time.frameCount % 30 == 0) player = FindAnyObjectByType<HDRPSlicePlayer>();
        float target = on && player != null && player.isActiveAndEnabled && player.IsAiming && !player.IsDead ? 1f : 0f;
        aimWeight = Mathf.MoveTowards(aimWeight, target, Time.unscaledDeltaTime * 5f);
        aim.weight = aimWeight;
        aim.enabled = aimWeight > .001f;
    }

    void Apply(bool on)
    {
        ssr.enabled.value = on; gi.enable.value = on;
        look.weight = on ? 1f : 0f; look.enabled = on;
        Cameras(on);
    }

    void Cameras(bool on)
    {
        if (on)
        {
            foreach (var cam in Camera.allCameras)
            {
                var hd = cam.GetComponent<HDAdditionalCameraData>();
                if (hd == null || saved.ContainsKey(hd)) continue;
                saved[hd] = (hd.customRenderingSettings, hd.renderingPathCustomFrameSettingsOverrideMask, hd.renderingPathCustomFrameSettings, hd.TAAQuality);
                hd.customRenderingSettings = true;
                var mask = hd.renderingPathCustomFrameSettingsOverrideMask;
                var settings = hd.renderingPathCustomFrameSettings;
                foreach (var f in Fields) { mask.mask[(uint)f] = true; settings.SetEnabled(f, true); }
                hd.renderingPathCustomFrameSettingsOverrideMask = mask;
                hd.renderingPathCustomFrameSettings = settings;
                hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;
            }
            return;
        }
        foreach (var kv in saved)
        {
            if (kv.Key == null) continue;
            kv.Key.customRenderingSettings = kv.Value.custom;
            kv.Key.renderingPathCustomFrameSettingsOverrideMask = kv.Value.mask;
            kv.Key.renderingPathCustomFrameSettings = kv.Value.settings;
            kv.Key.TAAQuality = kv.Value.taa;
        }
        saved.Clear();
    }
}
