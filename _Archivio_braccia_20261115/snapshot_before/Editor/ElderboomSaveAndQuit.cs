using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

// [InitializeOnLoad] removed 31 Oct 2026: the saved scene is the source of truth.
// Flag files no longer trigger rebuilds, builds or Editor exits when Unity opens.
public static class ElderboomSaveAndQuit
{
    const string Flag = "Logs/save_and_quit.txt";

    static ElderboomSaveAndQuit()
    {
        if (!File.Exists(Flag)) return;
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
            return;
        }
        EditorApplication.update -= Tick;
        File.Delete(Flag);
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path) || !scene.isDirty) continue;
            EditorSceneManager.SaveScene(scene);
        }
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
