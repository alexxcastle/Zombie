using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Hand-authoring helpers for the Elderboom scene (the saved scene is the source of truth).
public static class ElderboomLevelTools
{
    [MenuItem("Elderboom HDRP/Level/Create Areas From Gates")]
    static void CreateAreas()
    {
        if (Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Include).Length > 0
            && !EditorUtility.DisplayDialog("Areas already exist", "The scene already has areas. Add missing gate areas anyway?", "Add", "Cancel")) return;
        var root = GameObject.Find("Level_Areas") ?? new GameObject("Level_Areas");
        Undo.RegisterCreatedObjectUndo(root, "Create areas");
        var spawns = Object.FindObjectsByType<ElderboomSpawnPoint>(FindObjectsInactive.Include);
        if (ElderboomAreaNamed("Village") == null && spawns.Length > 0)
        {
            var bounds = new Bounds(spawns[0].transform.position, Vector3.zero);
            foreach (var spawn in spawns) bounds.Encapsulate(spawn.transform.position);
            var village = Create(root.transform, "Village", true, bounds.center, bounds.size + new Vector3(10f, 12f, 10f));
            village.transform.SetAsFirstSibling();
        }
        foreach (var gate in Object.FindObjectsByType<ElderboomGate>(FindObjectsInactive.Include))
            if (!string.IsNullOrEmpty(gate.areaName) && ElderboomAreaNamed(gate.areaName) == null)
                Create(root.transform, gate.areaName, false, gate.transform.position, new Vector3(30f, 12f, 30f));
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
        Debug.Log("Elderboom areas created. Resize each gate area to cover the zone behind its gate, then set spawn points' 'area' or place them inside the boxes.");
    }

    [MenuItem("Elderboom HDRP/Level/Add Spawn Point At Scene View")]
    static void AddSpawn() => AddAtPivot("ZombieSpawn", go => go.AddComponent<ElderboomSpawnPoint>());

    [MenuItem("Elderboom HDRP/Level/Add Scripted Trigger At Scene View")]
    static void AddTrigger() => AddAtPivot("ScriptedTrigger", go => { var box = go.AddComponent<BoxCollider>(); box.size = new Vector3(4f, 3f, 4f); box.center = Vector3.up * 1.5f; go.AddComponent<ElderboomTrigger>(); });

    static void AddAtPivot(string name, System.Action<GameObject> setup)
    {
        var go = new GameObject(name);
        var view = SceneView.lastActiveSceneView;
        if (view != null) go.transform.position = view.pivot;
        setup(go);
        Undo.RegisterCreatedObjectUndo(go, "Add " + name);
        Selection.activeGameObject = go;
        EditorSceneManager.MarkSceneDirty(go.scene);
    }

    static ElderboomArea ElderboomAreaNamed(string name)
        => Object.FindObjectsByType<ElderboomArea>(FindObjectsInactive.Include).FirstOrDefault(a => a.areaName == name);

    static ElderboomArea Create(Transform parent, string name, bool unlocked, Vector3 center, Vector3 size)
    {
        var go = new GameObject("Area_" + name.Replace(' ', '_'));
        go.transform.SetParent(parent, false);
        go.transform.position = center;
        var area = go.AddComponent<ElderboomArea>();
        area.areaName = name; area.unlockedAtStart = unlocked; area.size = size;
        Undo.RegisterCreatedObjectUndo(go, "Create area");
        return area;
    }
}
