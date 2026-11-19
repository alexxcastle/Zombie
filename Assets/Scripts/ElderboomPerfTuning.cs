using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Frame-rate tuning that applies to every quality level (measured on an Apple M2: 11-20 fps at native Retina).
//  1. Render scale: the 3D view is rendered at a pixel budget that depends on the quality level and upscaled
//     (HDRP dynamic resolution, FSR 1). The HUD and menus stay at full resolution.
//  2. Lights: the village has ~85 point lights, all with shadows redrawn every frame and never faded out.
//     Lights now fade with distance, stop feeding the volumetric fog when far, and only the nearest few
//     keep their shadows.
// Set ElderboomPerfTuning.Enabled = false to compare (Elderboom HDRP > Dev > Toggle Perf Tuning).
public sealed class ElderboomPerfTuning : MonoBehaviour
{
    public static bool Enabled = true;
    static ElderboomPerfTuning instance;

    // Rendered rows per quality: LOW, MEDIUM, HIGH, PERFORMANCE (ElderboomDisplaySettings.Quality).
    static readonly float[] RowBudget = { 720f, 900f, 1080f, 600f, 1440f };   // last: ULTRA
    static readonly int[] ShadowLights = { 2, 4, 6, 1, 10 };
    const float LightFade = 60f, VolumetricFade = 28f, ShadowFade = 18f;

    sealed class Lamp { public HDAdditionalLightData hd; public Light light; public bool hadShadows; public float fade, volFade, shadowFade; }
    readonly List<Lamp> lamps = new List<Lamp>();
    float nextScan, nextShadowPass;
    static float percentage = 100f;
    public static float RenderScale => percentage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ElderboomPerfTuning");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ElderboomPerfTuning>();
        DynamicResolutionHandler.SetDynamicResScaler(() => percentage, DynamicResScalePolicyType.ReturnsPercentage);
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) { instance.lamps.Clear(); instance.nextScan = 0f; } };
    }

    void Update()
    {
        float now = Time.unscaledTime;
        if (now >= nextScan) { nextScan = now + 1f; UpdateScale(); ScanCameras(); ScanLights(); }
        if (now >= nextShadowPass) { nextShadowPass = now + .4f; ShadowPass(); }
    }

    static void UpdateScale()
    {
        float forced = ElderboomGraphicsOptions.RenderScaleOverride; // Settings > Video > Render resolution
        if (forced > 0f) { percentage = forced; return; }
        if (!Enabled) { percentage = 100f; return; }
        float budget = RowBudget[Mathf.Clamp(ElderboomDisplaySettings.Quality, 0, RowBudget.Length - 1)];
        percentage = Mathf.Clamp(budget / Mathf.Max(1, Screen.height) * 100f, 50f, 100f);
    }

    static void ScanCameras()
    {
        foreach (var cam in Camera.allCameras)
        {
            var hd = cam.GetComponent<HDAdditionalCameraData>();
            if (hd != null && !hd.allowDynamicResolution) hd.allowDynamicResolution = true;
        }
    }

    void ScanLights()
    {
        lamps.RemoveAll(l => l.hd == null);
        var known = new HashSet<HDAdditionalLightData>();
        foreach (var l in lamps) known.Add(l.hd);
        foreach (var hd in FindObjectsByType<HDAdditionalLightData>(FindObjectsInactive.Exclude))
        {
            if (known.Contains(hd)) continue;
            var light = hd.GetComponent<Light>();
            if (light == null || light.type == LightType.Directional) continue;
            // Short-lived effect lights (muzzle flash, burning zombies, abilities) are left alone.
            if (!hd.gameObject.isStatic && light.shadows == LightShadows.None && hd.fadeDistance < 9000f) continue;
            lamps.Add(new Lamp { hd = hd, light = light, hadShadows = light.shadows != LightShadows.None, fade = hd.fadeDistance, volFade = hd.volumetricFadeDistance, shadowFade = hd.shadowFadeDistance });
        }
        foreach (var l in lamps)
        {
            l.hd.fadeDistance = Enabled ? Mathf.Min(l.fade, LightFade) : l.fade;
            l.hd.volumetricFadeDistance = Enabled ? Mathf.Min(l.volFade, VolumetricFade) : l.volFade;
            l.hd.shadowFadeDistance = Enabled ? Mathf.Min(l.shadowFade, ShadowFade) : l.shadowFade;
        }
    }

    // Only the nearest few lamps keep their shadows; they are the only ones whose shadows can be seen anyway.
    readonly List<(float d, Lamp lamp)> order = new List<(float, Lamp)>();
    void ShadowPass()
    {
        var cam = Camera.main;
        if (cam == null || lamps.Count == 0) return;
        Vector3 eye = cam.transform.position;
        order.Clear();
        foreach (var l in lamps)
        {
            if (l.hd == null || !l.hadShadows) continue;
            order.Add(((l.hd.transform.position - eye).sqrMagnitude, l));
        }
        order.Sort((a, b) => a.d.CompareTo(b.d));
        int keep = Enabled ? ShadowLights[Mathf.Clamp(ElderboomDisplaySettings.Quality, 0, ShadowLights.Length - 1)] : int.MaxValue;
        if (ElderboomGraphicsOptions.ShadowLampOverride >= 0) keep = ElderboomGraphicsOptions.ShadowLampOverride; // Settings > Video > Lamp shadows
        float limit = (ShadowFade + 2f) * (ShadowFade + 2f);
        for (int i = 0; i < order.Count; i++)
        {
            bool on = !Enabled || (i < keep && order[i].d <= limit);
            var light = order[i].lamp.light;
            if ((light.shadows != LightShadows.None) != on) order[i].lamp.hd.EnableShadows(on);
        }
    }
}
