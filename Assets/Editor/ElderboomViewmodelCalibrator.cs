using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ElderboomViewmodelCalibrator
{
    const string ScenePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string ShotDir = "Logs/weapon_framing";

    [MenuItem("Elderboom HDRP/Import Tools/Recalibrate Weapon Framing")]
    static void RunFromMenu()
    {
        if (ElderboomEditorGuard.Confirm("Recalibrate weapon framing", "Recomputes and overwrites the stored hip/ADS framing of every weapon prefab.")) Run();
    }
    public static void Run()
    {
        Directory.CreateDirectory(ShotDir);
        if (SceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var report = new StringBuilder();
        report.AppendLine("weapon,mode,centerX,centerY,centerZ,sizeX,sizeY,sizeZ,notes");

        var player = UnityEngine.Object.FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null) throw new InvalidOperationException("FPS player missing");
        var camera = player.GetComponentInChildren<Camera>(true);
        if (camera == null) throw new InvalidOperationException("FPS camera missing");

        FrameOperator(camera.transform, "Soph_Viewmodel_Pivot", "AKMN ZENITCO", "SOPH", "Assets/GameAssets/Soph/Soph_AKMN_Plasma.glb", report);
        FrameOperator(camera.transform, "Eli_Viewmodel_Pivot", "SIG 550", "ELI", "Assets/GameAssets/Ported/Eli/Eli_Acoustic_Ignore.glb", report);
        FrameOperator(camera.transform, "Tactical_Viewmodel_Pivot", "STRIKER-12", "TACTICAL", "Assets/GameAssets/Ported/Tactical/Striker-12-shockwaveattack.glb", report);

        if (!AssetDatabase.IsValidFolder("Assets/Resources/Elderboom/Weapons"))
            AssetDatabase.CreateFolder("Assets/Resources/Elderboom", "Weapons");

        var staging = new GameObject("WeaponFramingStaging").transform;
        staging.SetParent(camera.transform, false);
        try
        {
            foreach (var definition in ElderboomWeapons.All.Where(w => !w.IsOperatorPrimary))
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(definition.asset);
                if (source == null) throw new InvalidOperationException("Missing " + definition.asset);
                var root = new GameObject("Weapon_" + definition.id);
                root.transform.SetParent(staging, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                if (model == null) model = UnityEngine.Object.Instantiate(source, root.transform);
                model.name = "Viewmodel";
                if (PrefabUtility.IsPartOfPrefabInstance(model))
                    PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                foreach (var embedded in model.GetComponentsInChildren<Camera>(true)) UnityEngine.Object.DestroyImmediate(embedded);
                foreach (var listener in model.GetComponentsInChildren<AudioListener>(true)) UnityEngine.Object.DestroyImmediate(listener);
                var clips = AssetDatabase.LoadAllAssetsAtPath(definition.asset).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();
                var idle = clips.FirstOrDefault(c => c.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0) ?? clips.FirstOrDefault();
                if (idle != null) idle.SampleAnimation(model, 0f);
                ElderboomWeaponFraming.HideNonViewmodelBodies(root.transform);
                ElderboomArsenalBuilder.ConvertMaterials(root, definition.id);
                foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 19;

                var driver = root.AddComponent<ElderboomViewmodel>();
                driver.model = model;
                driver.operatorId = definition.category;
                driver.idle = idle;
                driver.reload = clips.FirstOrDefault(c => c.name.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0);

                string notes = FrameWeapon(root.transform, model.transform, definition.name, definition.id, definition.category, idle);
                SetExclusiveViewmodel(camera.transform, root.transform);
                Capture(camera, definition.id + "_hip", false, root.transform);
                Capture(camera, definition.id + "_ads", true, root.transform);
                AppendBounds(report, definition.id, "hip", root.transform, notes);
                PrefabUtility.SaveAsPrefabAsset(root, "Assets/Resources/Elderboom/Weapons/" + definition.id + ".prefab");
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(staging.gameObject);
        }

        CaptureOperator(camera, "soph", player);
        CaptureOperator(camera, "eli", player);
        CaptureOperator(camera, "tactical", player);
        foreach (Transform child in camera.transform)
            if (child.GetComponent<Light>() != null || child.name == "Soph_Viewmodel_Pivot")
                child.gameObject.SetActive(true);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        File.WriteAllText(Path.Combine(ShotDir, "report.csv"), report.ToString());
        AssetDatabase.SaveAssets();
        Debug.Log("ELDERBOOM_WEAPON_FRAMING_DONE " + Path.GetFullPath(ShotDir));
    }

    static void FrameOperator(Transform camera, string pivotName, string weaponName, string operatorId, string assetPath, StringBuilder report)
    {
        var pivot = camera.Find(pivotName);
        if (pivot == null)
        {
            report.AppendLine(weaponName + ",missing,0,0,0,0,0,0,pivot missing");
            return;
        }
        pivot.gameObject.SetActive(true);
        var model = pivot.childCount > 0 ? pivot.GetChild(0) : pivot;
        var driver = pivot.GetComponent<ElderboomViewmodel>() ?? pivot.gameObject.AddComponent<ElderboomViewmodel>();
        driver.model = model.gameObject;
        driver.operatorId = operatorId;
        var clips = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>().ToArray();
        driver.idle = clips.FirstOrDefault(c => c.name == (operatorId == "ELI" ? "FPS_Idle_Breathing" : operatorId == "SOPH" ? "Idle_Breathing" : "shockwave"));
        pivot.localPosition = Vector3.zero;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;
        bool sampleIdle = driver.idle != null && driver.idle.name.IndexOf("shock", StringComparison.OrdinalIgnoreCase) < 0;
        if (sampleIdle) driver.idle.SampleAnimation(model.gameObject, 0f);
        ElderboomWeaponFraming.HideNonViewmodelBodies(pivot);
        string notes = FrameWeapon(pivot, model, weaponName, operatorId.ToLowerInvariant(), operatorId == "TACTICAL" ? "shotgun" : "assault_rifle", sampleIdle ? driver.idle : null);
        AppendBounds(report, weaponName, "hip", pivot, notes);
        pivot.gameObject.SetActive(operatorId == "SOPH");
    }

    static void AlignSophEyes(Transform model)
    {
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one * 0.72f;
        var eyes = ElderboomWeaponFraming.FindNamed(model, "Eyes");
        if (eyes == null) return;
        Vector3 eyeInModel = model.InverseTransformPoint(eyes.position);
        model.localPosition = -Vector3.Scale(eyeInModel, model.localScale);
    }

    static void AlignOperatorEyes(Transform model)
    {
        var eyes = ElderboomWeaponFraming.FindNamed(model, "Eyes")
            ?? ElderboomWeaponFraming.FindNamed(model, "Camera_FPS");
        if (eyes == null) return;
        model.localRotation = Quaternion.identity;
        Vector3 eyeInModel = model.InverseTransformPoint(eyes.position);
        model.localPosition = -eyeInModel;
        var bounds = RendererBounds(model.parent != null ? model.parent : model);
        if (bounds.center.z < 0.08f)
        {
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.localPosition = -(model.localRotation * eyeInModel);
        }
    }

    static string FrameWeapon(Transform root, Transform model, string weaponName, string id, string category, AnimationClip idle)
    {
        var notes = new List<string>();
        if (idle != null) idle.SampleAnimation(model.gameObject, 0f);
        bool soph = string.Equals(weaponName, "AKMN ZENITCO", StringComparison.OrdinalIgnoreCase) || id == "soph";
        if (soph) AlignSophEyes(model);
        else if (id == "eli" || id == "tactical") AlignOperatorEyes(model);
        ElderboomWeaponFraming.HideNonViewmodelBodies(root);

        Transform rear = ElderboomWeaponFraming.FindNamed(root, "Soph_ADS_Rear", "MainWeapon_ADS_Rear");
        Transform front = ElderboomWeaponFraming.FindNamed(root, "Soph_ADS_Front", "MainWeapon_ADS_Front");
        var pose = new ElderboomWeaponPose();

        if (soph)
        {
            var sophRear = ElderboomWeaponFraming.FindNamed(root, "Soph_ADS_Rear");
            if (sophRear != null) sophRear.localPosition = new Vector3(0f, 0.14176235f, 0.196367f);
            rear = sophRear;
            front = ElderboomWeaponFraming.FindNamed(root, "Soph_ADS_Front");
            pose.adsRelief = 0.16f;
            pose.hasAds = ElderboomWeaponFraming.SolveSightPose(root, rear, front, new Vector3(0f, 0f, 0.16f), out pose.adsPosition, out pose.adsRotation);
            notes.Add("soph-eyes");
        }
        else if (id == "eli" || id == "tactical" || string.Equals(weaponName, "SIG 550", StringComparison.OrdinalIgnoreCase) || string.Equals(weaponName, "STRIKER-12", StringComparison.OrdinalIgnoreCase))
        {
            if (ElderboomWeaponFraming.TryCalibration(weaponName, out var calibration) || ElderboomWeaponFraming.TryCalibration(id, out calibration))
            {
                var mesh = FindExact(root, calibration.mesh);
                if (mesh != null)
                {
                    rear = ElderboomWeaponFraming.EnsureMarker(mesh, "MainWeapon_ADS_Rear", calibration.rear);
                    front = ElderboomWeaponFraming.EnsureMarker(mesh, "MainWeapon_ADS_Front", calibration.front);
                    notes.Add("mesh:" + mesh.name);
                }
                else notes.Add("mesh-missing");
                pose.adsRelief = calibration.relief;
            }
            else pose.adsRelief = 0.18f;
            pose.hasAds = ElderboomWeaponFraming.SolveSightPose(root, rear, front, new Vector3(0f, 0f, pose.adsRelief > 0 ? pose.adsRelief : 0.18f), out pose.adsPosition, out pose.adsRotation);
            notes.Add("operator-eyes");
        }
        else if (ElderboomWeaponFraming.TryCalibration(weaponName, out var sidearm) || ElderboomWeaponFraming.TryCalibration(id, out sidearm))
        {
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            ApplyGodotHold(model, sidearm.hip, Quaternion.Euler(0f, 180f, 0f), 1f);
            PlaceInView(model, category == "handgun" ? new Vector3(0.1f, -0.16f, 0.34f) : new Vector3(0.12f, -0.18f, 0.4f));
            var mesh = FindExact(root, sidearm.mesh);
            if (mesh != null)
            {
                rear = ElderboomWeaponFraming.EnsureMarker(mesh, "MainWeapon_ADS_Rear", sidearm.rear);
                front = ElderboomWeaponFraming.EnsureMarker(mesh, "MainWeapon_ADS_Front", sidearm.front);
            }
            else notes.Add("mesh-missing:" + sidearm.mesh);
            pose.adsRelief = sidearm.relief;
            pose.hasAds = ElderboomWeaponFraming.SolveSightPose(root, rear, front, new Vector3(0f, 0f, sidearm.relief), out pose.adsPosition, out pose.adsRotation);
            notes.Add("sidearm-godot");
        }
        else
        {
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            Vector3 godot = ShopGodotPosition(id, category);
            Vector3 euler = id == "wrench" ? new Vector3(8f, 180f, -12f) : new Vector3(0f, 180f, 0f);
            ApplyGodotHold(model, new Vector3(godot.x, godot.y, -godot.z), Quaternion.Euler(euler), ShopGodotScale(id, category));
            PlaceInView(model, category == "handgun" ? new Vector3(0.1f, -0.16f, 0.34f) : category == "melee" ? new Vector3(0.16f, -0.18f, 0.36f) : new Vector3(0.1f, -0.18f, 0.4f));
            pose.hasAds = false;
            notes.Add("godot-shop");
        }

        pose.hipPosition = root.localPosition;
        pose.hipRotation = root.localRotation;
        if (!pose.hasAds)
        {
            pose.adsPosition = pose.hipPosition;
            pose.adsRotation = pose.hipRotation;
        }
        var driver = root.GetComponent<ElderboomViewmodel>();
        ElderboomWeaponFraming.Store(driver, pose);
        root.localPosition = pose.hipPosition;
        root.localRotation = pose.hipRotation;
        return string.Join("|", notes);
    }

    static Transform FindExact(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase)) return node;
            if (string.Equals(node.name.Replace('_', '.'), name, StringComparison.OrdinalIgnoreCase)) return node;
            if (string.Equals(node.name.Replace('.', '_'), name, StringComparison.OrdinalIgnoreCase)) return node;
        }
        return null;
    }

    static void PlaceInView(Transform model, Vector3 targetCenter)
    {
        var root = model.parent != null ? model.parent : model;
        var bounds = RendererBounds(root);
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest < 0.05f)
        {
            model.localScale *= 20f;
            bounds = RendererBounds(root);
            longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        }
        if (longest > 1.15f) model.localScale *= 0.82f / longest;
        else if (longest > 1e-5f && longest < 0.2f) model.localScale *= 0.42f / longest;
        bounds = RendererBounds(root);
        model.localPosition += targetCenter - bounds.center;
    }

    static void ApplyGodotHold(Transform model, Vector3 unityPosition, Quaternion rotation, float scale)
    {
        model.localPosition = unityPosition;
        model.localRotation = rotation;
        model.localScale = Vector3.one * scale;
    }

    static Vector3 ShopGodotPosition(string id, string category)
    {
        if (id == "wrench") return new Vector3(0.22f, -0.22f, -0.13f);
        if (id == "crowbar") return new Vector3(0.06f, -0.25f, -0.13f);
        if (category == "handgun" || id == "service") return new Vector3(0f, -0.78f, -0.15f);
        if (category == "melee") return new Vector3(0.20f, -0.25f, -0.13f);
        return new Vector3(0.06f, -0.25f, -0.13f);
    }

    static float ShopGodotScale(string id, string category)
    {
        if (id == "wrench") return 0.40f;
        if (id == "service" || category == "handgun") return 0.45f;
        if (category == "melee" && id != "crowbar") return 0.44f;
        return 0.68f;
    }

    static void AutoOrient(Transform model)
    {
        Quaternion rest = model.localRotation;
        Quaternion best = rest;
        float bestScore = float.NegativeInfinity;
        foreach (float yaw in new[] { 0f, 180f, 90f, -90f })
        {
            model.localRotation = Quaternion.Euler(0f, yaw, 0f) * rest;
            var bounds = RendererBounds(model.parent != null ? model.parent : model);
            float score = bounds.center.z * 2f - Mathf.Abs(bounds.center.x) - Mathf.Abs(bounds.center.y) - (bounds.min.z < 0.02f ? 12f : 0f);
            if (score > bestScore) { bestScore = score; best = model.localRotation; }
        }
        model.localRotation = best;
    }

    static void NormalizeModelScale(Transform model, float targetLongest)
    {
        var bounds = RendererBounds(model.parent != null ? model.parent : model);
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest < 1e-5f)
        {
            model.localScale *= 25f;
            bounds = RendererBounds(model.parent != null ? model.parent : model);
            longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        }
        if (longest < 1e-5f) return;
        model.localScale *= targetLongest / longest;
    }

    static void FitBounds(Transform root, Vector3 targetCenter)
    {
        var bounds = RendererBounds(root);
        if (bounds.size.sqrMagnitude < 1e-8f) return;
        float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (longest > 0.95f)
        {
            root.localScale *= 0.72f / longest;
            bounds = RendererBounds(root);
        }
        root.localPosition += targetCenter - bounds.center;
        bounds = RendererBounds(root);
        if (bounds.min.z < 0.06f)
            root.localPosition += Vector3.forward * (0.08f - bounds.min.z);
        if (bounds.center.y > 0.02f)
            root.localPosition += Vector3.down * (bounds.center.y + 0.12f);
    }

    static void SetExclusiveViewmodel(Transform camera, Transform visible)
    {
        foreach (Transform child in camera)
        {
            if (child.GetComponent<Light>() != null) continue;
            bool show = visible != null && (child == visible || visible.IsChildOf(child));
            child.gameObject.SetActive(show);
        }
    }

    static Bounds RendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        if (renderers.Length == 0) return new Bounds(root.position, Vector3.zero);
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        if (root.parent != null)
        {
            Vector3 min = root.parent.InverseTransformPoint(bounds.min);
            Vector3 max = root.parent.InverseTransformPoint(bounds.max);
            var local = new Bounds();
            local.SetMinMax(Vector3.Min(min, max), Vector3.Max(min, max));
            return local;
        }
        return bounds;
    }

    static void AppendBounds(StringBuilder report, string name, string mode, Transform root, string notes)
    {
        var bounds = RendererBounds(root);
        report.AppendLine($"{name},{mode},{bounds.center.x:F3},{bounds.center.y:F3},{bounds.center.z:F3},{bounds.size.x:F3},{bounds.size.y:F3},{bounds.size.z:F3},{notes}");
    }

    static void CaptureOperator(Camera camera, string id, HDRPSlicePlayer player)
    {
        string pivotName = id == "eli" ? "Eli_Viewmodel_Pivot" : id == "tactical" ? "Tactical_Viewmodel_Pivot" : "Soph_Viewmodel_Pivot";
        var selected = camera.transform.Find(pivotName);
        if (selected == null) return;
        SetExclusiveViewmodel(camera.transform, selected);
        Capture(camera, id + "_hip", false, selected);
        Capture(camera, id + "_ads", true, selected);
        selected.localPosition = selected.GetComponent<ElderboomViewmodel>()?.hipPosition ?? selected.localPosition;
        selected.localRotation = selected.GetComponent<ElderboomViewmodel>()?.hipRotation ?? selected.localRotation;
        if (id != "soph") selected.gameObject.SetActive(false);
        var soph = camera.transform.Find("Soph_Viewmodel_Pivot");
        if (soph != null) soph.gameObject.SetActive(true);
    }

    static void Capture(Camera camera, string name, bool ads, Transform root)
    {
        var viewmodel = root.GetComponent<ElderboomViewmodel>();
        if (viewmodel != null && viewmodel.framingStored)
        {
            root.localPosition = ads && viewmodel.hasAds ? viewmodel.adsPosition : viewmodel.hipPosition;
            root.localRotation = ads && viewmodel.hasAds ? viewmodel.adsRotation : viewmodel.hipRotation;
        }
        var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        var previous = camera.targetTexture;
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), image.EncodeToPNG());
        camera.targetTexture = previous;
        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(rt);
        UnityEngine.Object.DestroyImmediate(image);
        if (viewmodel != null && viewmodel.framingStored)
        {
            root.localPosition = viewmodel.hipPosition;
            root.localRotation = viewmodel.hipRotation;
        }
    }
}
