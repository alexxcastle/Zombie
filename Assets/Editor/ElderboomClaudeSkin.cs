using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_skin.txt ("dump") -> the operators' first-person arm materials (Tactical/Eli/Soph viewmodel pivots in the
// open scene): renderer, material asset, shader, textures. Result in Logs/claude_skin_result.txt.
static partial class ElderboomClaudeSkin
{
    const string Flag = "Logs/claude_skin.txt", Result = "Logs/claude_skin_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        string r; try { r = mode.StartsWith("apply") ? Apply(mode) : Dump(); } catch (System.Exception e) { r = "FAIL " + e; }
        File.WriteAllText(Result, r);
    }

    static string Dump()
    {
        var sb = new StringBuilder();
        var seen = new System.Collections.Generic.HashSet<Material>();
        foreach (var pivot in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(t => t.name.EndsWith("_Viewmodel_Pivot")))
        {
            sb.AppendLine("== " + pivot.name);
            foreach (var r in pivot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (!m) continue;
                    sb.AppendLine("  " + r.name + " -> " + m.name + " | " + AssetDatabase.GetAssetPath(m) + " | " + m.shader.name);
                    if (!seen.Add(m)) continue;
                    foreach (var p in m.GetTexturePropertyNames()) { var t = m.GetTexture(p); if (t) sb.AppendLine("      " + p + " = " + AssetDatabase.GetAssetPath(t) + " " + t.width + "x" + t.height); }
                    foreach (var p in new[] { "_BaseColor", "_Smoothness", "_SmoothnessRemapMax", "_MaterialID", "_DiffusionProfileHash", "_SubsurfaceMask", "_NormalScale", "_Metallic", "_AORemapMax" })
                        if (m.HasProperty(p)) sb.AppendLine("      " + p + " = " + (m.HasColor(p) ? m.GetColor(p).ToString() : m.GetFloat(p).ToString()));
                }
        }
        sb.AppendLine("DONE"); return sb.ToString();
    }
    static partial void ApplyImpl(string mode, StringBuilder sb);
    static string Apply(string mode) { var sb = new StringBuilder(); ApplyImpl(mode, sb); sb.AppendLine("DONE"); return sb.ToString(); }
}
