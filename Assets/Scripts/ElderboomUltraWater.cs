using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// ULTRA graphics only: the creek becomes an HDRP water surface (moving waves, refraction of the bed, caustics, foam)
// and wet ground (puddles, mud) gets sharper screen-space reflections. Every other preset keeps the cheap flowing ribbon.
public sealed class ElderboomUltraWater : MonoBehaviour
{
    public GameObject simple, ultra;
    Volume volume; bool state, init;

    void Awake()
    {
        var go = new GameObject("UltraWaterVolume"); go.transform.SetParent(transform, false);
        volume = go.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 57f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = p;
        p.Add<WaterRendering>(false).enable.Override(true);
        var ssr = p.Add<ScreenSpaceReflection>(false);
        ssr.enabled.Override(true); ssr.minSmoothness = .55f; ssr.smoothnessFadeStart = .65f; ssr.rayMaxIterations = 96; ssr.screenFadeDistance.Override(.08f);
        volume.enabled = false;
    }

    void Update()
    {
        bool on = ElderboomUltraGraphics.Active;
        if (init && on == state) return;
        init = true; state = on;
        if (ultra) ultra.SetActive(on);
        if (simple) simple.SetActive(!on);
        volume.enabled = on;
    }
}
