using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_arms.txt ("dump" | "apply") -> the weapon packs' own arm meshes (not the operators' arms) are listed / removed
// from Resources/Elderboom/Weapons/*.prefab. Bones stay, so the weapon animations still work. Result in Logs/claude_arms_result.txt.
static class ElderboomClaudeArms
{
    const string Flag = "Logs/claude_arms.txt", Result = "Logs/claude_arms_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        string r; try { r = Run(mode.StartsWith("apply")); } catch (System.Exception e) { r = "FAIL " + e; }
        File.WriteAllText(Result, r);
    }
    public static bool IsPackArm(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        if (n.StartsWith("pistol") || n.Contains("guard") || n.Contains("handle")) return false;
        return n.StartsWith("p_") || n.Contains("arm") || n.Contains("hand") || n.Contains("glove") || n.Contains("sleeve");
    }
    static string Run(bool apply)
    {
        var sb = new StringBuilder();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Elderboom/Weapons" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                sb.AppendLine(Path.GetFileNameWithoutExtension(path) + ": " + r.name + " (" + r.GetType().Name + ")" + (IsPackArm(r) ? "  <- ARM" : ""));
                if (apply && IsPackArm(r))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    Object.DestroyImmediate(r, true); if (mf) Object.DestroyImmediate(mf, true); n++;
                }
            }
            if (apply && n > 0) { PrefabUtility.SaveAsPrefabAsset(root, path); sb.AppendLine("  removed " + n); }
            PrefabUtility.UnloadPrefabContents(root);
        }
        sb.AppendLine("DONE");
        return sb.ToString();
    }
}
