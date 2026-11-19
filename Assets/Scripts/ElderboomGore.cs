using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Gore: dismemberment and ground blood.
// A limb is severed by baking the zombie's skinned mesh twice, before and after collapsing the limb's bone: the
// vertices that moved are the limb. They become a loose physics gib flung away from the shot, the bone stays
// collapsed on the body (ElderboomSevered keeps it so against the animation) and the stump spurts blood.
// No read/write access to the source meshes is needed: only the baked copies are read.
// Ground: dark blood pools (pooled quads) under kills and heavy hits.
public static class ElderboomGore
{
    static readonly HumanBodyBones[] Limbs =
    {
        HumanBodyBones.Head, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm, HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };
    static readonly Dictionary<HumanBodyBones, string[]> Names = new Dictionary<HumanBodyBones, string[]>
    {
        { HumanBodyBones.Head, new[] { "head" } },
        { HumanBodyBones.LeftLowerArm, new[] { "leftforearm", "forearm.l", "lowerarm_l", "forearm_l", "l_forearm" } },
        { HumanBodyBones.RightLowerArm, new[] { "rightforearm", "forearm.r", "lowerarm_r", "forearm_r", "r_forearm" } },
        { HumanBodyBones.LeftUpperArm, new[] { "leftarm", "upper_arm.l", "upperarm_l", "l_upperarm" } },
        { HumanBodyBones.RightUpperArm, new[] { "rightarm", "upper_arm.r", "upperarm_r", "r_upperarm" } },
        { HumanBodyBones.LeftLowerLeg, new[] { "leftleg", "shin.l", "calf_l", "lowerleg_l", "l_calf" } },
        { HumanBodyBones.RightLowerLeg, new[] { "rightleg", "shin.r", "calf_r", "lowerleg_r", "r_calf" } },
    };
    const int MaxGibs = 36;
    static readonly Queue<GameObject> gibs = new Queue<GameObject>();
    static int frame, severedThisFrame;

    // Killing blow. Big hits and blasts take more off; a head shot often takes the head.
    public static void OnKill(Component zombie, Animator animator, Vector3 hitPoint, Vector3 origin, float damage, bool blast)
    {
        Pool(hitPoint, 1f);
        Vector3 dir = hitPoint - origin; dir.y = Mathf.Max(dir.y, 0f); if (dir.sqrMagnitude < .001f) dir = zombie.transform.forward * -1f; dir.Normalize();
        float chance = blast ? 1f : damage >= 60f ? .9f : .65f;
        if (Random.value > chance) return;
        var first = Nearest(zombie.transform, animator, hitPoint, blast ? 9f : .45f);
        if (first == null && !blast) first = Bone(zombie.transform, animator, Random.value < .5f ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        Sever(zombie, animator, first, dir, blast ? 6.5f : 3.6f);
        if (blast)
            for (int i = 0; i < 2; i++)
                if (Random.value < .7f) Sever(zombie, animator, Bone(zombie.transform, animator, Limbs[Random.Range(1, Limbs.Length)]), (dir + Random.insideUnitSphere * .6f).normalized, 5.5f);
    }

    // Heavy hit on a living zombie: sometimes an arm comes off (it keeps coming).
    public static void OnHit(Component zombie, Animator animator, Vector3 hitPoint, Vector3 origin, float damage)
    {
        if (damage >= 35f && Random.value < .35f) Pool(hitPoint, .55f);
        if (damage < 40f || Random.value > .22f) return;
        var bone = Nearest(zombie.transform, animator, hitPoint, .4f);
        if (bone == null) return;
        string n = bone.name.ToLowerInvariant();
        if (!(n.Contains("arm") || n.Contains("forearm"))) return;
        Vector3 dir = (hitPoint - origin).normalized;
        Sever(zombie, animator, bone, dir, 3f);
    }

    static Transform Bone(Transform root, Animator animator, HumanBodyBones which)
    {
        if (animator != null && animator.isHuman) { var b = animator.GetBoneTransform(which); if (b != null) return b; }
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            string n = t.name.ToLowerInvariant().Replace("mixamorig:", "").Replace("mixamorig_", "").Replace(" ", "");
            foreach (var key in Names[which]) if (n == key || n.EndsWith(key)) return t;
        }
        return null;
    }

