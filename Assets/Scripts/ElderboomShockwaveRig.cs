// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public sealed class ElderboomShockwaveRig : MonoBehaviour
{
    public bool captureOnPlay;
    public Vector3 origin;

    IEnumerator Start()
    {
        if (!captureOnPlay) yield break;
        var report = new StringBuilder();
        string dir = "Logs/shockwave_preview";
        var camera = GetComponent<Camera>();
        for (int i = 0; i < 12; i++) yield return null;
        Directory.CreateDirectory(dir);
            report.AppendLine("gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
            var zombie = GameObject.Find("Zombie");
            report.AppendLine("zombie=" + (zombie != null));
            if (zombie != null)
            {
                var bounds = new Bounds(zombie.transform.position, Vector3.one * 0.2f);
                bool any = false;
                foreach (var renderer in zombie.GetComponentsInChildren<Renderer>(true))
                {
                    if (!any) { bounds = renderer.bounds; any = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                report.AppendLine("zombieBounds=" + bounds.center.ToString("F2") + " size=" + bounds.size.ToString("F2"));
            }
            report.AppendLine("before " + ElderboomAbilityShots.Capture(camera, dir + "/00_before.png"));
            var wave = ElderboomImportedVfx.Shockwave(origin);
            report.AppendLine("wave=" + (wave != null) + " origin=" + origin.ToString("F2"));
            if (wave != null)
            {
                var systems = wave.GetComponentsInChildren<ParticleSystem>(true);
                var renderers = wave.GetComponentsInChildren<ParticleSystemRenderer>(true);
                report.AppendLine("systems=" + systems.Length + " renderers=" + renderers.Length);
                if (renderers.Length > 0 && renderers[0].sharedMaterial != null && renderers[0].sharedMaterial.shader != null)
                    report.AppendLine("shader=" + renderers[0].sharedMaterial.shader.name);
            }
            float[] times = { 0.22f, 0.4f, 0.72f };
            float elapsed = 0f;
            int shot = 0;
            while (shot < times.Length)
            {
                yield return null;
                elapsed += Time.deltaTime;
                if (elapsed < times[shot]) continue;
                int count = 0;
                if (wave != null)
                {
                    foreach (var system in wave.GetComponentsInChildren<ParticleSystem>(true))
                        count += system.particleCount;
                }
                string path = dir + "/0" + (shot + 1) + "_shockwave.png";
                report.AppendLine("t=" + elapsed.ToString("0.00") + " particles=" + count + " " + ElderboomAbilityShots.Capture(camera, path));
                shot++;
            }
        Directory.CreateDirectory(dir);
        File.WriteAllText(dir + "/report.txt", report.ToString());
        File.WriteAllText(dir + "/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
#endif
