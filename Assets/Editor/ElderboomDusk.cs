using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Winter golden hour for the village (replaces the night): low warm sun, physically based sky with volumetric clouds,
// warm ground fog with sun shafts, grading in the profile. Batch:
//   Unity -batchmode -projectPath ... -executeMethod ElderboomDusk.LightBatch   (then .ShotsBatch for pictures)
static partial class ElderboomDusk
{
    const string Scene = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string NightProfile = "Assets/Settings/Elderboom_Night_Profile.asset", DuskProfile = "Assets/Settings/Elderboom_Dusk_Profile.asset";
    public const float SunAzimuth = 252f, SunElevation = 13f;
    static StringBuilder log = new StringBuilder();

    static void Open() { if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene); }
    static void Save(string tag)
    {
        var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Logs/dusk"); File.WriteAllText("Logs/dusk/" + tag + ".txt", log.ToString()); Debug.Log("DUSK_DONE " + tag);
    }

    static T Comp<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet<T>(out var c)) return c;
        c = p.Add<T>(true); AssetDatabase.AddObjectToAsset(c, p); return c;
    }
    static void Drop<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (!p.TryGet<T>(out var c)) return;
        p.Remove<T>(); Object.DestroyImmediate(c, true);
    }

    public static void LightBatch() { Open(); Light(); Save("light"); }

    static void Light()
    {
        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(DuskProfile) == null) AssetDatabase.CopyAsset(NightProfile, DuskProfile);
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DuskProfile);
        p.name = "Elderboom_Dusk_Profile";
        Drop<HDRISky>(p);
        var env = Comp<VisualEnvironment>(p);
        env.skyType.Override(SkySettings.GetUniqueID<PhysicallyBasedSky>());
        env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = Comp<PhysicallyBasedSky>(p);
        sky.aerosolDensity.overrideState = false; sky.aerosolAnisotropy.overrideState = false; sky.aerosolTint.overrideState = false;
        sky.horizonTint.overrideState = false; sky.zenithTint.overrideState = false; sky.horizonZenithShift.overrideState = false;
        sky.groundTint.Override(new Color(.16f, .15f, .14f));
        Drop<CloudLayer>(p);
        env.cloudType.Override(0);
        foreach (var guid in AssetDatabase.FindAssets("t:HDRenderPipelineAsset"))
        {
            var rp = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid)); if (!rp) continue;
            var so = new SerializedObject(rp); var prop = so.FindProperty("m_RenderPipelineSettings.supportVolumetricClouds");
            if (prop != null && !prop.boolValue) { prop.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); log.AppendLine("volumetric clouds enabled in " + rp.name); }
            var wp = so.FindProperty("m_RenderPipelineSettings.supportWater");
            if (wp != null && !wp.boolValue) { wp.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(rp); log.AppendLine("water system enabled in " + rp.name + " (drawn only when a volume asks for it: ULTRA)"); }
        }
        var clouds = Comp<VolumetricClouds>(p);
        clouds.enable.Override(true); clouds.cloudPreset = VolumetricClouds.CloudPresets.Cloudy;
        var ind = Comp<IndirectLightingController>(p); ind.indirectDiffuseLightingMultiplier.Override(2.3f);

        var fog = Comp<Fog>(p);
        fog.enabled.Override(true); fog.enableVolumetricFog.Override(true);
        fog.colorMode.Override(FogColorMode.SkyColor);
        fog.tint.Override(new Color(.9f, .92f, 1f));
        fog.meanFreePath.Override(420f); fog.baseHeight.Override(1.5f); fog.maximumHeight.Override(55f); fog.maxFogDistance.Override(4000f);
        fog.albedo.Override(new Color(1f, .93f, .86f)); fog.anisotropy.Override(.7f);
        fog.depthExtent.Override(110f); fog.mipFogNear.Override(0f); fog.mipFogFar.Override(600f); fog.mipFogMaxMip.Override(.55f);

        var ex = Comp<Exposure>(p); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(7.1f);
        var bloom = Comp<Bloom>(p); bloom.threshold.Override(1f); bloom.intensity.Override(.24f); bloom.scatter.Override(.7f);
        var sh = Comp<HDShadowSettings>(p); sh.maxShadowDistance.Override(150f);
        var color = Comp<ColorAdjustments>(p); color.contrast.Override(12f); color.saturation.Override(4f); color.postExposure.Override(0f);
        Comp<WhiteBalance>(p).temperature.Override(-5f);
        var split = Comp<SplitToning>(p);
        split.shadows.Override(new Color(.47f, .5f, .55f)); split.highlights.Override(new Color(.55f, .51f, .46f)); split.balance.Override(-10f);
        var vig = Comp<Vignette>(p); vig.intensity.Override(.22f); vig.smoothness.Override(.4f);
        var grain = Comp<FilmGrain>(p); grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(.12f); grain.response.Override(.8f);
        var ao = Comp<ScreenSpaceAmbientOcclusion>(p); ao.intensity.Override(.75f); ao.radius.Override(1.8f); ao.directLightingStrength.Override(.2f);
        var contact = Comp<ContactShadows>(p); contact.enable.Override(true); contact.length.Override(.12f); contact.maxDistance.Override(40f);
        var micro = Comp<MicroShadowing>(p); micro.enable.Override(true); micro.opacity.Override(.85f);
        var ssr = Comp<ScreenSpaceReflection>(p); ssr.enabled.Override(true);
        EditorUtility.SetDirty(p);

        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
            if (v.sharedProfile && AssetDatabase.GetAssetPath(v.sharedProfile) == NightProfile) { v.sharedProfile = p; v.name = "Elderboom_HDRP_Dusk_Volume"; EditorUtility.SetDirty(v); }

        var sunGo = GameObject.Find("Moonlight") ?? GameObject.Find("Sunlight");
        var sun = sunGo.GetComponent<Light>(); sunGo.name = "Sunlight";
        sun.transform.rotation = Quaternion.Euler(SunElevation, SunAzimuth + 180f, 0f);
        sun.useColorTemperature = false; sun.color = new Color(1f, .96f, .9f); sun.intensity = 5200f; sun.shadows = LightShadows.Soft;
        var hd = sunGo.GetComponent<HDAdditionalLightData>();
        hd.affectsVolumetric = true; hd.volumetricDimmer = 1f; hd.volumetricShadowDimmer = 1f; hd.angularDiameter = 1.1f;
        hd.interactsWithSky = true; hd.shadowDimmer = 1f;
        EditorUtility.SetDirty(sun); EditorUtility.SetDirty(hd);
        RenderSettings.sun = sun;

        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            if (l.name == "Viewmodel_Soft_Fill") { l.color = new Color(1f, .93f, .84f); l.intensity = 14000f; EditorUtility.SetDirty(l); }
        log.AppendLine("light: profile " + DuskProfile + " sun az " + SunAzimuth + " el " + SunElevation);
    }

    // ---------------------------------------------------------------- pictures (edit mode, the game's field of view)
    public static void ShotsBatch()
    {
        Open();
        string spec = File.Exists("Logs/dusk/shots.txt") ? File.ReadAllText("Logs/dusk/shots.txt") : "";
        Shots(spec);
        Debug.Log("DUSK_DONE shots");
    }

    static Terrain Valley() => Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude).First(t => t.name != "Elderboom_Mountains" && t.terrainData.size.x > 400);
    static float GroundY(float x, float z) { var t = Valley(); return t.SampleHeight(new Vector3(x, 0, z)) + t.transform.position.y; }

    // spec lines: name x z lookX lookZ [eyeHeight] [lookHeight] [vfov]
    static void Shots(string spec)
    {
        Directory.CreateDirectory("Logs/dusk/shots");
        var go = new GameObject("DuskShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>(); cam.nearClipPlane = .08f; cam.farClipPlane = 3200f; cam.cullingMask = ~(1 << 19); cam.enabled = false;
        var d = new float[32]; for (int i = 0; i < 32; i++) d[i] = 400f; d[30] = 0f; cam.layerCullDistances = d; cam.layerCullSpherical = true;
        var hd = go.AddComponent<HDAdditionalCameraData>(); hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var fp = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
        if (fp) foreach (var r in fp.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DuskProfile);
        foreach (var line in spec.Split('\n'))
        {
            var a = line.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries); if (a.Length < 5 || a[0].StartsWith("#")) continue;
            if (a[0] == "vclouds" && prof.TryGet<VolumetricClouds>(out var vc))
            {   // vclouds preset(-1 off, 0 sparse, 1 cloudy, 2 overcast, 3 stormy) x x x   (tests only)
                int pr = int.Parse(a[1]); vc.enable.Override(pr >= 0); if (pr >= 0) vc.cloudPreset = (VolumetricClouds.CloudPresets)pr; continue;
            }
            if (a[0] == "ultra")
            {   // ultra 1 x x x : preview of the ULTRA water in edit mode (nothing saved)
                var hr = GameObject.Find("Dusk_Hero_Forge"); var uw = hr ? hr.GetComponent<ElderboomUltraWater>() : null;
                if (uw) { uw.ultra.SetActive(true); uw.simple.SetActive(false); }
                var tv = new GameObject("UltraPreview") { hideFlags = HideFlags.HideAndDontSave }.AddComponent<Volume>(); tv.isGlobal = true; tv.priority = 900;
                var tp = ScriptableObject.CreateInstance<VolumeProfile>(); tv.sharedProfile = tp; tp.Add<WaterRendering>(false).enable.Override(true);
                var ts = tp.Add<ScreenSpaceReflection>(false); ts.enabled.Override(true); ts.minSmoothness = .55f; ts.smoothnessFadeStart = .65f;
                continue;
            }
            if (a[0] == "grade" && prof.TryGet<SplitToning>(out var st) && prof.TryGet<WhiteBalance>(out var wb))
            {   // grade temperature splitOn(0/1) fogOn(0/1) x
                wb.temperature.Override(float.Parse(a[1], inv)); st.active = a[2] == "1"; if (prof.TryGet<Fog>(out var fg)) fg.active = a[3] == "1"; continue;
            }
            float x = float.Parse(a[1], inv), z = float.Parse(a[2], inv), lx = float.Parse(a[3], inv), lz = float.Parse(a[4], inv);
            float eh = a.Length > 5 ? float.Parse(a[5], inv) : 1.62f, lh = a.Length > 6 ? float.Parse(a[6], inv) : 1.9f, fov = a.Length > 7 ? float.Parse(a[7], inv) : 50.5f;
            if (a[0].StartsWith("top"))
            {   // top x z span: orthographic, sun raised for a readable plan
                var sl = RenderSettings.sun; var keep = sl.transform.rotation; sl.transform.rotation = Quaternion.Euler(62, 30, 0);
                cam.orthographic = true; cam.orthographicSize = lx * .5f; cam.aspect = 1; cam.nearClipPlane = 1; cam.farClipPlane = 900;
                cam.transform.SetPositionAndRotation(new Vector3(x, 300, z), Quaternion.LookRotation(Vector3.down, Vector3.forward));
                ElderboomAbilityShots.Capture(cam, "Logs/dusk/shots/_warm.png", 640, 640);
                Debug.Log("DUSK_SHOT " + a[0] + " " + ElderboomAbilityShots.Capture(cam, "Logs/dusk/shots/" + a[0] + ".png", 2400, 2400));
                sl.transform.rotation = keep; cam.orthographic = false; cam.nearClipPlane = .08f; cam.farClipPlane = 3200f; continue;
            }
            var eye = new Vector3(x, GroundY(x, z) + eh, z); var look = new Vector3(lx, GroundY(lx, lz) + lh, lz);
            cam.fieldOfView = fov; cam.aspect = 16f / 9f;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            ElderboomAbilityShots.Capture(cam, "Logs/dusk/shots/_warm.png", 640, 360);
            ElderboomAbilityShots.Capture(cam, "Logs/dusk/shots/_warm.png", 640, 360);
            string r = ElderboomAbilityShots.Capture(cam, "Logs/dusk/shots/" + a[0] + ".png", 1600, 900);
            Debug.Log("DUSK_SHOT " + a[0] + " eye " + eye + " " + r);
        }
        Object.DestroyImmediate(go);
    }

    static string PathOf(Transform t) { var n = t.name; while (t.parent) { t = t.parent; n = t.name + "/" + n; } return n; }
    public static void ProbeBatch()
    {
        Open(); var sb = new StringBuilder();
        var pl = Object.FindAnyObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
        sb.AppendLine("PLAYER pos " + pl.transform.position + " rot " + pl.transform.eulerAngles);
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include)) sb.AppendLine("GATE " + PathOf(g.transform) + " pos " + g.transform.position.ToString("F1") + " rot " + g.transform.eulerAngles.y.ToString("F0") + " active " + g.gameObject.activeInHierarchy);
        foreach (var g in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Include)) sb.AppendLine("SPAWN " + PathOf(g.transform) + " pos " + g.transform.position.ToString("F1") + " active " + g.gameObject.activeInHierarchy);
        foreach (var g in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Include)) sb.AppendLine("STATION " + g.name + " pos " + g.transform.position.ToString("F1"));
        sb.AppendLine("== ZONE x45..100 z-60..-15");
        var seen = new Dictionary<string, (int n, Bounds b)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r is ParticleSystemRenderer || r.gameObject.layer == 30) continue;
            var c = r.bounds.center; if (c.x < 45 || c.x > 100 || c.z < -60 || c.z > -15 || r.bounds.size.x > 120) continue;
            var t = r.transform; var chain = new List<Transform>(); while (t) { chain.Insert(0, t); t = t.parent; }
            string key = string.Join("/", chain.Take(Mathf.Min(3, chain.Count)).Select(x => x.name));
            if (seen.TryGetValue(key, out var e)) { e.b.Encapsulate(r.bounds); seen[key] = (e.n + 1, e.b); } else seen[key] = (1, r.bounds);
        }
        foreach (var kv in seen.OrderBy(k => k.Key)) sb.AppendLine("  " + kv.Key + " n=" + kv.Value.n + " c=" + kv.Value.b.center.ToString("F1") + " s=" + kv.Value.b.size.ToString("F1"));
        sb.AppendLine("== COLLIDERS in zone (non-terrain)");
        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
        { if (c is TerrainCollider) continue; var p = c.bounds.center; if (p.x < 45 || p.x > 100 || p.z < -60 || p.z > -15 || c.bounds.size.x > 60) continue; sb.AppendLine("  " + PathOf(c.transform) + " " + c.GetType().Name + " c=" + p.ToString("F1") + " s=" + c.bounds.size.ToString("F1") + (c.isTrigger ? " trigger" : "")); }
        sb.AppendLine("== PROPS (unique names, size < 7 m)");
        var props = new Dictionary<string, (int n, string path, Vector3 size, string mesh)>();
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude))
        {
            if (!mf.sharedMesh || mf.gameObject.layer == 30 || mf.gameObject.layer == 19) continue; var r = mf.GetComponent<MeshRenderer>(); if (!r) continue;
            if (r.bounds.size.magnitude > 7) continue;
            string key = System.Text.RegularExpressions.Regex.Replace(mf.sharedMesh.name, @"[\s_\-\.]*\(?\d+\)?$", "");
            if (props.TryGetValue(key, out var e)) props[key] = (e.n + 1, e.path, e.size, e.mesh); else props[key] = (1, PathOf(mf.transform), r.bounds.size, AssetDatabase.GetAssetPath(mf.sharedMesh));
        }
        foreach (var kv in props.OrderBy(k => k.Key)) sb.AppendLine("  " + kv.Key + " x" + kv.Value.n + " " + kv.Value.size.ToString("F1") + " | " + kv.Value.path + " | " + kv.Value.mesh);
        sb.AppendLine("== VALLEY terrain layer coverage");
        var td = Valley().terrainData; var am = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight);
        for (int l = 0; l < td.alphamapLayers; l++) { double sum = 0; for (int y = 0; y < td.alphamapHeight; y++) for (int x = 0; x < td.alphamapWidth; x++) sum += am[y, x, l]; sb.AppendLine("  layer " + l + " " + td.terrainLayers[l].name + " " + (sum / (td.alphamapWidth * td.alphamapHeight)).ToString("F3")); }
        sb.AppendLine("== creek profile (x: min height z, bank heights)");
        for (float x = 40; x <= 100; x += 5) { float best = 99, bz = 0; for (float z = -75; z <= -38; z += .5f) { float h = GroundY(x, z); if (h < best) { best = h; bz = z; } } sb.AppendLine("  x " + x + " bed " + best.ToString("F2") + " at z " + bz + "  h(-38) " + GroundY(x, -38).ToString("F2") + " h(-31) " + GroundY(x, -31).ToString("F2")); }
        File.WriteAllText("Logs/dusk/probe2.txt", sb.ToString()); Debug.Log("DUSK_DONE probe2");
    }
}
