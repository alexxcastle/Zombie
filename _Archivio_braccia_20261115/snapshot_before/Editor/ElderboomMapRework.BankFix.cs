using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/25 Clear Creek Bank")]
    static void ClearCreekBank()
    {
        var log = new System.Text.StringBuilder();
        Transform Find(string n) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(t => t.name == n);
        foreach (var (n, dz) in new[] { ("bakers_house_19", 2.2f), ("cooper_workshop", 1.6f), ("greenhouse_large_45", 1.0f) })
        {
            var h = Find(n); if (h == null) continue;
            var b0 = Bounds(h.gameObject);
            h.position += new Vector3(0, 0, dz); Physics.SyncTransforms();
            var b = Bounds(h.gameObject);
            h.position += Vector3.up * (TerrainY(b.center.x, b.center.z) - TerrainY(b0.center.x, b0.center.z));
            EditorUtility.SetDirty(h); log.AppendLine(n + " south edge " + b0.min.z.ToString("F1") + " -> " + Bounds(h.gameObject).min.z.ToString("F1"));
        }
        // the lantern that landed over the creek goes beside the bakers' house, on the bridge ramp
        var lamp = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => t.name.Contains("Lantern") && t.GetComponentInChildren<Renderer>() != null && t.parent != null)
            .OrderBy(t => (XZ(Bounds(t.gameObject).center) - new Vector2(19f, -47.3f)).sqrMagnitude).FirstOrDefault();
        if (lamp != null && (XZ(Bounds(lamp.gameObject).center) - new Vector2(19f, -47.3f)).magnitude < 2f)
        {
            var lb = Bounds(lamp.gameObject); var at = new Vector2(11.6f, -35.5f);
            lamp.position += new Vector3(at.x - lb.center.x, TerrainY(at.x, at.y) - lb.min.y, at.y - lb.center.z);
            EditorUtility.SetDirty(lamp); log.AppendLine("lantern " + lamp.name + " -> " + at);
        }
        Physics.SyncTransforms();
        log.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/bankfix.txt", log.ToString());
    }
}
