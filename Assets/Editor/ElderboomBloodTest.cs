using UnityEditor;
using UnityEngine;

// Elderboom HDRP > Dev > Blood Splat Test: in Play mode, drops the three ground splats in front of the camera.
static class ElderboomBloodTest
{
    [MenuItem("Elderboom HDRP/Dev/Blood Splat Test")]
    static void Run()
    {
        if (!Application.isPlaying) { Debug.LogWarning("[BloodTest] enter Play mode first"); return; }
        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam == null) return;
        Vector3 f = cam.transform.forward; f.y = 0f; f.Normalize();
        Vector3 r = cam.transform.right; r.y = 0f; r.Normalize();
        Vector3 basePos = cam.transform.position + f * 3.2f;
        ElderboomBloodSplat.FromHit(basePos - r * 1.4f + Vector3.up, f, true);
        ElderboomBloodSplat.PoolAt(basePos + Vector3.up, 1.2f);
        ElderboomBloodSplat.FromHit(basePos + r * 1.4f + Vector3.up, (f + r).normalized, true);
        Debug.Log("[BloodTest] spawned at " + basePos + " available=" + ElderboomBloodSplat.Available);
        double at = EditorApplication.timeSinceStartup + .6;
        EditorApplication.CallbackFunction report = null;
        report = () =>
        {
            if (EditorApplication.timeSinceStartup < at) return;
            EditorApplication.update -= report;
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>())
            {
                var root = ps.transform.root;
                if (!root.name.StartsWith("BloodSplat_")) continue;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                Debug.Log("[BloodTest] " + root.name + "/" + ps.name + " active=" + ps.gameObject.activeInHierarchy + " pos=" + ps.transform.position + " rot=" + ps.transform.eulerAngles
                    + " scale=" + ps.transform.lossyScale + " count=" + ps.particleCount + " playing=" + ps.isPlaying + " life=" + ps.main.startLifetime.constant + "/" + ps.main.startLifetime.constantMax
                    + " size=" + ps.main.startSize.constant + " mode=" + (r != null ? r.renderMode + " align=" + r.alignment + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.name + ":" + r.sharedMaterial.shader.name + " ok=" + r.sharedMaterial.shader.isSupported : "none") : "-"));
            }
        };
        EditorApplication.update += report;
    }
}
