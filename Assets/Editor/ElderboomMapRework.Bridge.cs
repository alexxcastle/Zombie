using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/8 Probe Bridge")]
    static void ProbeBridge()
    {
        var sb = new StringBuilder();
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (Flat(g.transform.position - new Vector3(7, 0, -48)) > 15) continue;
            sb.AppendLine("GATE " + Path(g.transform) + " active=" + g.gameObject.activeInHierarchy + " pos " + g.transform.position.ToString("F2") + " rot " + g.transform.eulerAngles.ToString("F0") + " area " + g.areaName + "/" + g.otherAreaName + " cost " + g.cost + " reveal " + (g.reveal ? Path(g.reveal.transform) + " active=" + g.reveal.activeSelf : "none"));
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) sb.AppendLine("   col " + Path(c.transform) + " " + c.GetType().Name + " b " + c.bounds.center.ToString("F2") + " / " + c.bounds.size.ToString("F2") + " trig=" + c.isTrigger);
            var lv = g.GetComponent<ElderboomGateLeaves>(); if (lv) sb.AppendLine("   leaves " + (lv.leftHinge ? Path(lv.leftHinge) + " " + lv.leftHinge.position.ToString("F2") : "-") + " | " + (lv.rightHinge ? Path(lv.rightHinge) + " " + lv.rightHinge.position.ToString("F2") : "-"));
            if (g.reveal) foreach (var r in g.reveal.GetComponentsInChildren<Renderer>(true)) sb.AppendLine("   reveal r " + Path(r.transform) + " b " + r.bounds.center.ToString("F2") + " / " + r.bounds.size.ToString("F2"));
            if (g.reveal) foreach (var c in g.reveal.GetComponentsInChildren<Collider>(true)) sb.AppendLine("   reveal c " + Path(c.transform) + " " + c.GetType().Name + " b " + c.bounds.center.ToString("F2") + " / " + c.bounds.size.ToString("F2"));
        }
        sb.AppendLine("== objects within 14 m of (7,-48)");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var r = t.GetComponent<Renderer>(); var c = t.GetComponent<Collider>();
            if (r == null && c == null) continue;
            var b = r ? r.bounds : c.bounds;
            if (Flat(b.center - new Vector3(7, 0, -48)) > 14 || b.size.magnitude < 1.5f) continue;
            var n = t.name.ToLowerInvariant(); if (n.Contains("grass") || n.Contains("fern")) continue;
            sb.AppendLine("   " + Path(t) + " act=" + t.gameObject.activeInHierarchy + " b " + b.center.ToString("F2") + " / " + b.size.ToString("F2") + (c ? " col=" + c.GetType().Name + (c.isTrigger ? "(trig)" : "") : "") + (r ? " r" : ""));
        }
        sb.AppendLine("== ground profile along x=7 and x=1/13 (z -60..-36)");
        foreach (float x in new[] { 1f, 7f, 13f }) { var line = "x=" + x + ":"; for (float z = -60; z <= -36; z += 1f) line += " " + TerrainY(x, z).ToString("F1"); sb.AppendLine(line); }
        File.WriteAllText("Logs/map_bridge.txt", sb.ToString()); Debug.Log("[MapRework] bridge probe written");
    }
}
