using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

// A separate surface pass preserves the original PBR textures, normals and arms.
// Object-space veins follow both rigid and skinned animated weapon parts.
public sealed class ElderboomUpgradedWeapon : MonoBehaviour
{
    readonly List<GameObject> overlays = new List<GameObject>();
    static Material veinMaterial;
    static Material VeinMaterial
    {
        get
        {
            if (veinMaterial != null) return veinMaterial;
            var shader = Resources.Load<Shader>("Elderboom/Shaders/CrimsonVeins");
            if (shader == null) return null;
            veinMaterial = new Material(shader) { name = "Crimson weapon veins", hideFlags = HideFlags.DontSave };
            return veinMaterial;
        }
    }

    public static Renderer[] WeaponRenderers(Transform root, ElderboomWeaponDefinition definition, string operatorId)
    {
        var holders = root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("OperatorWeapon_")).ToArray();
        var op = ElderboomOperators.Find(operatorId);
        var own = op != null && definition != null && definition.id == op.primaryWeapon;
        var named = own ? root.GetComponentsInChildren<Transform>(true)
            .Where(t => System.Array.IndexOf(op.stowedWeaponNodes, t.name) >= 0).ToArray() : new Transform[0];
        var include = holders.Length > 0 ? holders : named;
        return root.GetComponentsInChildren<Renderer>(true).Where(r =>
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) return false;
            if (r.name.StartsWith("CrimsonSurface_")) return false;
            if (!r.enabled || !r.gameObject.activeInHierarchy) return false;
            if (include.Length > 0) return include.Any(t => r.transform == t || r.transform.IsChildOf(t));
            string n = r.name.ToLowerInvariant();
            return !(n.StartsWith("p_") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("body")
                || n.Contains("arm") && !n.Contains("firearm") || n.Contains("hand") && !n.Contains("guard") && !n.Contains("handle"));
        }).ToArray();
    }

    public static void Apply(Transform root, ElderboomWeaponDefinition definition, string operatorId, bool upgraded)
    {
        if (root == null) return;
        var effect = root.GetComponent<ElderboomUpgradedWeapon>();
        if (!upgraded) { if (effect != null) effect.enabled = false; return; }
        if (effect == null) effect = root.gameObject.AddComponent<ElderboomUpgradedWeapon>();
        if (effect.overlays.Count == 0)
            foreach (var renderer in WeaponRenderers(root, definition, operatorId)) effect.Add(renderer);
        effect.enabled = true;
    }

    void Add(Renderer source)
    {
        var material = VeinMaterial; if (material == null) return;
        var go = new GameObject("CrimsonSurface_" + source.name); go.layer = source.gameObject.layer;
        go.transform.SetParent(source.transform, false);
        Renderer overlay; Mesh mesh;
        if (source is SkinnedMeshRenderer skin)
        {
            var copy = go.AddComponent<SkinnedMeshRenderer>(); mesh = skin.sharedMesh;
            copy.sharedMesh = mesh; copy.bones = skin.bones; copy.rootBone = skin.rootBone;
            copy.localBounds = skin.localBounds; copy.updateWhenOffscreen = true; overlay = copy;
        }
        else
        {
            var filter = source.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) { Destroy(go); return; }
            mesh = filter.sharedMesh; go.AddComponent<MeshFilter>().sharedMesh = mesh; overlay = go.AddComponent<MeshRenderer>();
        }
        overlay.sharedMaterials = Enumerable.Repeat(material, mesh.subMeshCount).ToArray();
        overlay.shadowCastingMode = ShadowCastingMode.Off; overlay.receiveShadows = false;
        var block = new MaterialPropertyBlock();
        block.SetFloat("_VeinScale", 13f / Mathf.Max(.001f, Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z))));
        overlay.SetPropertyBlock(block); overlays.Add(go);
    }
    void OnEnable() { foreach (var go in overlays) if (go != null) go.SetActive(true); }
    void OnDisable() { foreach (var go in overlays) if (go != null) go.SetActive(false); }

    public static GameObject CreateDisplay(Transform root, ElderboomWeaponDefinition definition, string operatorId, Transform socket)
    {
        var sources = WeaponRenderers(root, definition, operatorId);
        if (sources.Length == 0) return null;
        var display = new GameObject("Deposited_" + definition.id); display.transform.SetParent(socket, false);
        var lifetime = display.AddComponent<ElderboomWeaponDisplay>();
        var content = new GameObject("Weapon").transform; content.SetParent(display.transform, false);
        var bounds = new Bounds(); bool first = true;
        foreach (var source in sources)
        {
            Mesh mesh;
            if (source is SkinnedMeshRenderer skin)
            {
                mesh = new Mesh(); skin.BakeMesh(mesh); lifetime.meshes.Add(mesh);
            }
            else mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            // Freeze the current pose into the display root's original coordinate system.
            var baked = new Mesh();
            baked.indexFormat = IndexFormat.UInt32;
            var matrix = root.worldToLocalMatrix * source.transform.localToWorldMatrix;
            var parts = Enumerable.Range(0, mesh.subMeshCount).Select(i => new CombineInstance { mesh = mesh, subMeshIndex = i, transform = matrix }).ToArray();
            baked.CombineMeshes(parts, false, true); lifetime.meshes.Add(baked);
            var go = new GameObject(source.name); go.transform.SetParent(content, false);
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            go.AddComponent<MeshRenderer>().sharedMaterials = source.sharedMaterials;
            if (first) { bounds = baked.bounds; first = false; } else bounds.Encapsulate(baked.bounds);
        }
        if (first) { Destroy(display); return null; }
        // Longest horizontal axis lies across the tray; all weapon types fit its recess.
        float max = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        var align = bounds.size.z > bounds.size.x ? Quaternion.Euler(0, 90, 0) : Quaternion.identity;
        var rotation = Quaternion.Euler(90, 0, 0) * align;
        float length = definition.category == ElderboomIds.Category.Handgun ? .38f : definition.category == ElderboomIds.Category.AssaultRifle ? .92f : .85f;
        float scale = Mathf.Clamp(length / Mathf.Max(.01f, max), .001f, 100f);
        content.localRotation = rotation; content.localScale = Vector3.one * scale;
        content.localPosition = -(rotation * bounds.center) * scale;
        display.transform.localPosition = new Vector3(0, .12f, 0);
        return display;
    }
}

public sealed class ElderboomWeaponDisplay : MonoBehaviour
{
    public readonly List<Mesh> meshes = new List<Mesh>();
    void OnDestroy() { foreach (var mesh in meshes) if (mesh != null) Destroy(mesh); }
}
