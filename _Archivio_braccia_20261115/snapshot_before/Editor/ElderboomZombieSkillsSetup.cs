using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Logs/claude_skills.txt -> "setup": Mixamo skill clips (Assets/GameAssets/Zombies/Skills/Anims/ZB_*.fbx) imported as
// Humanoid in-place clips and added as states to both zombie controllers; the chef's cleaver built (mesh, PBR materials,
// prefab) and given to the inn cook. "shots [state] [t]": pictures of the cook with the cleaver in a pose.
static class ElderboomZombieSkillsSetup
{
    const string Flag = "Logs/claude_skills.txt", Result = "Logs/claude_skills_result.txt";
    const string Anims = "Assets/GameAssets/Zombies/Skills/Anims/", Cleaver = "Assets/GameAssets/Weapons/Cleaver/";
    const string CookPrefab = "Assets/Resources/Elderboom/Zombies/07_inn_cook.prefab";
    static readonly (string file, string state, bool loop, bool keepY)[] Clips =
    {
        ("ZB_StandToCover", "AmbushCover", false, false), ("ZB_CrouchIdle", "AmbushIdle", true, false), ("ZB_CrouchToStand", "AmbushRise", false, false),
        ("ZB_JumpAttack", "AmbushPounce", false, true), ("ZB_ZombieRun", "Sprint", true, false), ("ZB_GrabLunge", "GrabLunge", false, false),
        ("ZB_NeckBite", "GrabHold", true, false), ("ZB_CleaverDown", "CleaverDown", false, false), ("ZB_CleaverSide", "CleaverSide", false, false),
    };
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static double next;
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        try { File.WriteAllText(Result, mode.StartsWith("setup") ? Setup() : mode.StartsWith("shots") ? Shots(mode) : "FAIL unknown"); }
        catch (System.Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static string Setup()
    {
        var sb = new StringBuilder();
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var c in Clips)
        {
            string path = Anims + c.file + ".fbx"; var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            if (mi == null) { sb.AppendLine("MISSING " + path); continue; }
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true; mi.importBlendShapes = false; mi.materialImportMode = ModelImporterMaterialImportMode.None;
            var take = mi.defaultClipAnimations.FirstOrDefault(); if (take == null) { sb.AppendLine("NO TAKE " + path); continue; }
            take.name = c.state; take.loopTime = c.loop; take.loopPose = c.loop;
            take.lockRootRotation = true; take.keepOriginalOrientation = true;
            take.lockRootPositionXZ = true; take.keepOriginalPositionXZ = false;
            take.lockRootHeightY = true; take.keepOriginalPositionY = c.keepY; take.heightFromFeet = !c.keepY;
            mi.clipAnimations = new[] { take }; mi.SaveAndReimport();
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(a => a.name == c.state);
            if (clip == null) { sb.AppendLine("NO CLIP " + path); continue; }
            clips[c.state] = clip; sb.AppendLine($"clip {c.state} {clip.length:F2}s loop {c.loop} human {clip.humanMotion}");
        }
        foreach (var ctrlPath in new[] { "Assets/Animations/Port_Zombie.controller", "Assets/Animations/Port_ZombieFemale.controller" })
        {
            var ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ctrlPath); var sm = ac.layers[0].stateMachine; int added = 0;
            foreach (var kv in clips)
            {
                var st = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == kv.Key);
                if (st == null) { st = sm.AddState(kv.Key, new Vector3(650, 40 + 50 * added, 0)); added++; }
                st.motion = kv.Value; st.writeDefaultValues = true;
            }
            EditorUtility.SetDirty(ac); sb.AppendLine(ctrlPath + " states +" + added);
        }
        AssetDatabase.SaveAssets();
        sb.AppendLine(BuildCleaver());
        // The cook carries it.
        var cook = PrefabUtility.LoadPrefabContents(CookPrefab);
        var w = cook.GetComponent<ElderboomZombieWeapon>() ?? cook.AddComponent<ElderboomZombieWeapon>();
        w.weaponPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Cleaver + "Cleaver.prefab");
        PrefabUtility.SaveAsPrefabAsset(cook, CookPrefab); PrefabUtility.UnloadPrefabContents(cook);
        sb.AppendLine("cook armed");
        return sb.ToString();
    }

    // ---- Chef's cleaver: blade with a bevelled edge and rounded nose, riveted wooden handle ----------------
    static string BuildCleaver()
    {
        const float L = .205f, H = .098f, R = .02f; const int NX = 44, NY = 18;
        var V = new List<Vector3>(); var UV = new List<Vector2>(); var steel = new List<int>(); var wood = new List<int>();
        float Edge(float x) { float e = -H * (1f - .05f * x / L * x / L); if (x > L - R) e = -H + R - Mathf.Sqrt(Mathf.Max(0, R * R - (x - (L - R)) * (x - (L - R)))); return e; }
        float Half(float x, float f) { float t = Mathf.Lerp(.0024f, .0017f, x / L); return t * (1f - Mathf.SmoothStep(.6f, 1f, f)) + .00015f; }
        int Add(Vector3 p, Vector2 uv) { V.Add(p); UV.Add(uv); return V.Count - 1; }
        void Tri(List<int> l, int a, int b, int c, Vector3 outward)
        { if (Vector3.Dot(Vector3.Cross(V[b] - V[a], V[c] - V[a]), outward) < 0) { int t = b; b = c; c = t; } l.Add(a); l.Add(b); l.Add(c); }
        void Quad(List<int> l, int a, int b, int c, int d, Vector3 outward) { Tri(l, a, b, c, outward); Tri(l, a, c, d, outward); }
        var F = new int[NX + 1, NY + 1]; var B = new int[NX + 1, NY + 1];
        for (int i = 0; i <= NX; i++)
            for (int j = 0; j <= NY; j++)
            {
                float x = L * i / NX, f = (float)j / NY, y = Mathf.Lerp(0, Edge(x), f), h = Half(x, f);
                var uv = new Vector2(x / L, (y + H) / H);
                F[i, j] = Add(new Vector3(x, y, h), uv); B[i, j] = Add(new Vector3(x, y, -h), uv);
            }
        for (int i = 0; i < NX; i++)
            for (int j = 0; j < NY; j++)
            { Quad(steel, F[i, j], F[i + 1, j], F[i + 1, j + 1], F[i, j + 1], Vector3.forward); Quad(steel, B[i, j], B[i + 1, j], B[i + 1, j + 1], B[i, j + 1], Vector3.back); }
        // Spine, cutting edge, nose and heel strips (own vertices so the hard edges shade crisply).
        void Strip(System.Func<int, (int, int)> at, int count, Vector3 outward)
        {
            for (int k = 0; k < count; k++)
            {
                var (i0, j0) = at(k); var (i1, j1) = at(k + 1);
                int a = Add(V[F[i0, j0]], UV[F[i0, j0]]), b = Add(V[F[i1, j1]], UV[F[i1, j1]]), c = Add(V[B[i1, j1]], UV[B[i1, j1]]), d = Add(V[B[i0, j0]], UV[B[i0, j0]]);
                Quad(steel, a, b, c, d, outward);
            }
        }
        Strip(k => (k, 0), NX, Vector3.up); Strip(k => (k, NY), NX, Vector3.down); Strip(k => (NX, k), NY, Vector3.right); Strip(k => (0, k), NY, Vector3.left);
        // Handle: rounded-rectangle section lofted toward the butt, slight swell and downward flare.
        const int RINGS = 16, SEG = 22; const float HL = .122f, CY = -.017f;
        var ring = new int[RINGS + 1, SEG + 1];
        for (int r = 0; r <= RINGS; r++)
        {
            float t = (float)r / RINGS, x = -.004f - HL * t, a = .0135f + .0025f * Mathf.Sin(Mathf.PI * t) + (t > .9f ? .0015f * (t - .9f) / .1f : 0), b = .0092f + .0008f * Mathf.Sin(Mathf.PI * t), cy = CY - .006f * t * t * t;
            for (int s = 0; s <= SEG; s++)
            {
                float ang = 2 * Mathf.PI * s / SEG, c = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                float yy = Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), 2f / 2.8f) * a, zz = Mathf.Sign(sn) * Mathf.Pow(Mathf.Abs(sn), 2f / 2.8f) * b;
                ring[r, s] = Add(new Vector3(x, cy + yy, zz), new Vector2((float)s / SEG, t * .9f));
            }
        }
        for (int r = 0; r < RINGS; r++)
            for (int s = 0; s < SEG; s++)
            {
                var mid = (V[ring[r, s]] + V[ring[r + 1, s + 1]]) * .5f; var outward = mid - new Vector3(mid.x, CY, 0);
                Quad(wood, ring[r, s], ring[r + 1, s], ring[r + 1, s + 1], ring[r, s + 1], outward);
            }
        { var cen = Vector3.zero; for (int s = 0; s < SEG; s++) cen += V[ring[RINGS, s]]; cen /= SEG; int ci = Add(cen, new Vector2(.5f, .95f)); for (int s = 0; s < SEG; s++) Tri(wood, ci, ring[RINGS, s], ring[RINGS, s + 1], Vector3.left); }
        // Three brass-less steel rivets through the scales, a low dome on each side.
        foreach (float rx in new[] { -.028f, -.064f, -.1f })
            foreach (float side in new[] { 1f, -1f })
            {
                float t = (-.004f - rx) / HL, zb = (.0092f + .0008f * Mathf.Sin(Mathf.PI * t)) * side, cy = CY - .006f * t * t * t;
                var o = new Vector3(rx, cy, zb); int top = Add(o + new Vector3(0, 0, .0011f * side), new Vector2(.6f, .72f)); var rim = new List<int>();
                for (int s = 0; s <= 10; s++) { float ang = 2 * Mathf.PI * s / 10; rim.Add(Add(o + new Vector3(Mathf.Cos(ang) * .0043f, Mathf.Sin(ang) * .0043f, -.0003f * side), new Vector2(.6f, .7f))); }
                for (int s = 0; s < 10; s++) Tri(steel, top, rim[s], rim[s + 1], new Vector3(0, 0, side));
            }
        // Origin at the grip (middle of the handle).
        var grip = new Vector3(-.064f, CY - .001f, 0); for (int i = 0; i < V.Count; i++) V[i] -= grip;
        var mesh = new Mesh { name = "Cleaver" }; mesh.SetVertices(V); mesh.SetUVs(0, UV); mesh.subMeshCount = 2;
        mesh.SetTriangles(steel, 0); mesh.SetTriangles(wood, 1); mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        Directory.CreateDirectory(Cleaver);
        var old = AssetDatabase.LoadAssetAtPath<Mesh>(Cleaver + "Cleaver_Mesh.asset");
        if (old != null) { old.Clear(); EditorUtility.CopySerialized(mesh, old); mesh = old; } else AssetDatabase.CreateAsset(mesh, Cleaver + "Cleaver_Mesh.asset");
        var steelMat = Mat("Cleaver_Steel", "Cleaver_Steel_BaseColor.png", "Cleaver_Steel_Mask.png", 1f);
        var woodMat = Mat("Cleaver_Wood", "Cleaver_Wood_BaseColor.png", "Cleaver_Wood_Mask.png", 0f);
        var go = new GameObject("Cleaver"); go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { steelMat, woodMat };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        PrefabUtility.SaveAsPrefabAsset(go, Cleaver + "Cleaver.prefab"); Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        return $"cleaver {V.Count} verts, {(steel.Count + wood.Count) / 3} tris, size {mesh.bounds.size.x:F3}x{mesh.bounds.size.y:F3}x{mesh.bounds.size.z:F3} m";
    }

    static Material Mat(string name, string baseMap, string maskMap, float metallic)
    {
        foreach (var t in new[] { baseMap, maskMap })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(Cleaver + t); if (ti == null) continue;
            ti.sRGBTexture = t == baseMap; ti.maxTextureSize = 1024; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.SaveAndReimport();
        }
        string path = Cleaver + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Cleaver + baseMap)); m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Cleaver + maskMap)); m.EnableKeyword("_MASKMAP");
        m.SetFloat("_MetallicRemapMin", 0); m.SetFloat("_MetallicRemapMax", metallic > 0 ? 1 : 0);
        m.SetFloat("_SmoothnessRemapMin", 0); m.SetFloat("_SmoothnessRemapMax", 1); m.SetFloat("_AORemapMin", 0); m.SetFloat("_AORemapMax", 1);
        HDMaterial.ValidateMaterial(m); EditorUtility.SetDirty(m); return m;
    }

    // ---- Pictures: the cook posed with the cleaver ------------------------------------------------------
    static string Shots(string mode)
    {
        var parts = mode.Split(' '); var sb = new StringBuilder(); Directory.CreateDirectory("Logs/Skills");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CookPrefab);
        var states = parts.Length > 1 ? new[] { parts[1] } : new[] { "CleaverDown", "CleaverSide", "WalkA", "AmbushIdle", "GrabHold" };
        var light = new GameObject("SkillShotSun"); var l = light.AddComponent<Light>(); l.type = LightType.Directional; light.AddComponent<HDAdditionalLightData>(); l.intensity = 9f; light.transform.rotation = Quaternion.Euler(30, 200, 0);
        var basePos = new Vector3(0, 300, 0);
        foreach (var st in states)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab); go.transform.position = basePos;
            var w = go.GetComponent<ElderboomZombieWeapon>(); typeof(ElderboomZombieWeapon).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.Invoke(w, null);
            var anim = go.GetComponentInChildren<Animator>();
            var clip = anim.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name == st);
            foreach (float f in parts.Length > 2 ? new[] { float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) } : new[] { .15f, .43f, .7f })
            {
                if (clip != null) { AnimationMode.StartAnimationMode(); AnimationMode.SampleAnimationClip(anim.gameObject, clip, clip.length * f); }
                foreach (var view in new[] { (Vector3.forward, "front"), (Vector3.right, "side") })
                {
                    var cg = new GameObject("SkillShotCam"); var cam = cg.AddComponent<Camera>(); cg.AddComponent<HDAdditionalCameraData>(); cam.fieldOfView = 32; cam.nearClipPlane = .05f;
                    var target = basePos + Vector3.up * 1.15f; cg.transform.position = target + view.Item1 * 3.6f; cg.transform.LookAt(target);
                    var rt = new RenderTexture(900, 900, 24); cam.targetTexture = rt; for (int k = 0; k < 3; k++) cam.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(900, 900, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, 900, 900), 0, 0); tex.Apply();
                    string file = $"Logs/Skills/{st}_{f:F2}_{view.Item2}.jpg"; File.WriteAllBytes(file, tex.EncodeToJPG(88)); sb.AppendLine(file);
                    RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(cg); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                }
                if (clip != null) AnimationMode.StopAnimationMode();
            }
            var held = w != null ? w.Held : null; if (held) Object.DestroyImmediate(held.gameObject);
            Object.DestroyImmediate(go);
        }
        Object.DestroyImmediate(light);
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        return sb.ToString();
    }
}
