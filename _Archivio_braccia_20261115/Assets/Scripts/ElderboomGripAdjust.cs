using System.Collections.Generic;
using UnityEngine;

// Hand-tuned corrections of an authored hold (operator + weapon), on top of what ElderboomAuthoredGrip computes:
// the weapon can slide/turn in the hands, and each hand can be moved/turned on the weapon (two-bone solve, works on
// hierarchical and flat rigs). Offsets are in the viewmodel root's space, so they hold at hip and while aiming.
// Saved in Resources/Elderboom/GripOffsets.json (edited in the test range: ElderboomTestRange, key G).
[DefaultExecutionOrder(120)]
public sealed class ElderboomGripAdjust : MonoBehaviour
{
    [System.Serializable]
    public sealed class Offset
    {
        public string key;
        public Vector3 weaponPos, weaponEuler, rightPos, rightEuler, leftPos, leftEuler, adsPos, adsEuler;
        // Whole arm, hand included, moved as one piece. The weapon and the other arm stay where they are.
        public Vector3 rightArmPos, rightArmEuler, leftArmPos, leftArmEuler;
        public Vector3 camPos, camEuler;   // first-person camera against arms and weapon (hip pose; aim has its own)
        public Vector3 magPos, magEuler;   // the separate magazine(s) on the weapon, in their parent's space (kept through every animation)
        // Uniform scales for the selected component. 0 means "untouched" so older saves stay at their real size (the Striker's own scale included).
        public float weaponScale, rightScale, leftScale, magScale;
        // Single bones of the arms (upper arm, forearm, hand, every finger joint): extra local rotation, and an optional uniform scale.
        public List<BoneTweak> bones = new List<BoneTweak>();
        // Mounted optics and accessories, one entry each, only saved when that weapon actually has the part.
        public List<PartTweak> gear = new List<PartTweak>();
        public bool HasBones { get { if (bones != null) foreach (var b in bones) if (b.euler != Vector3.zero || Scaled(b.scale)) return true; return false; } }
        public bool HasGear { get { if (gear != null) foreach (var g in gear) if (g != null && !g.IsZero) return true; return false; } }
        public bool IsZero => weaponPos == Vector3.zero && weaponEuler == Vector3.zero && rightPos == Vector3.zero && rightEuler == Vector3.zero && leftPos == Vector3.zero && leftEuler == Vector3.zero && rightArmPos == Vector3.zero && rightArmEuler == Vector3.zero && leftArmPos == Vector3.zero && leftArmEuler == Vector3.zero && adsPos == Vector3.zero && adsEuler == Vector3.zero && camPos == Vector3.zero && camEuler == Vector3.zero && magPos == Vector3.zero && magEuler == Vector3.zero && !Scaled(weaponScale) && !Scaled(rightScale) && !Scaled(leftScale) && !Scaled(magScale) && !HasBones && !HasGear;
        public static bool Scaled(float s) => s > 0.001f && Mathf.Abs(s - 1f) > 0.0001f;
        public static float ScaleOf(float s) => Scaled(s) ? s : 1f;
        public Offset Copy() => (Offset)MemberwiseClone();
    }
    [System.Serializable]
    public sealed class BoneTweak { public string name; public Vector3 euler; public float scale; }
    [System.Serializable]
    public sealed class PartTweak
    {
        public string name;
        public Vector3 pos, euler;
        public float scale;
        public bool IsZero => pos == Vector3.zero && euler == Vector3.zero && !Offset.Scaled(scale);
    }
    [System.Serializable] sealed class Table { public List<Offset> entries = new List<Offset>(); }

    const string Resource = "Elderboom/GripOffsets";
    static Table table;

    public static Offset Load(string key)
    {
        if (table == null)
        {
            var text = Resources.Load<TextAsset>(Resource);
            table = text != null ? JsonUtility.FromJson<Table>(text.text) ?? new Table() : new Table();
#if !UNITY_EDITOR
            // Holds saved in a build (key G in the test range) override the shipped ones.
            try
            {
                if (System.IO.File.Exists(UserPath))
                {
                    var user = JsonUtility.FromJson<Table>(System.IO.File.ReadAllText(UserPath));
                    if (user != null) foreach (var e in user.entries) { table.entries.RemoveAll(x => x.key == e.key); table.entries.Add(e); }
                }
            }
            catch (System.Exception ex) { Debug.LogWarning("[GripAdjust] " + ex.Message); }
#endif
        }
        // The shared entry: live edits show at once wherever it is used (hold and aim).
        foreach (var e in table.entries) if (e.key == key) return e;
        var created = new Offset { key = key };
        table.entries.Add(created);
        return created;
    }

