using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_gripprobe.txt ("Soph shotgun") -> compares the operator's own viewmodel idle with its Grips model sampled
// for a weapon (hand bone and grip points, in the viewmodel pivot's space). Result in Logs/claude_gripprobe_result.txt.
static class ElderboomClaudeGripProbe
{
    const string Flag = "Logs/claude_gripprobe.txt", Result = "Logs/claude_gripprobe_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static Transform Find(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
    static Transform FindPrefix(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith(n)) return t; return null; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        var args = File.ReadAllText(Flag).Trim().Split(' '); File.Delete(Flag);
        var sb = new StringBuilder();
        GameObject a = null, b = null;
        try
        {
            string op = args[0], weapon = args[1], hand = args.Length > 2 ? args[2] : "DEF-hand.R";
            var pivot = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == op + "_Viewmodel_Pivot");
            var view = pivot.GetComponent<ElderboomViewmodel>();
            var m = view.model.transform;
            foreach (var c in pivot.GetComponentsInChildren<Component>(true)) if (c is Behaviour || c is Animation) sb.AppendLine("  comp " + c.GetType().Name + " on " + c.gameObject.name);
            sb.AppendLine("pivot " + pivot.name + " model " + m.name + " local " + m.localPosition + " " + m.localEulerAngles + " s " + m.localScale + " parent " + m.parent.name + " idle " + (view.idle ? view.idle.name : "-"));
            a = Object.Instantiate(m.gameObject, m.parent, false);
            ElderboomAnimationSampling.Sample(view.idle, a, 0f);
            var ha = Find(a.transform, hand);
            sb.AppendLine("own idle hand " + (ha ? pivot.InverseTransformPoint(ha.position).ToString("F3") + " fwd " + pivot.InverseTransformDirection(ha.forward).ToString("F2") : "missing"));
            var gp = Resources.Load<GameObject>("Elderboom/Grips/" + op + "_Grips");
            b = Object.Instantiate(gp, m.parent, false); b.transform.localPosition = m.localPosition; b.transform.localRotation = m.localRotation; b.transform.localScale = m.localScale;
            var clip = Resources.LoadAll<AnimationClip>("Elderboom/Grips/" + op + "_Grips").FirstOrDefault(c => c.name == op + "_" + weapon + "_Idle");
            sb.AppendLine("clip " + (clip ? clip.name : "missing"));
            var hb0 = Find(b.transform, hand);
            sb.AppendLine("grips bind hand " + (hb0 ? pivot.InverseTransformPoint(hb0.position).ToString("F3") : "missing"));
            ElderboomAnimationSampling.Sample(clip, b, 0f);
            var hb = Find(b.transform, hand);
            for (var q = hb; q != null && q != pivot; q = q.parent) sb.AppendLine("   chain " + q.name + " lp " + q.localPosition.ToString("F3") + " lr " + q.localEulerAngles.ToString("F1") + " ls " + q.localScale.ToString("F3"));
            sb.AppendLine("grips sampled hand " + (hb ? pivot.InverseTransformPoint(hb.position).ToString("F3") + " fwd " + pivot.InverseTransformDirection(hb.forward).ToString("F2") + " ls " + hb.lossyScale.ToString("F3") : "missing"));
            foreach (var k in new[] { "W", "I", "M", "P" })
            {
                var g = FindPrefix(b.transform, "GripPt_" + weapon + "_" + k);
                sb.AppendLine("  GripPt_" + weapon + "_" + k + " " + (g ? g.name + " " + pivot.InverseTransformPoint(g.position).ToString("F3") + " parent " + g.parent.name : "missing"));
            }
            var gps = b.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("GripPt_" + weapon)).Select(t => t.name);
            sb.AppendLine("gripPts: " + string.Join(", ", gps));
        }
        catch (System.Exception e) { sb.AppendLine("FAIL " + e); }
        if (a) Object.DestroyImmediate(a); if (b) Object.DestroyImmediate(b);
        File.WriteAllText(Result, sb.ToString());
    }
}
