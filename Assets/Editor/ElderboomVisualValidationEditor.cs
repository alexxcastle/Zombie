using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
public static class ElderboomVisualValidationEditor
{
    const string Method="ElderboomVisualValidationEditor.Run";
    public static void Run(){ElderboomVisualRepair.Apply();EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");EditorApplication.playModeStateChanged+=OnPlay;EditorApplication.isPlaying=true;}
    [InitializeOnLoadMethod]static void Restore(){if(Environment.GetCommandLineArgs().Contains(Method)){EditorApplication.playModeStateChanged-=OnPlay;EditorApplication.playModeStateChanged+=OnPlay;}}
    static void OnPlay(PlayModeStateChange state){if(state==PlayModeStateChange.EnteredPlayMode)ElderboomVisualValidation.StartEditor();}
}