    public static void Store(Offset offset)
    {
        if (table == null) Load(offset.key);
        if (!table.entries.Contains(offset)) { table.entries.RemoveAll(e => e.key == offset.key); table.entries.Add(offset); }
        table.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        var saved = new Table();
        foreach (var e in table.entries) if (!e.IsZero) saved.entries.Add(e);
        string json = JsonUtility.ToJson(saved, true);
#if UNITY_EDITOR
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "Resources/" + Resource + ".json"), json);
#else
        // A build saves next to the player's data, and into the project when it runs from <project>/Builds,
        // so the next build ships the same holds.
        try { System.IO.File.WriteAllText(UserPath, json); } catch (System.Exception ex) { Debug.LogWarning("[GripAdjust] " + ex.Message); }
        try
        {
            var project = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../Assets/Resources/" + Resource + ".json"));
            if (System.IO.File.Exists(project)) System.IO.File.WriteAllText(project, json);
        }
        catch (System.Exception ex) { Debug.LogWarning("[GripAdjust] " + ex.Message); }
        Debug.Log("[GripAdjust] saved " + offset.key + " (" + saved.entries.Count + " holds)");
#endif
    }

    static string UserPath => System.IO.Path.Combine(Application.persistentDataPath, "GripOffsets.json");

    // ---- single bones ----
    readonly List<Transform> rigBones = new List<Transform>();
    readonly Dictionary<string, Transform> boneByName = new Dictionary<string, Transform>();
    public IReadOnlyList<Transform> RigBones => rigBones;

    public BoneTweak Tweak(string bone, bool create)
    {
        if (offset.bones == null) offset.bones = new List<BoneTweak>();
        foreach (var b in offset.bones) if (b.name == bone) return b;
        if (!create) return null;
        var t = new BoneTweak { name = bone };
        offset.bones.Add(t);
        return t;
    }

    void CollectRig(List<Transform> bones)
    {
        rigBones.Clear(); boneByName.Clear();
        foreach (var b in bones) if (!boneByName.ContainsKey(b.name)) boneByName[b.name] = b;
        // Right arm then left arm, each from the shoulder down to the finger tips.
        foreach (var arm in new[] { right, left })
        {
            if (arm == null) continue;
            var start = arm.shoulder;
            var list = new List<Transform>();
            foreach (var b in bones)
                if ((arm.upper.Contains(b) || arm.fore.Contains(b) || arm.hand.Contains(b)) && !list.Contains(b)) list.Add(b);
            list.Sort((x, y) =>
            {
                int dx = Depth(x), dy = Depth(y);
                int gx = arm.upper.Contains(x) ? 0 : arm.fore.Contains(x) ? 1 : 2, gy = arm.upper.Contains(y) ? 0 : arm.fore.Contains(y) ? 1 : 2;
                if (gx != gy) return gx.CompareTo(gy);
                int c = string.CompareOrdinal(Finger(x.name), Finger(y.name));
                return c != 0 ? c : dx != dy ? dx.CompareTo(dy) : string.CompareOrdinal(x.name, y.name);
            });
            rigBones.AddRange(list);
        }
    }

    static int Depth(Transform t) { int d = 0; for (var p = t.parent; p != null; p = p.parent) d++; return d; }
    static string Finger(string n)
    {
        n = n.ToLowerInvariant();
        foreach (var f in new[] { "thumb", "index", "point", "middle", "ring", "pink", "little" }) if (n.Contains(f)) return f;
        return "";
    }

    void ApplyBones()
    {
        if (offset.bones == null) return;
        foreach (var b in offset.bones)
        {
            if (!boneByName.TryGetValue(b.name, out var t) || t == null) continue;
            var cur = t.localRotation;
            // Nothing re-posed the bone since last frame (no animation on it): start from the pose without our turn.
            if (applied.TryGetValue(t, out var last) && Quaternion.Angle(cur, last.after) < 0.01f) cur = last.after * Quaternion.Inverse(last.turn);
            var turn = Quaternion.Euler(b.euler);
            var after = cur * turn;
            t.localRotation = after;
            applied[t] = (after, turn);
            if (Offset.Scaled(b.scale))
            {
                if (!boneScale.ContainsKey(t)) boneScale[t] = t.localScale;
                t.localScale = boneScale[t] * b.scale;
            }
            else if (boneScale.TryGetValue(t, out var back)) { t.localScale = back; boneScale.Remove(t); }
        }
    }
    readonly Dictionary<Transform, (Quaternion after, Quaternion turn)> applied = new Dictionary<Transform, (Quaternion after, Quaternion turn)>();
    readonly Dictionary<Transform, Vector3> boneScale = new Dictionary<Transform, Vector3>();

    sealed class Arm
    {
        public List<Transform> upper = new List<Transform>(), fore = new List<Transform>(), hand = new List<Transform>();
        public Transform shoulder, elbow, wrist;
        public Vector3 wristScale;
        public bool scaled;
        public Vector3[] upperPos, forePos, handPos;
        public Quaternion[] upperRot, foreRot, handRot;
    }

    public string key;
    public Offset offset;
    Transform holder;
    Vector3 holderLocalPos; Quaternion holderLocalRot;
    Arm right, left;
    Vector3 bakedPos, bakedEuler, holderBaseScale;
    bool holderScaled;

    public static ElderboomGripAdjust Attach(GameObject root, string key, Transform holder, Transform model, Transform handR, Transform handL)
    {
        var a = root.GetComponent<ElderboomGripAdjust>() ?? root.AddComponent<ElderboomGripAdjust>();
        a.key = key; a.offset = Load(key); a.holder = holder;
        if (holder != null)
        {
            // The saved weapon correction is baked into the hold now, so the aim solved at equip uses the moved sights.
            var o = a.offset; var rootRot = root.transform.rotation;
            if (o.weaponPos != Vector3.zero || o.weaponEuler != Vector3.zero)
            {
                Quaternion wq = rootRot * Quaternion.Euler(o.weaponEuler) * Quaternion.Inverse(rootRot);
                holder.SetPositionAndRotation(holder.position + root.transform.TransformVector(o.weaponPos), wq * holder.rotation);
            }
            a.bakedPos = o.weaponPos; a.bakedEuler = o.weaponEuler;
            a.holderLocalPos = holder.localPosition; a.holderLocalRot = holder.localRotation;
            a.holderBaseScale = holder.localScale; a.holderScaled = false;
        }
        var bones = new List<Transform>();
        foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.bones != null) foreach (var b in smr.bones) if (b != null && !bones.Contains(b)) bones.Add(b);
        // Separate magazines of the weapon (not the spare ones), outermost node of each.
        a.magazines.Clear();
        if (holder != null)
            foreach (var node in holder.GetComponentsInChildren<Transform>(true))
            {
                if (ElderboomDonorMotion.PartRole(node.name) != "magazine") continue;
                bool nested = false;
                foreach (var m in a.magazines) if (node.IsChildOf(m.node)) { nested = true; break; }
                if (!nested) a.magazines.Add(new Magazine { node = node, baseScale = node.localScale });
            }
        a.right = Build(bones, handR, true);
        a.left = Build(bones, handL, false);
        a.CollectRig(bones);
        return a;
    }

    static Arm Build(List<Transform> bones, Transform wrist, bool right)
    {
        if (wrist == null) return null;
        var arm = new Arm { wrist = wrist };
        foreach (var b in bones)
        {
            if (!IsSide(b.name, right)) continue;
            string n = b.name.ToLowerInvariant();
            if (n.Contains("upper")) arm.upper.Add(b);
            else if (n.Contains("fore")) arm.fore.Add(b);
            else if (b == wrist || IsUnder(b, wrist) || n.Contains("palm") || n.Contains("thumb") || n.Contains("index") || n.Contains("middle") || n.Contains("ring") || n.Contains("pink") || n.Contains("f_") || n.Contains("finger") || n.Contains("point")) arm.hand.Add(b);
        }
        if (arm.upper.Count == 0 || arm.fore.Count == 0) return null;
        if (!arm.hand.Contains(wrist)) arm.hand.Insert(0, wrist);
        arm.wristScale = wrist.localScale;
        arm.shoulder = Farthest(arm.upper, wrist.position);
        arm.elbow = Farthest(arm.fore, wrist.position);
        arm.upperPos = new Vector3[arm.upper.Count]; arm.upperRot = new Quaternion[arm.upper.Count];
        arm.forePos = new Vector3[arm.fore.Count]; arm.foreRot = new Quaternion[arm.fore.Count];
        arm.handPos = new Vector3[arm.hand.Count]; arm.handRot = new Quaternion[arm.hand.Count];
        return arm;
    }

    static bool IsUnder(Transform t, Transform parent) { for (var p = t.parent; p != null; p = p.parent) if (p == parent) return true; return false; }

    static bool IsSide(string name, bool right)
    {
        string n = name.ToLowerInvariant();
        return right
            ? n.EndsWith(".r") || n.Contains(".r.") || n.EndsWith("_r") || n.StartsWith("r_") || n.Contains("right")
            : n.EndsWith(".l") || n.Contains(".l.") || n.EndsWith("_l") || n.StartsWith("l_") || n.Contains("left");
    }

    static Transform Farthest(List<Transform> set, Vector3 from)
    {
        Transform best = set[0]; float d = -1f;
        foreach (var t in set) { float e = (t.position - from).sqrMagnitude; if (e > d) { d = e; best = t; } }
        return best;
    }

    // ---- magazines ----
    sealed class Magazine { public Transform node; public bool set, scaled; public Vector3 afterPos, add, baseScale; public Quaternion afterRot, turn; }
    readonly List<Magazine> magazines = new List<Magazine>();
    public int MagazineCount => magazines.Count;
    public Transform MagazineParent => magazines.Count > 0 && magazines[0].node != null ? magazines[0].node.parent : null;

    // The correction is added on top of wherever the magazine is this frame (rest, or moved by a reload motion).
    void ApplyMagazines()
    {
        bool any = offset.magPos != Vector3.zero || offset.magEuler != Vector3.zero;
        foreach (var m in magazines)
        {
            if (m.node == null) continue;
            if (any || m.set)
            {
                Vector3 pos = m.node.localPosition; Quaternion rot = m.node.localRotation;
                // Nothing re-posed it since last frame: start from the pose without our correction.
                if (m.set && (pos - m.afterPos).sqrMagnitude < 1e-10f && Quaternion.Angle(rot, m.afterRot) < 0.01f) { pos -= m.add; rot = Quaternion.Inverse(m.turn) * rot; }
                m.add = offset.magPos; m.turn = Quaternion.Euler(offset.magEuler);
                m.afterPos = pos + m.add; m.afterRot = m.turn * rot; m.set = true;
                m.node.localPosition = m.afterPos; m.node.localRotation = m.afterRot;
            }
            if (Offset.Scaled(offset.magScale)) { m.node.localScale = m.baseScale * offset.magScale; m.scaled = true; }
            else if (m.scaled) { m.node.localScale = m.baseScale; m.scaled = false; }
        }
    }

    void ApplyPartScales()
    {
        if (holder != null)
        {
            if (Offset.Scaled(offset.weaponScale)) { holder.localScale = holderBaseScale * offset.weaponScale; holderScaled = true; }
            else if (holderScaled) { holder.localScale = holderBaseScale; holderScaled = false; }
        }
        ApplyHandScale(right, offset.rightScale);
        ApplyHandScale(left, offset.leftScale);
    }

    void ApplyHandScale(Arm arm, float scale)
    {
        if (arm == null || arm.wrist == null) return;
        if (Offset.Scaled(scale)) { arm.wrist.localScale = arm.wristScale * scale; arm.scaled = true; }
        else if (arm.scaled) { arm.wrist.localScale = arm.wristScale; arm.scaled = false; }
    }

    // ---- mounted optics and accessories (only the ones this weapon is actually wearing) ----
    public struct GearPiece { public string name, label; public Transform node; }
    readonly Dictionary<string, GearLive> gearLive = new Dictionary<string, GearLive>();
    sealed class GearLive { public Transform node; public bool set, scaled; public Vector3 afterPos, add, baseScale; public Quaternion afterRot, turn; }

    public PartTweak GearTweak(string name, bool create)
    {
        if (offset.gear == null) offset.gear = new List<PartTweak>();
        foreach (var g in offset.gear) if (g != null && g.name == name) return g;
        if (!create) return null;
        var made = new PartTweak { name = name };
        offset.gear.Add(made);
        return made;
    }

    public void ListGear(List<GearPiece> into)
    {
        into.Clear();
        var found = new List<Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            if (t.name.StartsWith("Mod_") || BuiltinOf(t.name) != null) found.Add(t);
        }
        for (int i = found.Count - 1; i >= 0; i--)
        {
            bool nested = false;
            for (int j = 0; j < found.Count; j++) if (j != i && found[i].IsChildOf(found[j])) { nested = true; break; }
            if (nested) found.RemoveAt(i);
        }
        foreach (var t in found) into.Add(new GearPiece { name = t.name, label = GearLabel(t.name), node = t });
    }

    static string GearLabel(string nodeName)
    {
        if (nodeName.StartsWith("Mod_"))
        {
            var mod = ElderboomWeaponMods.FindMod(nodeName.Substring(4));
            return mod != null ? ElderboomWeaponMods.SlotName(mod.slot) + " · " + mod.name : nodeName.Substring(4);
        }
        var builtin = BuiltinOf(nodeName);
        return builtin != null ? ElderboomWeaponMods.SlotName(builtin.slot) + " · " + builtin.name : nodeName;
    }

    static ElderboomWeaponMods.Mod BuiltinOf(string nodeName)
    {
        foreach (var m in ElderboomWeaponMods.All)
        {
            if (!m.Builtin || m.builtinNodes == null) continue;
            foreach (var n in m.builtinNodes)
                if (!string.IsNullOrEmpty(n) && nodeName.IndexOf(n, System.StringComparison.OrdinalIgnoreCase) >= 0) return m;
        }
        return null;
    }

    void ApplyGear()
    {
        if (offset.gear == null) return;
        foreach (var g in offset.gear)
        {
            if (g == null || string.IsNullOrEmpty(g.name)) continue;
            if (!gearLive.TryGetValue(g.name, out var live) || live.node == null)
            {
                Transform node = null;
                foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == g.name) { node = t; break; }
                if (node == null) continue;
                live = new GearLive { node = node, baseScale = node.localScale };
                gearLive[g.name] = live;
            }
            if (g.IsZero)
            {
                if (live.set && (live.node.localPosition - live.afterPos).sqrMagnitude < 1e-10f && Quaternion.Angle(live.node.localRotation, live.afterRot) < 0.01f)
                {
                    live.node.localPosition -= live.add;
                    live.node.localRotation = Quaternion.Inverse(live.turn) * live.node.localRotation;
                }
                live.set = false;
                if (live.scaled) { live.node.localScale = live.baseScale; live.scaled = false; }
                continue;
            }
            if (g.pos != Vector3.zero || g.euler != Vector3.zero || live.set)
            {
                Vector3 pos = live.node.localPosition; Quaternion rot = live.node.localRotation;
                if (live.set && (pos - live.afterPos).sqrMagnitude < 1e-10f && Quaternion.Angle(rot, live.afterRot) < 0.01f)
                { pos -= live.add; rot = Quaternion.Inverse(live.turn) * rot; }
                live.add = g.pos; live.turn = Quaternion.Euler(g.euler);
                live.afterPos = pos + live.add; live.afterRot = live.turn * rot; live.set = true;
                live.node.localPosition = live.afterPos; live.node.localRotation = live.afterRot;
            }
            if (Offset.Scaled(g.scale)) { live.node.localScale = live.baseScale * g.scale; live.scaled = true; }
            else if (live.scaled) { live.node.localScale = live.baseScale; live.scaled = false; }
        }
    }

    public Transform ArmWrist(bool rightSide)
    {
        var arm = rightSide ? right : left;
        return arm != null ? arm.wrist : null;
    }

    void LateUpdate()
    {
        if (holder == null || offset == null) return;
        ApplyMagazines();
        holder.localPosition = holderLocalPos; holder.localRotation = holderLocalRot;
        bool plain = offset.weaponPos == bakedPos && offset.weaponEuler == bakedEuler && offset.rightPos == Vector3.zero && offset.rightEuler == Vector3.zero && offset.leftPos == Vector3.zero && offset.leftEuler == Vector3.zero;
        if (!plain)
        {
            var rootRot = transform.rotation;
            // Weapon pose in the hands (taken before the hands move: they carry the holder).
            // Live edits beyond what is baked in the hold.
            Quaternion wq = rootRot * (Quaternion.Euler(offset.weaponEuler) * Quaternion.Inverse(Quaternion.Euler(bakedEuler))) * Quaternion.Inverse(rootRot);
            Vector3 gunPos = holder.position + transform.TransformVector(offset.weaponPos - bakedPos);
            Quaternion gunRot = wq * holder.rotation;
            if (right != null && (offset.rightPos != Vector3.zero || offset.rightEuler != Vector3.zero)) Solve(right, offset.rightPos, offset.rightEuler);
            if (left != null && (offset.leftPos != Vector3.zero || offset.leftEuler != Vector3.zero)) Solve(left, offset.leftPos, offset.leftEuler);
            holder.SetPositionAndRotation(gunPos, gunRot);
        }
        if (offset.HasBones || applied.Count > 0) ApplyBones();
        ApplyPartScales();
        ApplyGear();
        // The hands carry the weapon, so remember its pose and put it back after the arm moves.
        Vector3 keepPos = holder.position; Quaternion keepRot = holder.rotation;
        ShiftArm(right, offset.rightArmPos, offset.rightArmEuler);
        ShiftArm(left, offset.leftArmPos, offset.leftArmEuler);
        holder.SetPositionAndRotation(keepPos, keepRot);
    }

    // Upper arm, forearm, hand and fingers move and turn together around the shoulder. No elbow solve, and the weapon stays put.
    void ShiftArm(Arm arm, Vector3 localShift, Vector3 localEuler)
    {
        if (arm == null || arm.shoulder == null) return;
        if (localShift == Vector3.zero && localEuler == Vector3.zero) return;
        Cache(arm.upper, arm.upperPos, arm.upperRot);
        Cache(arm.fore, arm.forePos, arm.foreRot);
        Cache(arm.hand, arm.handPos, arm.handRot);
        Vector3 pivot = arm.shoulder.position;
        Vector3 delta = transform.TransformVector(localShift);
        var rootRot = transform.rotation;
        Quaternion turn = rootRot * Quaternion.Euler(localEuler) * Quaternion.Inverse(rootRot);
        System.Func<Vector3, Vector3> place = p => pivot + turn * (p - pivot) + delta;
        Apply(arm.upper, arm.upperPos, arm.upperRot, place, turn);
        Apply(arm.fore, arm.forePos, arm.foreRot, place, turn);
        Apply(arm.hand, arm.handPos, arm.handRot, place, turn);
    }

    void Solve(Arm arm, Vector3 localShift, Vector3 localEuler)
    {
        Cache(arm.upper, arm.upperPos, arm.upperRot);
        Cache(arm.fore, arm.forePos, arm.foreRot);
        Cache(arm.hand, arm.handPos, arm.handRot);
        Vector3 a = arm.shoulder.position, b = arm.elbow.position, c = arm.wrist.position;
        Vector3 t = c + transform.TransformVector(localShift);
        var rootRot = transform.rotation;
        Quaternion handTurn = rootRot * Quaternion.Euler(localEuler) * Quaternion.Inverse(rootRot);
        float upperLength = (b - a).magnitude, foreLength = (c - b).magnitude;
        if (upperLength < 1e-4f || foreLength < 1e-4f) return;
        Vector3 toTarget = t - a;
        float distance = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(upperLength - foreLength) + 1e-3f, upperLength + foreLength - 1e-3f);
        Vector3 direction = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : (c - a).normalized;
        t = a + direction * distance;
        Vector3 bend = (b - a) - Vector3.Dot(b - a, direction) * direction;
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.Cross(direction, Vector3.up);
        bend.Normalize();
        float along = (upperLength * upperLength - foreLength * foreLength + distance * distance) / (2f * distance);
        float height = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 newElbow = a + direction * along + bend * height;
        var upperDelta = Quaternion.FromToRotation(b - a, newElbow - a);
        var foreDelta = Quaternion.FromToRotation(upperDelta * (c - b), t - newElbow) * upperDelta;
        Apply(arm.upper, arm.upperPos, arm.upperRot, p => a + upperDelta * (p - a), upperDelta);
        Apply(arm.fore, arm.forePos, arm.foreRot, p => newElbow + foreDelta * (p - b), foreDelta);
        Apply(arm.hand, arm.handPos, arm.handRot, p => t + handTurn * (p - c), handTurn);
    }

    static void Cache(List<Transform> bones, Vector3[] pos, Quaternion[] rot)
    {
        for (int i = 0; i < bones.Count; i++) { pos[i] = bones[i].position; rot[i] = bones[i].rotation; }
    }

    static void Apply(List<Transform> bones, Vector3[] pos, Quaternion[] rot, System.Func<Vector3, Vector3> place, Quaternion delta)
    {
        for (int i = 0; i < bones.Count; i++) bones[i].SetPositionAndRotation(place(pos[i]), delta * rot[i]);
    }
}
