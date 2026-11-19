using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// The authored gravel road strips that cross the Village Square are cut at its border: the part outside stays,
// the part inside is replaced by the new streets (Map Rework 19b). Originals are kept; clipped copies are new assets.
static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/19d Clip Road Strips In Square")]
    static void ClipStrips()
    {
        var sb = new StringBuilder(); var scene = EditorSceneManager.GetActiveScene();
        Directory.CreateDirectory("Logs/backup_strips");
        File.Copy(scene.path, "Logs/backup_strips/" + System.IO.Path.GetFileName(scene.path), true);
        var square = Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).First(a => a.areaName == "Village Square");
        bool Deep(Vector3 p)
        {
            if (!square.Contains(p)) return false;
            var poly = square.polygon; var q = new Vector2(p.x, p.z);
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++) if (SegDist(q, poly[j], poly[i]) < 2f) return false;
            return true;
        }
        Directory.CreateDirectory("Assets/Resources/Elderboom/ClippedRoads");
        foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!mf.name.StartsWith("Road_") || mf.sharedMesh == null) continue;
            var mesh = mf.sharedMesh; if (!mesh.isReadable) { sb.AppendLine("unreadable " + mf.name); continue; }
            var v = mesh.vertices; var tris = mesh.triangles; var keep = new List<int>(); int cut = 0;
            for (int k = 0; k + 2 < tris.Length; k += 3)
            {
                var c = mf.transform.TransformPoint((v[tris[k]] + v[tris[k + 1]] + v[tris[k + 2]]) / 3f);
                if (Deep(c)) { cut++; continue; }
                keep.Add(tris[k]); keep.Add(tris[k + 1]); keep.Add(tris[k + 2]);
            }
            if (cut == 0) continue;
            if (keep.Count == 0) { mf.gameObject.SetActive(false); EditorUtility.SetDirty(mf.gameObject); sb.AppendLine("hidden " + mf.name); continue; }
            var m2 = Object.Instantiate(mesh); m2.name = mesh.name + "_clipped"; m2.triangles = keep.ToArray(); m2.RecalculateBounds();
            string path = "Assets/Resources/Elderboom/ClippedRoads/" + mf.name + "_clipped.asset";
            AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m2, path);
            mf.sharedMesh = m2; var mc = mf.GetComponent<MeshCollider>(); if (mc) mc.sharedMesh = m2;
            EditorUtility.SetDirty(mf);
            sb.AppendLine("clipped " + mf.name + ": " + cut + " of " + tris.Length / 3 + " triangles removed");
        }
        AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        sb.AppendLine("SAVED"); File.WriteAllText("Logs/strips.txt", sb.ToString());
    }
}
