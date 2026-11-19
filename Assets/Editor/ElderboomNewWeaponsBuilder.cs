using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Glock 19, MP-133 and MP7 (Sketchfab, CC BY 4.0 - see References/Weapons/CREDITS.txt).
// Builds Resources/Elderboom/Weapons/<id>.prefab from GameAssets/<Name>/<Name>_FPS.glb like the MP5:
// the hand frames (wrist + index/middle/pinky knuckles) are copied from a donor weapon pack, anchored on
// matching points of the two weapons, and the sight line is written to GripSights.json.
public static class ElderboomNewWeaponsBuilder
{
    class Spec
    {
        public string id, model, donor;
        public string donorRight, modelRight, donorLeft, modelLeft; // anchors: "node" or "node:bottom"
    }

    static readonly Spec[] Specs =
    {
        new Spec { id = "glock19", model = "Assets/GameAssets/Glock19/Glock19_FPS.glb", donor = "m1911",
            donorRight = "MainWeapon_ADS_Rear", modelRight = "MainWeapon_ADS_Rear", donorLeft = "MainWeapon_ADS_Rear", modelLeft = "MainWeapon_ADS_Rear" },
        new Spec { id = "mp133", model = "Assets/GameAssets/MP133/MP133_FPS.glb", donor = "shotgun",
            donorRight = "trigger", modelRight = "G_Trigger", donorLeft = "pump", modelLeft = "LeftHand_Support" },
        new Spec { id = "mp7", model = "Assets/GameAssets/MP7/MP7_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
    };

    // CuiGames A5 Carbine and K40 Battle Rifle (see Assets/GameAssets/CuiGames/CuiGames_LICENSE.txt). The _FPS.glb files are
    // the shipped GLBs plus the marker nodes (Muzzle, sights, TriggerPoint, hand points) added in glTF space.
    static readonly Spec[] CuiSpecs =
    {
        new Spec { id = "a5", model = "Assets/GameAssets/CuiGames/A5/A5_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
        new Spec { id = "k40", model = "Assets/GameAssets/CuiGames/K40/K40_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
    };

    // Meshy community kits (CC0), assembled from their parts in Blender: AK-74, Mosin-Nagant, MP5 (Vanguard Kinetics),
    // .45 pistol (Obsidian Arms). Alternative parts are in the model, named *_alt, and hidden here; ElderboomWeaponMods
    // shows them when they are chosen in the loadout menu.
    static readonly Spec[] MeshySpecs =
    {
        new Spec { id = "ak74", model = "Assets/GameAssets/Meshy/AK74/AK74_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
        new Spec { id = "mosin", model = "Assets/GameAssets/Meshy/Mosin/Mosin_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
        new Spec { id = "mp5vk", model = "Assets/GameAssets/Meshy/MP5VK/MP5VK_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
        new Spec { id = "p45", model = "Assets/GameAssets/Meshy/P45/P45_FPS.glb", donor = "m1911",
            donorRight = "MainWeapon_ADS_Rear", modelRight = "MainWeapon_ADS_Rear", donorLeft = "MainWeapon_ADS_Rear", modelLeft = "MainWeapon_ADS_Rear" },
    };

    // M4 carbine (GameAssets/M4_v7/M4_v7_FPS.glb). The V7 mesh is baked into the same glTF space as the previous
    // viewmodel: Y up, muzzle -Z, bore on Y = 0. The birdcage and the rear sight stay separate nodes so the loadout
    // can hide them. M4_v4 and M4_v6 stay on disk.
    static readonly Spec[] M4Specs =
    {
        new Spec { id = "m4", model = "Assets/GameAssets/M4_v7/M4_v7_FPS.glb", donor = "hk53",
            donorRight = "Trigger", modelRight = "TriggerPoint", donorLeft = "Handguard:bottom", modelLeft = "LeftHand_Support" },
    };

    [MenuItem("Elderboom/Build M16A4")]
    public static void BuildM4()
    {
        AssetDatabase.Refresh();
        var sights = new List<string>();
        foreach (var s in M4Specs) sights.Add(Build(s));
        WriteSights(sights);
        AssetDatabase.SaveAssets();
    }

    // Batch: -executeMethod ElderboomNewWeaponsBuilder.BuildM4Batch (result in Logs/m4_build.txt)
    public static void BuildM4Batch()
    {
        try
        {
            BuildM4();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/m4.prefab");
            File.WriteAllText("Logs/m4_build.txt", "OK prefab=" + (prefab != null) + " renderers=" + (prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).Length : 0)
                + " def=" + (ElderboomWeapons.Find("m4") != null) + " platform=" + (ElderboomWeaponMods.Find("m4") != null));
        }
        catch (Exception e) { File.WriteAllText("Logs/m4_build.txt", "FAIL " + e); EditorApplication.Exit(1); }
    }

    // Batch: rebuild the M4 prefab from M4_v7, then the macOS player. Result in Logs/m4_build.txt and Logs/claude_build_result.json.
    public static void BuildM4ThenMacBatch()
    {
        try
        {
            BuildM4();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/m4.prefab");
            int renderers = prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).Length : 0;
            File.WriteAllText("Logs/m4_build.txt", "OK prefab=" + (prefab != null) + " renderers=" + renderers
                + " def=" + (ElderboomWeapons.Find("m4") != null) + " platform=" + (ElderboomWeaponMods.Find("m4") != null)
                + " model=" + M4Specs[0].model);
            if (prefab == null || renderers == 0) { EditorApplication.Exit(1); return; }
        }
        catch (Exception e) { File.WriteAllText("Logs/m4_build.txt", "FAIL " + e); EditorApplication.Exit(1); return; }
        ElderboomClaudeBuild.BuildMacBatch();
    }

    [MenuItem("Elderboom/Build Meshy Weapons (AK74, Mosin, MP5 VK, .45)")]
    public static void BuildMeshy()
    {
        AssetDatabase.Refresh();
        var sights = new List<string>();
        foreach (var s in MeshySpecs) sights.Add(Build(s));
        WriteSights(sights);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Elderboom/Build CuiGames Weapons (A5, K40)")]
    public static void BuildCuiGames()
    {
        AssetDatabase.Refresh();
        var sights = new List<string>();
        foreach (var s in CuiSpecs) sights.Add(Build(s));
        WriteSights(sights);
        AssetDatabase.SaveAssets();
    }

    // Logs/claude_cuigames.txt -> BuildCuiGames(), result in Logs/claude_cuigames_result.txt.
    static double nextPoll;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return; nextPoll = EditorApplication.timeSinceStartup + 2;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        // Logs/claude_m4.txt -> BuildM4(), result in Logs/claude_m4_result.txt.
        const string M4Flag = "Logs/claude_m4.txt", M4Result = "Logs/claude_m4_result.txt";
        if (File.Exists(M4Flag))
        {
            File.Delete(M4Flag);
            try
            {
                BuildM4();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/m4.prefab");
                var sb = new System.Text.StringBuilder("OK\n");
                sb.AppendLine("model=" + M4Specs[0].model + " prefab=" + (prefab != null) + " def=" + (ElderboomWeapons.Find("m4") != null)
                    + " platform=" + (ElderboomWeaponMods.Find("m4") != null));
                if (prefab != null)
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                        sb.AppendLine("renderer " + r.name + " active=" + r.gameObject.activeSelf + " material=" + (r.sharedMaterial != null ? r.sharedMaterial.name + " / " + r.sharedMaterial.shader.name : "none")
                            + " size=" + r.bounds.size.ToString("F3"));
                sb.AppendLine("catalog " + string.Join(", ", ElderboomWeapons.All.Select(w => w.id)));
                File.WriteAllText(M4Result, sb.ToString());
            }
            catch (Exception e) { File.WriteAllText(M4Result, "FAIL " + e); }
            return;
        }
        const string MFlag = "Logs/claude_meshy.txt", MResult = "Logs/claude_meshy_result.txt";
        if (File.Exists(MFlag))
        {
            File.Delete(MFlag);
            try
            {
                BuildMeshy();
                var sb = new System.Text.StringBuilder("OK\n");
                foreach (var s in MeshySpecs)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/" + s.id + ".prefab");
                    sb.AppendLine(s.id + " prefab=" + (prefab != null) + " renderers=" + (prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).Length : 0)
                        + " def=" + (ElderboomWeapons.Find(s.id) != null));
                }
                foreach (var id in new[] { "VKHOLO", "OARDS", "SOLAR", "TBL", "PLIGHT", "ANGLED", "RV1", "VGRIP" })
                    sb.AppendLine("attachment " + id + " " + (Resources.Load<GameObject>("Elderboom/Attachments/Meshy/" + id) != null));
                File.WriteAllText(MResult, sb.ToString());
            }
            catch (Exception e) { File.WriteAllText(MResult, "FAIL " + e); }
            return;
        }
        const string Flag = "Logs/claude_cuigames.txt", Result = "Logs/claude_cuigames_result.txt";
        if (!File.Exists(Flag)) return;
        File.Delete(Flag);
        try
        {
            BuildCuiGames();
            var sb = new System.Text.StringBuilder("OK\n");
            foreach (var s in CuiSpecs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/" + s.id + ".prefab");
                sb.AppendLine(s.id + " prefab=" + (prefab != null) + " renderers=" + (prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).Length : 0));
            }
            foreach (var path in AssetDatabase.FindAssets("t:GameObject", new[] { "Assets/Resources/Elderboom/Attachments" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(path));
                sb.AppendLine("attachment " + AssetDatabase.GUIDToAssetPath(path) + " renderers=" + (go != null ? go.GetComponentsInChildren<Renderer>(true).Length : -1));
            }
            File.WriteAllText(Result, sb.ToString());
        }
        catch (Exception e) { File.WriteAllText(Result, "FAIL " + e); }
    }

    static readonly string[][] Right =
    {
        new[] { "R_wrist", "DEF-hand.R", "DEF_hand.R", "Hand.R", "hand.R", "RightHand" },
        new[] { "R_point1", "DEF-f_index.01.R.01", "DEF-f_index.01.R", "DEF_index_01.R", "Bone.023.R", "point1.R", "RightHandIndex1" },
        new[] { "R_middle1", "DEF-f_middle.01.R.01", "DEF-f_middle.01.R", "DEF_middle_01.R", "Middle_Finger_1.R", "middle1.R", "RightHandMiddle1" },
        new[] { "R_pink1", "DEF-f_pinky.01.R.01", "DEF-f_pinky.01.R", "DEF_little_01.R", "Pinky1.R", "pink1.R", "RightHandPinky1" },
    };
    static readonly string[][] Left =
    {
        new[] { "L_wrist", "DEF-hand.L", "DEF_hand.L", "Hand.L", "hand.L", "LeftHand" },
        new[] { "L_point1", "DEF-f_index.01.L.01", "DEF-f_index.01.L", "DEF_index_01.L", "Bone.023.L", "point1.L", "LeftHandIndex1" },
        new[] { "L_middle1", "DEF-f_middle.01.L.01", "DEF-f_middle.01.L", "DEF_middle_01.L", "Middle_Finger_1.L", "middle1.L", "LeftHandMiddle1" },
        new[] { "L_pink1", "DEF-f_pinky.01.L.01", "DEF-f_pinky.01.L", "DEF_little_01.L", "Pinky1.L", "pink1.L", "LeftHandPinky1" },
    };
    static readonly string[] Points = { "wrist", "point1", "middle1", "pink1" };

    [MenuItem("Elderboom/Build New Weapons (Glock19, MP133, MP7)")]
    public static void BuildAll()
    {
        AssetDatabase.Refresh();
        var sights = new List<string>();
        foreach (var s in Specs) sights.Add(Build(s));
        WriteSights(sights);
        AssetDatabase.SaveAssets();
    }

    static Transform Exact(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static Vector3 Anchor(GameObject root, string spec)
    {
        string[] p = spec.Split(':');
        var t = Exact(root.transform, p[0]) ?? throw new InvalidOperationException(root.name + " missing " + p[0]);
        var r = t.GetComponent<Renderer>();
        if (r == null) return t.position;
        var b = r.bounds;
        return p.Length > 1 && p[1] == "bottom" ? b.center - Vector3.up * b.extents.y : b.center;
    }

    static Transform Joint(GameObject root, string[] names)
    {
        foreach (var n in names) { var t = Exact(root.transform, n); if (t != null) return t; }
        foreach (var n in names) { var t = ElderboomWeaponFraming.FindNamed(root.transform, n); if (t != null) return t; }
        throw new InvalidOperationException(root.name + " missing hand joint " + names[0]);
    }

    static string Build(Spec s)
    {
        string prefabPath = "Assets/Resources/Elderboom/Weapons/" + s.id + ".prefab";
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(s.model) ?? throw new InvalidOperationException("not imported: " + s.model);
        var donorAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Elderboom/Weapons/" + s.donor + ".prefab")
            ?? throw new InvalidOperationException("donor missing: " + s.donor);
        var root = new GameObject("Weapon_" + s.id);
        var donor = Object.Instantiate(donorAsset);
        try
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            // Alternative kit parts start hidden (the loadout shows the chosen ones).
            foreach (var part in model.GetComponentsInChildren<Renderer>(true))
                if (part.name.IndexOf("_alt", StringComparison.OrdinalIgnoreCase) >= 0) part.gameObject.SetActive(false);
            var muzzle = Exact(model.transform, "Muzzle");
            var rear = Exact(model.transform, "MainWeapon_ADS_Rear");
            var front = Exact(model.transform, "MainWeapon_ADS_Front");
            if (Vector3.Dot(front.position - rear.position, Vector3.forward) < 0)
                model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            muzzle.name = "MainWeapon_Muzzle";
            muzzle.rotation = Quaternion.identity;

            var donorView = donor.GetComponentInChildren<ElderboomViewmodel>(true);
            if (donorView != null && donorView.idle != null && donorView.model != null)
                ElderboomAnimationSampling.Sample(donorView.idle, donorView.model, 0f);
            var dRear = Exact(donor.transform, "MainWeapon_ADS_Rear");
            var dFront = Exact(donor.transform, "MainWeapon_ADS_Front");
            Vector3 donorAxis = dRear != null && dFront != null
                ? dFront.position - dRear.position
                : Exact(donor.transform, "MainWeapon_Muzzle").position - Anchor(donor, s.donorRight);
            var align = Quaternion.FromToRotation(Vector3.ProjectOnPlane(donorAxis, Vector3.up), Vector3.forward);

            Copy(donor, Right, "R", Anchor(donor, s.donorRight), Anchor(model, s.modelRight), align, Exact(model.transform, "RightHand_Grip"));
            Copy(donor, Left, "L", Anchor(donor, s.donorLeft), Anchor(model, s.modelLeft), align, Exact(model.transform, "LeftHand_Support"));

            var flash = new GameObject("ShotFlash").AddComponent<Light>();
            flash.transform.SetParent(root.transform, false);
            flash.transform.position = muzzle.position;
            flash.type = LightType.Point;
            flash.color = new Color(1f, .63f, .22f);
            flash.intensity = 2f;
            flash.range = 3f;
            flash.enabled = false;
            foreach (var node in root.GetComponentsInChildren<Transform>(true)) node.gameObject.layer = 19;

            Vector3 r = muzzle.InverseTransformPoint(rear.position), f = muzzle.InverseTransformPoint(front.position);
            Vector3 u = r + Vector3.up * 0.05f;
            if (PrefabUtility.SaveAsPrefabAsset(root, prefabPath) == null) throw new IOException("Could not save " + prefabPath);
            Debug.Log($"NEW_WEAPON_PREFAB_READY {s.id} rear={r:F4} front={f:F4} renderers={model.GetComponentsInChildren<Renderer>(true).Length}");
            return "    {\"id\":\"" + s.id + "\",\"rear\":" + V(r) + ",\"front\":" + V(f) + ",\"up\":" + V(u) + "}";
        }
        finally
        {
            Object.DestroyImmediate(donor);
            Object.DestroyImmediate(root);
        }
    }

    static void Copy(GameObject donor, string[][] joints, string side, Vector3 from, Vector3 to, Quaternion align, Transform marker)
    {
        for (int i = 0; i < joints.Length; i++)
        {
            var bone = Joint(donor, joints[i]);
            var g = new GameObject(side + "_" + Points[i]).transform;
            g.SetParent(marker, false);
            g.position = to + align * (bone.position - from);
        }
    }

    static string F(float v) => v.ToString("0.00000", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "{\"x\":" + F(v.x) + ",\"y\":" + F(v.y) + ",\"z\":" + F(v.z) + "}";

    static void WriteSights(List<string> lines)
    {
        const string path = "Assets/Resources/Elderboom/GripSights.json";
        var text = File.ReadAllText(path);
        foreach (var line in lines)
        {
            string id = Regex.Match(line, "\"id\":\"([^\"]+)\"").Groups[1].Value;
            text = Regex.Replace(text, "\\s*\\{\"id\":\"" + id + "\"[^\\n]*\\},?", "");
        }
        int close = text.LastIndexOf(']');
        string head = text.Substring(0, close).TrimEnd();
        if (head.EndsWith(",")) head = head.TrimEnd(',');
        text = head + ",\n" + string.Join(",\n", lines) + "\n" + text.Substring(close);
        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(path);
        Debug.Log("NEW_WEAPON_SIGHTS_WRITTEN " + lines.Count);
    }
}
