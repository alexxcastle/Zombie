// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.VFX;

public sealed class ElderboomNightVfxRig : MonoBehaviour
{
    public bool captureOnPlay;

    IEnumerator Start()
    {
        if (!captureOnPlay) yield break;
        var camera = GetComponent<Camera>();
        var report = new StringBuilder();
        string dir = "Logs/night_vfx_preview";
        for (int i = 0; i < 16; i++) yield return null;
        Directory.CreateDirectory(dir);
        report.AppendLine("gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
        report.AppendLine("sky " + ElderboomAbilityShots.Capture(camera, dir + "/00_sky.png"));

        camera.transform.position = new Vector3(1.4f, 1.65f, -5.2f);
        camera.transform.LookAt(new Vector3(0f, 1.15f, 1.2f));
        yield return null;
        ElderboomImportedVfx.Blood(new Vector3(0.35f, 0.02f, 1.15f), false);
        ElderboomImportedVfx.Blood(new Vector3(-0.45f, 0.9f, 1.2f), true);
        var plasma = ElderboomImportedVfx.Fireball(new Vector3(0f, 1.35f, -0.4f), Vector3.forward);
        report.AppendLine("plasma=" + (plasma != null));
        if (plasma != null)
        {
            var visual = plasma.GetComponent<VisualEffect>();
            report.AppendLine("vfx=" + (visual != null) + " asset=" + (visual != null && visual.visualEffectAsset != null));
        }
        float elapsed = 0f;
        float[] times = { 0.2f, 0.55f };
        int shot = 0;
        while (shot < times.Length)
        {
            yield return null;
            elapsed += Time.deltaTime;
            if (elapsed < times[shot]) continue;
            int particles = 0;
            int alive = 0;
            foreach (var system in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
                particles += system.particleCount;
            foreach (var visual in Object.FindObjectsByType<VisualEffect>(FindObjectsSortMode.None))
                alive += visual.aliveParticleCount;
            report.AppendLine("t=" + elapsed.ToString("0.00") + " particles=" + particles + " vfxAlive=" + alive + " " + ElderboomAbilityShots.Capture(camera, dir + "/0" + (shot + 1) + "_vfx.png"));
            shot++;
        }
        File.AppendAllText(dir + "/report.txt", report.ToString());
        File.WriteAllText(dir + "/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
#endif
