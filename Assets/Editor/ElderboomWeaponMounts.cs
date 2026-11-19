using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Logs/claude_mounts.txt ("probe" | "build") -> Logs/claude_mounts_result.txt
// Rewritten from scratch: mounts every weapon bore-horizontal onto its Basic FPS family skeleton with explicit
// Grip_R / Grip_L sockets placed on the weapon's REAL grips/handles (never detaching the mesh from its grips).
static class ElderboomWeaponMounts
{
    const string Flag = "Logs/claude_mounts.txt", Result = "Logs/claude_mounts_result.txt";
    public const string ClipRoot = "Assets/GameAssets/BasicFPSAnimations";
    public static readonly Dictionary<string, string> Family = new Dictionary<string, string>
    {
        {"m4","AR10"},{"mp7","XBR9"},{"glock19","Deagle"},{"rhino","Deagle"},{"shotgun","Winchester"},
        {"mp133","Winchester"},{"minigun","AR10"},{"flame","AR10"},{"grenade","AR10"},
    };
    static readonly Dictionary<string, float> HeavyScale = new Dictionary<string, float>
    {
        {"grenade", 1.65f}, {"minigun", 1.50f}, {"flame", 1.40f},
    };

    public static void BuildBatch()
    {
        var sb = new StringBuilder();
        Build(sb, preview: false);
        File.WriteAllText(Result, sb.ToString());
        Debug.Log("[ElderboomWeaponMounts] BuildBatch complete:\n" + sb);
    }

    static double next;
    [InitializeOnLoadMethod] static void Watch() { EditorApplication.update += Poll; }
    static void Poll()
    {
        if (EditorApplication.timeSinceStartup < next) return; next = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(Flag) || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        string mode = File.ReadAllText(Flag).Trim(); File.Delete(Flag);
        var sb = new StringBuilder();
        try { if (mode == "build") Build(sb); else if (mode == "spare") Spare(sb); else if (mode == "dump") Dump(sb); else Probe(sb); }
        catch (System.Exception e) { sb.AppendLine("EXCEPTION " + e); }
        File.WriteAllText(Result, sb.ToString());
    }

    // ---------- pack side
    public class Pack { public string family; public Transform root, weapon, mag, spare; public float scale; public GameObject go; }
    public static Pack LoadPack(string family, string role = "Idle")
    {
        string path = ClipRoot + "/" + family + "/" + family + "_" + role + ".fbx";
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (model == null || clip == null) return null;
        var go = Object.Instantiate(model); go.hideFlags = HideFlags.DontSave;
        go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity;
        clip.SampleAnimation(go, 0f);
        var p = new Pack { family = family, go = go, root = go.transform };
        p.weapon = Deep(go.transform, "Weapon"); p.mag = Deep(go.transform, "Magazine"); p.spare = Deep(go.transform, "MagazineSpare");
        var h = Deep(go.transform, "DEF-hand.L"); var f = Deep(go.transform, "DEF-forearm.L");
        p.scale = (h != null && f != null) ? 0.2668f / Vector3.Distance(h.position, f.position) : 1f;
        return p;
    }
    public static Vector3 Palm(Transform root, string side)
    {
        var h = Deep(root, "DEF-hand." + side); var m = Deep(root, "DEF-f_middle.01." + side);
        return (h.position + m.position * 2f) / 3f;
    }
    public static Vector3 Fist(Transform root, string side)
    {
        var pts = new List<Vector3> { Palm(root, side) };
        foreach (var n in new[] { "DEF-f_index.02.", "DEF-f_middle.02.", "DEF-f_ring.02.", "DEF-thumb.03." })
        { var t = Deep(root, n + side); if (t != null) pts.Add(t.position); }
        var c = Vector3.zero; foreach (var p in pts) c += p; return c / pts.Count;
    }
    public static Transform Deep(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }
    static string V(Vector3 v) => v.ToString("F3");

