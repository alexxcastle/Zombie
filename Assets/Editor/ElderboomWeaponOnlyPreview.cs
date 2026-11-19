using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Logs/claude_weaponpreview.txt -> renders every weapon built by ElderboomWeaponOnly (no arms) at hip and aimed.
// Lines: weapon ids (empty = the game's catalogue). Output: Logs/weapon_preview/<id>.png (hip | aim), report
// Logs/claude_weaponpreview_result.txt.
static class ElderboomWeaponOnlyPreview
{
    const string Flag = "Logs/claude_weaponpreview.txt";
    const int W = 640, H = 360;
    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 1.5;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var ids = File.ReadAllLines(Flag).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        File.Delete(Flag);
        var sb = new StringBuilder();
        try { Run(ids, sb); } catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText("Logs/claude_weaponpreview_result.txt", sb.ToString());
    }
    static void Run(string[] ids, StringBuilder sb)
    {
        if (ids.Length == 0) ids = ElderboomWeapons.All.Select(w => w.id).ToArray();
        var stage = new GameObject("__WeaponPreview"); stage.transform.position = new Vector3(0, 2000, 0);
        try
        {
            var camGo = new GameObject("Cam"); camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>(); cam.nearClipPlane = .01f; cam.farClipPlane = 50f;
            cam.fieldOfView = Camera.HorizontalToVerticalFieldOfView(ElderboomPauseMenu.DefaultHorizontalFov, (float)W / H);
            var hd = camGo.AddComponent<HDAdditionalCameraData>(); hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = new Color(.16f, .17f, .19f);
            var vol = stage.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10000f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>(); vol.sharedProfile = profile;
            var ex = profile.Add<Exposure>(true); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(9.6f);
            var key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(stage.transform, false); key.type = LightType.Directional;
            key.transform.localRotation = Quaternion.Euler(40, -35, 0); key.gameObject.AddComponent<HDAdditionalLightData>(); key.intensity = 7000;
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(stage.transform, false); fill.type = LightType.Directional;
            fill.transform.localRotation = Quaternion.Euler(10, 150, 0); fill.gameObject.AddComponent<HDAdditionalLightData>(); fill.intensity = 2500;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 }; cam.targetTexture = rt;
            var img = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
            Directory.CreateDirectory("Logs/weapon_preview");
            foreach (var id in ids)
            {
                var w = ElderboomWeapons.Find(id);
                if (w == null) { sb.AppendLine(id + ": not in catalogue"); continue; }
                var root = ElderboomWeaponOnly.Build(w, cam.transform);
                if (root == null) { sb.AppendLine(id + ": build failed"); continue; }
                root.gameObject.SetActive(true);
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = camGo.layer;
                var v = root.GetComponent<ElderboomViewmodel>();
                int visible = root.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled), hidden = root.GetComponentsInChildren<Renderer>(true).Count(r => !r.enabled);
                for (int pass = 0; pass < 2; pass++)
                {
                    root.localPosition = pass == 0 ? v.hipPosition : v.adsPosition; root.localRotation = pass == 0 ? v.hipRotation : v.adsRotation;
                    cam.Render(); RenderTexture.active = rt; img.ReadPixels(new Rect(0, 0, W, H), pass * W, 0); RenderTexture.active = null;
                }
                img.Apply(); File.WriteAllBytes("Logs/weapon_preview/" + id + ".png", img.EncodeToPNG());
                sb.AppendLine(id + ": " + root.name + " renderers " + visible + " visible, " + hidden + " hidden (arms: " + string.Join(",", root.GetComponentsInChildren<Renderer>(true).Where(r => !r.enabled).Select(r => r.name).Take(6)) + ") hasAds " + v.hasAds + " hip " + v.hipPosition.ToString("F3"));
                Object.DestroyImmediate(root.gameObject);
            }
            cam.targetTexture = null; rt.Release();
        }
        finally { Object.DestroyImmediate(stage); }
    }
}
