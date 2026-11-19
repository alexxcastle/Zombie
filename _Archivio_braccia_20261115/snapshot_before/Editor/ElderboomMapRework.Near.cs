using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/10 Near Shot 06")]
    static void NearShot06()
    {
        var sb = new StringBuilder();
        var eye = new Vector3(-3, Ground(-3, 8), 8);
        sb.AppendLine("eye ground " + eye.y.ToString("F2"));
        if (Physics.Raycast(new Vector3(-3, 120, 8), Vector3.down, out var hit, 300f, ~((1 << 2) | (1 << 19)), QueryTriggerInteraction.Ignore)) sb.AppendLine("ground hit " + Path(hit.collider.transform));
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            var b = r.bounds; var c = new Vector3(Mathf.Clamp(-3, b.min.x, b.max.x), 0, Mathf.Clamp(8, b.min.z, b.max.z));
            if (Flat(c - new Vector3(-3, 0, 8)) > 4f || b.max.y < eye.y + 1.2f || b.size.magnitude > 60) continue;
            sb.AppendLine(Path(r.transform) + " b " + b.center.ToString("F1") + " / " + b.size.ToString("F1"));
        }
        File.WriteAllText("Logs/map_near06.txt", sb.ToString());
    }
}
