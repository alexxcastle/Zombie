using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Logs/claude_tacarms.txt ("build" | "preview") -> Logs/claude_tacarms_result.txt
// Builds the Tactical arms (mesh data from Blender, already in Unity space and bound to the Basic FPS clip nodes),
// their materials, the prefab (clip skeleton + skinned arms + Animator) and one controller per weapon family.
static class ElderboomTacticalArmsBuild
{
    const string Flag = "Logs/claude_tacarms.txt", Result = "Logs/claude_tacarms_result.txt";
    const string Src = "Assets/GameAssets/TacticalArms", Out = "Assets/Resources/Elderboom/TacticalArms";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        var sb = new StringBuilder();
        try { if (mode == "build") { Build(sb); Preview(sb); } else if (mode == "debug") Debug(sb); else Preview(sb); }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText(Result, sb.ToString());
    }

    static Material Mat(string name, string baseTex, string maskTex)
    {
        foreach (var p in new[] { maskTex })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(p);
            if (ti != null && ti.sRGBTexture) { ti.sRGBTexture = false; ti.SaveAndReimport(); }
        }
        string path = Src + "/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(Shader.Find("HDRP/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetTexture("_BaseColorMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseTex));
        m.SetTexture("_MaskMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maskTex));
        m.SetFloat("_Metallic", 1f); m.SetFloat("_SmoothnessRemapMin", 0f); m.SetFloat("_SmoothnessRemapMax", 1f);
        m.SetFloat("_AORemapMin", 0f); m.SetFloat("_AORemapMax", 1f);
        m.EnableKeyword("_MASKMAP");
        EditorUtility.SetDirty(m);
        return m;
    }

    static Mesh ReadMesh(string side, Matrix4x4[] bind, out int subs)
    {
        var bytes = File.ReadAllBytes(Src + "/arms_" + side + ".bytes");
        var br = new BinaryReader(new MemoryStream(bytes));
        int nv = br.ReadInt32(); subs = br.ReadInt32(); int nb = br.ReadInt32();
        var P = new Vector3[nv]; var N = new Vector3[nv]; var UV = new Vector2[nv];
        for (int i = 0; i < nv; i++) P[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        for (int i = 0; i < nv; i++) N[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
        for (int i = 0; i < nv; i++) UV[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
        var bi = new int[nv * 4]; var bw = new float[nv * 4];
        for (int i = 0; i < nv * 4; i++) bi[i] = br.ReadInt32();
        for (int i = 0; i < nv * 4; i++) bw[i] = br.ReadSingle();
        var mesh = new Mesh { name = "TacticalArms_" + side, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = P; mesh.normals = N; mesh.uv = UV;
        var w = new BoneWeight[nv];
        for (int i = 0; i < nv; i++)
            w[i] = new BoneWeight { boneIndex0 = bi[i * 4], weight0 = bw[i * 4], boneIndex1 = bi[i * 4 + 1], weight1 = bw[i * 4 + 1], boneIndex2 = bi[i * 4 + 2], weight2 = bw[i * 4 + 2], boneIndex3 = bi[i * 4 + 3], weight3 = bw[i * 4 + 3] };
        mesh.boneWeights = w; mesh.bindposes = bind;
        mesh.subMeshCount = subs;
        for (int s = 0; s < subs; s++)
        {
            int n = br.ReadInt32(); var tri = new int[n * 3];
            for (int i = 0; i < n * 3; i++) tri[i] = br.ReadInt32();
            mesh.SetTriangles(tri, s);
        }
        mesh.RecalculateBounds(); mesh.RecalculateTangents();
        return mesh;
    }

    static void Build(StringBuilder sb)
    {
        Directory.CreateDirectory(Out);
        AssetDatabase.Refresh();
        // bones + bindposes (model space of the clip skeleton)
        var names = new List<string>(); var bind = new List<Matrix4x4>();
        foreach (var line in File.ReadAllLines(Src + "/arms_bones.txt"))
        {
            var p = line.Split('|'); if (p.Length < 17) continue;
            names.Add(p[0]); var m = new Matrix4x4();
            for (int i = 0; i < 16; i++) m[i / 4, i % 4] = float.Parse(p[1 + i], System.Globalization.CultureInfo.InvariantCulture);
            bind.Add(m);
        }
        var mats = new Dictionary<string, Material[]>
        {
            {"L", new[]{ Mat("TacticalLeft", Src+"/tex/TacticalLeft_BaseColor.jpg", Src+"/tex/TacticalLeft_Mask.png"), Mat("TacticalLeftSleeve", Src+"/tex/TacticalLeftSleeve_BaseColor.jpg", Src+"/tex/TacticalLeftSleeve_Mask.png") }},
            {"R", new[]{ Mat("TacticalRight", Src+"/tex/TacticalRight_BaseColor.jpg", Src+"/tex/TacticalRight_Mask.png"), Mat("TacticalRightSleeve", Src+"/tex/TacticalRightSleeve_BaseColor.jpg", Src+"/tex/TacticalRightSleeve_Mask.png") }},
        };
        // skeleton: the clip FBX hierarchy (root/DEF-..., Weapon/Magazine/MagazineSpare)
        var template = AssetDatabase.LoadAssetAtPath<GameObject>(ElderboomWeaponMounts.ClipRoot + "/AR10/AR10_Idle.fbx");
        var go = (GameObject)PrefabUtility.InstantiatePrefab(template);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.name = "TacticalArms"; go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); go.transform.localScale = Vector3.one;
        foreach (var a in go.GetComponents<Animator>()) Object.DestroyImmediate(a);
        var bones = names.Select(n => ElderboomWeaponMounts.Deep(go.transform, n)).ToArray();
        var missing = names.Where((n, i) => bones[i] == null).ToList();
        if (missing.Count > 0) sb.AppendLine("missing bones: " + string.Join(",", missing));
        foreach (var side in new[] { "L", "R" })
        {
            var mesh = ReadMesh(side, bind.ToArray(), out int subs);
            string mp = Src + "/TacticalArms_" + side + ".asset";
            AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var child = new GameObject("Arms_" + side); child.transform.SetParent(go.transform, false);
            var smr = child.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh; smr.bones = bones; smr.rootBone = ElderboomWeaponMounts.Deep(go.transform, "root");
            smr.sharedMaterials = mats[side].Take(subs).ToArray();
            smr.updateWhenOffscreen = true; smr.quality = SkinQuality.Bone4;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sb.AppendLine($"Arms_{side}: {mesh.vertexCount} verts, {subs} submeshes");
        }
        var anim = go.AddComponent<Animator>(); anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        PrefabUtility.SaveAsPrefabAsset(go, Out + "/TacticalArms.prefab");
        Object.DestroyImmediate(go);
        // one controller per family, states named after the clip role
        foreach (var dir in Directory.GetDirectories(ElderboomWeaponMounts.ClipRoot))
        {
            string fam = Path.GetFileName(dir);
            string cp = Out + "/" + fam + ".controller";
            AssetDatabase.DeleteAsset(cp);
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(cp);
            var sm = ctrl.layers[0].stateMachine;
            var roles = new List<string>(); var lens = new List<float>();
            foreach (var f in Directory.GetFiles(dir, "*.fbx").OrderBy(x => x))
            {
                string role = Path.GetFileNameWithoutExtension(f).Substring(fam.Length + 1);
                var clip = AssetDatabase.LoadAllAssetsAtPath(f.Replace('\\', '/')).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
                if (clip == null) continue;
                var st = sm.AddState(role); st.motion = clip; st.writeDefaultValues = false;
                if (role == "Idle") sm.defaultState = st;
                roles.Add(role); lens.Add(clip.length);
            }
            File.WriteAllText(Out + "/" + fam + "_roles.json", "{\"roles\":[" + string.Join(",", roles.Select(r => "\"" + r + "\"")) + "],\"lengths\":[" + string.Join(",", lens.Select(l => l.ToString("F4", System.Globalization.CultureInfo.InvariantCulture))) + "]}");
            sb.AppendLine(fam + ": " + roles.Count + " states");
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
    }

    static void Debug(StringBuilder sb)
    {
        var def = ElderboomWeapons.Find("m4");
        var holder = new GameObject("__dbg");
        var root = ElderboomWeaponOnly.Build(def, holder.transform);
        var view = root.GetComponent<ElderboomViewmodel>();
        var arms = view.model.transform;
        System.Action<string> dump = tag => {
            var r = ElderboomWeaponMounts.Deep(arms, "root"); var h = ElderboomWeaponMounts.Deep(arms, "DEF-hand.R"); var w = arms.Find("Weapon");
            sb.AppendLine(tag + ": root lossy " + r.lossyScale.ToString("F3") + " pos " + r.position.ToString("F3") + " handR " + h.position.ToString("F3") + " weapon " + w.position.ToString("F3") + " lossy " + w.lossyScale.ToString("F3"));
        };
        dump("after build");
        var ctrl = view.arms.animator.runtimeAnimatorController;
        sb.AppendLine("clips: " + string.Join(",", ctrl.animationClips.Select(c => c.name)));
        var idle = ctrl.animationClips.First(c => c.name.EndsWith("_Idle"));
        var rel = ctrl.animationClips.First(c => c.name.EndsWith("_Reload"));
        rel.SampleAnimation(view.model, 1.5f); dump("reload 1.5");
        idle.SampleAnimation(view.model, 0f); dump("idle 0");
        var smr = arms.GetComponentsInChildren<SkinnedMeshRenderer>()[0];
        sb.AppendLine("smr bounds " + smr.bounds.ToString("F3") + " bones0 " + smr.bones[0].name + " clips " + string.Join(",", ctrl.animationClips.Select(c => c.name)));
        sb.AppendLine("bindings sample: " + string.Join(" ; ", AnimationUtility.GetCurveBindings(idle).Take(5).Select(b => b.path + ":" + b.propertyName)));
        Object.DestroyImmediate(holder);
    }
    // previews through the game's own builder: idle | mid reload | fire, from the eye
    static void Preview(StringBuilder sb)
    {
        const int W = 560, H = 315;
        Directory.CreateDirectory("Logs/tacarms_preview");
        var stage = new GameObject("__ArmsPreview"); stage.transform.position = new Vector3(0, 4000, 0);
        try
        {
            var camGo = new GameObject("Cam"); camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(ElderboomPauseMenu.DefaultHorizontalFov, (float)W / H); sb.AppendLine("hfov " + ElderboomPauseMenu.DefaultHorizontalFov);
            var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>(); hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(.16f, .17f, .19f);
            var vol = stage.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 10000f;
            var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>(); vol.sharedProfile = profile;
            var ex = profile.Add<UnityEngine.Rendering.HighDefinition.Exposure>(true); ex.mode.Override(UnityEngine.Rendering.HighDefinition.ExposureMode.Fixed); ex.fixedExposure.Override(9.6f);
            var key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(stage.transform, false); key.type = LightType.Directional;
            key.transform.localRotation = Quaternion.Euler(40, -35, 0); key.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); key.intensity = 7000;
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(stage.transform, false); fill.type = LightType.Directional;
            fill.transform.localRotation = Quaternion.Euler(10, 150, 0); fill.gameObject.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>(); fill.intensity = 2500;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            var img = new Texture2D(W * 3, H, TextureFormat.RGB24, false);
            foreach (var def in ElderboomWeapons.All)
            {
                string fam = ElderboomWeaponOnly.FamilyOf(def.id); if (fam == null) continue;
                var root = ElderboomWeaponOnly.Build(def, cam.transform);
                if (root == null) { sb.AppendLine(def.id + ": build failed"); continue; }
                root.gameObject.SetActive(true);
                var view = root.GetComponent<ElderboomViewmodel>();
                if (view == null || view.arms == null) { sb.AppendLine(def.id + ": not armed"); Object.DestroyImmediate(root.gameObject); continue; }
                var ctrl = view.arms.animator.runtimeAnimatorController;
                AnimationClip Clip(string suffix) => ctrl.animationClips.FirstOrDefault(c => c.name.EndsWith(suffix));
                var shots = new[] { (Clip("_Idle"), 0f), (Clip("_Reload") ?? Clip("_ReloadLoop"), .45f), (Clip("_Fire"), .04f) };
                for (int i = 0; i < 3; i++)
                {
                    var (c, f) = shots[i];
                    if (c != null) c.SampleAnimation(view.model, c.length * f);
                    cam.Render(); RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, W, H), i * W, 0); RenderTexture.active = null;
                }
                img.Apply(); File.WriteAllBytes("Logs/tacarms_preview/" + def.id + ".png", img.EncodeToPNG());
                sb.AppendLine(def.id + " (" + fam + "): ok, ads " + view.hasAds + " " + view.adsPosition.ToString("F3"));
                Object.DestroyImmediate(root.gameObject);
            }
            cam.targetTexture = null; rt.Release();
        }
        finally { Object.DestroyImmediate(stage); }
    }
}
