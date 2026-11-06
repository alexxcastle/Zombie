using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class ElderboomAbilityPreview
{
    const string ScenePath = "Assets/Scenes/AbilityPreview.unity";
    const string OutDir = "Logs/ability_preview";

    public static void Play()
    {
        Directory.CreateDirectory(OutDir);
        if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) File.Delete(Path.Combine(OutDir, "play_done.txt"));
        EditorSceneManager.OpenScene(ScenePath);
        var rig = UnityEngine.Object.FindAnyObjectByType<ElderboomAbilityPreviewRig>();
        if (rig == null)
        {
            File.AppendAllText(Path.Combine(OutDir, "report.txt"), "\nplay rig missing\n");
            EditorApplication.Exit(1);
            return;
        }
        rig.captureOnPlay = true;
        double deadline = EditorApplication.timeSinceStartup + 150;
        EditorApplication.update += () =>
        {
            if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) return;
            if (EditorApplication.timeSinceStartup < deadline) return;
            File.AppendAllText(Path.Combine(OutDir, "report.txt"), "\nPLAY TIMEOUT\n");
            EditorApplication.Exit(2);
        };
        EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
    }

    public static void Render()
    {
        Directory.CreateDirectory(OutDir);
        var report = new StringBuilder();
        try
        {
            FixImports(report);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = BuildCamera();
            BuildVolume();
            BuildSun(camera.transform);
            var lift = Place(camera.transform, "Assets/Resources/Elderboom/Abilities/LiftSmash.prefab");
            var keep = Place(camera.transform, "Assets/Resources/Elderboom/Abilities/KeepAway.prefab");
            camera.gameObject.AddComponent<ElderboomAbilityPreviewRig>();
            EditorSceneManager.SaveScene(scene, ScenePath);
            report.AppendLine("gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
            lift.SetActive(false);
            keep.SetActive(false);
            Probe(camera, lift, report);
            Probe(camera, keep, report);
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), report.ToString());
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), report + "\n" + ex);
            Debug.LogError(ex);
            EditorApplication.Exit(1);
        }
    }

    static void Probe(Camera camera, GameObject source, StringBuilder report)
    {
        var view = EnsureView(source, report);
        if (view == null || view.clip == null || view.model == null)
        {
            report.AppendLine("still missing view=" + (view != null) + " model=" + (view != null && view.model != null) + " clip=" + (view != null && view.clip != null));
            return;
        }
        DescribeClip(view, report);
        source.SetActive(false);
        float mid = view.clip.length * 0.45f;
        var methods = new Dictionary<string, Action<ElderboomAbilityView, float>>
        {
            { "current", (item, time) => item.Sample(time) },
            { "strip-animator", StripAndSample },
            { "wrapper", (item, time) => item.clip.SampleAnimation(item.gameObject, time) },
            { "legacy-animation", SampleLegacy },
            { "playable", SamplePlayable }
        };
        string winner = "current";
        float best = -1f;
        foreach (var method in methods)
        {
            var copy = UnityEngine.Object.Instantiate(source, camera.transform, false);
            copy.SetActive(true);
            Prepare(copy);
            var copyView = copy.GetComponent<ElderboomAbilityView>();
            if (copyView == null || copyView.model == null || copyView.clip == null)
            {
                report.AppendLine(method.Key + " copy missing view=" + (copyView != null) + " model=" + (copyView != null && copyView.model != null) + " clip=" + (copyView != null && copyView.clip != null));
                UnityEngine.Object.DestroyImmediate(copy);
                continue;
            }
            try
            {
                float score = Delta(copyView.model.transform, time => method.Value(copyView, time), 0.02f, mid, out string bone);
                var mesh = MeshDelta(copyView, method.Value, 0.02f, mid);
                report.AppendLine(method.Key + " bone=" + score.ToString("0.00") + " at " + bone + " mesh=" + mesh.ToString("0.000") + " " + PoseDelta(copyView, method.Value, 0.02f, mid));
                if (score > best)
                {
                    best = score;
                    winner = method.Key;
                }
            }
            catch (Exception ex)
            {
                report.AppendLine(method.Key + " FAILED " + ex.GetType().Name + ": " + ex.Message);
            }
            UnityEngine.Object.DestroyImmediate(copy);
        }
        report.AppendLine("winner=" + winner);
        RenderSequence(camera, source, "current", methods["current"], report);
        if (winner != "current") RenderSequence(camera, source, winner, methods[winner], report);
        source.SetActive(false);
    }

    static void RenderSequence(Camera camera, GameObject source, string label, Action<ElderboomAbilityView, float> apply, StringBuilder report)
    {
        var copy = UnityEngine.Object.Instantiate(source, camera.transform, false);
        copy.SetActive(true);
        Prepare(copy);
        var view = copy.GetComponent<ElderboomAbilityView>();
        float length = view.clip.length;
        float[] times = { 0.02f, length * 0.25f, length * 0.5f, length * 0.75f, Mathf.Max(0.05f, length - 0.05f) };
        for (int i = 0; i < times.Length; i++)
        {
            apply(view, times[i]);
            string path = Path.Combine(OutDir, "edit_" + label + "_" + source.name + "_" + i + ".png");
            report.AppendLine("shot " + path + " t=" + times[i].ToString("0.00") + " " + ElderboomAbilityShots.Capture(camera, path));
        }
        UnityEngine.Object.DestroyImmediate(copy);
    }

    static void FixImports(StringBuilder report)
    {
        foreach (var path in new[]
        {
            "Assets/GameAssets/Ported/Tactical/Abilities/LiftSmash.glb",
            "Assets/GameAssets/Ported/Tactical/Abilities/KeepAway.glb"
        })
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        BindClip("Assets/Resources/Elderboom/Abilities/LiftSmash.prefab", "Assets/GameAssets/Ported/Tactical/Abilities/LiftSmash.glb", "lift and smash", report);
        BindClip("Assets/Resources/Elderboom/Abilities/KeepAway.prefab", "Assets/GameAssets/Ported/Tactical/Abilities/KeepAway.glb", "Keep-Away-Striker12", report);
        var striker = AssetDatabase.LoadAllAssetsAtPath("Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack.glb").OfType<AnimationClip>();
        foreach (var clip in striker)
            if (clip != null && !clip.name.StartsWith("__preview"))
                report.AppendLine("striker " + clip.name + " legacy=" + clip.legacy + " length=" + clip.length.ToString("0.00"));
        AssetDatabase.SaveAssets();
    }

    static void BindClip(string prefabPath, string assetPath, string clipName, StringBuilder report)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var wrong = root.GetComponent<ElderboomTacticalArms>();
        if (wrong != null) UnityEngine.Object.DestroyImmediate(wrong);
        var view = root.GetComponent<ElderboomAbilityView>();
        if (view == null) view = root.AddComponent<ElderboomAbilityView>();
        if (view.model == null && root.transform.childCount > 0)
            view.model = root.transform.GetChild(0).gameObject;
        view.clip = LoadClip(assetPath, clipName);
        string bound = root.name + " clip=" + (view.clip != null ? view.clip.name + " legacy=" + view.clip.legacy : "null");
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        report.AppendLine("bound " + bound);
    }

    static string PoseDelta(ElderboomAbilityView view, Action<ElderboomAbilityView, float> apply, float a, float b)
    {
        string[] names = { "Hand.L", "ForeArm.L", "Upper_Arm.L", "Hand.R" };
        var nodes = new Transform[names.Length];
        var start = new Quaternion[names.Length];
        apply(view, a);
        for (int i = 0; i < names.Length; i++)
        {
            nodes[i] = FindNamed(view.model.transform, names[i]);
            start[i] = nodes[i] != null ? nodes[i].localRotation : Quaternion.identity;
        }
        apply(view, b);
        var text = new StringBuilder();
        for (int i = 0; i < names.Length; i++)
        {
            float angle = nodes[i] == null ? -1f : Quaternion.Angle(start[i], nodes[i].localRotation);
            text.Append(names[i]).Append('=').Append(angle.ToString("0.0")).Append(' ');
        }
        return text.ToString();
    }

    static Transform FindNamed(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == name) return node;
        return null;
    }

    static ElderboomAbilityView EnsureView(GameObject source, StringBuilder report)
    {
        report.AppendLine("---- " + source.name + " ----");
        report.AppendLine("components=" + string.Join(", ", source.GetComponents<Component>().Select(component => component == null ? "missing-script" : component.GetType().Name)));
        var view = source.GetComponent<ElderboomAbilityView>();
        report.AppendLine("serialized view=" + (view != null) + " model=" + (view != null && view.model != null) + " clip=" + (view != null && view.clip != null ? view.clip.name + " legacy=" + view.clip.legacy : "null"));
        if (view == null) view = source.AddComponent<ElderboomAbilityView>();
        if (view.model == null)
        {
            var model = source.transform.Find("Viewmodel");
            if (model == null && source.transform.childCount > 0) model = source.transform.GetChild(0);
            if (model != null) view.model = model.gameObject;
        }
        bool keep = source.name.IndexOf("Keep", StringComparison.OrdinalIgnoreCase) >= 0;
        var loaded = LoadClip(
            keep ? "Assets/GameAssets/Ported/Tactical/Abilities/KeepAway.glb" : "Assets/GameAssets/Ported/Tactical/Abilities/LiftSmash.glb",
            keep ? "Keep-Away-Striker12" : "lift and smash");
        report.AppendLine("asset clip=" + (loaded != null ? loaded.name + " legacy=" + loaded.legacy + " length=" + loaded.length.ToString("0.00") : "null"));
        if (view.clip == null) view.clip = loaded;
        return view;
    }

    static AnimationClip LoadClip(string assetPath, string name)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Where(clip => clip != null && !clip.name.StartsWith("__preview"));
        return clips.FirstOrDefault(clip => string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase))
            ?? clips.FirstOrDefault();
    }

    static void DescribeClip(ElderboomAbilityView view, StringBuilder report)
    {
        var clip = view.clip;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        int onModel = 0, onWrapper = 0;
        var paths = new HashSet<string>();
        foreach (var binding in bindings)
        {
            paths.Add(binding.path);
            if (string.IsNullOrEmpty(binding.path) || view.model.transform.Find(binding.path) != null) onModel++;
            if (string.IsNullOrEmpty(binding.path) || view.transform.Find(binding.path) != null) onWrapper++;
        }
        report.AppendLine("clip=" + clip.name + " legacy=" + clip.legacy + " length=" + clip.length.ToString("0.00")
            + " curves=" + bindings.Length + " paths=" + paths.Count + " onModel=" + onModel + " onWrapper=" + onWrapper);
        foreach (var path in paths.Take(12)) report.AppendLine("  path " + (string.IsNullOrEmpty(path) ? "<root>" : path));
        int animators = view.model.GetComponentsInChildren<Animator>(true).Length;
        report.AppendLine("animators=" + animators);
    }

    static void Prepare(GameObject root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.layer = 0;
            if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
        }
    }

    static void StripAndSample(ElderboomAbilityView view, float time)
    {
        foreach (var animator in view.GetComponentsInChildren<Animator>(true))
            UnityEngine.Object.DestroyImmediate(animator);
        view.Sample(time);
    }

    static void SampleLegacy(ElderboomAbilityView view, float time)
    {
        foreach (var animator in view.model.GetComponentsInChildren<Animator>(true))
            UnityEngine.Object.DestroyImmediate(animator);
        var animation = view.model.GetComponent<Animation>();
        if (animation == null) animation = view.model.AddComponent<Animation>();
        animation.playAutomatically = false;
        if (animation.GetClip(view.clip.name) == null) animation.AddClip(view.clip, view.clip.name);
        animation.clip = view.clip;
        var state = animation[view.clip.name];
        state.enabled = true;
        state.weight = 1f;
        state.time = time;
        animation.Sample();
    }

    static void SamplePlayable(ElderboomAbilityView view, float time)
    {
        var animator = view.model.GetComponent<Animator>();
        if (animator == null) animator = view.model.AddComponent<Animator>();
        animator.enabled = true;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var graph = PlayableGraph.Create("ability-preview");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var playable = AnimationClipPlayable.Create(graph, view.clip);
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        output.SetSourcePlayable(playable);
        playable.SetTime(time);
        graph.Evaluate(0f);
        graph.Destroy();
    }

    static float Delta(Transform root, Action<float> apply, float a, float b, out string bone)
    {
        apply(a);
        var bones = root.GetComponentsInChildren<Transform>(true);
        var rotations = new Quaternion[bones.Length];
        var positions = new Vector3[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            rotations[i] = bones[i].localRotation;
            positions[i] = bones[i].localPosition;
        }
        apply(b);
        float best = 0f;
        bone = "";
        for (int i = 0; i < bones.Length; i++)
        {
            float score = Quaternion.Angle(rotations[i], bones[i].localRotation) + Vector3.Distance(positions[i], bones[i].localPosition) * 40f;
            if (score <= best) continue;
            best = score;
            bone = bones[i].name;
        }
        return best;
    }

    static float MeshDelta(ElderboomAbilityView view, Action<ElderboomAbilityView, float> apply, float a, float b)
    {
        apply(view, a);
        var start = BakedCenter(view.model);
        apply(view, b);
        return Vector3.Distance(start, BakedCenter(view.model));
    }

    static Vector3 BakedCenter(GameObject model)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!skin.gameObject.activeInHierarchy) continue;
            var mesh = new Mesh();
            skin.BakeMesh(mesh);
            sum += skin.transform.TransformPoint(mesh.bounds.center);
            count++;
            UnityEngine.Object.DestroyImmediate(mesh);
        }
        return count == 0 ? Vector3.zero : sum / count;
    }

    static Camera BuildCamera()
    {
        var go = new GameObject("AbilityPreviewCamera");
        go.tag = "MainCamera";
        var camera = go.AddComponent<Camera>();
        camera.fieldOfView = 72f;
        camera.nearClipPlane = 0.02f;
        camera.farClipPlane = 30f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.025f, 0.04f);
        camera.allowHDR = true;
        var additional = go.AddComponent<HDAdditionalCameraData>();
        additional.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
        additional.backgroundColorHDR = new Color(0.02f, 0.025f, 0.04f, 1f);
        return camera;
    }

    static void BuildVolume()
    {
        var go = new GameObject("Global Volume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/Elderboom_Night_Profile.asset");

        var overObject = new GameObject("Preview Exposure");
        var over = overObject.AddComponent<Volume>();
        over.isGlobal = true;
        over.priority = 20;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var exposure = profile.Add<Exposure>(true);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(9.2f);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0f);
        over.sharedProfile = profile;
    }

    static void BuildSun(Transform camera)
    {
        var fill = new GameObject("Viewmodel Fill");
        fill.transform.SetParent(camera, false);
        fill.transform.localPosition = new Vector3(0.12f, 0.08f, 0.35f);
        fill.transform.localRotation = Quaternion.identity;
        var spot = fill.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.9f, 0.82f);
        spot.intensity = 180f;
        spot.range = 6f;
        spot.spotAngle = 90f;
        spot.shadows = LightShadows.None;
        fill.AddComponent<HDAdditionalLightData>();
    }

    static GameObject Place(Transform camera, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new FileNotFoundException("Ability prefab missing", path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, camera);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        Prepare(instance);
        return instance;
    }
}
