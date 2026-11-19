using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/12 Pond Tweak")]
    static void PondTweak()
    {
        var log = new System.Text.StringBuilder();
        var pond = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(r => r.name.StartsWith("SM_AssetPlatform3")).OrderBy(r => (r.bounds.center - new Vector3(-30.4f, 1f, 29.4f)).sqrMagnitude).First();
        var creek = GameObject.Find("Creek_Continuous_Surface");
        var water = creek != null ? creek.GetComponent<Renderer>().sharedMaterial : null;
        if (water != null) { pond.sharedMaterials = pond.sharedMaterials.Select(_ => water).ToArray(); EditorUtility.SetDirty(pond); log.AppendLine("pond uses " + water.name); }
        var rim = GameObject.Find("Rework_Pond_Rim");
        if (rim != null)
            foreach (Transform t in rim.transform)
            {
                if (!t.name.StartsWith("RimStone_")) continue;
                var b = Bounds(t.gameObject);
                if (b.size.y > Mathf.Max(b.size.x, b.size.z) * .6f) t.localScale = new Vector3(t.localScale.x * 1.15f, t.localScale.y * .42f, t.localScale.z * 1.15f);
                t.rotation = Quaternion.Euler(Random.Range(-8f, 8f), t.eulerAngles.y, Random.Range(-8f, 8f));
                Physics.SyncTransforms(); b = Bounds(t.gameObject);
                t.position += Vector3.up * (TerrainY(b.center.x, b.center.z) - .18f * b.size.y - b.min.y);
                EditorUtility.SetDirty(t);
            }
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/map_pond_tweak.txt", log.ToString() + "saved\n");
        PondShots();
    }
}
