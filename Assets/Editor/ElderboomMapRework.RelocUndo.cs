using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/15 Undo Relocation")]
    static void UndoRelocation()
    {
        const string B = "Logs/backup_relocate/";
        File.Copy(B + "Elderboom_Unity_Port.unity", "Assets/Scenes/Elderboom_Unity_Port.unity", true);
        File.Copy(B + "Elderboom_Valley.asset", "Assets/Terrain/Elderboom_Valley.asset", true);
        File.Copy(B + "Navigation.asset", "Assets/Resources/Elderboom/Navigation.asset", true);
        AssetDatabase.ImportAsset("Assets/Terrain/Elderboom_Valley.asset", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Resources/Elderboom/Navigation.asset", ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset("Assets/Scenes/Elderboom_Unity_Port.unity", ImportAssetOptions.ForceUpdate);
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        File.WriteAllText("Logs/relocate_undo.txt", "restored, marker " + (GameObject.Find(RelocMarker) != null ? "STILL PRESENT" : "gone"));
    }

    // Layout for planning: houses, big obstacles, roads, terrain trees, area polygons, outer edge.
    [MenuItem("Elderboom HDRP/Map Rework/16 Dump Layout")]
    static void DumpLayout()
    {
        var sb = new StringBuilder();
        var terrain = MainTerrain();
        foreach (var a in Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            if (a.polygon != null) sb.AppendLine("AREA|" + a.areaName + "|" + string.Join(";", a.polygon.Select(p => p.x.ToString("F1") + "," + p.y.ToString("F1"))));
        var church = GameObject.Find("FallenCity_Church_HumanScale");
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!IsHouseName(t.name) || t.GetComponentInChildren<Renderer>() == null) continue;
            if (church != null && t.IsChildOf(church.transform)) continue;
            var b = Bounds(t.gameObject);
            if (b.size.y < 2.6f || b.size.y > 30f || Mathf.Min(b.size.x, b.size.z) < 2.5f || Mathf.Max(b.size.x, b.size.z) > 45f) continue;
            if (t.parent != null && IsHouseName(t.parent.name)) continue;
            sb.AppendLine("HOUSE|" + Path(t) + "|" + b.center.x.ToString("F1") + "," + b.center.z.ToString("F1") + "|" + b.size.x.ToString("F1") + "," + b.size.z.ToString("F1") + "|" + t.eulerAngles.y.ToString("F0"));
        }
        if (church) { var cb = Bounds(church); sb.AppendLine("CHURCH|" + cb.center.x.ToString("F1") + "," + cb.center.z.ToString("F1") + "|" + cb.size.x.ToString("F1") + "," + cb.size.z.ToString("F1")); }
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            var b = r.bounds; if (b.size.y < 1.2f || Mathf.Max(b.size.x, b.size.z) > 60f || Mathf.Max(b.size.x, b.size.z) < 1.2f) continue;
            string n = r.name.ToLowerInvariant(); if (n.Contains("grass") || n.Contains("fern") || n.Contains("lod1") || n.Contains("lod2") || n.Contains("lod3")) continue;
            sb.AppendLine("OBS|" + b.min.x.ToString("F1") + "," + b.min.z.ToString("F1") + "," + b.max.x.ToString("F1") + "," + b.max.z.ToString("F1") + "|" + r.name);
        }
        foreach (var p in RoadPoints(terrain, out _, out _)) sb.AppendLine("ROAD|" + p.x.ToString("F1") + "," + p.y.ToString("F1"));
        var td = terrain.terrainData;
        foreach (var ti in td.treeInstances) { var w = Vector3.Scale(ti.position, td.size) + terrain.transform.position; if (Mathf.Abs(w.x - 5.7f) < 100 && Mathf.Abs(w.z + .7f) < 100) sb.AppendLine("TREE|" + w.x.ToString("F1") + "," + w.z.ToString("F1")); }
        foreach (var s in Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) sb.AppendLine("SPAWN|" + s.transform.position.x.ToString("F1") + "," + s.transform.position.z.ToString("F1"));
        foreach (var s in Object.FindObjectsByType<ElderboomStation>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) sb.AppendLine("STATION|" + s.transform.position.x.ToString("F1") + "," + s.transform.position.z.ToString("F1") + "|" + s.label);
        foreach (var g in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) sb.AppendLine("GATE|" + g.transform.position.x.ToString("F1") + "," + g.transform.position.z.ToString("F1") + "|" + g.name);
        File.WriteAllText("Logs/layout.txt", sb.ToString());
    }
}