    static Transform Nearest(Transform root, Animator animator, Vector3 point, float maxDistance)
    {
        Transform best = null; float bestD = maxDistance;
        foreach (var limb in Limbs)
        {
            var b = Bone(root, animator, limb); if (b == null || b.localScale.x < .01f) continue;
            Vector3 end = b.childCount > 0 ? b.GetChild(0).position : b.position + (b.position - b.parent.position) * .6f;
            if (limb == HumanBodyBones.Head) end = b.position + Vector3.up * .22f;
            float d = DistanceToSegment(point, b.position, end);
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        return Vector3.Distance(p, a + ab * t);
    }

    public static bool Sever(Component zombie, Animator animator, Transform bone, Vector3 dir, float speed)
    {
        if (bone == null || bone.localScale.x < .01f) return false;
        if (frame != Time.frameCount) { frame = Time.frameCount; severedThisFrame = 0; }
        if (severedThisFrame >= 3) return false; // a grenade into a crowd: keep the frame
        severedThisFrame++;
        // The most detailed skinned renderer that the bone drives (LOD0).
        SkinnedMeshRenderer skin = null; int bestVerts = 0;
        foreach (var smr in zombie.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null || smr.bones == null || System.Array.IndexOf(smr.bones, bone) < 0 && !Drives(smr, bone)) continue;
            if (smr.sharedMesh.vertexCount > bestVerts) { bestVerts = smr.sharedMesh.vertexCount; skin = smr; }
        }
        var severed = zombie.GetComponent<ElderboomSevered>() ?? zombie.gameObject.AddComponent<ElderboomSevered>();
        if (skin != null)
        {
            var before = new Mesh(); var after = new Mesh();
            skin.BakeMesh(before, true);
            Vector3 keep = bone.localScale; bone.localScale = Vector3.one * .0001f;
            skin.BakeMesh(after, true);
            bone.localScale = keep;
            var gib = BuildGib(skin, before, after, bone);
            Object.Destroy(before); Object.Destroy(after);
            if (gib != null) Throw(gib, dir, speed);
        }
        severed.Collapse(bone);
        ElderboomImportedVfx.Blood(bone.position, dir, true);
        severed.Spurt(bone, 1.6f);
        return true;
    }

    static bool Drives(SkinnedMeshRenderer smr, Transform bone)
    {
        foreach (var b in smr.bones) if (b != null && b.IsChildOf(bone)) return true;
        return false;
    }

