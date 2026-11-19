using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_gripsdump.txt (operator name, e.g. "Soph") -> clips, binding paths and hierarchy of Resources/Elderboom/Grips/<Op>_Grips.
static class ElderboomClaudeGripsDump
{
    const string Flag = "Logs/claude_gripsdump.txt", Result = "Logs/claude_gripsdump_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        string op = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        var sb = new StringBuilder();
        try
        {
            string path = "Elderboom/Grips/" + op + "_Grips";
            var prefab = Resources.Load<GameObject>(path);
            var clips = Resources.LoadAll<AnimationClip>(path);
            sb.AppendLine("prefab " + (prefab ? prefab.name : "null") + " clips " + clips.Length);
            foreach (var c in clips.Take(40))
            {
                var b = AnimationUtility.GetCurveBindings(c);
                sb.AppendLine("clip " + c.name + " len " + c.length + " legacy " + c.legacy + " bindings " + b.Length + " e.g. " + string.Join(" | ", b.Select(x => x.path).Distinct().Take(3)));
            }
            if (prefab)
                foreach (var t in prefab.GetComponentsInChildren<Transform>(true).Take(60))
                {
                    string p = t.name; var q = t.parent; while (q != null && q != prefab.transform) { p = q.name + "/" + p; q = q.parent; }
                    sb.AppendLine("  " + p + " ls=" + t.lossyScale.ToString("F3"));
                }
        }
        catch (System.Exception e) { sb.AppendLine("FAIL " + e); }
        File.WriteAllText(Result, sb.ToString());
    }
}
