using System.Collections.Generic;
using UnityEngine;

// Weapons held by an operator's own arms, authored in Blender (assets/grips/*.blend,
// pipeline in Logs/grip/*.py).
//
// Resources/Elderboom/Grips/<Operator>_Grips.glb holds the operator's own arms and, per
// weapon id:
//   - a clip "<Operator>_<id>_Idle": the weapon pack's hold retargeted onto these arms,
//     fingers included;
//   - grip points "GripPt_<id>_W/I/M/P" on the right hand (and "_LW/LI/LM/LP" on the
//     left): where the pack's wrist and index/middle/pinky knuckles must land.
// At runtime the weapon geometry comes from the weapon's own prefab (its materials and
// markers stay as they are). It is moved rigidly so that the pack's hand frame matches
// the grip points, then parented to the operator's hand. Frames are built from joint
// positions only, so the different importers' bone axes never matter.
public static class ElderboomAuthoredGrip
{
    internal static readonly string[][] RightJoints =
    {
        new[] { "R_wrist", "DEF-hand.R", "DEF_hand.R", "Hand.R", "hand.R", "RightHand", "Right_Rig/Hand" },
        new[] { "R_point1", "DEF-f_index.01.R.01", "DEF-f_index.01.R", "DEF_index_01.R", "Bone.023.R", "point1.R", "RightHandIndex1", "Right_Rig/Index1" },
        new[] { "R_middle1", "DEF-f_middle.01.R.01", "DEF-f_middle.01.R", "DEF_middle_01.R", "Middle_Finger_1.R", "middle1.R", "RightHandMiddle1", "Right_Rig/Middle1" },
        new[] { "R_pink1", "DEF-f_pinky.01.R.01", "DEF-f_pinky.01.R", "DEF_little_01.R", "Pinky1.R", "pink1.R", "RightHandPinky1", "Right_Rig/Pinky1" },
    };
    internal static readonly string[][] LeftJoints =
    {
        new[] { "L_wrist", "DEF-hand.L", "DEF_hand.L", "Hand.L", "hand.L", "LeftHand", "Left_Rig/Hand" },
        new[] { "L_point1", "DEF-f_index.01.L.01", "DEF-f_index.01.L", "DEF_index_01.L", "Bone.023.L", "point1.L", "LeftHandIndex1", "Left_Rig/Index1" },
        new[] { "L_middle1", "DEF-f_middle.01.L.01", "DEF-f_middle.01.L", "DEF_middle_01.L", "Middle_Finger_1.L", "middle1.L", "LeftHandMiddle1", "Left_Rig/Middle1" },
        new[] { "L_pink1", "DEF-f_pinky.01.L.01", "DEF-f_pinky.01.L", "DEF_little_01.L", "Pinky1.L", "pink1.L", "LeftHandPinky1", "Left_Rig/Pinky1" },
    };
    static readonly string[] PointKeys = { "W", "I", "M", "P" };