    // ---------- probe / utilities
    static void Probe(StringBuilder sb)
    {
        foreach (var fam in Family.Values.Distinct())
        {
            var p = LoadPack(fam);
            if (p == null) { sb.AppendLine(fam + ": clip missing"); continue; }
            var w = p.weapon;
            sb.AppendLine($"{fam}: scale {p.scale:F4} weapon pos {V(w.position)} lossy {V(w.lossyScale)} right {V(w.right)} up {V(w.up)} fwd {V(w.forward)}");
            Object.DestroyImmediate(p.go);
        }
    }
    static void Spare(StringBuilder sb) { sb.AppendLine("use probe"); }
    static void Dump(StringBuilder sb) { sb.AppendLine("use probe"); }

    // ---------- build
    public const string MountDir = "Assets/Resources/Elderboom/WeaponMounts";
    static Vector3 Axis(Transform t, Vector3 world, Vector3 exclude)
    {
        var c = new[] { t.right, -t.right, t.up, -t.up, t.forward, -t.forward };
        Vector3 best = Vector3.zero; float bd = -2;
        foreach (var a in c) { if (exclude != Vector3.zero && Mathf.Abs(Vector3.Dot(a, exclude)) > .5f) continue; float d = Vector3.Dot(a, world); if (d > bd) { bd = d; best = a; } }
        return best;
    }
    static Transform Named(Transform r, params string[] names)
    {
        foreach (var n in names) { var t = Deep(r, n); if (t != null) return t; }
        return null;
    }
    static bool IsMag(Renderer r, bool shotgun)
    {
        string n = r.name.ToLowerInvariant();
        if (shotgun) return n.Contains("shell") || n.Contains("slug");
        return (n.Contains("magazine") || n == "mag") && !n.Contains("release");
    }