    static GameObject BuildGib(SkinnedMeshRenderer skin, Mesh before, Mesh after, Transform bone)
    {
        var a = before.vertices; var b = after.vertices; if (a.Length != b.Length || a.Length == 0) return null;
        var moved = new bool[a.Length]; int count = 0;
        float scale = Mathf.Max(.0001f, skin.transform.lossyScale.x);
        for (int i = 0; i < a.Length; i++) if ((a[i] - b[i]).sqrMagnitude * scale * scale > .0004f) { moved[i] = true; count++; }
        if (count < 12) return null;
        var normals = before.normals; var uv = before.uv; var tangents = before.tangents;
        var remap = new int[a.Length]; for (int i = 0; i < remap.Length; i++) remap[i] = -1;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var tans = new List<Vector4>();
        var subs = new List<List<int>>(); var mats = new List<Material>();
        var shared = skin.sharedMaterials;
        for (int s = 0; s < before.subMeshCount; s++)
        {
            var tris = before.GetTriangles(s); var outTris = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
                if (!(moved[i0] && moved[i1] && moved[i2])) continue;
                foreach (int v in new[] { i0, i1, i2 })
                {
                    if (remap[v] < 0)
                    {
                        remap[v] = verts.Count; verts.Add(a[v]);
                        norms.Add(normals.Length > v ? normals[v] : Vector3.up);
                        uvs.Add(uv.Length > v ? uv[v] : Vector2.zero);
                        tans.Add(tangents.Length > v ? tangents[v] : new Vector4(1, 0, 0, 1));
                    }
                    outTris.Add(remap[v]);
                }
            }
            if (outTris.Count == 0) continue;
            subs.Add(outTris); mats.Add(shared.Length > 0 ? shared[Mathf.Min(s, shared.Length - 1)] : null);
        }
        if (verts.Count < 12) return null;
        // Pivot at the piece's centre so it tumbles naturally.
        Vector3 c = Vector3.zero; foreach (var v in verts) c += v; c /= verts.Count;
        for (int i = 0; i < verts.Count; i++) verts[i] -= c;
        var mesh = new Mesh { name = "Gib_" + bone.name };
        if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs); mesh.SetTangents(tans);
        mesh.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateBounds();
        var go = new GameObject("Gib_" + bone.name);
        // Baked with scale: the vertices are in the renderer's space without its scale.
        var tr = skin.transform;
        go.transform.SetPositionAndRotation(tr.TransformPoint(Vector3.Scale(c, new Vector3(1f / tr.lossyScale.x, 1f / tr.lossyScale.y, 1f / tr.lossyScale.z))), tr.rotation);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats.ToArray();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var col = go.AddComponent<BoxCollider>(); col.size = Vector3.Max(mesh.bounds.size, Vector3.one * .06f); col.center = mesh.bounds.center;
        go.layer = 19; // like the zombies' own: not hit by shots (ElderboomCombat.WorldMask) nor in the way of the player
        go.AddComponent<ElderboomGib>();
        return go;
    }

    static void Throw(GameObject gib, Vector3 dir, float speed)
    {
        var rb = gib.AddComponent<Rigidbody>(); rb.mass = 4f; rb.linearDamping = .2f; rb.angularDamping = .4f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rb.linearVelocity = dir * speed + Vector3.up * Random.Range(1.5f, 3.2f) + Random.insideUnitSphere * .8f;
        rb.angularVelocity = Random.insideUnitSphere * 9f;
        gibs.Enqueue(gib);
        while (gibs.Count > MaxGibs) { var old = gibs.Dequeue(); if (old != null) Object.Destroy(old); }
    }

    // ---- Ground blood -------------------------------------------------------------------
    const int MaxPools = 48;
    static readonly List<Transform> pools = new List<Transform>();
    static int nextPool;
    static Mesh poolMesh; static Material poolMaterial;
    public static void Pool(Vector3 near, float size)
    {
        if (ElderboomBloodSplat.Available) { ElderboomBloodSplat.PoolAt(near, size); return; } // Vefects HDRP pool
        RaycastHit ground = default; bool found = false; float best = 1e9f;
        foreach (var h in Physics.RaycastAll(near + Vector3.up * .5f, Vector3.down, 4f, ~((1 << 19) | (1 << 2)), QueryTriggerInteraction.Ignore))
            if (h.distance < best && h.collider.GetComponentInParent<ElderboomZombie>() == null && h.collider.GetComponentInParent<HDRPSlicePlayer>() == null && h.rigidbody == null)
            { best = h.distance; ground = h; found = true; }
        if (!found) return;
        if (Vector3.Dot(ground.normal, Vector3.up) < .6f) return;
        pools.RemoveAll(p => p == null);
        Transform pool;
        if (pools.Count < MaxPools) { pool = BuildPool(); pools.Add(pool); }
        else { pool = pools[nextPool % pools.Count]; nextPool++; }
        pool.SetPositionAndRotation(ground.point + ground.normal * (.012f + pools.IndexOf(pool) * .0004f),
            Quaternion.LookRotation(ground.normal) * Quaternion.Euler(0, 0, Random.Range(0f, 360f)));
        pool.localScale = Vector3.one * Random.Range(.9f, 1.6f) * size;
        pool.gameObject.SetActive(true);
        pool.GetComponent<ElderboomBloodPool>().Grow();
    }

    static Transform BuildPool()
    {
        if (poolMesh == null)
        {
            poolMesh = new Mesh { name = "BloodPool" };
            poolMesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
            poolMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            poolMesh.colors = new Color[4]; for (int i = 0; i < 4; i++) poolMesh.colors[i] = Color.white;
            var cols = new Color[4]; for (int i = 0; i < 4; i++) cols[i] = new Color(.24f, .006f, .01f, .92f); poolMesh.colors = cols;
            poolMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            poolMesh.RecalculateNormals(); poolMesh.RecalculateBounds();
        }
        if (poolMaterial == null)
        {
            poolMaterial = new Material(Resources.Load<Shader>("Elderboom/Shaders/BloodParticles")) { name = "BloodPoolShared", mainTexture = SplatTexture(), renderQueue = 2990 };
        }
        var go = new GameObject("BloodPool"); Object.DontDestroyOnLoad(go);
        go.AddComponent<MeshFilter>().sharedMesh = poolMesh;
        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = poolMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
        go.AddComponent<ElderboomBloodPool>();
        return go.transform;
    }

    // Irregular splat: a blobby core with droplets, alpha in the texture, white so the vertex colour tints it.
    static Texture2D SplatTexture()
    {
        const int S = 128; var tex = new Texture2D(S, S, TextureFormat.RGBA32, true, true) { name = "BloodSplat", wrapMode = TextureWrapMode.Clamp };
        var px = new Color[S * S]; var rng = new System.Random(5);
        var blobs = new List<(Vector2 c, float r)> { (new Vector2(.5f, .5f), .26f) };
        for (int i = 0; i < 7; i++) { float a = (float)rng.NextDouble() * 6.283f, d = .1f + (float)rng.NextDouble() * .14f; blobs.Add((new Vector2(.5f + Mathf.Cos(a) * d, .5f + Mathf.Sin(a) * d), .08f + (float)rng.NextDouble() * .09f)); }
        for (int i = 0; i < 14; i++) { float a = (float)rng.NextDouble() * 6.283f, d = .3f + (float)rng.NextDouble() * .16f; blobs.Add((new Vector2(.5f + Mathf.Cos(a) * d, .5f + Mathf.Sin(a) * d), .012f + (float)rng.NextDouble() * .025f)); }
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                var p = new Vector2((x + .5f) / S, (y + .5f) / S); float field = 0;
                foreach (var (c, r) in blobs) field += r * r / Mathf.Max(1e-5f, (p - c).sqrMagnitude);
                float n = Mathf.PerlinNoise(x * .09f, y * .09f) * .5f + .75f;
                float alpha = Mathf.SmoothStep(0, 1, (field * n - .9f) / .35f);
                float edge = Mathf.Clamp01(1f - Mathf.Abs(field * n - 1.1f) * 2f) * .25f; // darker rim
                px[y * S + x] = new Color(1f - edge, 1f - edge, 1f - edge, alpha);
            }
        tex.SetPixels(px); tex.Apply(true, true); return tex;
    }
}

