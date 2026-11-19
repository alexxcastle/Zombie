using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Windows builds use Direct3D 11: with HDRP it compiles pipeline states up front instead of stuttering
// the first time an effect appears (D3D12), and it is the most stable path on mid-range PCs.
[InitializeOnLoad]
static class ElderboomWindowsGraphics
{
    static ElderboomWindowsGraphics() { Apply(); } // immediate, so a -batchmode build already gets it
    static void Apply()
    {
        var target = BuildTarget.StandaloneWindows64;
        var apis = PlayerSettings.GetGraphicsAPIs(target);
        if (!PlayerSettings.GetUseDefaultGraphicsAPIs(target) && apis.Length == 1 && apis[0] == GraphicsDeviceType.Direct3D11) return;
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11 });
        Debug.Log("[WindowsGraphics] Windows builds now use Direct3D 11");
    }
}
