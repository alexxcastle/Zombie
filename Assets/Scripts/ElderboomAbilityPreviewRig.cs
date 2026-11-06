// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ElderboomAbilityPreviewRig : MonoBehaviour
{
    public bool captureOnPlay;

    IEnumerator Start()
    {
        if (!captureOnPlay) yield break;
        yield return null;
        var camera = GetComponent<Camera>();
        var views = GetComponentsInChildren<ElderboomAbilityView>(true);
        var report = new StringBuilder();
        report.AppendLine("play gfx=" + SystemInfo.graphicsDeviceType + " views=" + views.Length);
        foreach (var view in views)
        {
            foreach (var other in views)
                if (other != null) other.gameObject.SetActive(other == view);
            foreach (var skin in view.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
            float length = view != null ? view.Length : 1f;
            float[] times = { 0.02f, length * 0.35f, length * 0.65f, Mathf.Max(0.08f, length - 0.08f) };
            for (int i = 0; i < times.Length; i++)
            {
                view.Sample(times[i]);
                yield return null;
                string path = "Logs/ability_preview/play_" + view.gameObject.name + "_" + i + ".png";
                string stats = ElderboomAbilityShots.Capture(camera, path);
                report.AppendLine(view.gameObject.name + " t=" + times[i].ToString("0.00") + " hand=" + Marker(view).ToString("F3") + " " + stats);
            }
            view.gameObject.SetActive(false);
        }
        Directory.CreateDirectory("Logs/ability_preview");
        File.AppendAllText("Logs/ability_preview/report.txt", report.ToString());
        File.WriteAllText("Logs/ability_preview/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static Vector3 Marker(ElderboomAbilityView view)
    {
        if (view == null || view.model == null) return Vector3.zero;
        var hand = Find(view.model.transform, "Hand.L");
        return hand != null ? hand.localPosition : view.model.transform.localPosition;
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == name) return node;
        return null;
    }
}

public static class ElderboomAbilityShots
{
    public static string Capture(Camera camera, string path, int width = 1280, int height = 720)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Logs/ability_preview");
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        if (!rt.Create())
        {
            Object.DestroyImmediate(rt);
            return "RenderTexture.Create failed";
        }
        var previous = camera.targetTexture;
        var request = new RenderPipeline.StandardRequest { destination = rt };
        for (int i = 0; i < 4; i++) RenderPipeline.SubmitRenderRequest(camera, request);
        string stats = Read(rt, path, "request");
        if (stats.Contains("FLAT"))
        {
            camera.targetTexture = rt;
            camera.Render();
            stats = Read(rt, path, "camera.Render");
            camera.targetTexture = previous;
        }
        rt.Release();
        Object.DestroyImmediate(rt);
        return stats;
    }

    static string Read(RenderTexture rt, string path, string source)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        var pixels = image.GetPixels32();
        Object.DestroyImmediate(image);
        RenderTexture.active = previous;
        double r = 0, g = 0, b = 0;
        int step = 16;
        int count = 0;
        for (int i = 0; i < pixels.Length; i += step)
        {
            r += pixels[i].r; g += pixels[i].g; b += pixels[i].b; count++;
        }
        r /= count; g /= count; b /= count;
        double variance = 0;
        for (int i = 0; i < pixels.Length; i += step)
        {
            double dr = pixels[i].r - r, dg = pixels[i].g - g, db = pixels[i].b - b;
            variance += dr * dr + dg * dg + db * db;
        }
        variance = System.Math.Sqrt(variance / count);
        return source + " mean=" + r.ToString("0") + "," + g.ToString("0") + "," + b.ToString("0") + " spread=" + variance.ToString("0.0") + (variance < 2.0 ? " FLAT" : " OK");
    }
}
#endif
