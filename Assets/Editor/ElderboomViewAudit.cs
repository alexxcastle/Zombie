using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

public static class ElderboomViewAudit
{
    const string GamePath = "Assets/Scenes/Elderboom_Unity_Port.unity";
    const string ScenePath = "Assets/Scenes/ViewAudit.unity";
    const string OutDir = "Logs/view_audit";

    public static void PoseSweep()
    {
        Directory.CreateDirectory(OutDir);
        if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) File.Delete(Path.Combine(OutDir, "play_done.txt"));
        try
        {
            var game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
            var eli = Find(game, "Eli_Viewmodel_Pivot");
            var tactical = Find(game, "Tactical_Viewmodel_Pivot");
            var tacticalView = tactical.GetComponent<ElderboomViewmodel>();
            tacticalView.adsPosition = new Vector3(-0.2373f, 0.2791f, -0.3001f);
            tacticalView.adsRotation = new Quaternion(0.0699f, 0.0694f, -0.0022f, 0.9951f);
            tacticalView.adsRelief = 0.16f;
            tacticalView.hasAds = true;
            tacticalView.framingStored = true;
            EditorSceneManager.MarkSceneDirty(game);
            EditorSceneManager.SaveScene(game);
            var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(preview);
            var camera = BuildCamera();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            Copy(eli, camera.transform);
            Copy(tactical, camera.transform);
            EditorSceneManager.CloseScene(game, true);
            EditorSceneManager.SaveScene(preview, ScenePath);
            var rig = camera.GetComponent<ElderboomViewAuditRig>();
            rig.mode = "sweep";
            rig.captureOnPlay = true;
            double deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += () =>
            {
                if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) return;
                if (EditorApplication.timeSinceStartup < deadline) return;
                File.AppendAllText(Path.Combine(OutDir, "sweep.txt"), "\nSWEEP TIMEOUT\n");
                EditorApplication.Exit(2);
            };
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }
        catch (System.Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir, "sweep.txt"), ex.ToString());
            Debug.LogError(ex);
            EditorApplication.Exit(1);
        }
    }

    public static void FixAndRecheck()
    {
        Directory.CreateDirectory(OutDir);
        if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) File.Delete(Path.Combine(OutDir, "play_done.txt"));
        var notes = new System.Text.StringBuilder();
        try
        {
            FixScene(notes);
            FixPrefab("Assets/Resources/Elderboom/Weapons/rhino.prefab", 0.34f, notes);
            File.WriteAllText(Path.Combine(OutDir, "fix.txt"), notes.ToString());
            AssetDatabase.SaveAssets();
            var game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
            var eli = Find(game, "Eli_Viewmodel_Pivot");
            var tactical = Find(game, "Tactical_Viewmodel_Pivot");
            var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(preview);
            var camera = BuildCamera();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            Copy(eli, camera.transform);
            Copy(tactical, camera.transform);
            EditorSceneManager.CloseScene(game, true);
            EditorSceneManager.SaveScene(preview, ScenePath);
            var rig = camera.GetComponent<ElderboomViewAuditRig>();
            rig.mode = "recheck";
            rig.captureOnPlay = true;
            double deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.update += () =>
            {
                if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) return;
                if (EditorApplication.timeSinceStartup < deadline) return;
                File.AppendAllText(Path.Combine(OutDir, "recheck.txt"), "\nRECHECK TIMEOUT\n");
                EditorApplication.Exit(2);
            };
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }
        catch (System.Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir, "fix.txt"), notes + "\n" + ex);
            Debug.LogError(ex);
            EditorApplication.Exit(1);
        }
    }

    static void FixScene(System.Text.StringBuilder notes)
    {
        var game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
        var camera = Object.FindAnyObjectByType<HDRPSlicePlayer>().GetComponentInChildren<Camera>(true);
        var eli = Find(game, "Eli_Viewmodel_Pivot");
        var tactical = Find(game, "Tactical_Viewmodel_Pivot");
        TurnModelForward(camera, eli, notes);
        SolveStoredAds(camera, eli, 0.36f, notes);
        SolveStoredAds(camera, tactical, 0.42f, notes);
        EditorSceneManager.MarkSceneDirty(game);
        EditorSceneManager.SaveScene(game);
    }

    static void FixPrefab(string path, float relief, System.Text.StringBuilder notes)
    {
        var cameraObject = new GameObject("FramingCamera");
        var camera = cameraObject.AddComponent<Camera>();
        var root = PrefabUtility.LoadPrefabContents(path);
        var probe = Object.Instantiate(root, camera.transform, false);
        SolveStoredAds(camera, probe.transform, relief, notes);
        var saved = root.GetComponent<ElderboomViewmodel>();
        var solved = probe.GetComponent<ElderboomViewmodel>();
        if (saved != null && solved != null)
        {
            saved.adsPosition = solved.adsPosition;
            saved.adsRotation = solved.adsRotation;
            saved.adsRelief = solved.adsRelief;
            saved.hasAds = solved.hasAds;
            saved.framingStored = true;
        }
        Object.DestroyImmediate(probe);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
        Object.DestroyImmediate(cameraObject);
    }

    static void TurnModelForward(Camera camera, Transform pivot, System.Text.StringBuilder notes)
    {
        var view = pivot.GetComponent<ElderboomViewmodel>();
        if (view == null || view.model == null) return;
        pivot.gameObject.SetActive(true);
        float depth = CenterDepth(camera, view.model);
        notes.AppendLine(pivot.name + " model depth before=" + depth.ToString("0.00"));
        if (depth >= 0.05f) return;
        var model = view.model.transform;
        var turn = Quaternion.Euler(0f, 180f, 0f);
        model.localRotation = turn * model.localRotation;
        model.localPosition = turn * model.localPosition;
        notes.AppendLine(pivot.name + " model turned 180, depth=" + CenterDepth(camera, view.model).ToString("0.00"));
    }

    static void SolveStoredAds(Camera camera, Transform pivot, float relief, System.Text.StringBuilder notes)
    {
        var view = pivot.GetComponent<ElderboomViewmodel>();
        if (view == null) return;
        pivot.gameObject.SetActive(true);
        pivot.localPosition = view.hipPosition;
        pivot.localRotation = view.hipRotation;
        Transform rear = null, front = null;
        foreach (var node in pivot.GetComponentsInChildren<Transform>(true))
        {
            if (node.name == "MainWeapon_ADS_Rear" || node.name == "Soph_ADS_Rear") rear = node;
            if (node.name == "MainWeapon_ADS_Front" || node.name == "Soph_ADS_Front") front = node;
        }
        if (rear == null) { notes.AppendLine(pivot.name + " has no rear sight"); return; }
        if (!ElderboomWeaponFraming.SolveSightPose(pivot, rear, front, new Vector3(0f, 0f, relief), out var position, out var rotation))
            return;
        pivot.localPosition = position;
        pivot.localRotation = rotation;
        if (front != null && CenterDepth(camera, front.gameObject) < CenterDepth(camera, rear.gameObject))
        {
            ElderboomWeaponFraming.SolveSightPose(pivot, front, rear, new Vector3(0f, 0f, relief), out position, out rotation);
            pivot.localPosition = position;
            pivot.localRotation = rotation;
            notes.AppendLine(pivot.name + " sight axis swapped");
        }
        view.adsPosition = position;
        view.adsRotation = rotation;
        view.adsRelief = relief;
        view.hasAds = true;
        view.framingStored = true;
        notes.AppendLine(pivot.name + " ads=" + position.ToString("F3") + " rearZ=" + CenterDepth(camera, rear.gameObject).ToString("0.00")
            + " frontZ=" + (front != null ? CenterDepth(camera, front.gameObject).ToString("0.00") : "none"));
        pivot.localPosition = view.hipPosition;
        pivot.localRotation = view.hipRotation;
    }

    static float CenterDepth(Camera camera, GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return camera.transform.InverseTransformPoint(root.transform.position).z;
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return camera.transform.InverseTransformPoint(bounds.center).z;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutDir);
        if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) File.Delete(Path.Combine(OutDir, "play_done.txt"));
        try
        {
            var game = EditorSceneManager.OpenScene(GamePath, OpenSceneMode.Single);
            var soph = Find(game, "Soph_Viewmodel_Pivot");
            var eli = Find(game, "Eli_Viewmodel_Pivot");
            var tactical = Find(game, "Tactical_Viewmodel_Pivot");
            if (soph == null || eli == null || tactical == null)
                throw new System.InvalidOperationException("Operator viewmodel missing");
            var preview = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SetActiveScene(preview);
            var camera = BuildCamera();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            Copy(soph, camera.transform);
            Copy(eli, camera.transform);
            Copy(tactical, camera.transform);
            PlaceAbility(camera.transform, "Assets/Resources/Elderboom/Abilities/LiftSmash.prefab");
            PlaceAbility(camera.transform, "Assets/Resources/Elderboom/Abilities/KeepAway.prefab");
            EditorSceneManager.CloseScene(game, true);
            EditorSceneManager.SaveScene(preview, ScenePath);
            var rig = camera.GetComponent<ElderboomViewAuditRig>();
            rig.captureOnPlay = true;
            double deadline = EditorApplication.timeSinceStartup + 240;
            EditorApplication.update += () =>
            {
                if (File.Exists(Path.Combine(OutDir, "play_done.txt"))) return;
                if (EditorApplication.timeSinceStartup < deadline) return;
                File.AppendAllText(Path.Combine(OutDir, "report.txt"), "\nAUDIT TIMEOUT\n");
                EditorApplication.Exit(2);
            };
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }
        catch (System.Exception ex)
        {
            File.WriteAllText(Path.Combine(OutDir, "report.txt"), ex.ToString());
            Debug.LogError(ex);
            EditorApplication.Exit(1);
        }
    }

    static Transform Find(UnityEngine.SceneManagement.Scene scene, string name)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (transform.name == name) return transform;
        return null;
    }

    static void Copy(Transform source, Transform parent)
    {
        var copy = Object.Instantiate(source.gameObject, parent, false);
        copy.name = source.name;
        copy.SetActive(false);
    }

    static void PlaceAbility(Transform parent, string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.SetActive(false);
    }

    static Camera BuildCamera()
    {
        var go = new GameObject("ViewAuditCamera");
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
        go.AddComponent<ElderboomViewAuditRig>();

        var night = new GameObject("Global Volume");
        var volume = night.AddComponent<Volume>();
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

        var fill = new GameObject("Viewmodel Fill");
        fill.transform.SetParent(go.transform, false);
        fill.transform.localPosition = new Vector3(0.12f, 0.08f, 0.35f);
        var spot = fill.AddComponent<Light>();
        spot.type = LightType.Spot;
        spot.color = new Color(1f, 0.9f, 0.82f);
        spot.intensity = 180f;
        spot.range = 6f;
        spot.spotAngle = 90f;
        spot.shadows = LightShadows.None;
        fill.AddComponent<HDAdditionalLightData>();
        return camera;
    }
}