// Keeps severed bones collapsed (the animation would otherwise restore them) and spurts blood from the stumps.
[DefaultExecutionOrder(500)]
public sealed class ElderboomSevered : MonoBehaviour
{
    readonly List<Transform> bones = new List<Transform>();
    public void Collapse(Transform bone) { if (!bones.Contains(bone)) bones.Add(bone); bone.localScale = Vector3.one * .0001f; }
    void LateUpdate() { foreach (var b in bones) if (b != null) b.localScale = Vector3.one * .0001f; }
    public void Spurt(Transform bone, float seconds) { StartCoroutine(SpurtRoutine(bone, seconds)); }
    IEnumerator SpurtRoutine(Transform bone, float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end && bone != null)
        {
            var dir = bone.parent != null ? (bone.position - bone.parent.position).normalized : Vector3.up;
            ElderboomImportedVfx.Blood(bone.position, (dir + Vector3.up * .4f).normalized, false);
            yield return new WaitForSeconds(Random.Range(.12f, .22f));
        }
    }
}

// Severed limb: settles, then sinks into the ground and goes.
public sealed class ElderboomGib : MonoBehaviour
{
    float bornAt;
    void Awake() { bornAt = Time.time; }
    void Update()
    {
        float age = Time.time - bornAt;
        if (age > 9f)
        {
            var rb = GetComponent<Rigidbody>(); if (rb != null) { Destroy(rb); foreach (var c in GetComponents<Collider>()) Destroy(c); }
            transform.position += Vector3.down * Time.deltaTime * .15f;
            if (age > 12f) Destroy(gameObject);
        }
    }
    void OnDestroy() { var mf = GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh); }
}

// Blood pool: spreads in over a second, stays, then fades out after a while.
public sealed class ElderboomBloodPool : MonoBehaviour
{
    float at; Vector3 full;
    public void Grow() { at = Time.time; full = transform.localScale; transform.localScale = full * .2f; enabled = true; }
    void Update()
    {
        float t = Time.time - at;
        if (t < 1.2f) transform.localScale = full * Mathf.Lerp(.2f, 1f, 1f - Mathf.Pow(1f - t / 1.2f, 3f));
        else if (t > 40f) { transform.localScale = full * Mathf.Max(0f, 1f - (t - 40f) / 4f); if (t > 44f) { gameObject.SetActive(false); enabled = false; } }
    }
}
