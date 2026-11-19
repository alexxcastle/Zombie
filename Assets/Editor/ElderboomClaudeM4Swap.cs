using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_m4swap.txt ("dump" | "apply") -> inspects / replaces the gun model inside Resources/Elderboom/Weapons/shotgun.prefab
// with the Benelli M4 (arms and animations stay). Result in Logs/claude_m4swap_result.txt.
static class ElderboomClaudeM4Swap
{
    const string Flag = "Logs/claude_m4swap.txt", Result = "Logs/claude_m4swap_result.txt";
    const string PrefabPath = "Assets/Resources/Elderboom/Weapons/shotgun.prefab";
    const string M4 = "Assets/GameAssets/Benelli_M4/Benelli_M4_FPS.glb";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        string r;
        try { r = Run(mode); } catch (System.Exception e) { r = "FAIL " + e; }
        File.WriteAllText(Result, r);
    }
    static string P(Vector3 v) => v.ToString("F4");
    static string Run(string mode)
    {
        var sb = new StringBuilder();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            sb.AppendLine("root " + root.name + " scale " + P(root.transform.lossyScale));
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = t.name; var p = t.parent; while (p != null && p != root.transform) { path = p.name + "/" + path; p = p.parent; }
                var rd = t.GetComponent<Renderer>();
                string extra = "";
                if (rd != null) extra += " R:" + rd.GetType().Name + " b=" + P(rd.bounds.center) + " s=" + P(rd.bounds.size) + " en=" + rd.enabled;
                if (rd is SkinnedMeshRenderer sk) extra += " rootBone=" + (sk.rootBone ? sk.rootBone.name : "-") + " bones=" + string.Join(",", sk.bones.Take(6).Select(b => b ? b.name : "null"));
                var comps = string.Join(",", t.GetComponents<Component>().Where(c => !(c is Transform) && !(c is Renderer) && !(c is MeshFilter)).Select(c => c.GetType().Name));
                if (rd != null || mode.Contains("all") || path.ToLower().Contains("muzzle") || path.ToLower().Contains("ads") || t.parent == root.transform || comps.Length > 0)
                    sb.AppendLine(path + " pos=" + P(t.position) + " fwd=" + P(t.forward) + " up=" + P(t.up) + " ls=" + P(t.lossyScale) + extra + (comps.Length > 0 ? " C:" + comps : ""));
            }
            if (mode.StartsWith("apply")) sb.Append(Apply(root));
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        sb.AppendLine("DONE");
        return sb.ToString();
    }
    static Transform Find(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
    static string Apply(GameObject root)
    {
        var sb = new StringBuilder();
        var vm = Find(root.transform, "Viewmodel");
        Transform bone = null; foreach (Transform c in vm) if (c.name == "shotgun") bone = c;
        var oldTrig = Find(bone, "trigger").GetComponent<Renderer>().bounds.center;
        var muzzle = Find(root.transform, "MainWeapon_Muzzle");
        foreach (var n in new[] { "base", "lid", "pump", "trigger" }) { var r = Find(bone, n).GetComponent<Renderer>(); r.enabled = false; }
        var prev = Find(bone, "Benelli_M4_Model"); if (prev) Object.DestroyImmediate(prev.gameObject);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(M4);
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset, bone);
        inst.name = "Benelli_M4_Model";
        inst.transform.localRotation = Quaternion.identity; inst.transform.localPosition = Vector3.zero; inst.transform.localScale = Vector3.one;
        var end = Find(inst.transform, "Muzzle"); var trig = Find(inst.transform, "G_trigger");
        if (Vector3.Dot(end.position - trig.position, bone.forward) < 0) { inst.transform.localRotation = Quaternion.Euler(0, 180, 0); sb.AppendLine("flipped 180"); }
        var tc = trig.GetComponent<Renderer>().bounds.center;
        inst.transform.position += oldTrig - tc;
        end.name = "M4_BarrelEnd";
        foreach (var n in new[] { "RightHand_Grip", "LeftHand_Support" }) { var t = Find(inst.transform, n); if (t) t.gameObject.SetActive(false); }
        muzzle.position = end.position;
        var rear = Find(inst.transform, "MainWeapon_ADS_Rear"); var front = Find(inst.transform, "MainWeapon_ADS_Front");
        Vector3 rl = muzzle.InverseTransformPoint(rear.position), fl = muzzle.InverseTransformPoint(front.position), ul = muzzle.InverseTransformPoint(rear.position + inst.transform.up * 0.05f);
        System.Func<Vector3, string> J = v => "{\"x\": " + v.x.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + ", \"y\": " + v.y.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + ", \"z\": " + v.z.ToString("F5", System.Globalization.CultureInfo.InvariantCulture) + "}";
        sb.AppendLine("SIGHT {\"id\": \"shotgun\", \"rear\": " + J(rl) + ", \"front\": " + J(fl) + ", \"up\": " + J(ul) + "}");
        sb.AppendLine("m4 pos " + P(inst.transform.position) + " muzzle " + P(muzzle.position) + " rear " + P(rear.position) + " front " + P(front.position));
        var grip = Find(inst.transform, "G_body").GetComponent<Renderer>().bounds; sb.AppendLine("body bounds " + P(grip.center) + " " + P(grip.size));
        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        sb.AppendLine("saved");
        return sb.ToString();
    }
}
