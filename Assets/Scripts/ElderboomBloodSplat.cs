using System.Collections.Generic;
using UnityEngine;

// Ground blood from the Vefects "Free Blood VFX - HDRP" pack: thick, glossy, lit splats with normal maps.
// Hits leave a directional splat that streaks away from the shooter; kills leave a round pool plus a bunch of streaks.
// Prefabs are copies of the pack's "Once" floor effects in Resources/Elderboom/Blood (the pack folder is not in Resources).
// Instances are pooled and recycled oldest-first.
public static class ElderboomBloodSplat
{
    public enum Kind { Hit, Pool, Burst }
    const int MaxSplats = 48;
    const float HitInterval = .12f;
    // How long a splat stays on the ground: the pack's own erosion plays over this time.
    const float LifetimeScale = 1f;

    static readonly Dictionary<Kind, GameObject> prefabs = new Dictionary<Kind, GameObject>();
    static readonly List<GameObject> order = new List<GameObject>(); // oldest first
    static readonly Dictionary<GameObject, Kind> kinds = new Dictionary<GameObject, Kind>();
    static float lastHit;
    static bool loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { prefabs.Clear(); order.Clear(); kinds.Clear(); lastHit = 0f; loaded = false; }

    public static bool Available { get { Load(); return prefabs.Count > 0; } }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        Add(Kind.Hit, "Elderboom/Blood/Splat_Directional");
        Add(Kind.Pool, "Elderboom/Blood/Splat_Radial");
        Add(Kind.Burst, "Elderboom/Blood/Splat_Directional_Bunch");
    }

    static void Add(Kind kind, string path)
    {
        var prefab = Resources.Load<GameObject>(path);
        if (prefab != null) prefabs[kind] = prefab;
        else Debug.LogWarning("[BloodSplat] missing " + path);
    }

    // A shot on a zombie: a splat on the ground behind it, streaking away from the shooter.
    public static void FromHit(Vector3 hitPoint, Vector3 shotDirection, bool death)
    {
        if (!death && Time.time - lastHit < HitInterval) return;
        lastHit = Time.time;
        if (!Ground(hitPoint + Flat(shotDirection) * (death ? .35f : .5f), out var point, out var normal)) return;
        Spawn(death ? Kind.Burst : Kind.Hit, point, normal, shotDirection, death ? Random.Range(1.1f, 1.5f) : Random.Range(.7f, 1.1f));
    }

    // Under a body: a round pool.
    public static bool PoolAt(Vector3 near, float size)
    {
        if (!Ground(near, out var point, out var normal)) return false;
        return Spawn(Kind.Pool, point, normal, Random.insideUnitSphere, size * Random.Range(.9f, 1.3f)) != null;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-4f ? v.normalized : Vector3.forward; }

    static readonly RaycastHit[] hits = new RaycastHit[16];
    static bool Ground(Vector3 near, out Vector3 point, out Vector3 normal)
    {
        point = near; normal = Vector3.up;
        int count = Physics.RaycastNonAlloc(near + Vector3.up * .6f, Vector3.down, hits, 4f, ~((1 << 19) | (1 << 2)), QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var h = hits[i];
            if (h.distance >= best || h.rigidbody != null || h.collider.GetComponentInParent<ElderboomZombie>() != null || h.collider.GetComponentInParent<HDRPSlicePlayer>() != null) continue;
            best = h.distance; point = h.point; normal = h.normal; found = true;
        }
        return found && Vector3.Dot(normal, Vector3.up) > .6f;
    }

    static GameObject Spawn(Kind kind, Vector3 point, Vector3 normal, Vector3 direction, float scale)
    {
        Load();
        if (!prefabs.TryGetValue(kind, out var prefab)) return null;
        var go = Take(kind, prefab);
        Vector3 forward = Vector3.ProjectOnPlane(direction, normal);
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.ProjectOnPlane(Random.onUnitSphere, normal);
        go.transform.SetPositionAndRotation(point + normal * .01f, Quaternion.LookRotation(forward.normalized, normal));
        go.transform.localScale = Vector3.one * scale;
        go.SetActive(true);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Clear(false); ps.Play(false); }
        return go;
    }

    // Reuse a finished splat of this kind, else make one, else recycle the oldest one of this kind on the ground.
    static GameObject Take(Kind kind, GameObject prefab)
    {
        order.RemoveAll(g => g == null);
        GameObject pick = null;
        foreach (var g in order) if (kinds[g] == kind && !Alive(g)) { pick = g; break; }
        if (pick == null && order.Count < MaxSplats) pick = Make(kind, prefab);
        if (pick == null) foreach (var g in order) if (kinds[g] == kind) { pick = g; break; }
        if (pick == null) pick = Make(kind, prefab);
        order.Remove(pick); order.Add(pick);
        return pick;
    }

    static GameObject Make(Kind kind, GameObject prefab)
    {
        var go = Object.Instantiate(prefab);
        go.name = "BloodSplat_" + kind;
        Object.DontDestroyOnLoad(go);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.stopAction = ParticleSystemStopAction.None;
            main.startLifetimeMultiplier *= LifetimeScale;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        }
        foreach (var r in go.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }
        kinds[go] = kind;
        return go;
    }

    static bool Alive(GameObject g) { foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true)) if (ps.IsAlive(false)) return true; return false; }
}
