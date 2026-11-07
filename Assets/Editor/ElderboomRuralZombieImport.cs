using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Object = UnityEngine.Object;

// Bounded importer: only RuralSet assets; never rewrites the map or existing controllers.
public static class ElderboomRuralZombieImport
{
    const string Root = "Assets/GameAssets/Zombies/RuralSet";
    const string Output = "Logs/RuralZombies";
    // Prefabs live in Resources so the waves can load them by folder (Resources/Elderboom/Zombies).
    const string PrefabFolder = "Assets/Resources/Elderboom/Zombies";
    [Serializable] class Spec { public string name, role; public float height_m; public bool female; }
    [Serializable] class Report { public string name, role, avatar, prefab; public bool validHumanoid; public float height; public int[] triangles; public List<PoseReport> poses = new List<PoseReport>(); }
    [Serializable] class PoseReport { public string state; public int samples; public float handTravel, limbRotationTravelDegrees; public Vector3 maxBounds; }
    [Serializable] class BundleReport { public bool passed; public List<Report> characters = new List<Report>(); }
    static BundleReport report;
    static Camera camera;

    public static void ValidateLive()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupStudio();
        EditorApplication.playModeStateChanged -= OnPlay;
        EditorApplication.playModeStateChanged += OnPlay;
        EditorApplication.isPlaying = true;
    }

    [InitializeOnLoadMethod]
    static void RestoreLiveValidation()
    {
        if (!Environment.GetCommandLineArgs().Contains("ElderboomRuralZombieImport.ValidateLive")) return;
        EditorApplication.playModeStateChanged -= OnPlay;
        EditorApplication.playModeStateChanged += OnPlay;
    }

    static void OnPlay(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode) return;
        camera = Object.FindAnyObjectByType<Camera>();
        ElderboomValidationHost.Create().StartCoroutine(LivePoses());
    }

    static IEnumerator LivePoses()
    {
        var lines = new List<string>();
        foreach (var folder in Directory.GetDirectories(Root).OrderBy(x => x))
        {
            var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(folder + "/spec.json"));
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + "/" + spec.name + ".prefab"));
            root.GetComponent<ElderboomZombie>().enabled = false;
            root.GetComponent<LODGroup>().ForceLOD(0);
            var animator = root.GetComponentInChildren<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var mesh = root.GetComponentsInChildren<SkinnedMeshRenderer>().Single(m => m.name.Contains("LOD0"));
            foreach (string state in new[] { "WalkA", "AttackA", "ShotHit", "DeathA" })
            {
                animator.speed = 0;
                animator.Play(state, 0, .48f); animator.Update(0);
                var center = BakedBounds(mesh).center;
                camera.transform.SetPositionAndRotation(center + new Vector3(2.2f, .35f, 3.4f), Quaternion.LookRotation(new Vector3(-2.2f, -.35f, -3.4f)));
                yield return null; yield return null; yield return null;
                var rt = new RenderTexture(640, 800, 24); var tex = new Texture2D(640, 800, TextureFormat.RGB24, false);
                camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 640, 800), 0, 0); tex.Apply();
                File.WriteAllBytes(Output + "/live_" + spec.name + "_" + state + ".jpg", tex.EncodeToJPG(92));
                RenderTexture.active = null; camera.targetTexture = null;
                Object.Destroy(tex); Object.Destroy(rt);
                lines.Add(spec.name + " " + state + " human=" + animator.isHuman + " state=" + animator.GetCurrentAnimatorStateInfo(0).IsName(state));
            }
            foreach (var mat in root.GetComponentsInChildren<Renderer>().SelectMany(r => r.sharedMaterials).Distinct())
                foreach (string property in new[] { "_BaseColorMap", "_NormalMap", "_MaskMap" })
                {
                    var tex = mat.GetTexture(property);
                    if (tex == null || tex.width > 2048 || tex.height > 2048) throw new Exception("Texture exceeds 2K: " + spec.name);
                    lines.Add(spec.name + " " + property + "=" + tex.width + "x" + tex.height);
                }
            Object.Destroy(root); yield return null;
        }
        File.WriteAllLines(Output + "/live-validation.txt", lines);
        Debug.Log("RURAL_ZOMBIES_LIVE_7_PASSED");
        EditorApplication.isPlaying = false;
        EditorApplication.delayCall += () => EditorApplication.Exit(0);
    }

    public static void Inspect()
    {
        var obj = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/01_market_vendor/character.fbx"));
        var lines = new List<string>();
        var animator = obj.GetComponent<Animator>();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animations/Port_ZombieFemale.controller");
        foreach (var clip in controller.animationClips) lines.Add("CLIP " + clip.name + " human=" + clip.humanMotion + " len=" + clip.length + " path=" + AssetDatabase.GetAssetPath(clip));
        foreach (var bone in animator.avatar.humanDescription.human) lines.Add("MAP " + bone.humanName + "=" + bone.boneName);
        animator.runtimeAnimatorController = controller; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.applyRootMotion = false;
        animator.Rebind(); animator.Update(0);
        foreach (float phase in new[] {0f, .25f, .5f, .75f})
        {
            animator.Play("AttackA", 0, phase); animator.Update(.001f);
            foreach (var key in new[] {HumanBodyBones.Hips,HumanBodyBones.LeftUpperArm,HumanBodyBones.LeftLowerArm,HumanBodyBones.LeftHand,HumanBodyBones.LeftUpperLeg})
            { var bone=animator.GetBoneTransform(key); lines.Add("POSE "+phase+" "+key+" rot="+bone.localRotation.eulerAngles.ToString("F2")+" pos="+bone.position.ToString("F3")); }
        }
        foreach (var t in obj.GetComponentsInChildren<Transform>())
            if (t == obj.transform || t.name.Contains("LOD") || t.name == "Hips" || t.name == "Armature")
                lines.Add(t.name + " local=" + t.localScale.ToString("F5") + " lossy=" + t.lossyScale.ToString("F5") + " pos=" + t.position.ToString("F5"));
        foreach (var r in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            lines.Add(r.name + " shared=" + r.sharedMesh.bounds + " local=" + r.localBounds + " world=" + r.bounds);
            lines.Add("WEIGHTS " + string.Join(",",r.sharedMesh.boneWeights.GroupBy(w=>w.boneIndex0).Select(g=>r.bones[g.Key].name+":"+g.Count())));
            foreach (bool scale in new[] { false, true })
            {
                var mesh = new Mesh(); r.BakeMesh(mesh, scale); mesh.RecalculateBounds();
                lines.Add("bake " + scale + " = " + mesh.bounds);
                Object.DestroyImmediate(mesh);
            }
        }
        File.WriteAllLines(Output + "/inspect.txt", lines);
        Object.DestroyImmediate(obj); EditorApplication.Exit(0);
    }

    public static void Run()
    {
        Directory.CreateDirectory(Output);
        report = new BundleReport();
        try
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SetupStudio();
            var folders = Directory.GetDirectories(Root).OrderBy(x => x).ToArray();
            if (folders.Length != 7) throw new Exception("Expected all seven character packages");
            foreach (var folder in folders) Build(folder.Replace('\\', '/'));
            AssetDatabase.SaveAssets();
            report.passed = true;
            File.WriteAllText(Output + "/validation.json", JsonUtility.ToJson(report, true));
            if (File.Exists(Output + "/error.txt")) File.Delete(Output + "/error.txt");
            Debug.Log("RURAL_ZOMBIES_VALIDATED_7");
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            File.WriteAllText(Output + "/validation.json", JsonUtility.ToJson(report, true));
            File.WriteAllText(Output + "/error.txt", e.ToString());
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void Build(string folder)
    {
        var spec = JsonUtility.FromJson<Spec>(File.ReadAllText(folder + "/spec.json"));
        string path = folder + "/character.fbx";
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = false;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false; importer.importLights = false;
        importer.optimizeGameObjects = false;
        importer.meshCompression = ModelImporterMeshCompression.Off;
        importer.isReadable = true;
        importer.SaveAndReimport();
        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault(a => a.isHuman && a.isValid);
        if (avatar == null) throw new Exception("Invalid Humanoid: " + spec.name);
        var material = Material(folder, spec.name);
        var root = new GameObject(spec.name);
        var visual = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);
        visual.name = "Visual";
        foreach (var group in visual.GetComponentsInChildren<LODGroup>()) Object.DestroyImmediate(group);
        var animator = visual.GetComponent<Animator>() ?? visual.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(spec.female
            ? "Assets/Animations/Port_ZombieFemale.controller" : "Assets/Animations/Port_Zombie.controller");
        if (animator.runtimeAnimatorController == null) throw new Exception("Existing controller missing");
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        var meshes = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (meshes.Length != 3) throw new Exception("Expected three skinned LOD meshes: " + spec.name);
        var levels = Enumerable.Range(0, 3).Select(i => meshes.Single(m => m.name.Contains("LOD" + i))).ToArray();
        foreach (var mesh in meshes)
        {
            mesh.sharedMaterial = material;
            mesh.quality = SkinQuality.Bone4;
            mesh.updateWhenOffscreen = false;
            if (mesh.sharedMesh.boneWeights.Any(w => Math.Abs(w.weight0 + w.weight1 + w.weight2 + w.weight3 - 1) > .002f))
                throw new Exception("Invalid imported bone weights: " + mesh.name);
        }
        // Imported skinned bounds include a conservative animation margin. Measure
        // the actual deformed vertices for height and the ground plane instead.
        var rest = BakedBounds(levels[0]);
        if (Mathf.Abs(rest.size.y - spec.height_m) > .015f || Mathf.Abs(rest.min.y) > .015f)
            throw new Exception("Incorrect imported scale/ground: " + spec.name + " " + rest);
        var lod = root.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(.35f, new Renderer[] { levels[0] }), new LOD(.15f, new Renderer[] { levels[1] }), new LOD(.025f, new Renderer[] { levels[2] }) });
        lod.RecalculateBounds();
        var body = root.AddComponent<CapsuleCollider>();
        body.height = spec.height_m * .80f; body.radius = spec.height_m * .16f;
        body.center = Vector3.up * body.height * .5f;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) throw new Exception("No humanoid head transform");
        var hit = new GameObject("HeadHitbox"); hit.transform.SetParent(head, false);
        hit.transform.position = head.position + Vector3.up * spec.height_m * .047f;
        var sphere = hit.AddComponent<SphereCollider>();
        sphere.radius = spec.height_m * .09f / Mathf.Abs(hit.transform.lossyScale.x);
        root.AddComponent<ElderboomZombie>();
        Directory.CreateDirectory(PrefabFolder); string prefabPath = PrefabFolder + "/" + spec.name + ".prefab";
        // Save the untouched rest pose before exercising the animator.
        if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new Exception("Prefab save failed");
        var item = new Report { name = spec.name, role = spec.role, avatar = avatar.name, prefab = prefabPath,
            validHumanoid = true, height = rest.size.y, triangles = levels.Select(m => m.sharedMesh.triangles.Length / 3).ToArray() };
        report.characters.Add(item);
        lod.ForceLOD(0);
        foreach (var mesh in meshes) mesh.updateWhenOffscreen = true;
        Capture(spec.name + "_front", new Vector3(0, spec.height_m * .55f, 3.5f), Vector3.up * spec.height_m * .49f);
        Capture(spec.name + "_back", new Vector3(-2, spec.height_m * .65f, -3.2f), Vector3.up * spec.height_m * .49f);
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind(); animator.Update(0);
        var controller = (AnimatorController)animator.runtimeAnimatorController;
        var states = controller.layers[0].stateMachine.states.Select(s => s.state).ToArray();
        foreach (string required in new[] { "WalkA", "AttackA", "ShotHit", "DeathA" })
            if (!states.Any(s => s.name == required)) throw new Exception("Missing state " + required);
        foreach (var state in states)
        {
            var pose = new PoseReport { state = state.name };
            Vector3? previous = null;
            Quaternion[] previousRotations = null;
            for (int i = 0; i < 5; i++)
            {
                animator.Play(state.name, 0, i * .24f); animator.Update(0);
                var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                if (previous.HasValue) pose.handTravel += Vector3.Distance(previous.Value, hand);
                previous = hand;
                var rotations = new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg }
                    .Select(b => animator.GetBoneTransform(b).localRotation).ToArray();
                if (previousRotations != null) for (int j = 0; j < rotations.Length; j++) pose.limbRotationTravelDegrees += Quaternion.Angle(previousRotations[j], rotations[j]);
                previousRotations = rotations;
                foreach (var mesh in levels)
                {
                    var baked = new Mesh(); mesh.BakeMesh(baked, true);
                    var world = baked.vertices.Select(v => mesh.transform.TransformPoint(v)).ToArray();
                    if (world.Any(v => !float.IsFinite(v.x) || !float.IsFinite(v.y) || !float.IsFinite(v.z)))
                        throw new Exception("Nonfinite deformation " + spec.name + "/" + state.name);
                    var b = new Bounds(world[0], Vector3.zero); foreach (var v in world) b.Encapsulate(v);
                    if (b.size.magnitude > spec.height_m * 3) throw new Exception("Exploding mesh " + spec.name + "/" + state.name);
                    pose.maxBounds = Vector3.Max(pose.maxBounds, b.size);
                    Object.DestroyImmediate(baked);
                }
                pose.samples++;
                if (i == 2 && new[] { "WalkA", "AttackA", "ShotHit", "DeathA" }.Contains(state.name))
                {
                    var center = BakedBounds(levels[0]).center;
                    Capture(spec.name + "_" + state.name, center + new Vector3(2.2f, .35f, 3.4f), center);
                }
            }
            if (pose.handTravel < .005f) throw new Exception("Animation does not move the hand: " + spec.name + "/" + state.name);
            if (new[] { "WalkA", "AttackA", "ShotHit", "DeathA" }.Contains(state.name) && pose.limbRotationTravelDegrees < 1f)
                throw new Exception("Animation moves root but not limbs: " + spec.name + "/" + state.name);
            item.poses.Add(pose);
        }
        Debug.Log("RURAL_CHARACTER_OK " + spec.name + " avatar=" + avatar.name + " states=" + item.poses.Count);
        Object.DestroyImmediate(root);
    }

    static Material Material(string folder, string name)
    {
        Texture2D Texture(string suffix, bool srgb, bool normal)
        {
            string path = folder + "/" + suffix + ".png";
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = srgb; imp.maxTextureSize = 2048; imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        var albedo = Texture("diffuse", true, false); var normalMap = Texture("normal", false, true); var mask = Texture("mask", false, false);
        string path = folder + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("HDRP/Lit")) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", Color.white); mat.SetTexture("_BaseColorMap", albedo);
        mat.SetTexture("_NormalMap", normalMap); mat.SetTexture("_MaskMap", mask);
        mat.SetFloat("_Metallic", 1); mat.SetFloat("_Smoothness", 1);
        mat.SetFloat("_SmoothnessRemapMin", 0); mat.SetFloat("_SmoothnessRemapMax", 1);
        HDMaterial.ValidateMaterial(mat); EditorUtility.SetDirty(mat); return mat;
    }

    static Bounds BakedBounds(SkinnedMeshRenderer renderer)
    {
        var baked = new Mesh(); renderer.BakeMesh(baked, true);
        var vertices = baked.vertices;
        var bounds = new Bounds(renderer.transform.TransformPoint(vertices[0]), Vector3.zero);
        foreach (var vertex in vertices) bounds.Encapsulate(renderer.transform.TransformPoint(vertex));
        Object.DestroyImmediate(baked);
        return bounds;
    }

    static void SetupStudio()
    {
        var volume = new GameObject("PreviewExposure").AddComponent<Volume>(); volume.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>(); volume.sharedProfile = profile;
        var exposure = profile.Add<Exposure>(true); exposure.mode.Override(ExposureMode.Fixed); exposure.fixedExposure.Override(9.2f);
        profile.Add<Fog>(true).enabled.Override(false);
        profile.Add<MotionBlur>(true).intensity.Override(0);
        profile.Add<DepthOfField>(true).focusMode.Override(DepthOfFieldMode.Off);
        void Light(string name, Vector3 rotation, float lux, Color color)
        {
            var go = new GameObject(name); go.transform.rotation = Quaternion.Euler(rotation);
            var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.color = color;
            go.AddComponent<HDAdditionalLightData>(); light.intensity = lux;
        }
        Light("Key", new Vector3(30, 155, 0), 1800, new Color(1f, .93f, .86f));
        Light("Fill", new Vector3(15, -135, 0), 900, new Color(.83f, .9f, 1f));
        Light("Back", new Vector3(30, 20, 0), 1400, Color.white);
        camera = new GameObject("PreviewCamera").AddComponent<Camera>();
        camera.nearClipPlane = .05f; camera.farClipPlane = 30; camera.fieldOfView = 37;
        var data = camera.gameObject.AddComponent<HDAdditionalCameraData>();
        data.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; data.backgroundColorHDR = new Color(.10f, .115f, .13f);
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
    }

    static void Capture(string name, Vector3 eye, Vector3 target)
    {
        // Multiple Camera.Render calls in one editor tick can reuse GPU skinning
        // from the first pose. Render the exact CPU-skinned surface for each pose.
        var skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None);
        var snapshots = new List<GameObject>(); var bakedMeshes = new List<Mesh>();
        foreach (var r in skinned)
        {
            r.enabled = false;
            if (!r.name.Contains("LOD0")) continue;
            var mesh = new Mesh(); r.BakeMesh(mesh, true); mesh.RecalculateBounds();
            var go = new GameObject("PoseSurface"); go.transform.SetParent(r.transform.parent, false);
            go.transform.localPosition = r.transform.localPosition; go.transform.localRotation = r.transform.localRotation; go.transform.localScale = r.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = r.sharedMaterials;
            snapshots.Add(go); bakedMeshes.Add(mesh);
        }
        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
        var rt = new RenderTexture(640, 800, 24); var tex = new Texture2D(640, 800, TextureFormat.RGB24, false);
        camera.targetTexture = rt;
        for (int i = 0; i < 3; i++) camera.Render();
        RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 640, 800), 0, 0); tex.Apply();
        File.WriteAllBytes(Output + "/" + name + ".jpg", tex.EncodeToJPG(92));
        RenderTexture.active = null; camera.targetTexture = null;
        Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        foreach (var go in snapshots) Object.DestroyImmediate(go);
        foreach (var mesh in bakedMeshes) Object.DestroyImmediate(mesh);
        foreach (var r in skinned) r.enabled = true;
    }
}
