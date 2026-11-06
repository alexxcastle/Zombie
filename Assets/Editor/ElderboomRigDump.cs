using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Writes the hierarchy (renderers, skinned bones, markers) of the operator
// viewmodels in the open scene and of every weapon prefab to Logs/rig_dump.txt.
static class ElderboomRigDump
{
    [MenuItem("Elderboom HDRP/Dev/Dump Viewmodel Rigs")]
    static void Dump()
    {
        var report = new StringBuilder();
        foreach (var view in Object.FindObjectsByType<ElderboomViewmodel>(FindObjectsInactive.Include))
        {
            report.AppendLine("=== OPERATOR VIEWMODEL " + view.name + " op=" + view.operatorId + " model=" + (view.model ? view.model.name : "null")
                + " idle=" + (view.idle ? view.idle.name + (view.idle.legacy ? "(legacy)" : "") : "null"));
            var animator = view.model ? view.model.GetComponentInChildren<Animator>(true) : null;
            if (animator != null) report.AppendLine("animator human=" + animator.isHuman + " avatar=" + (animator.avatar ? animator.avatar.name : "null"));
            Describe(view.transform, report);
        }
        foreach (var path in Directory.GetFiles("Assets/Resources/Elderboom/Weapons", "*.prefab"))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            report.AppendLine("=== WEAPON " + Path.GetFileNameWithoutExtension(path));
            var view = prefab.GetComponentInChildren<ElderboomViewmodel>(true);
            if (view != null) report.AppendLine("viewmodel model=" + (view.model ? view.model.name : "null") + " idle=" + (view.idle ? view.idle.name : "null"));
            Describe(prefab.transform, report);
        }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/rig_dump.txt", report.ToString());
        Debug.Log("[Elderboom] Rig dump written to Logs/rig_dump.txt");
    }

    static void Describe(Transform root, StringBuilder report)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            string kind = r is SkinnedMeshRenderer ? "SKIN" : "MESH";
            string bones = r is SkinnedMeshRenderer s && s.bones != null
                ? string.Join(",", s.bones.Where(b => b != null).Select(b => b.name).Take(12)) + (s.bones.Length > 12 ? ",…(" + s.bones.Length + ")" : "")
                : "";
            report.AppendLine("  " + kind + " " + PathOf(r.transform, root) + (bones.Length > 0 ? "  bones[" + bones + "]" : "") + "  verts=" + VertexCount(r));
        }
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("hand") || n.Contains("wrist") || n.Contains("palm") || n.Contains("muzzle") || n.Contains("ads") || n.Contains("grip") || n.Contains("weapon") || n.Contains("gun"))
                report.AppendLine("  NODE " + PathOf(t, root) + "  pos=" + root.InverseTransformPoint(t.position).ToString("F3") + " fwd=" + root.InverseTransformDirection(t.forward).ToString("F2") + " up=" + root.InverseTransformDirection(t.up).ToString("F2"));
        }
    }

    static int VertexCount(Renderer r) =>
        r is SkinnedMeshRenderer s && s.sharedMesh ? s.sharedMesh.vertexCount :
        r.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh ? f.sharedMesh.vertexCount : 0;

    static string PathOf(Transform t, Transform root)
    {
        var parts = new System.Collections.Generic.List<string>();
        for (var c = t; c != null && c != root; c = c.parent) parts.Insert(0, c.name);
        return string.Join("/", parts);
    }
}