    static Bounds MeasureBounds(Transform root)
    {
        var b = new Bounds(); bool any = false;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // Resolves the weapon's REAL right-hand grip and left-hand support points in world space (before mounting).
    // Every point sits physically on the weapon's real grip, stock neck, trigger, forend or handle.
    static void ResolveRealGrips(string id, ElderboomWeaponDefinition def, Transform inst, Transform muzzle, Vector3 oFwd, Vector3 oUp, out Vector3 gripR, out Vector3 gripL, out bool hasExplicitL)
    {
        var gRNode = Named(inst, "RightHand_Grip", "R_palm", "palm_2", "R_wrist");
        var gLNode = Named(inst, "LeftHand_Support", "L_palm", "palm", "L_wrist");
        hasExplicitL = gLNode != null;

        // Default from source markers
        gripR = gRNode != null ? gRNode.position : inst.position;
        gripL = gLNode != null ? gLNode.position : gripR;

        if (muzzle == null) return;

        // Local helper: convert muzzle-space (+Z forward, +Y up, +X right) point to current world space
        Vector3 M(float x, float y, float z) => muzzle.TransformPoint(new Vector3(x, y, z));

        switch (id)
        {
            case "m4":
                // Right hand on pistol grip; left hand forward on handguard/foregrip (~23 cm ahead of grip, well inside arm reach)
                gripR = gRNode != null ? gRNode.position : M(0f, -0.088f, -0.611f);
                gripL = M(0f, -0.040f, -0.385f);
                hasExplicitL = true;
                break;

            case "mp7":
                // Right hand slightly lower on pistol grip (no thumb clipping into rear receiver); left hand on front foregrip
                gripR = gRNode != null ? gRNode.position - oUp * 0.015f : M(0f, -0.093f, -0.256f);
                gripL = gLNode != null ? gLNode.position : M(0f, -0.078f, -0.119f);
                hasExplicitL = true;
                break;

            case "glock19":
                // Right hand on grip; left hand wraps snugly around right hand on the grip
                gripR = gRNode != null ? gRNode.position : M(0f, -0.072f, -0.144f);
                gripL = gripR + (-muzzle.right * 0.022f) + (-oUp * 0.008f) + (oFwd * 0.022f);
                hasExplicitL = true;
                break;

            case "rhino":
                // Rhino has no source grip markers: place grip high under trigger guard (matching glock19 sight-over-grip height)
                gripR = M(0f, -0.070f, -0.205f);
                gripL = gripR + (-muzzle.right * 0.022f) + (-oUp * 0.008f) + (oFwd * 0.022f);
                hasExplicitL = true;
                break;

            case "shotgun":
                // Benelli M4: place right hand BELOW the stock neck/pistol grip so the back of the hand never clips through the stock,
                // and left hand cleanly UNDER the forend.
                gripR = M(-0.002f, -0.118f, -0.715f);
                gripL = M(-0.002f, -0.078f, -0.390f);
                hasExplicitL = true;
                break;

            case "mp133":
                // MP-133 pump: right hand BELOW the wooden stock neck (zero hand-through-stock clipping),
                // left hand cleanly UNDER the wooden pump (G_Pump bottom is y=-0.053, z=-0.66..-0.26).
                gripR = M(0f, -0.096f, -0.720f);
                gripL = M(0f, -0.068f, -0.430f);
                hasExplicitL = true;
                break;

            case "grenade":
                // M79: right hand on the REAL trigger/stock neck (trigger is at z=-0.21, y=-0.04), NOT back on the buttcap (palm_2 was -0.321)!
                // Left hand under the wooden forend (front bounds y=-0.026, z=-0.198..+0.128).
                gripR = M(0f, -0.048f, -0.220f);
                gripL = M(0f, -0.036f, -0.060f);
                hasExplicitL = true;
                break;

            case "minigun":
                // Minigun: hands on the REAL wooden chainsaw handles!
                // palm_2 (rear top handle) = (0.012, +0.162, -1.113), palm (front-left handle) = (-0.190, +0.080, -0.831).
                gripR = gRNode != null ? gRNode.position : M(0.012f, 0.162f, -1.113f);
                gripL = gLNode != null ? gLNode.position : M(-0.190f, 0.080f, -0.831f);
                hasExplicitL = true;
                break;

            case "flame":
                // Flamethrower: right hand on the REAL rear grip/trigger, left hand on the REAL left side handle!
                // trigger bounds x=0.032..0.120 y=-0.093..+0.059 z=-1.081..-0.953; L_palm = (-0.118, +0.108, -0.777).
                gripR = M(0.075f, -0.030f, -1.035f);
                gripL = gLNode != null ? gLNode.position : M(-0.118f, 0.108f, -0.777f);
                hasExplicitL = true;
                break;
        }
    }

    static void Build(StringBuilder sb, bool preview = true)
    {
        Directory.CreateDirectory(MountDir);
        var table = new List<string>();
        foreach (var def in ElderboomWeapons.All)
        {
            if (!Family.TryGetValue(def.id, out var fam)) continue;
            var pack = LoadPack(fam);
            var prefab = Resources.Load<GameObject>(def.resource);
            if (pack == null || prefab == null) { sb.AppendLine(def.id + ": missing pack/prefab"); continue; }

            // Bore ALWAYS aligns cleanly to horizontal pack forward (pFwd) and upright (pUp) — NEVER pitched up by FromToRotation!
            Vector3 pFwd = Axis(pack.weapon, Vector3.forward, Vector3.zero);
            Vector3 pUp = Axis(pack.weapon, Vector3.up, pFwd);
            Vector3 pGrip = Fist(pack.root, "R");
            Vector3 pSup = Fist(pack.root, "L");
            var uaR = Deep(pack.root, "DEF-upper_arm.R");
            var uaL = Deep(pack.root, "DEF-upper_arm.L");
            var faL = Deep(pack.root, "DEF-forearm.L");
            var hL = Deep(pack.root, "DEF-hand.L");

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = prefab.name; inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var vm = inst.GetComponent<ElderboomViewmodel>();
            if (vm != null && vm.model != null && vm.idle != null) ElderboomAnimationSampling.Sample(vm.idle, vm.model, 0f);
            if (vm != null) Object.DestroyImmediate(vm);
            foreach (var a in inst.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            foreach (var a in inst.GetComponentsInChildren<Animation>(true)) Object.DestroyImmediate(a);
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true).Where(ElderboomWeaponOnly.IsArm).ToList()) Object.DestroyImmediate(r);

            bool isHeavy = HeavyScale.TryGetValue(def.id, out float hScale);
            if (isHeavy) inst.transform.localScale *= hScale;
            ElderboomWeaponOnly.EnsureSights(inst.transform, def);

            var rear = Deep(inst.transform, "MainWeapon_ADS_Rear");
            var front = Deep(inst.transform, "MainWeapon_ADS_Front");
            var upm = Deep(inst.transform, "MainWeapon_ADS_Up");
            var muzzle = ElderboomWeaponOnly.Muzzle(inst.transform);

            Vector3 oFwd, oUp;
            if (isHeavy && muzzle != null) { oFwd = muzzle.forward; oUp = muzzle.up; }
            else if (rear != null && front != null) { oFwd = (front.position - rear.position).normalized; oUp = upm != null ? upm.position - rear.position : (muzzle != null ? muzzle.up : Vector3.up); }
            else { oFwd = muzzle.forward; oUp = muzzle.up; }
            oUp = Vector3.ProjectOnPlane(oUp, oFwd).normalized;

            ResolveRealGrips(def.id, def, inst.transform, muzzle, oFwd, oUp, out Vector3 realGripR, out Vector3 realGripL, out _);

            // Slight natural yaw on shouldered long guns so the stock settles cleanly into the right shoulder while the bore points toward center
            float naturalYaw = (def.id == "shotgun" || def.id == "mp133" || def.id == "m4") ? -3.5f : 0f;
            Vector3 aimFwd = Quaternion.AngleAxis(naturalYaw, pUp) * pFwd;
            var q = Quaternion.LookRotation(aimFwd, pUp) * Quaternion.Inverse(Quaternion.LookRotation(oFwd, oUp));

            // Place the weapon so its REAL right grip sits cleanly at pGrip (shifted slightly back toward the right shoulder on shotguns so the buttstock seats on the shoulder)
            Vector3 anchorR = pGrip;
            if (def.id == "shotgun" || def.id == "mp133")
                anchorR = pGrip + new Vector3(0.015f, -0.012f, -0.035f);

            Vector3 gripRLocal = realGripR - inst.transform.position;
            Vector3 gripLLocal = realGripL - inst.transform.position;
            inst.transform.rotation = q * inst.transform.rotation;
            inst.transform.position = anchorR - q * gripRLocal;

            Vector3 worldGripR = inst.transform.position + q * gripRLocal;
            Vector3 worldGripL = inst.transform.position + q * gripLLocal;

            // Remove any old Grip_R / Grip_L before creating clean canonical sockets on the real grips
            foreach (var old in inst.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Grip_R" || t.name == "Grip_L").ToList())
                Object.DestroyImmediate(old.gameObject);

            // Verify left-hand socket is within anatomical arm reach from left shoulder (keep it on the weapon's gripR->gripL line if slightly far)
            float fistOffsetL = (hL != null) ? Vector3.Distance(hL.position, pSup) : 0.09f;
            float wristReachL = (uaL != null && faL != null && hL != null)
                ? (Vector3.Distance(uaL.position, faL.position) + Vector3.Distance(faL.position, hL.position)) * 0.94f
                : 0.56f;
            float maxFistReachL = wristReachL + fistOffsetL;
            if (uaL != null && Vector3.Distance(uaL.position, worldGripL) > maxFistReachL)
            {
                // Slide along gripL -> gripR toward shoulder until reachable (stays physically ON the forend/handle!)
                Vector3 dirBack = (worldGripR - worldGripL).normalized;
                for (int iter = 0; iter < 20 && Vector3.Distance(uaL.position, worldGripL) > maxFistReachL; iter++)
                    worldGripL += dirBack * 0.01f;
            }

            var gr = new GameObject("Grip_R").transform; gr.SetParent(inst.transform, false); gr.position = worldGripR;
            var gl = new GameObject("Grip_L").transform; gl.SetParent(inst.transform, false); gl.position = worldGripL;

            float supDistToPack = Vector3.Distance(worldGripL, pSup);
            float uaToL = uaL != null ? Vector3.Distance(uaL.position, worldGripL) : 0f;
            var wb = MeasureBounds(inst.transform);
            float stockDistToShoulder = uaR != null ? (wb.min.z - uaR.position.z) : 0f;

            // Build mount hierarchy: Weapon / {model, Magazine, MagazineSpare}
            var mount = new GameObject("Weapon");
            mount.transform.SetPositionAndRotation(pack.weapon.position, pack.weapon.rotation);
            mount.transform.localScale = pack.weapon.lossyScale;
            inst.transform.SetParent(mount.transform, true);

            var magN = new GameObject("Magazine").transform; magN.SetParent(mount.transform, false);
            magN.localPosition = pack.mag.localPosition; magN.localRotation = pack.mag.localRotation; magN.localScale = pack.mag.localScale;
            var spareN = new GameObject("MagazineSpare").transform; spareN.SetParent(mount.transform, false);
            spareN.localPosition = pack.spare.localPosition; spareN.localRotation = pack.spare.localRotation; spareN.localScale = pack.spare.localScale;

            bool shotgun = fam == "Winchester" || fam == "DoubleBarrel";
            var mags = inst.GetComponentsInChildren<Renderer>(true).Where(r => IsMag(r, shotgun)).ToList();
            string magInfo = "none";
            if (mags.Count > 0)
            {
                var m = mags[0];
                bool skinned = m is SkinnedMeshRenderer;
                if (!shotgun && !skinned)
                {
                    m.transform.SetParent(magN, true);
                    var spareCopy = Object.Instantiate(m.gameObject, spareN, false); spareCopy.name = m.name + "_Spare";
                    spareCopy.transform.localPosition = m.transform.localPosition; spareCopy.transform.localRotation = m.transform.localRotation; spareCopy.transform.localScale = m.transform.localScale;
                    magInfo = m.name + " moved under Magazine, copy under MagazineSpare";
                }
                else
                {
                    var c1 = Object.Instantiate(m.gameObject, magN, true); c1.name = m.name + "_Mag";
                    var c2 = Object.Instantiate(m.gameObject, spareN, false); c2.name = m.name + "_Spare";
                    c2.transform.localPosition = c1.transform.localPosition; c2.transform.localRotation = c1.transform.localRotation; c2.transform.localScale = c1.transform.localScale;
                    magInfo = m.name + (skinned ? " (skinned)" : "") + " copied under Magazine and MagazineSpare";
                }
            }

            foreach (var t in mount.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.None;
            string path = MountDir + "/" + def.id + ".prefab";
            PrefabUtility.SaveAsPrefabAsset(mount, path);
            sb.AppendLine($"{def.id} -> {fam}: len {wb.size.z:F3}m, R@pGrip, L->pack {supDistToPack:F3}m (uaL->L {uaToL:F3}/{maxFistReachL:F3}m), stock-shoulder dZ {stockDistToShoulder:F3}m, mag: {magInfo}");
            table.Add("{\"id\":\"" + def.id + "\",\"family\":\"" + fam + "\",\"mount\":\"Elderboom/WeaponMounts/" + def.id + "\"}");
            Object.DestroyImmediate(mount); Object.DestroyImmediate(pack.go);
        }
        File.WriteAllText("Assets/Resources/Elderboom/WeaponMounts.json", "{\n \"mounts\": [\n  " + string.Join(",\n  ", table) + "\n ]\n}\n");
        AssetDatabase.Refresh();
    }
}
