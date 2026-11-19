using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

static class ElderboomDuskProbe
{
    static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Elderboom_Unity_Port.unity");
        var sb = new StringBuilder();
        sb.AppendLine("== ROOTS");
        foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            sb.AppendLine("  " + go.name + " active=" + go.activeSelf + " children=" + go.transform.childCount + " layer=" + go.layer + " comps=" + string.Join(",", go.GetComponents<Component>().Select(c => c ? c.GetType().Name : "null")));
        sb.AppendLine("== LIGHTS");
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include).OrderBy(l => l.type))
            sb.AppendLine("  " + l.type + " " + PathOf(l.transform) + " int=" + l.intensity + " col=" + l.color + " temp=" + l.colorTemperature + " rot=" + l.transform.eulerAngles + " pos=" + l.transform.position + " range=" + l.range + " shadows=" + l.shadows + " on=" + l.gameObject.activeInHierarchy);
        sb.AppendLine("== VOLUMES");
        foreach (var v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
        {
            sb.AppendLine("  " + PathOf(v.transform) + " global=" + v.isGlobal + " prio=" + v.priority + " profile=" + (v.sharedProfile ? AssetDatabase.GetAssetPath(v.sharedProfile) + ":" + v.sharedProfile.name : "-"));
            if (v.sharedProfile) foreach (var c in v.sharedProfile.components) sb.AppendLine("     " + c.GetType().Name + " active=" + c.active);
        }
        sb.AppendLine("== TERRAINS");
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
        {
            var d = t.terrainData;
            sb.AppendLine("  " + PathOf(t.transform) + " pos=" + t.transform.position + " size=" + d.size + " hm=" + d.heightmapResolution + " am=" + d.alphamapResolution + " layer=" + t.gameObject.layer + " mat=" + (t.materialTemplate ? t.materialTemplate.name + "/" + t.materialTemplate.shader.name : "-") + " trees=" + d.treeInstanceCount + " details=" + d.detailPrototypes.Length + " asset=" + AssetDatabase.GetAssetPath(d));
            foreach (var l in d.terrainLayers) sb.AppendLine("     layer " + (l ? l.name + " tex=" + (l.diffuseTexture ? AssetDatabase.GetAssetPath(l.diffuseTexture) : "-") + " tile=" + l.tileSize : "null"));
            foreach (var p in d.treePrototypes) sb.AppendLine("     tree " + (p.prefab ? AssetDatabase.GetAssetPath(p.prefab) : "null"));
        }
        sb.AppendLine("== WATER / DECAL / REFLECTION / PARTICLES");
        foreach (var w in Object.FindObjectsByType<WaterSurface>(FindObjectsInactive.Include)) sb.AppendLine("  water " + PathOf(w.transform) + " pos=" + w.transform.position + " type=" + w.surfaceType + " scale=" + w.transform.lossyScale);
        foreach (var w in Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include).Take(12)) sb.AppendLine("  decal " + PathOf(w.transform) + " mat=" + (w.material ? w.material.name : "-"));
        sb.AppendLine("  decals total " + Object.FindObjectsByType<DecalProjector>(FindObjectsInactive.Include).Length);
        foreach (var w in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include)) sb.AppendLine("  probe " + PathOf(w.transform) + " mode=" + w.mode);
        foreach (var w in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include).Take(40)) sb.AppendLine("  ps " + PathOf(w.transform) + " pos=" + w.transform.position);
        sb.AppendLine("== LAYER 30 + sky objects");
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include).Where(r => r.gameObject.layer == 30).Take(60))
            sb.AppendLine("  " + PathOf(r.transform) + " mat=" + string.Join(",", r.sharedMaterials.Select(m => m ? m.name + "/" + m.shader.name : "null")) + " b=" + r.bounds.size);
        sb.AppendLine("== SOUTH CREEK objects (x -60..80, z -120..-45), second-level groups");
        var seen = new System.Collections.Generic.Dictionary<string, (int n, Bounds b)>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r is ParticleSystemRenderer || r.gameObject.layer == 30) continue;
            var c = r.bounds.center; if (c.x < -60 || c.x > 80 || c.z < -120 || c.z > -45) continue;
            if (r.bounds.size.x > 150) continue;
            var t = r.transform; var chain = new System.Collections.Generic.List<Transform>(); while (t) { chain.Insert(0, t); t = t.parent; }
            string key = string.Join("/", chain.Take(Mathf.Min(3, chain.Count)).Select(x => x.name));
            if (seen.TryGetValue(key, out var e)) { e.b.Encapsulate(r.bounds); seen[key] = (e.n + 1, e.b); } else seen[key] = (1, r.bounds);
        }
        foreach (var kv in seen.OrderBy(k => k.Key)) sb.AppendLine("  " + kv.Key + " n=" + kv.Value.n + " c=" + kv.Value.b.center.ToString("F1") + " s=" + kv.Value.b.size.ToString("F1"));
        sb.AppendLine("== RenderSettings sun=" + (RenderSettings.sun ? RenderSettings.sun.name : "-") + " skybox=" + (RenderSettings.skybox ? RenderSettings.skybox.name : "-"));
        var rp = GraphicsSettings.defaultRenderPipeline as HDRenderPipelineAsset;
        if (rp) sb.AppendLine("== HDRP asset " + AssetDatabase.GetAssetPath(rp) + " water=" + rp.currentPlatformRenderPipelineSettings.supportWater + " decals=" + rp.currentPlatformRenderPipelineSettings.supportDecals + " volumetrics=" + rp.currentPlatformRenderPipelineSettings.supportVolumetrics + " ssr=" + rp.currentPlatformRenderPipelineSettings.supportSSR);
        File.WriteAllText("Logs/dusk/probe.txt", sb.ToString());
        Debug.Log("DUSK_PROBE_DONE");
    }
}
