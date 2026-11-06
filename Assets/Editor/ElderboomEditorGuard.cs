using UnityEditor;
using UnityEngine;

// Generators are one-off import tools now; the saved scene and prefabs are the
// source of truth. Every destructive menu entry asks first (batch mode proceeds).
public static class ElderboomEditorGuard
{
    public static bool Confirm(string title, string message)
        => Application.isBatchMode || EditorUtility.DisplayDialog(title, message + "\n\nContinue?", "Overwrite", "Cancel");
}
