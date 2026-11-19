using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/23 Fix Fence Openings")]
    static void FixFenceOpenings()
    {
        var log = new System.Text.StringBuilder();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.gameObject.activeSelf || !t.name.StartsWith("fence_farm_rail_LOD")) continue;
            var g = t; while (g != null && !Regex.IsMatch(g.name, @"^fence_farm_rail_\d+$")) g = g.parent;
            if (g == null) continue;
            t.gameObject.SetActive(true); EditorUtility.SetDirty(t.gameObject);
            if (g.gameObject.activeSelf) { g.gameObject.SetActive(false); EditorUtility.SetDirty(g.gameObject); log.AppendLine("hidden module " + g.name + " at " + g.position.ToString("F1")); }
        }
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/fencefix.txt", log.ToString());
    }
}
