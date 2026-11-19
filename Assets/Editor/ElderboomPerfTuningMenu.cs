using UnityEditor;
using UnityEngine;

static class ElderboomPerfTuningMenu
{
    [MenuItem("Elderboom HDRP/Dev/Toggle Perf Tuning")]
    static void Toggle()
    {
        ElderboomPerfTuning.Enabled = !ElderboomPerfTuning.Enabled;
        Debug.Log("[PerfTuning] " + (ElderboomPerfTuning.Enabled ? "ON" : "OFF"));
    }

    [MenuItem("Elderboom HDRP/Dev/Toggle Atmosphere")]
    static void ToggleAtmosphere()
    {
        ElderboomAtmosphere.Enabled = !ElderboomAtmosphere.Enabled;
        Debug.Log("[Atmosphere] " + (ElderboomAtmosphere.Enabled ? "ON" : "OFF"));
    }

    // Saves the Game view to Logs/look_<tag>.png (Play mode).
    [MenuItem("Elderboom HDRP/Dev/Capture Look")]
    static void Capture()
    {
        string tag = (ElderboomAtmosphere.Enabled ? "atm1" : "atm0") + "_" + System.DateTime.Now.ToString("HHmmss");
        ScreenCapture.CaptureScreenshot("Logs/look_" + tag + ".png");
        Debug.Log("[Look] Logs/look_" + tag + ".png");
    }
}
