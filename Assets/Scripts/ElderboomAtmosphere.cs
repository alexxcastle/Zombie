using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Winter golden hour: the look is in Elderboom_Dusk_Profile (ElderboomDusk editor tool). Formerly the night look:
//  - ground fog that thickens toward the ground and scatters light forward (shafts around lanterns and the moon);
//  - cold shadows / warm highlights (split toning), a touch more contrast, cooler white balance;
//  - softer, wider bloom on lanterns; vignette; light film grain;
//  - ambient occlusion, contact shadows and micro shadows so objects sit on the ground.
// Lanterns also flicker slightly. Elderboom HDRP > Dev > Toggle Atmosphere switches it for comparison.
public sealed class ElderboomAtmosphere : MonoBehaviour
{
    public static bool Enabled = true;
    const string GameScene = "Elderboom_Unity_Port";
    static ElderboomAtmosphere instance;
    Volume volume;
    readonly List<(Light light, float baseIntensity, float seed)> flames = new List<(Light, float, float)>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
        Ensure(SceneManager.GetActiveScene());
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode) => Ensure(scene);

    static void Ensure(Scene scene)
    {
        if (instance != null || scene.name != GameScene) return;
        var go = new GameObject("ElderboomAtmosphere");
        instance = go.AddComponent<ElderboomAtmosphere>(); // lives and dies with the game scene
    }

    void Awake()
    {
        volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true; volume.priority = 50f;
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        volume.sharedProfile = p;

        // The look itself (sky, fog, grading, occlusion) lives in Assets/Settings/Elderboom_Dusk_Profile.asset;
        // this component only keeps the lantern flames alive.
    }

    void Update()
    {
        volume.weight = Enabled ? 1f : 0f;
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + 5f;
            if (flames.Count == 0)
                foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                {
                    string n = light.name;
                    if (n.Contains("Lantern") || n.Contains("Lamp_Light") || n.Contains("oil_lamp"))
                        flames.Add((light, light.intensity, Random.Range(0f, 100f)));
                }
        }
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 eye = cam.transform.position; float t = Time.time;
        foreach (var (light, baseIntensity, seed) in flames)
        {
            if (light == null) continue;
            if (!Enabled) { light.intensity = baseIntensity; continue; }
            if ((light.transform.position - eye).sqrMagnitude > 45f * 45f) continue;
            // Slow breathing plus a faster, smaller tremble: a flame, not a strobe.
            float f = .93f + .09f * Mathf.PerlinNoise(seed, t * 1.7f) + .05f * Mathf.PerlinNoise(seed + 31f, t * 7.5f);
            light.intensity = baseIntensity * f;
        }
    }

    void OnDestroy() { if (instance == this) instance = null; }
}