    // Parts held by the left hand (dual sawn-off).
    internal static bool IsLeftPart(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent)
            if (p.name == "sawnoffL") return true;
        return false;
    }

    internal static bool IsArm(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        if (n.StartsWith("p_")) return true; // mannequin boxes of the 1911 pack
        if (n.StartsWith("pistol") || n.Contains("guard") || n.Contains("handle")) return false;
        return n.Contains("arm") || n.Contains("hand");
    }

    internal static string ResourcePath(string operatorId)
    {
        if (string.IsNullOrEmpty(operatorId)) return null;
        string id = operatorId.ToLowerInvariant();
        return "Elderboom/Grips/" + char.ToUpperInvariant(id[0]) + id.Substring(1) + "_Grips";
    }

    internal static string ClipName(ElderboomOperatorDefinition op, string gripPose)
    {
        string id = op.id.ToLowerInvariant();
        return char.ToUpperInvariant(id[0]) + id.Substring(1) + "_" + gripPose + "_Idle";
    }

    static AnimationClip Clip(string path, string name)
    {
        foreach (var clip in Resources.LoadAll<AnimationClip>(path))
            if (clip.name == name) return clip;
        return null;
    }

    public static Transform Build(Transform operatorTemplate, ElderboomOperatorDefinition op, ElderboomWeaponDefinition weapon, Transform parent)
    {
        if (operatorTemplate == null || op == null || weapon == null) return null;
        string path = ResourcePath(op.id);
        var gripsPrefab = path != null ? Resources.Load<GameObject>(path) : null;
        // Pack: the weapon prefab, or for another operator's primary weapon that operator's viewmodel.
        GameObject packPrefab = string.IsNullOrEmpty(weapon.resource) ? null : Resources.Load<GameObject>(weapon.resource);
        ElderboomOperatorDefinition source = null;
        if (packPrefab == null && weapon.IsOperatorPrimary)
        {
            source = ElderboomOperators.Find(weapon.operatorId);
            var pivot = source != null ? FindPivot(parent, source.id) : null;
            if (pivot != null) packPrefab = pivot.gameObject;
        }
        var donorPack = ElderboomDonorMotion.ForWeapon(weapon.id);
        ElderboomDonorMotion donor = null;
        // Soph holds every weapon in her own rifle pose (her own viewmodel and idle): her authored holds deform the
        // long sleeves of her flat rig. The weapon goes to her right hand; the left stays where her rifle hold puts it.
        bool ownPose = op.id == ElderboomIds.Soph && donorPack == null;
        string gripPose = string.IsNullOrEmpty(weapon.gripPose) ? weapon.id : weapon.gripPose;
        // With the unified arms rig Soph holds weapons with her own Grips clips like the others; her old own-rifle pose is
        // only the fallback when she has no clip for this grip (hk53 holds borrow her carbine hold).
        if (ownPose && gripsPrefab != null)
        {
            var own = Clip(path, ClipName(op, gripPose)) ?? (gripPose == "hk53" ? Clip(path, ClipName(op, "carbine")) : null);
            if (own != null) { ownPose = false; if (Clip(path, ClipName(op, gripPose)) == null) gripPose = "carbine"; }
        }
        var clip = !ownPose && donorPack == null && gripsPrefab != null ? Clip(path, ClipName(op, gripPose)) : null;
        if (packPrefab == null || (!ownPose && (gripsPrefab == null || (clip == null && donorPack == null)))) return null;

        // ---- operator clone with the grips model ----
        bool templateActive = operatorTemplate.gameObject.activeSelf;
        var clone = Object.Instantiate(operatorTemplate.gameObject, parent, false);
        clone.name = operatorTemplate.name + "_" + weapon.id;
        clone.SetActive(true);
        if (donorPack != null) clone.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        var view = clone.GetComponent<ElderboomViewmodel>();
        if (view == null || view.model == null) { Object.Destroy(clone); return null; }
        Transform model;
        Vector3? ownRear = null, ownFront = null; bool ownAim = false; float ownAimRelief = 0.16f;
        if (ownPose)
        {
            model = view.model.transform;
            clip = view.idle;
            ElderboomAnimationSampling.Sample(clip, model.gameObject, 0f);
            // Her own rifle off, and its markers renamed so aim and muzzle come from the new weapon. Its sight line is
            // kept: every weapon is aimed along it, which is where her rifle hold already aims cleanly.
            foreach (var name in op.stowedWeaponNodes ?? new string[0])
            {
                var node = Find(clone.transform, name);
                if (node == null) continue;
                foreach (var t in node.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (n.Contains("ads") && n.Contains("rear") && ownRear == null) ownRear = t.position;
                    if (n.Contains("ads") && n.Contains("front") && ownFront == null) ownFront = t.position;
                    if (n.Contains("muzzle") || n.Contains("ads")) t.name = "StowedMarker"; // no "ads"/"rear"/"muzzle" left to find
                }
                node.gameObject.SetActive(false);
            }
        }
        else
        {
        var old = view.model.transform;
        model = Object.Instantiate(gripsPrefab, old.parent, false).transform;
        model.name = old.name + "_Grips";
        ElderboomOperatorSkin.Apply(model); // realistic skin materials on the Grips arms too
        model.localPosition = old.localPosition;
        model.localRotation = old.localRotation;
        model.localScale = old.localScale;
        SetLayer(model, old.gameObject.layer);
        foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        old.gameObject.SetActive(false);
        old.SetParent(null, false);
        Object.Destroy(old.gameObject);
        if (donorPack != null)
        {
            donor = ElderboomDonorMotion.Create(clone.transform, model, donorPack, op.id, weapon.category == ElderboomIds.Category.Handgun);
            if (donor == null)
            {
                Debug.LogWarning("[Elderboom] Missing donor rig joints for " + op.id + "/" + weapon.id);
                clone.SetActive(false);
                Object.Destroy(clone);
                return null;
            }
            donor.Apply(donor.Idle, 0f);
        }
        else if (op.id == ElderboomIds.Eli)
            ElderboomLimbLock.Attach(model, "L", Clip(path, weapon.category == ElderboomIds.Category.Handgun ? "HandGrip_Pistol" : "HandGrip_Rifle"), null);
        if (donor == null) ElderboomAnimationSampling.Sample(clip, model.gameObject, 0f);
        }

        var gripR = (ownPose || donor != null) ? PackFrame(model, RightJoints) : GripFrame(model, gripPose, "");
        var gripL = (ownPose || donor != null) ? PackFrame(model, LeftJoints) : GripFrame(model, gripPose, "L");
        var handR = Find(model, "DEF-hand.R") ?? Find(model, op.rightHandBone);
        var handL = Find(model, "DEF-hand.L") ?? Find(model, LeftName(op.rightHandBone));
        if (gripR == null || handR == null)
        {
            Debug.LogWarning("[Elderboom] " + op.id + ": grip points for " + weapon.id + " missing");
            Object.Destroy(clone);
            return null;
        }

        // ---- weapon geometry from the pack, at its idle start ----
        var pack = Object.Instantiate(packPrefab, parent, false);
        pack.SetActive(true);
        var packView = pack.GetComponentInChildren<ElderboomViewmodel>(true);
        float packTime = source != null && source.id == ElderboomIds.Tactical ? 1f / 60f : 0f;
        if (packView != null && packView.model != null && packView.idle != null) ElderboomAnimationSampling.Sample(packView.idle, packView.model, packTime);
        // Only the weapon nodes of an operator viewmodel (not its arms or ability props).
        var include = source != null ? WeaponNodes(pack.transform, source) : null;
        var packR = PackFrame(pack.transform, RightJoints);
        var packL = PackFrame(pack.transform, LeftJoints);
        if (packR == null)
        {
            Debug.LogWarning("[Elderboom] " + weapon.id + ": pack hand joints not found");
            Object.Destroy(pack); Object.Destroy(clone);
            return null;
        }
        Matrix4x4 moveR = gripR.Value * packR.Value.inverse;
        Matrix4x4 moveL = gripL != null && packL != null ? gripL.Value * packL.Value.inverse : moveR;

        var holderR = new GameObject("OperatorWeapon_" + weapon.id).transform;
        holderR.SetParent(handR, false);
        Transform holderL = null;
        // Markers (muzzle, sights, lights) are points, the weapon parts are meshes: moved one by one into a holder under the
        // depth-scaled viewmodel parent, they drift apart (the M4's muzzle marker sat 15 mm right of its bore, so optics
        // and the sight line went right). Each marker is put back where it was relative to the biggest weapon part.
        Renderer refPart = null; float refSize = 0f;
        foreach (var r in pack.GetComponentsInChildren<Renderer>(true))
        {
            if (include != null ? !Under(r.transform, include) : IsArm(r)) continue;
            if (!(r is MeshRenderer)) continue; // skinned parts are re-created by Transplant
            float size = r.bounds.size.sqrMagnitude; if (size > refSize) { refSize = size; refPart = r; }
        }
        var markerRel = new Dictionary<Transform, (Vector3 pos, Quaternion rot)>();
        if (refPart != null)
            foreach (var t in pack.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (!(n.Contains("muzzle") || n.Contains("ads_") || t.GetComponent<Light>() != null)) continue;
                if (t.IsChildOf(refPart.transform)) continue;
                markerRel[t] = (refPart.transform.InverseTransformPoint(t.position), Quaternion.Inverse(refPart.transform.rotation) * t.rotation);
            }
        var moved = new List<Transform>();
        foreach (var r in pack.GetComponentsInChildren<Renderer>(true))
        {
            if (include != null ? !Under(r.transform, include) : IsArm(r)) continue;
            // A part under an already moved part (mag/bullet) has moved with it.
            bool carried = false;
            foreach (var m in moved) if (r.transform.IsChildOf(m)) { carried = true; break; }
            if (carried) continue;
            bool left = IsLeftPart(r.transform, pack.transform) && handL != null;
            if (left && holderL == null)
            {
                holderL = new GameObject("OperatorWeapon_" + weapon.id + "_L").transform;
                holderL.SetParent(handL, false);
            }
            var t = Transplant(r);
            if (t == null) continue;
            Move(t, left ? moveL : moveR);
            t.SetParent(left ? holderL : holderR, true);
            moved.Add(t);
        }
        // Markers (muzzle, sights, lights) that are not under a moved renderer.
        foreach (var t in pack.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!(n.Contains("muzzle") || n.Contains("ads_") || t.GetComponent<Light>() != null)) continue;
            if (include != null && !Under(t, include)) continue;
            bool under = false;
            foreach (var m in moved) if (t.IsChildOf(m)) { under = true; break; }
            if (under) continue;
            bool left = IsLeftPart(t, pack.transform) && holderL != null;
            Move(t, left ? moveL : moveR);
            t.SetParent(left ? holderL : holderR, true);
            if (!left && refPart != null && markerRel.TryGetValue(t, out var rel) && !IsLeftPart(refPart.transform, pack.transform))
                t.SetPositionAndRotation(refPart.transform.TransformPoint(rel.pos), refPart.transform.rotation * rel.rot);
        }
        // Support hand: where the pack's left hand held this weapon, as a marker on the weapon.
        // Flat rigs (Soph: every bone a direct child of the armature) have no arm chain to solve: the two-bone solve
        // would turn the whole armature. Their left hand keeps the authored hold.
        bool flatRig = handL != null && handR != null && handL.parent == handR.parent;
        Transform donorSupport = null;
        if (donor != null && packL != null && handL != null && holderL == null)
        {
            var pose = moveR * packL.Value;
            donorSupport = new GameObject("DonorSupportContact").transform;
            donorSupport.SetParent(holderR, false);
            donorSupport.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
            // The weapon's own handguard hold, where it has one (the donor's support hand fits a different handguard).
            if (ElderboomSupportGrip.PlaceMarker(donorSupport, holderR, weapon.id, handL, Find(model, "DEF-f_middle.01.L")))
                ElderboomSupportGrip.Attach(clone.transform, model, holderR, handL);
        }
        if (donor == null && !flatRig && packL != null && handL != null && holderL == null && weapon.category != ElderboomIds.Category.Melee)
        {
            var own = PackFrame(model, LeftJoints);
            if (own != null)
            {
                Matrix4x4 handOffset = own.Value.inverse * handL.localToWorldMatrix;
                Matrix4x4 want = moveR * packL.Value * handOffset;
                var marker = new GameObject("LeftSupport").transform;
                marker.SetParent(holderR, false);
                marker.SetPositionAndRotation(want.GetColumn(3), want.rotation);
                ElderboomSupportHand.Attach(model, handL, marker);
            }
        }
        SetLayer(holderR, model.gameObject.layer);
        if (holderL != null) SetLayer(holderL, model.gameObject.layer);
        pack.SetActive(false);
        Object.Destroy(pack);

        // Aim with the new weapon's sights (the player solves the pose from these markers).
        if (weapon.category != ElderboomIds.Category.Melee) EnsureSights(holderR, weapon);
        // Holds built from donor motions are tuned in the hold editor (key +) like the others, under their own key:
        // corrections saved for the old holds do not fit the new ones. Attached before the sight line and the support
        // contact are read, so a saved weapon correction is part of what the donor motion aligns to.
        if (donor != null) ElderboomGripAdjust.Attach(clone, AdjustKey(op.id, weapon.id, true), holderR, model, handR, handL);
        if (donor != null)
        {
            var rear = Find(holderR, "MainWeapon_ADS_Rear");
            var front = Find(holderR, "MainWeapon_ADS_Front");
            var up = Find(holderR, "MainWeapon_ADS_Up");
            var muzzle = Muzzle(holderR);
            Vector3 axis = rear != null && front != null ? front.position - rear.position : muzzle != null ? muzzle.forward : Vector3.zero;
            Vector3 vertical = up != null && rear != null ? up.position - rear.position : muzzle != null ? muzzle.up : clone.transform.up;
            donor.Align(clone.transform.InverseTransformDirection(axis), clone.transform.InverseTransformDirection(vertical));
            donor.Apply(donor.Idle, 0f);
            if (donorSupport != null)
            {
                donor.BindSupport(donorSupport);
                donor.Apply(donor.Idle, 0f);
            }
            donor.BindParts(holderR);
            view.donor = donor;
            view.framingStored = false;
        }
        if (ownPose && ownRear.HasValue && ownFront.HasValue && weapon.category != ElderboomIds.Category.Melee)
        {
            Transform r1t = null, f1t = null;
            foreach (var t in holderR.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "MainWeapon_ADS_Rear") r1t = t;
                else if (t.name == "MainWeapon_ADS_Front") f1t = t;
            }
            if (r1t != null && f1t != null && (f1t.position - r1t.position).sqrMagnitude > 1e-6f)
            {
                Vector3 r0 = ownRear.Value, a0 = (ownFront.Value - r0).normalized, r1 = r1t.position;
                var q = Quaternion.FromToRotation((f1t.position - r1).normalized, a0);
                Vector3 target = r0 + a0 * Vector3.Dot(r1 - r0, a0);
                holderR.SetPositionAndRotation(target + q * (holderR.position - r1), q * holderR.rotation);
                // Aim with the rig where her rifle's aim puts it (known clean): the eye sits as far behind this
                // weapon's rear sight as it does behind her rifle's (0.12 m), plus how far apart the two rear sights are.
                // A weapon whose sights sit far behind her rifle's (Striker) slides forward in the hand instead:
                // pulling the whole rig forward would bring her shoulders past the eye.
                float behind = Vector3.Dot(r1t.position - r0, a0);
                if (behind < -0.03f) { holderR.position += a0 * (-0.03f - behind); behind = -0.03f; }
                ownAimRelief = Mathf.Max(0.09f, 0.12f + behind / Mathf.Max(1e-4f, parent.lossyScale.z));
                ownAim = true;
            }
        }
        view.adsRelief = weapon.category == ElderboomIds.Category.Handgun ? 0.28f : weapon.category == ElderboomIds.Category.Shotgun ? 0.16f : 0.18f;
        if (flatRig && !ownPose) view.adsRelief = Mathf.Max(view.adsRelief, FlatRigRelief);
        if (ownAim) { view.adsRelief = ownAimRelief; view.keepAdsRelief = true; }
        view.model = model.gameObject;
        view.proceduralReload = donor == null;
        view.reloadPivot = model.parent.InverseTransformPoint(handR.position);
        view.StoreModelBase();
        view.idle = clip;
        view.reload = null;
        view.ability = null;
        // The operator's aim pose is calibrated on its own weapon's sights.
        view.hasAds = false;
        view.ResetToIdle();
        // Hand-tuned corrections for this operator and weapon (test range, key G).
        if (donor == null) ElderboomGripAdjust.Attach(clone, op.id + "/" + weapon.id, holderR, model, handR, handL);
        // Fingers closed on this weapon's surface (unified arms rig only).
        if (weapon.category != ElderboomIds.Category.Melee) ElderboomFingerContact.Attach(clone.transform, model, holderR, handR, handL);
        clone.SetActive(false);
        if (!templateActive) operatorTemplate.gameObject.SetActive(false);
        return clone.transform;
    }

    public const float FlatRigRelief = 0.27f;

    // Key of the hand-tuned corrections (GripOffsets.json) for an operator and weapon.
    public const string DonorSuffix = "#donor";
    public static string AdjustKey(string operatorId, string weaponId, bool donor) => operatorId + "/" + weaponId + (donor ? DonorSuffix : "");

    internal static Transform FindPivot(Transform parent, string operatorId)
    {
        string id = operatorId.ToLowerInvariant();
        string name = char.ToUpperInvariant(id[0]) + id.Substring(1) + "_Viewmodel_Pivot";
        for (var p = parent; p != null; p = p.parent)
        {
            var found = Find(p, name);
            if (found != null) return found;
        }
        return null;
    }

    // An operator's own weapon: its stowed nodes, without the spare magazine.
    internal static List<Transform> WeaponNodes(Transform root, ElderboomOperatorDefinition source)
    {
        var list = new List<Transform>();
        if (source.stowedWeaponNodes == null) return list;
        foreach (var name in source.stowedWeaponNodes)
        {
            if (name.Contains("Spare")) continue;
            var t = Find(root, name);
            if (t != null) list.Add(t);
        }
        return list;
    }

    internal static bool Under(Transform t, List<Transform> roots)
    {
        foreach (var r in roots) if (t == r || t.IsChildOf(r)) return true;
        return false;
    }

    // Sight lines measured on each weapon's geometry (Editor/ElderboomClaudeWeaponAudit "calibrate"), in the
    // muzzle marker's local space: Resources/Elderboom/GripSights.json. They replace the packs' markers, which were
    // placed for the packs' own framing (or guessed from bounds) and missed the transplanted weapon.
    [System.Serializable] public class SightLine { public string id; public Vector3 rear, front, up; }
    [System.Serializable] class SightTable { public SightLine[] sights; }
    static Dictionary<string, SightLine> sightLines;
    public static SightLine Calibrated(string id)
    {
#if UNITY_EDITOR
        sightLines = null; // re-read while calibrating (play mode may keep statics between sessions)
#endif
        if (sightLines == null)
        {
            sightLines = new Dictionary<string, SightLine>();
            var text = Resources.Load<TextAsset>("Elderboom/GripSights");
            if (text != null)
                foreach (var line in JsonUtility.FromJson<SightTable>(text.text).sights ?? new SightLine[0])
                    if (line != null && !string.IsNullOrEmpty(line.id)) sightLines[line.id] = line;
        }
        return sightLines.TryGetValue(id, out var found) ? found : null;
    }

    internal static Transform Muzzle(Transform holder)
    {
        foreach (var t in holder.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLowerInvariant().Contains("muzzle")) return t;
        return null;
    }

    internal static void EnsureSights(Transform holder, ElderboomWeaponDefinition weapon)
    {
        var calibrated = Calibrated(weapon.id);
        var muzzleMarker = calibrated != null ? Muzzle(holder) : null;
        if (calibrated != null && muzzleMarker != null)
        {
            foreach (var t in holder.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if (n.Contains("ads") && (n.Contains("rear") || n.Contains("front"))) t.name = "SightUnused_" + (n.Contains("rear") ? "R" : "F");
            }
            var r = new GameObject("MainWeapon_ADS_Rear").transform;
            r.SetParent(muzzleMarker, false); r.localPosition = calibrated.rear;
            var f = new GameObject("MainWeapon_ADS_Front").transform;
            f.SetParent(muzzleMarker, false); f.localPosition = calibrated.front;
            if (calibrated.up != Vector3.zero)
            {
                // Which way is up on the sights, so aiming keeps the weapon level (ElderboomWeaponFraming.SolveSightPose).
                var u = new GameObject("MainWeapon_ADS_Up").transform;
                u.SetParent(muzzleMarker, false); u.localPosition = calibrated.up;
            }
            return;
        }
        Transform rear = null, front = null, muzzle = null;
        foreach (var t in holder.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("ads") && n.Contains("rear")) rear = t;
            else if (n.Contains("ads") && n.Contains("front")) front = t;
            else if (muzzle == null && n.Contains("muzzle")) muzzle = t;
        }
        if ((rear != null && front != null) || muzzle == null) return;
        Vector3 axis = muzzle.forward, up = muzzle.up;
        float top = 0f, back = 0f;
        foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                top = Mathf.Max(top, Vector3.Dot(c - muzzle.position, up));
                back = Mathf.Min(back, Vector3.Dot(c - muzzle.position, axis));
            }
        }
        // Bounds are loose; keep the line just above the bore.
        float height = Mathf.Min(top * 0.8f, 0.06f);
        rear = new GameObject("MainWeapon_ADS_Rear").transform;
        rear.SetParent(holder, false);
        rear.position = muzzle.position + up * height + axis * (back * 0.55f);
        front = new GameObject("MainWeapon_ADS_Front").transform;
        front.SetParent(holder, false);
        front.position = muzzle.position + up * height - axis * 0.03f;
    }

    // Frame from wrist + index/middle/pinky knuckles. Only positions are used.
    internal static Matrix4x4 Frame(Vector3 w, Vector3 i, Vector3 m, Vector3 p)
    {
        Vector3 x = (i - p).normalized;
        Vector3 y = (m - w).normalized;
        Vector3 z = Vector3.Cross(x, y).normalized;
        y = Vector3.Cross(z, x);
        var f = Matrix4x4.identity;
        f.SetColumn(0, x); f.SetColumn(1, y); f.SetColumn(2, z);
        f.SetColumn(3, new Vector4(w.x, w.y, w.z, 1f));
        return f;
    }

    internal static Matrix4x4? GripFrame(Transform model, string id, string side)
    {
        var pts = new Vector3[4];
        for (int k = 0; k < 4; k++)
        {
            // Blender names them "GripPt_<id>_<key>.<Operator>".
            var t = FindPrefix(model, "GripPt_" + id + "_" + side + PointKeys[k]);
            if (t == null) return null;
            pts[k] = t.position;
        }
        return Frame(pts[0], pts[1], pts[2], pts[3]);
    }

    internal static Matrix4x4? PackFrame(Transform root, string[][] joints)
    {
        var pts = new Vector3[4];
        for (int k = 0; k < 4; k++)
        {
            Transform found = null;
            foreach (var name in joints[k]) { found = FindPath(root, name); if (found != null) break; }
            if (found == null) return null;
            pts[k] = found.position;
        }
        return Frame(pts[0], pts[1], pts[2], pts[3]);
    }

    internal static void Move(Transform t, Matrix4x4 m)
    {
        t.SetPositionAndRotation(m.MultiplyPoint3x4(t.position), m.rotation * t.rotation);
    }

    // Static meshes move as they are; skinned weapon parts are frozen in their pose.
    internal static Transform Transplant(Renderer r)
    {
        if (r is SkinnedMeshRenderer skinned)
        {
            if (skinned.sharedMesh == null) return null;
            var baked = new Mesh { name = skinned.sharedMesh.name + "_Held" };
            skinned.BakeMesh(baked, true);
            var go = new GameObject(skinned.name);
            go.transform.SetPositionAndRotation(skinned.transform.position, skinned.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = skinned.sharedMaterials;
            mr.shadowCastingMode = skinned.shadowCastingMode;
            foreach (Transform child in skinned.transform) child.SetParent(go.transform, true);
            return go.transform;
        }
        r.transform.SetParent(null, true);
        return r.transform;
    }

    // "Right_Rig/Hand" = a node named Hand under a node named Right_Rig.
    internal static Transform FindPath(Transform root, string path)
    {
        int slash = path.IndexOf('/');
        if (slash < 0) return Find(root, path);
        var scope = Find(root, path.Substring(0, slash));
        return scope != null ? Find(scope, path.Substring(slash + 1)) : null;
    }

    internal static Transform Find(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name)) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name == name.Replace('.', '_')) return t;
        return null;
    }

    internal static Transform FindPrefix(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name || t.name.StartsWith(name + ".") || t.name.StartsWith(name + "_")) return t;
        return null;
    }

    internal static string LeftName(string right) =>
        string.IsNullOrEmpty(right) ? right :
        right.EndsWith(".R") ? right.Substring(0, right.Length - 2) + ".L" :
        right.EndsWith("_R") ? right.Substring(0, right.Length - 2) + "_L" : right.Replace("Right", "Left");

    internal static void SetLayer(Transform root, int layer)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }
}
