using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ElderboomTestRangeBuild
{
    const string Scene = "Assets/Scenes/Elderboom_TestRange.unity";
    const string App = "Builds/ElderboomTestRange.app";
    const string Model = "Assets/GameAssets/MP5/MP5_FPS.glb";
    const string Prefab = "Assets/Resources/Elderboom/Weapons/mp5.prefab";
    const string GripDonor = "Assets/Resources/Elderboom/Weapons/hk53.prefab";

    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        int output = Array.IndexOf(args, "-elderboom-range-output");
        if (output == args.Length - 1)
            throw new ArgumentException("-elderboom-range-output requires an app path");
        string app = Path.GetFullPath(output < 0 ? App : args[output + 1]);
        if (File.Exists(app) || Directory.Exists(app))
            throw new IOException("Choose a fresh output path with -elderboom-range-output: " + app);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(Prefab) == null)
            BuildMP5Prefab();

        Directory.CreateDirectory(Path.GetDirectoryName(app));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { Scene },
            locationPathName = app,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.CompressWithLz4
        });
        var summary = report.summary;
        if (summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Test range build {summary.result} with {summary.totalErrors} errors");
        Debug.Log($"TEST_RANGE_BUILD_READY {app} ({summary.totalSize} bytes, {summary.totalTime.TotalSeconds:F1}s)");
    }

    public static void BuildMP5Prefab()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        var donorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(GripDonor);
        if (source == null || donorAsset == null)
            throw new InvalidOperationException("The MP5 model and HK53 grip reference must be imported");

        var root = new GameObject("Weapon_mp5");
        var donor = Object.Instantiate(donorAsset);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            var muzzle = Find(model, "Muzzle");
            var rear = Find(model, "MainWeapon_ADS_Rear");
            var front = Find(model, "MainWeapon_ADS_Front");
            if (Vector3.Dot(front.position - rear.position, Vector3.forward) < 0)
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            muzzle.name = "MainWeapon_Muzzle";
            muzzle.rotation = Quaternion.identity;
            var rearLocal = muzzle.InverseTransformPoint(rear.position);
            var frontLocal = muzzle.InverseTransformPoint(front.position);
            if (Vector3.Distance(rearLocal, new Vector3(0f, .0485f, -.393f)) > .001f ||
                Vector3.Distance(frontLocal, new Vector3(0f, .051f, -.0448f)) > .001f)
                throw new InvalidOperationException($"MP5 sight calibration mismatch: {rearLocal:F4}, {frontLocal:F4}");

            var donorView = donor.GetComponentInChildren<ElderboomViewmodel>(true);
            ElderboomAnimationSampling.Sample(donorView.idle, donorView.model, 0f);
            var donorAxis = Find(donor, "MainWeapon_ADS_Front").position - Find(donor, "MainWeapon_ADS_Rear").position;
            var align = Quaternion.FromToRotation(Vector3.ProjectOnPlane(donorAxis, Vector3.up), Vector3.forward);
            var donorTrigger = Find(donor, "Trigger").GetComponent<Renderer>().bounds.center;
            var mp5Trigger = Find(model, "G_Trigger").GetComponent<Renderer>().bounds.center;
            var donorHandguard = Find(donor, "Handguard").GetComponent<Renderer>().bounds;
            var support = Find(model, "LeftHand_Support");
            CopyGripFrame(donor, Find(model, "RightHand_Grip"), "R", donorTrigger, mp5Trigger, align);
            CopyGripFrame(donor, support, "L", donorHandguard.center - Vector3.up * donorHandguard.extents.y, support.position, align);

            var flash = new GameObject("ShotFlash").AddComponent<Light>();
            flash.transform.SetParent(root.transform, false);
            flash.transform.position = muzzle.position;
            flash.type = LightType.Point;
            flash.color = new Color(1f, .63f, .22f);
            flash.intensity = 2f;
            flash.range = 3f;
            flash.enabled = false;

            foreach (var node in root.GetComponentsInChildren<Transform>(true))
                node.gameObject.layer = 19;
            var renderers = model.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length != 12 || renderers.Select(r => r.sharedMaterial).Distinct().Count() != 5)
                throw new InvalidOperationException("Expected twelve MP5 parts and five imported materials");
            if (PrefabUtility.SaveAsPrefabAsset(root, Prefab) == null)
                throw new IOException("Could not save " + Prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"MP5_PREFAB_READY rear={rearLocal:F4} front={frontLocal:F4} " +
                $"materials={string.Join(", ", renderers.Select(r => r.sharedMaterial.shader.name).Distinct())}");
        }
        finally
        {
            Object.DestroyImmediate(donor);
            Object.DestroyImmediate(root);
        }
    }

    static Transform Find(GameObject root, string name) =>
        ElderboomWeaponFraming.FindNamed(root.transform, name) ??
        throw new InvalidOperationException(root.name + " missing " + name);

    // Transfer the wrist and knuckle frame, anchored to the new trigger or foregrip.
    static void CopyGripFrame(GameObject donor, Transform marker, string side, Vector3 from, Vector3 to, Quaternion align)
    {
        foreach (var (joint, point) in new[]
        {
            ("DEF-hand", "wrist"), ("DEF-f_index.01", "point1"),
            ("DEF-f_middle.01", "middle1"), ("DEF-f_pinky.01", "pink1")
        })
        {
            var bone = Find(donor, joint + "." + side);
            var grip = new GameObject(side + "_" + point).transform;
            grip.SetParent(marker, false);
            grip.position = to + align * (bone.position - from);
        }
    }
}
