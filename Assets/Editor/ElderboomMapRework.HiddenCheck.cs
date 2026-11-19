using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    // Bank stones carry the creek railing (a barrier): never hidden by a plot. Lists what plots hid; restores bank pieces
    // and moves the house off them if needed (reported).
    [MenuItem("Elderboom HDRP/Map Rework/24 Check Hidden Props")]
    static void CheckHidden()
    {
        var sb = new StringBuilder();
        var houses = new[] { "bakers_house_19", "cooper_workshop", "smokehouse", "weavers_house_44", "greenhouse_small_21", "greenhouse_small_22", "SouthCreek_Timber_Inn", "greenhouse_large_45" }
            .Select(n => Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).FirstOrDefault(t => t.name == n)).Where(t => t != null).ToList();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t.gameObject.activeSelf || t.GetComponentInChildren<Renderer>(true) == null) continue;
            var rs = t.GetComponentsInChildren<Renderer>(true); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            foreach (var h in houses)
            {
                var hb = Bounds(h.gameObject); hb.Expand(new Vector3(2, 50, 2));
                if (!hb.Contains(new Vector3(b.center.x, hb.center.y, b.center.z))) continue;
                bool bank = Path(t).Contains("Riverbank") || Path(t).Contains("Bank_");
                sb.AppendLine((bank ? "BANK " : "prop ") + Path(t) + " at " + b.center.ToString("F1") + " under " + h.name);
                if (bank) { t.gameObject.SetActive(true); EditorUtility.SetDirty(t.gameObject); }
            }
        }
        foreach (var h in houses) sb.AppendLine("house " + h.name + " " + Bounds(h.gameObject).center.ToString("F1") + " / " + Bounds(h.gameObject).size.ToString("F1"));
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/hidden_check.txt", sb.ToString());
    }
}
