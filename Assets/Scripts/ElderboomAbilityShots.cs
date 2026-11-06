// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Render a camera to a PNG (used by the capture/validation tools).
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
