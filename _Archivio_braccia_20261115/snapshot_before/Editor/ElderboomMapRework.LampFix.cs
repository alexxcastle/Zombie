using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

static partial class ElderboomMapRework
{
    [MenuItem("Elderboom HDRP/Map Rework/21 Fix Inn Lamp")]
    static void FixInnLamp()
    {
        var lamp = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("Elderboom_Streetlamp") && t.GetComponentInChildren<Renderer>() != null)
            .OrderBy(t => (XZ(Bounds(t.gameObject).center) - new Vector2(-28.3f, 1.1f)).sqrMagnitude).First();
        var b = Bounds(lamp.gameObject); var at = new Vector2(-33.5f, 1.6f);
        lamp.position += new Vector3(at.x - b.center.x, TerrainY(at.x, at.y) - b.min.y, at.y - b.center.z);
        EditorUtility.SetDirty(lamp);
        var scene = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        File.WriteAllText("Logs/lampfix.txt", lamp.name + " from " + b.center.ToString("F1") + " to " + at);
    }

    [MenuItem("Elderboom HDRP/Map Rework/22 Square Shots")]
    static void SquareShots()
    {
        var restore = Daylight();
        try
        {
            Shot("Logs/rework_shots/sq_A_maestra_west.png", new Vector3(-8, 0, 6), new Vector3(-30, 3, 0), 80f);
            Shot("Logs/rework_shots/sq_B_inn.png", new Vector3(-22, 0, 9), new Vector3(-24, 4, -10), 80f);
            Shot("Logs/rework_shots/sq_C_south.png", new Vector3(-12, 0, -26), new Vector3(10, 2, -32), 80f);
            Shot("Logs/rework_shots/sq_D_high.png", new Vector3(-30, 0, 20), new Vector3(-10, 0, -15), 70f, 22f);
        }
        finally { restore(); }
    }
}
