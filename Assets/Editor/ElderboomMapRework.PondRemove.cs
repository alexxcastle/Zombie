using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    // The pond is a flat platform with no basin: neither an opaque nor the creek's depth water reads as water.
    // It goes (with its rim stones); the clearing stays. Both objects are only disabled.
    [MenuItem("Elderboom HDRP/Map Rework/27 Remove Pond")]
    static void RemovePond()
    {
        var log = new System.Text.StringBuilder();
        var pond = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.name.StartsWith("SM_AssetPlatform3")).OrderBy(r => (r.bounds.center - new Vector3(-30.4f, 1f, 29.4f)).sqrMagnitude).FirstOrDefault();
        if (pond != null && (pond.bounds.center - new Vector3(-30.4f, 1f, 29.4f)).magnitude < 6f) { pond.gameObject.SetActive(false); EditorUtility.SetDirty(pond.gameObject); log.AppendLine("pond hidden: " + Path(pond.transform)); }
        var rim = GameObject.Find("Rework_Pond_Rim"); if (rim) { rim.SetActive(false); EditorUtility.SetDirty(rim); log.AppendLine("rim hidden"); }
        Physics.SyncTransforms();
        log.AppendLine(ElderboomClaudeBlocks.BakeAccessNavigation());
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/pond_remove.txt", log.ToString());
    }
}
