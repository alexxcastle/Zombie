using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElderboomTacticalCheck
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string StrikerAsset = "Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack.gltf";
    const string LiftAsset = "Assets/GameAssets/Ported/Tactical/Abilities/LiftSmash.gltf";
    const string KeepAsset = "Assets/GameAssets/Ported/Tactical/Abilities/KeepAway.gltf";
    const string PistolAsset = "Assets/GameAssets/Ported/Sidearms/1911_idle.gltf";

    public static void Apply()
    {
        Directory.CreateDirectory("Logs");
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var report = new StringBuilder();
        var player = UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null) throw new InvalidOperationException("FPS player missing");
        var camera = player.GetComponentInChildren<Camera>(true);
        if (camera == null) throw new InvalidOperationException("FPS camera missing");

        float strikerDepth = FrameStriker(camera.transform, report);
        FramePistol(camera.transform, report);
        BuildAbilityPrefab(LiftAsset, "lift and smash", "Assets/Resources/Elderboom/Abilities/LiftSmash.prefab", camera.transform, report);
        BuildAbilityPrefab(KeepAsset, "Keep-Away-Striker12", "Assets/Resources/Elderboom/Abilities/KeepAway.prefab", camera.transform, report);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText("Logs/tactical_check.txt", report.ToString());
        AssetDatabase.SaveAssets();
        Debug.Log("ELDERBOOM_TACTICAL_CHECK\n" + report);
        if (strikerDepth < 0.02f && strikerDepth > -10f)
            throw new InvalidOperationException("Striker is still behind the camera: " + strikerDepth.ToString("0.000"));
        HDRPSliceStandaloneBuild.BuildPort();
    }

    static float FrameStriker(Transform camera, StringBuilder report)
    {
        var pivot = camera.Find("Tactical_Viewmodel_Pivot");
        if (pivot == null) throw new InvalidOperationException("Tactical viewmodel pivot missing");
        bool wasActive = pivot.gameObject.activeSelf;
        pivot.gameObject.SetActive(true);
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;
        var model = pivot.childCount > 0 ? pivot.GetChild(0) : pivot;
        PrepareModel(model.gameObject);
        var shock = Clip(StrikerAsset, "shockwave");
        if (shock != null) shock.SampleAnimation(model.gameObject, 1f / 60f);
        bool aligned = ElderboomWeaponFraming.AlignAuthoredCamera(model);
        HideNamed(model, "output_unwrapped.002", "output_unwrapped_002");
        StripCameras(model.gameObject);
        var gun = FindExact(model, "output_unwrapped.003");
        float hipDepth = gun != null ? MeanDepth(camera, gun) : MeanDepth(camera, model);
        if (hipDepth < 0.02f)
        {
            // The glove GLB camera looks down authoring -Z. Unity looks down +Z.
            var turn = Quaternion.Euler(0f, 180f, 0f);
            model.localRotation = turn * model.localRotation;
            model.localPosition = turn * model.localPosition;
            hipDepth = gun != null ? MeanDepth(camera, gun) : MeanDepth(camera, model);
            report.AppendLine("striker view turned 180 so the glove faces the Unity camera");
        }
        var rear = gun != null ? ElderboomWeaponFraming.EnsureMarker(gun, "MainWeapon_ADS_Rear", new Vector3(-0.085f, 0.225f, 0f)) : null;
        var front = gun != null ? ElderboomWeaponFraming.EnsureMarker(gun, "MainWeapon_ADS_Front", new Vector3(-0.419f, 0.21241f, 0f)) : null;
        if (rear != null && front != null && Depth(camera, front) < Depth(camera, rear))
        {
            rear.localPosition = new Vector3(0.085f, 0.225f, 0f);
            front.localPosition = new Vector3(0.419f, 0.21241f, 0f);
            report.AppendLine("striker sight axis flipped on X");
        }
        var pose = new ElderboomWeaponPose { adsRelief = 0.16f, hipPosition = Vector3.zero, hipRotation = Quaternion.identity };
        pose.hasAds = ElderboomWeaponFraming.SolveSightPose(pivot, rear, front, new Vector3(0f, 0f, 0.16f), out pose.adsPosition, out pose.adsRotation);
        float adsDepth = gun != null ? MeanDepth(camera, gun) : hipDepth;
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        var driver = pivot.GetComponent<ElderboomViewmodel>() ?? pivot.gameObject.AddComponent<ElderboomViewmodel>();
        driver.model = model.gameObject;
        driver.operatorId = "TACTICAL";
        driver.idle = shock;
        driver.ability = shock;
        ElderboomWeaponFraming.Store(driver, pose);
        report.AppendLine("striker aligned=" + aligned + " hipZ=" + hipDepth.ToString("0.000") + " adsZ=" + adsDepth.ToString("0.000") + " model=" + model.localPosition.ToString("0.000") + " gun=" + (gun != null ? gun.name : "missing"));
        pivot.gameObject.SetActive(wasActive);
        return hipDepth;
    }

    static void FramePistol(Transform camera, StringBuilder report)
    {
        const string path = "Assets/Resources/Elderboom/Weapons/m1911.prefab";
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, camera);
        if (PrefabUtility.IsPartOfPrefabInstance(root))
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        try
        {
            var driver = root.GetComponent<ElderboomViewmodel>();
            var model = driver != null && driver.model != null ? driver.model.transform : root.transform.GetChild(0);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            PrepareModel(model.gameObject);
            var idle = Clip(PistolAsset, "1911_IDLE");
            if (idle != null) idle.SampleAnimation(model.gameObject, 0f);
            ElderboomWeaponFraming.HideNonViewmodelBodies(root.transform);
            var head = FindExact(model, "P_head");
            var rail = FindExact(model, "1911_Rail");
            float bestDepth = -999f;
            Vector3 bestPosition = model.localPosition;
            Quaternion bestRotation = model.localRotation;
            if (head != null)
            {
                Vector3 headLocal = model.InverseTransformPoint(head.position);
                foreach (float yaw in new[] { 180f, 0f })
                {
                    model.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    model.localPosition = new Vector3(0f, -0.055f, 0.12f) - model.localRotation * Vector3.Scale(model.localScale, headLocal);
                    float depth = rail != null ? MeanDepth(camera, rail) : MeanDepth(camera, model);
                    if (depth > bestDepth)
                    {
                        bestDepth = depth;
                        bestPosition = model.localPosition;
                        bestRotation = model.localRotation;
                    }
                }
                model.localPosition = bestPosition;
                model.localRotation = bestRotation;
            }
            var rear = rail != null ? ElderboomWeaponFraming.EnsureMarker(rail, "MainWeapon_ADS_Rear", new Vector3(0.076f, 0.046f, -0.00015f)) : null;
            var front = rail != null ? ElderboomWeaponFraming.EnsureMarker(rail, "MainWeapon_ADS_Front", new Vector3(-0.088f, 0.04119f, -0.000225f)) : null;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            if (rear != null && front != null && Depth(camera, front) < Depth(camera, rear))
            {
                rear.localPosition = new Vector3(-0.076f, 0.046f, -0.00015f);
                front.localPosition = new Vector3(0.088f, 0.04119f, -0.000225f);
                report.AppendLine("1911 sight axis flipped on X");
            }
            var pose = new ElderboomWeaponPose { adsRelief = 0.28f, hipPosition = Vector3.zero, hipRotation = Quaternion.identity };
            pose.hasAds = ElderboomWeaponFraming.SolveSightPose(root.transform, rear, front, new Vector3(0f, 0f, 0.28f), out pose.adsPosition, out pose.adsRotation);
            float adsDepth = rail != null ? MeanDepth(camera, rail) : bestDepth;
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            if (driver == null) driver = root.AddComponent<ElderboomViewmodel>();
            driver.model = model.gameObject;
            driver.operatorId = "handgun";
            driver.idle = idle;
            ElderboomWeaponFraming.Store(driver, pose);
            report.AppendLine("1911 hipZ=" + bestDepth.ToString("0.000") + " adsZ=" + adsDepth.ToString("0.000") + " head=" + (head != null));
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void BuildAbilityPrefab(string assetPath, string clipName, string prefabPath, Transform camera, StringBuilder report)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (source == null) throw new InvalidOperationException("Missing " + assetPath);
        var striker = camera.Find("Tactical_Viewmodel_Pivot");
        var reference = striker != null && striker.childCount > 0 ? striker.GetChild(0) : null;
        var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
        if (model == null) model = UnityEngine.Object.Instantiate(source, root.transform);
        if (PrefabUtility.IsPartOfPrefabInstance(model))
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Viewmodel";
        if (reference != null)
        {
            model.transform.localPosition = reference.localPosition;
            model.transform.localRotation = reference.localRotation;
            model.transform.localScale = reference.localScale;
        }
        else ElderboomWeaponFraming.AlignAuthoredCamera(model.transform);
        PrepareModel(model);
        HideNamed(model.transform, "output_unwrapped.002", "output_unwrapped_002");
        StripCameras(model);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.gameObject.layer = 19;
        }
        var clip = Clip(assetPath, clipName);
        if (clip != null) clip.SampleAnimation(model, clipName == "Keep-Away-Striker12" ? 20f / 60f : 30f / 60f);
        if (reference != null)
        {
            model.transform.localPosition = reference.localPosition;
            model.transform.localRotation = reference.localRotation;
            model.transform.localScale = reference.localScale;
        }
        var view = root.AddComponent<ElderboomAbilityView>();
        view.model = model;
        view.clip = clip;
        view.leds = Leds(model);
        var hand = FindExact(model.transform, "Hand.L");
        var glowObject = new GameObject("GlovePalmLight");
        glowObject.transform.SetParent(hand != null ? hand : model.transform, false);
        var glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.02f, 0.68f, 1f);
        glow.range = 1.15f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;
        view.glow = glow;
        root.transform.SetParent(camera, false);
        var gun = FindExact(model.transform, "output_unwrapped.003");
        float depth = gun != null ? MeanDepth(camera, gun) : MeanDepth(camera, model.transform);
        report.AppendLine(root.name + " clip=" + (clip != null ? clip.name + " " + clip.length.ToString("0.00") + "s" : "MISSING") + " z=" + depth.ToString("0.000") + " leds=" + view.leds.Length);
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Elderboom/Abilities"))
            AssetDatabase.CreateFolder("Assets/Resources/Elderboom", "Abilities");
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    static void PrepareModel(GameObject model)
    {
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
    }

    static void StripCameras(GameObject model)
    {
        foreach (var embedded in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(embedded);
        foreach (var listener in model.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
    }

    static void HideNamed(Transform root, params string[] names)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            foreach (var name in names)
                if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase))
                    node.gameObject.SetActive(false);
    }

    static Renderer[] Leds(GameObject root)
    {
        return root.GetComponentsInChildren<Renderer>(true).Where(renderer =>
            renderer.sharedMaterials.Any(material => material != null && material.name.IndexOf("shockwave-led", StringComparison.OrdinalIgnoreCase) >= 0)).ToArray();
    }

    static AnimationClip Clip(string assetPath, string name)
    {
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().Where(clip => !clip.name.StartsWith("__preview"));
        return clips.FirstOrDefault(clip => string.Equals(clip.name, name, StringComparison.OrdinalIgnoreCase))
            ?? clips.FirstOrDefault(clip => clip.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    static Transform FindExact(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase)) return node;
            if (string.Equals(node.name.Replace('_', '.'), name, StringComparison.OrdinalIgnoreCase)) return node;
            if (string.Equals(node.name.Replace('.', '_'), name, StringComparison.OrdinalIgnoreCase)) return node;
        }
        return null;
    }

    static float Depth(Transform camera, Transform node)
    {
        return camera.InverseTransformPoint(node.position).z;
    }

    static float MeanDepth(Transform camera, Transform meshNode)
    {
        var skinned = meshNode.GetComponent<SkinnedMeshRenderer>();
        var filter = meshNode.GetComponent<MeshFilter>();
        Mesh mesh = null;
        bool baked = false;
        Transform space = meshNode;
        if (skinned != null && skinned.sharedMesh != null)
        {
            mesh = new Mesh();
            skinned.BakeMesh(mesh);
            baked = true;
        }
        else if (filter != null) mesh = filter.sharedMesh;
        if (mesh == null) return -999f;
        var vertices = mesh.vertices;
        int step = Mathf.Max(1, vertices.Length / 3000);
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int index = 0; index < vertices.Length; index += step)
        {
            sum += camera.InverseTransformPoint(space.TransformPoint(vertices[index]));
            count++;
        }
        if (baked) UnityEngine.Object.DestroyImmediate(mesh);
        return count == 0 ? -999f : sum.z / count;
    }
}
