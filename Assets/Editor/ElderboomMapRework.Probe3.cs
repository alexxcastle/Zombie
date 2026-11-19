using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/29 Probe Church East + Kits")]
    static void Probe3()
    {
        var sb = new StringBuilder();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (t.name == "Working_LeanTo" || t.name == "Abandoned_Farm_Cart" || t.name.StartsWith("CC0_wicker_basket") || t.name.StartsWith("CC0_wooden_crate") || t.name.StartsWith("CC0_wine_barrel") || t.name == "Elderboom_Streetlamp" || t.name == "Forged_Lantern" || t.name == "Elderboom_MossyStoneWall" || t.name == "Elderboom_ModularFence")
            {
                var b = Bounds(t.gameObject);
                sb.AppendLine("KIT " + Path(t) + " " + b.center.ToString("F1") + " / " + b.size.ToString("F1") + " children " + t.childCount + " rotY " + t.eulerAngles.y.ToString("F0"));
            }
        }
        var cf = GameObject.Find("Elderboom_Access_Redesign/churchyard_front");
        if (cf) foreach (Transform c in cf.transform) { var b = Bounds(c.gameObject); sb.AppendLine("CYF " + c.name + " " + b.center.ToString("F1") + " / " + b.size.ToString("F1")); }
        sb.AppendLine("== colliders x 38..58, z -34..8");
        foreach (var c in Physics.OverlapBox(new Vector3(48, 2, -13), new Vector3(10, 3, 21), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore))
        {
            if (c is TerrainCollider) continue;
            if (c.transform.IsChildOf(GameObject.Find("FallenCity_Church_HumanScale").transform)) continue;
            sb.AppendLine("   " + Path(c.transform) + " " + c.bounds.center.ToString("F1") + " / " + c.bounds.size.ToString("F1"));
        }
        var church = GameObject.Find("FallenCity_Church_HumanScale"); var cb = Bounds(church);
        sb.AppendLine("church " + cb.center.ToString("F1") + " / " + cb.size.ToString("F1"));
        // where is the door: lowest wide opening on the west or east face
        foreach (var x in new[] { cb.min.x - 1f, cb.max.x + 1f })
        {
            string line = "face x " + x.ToString("F1") + ":";
            for (float z = cb.min.z; z <= cb.max.z; z += 1f)
            {
                var o = new Vector3(x, TerrainY(x, z) + 1.2f, z); var dir = x < cb.center.x ? Vector3.right : Vector3.left;
                bool hit = Physics.Raycast(o, dir, out var h, 6f, ~0, QueryTriggerInteraction.Ignore) && h.collider.transform.IsChildOf(church.transform);
                line += hit ? "#" : ".";
            }
            sb.AppendLine(line);
        }
        File.WriteAllText("Logs/probe3.txt", sb.ToString());
    }
}
