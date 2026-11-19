// Capture tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

public sealed class ElderboomValleyShot : MonoBehaviour
{
    public bool captureOnPlay;
    public bool satellite;

    IEnumerator Start()
    {
        if (!captureOnPlay) yield break;
        if (satellite)
        {
            yield return Satellite();
            yield break;
        }
        var camera = GetComponent<Camera>();
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            if (other != camera) other.enabled = false;
        for (int i = 0; i < 24; i++) yield return null;
        string dir = "Logs/valley_preview";
        Directory.CreateDirectory(dir);
        var report = new System.Text.StringBuilder();
        report.AppendLine("gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
        transform.position = new Vector3(0f, 2.6f, -8f);
        transform.LookAt(new Vector3(12f, 3f, 24f));
        yield return null;
        report.AppendLine("village " + ElderboomAbilityShots.Capture(camera, dir + "/00_village.png"));
        transform.position = new Vector3(8f, 9f, 96f);
        transform.LookAt(new Vector3(8f, 30f, 176f));
        yield return null;
        report.AppendLine("ridge " + ElderboomAbilityShots.Capture(camera, dir + "/01_ridge.png"));
        File.WriteAllText(dir + "/report.txt", report.ToString());
        File.WriteAllText(dir + "/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    IEnumerator Satellite()
    {
        var camera = GetComponent<Camera>();
        foreach (var other in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            if (other != camera) other.enabled = false;
        for (int i = 0; i < 12; i++) yield return null;
        camera.orthographic = true;
        camera.orthographicSize = 350f;
        camera.aspect = 1f;
        camera.nearClipPlane = 1f;
        camera.farClipPlane = 900f;
        var center = new Vector3(5.7f, 0f, -0.7f);
        transform.position = center + Vector3.up * 520f;
        transform.LookAt(center, Vector3.forward);
        yield return null;
        string dir = "Logs/valley_preview";
        Directory.CreateDirectory(dir);
        string stats = ElderboomAbilityShots.Capture(camera, dir + "/satellite.png", 1536, 1536);
        File.WriteAllText(dir + "/satellite_report.txt", "gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName + "\n" + stats + "\n");
        File.WriteAllText(dir + "/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
#endif
