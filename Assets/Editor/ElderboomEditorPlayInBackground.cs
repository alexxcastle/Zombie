using UnityEditor;
using UnityEngine;

// Editor only: Play mode keeps running while the Editor is behind other windows,
// so tests and visual checks do not stall. Player builds keep the project setting
// (Run In Background off). Toggle: Elderboom HDRP > Editor > Play In Background.
[InitializeOnLoad]
static class ElderboomEditorPlayInBackground
{
    const string Key = "Elderboom.PlayInBackground";
    const string MenuPath = "Elderboom HDRP/Editor/Play In Background";

    static ElderboomEditorPlayInBackground()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && EditorPrefs.GetBool(Key, true)) Application.runInBackground = true;
        };
    }

    [MenuItem(MenuPath)]
    static void Toggle() => EditorPrefs.SetBool(Key, !EditorPrefs.GetBool(Key, true));

    [MenuItem(MenuPath, true)]
    static bool ToggleValidate() { Menu.SetChecked(MenuPath, EditorPrefs.GetBool(Key, true)); return true; }
}
