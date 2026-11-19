using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Extra graphics options (Settings > Video), applied live and kept in PlayerPrefs. They help slower PCs on top of the
// quality preset: view distance, internal resolution, lamp shadows, volumetric fog, ambient occlusion, grass and a
// frame cap. Defaults leave the game exactly as it looks with the preset alone.
public sealed class ElderboomGraphicsOptions : MonoBehaviour
{
    const string Prefix = "hdrp.gfx.";
    public static readonly string[] DistanceLabels = { "NEAR", "MEDIUM", "FAR", "MAX" };
    static readonly float[] DistanceMeters = { 90f, 150f, 250f, 0f };      // 0 = untouched (the camera's own 400 m)
    static readonly float[] DistanceTrees = { 70f, 100f, 130f, 0f };
    static readonly float[] DistanceLod = { .55f, .75f, .9f, 1f };
    public static readonly string[] ScaleLabels = { "AUTO", "50%", "67%", "75%", "85%", "100%" };
    static readonly float[] ScaleValues = { 0f, 50f, 67f, 75f, 85f, 100f };
    public static readonly string[] ShadowLabels = { "AUTO", "OFF", "LOW", "HIGH" };
    static readonly int[] ShadowCounts = { -1, 0, 2, 6 };
    public static readonly string[] GrassLabels = { "LOW", "MEDIUM", "HIGH" };
    static readonly float[] GrassDensity = { .3f, .6f, 1f }, GrassDistance = { 30f, 50f, 0f };
    public static readonly string[] FpsLabels = { "UNLIMITED", "30", "60", "120", "144" };
    static readonly int[] FpsValues = { -1, 30, 60, 120, 144 };

    public static int Distance { get => Get("distance", 3); set => Set("distance", value); }
    public static int Scale { get => Get("scale", 0); set => Set("scale", value); }
    public static int Shadows { get => Get("shadows", 0); set => Set("shadows", value); }
    public static int Grass { get => Get("grass", 2); set => Set("grass", value); }
    public static int FrameCap { get => Get("fps", 0); set => Set("fps", value); }
    public static bool VolumetricFog { get => Get("volumetric", 1) == 1; set => Set("volumetric", value ? 1 : 0); }
    public static bool AmbientOcclusion { get => Get("ssao", 1) == 1; set => Set("ssao", value ? 1 : 0); }

    // Used by ElderboomPerfTuning: 0 = automatic (preset), otherwise a fixed render scale in percent.
    public static float RenderScaleOverride => ScaleValues[Mathf.Clamp(Scale, 0, ScaleValues.Length - 1)];
    // Used by ElderboomPerfTuning: -1 = preset, otherwise how many nearby lamps keep shadows.
    public static int ShadowLampOverride => ShadowCounts[Mathf.Clamp(Shadows, 0, ShadowCounts.Length - 1)];
    // Used by the pause menu: -1 = no cap.
    public static int TargetFps => FpsValues[Mathf.Clamp(FrameCap, 0, FpsValues.Length - 1)];

    static int Get(string k, int d) => PlayerPrefs.GetInt(Prefix + k, d);
    static void Set(string k, int v) { PlayerPrefs.SetInt(Prefix + k, v); PlayerPrefs.Save(); if (instance != null) instance.ApplyNow(); }

