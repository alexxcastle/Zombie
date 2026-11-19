using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;

// Explicit one-shot request. Refuses to touch a manually edited open scene.
[InitializeOnLoad]
static class ElderboomGeneratedGatePlacement
{
    const string Request="Logs/ArchitectureKit/place-gates.request";
    static double next;
    static ElderboomGeneratedGatePlacement(){EditorApplication.update+=Poll;}
    static void Poll()
    {
        if(EditorApplication.timeSinceStartup<next)return;next=EditorApplication.timeSinceStartup+2;
        if(!File.Exists(Request)||EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlayingOrWillChangePlaymode)return;
        File.Delete(Request);
        try
        {
            var scene=EditorSceneManager.GetActiveScene();
            if(scene.path!="Assets/Scenes/Elderboom_Unity_Port.unity")throw new InvalidOperationException("Open Elderboom_Unity_Port before placing gates.");
            if(scene.isDirty)throw new InvalidOperationException("Scene has unsaved manual changes; placement not run.");
            ElderboomAccessLayoutBuilder.Rebuild();
            ElderboomAccessLayoutBuilder.Validate();
            File.WriteAllText("Logs/ArchitectureKit/place-gates.complete",DateTime.UtcNow.ToString("O"));
        }
        catch(Exception e){File.WriteAllText("Logs/ArchitectureKit/place-gates.error",e.ToString());}
    }
}
