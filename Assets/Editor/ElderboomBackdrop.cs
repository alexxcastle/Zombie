using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Logs/claude_backdrop.txt: "inspect" (sky, fog, cameras, map edge + horizon pictures), "build" (the world beyond the
// map: billboard forest rows, mountain rings, painted panorama if present, distant villages, smoke, pagoda, poles,
// birds, valley haze). Result in Logs/claude_backdrop_result.txt, pictures in Logs/Backdrop.
static partial class ElderboomBackdrop
{
    const string Flag = "Logs/claude_backdrop.txt", Result = "Logs/claude_backdrop_result.txt", Out = "Logs/Backdrop";
    const string Scene = "Assets/Scenes/Elderboom_Unity_Port.unity";

    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, Run(mode)); } catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static string Run(string mode)
    {
        if (EditorSceneManager.GetActiveScene().path != Scene) EditorSceneManager.OpenScene(Scene);
        Directory.CreateDirectory(Out);
        if (mode.StartsWith("inspect")) return Inspect();
        if (mode.StartsWith("build")) return Build(mode);
        if (mode.StartsWith("sky")) return Sky();
        if (mode.StartsWith("shots")) { Horizon("now"); return "DONE"; }
        return "FAIL unknown mode " + mode;
    }

    static string Inspect()
    {
        var sb = new StringBuilder();
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)) sb.AppendLine("terrain " + t.name + " pos " + t.transform.position + " size " + t.terrainData.size + " trees " + t.terrainData.treeInstanceCount + " prototypes " + string.Join(",", t.terrainData.treePrototypes.Select(p => p.prefab ? p.prefab.name : "?")));
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
        {
            var p = v.sharedProfile; if (!p) continue;
            sb.AppendLine("volume " + v.name + " global " + v.isGlobal + " prio " + v.priority + " profile " + AssetDatabase.GetAssetPath(p));
            foreach (var c in p.components) sb.AppendLine("  " + c.GetType().Name + " active " + c.active);
            if (p.TryGet<Fog>(out var fog)) sb.AppendLine("  fog enabled " + fog.enabled.value + " meanFreePath " + fog.meanFreePath.value + " baseHeight " + fog.baseHeight.value + " maxHeight " + fog.maximumHeight.value + " maxDist " + fog.maxFogDistance.value + " colorMode " + fog.colorMode.value + " tint " + fog.tint.value + " volumetric " + fog.enableVolumetricFog.value);
            if (p.TryGet<VisualEnvironment>(out var ve)) sb.AppendLine("  sky " + ve.skyType.value);
            if (p.TryGet<HDRISky>(out var hd)) sb.AppendLine("  hdri " + (hd.hdriSky.value ? hd.hdriSky.value.name : "-") + " exposure " + hd.exposure.value + " rotation " + hd.rotation.value);
            if (p.TryGet<Exposure>(out var ex)) sb.AppendLine("  exposure mode " + ex.mode.value + " fixed " + ex.fixedExposure.value);
        }
        foreach (var name in new[] { "SM_SkySphere", "Elderboom_Valley", "Brush_Border", "Borders", "Elderboom_Blocks" })
        {
            var go = GameObject.Find(name); if (!go) { sb.AppendLine(name + " missing"); continue; }
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { sb.AppendLine(name + " no renderers"); continue; }
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            sb.AppendLine(name + " bounds " + b.center.ToString("F0") + " size " + b.size.ToString("F0") + " renderers " + rs.Length + " mat " + (rs[0].sharedMaterial ? rs[0].sharedMaterial.shader.name : "-"));
        }
        var player = Resources.Load<GameObject>("Elderboom/Player") ;
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)) sb.AppendLine("camera " + cam.name + " far " + cam.farClipPlane);
        foreach (var g in AssetDatabase.FindAssets("t:Prefab Player")) { var path = AssetDatabase.GUIDToAssetPath(g); var pr = AssetDatabase.LoadAssetAtPath<GameObject>(path); var c = pr ? pr.GetComponentInChildren<Camera>(true) : null; if (c) sb.AppendLine("prefab camera " + path + " far " + c.farClipPlane); }
        sb.AppendLine("RenderSettings fog " + RenderSettings.fog + " sky " + (RenderSettings.skybox ? RenderSettings.skybox.name : "-"));
        Horizon("before");
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    // From eye height near the edges of the playable valley, looking out; and from above the village.
    static void Horizon(string tag)
    {
        var t = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(x => x.terrainData.size.x).First();
        float G(Vector3 p) => t.SampleHeight(p) + t.transform.position.y;
        var centre = new Vector3(25, 0, -20);
        int i = 0;
        foreach (var yaw in new[] { 0f, 72f, 144f, 216f, 288f })
        {
            var dir = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var eye = centre + dir * 60; eye.y = G(eye) + 1.7f;
            Shot(tag + "_eye_" + i, eye, eye + dir * 100 + Vector3.up * 9);
            var high = centre + Vector3.up * (G(centre) + 32);
            Shot(tag + "_high_" + i, high, high + dir * 100 + Vector3.up * 2);
            i++;
        }
    }

    static void Shot(string name, Vector3 eye, Vector3 target, float fov = 70)
    {
        var go = new GameObject("BackdropCam"); var cam = go.AddComponent<Camera>(); go.AddComponent<HDAdditionalCameraData>();
        cam.fieldOfView = fov; cam.nearClipPlane = .2f; cam.farClipPlane = 4000; go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        var rt = new RenderTexture(1280, 720, 24); cam.targetTexture = rt; for (int k = 0; k < 8; k++) cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply();
        File.WriteAllBytes(Out + "/" + name + ".jpg", tex.EncodeToJPG(88));
        RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(go);
    }

    const string Dir = "Assets/GameAssets/Backdrop";
    static readonly Vector3 C = new Vector3(25, 0, -20);
    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static Terrain terrainMain;
    static float Ground(Vector3 p)
    {
        var t = terrainMain; var o = t.transform.position; var sz = t.terrainData.size;
        if (p.x < o.x || p.z < o.z || p.x > o.x + sz.x || p.z > o.z + sz.z) return -8f;
        return t.SampleHeight(p) + o.y;
    }

    static Material Mat(string name, System.Action<Material> setup)
    {
        string path = Dir + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
        setup(m); HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }
    static Material Solid(string name, Color c) => Mat(name, m => { m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", .05f); m.SetFloat("_Metallic", 0); m.SetFloat("_DoubleSidedEnable", 1); });
    static Material Glow(string name, Color c, float intensity) => Mat(name, m =>
    {
        m.SetColor("_BaseColor", Color.black); m.SetFloat("_Smoothness", 0); m.SetFloat("_DoubleSidedEnable", 1);
        m.SetColor("_EmissiveColor", c * intensity); m.SetColor("_EmissiveColorLDR", c); m.SetFloat("_EmissiveIntensity", intensity);
        m.SetFloat("_EmissiveExposureWeight", 0f); m.EnableKeyword("_EMISSIVE_COLOR_MAP"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
    });

    static GameObject Part(Transform parent, string name, Mesh mesh, Material mat)
    {
        Directory.CreateDirectory(Dir + "/Meshes");
        string path = Dir + "/Meshes/" + name + ".asset";
        AssetDatabase.DeleteAsset(path); mesh.name = name; AssetDatabase.CreateAsset(mesh, path);
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.layer = ElderboomBackdropRuntime.Layer;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    // A ring "curtain" around the map: bottom well below the ground, top following the profile; faces the centre.
    static Mesh RingMesh(float radius, float[] top, float bottom, float jitter)
    {
        int n = top.Length; var v = new List<Vector3>(); var nr = new List<Vector3>(); var tri = new List<int>();
        for (int i = 0; i <= n; i++)
        {
            float a = i % n * Mathf.PI * 2 / n; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            float r = radius + Mathf.Sin(a * 7 + radius) * jitter;
            var p = C + dir * r; float g = Ground(p);
            v.Add(new Vector3(p.x, Mathf.Min(g, 0) + bottom, p.z)); v.Add(new Vector3(p.x, g + top[i % n], p.z));
            var nn = (-dir + Vector3.up * .35f).normalized; nr.Add(nn); nr.Add(nn);
            if (i > 0) { int b = (i - 1) * 2; tri.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 }); }
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetNormals(nr); m.SetTriangles(tri, 0); m.RecalculateBounds(); return m;
    }

    static float[] Profile(int n, float baseH, float hillAmp, int peaks, float peakMin, float peakMax, float widthMinDeg, float widthMaxDeg, float treeSpikes)
    {
        var h = new float[n];
        float o1 = R(0, 10), o2 = R(0, 10);
        for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; h[i] = baseH + hillAmp * (.6f * Mathf.PerlinNoise(a * 3 + o1, o2) + .4f * Mathf.PerlinNoise(a * 11 + o2, o1)); }
        for (int k = 0; k < peaks; k++)
        {
            float c = R(0, n), w = R(widthMinDeg, widthMaxDeg) / 360f * n, H = R(peakMin, peakMax), lean = R(-.25f, .25f);
            for (int i = (int)(c - w * 1.6f); i <= (int)(c + w * 1.6f); i++)
            {
                int j = ((i % n) + n) % n; float d = (i - c) / w - lean * (i - c) / w * .3f;
                // Karst sugarloaf: steep sides, rounded top.
                float s = Mathf.Max(0, 1 - Mathf.Pow(Mathf.Abs(d), 2.2f)); float y = H * Mathf.Pow(s, .85f);
                if (y > h[j]) h[j] = y;
            }
        }
        // Fractal roughness on the whole silhouette (rock ledges, saddles), proportional to height.
        float o3 = R(0, 50);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2 / n, r = 0, amp = .5f, f = 24;
            for (int k = 0; k < 5; k++) { r += (Mathf.PerlinNoise(a * f + o3, k * 7.3f) - .5f) * amp; amp *= .5f; f *= 2.3f; }
            h[i] = Mathf.Max(2, h[i] * (1 + r * .35f) + r * 6);
        }
        if (treeSpikes > 0)
            for (int i = 0; i < n; )
            {
                int w = (int)R(2, 6); float t = R(.4f, 1f) * treeSpikes;
                for (int k = 0; k < w && i + k < n; k++) h[i + k] += t * (1 - Mathf.Abs(k - (w - 1) * .5f) / (w * .5f + .01f));
                i += w;
            }
        return h;
    }

    static Mesh Quads(List<(Vector3 c, Vector3 right, Vector3 up)> qs)
    {
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        foreach (var q in qs)
        {
            int b = v.Count;
            v.Add(q.c - q.right - q.up); v.Add(q.c + q.right - q.up); v.Add(q.c + q.right + q.up); v.Add(q.c - q.right + q.up);
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
            tri.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    static Mesh Boxes(List<(Vector3 c, Vector3 size, float yaw, float topScale)> bs)
    {
        // Boxes whose top face can be scaled (roofs: wide bottom, narrow top).
        var v = new List<Vector3>(); var tri = new List<int>();
        foreach (var b in bs)
        {
            var rot = Quaternion.Euler(0, b.yaw, 0); var e = b.size * .5f; int s = v.Count;
            for (int y = 0; y < 2; y++) { float k = y == 0 ? 1 : b.topScale; foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) }) v.Add(b.c + rot * new Vector3(c.x * e.x * k, y == 0 ? -e.y : e.y, c.y * e.z * k)); }
            int[] f = { 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7, 4, 5, 6, 4, 6, 7, 0, 3, 2, 0, 2, 1 };
            foreach (var i in f) tri.Add(s + i);
        }
        var m = new Mesh { indexFormat = IndexFormat.UInt32 }; m.SetVertices(v); m.SetTriangles(tri, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
    }

    // The world beyond, painted into the night sky (tools/backdrop/compose_sky.py): no geometry, no cost while playing.
    // Replaces the geometry backdrop (which needed a far camera and cost ~3 fps), keeps a flock of birds over the valley.
    static string Sky()
    {
        var sb = new StringBuilder();
        AssetDatabase.ImportAsset(Dir + "/Elderboom_NightSky_Valley.png", ImportAssetOptions.ForceUpdate);
        var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(Dir + "/Elderboom_NightSky_Valley.png");
        if (!cube) return "FAIL sky texture is not a cubemap";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");
        if (!profile || !profile.TryGet<HDRISky>(out var hdri)) return "FAIL night profile / HDRI sky";
        sb.AppendLine("sky " + (hdri.hdriSky.value ? hdri.hdriSky.value.name : "-") + " -> " + cube.name + " (" + cube.width + " per face)");
        hdri.hdriSky.value = cube; EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
        var old = GameObject.Find("Elderboom_Backdrop"); if (old) Object.DestroyImmediate(old);
        var oldBirds = GameObject.Find("Elderboom_Birds"); if (oldBirds) Object.DestroyImmediate(oldBirds);
        var bm = AssetDatabase.LoadAssetAtPath<Mesh>(Dir + "/Meshes/Bird.asset");
        var birds = new GameObject("Elderboom_Birds"); birds.transform.position = C + new Vector3(-10, 0, 15);
        var b = birds.AddComponent<ElderboomBackdropBirds>(); b.birdMesh = bm; b.birdMaterial = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/Backdrop_Bird.mat");
        b.radius = 65; b.height = 58; b.speed = 8; b.count = 12;
        EditorSceneManager.MarkSceneDirty(birds.scene); EditorSceneManager.SaveScene(birds.scene);
        // The geometry layers are no longer used.
        foreach (var n in new[] { "Ring0_ForestRidge", "Ring1_Hills", "Ring2_Karst", "Ring3_FarRange", "Pagoda", "Pagoda_Lanterns", "Power_Poles", "Power_Wires" }) AssetDatabase.DeleteAsset(Dir + "/Meshes/" + n + ".asset");
        foreach (var f in Directory.GetFiles(Dir + "/Meshes", "*.asset")) if (Path.GetFileName(f).StartsWith("Village_") || Path.GetFileName(f).StartsWith("Smoke_") || Path.GetFileName(f).StartsWith("Fire_")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
        Horizon("sky");
        sb.AppendLine("DONE");
        return sb.ToString();
    }

    static string Build(string mode)
    {
        var sb = new StringBuilder(); rng = new System.Random(20261107);
        terrainMain = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None).OrderByDescending(x => x.terrainData.size.x).First();
        var old = GameObject.Find("Elderboom_Backdrop"); if (old) Object.DestroyImmediate(old);
        var root = new GameObject("Elderboom_Backdrop"); root.layer = ElderboomBackdropRuntime.Layer; root.AddComponent<ElderboomBackdropRuntime>();
        Directory.CreateDirectory(Dir);

        // Colours from the night: each ring a little lighter and bluer than the one in front (aerial perspective).
        var ring0 = Solid("Backdrop_ForestRidge", new Color(.018f, .026f, .024f));
        var ring1 = Solid("Backdrop_Hills", new Color(.03f, .036f, .045f));
        var ring2 = Solid("Backdrop_Karst", new Color(.04f, .047f, .06f));
        var ring3 = Solid("Backdrop_FarRange", new Color(.055f, .062f, .08f));
        int N = 2880;
        var p0 = Profile(N, 16, 10, 0, 0, 0, 1, 1, 7); Part(root.transform, "Ring0_ForestRidge", RingMesh(338, p0, -20, 4), ring0);
        var p1 = Profile(N, 22, 40, 34, 55, 150, 1.6f, 4.5f, 6); Part(root.transform, "Ring1_Hills", RingMesh(640, p1, -40, 18), ring1);
        var p2 = Profile(N, 40, 70, 60, 120, 340, 1.0f, 3.2f, 2.5f); Part(root.transform, "Ring2_Karst", RingMesh(1150, p2, -60, 30), ring2);
        var p3 = Profile(N, 160, 220, 40, 300, 560, 2f, 7f, 0); Part(root.transform, "Ring3_FarRange", RingMesh(1950, p3, -80, 40), ring3);
        sb.AppendLine("mountain rings: forest ridge 338 m, hills 640 m, karst 1150 m, far range 1950 m");

        // Distant villages: clusters of warm windows on the lower slopes of the hills.
        var windows = Glow("Backdrop_Windows", new Color(1f, .58f, .24f), 14f);
        int villages = 0;
        foreach (var deg in new[] { 28f, 118f, 205f, 300f, 338f })
        {
            int i = Mathf.RoundToInt(deg / 360f * N) % N; float a = deg * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            var side = Vector3.Cross(Vector3.up, dir); var qs = new List<(Vector3, Vector3, Vector3)>();
            int count = (int)R(14, 26); float spread = R(30, 70);
            for (int k = 0; k < count; k++)
            {
                float off = R(-spread, spread); var p = C + dir * (600 + R(-8, 0)) + side * off; float g = Ground(p);
                float y = Mathf.Min(g, 0) + R(6, Mathf.Max(12, p1[i] * .45f));
                qs.Add((new Vector3(p.x, y, p.z), side * R(2.2f, 3.6f), Vector3.up * R(1.6f, 2.6f)));
            }
            Part(root.transform, "Village_Lights_" + villages++, Quads(qs), windows);
        }
        sb.AppendLine("village lights: " + villages + " clusters");

        // A pagoda on the tallest hill in the north-east.
        int best = Mathf.RoundToInt(70f / 360f * N); for (int i = 0; i < N; i++) { float d = i * 360f / N; if (d > 40 && d < 100 && p1[i] > p1[best]) best = i; }
        {
            float a = best * Mathf.PI * 2 / N; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var basePos = C + dir * 612; basePos.y = Mathf.Min(Ground(basePos), 0) + p1[best] - 2;
            var bs = new List<(Vector3, Vector3, float, float)>(); float y = basePos.y, w = 12; float yaw = R(0, 90);
            bs.Add((new Vector3(basePos.x, y + 1.5f, basePos.z), new Vector3(w * 1.3f, 3, w * 1.3f), yaw, 1));
            y += 3;
            for (int t = 0; t < 5; t++)
            {
                float h = t == 0 ? 6 : 4.2f;
                bs.Add((new Vector3(basePos.x, y + h * .5f, basePos.z), new Vector3(w, h, w), yaw, 1)); y += h;
                bs.Add((new Vector3(basePos.x, y + .7f, basePos.z), new Vector3(w * 1.75f, 1.4f, w * 1.75f), yaw, .55f)); y += 1.4f;
                w *= .82f;
            }
            bs.Add((new Vector3(basePos.x, y + 4, basePos.z), new Vector3(.8f, 8, .8f), yaw, .3f));
            Part(root.transform, "Pagoda", Boxes(bs), Solid("Backdrop_Pagoda", new Color(.03f, .03f, .035f)));
            var lamps = new List<(Vector3, Vector3, Vector3)>(); var toC = (C - basePos); toC.y = 0; toC.Normalize(); var sd = Vector3.Cross(Vector3.up, toC);
            for (int t = 0; t < 3; t++) lamps.Add((basePos + Vector3.up * (5 + t * 5.6f) + toC * 7f, sd * .9f, Vector3.up * 1.1f));
            Part(root.transform, "Pagoda_Lanterns", Quads(lamps), Glow("Backdrop_Lanterns", new Color(1f, .35f, .18f), 9f));
            sb.AppendLine("pagoda at " + basePos.ToString("F0") + " (" + (best * 360f / N).ToString("F0") + " deg)");
        }

        // Smoke columns rising behind the hills, with the glow of the fires under them.
        var smokeMat = Mat("Backdrop_Smoke", m =>
        {
            m.SetFloat("_SurfaceType", 1); m.SetFloat("_BlendMode", 0); m.SetFloat("_DoubleSidedEnable", 1); m.SetFloat("_Smoothness", 0);
            m.SetColor("_BaseColor", new Color(.5f, .49f, .5f, .9f)); m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/smoke_column.png"));
            m.SetFloat("_EnableFogOnTransparent", 1); m.renderQueue = 3000;
        });
        var fire = Glow("Backdrop_FireGlow", new Color(1f, .42f, .12f), 9f);
        int smokes = 0;
        foreach (var deg in new[] { 75f, 162f, 255f })
        {
            float a = deg * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); var bp = C + dir * R(820, 980); bp.y = -10;
            var col = new GameObject("Smoke_" + smokes); col.layer = ElderboomBackdropRuntime.Layer; col.transform.SetParent(root.transform, false); col.transform.position = bp;
            var qs = new List<(Vector3, Vector3, Vector3)>();
            for (int k = 0; k < 3; k++) { var r = Quaternion.Euler(0, k * 60, 0) * Vector3.right; qs.Add((Vector3.up * 150, r * 70, Vector3.up * 160)); }
            var smoke = Part(col.transform, "Smoke_Column_" + smokes, Quads(qs), smokeMat); smoke.AddComponent<ElderboomBackdropSmoke>();
            var gl = new List<(Vector3, Vector3, Vector3)>(); var toC = (C - bp); toC.y = 0; toC.Normalize(); var sd = Vector3.Cross(Vector3.up, toC);
            gl.Add((Vector3.up * 12 + toC * 20, sd * 18, Vector3.up * 8));
            Part(col.transform, "Fire_Glow_" + smokes, Quads(gl), fire);
            smokes++;
        }
        sb.AppendLine("smoke columns: " + smokes);

        // Power line leaving the valley to the east, beyond the cemetery.
        var wood = Solid("Backdrop_PoleWood", new Color(.06f, .045f, .035f));
        var poleBoxes = new List<(Vector3, Vector3, float, float)>(); var tops = new List<Vector3>();
        for (float x = 135; x <= 335; x += 40)
        {
            var p = new Vector3(x, 0, -14 + (x - 135) * .12f); p.y = Ground(p);
            poleBoxes.Add((p + Vector3.up * 5f, new Vector3(.3f, 10f, .3f), 8, .8f));
            poleBoxes.Add((p + Vector3.up * 9.3f, new Vector3(.18f, .18f, 2.6f), 8, 1));
            tops.Add(p + Vector3.up * 9.45f);
        }
        Part(root.transform, "Power_Poles", Boxes(poleBoxes), wood);
        var wire = Solid("Backdrop_Wire", new Color(.02f, .02f, .02f));
        var wv = new List<(Vector3, Vector3, float, float)>();
        for (int i = 0; i + 1 < tops.Count; i++)
            foreach (float zoff in new[] { -1.1f, 0f, 1.1f })
            {
                var a = tops[i] + Quaternion.Euler(0, 8, 0) * new Vector3(0, 0, zoff); var b = tops[i + 1] + Quaternion.Euler(0, 8, 0) * new Vector3(0, 0, zoff);
                int segs = 10; var prev = a;
                for (int s = 1; s <= segs; s++) { float t = s / (float)segs; var q = Vector3.Lerp(a, b, t) + Vector3.down * 1.4f * 4 * t * (1 - t); var mid = (prev + q) * .5f; float len = Vector3.Distance(prev, q); var dirw = (q - prev).normalized; float yaw = Mathf.Atan2(dirw.x, dirw.z) * Mathf.Rad2Deg; wv.Add((mid, new Vector3(.05f, .05f, len), yaw, 1)); prev = q; }
            }
        Part(root.transform, "Power_Wires", Boxes(wv), wire);
        sb.AppendLine("power line: " + tops.Count + " poles");

        // Birds circling far away.
        var bm = new Mesh(); bm.SetVertices(new List<Vector3> { new(0, 0, .3f), new(-1, .25f, -.2f), new(0, 0, -.3f), new(1, .25f, -.2f) }); bm.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0); bm.RecalculateNormals();
        Directory.CreateDirectory(Dir + "/Meshes"); AssetDatabase.DeleteAsset(Dir + "/Meshes/Bird.asset"); AssetDatabase.CreateAsset(bm, Dir + "/Meshes/Bird.asset");
        foreach (var deg in new[] { 140f, 320f })
        {
            float a = deg * Mathf.Deg2Rad; var go = new GameObject("Birds_" + deg); go.layer = ElderboomBackdropRuntime.Layer; go.transform.SetParent(root.transform, false);
            go.transform.position = C + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 300;
            var birds = go.AddComponent<ElderboomBackdropBirds>(); birds.birdMesh = bm; birds.birdMaterial = Solid("Backdrop_Bird", new Color(.01f, .01f, .012f));
        }
        sb.AppendLine("bird flocks: 2");

        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ElderboomBackdropRuntime.Layer;
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene); EditorSceneManager.SaveScene(root.scene);
        Horizon("after");
        // Close looks at the features from above the village (narrow lens).
        var hp = C + Vector3.up * (Ground(C) + 32);
        foreach (var (name, deg, up) in new[] { ("village28", 28f, 30f), ("village118", 118f, 30f), ("pagoda", best * 360f / N, 120f), ("smoke162", 162f, 140f), ("power", 352f, 6f) })
        {
            float a = deg * Mathf.Deg2Rad; var target = C + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (name == "power" ? 200 : 640) + Vector3.up * up;
            Shot("feature_" + name, hp, target, 28);
        }
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