    static ElderboomGraphicsOptions instance;
    Volume volume; Fog fog; ScreenSpaceAmbientOcclusion ssao;
    readonly Dictionary<Camera, (float[] original, float[] applied)> cameras = new Dictionary<Camera, (float[], float[])>();
    readonly Dictionary<Terrain, (float trees, float detailDist, float density)> terrains = new Dictionary<Terrain, (float, float, float)>();
    float lodBase = -1f, lodApplied = -1f, nextApply;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (instance != null) return;
        var go = new GameObject("ElderboomGraphicsOptions"); DontDestroyOnLoad(go);
        instance = go.AddComponent<ElderboomGraphicsOptions>();
        SceneManager.sceneLoaded += (s, m) => { if (instance != null) { instance.terrains.Clear(); instance.cameras.Clear(); instance.nextApply = 0f; } };
    }

    void Awake()
    {
        volume = gameObject.AddComponent<Volume>(); volume.isGlobal = true; volume.priority = 60f; // above the atmosphere (50)
        var p = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = p;
        fog = p.Add<Fog>(false);
        ssao = p.Add<ScreenSpaceAmbientOcclusion>(false);
    }

    void Update()
    {
        if (Time.unscaledTime < nextApply) return;
        nextApply = Time.unscaledTime + 1f; // new cameras, terrains and late scene setup are picked up within a second
        ApplyNow();
    }

    void ApplyNow()
    {
        // First-person arms are skinned with up to 4 bones per vertex (twist bones, smooth elbows and wrists): the
        // Low/Medium/High presets' 2 bones would bring back the creases. Never fewer than 4.
        if (QualitySettings.skinWeights != SkinWeights.Unlimited && QualitySettings.skinWeights < SkinWeights.FourBones)
            QualitySettings.skinWeights = SkinWeights.FourBones;

        // Volumetric fog and ambient occlusion: override only when switched off.
        fog.enableVolumetricFog.overrideState = !VolumetricFog; fog.enableVolumetricFog.value = false;
        ssao.intensity.overrideState = !AmbientOcclusion; ssao.intensity.value = 0f;

        // View distance: every layer except the mountain backdrop is culled at the chosen distance.
        int di = Mathf.Clamp(Distance, 0, DistanceMeters.Length - 1);
        float meters = DistanceMeters[di];
        foreach (var cam in Camera.allCameras)
        {
            if (cam == null || cam.orthographic) continue;
            var current = cam.layerCullDistances;
            if (!cameras.TryGetValue(cam, out var rec) || !Same(current, rec.applied)) rec = ((float[])current.Clone(), null);
            var d = (float[])rec.original.Clone();
            if (meters > 0f)
                for (int i = 0; i < 32; i++)
                {
                    if (i == ElderboomFarView.Layer) continue;
                    float own = d[i] > 0f ? d[i] : cam.farClipPlane;
                    d[i] = Mathf.Min(own, meters);
                }
            cam.layerCullDistances = d;
            if (GraphicsSettings.currentRenderPipeline == null) cam.layerCullSpherical = true;
            cameras[cam] = (rec.original, d);
        }
        // LOD bias: the PERFORMANCE preset sets its own; otherwise scale the preset's value with the view distance.
        if (!ElderboomPerformanceMode.Enabled)
        {
            if (lodBase < 0f || !Mathf.Approximately(QualitySettings.lodBias, lodApplied)) lodBase = QualitySettings.lodBias;
            lodApplied = lodBase * DistanceLod[di]; QualitySettings.lodBias = lodApplied;
        }

        // Village terrain: tree distance with the view distance; grass density and distance with the grass option.
        int gi = Mathf.Clamp(Grass, 0, GrassDensity.Length - 1);
        foreach (var t in Terrain.activeTerrains)
        {
            if (t == null || t.gameObject.layer == ElderboomFarView.Layer) continue;
            if (!terrains.TryGetValue(t, out var orig)) terrains[t] = orig = (t.treeDistance, t.detailObjectDistance, t.detailObjectDensity);
            t.treeDistance = DistanceTrees[di] > 0f ? Mathf.Min(orig.trees, DistanceTrees[di]) : orig.trees;
            t.detailObjectDensity = orig.density * GrassDensity[gi];
            t.detailObjectDistance = GrassDistance[gi] > 0f ? Mathf.Min(orig.detailDist, GrassDistance[gi]) : orig.detailDist;
        }

        if (QualitySettings.vSyncCount == 0) Application.targetFrameRate = TargetFps;
    }

    static bool Same(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!Mathf.Approximately(a[i], b[i])) return false;
        return true;
    }
}
