using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Probe 2: what keeps the Village Square / East Cemetery border closed around the church,
// and what sits inside the church footprint without being part of the church.
static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/6 Probe Church Border")]
    static void ProbeChurchBorder()
    {
        var sb = new StringBuilder();
        Physics.SyncTransforms();
        var church = GameObject.Find("FallenCity_Church_HumanScale");
        var cb = Bounds(church);
        sb.AppendLine("church bounds " + cb.center.ToString("F1") + " / " + cb.size.ToString("F1"));
        var areas = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude).Where(m => m.GetType().Name == "ElderboomArea").ToArray();
        sb.AppendLine("areas " + areas.Length);
        // Border segments of interest: the Village Square polygon edges near the church.
        var segs = new (Vector2 a, Vector2 b)[] { (new Vector2(34, 24), new Vector2(34, 12)), (new Vector2(34, 12), new Vector2(52, 5)), (new Vector2(52, 5), new Vector2(34, -25)), (new Vector2(34, -25), new Vector2(46, -27)), (new Vector2(46, -27), new Vector2(56, -41)) };
        foreach (var (a, b) in segs)
        {
            sb.AppendLine("== segment " + a + " -> " + b);
            float len = (b - a).magnitude; string run = ""; int open = 0, churchOnly = 0;
            for (float s = 0; s <= len; s += 1f)
            {
                var p = Vector2.Lerp(a, b, s / len); float y = Ground(p.x, p.y);
                var hits = Physics.OverlapCapsule(new Vector3(p.x, y + .5f, p.y), new Vector3(p.x, y + 1.6f, p.y), .8f, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    .Where(c => c.GetComponent<Terrain>() == null).ToArray();
                bool any = hits.Length > 0; bool onlyChurch = any && hits.All(c => c.transform.IsChildOf(church.transform));
                char ch = !any ? '.' : onlyChurch ? 'C' : '#'; run += ch; if (!any) open++; if (onlyChurch) churchOnly++;
                if (any && s % 4 == 0) sb.AppendLine("   " + p.ToString("F0") + " " + string.Join(", ", hits.Take(3).Select(c => Path(c.transform))));
            }
            sb.AppendLine("   pattern " + run + "  open " + open + " churchOnly " + churchOnly);
        }
        sb.AppendLine("== non-church objects inside church footprint");
        var seen = new HashSet<Transform>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (t.IsChildOf(church.transform)) continue;
            var p = t.position;
            if (p.x < cb.min.x - 1 || p.x > cb.max.x + 1 || p.z < cb.min.z - 1 || p.z > cb.max.z + 1) continue;
            var top = t; while (top.parent != null && top.parent.position.x > cb.min.x - 1 && top.parent.position.x < cb.max.x + 1 && top.parent.position.z > cb.min.z - 1 && top.parent.position.z < cb.max.z + 1 && !top.parent.IsChildOf(church.transform) && top.parent.parent != null) top = top.parent;
            if (!seen.Add(top)) continue;
            var comps = string.Join(",", top.GetComponents<Component>().Select(c => c == null ? "null" : c.GetType().Name).Where(n => n != "Transform"));
            sb.AppendLine("   " + Path(top) + " at " + top.position.ToString("F1") + " active=" + top.gameObject.activeInHierarchy + " [" + comps + "] children " + top.childCount);
        }
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/map_probe2.txt", sb.ToString());
        Debug.Log("[MapRework] probe2 written");
    }
}
