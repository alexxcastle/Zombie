using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

// Logs/claude_flash.txt -> describes the player's flashlight and the viewmodel lighting in the open scene.
static class ElderboomClaudeFlash
{
    const string Flag = "Logs/claude_flash.txt", Result = "Logs/claude_flash_result.txt";
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        File.Delete(Flag);
        var sb = new StringBuilder();
        try
        {
            var p = Object.FindFirstObjectByType<HDRPSlicePlayer>(FindObjectsInactive.Include);
            foreach (var l in p.GetComponentsInChildren<Light>(true))
            {
                var hd = l.GetComponent<HDAdditionalLightData>();
                sb.AppendLine(l.name + " type " + l.type + " parent " + l.transform.parent.name + " lp " + l.transform.localPosition.ToString("F3") + " le " + l.transform.localEulerAngles.ToString("F1") + " int " + l.intensity + " range " + l.range + " spot " + l.spotAngle + "/" + l.innerSpotAngle + " layers " + l.renderingLayerMask + " cull " + l.cullingMask + " shadows " + l.shadows + (hd ? " hdLayer " + hd.GetType().GetProperty("lightlayersMask")?.GetValue(hd) : ""));
            }
            var cams = p.GetComponentsInChildren<Camera>(true);
            foreach (var c in cams) sb.AppendLine("cam " + c.name + " near " + c.nearClipPlane + " cull " + c.cullingMask);
            var r = p.GetComponentsInChildren<Renderer>(true).FirstOrDefault(x => x.name.Contains("Arms") || x.name.Contains("arm"));
            if (r) sb.AppendLine("viewmodel renderer " + r.name + " layer " + LayerMask.LayerToName(r.gameObject.layer) + " renderingLayerMask " + r.renderingLayerMask);
        }
        catch (System.Exception e) { sb.AppendLine("FAIL " + e); }
        File.WriteAllText(Result, sb.ToString());
    }
}
