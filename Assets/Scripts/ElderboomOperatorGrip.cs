using System.Collections.Generic;
using UnityEngine;

// Puts any weapon in an operator's own right hand.
// The operator viewmodel (arms + animations + framing) is cloned, its own weapon is
// stowed, and only the geometry of the weapon prefab is transplanted onto the
// operator's right-hand bone. The weapon pack's generic arms are discarded.
//
// The weapon stays exactly where its pack frames it for the camera (angle, size,
// distance), as a child of the operator viewmodel root so sway/recoil/aim move it.
// The operator's own arms reach it through ElderboomArmIK: right hand to the pack's
// right wrist (the grip), left hand to the pack's left wrist (support or rest).
public static class ElderboomOperatorGrip
{
    // Renderers of the weapon packs' arms (from Logs/rig_dump.txt).
    static bool IsArm(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        return n.Contains("arm") || n.Contains("hand");
    }

    static readonly string[] WristNames = { "R_wrist", "DEF-hand.R", "hand.R", "RightHand" };
    static readonly string[] MuzzleNames = { "MainWeapon_Muzzle", "HK53_Muzzle" };

    public static Transform Build(Transform operatorTemplate, ElderboomOperatorDefinition op, ElderboomWeaponDefinition weapon, Transform parent)
    {
        if (operatorTemplate == null || op == null || weapon == null || string.IsNullOrEmpty(op.rightHandBone)) return null;
        var prefab = Resources.Load<GameObject>(weapon.resource);
        if (prefab == null) return null;

        // ---- Operator clone in its rest pose ----
        bool templateActive = operatorTemplate.gameObject.activeSelf;
        var clone = Object.Instantiate(operatorTemplate.gameObject, parent, false);
        clone.name = operatorTemplate.name + "_" + weapon.id;
        clone.SetActive(true);
        var view = clone.GetComponent<ElderboomViewmodel>();
        Frame(clone.transform, view);
        if (view != null && view.model != null && view.idle != null) ElderboomAnimationSampling.Sample(view.idle, view.model, RestTime(view));
        var hand = Find(clone.transform, op.rightHandBone);
        var ownMuzzle = Find(clone.transform, op.weaponMuzzle);
        if (hand == null || ownMuzzle == null)
        {
            Debug.LogWarning("[Elderboom] " + op.id + ": right hand '" + op.rightHandBone + "' or muzzle '" + op.weaponMuzzle + "' not found; using the weapon pack's arms for " + weapon.id);
            Object.Destroy(clone);
            return null;
        }
        Stow(clone.transform, op.stowedWeaponNodes);

        // ---- Weapon geometry from the pack, in its own rest pose ----
        var pack = Object.Instantiate(prefab, parent, false);
        pack.SetActive(true);
        var packView = pack.GetComponentInChildren<ElderboomViewmodel>(true);
        Frame(pack.transform, pack.GetComponent<ElderboomViewmodel>());
        if (packView != null && packView.model != null && packView.idle != null) ElderboomAnimationSampling.Sample(packView.idle, packView.model, 0f);
        var muzzle = FindAny(pack.transform, MuzzleNames) ?? FindContaining(pack.transform, "muzzle");
        var weaponRenderers = new List<Renderer>();
        foreach (var r in pack.GetComponentsInChildren<Renderer>(true)) if (!IsArm(r)) weaponRenderers.Add(r);
        if (muzzle == null || weaponRenderers.Count == 0)
        {
            Object.Destroy(pack); Object.Destroy(clone);
            return null;
        }
        var wrist = FindWrist(pack.transform);
        var leftWrist = FindWrist(pack.transform, false);

        var holder = new GameObject("OperatorWeapon_" + weapon.id).transform;
        holder.SetParent(clone.transform, false);
        var rightTarget = new GameObject("IK_Grip_R").transform;
        rightTarget.SetParent(holder, false);
        rightTarget.position = wrist != null ? wrist.position : EstimateGrip(weaponRenderers, muzzle);
        Transform leftTarget = null;
        if (leftWrist != null)
        {
            leftTarget = new GameObject("IK_Support_L").transform;
            leftTarget.SetParent(holder, false);
            leftTarget.position = leftWrist.position;
        }
        foreach (var r in weaponRenderers) Transplant(r, holder);
        // Markers outside any renderer (e.g. the wrench's muzzle) come along too.
        foreach (var t in pack.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if ((n.Contains("muzzle") || n.Contains("ads_") || t.GetComponent<Light>() != null) && !t.IsChildOf(holder)) t.SetParent(holder, true);
        }
        // Drop anything of the pack's arms that came along with a moved part.
        foreach (var r in holder.GetComponentsInChildren<Renderer>(true)) if (IsArm(r)) Object.Destroy(r.gameObject);
        Object.Destroy(pack);

        var armBones = new List<Transform>();
        foreach (var smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.bones != null) foreach (var b in smr.bones) if (b != null && !armBones.Contains(b)) armBones.Add(b);
        var ik = clone.AddComponent<ElderboomArmIK>();
        ik.Setup(armBones, op.rightHandBone, rightTarget, LeftName(op.rightHandBone), leftTarget);

        // Aim with the new weapon's sights when it has them; otherwise keep the
        // operator's calibrated aim pose.
        if (view != null && FindContaining(holder, "ads_rear") != null && FindContaining(holder, "ads_front") != null) view.hasAds = false;
        view?.ResetToIdle();
        clone.SetActive(false);
        if (!templateActive) operatorTemplate.gameObject.SetActive(false);
        return clone.transform;
    }

    // The hip pose the player applies when this root is equipped (HDRPSlicePlayer.BindWeaponRoot).
    static void Frame(Transform root, ElderboomViewmodel view)
    {
        var pose = ElderboomWeaponFraming.FromViewmodel(view, root);
        root.localPosition = pose.hipPosition;
        root.localRotation = pose.hipRotation;
    }

    // Same frozen frame ElderboomViewmodel shows at rest.
    static float RestTime(ElderboomViewmodel view) => view.operatorId == ElderboomIds.Tactical ? 1f / 60f : 0f;

    // Hide the operator's own weapon: renderers off, lights removed, markers renamed so
    // muzzle/sight lookups find the new weapon instead.
    static void Stow(Transform root, string[] nodes)
    {
        if (nodes == null) return;
        int index = 0;
        foreach (var name in nodes)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != name) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                foreach (var l in t.GetComponentsInChildren<Light>(true)) Object.Destroy(l);
                foreach (var m in t.GetComponentsInChildren<Transform>(true))
                {
                    string n = m.name.ToLowerInvariant();
                    if (n.Contains("muzzle") || n.Contains("ads") || n.Contains("hip_")) m.name = "Stowed_" + index++;
                }
            }
    }

    // Static meshes move as they are; skinned weapon parts are frozen in their rest pose.
    static void Transplant(Renderer r, Transform holder)
    {
        if (r is SkinnedMeshRenderer skinned)
        {
            if (skinned.sharedMesh == null) return;
            var baked = new Mesh { name = skinned.sharedMesh.name + "_Rest" };
            skinned.BakeMesh(baked, true);
            var go = new GameObject(skinned.name);
            go.transform.SetPositionAndRotation(skinned.transform.position, skinned.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = baked;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = skinned.sharedMaterials;
            mr.shadowCastingMode = skinned.shadowCastingMode;
            go.transform.SetParent(holder, true);
            foreach (Transform child in skinned.transform) child.SetParent(go.transform, true);
            return;
        }
        r.transform.SetParent(holder, true);
    }

    static readonly string[] LeftWristNames = { "L_wrist", "DEF-hand.L", "hand.L", "LeftHand" };

    static Transform FindWrist(Transform root, bool right = true)
    {
        var wrist = FindAny(root, right ? WristNames : LeftWristNames);
        if (wrist != null) return wrist;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == "Hand" && t.parent != null && HasAncestorContaining(t, right ? "right" : "left")) return t;
        return null;
    }

    // DEF-hand.R -> DEF-hand.L, DEF_hand.R -> DEF_hand.L, Hand.R -> Hand.L
    static string LeftName(string right) =>
        right.EndsWith(".R") ? right.Substring(0, right.Length - 2) + ".L" :
        right.EndsWith("_R") ? right.Substring(0, right.Length - 2) + "_L" : right.Replace("Right", "Left");

    // No wrist in the pack (e.g. the wrench): grip a fifth of the way from the back end.
    static Vector3 EstimateGrip(List<Renderer> renderers, Transform muzzle)
    {
        Vector3 axis = muzzle.forward;
        float min = float.MaxValue, max = float.MinValue;
        Vector3 center = Vector3.zero;
        foreach (var r in renderers)
        {
            var b = r.bounds;
            center += b.center;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float d = Vector3.Dot(corner - muzzle.position, axis);
                min = Mathf.Min(min, d); max = Mathf.Max(max, d);
            }
        }
        center /= renderers.Count;
        float along = Mathf.Lerp(min, max, .2f);
        Vector3 onAxis = Vector3.Dot(center - muzzle.position, axis) * axis + muzzle.position;
        return center + (muzzle.position + axis * along - onAxis);
    }

    static Transform Find(Transform root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    static Transform FindAny(Transform root, string[] names)
    {
        foreach (var name in names) { var t = Find(root, name); if (t != null) return t; }
        return null;
    }

    static Transform FindContaining(Transform root, string token)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Contains(token)) return t;
        return null;
    }

    static bool HasAncestorContaining(Transform t, string token)
    {
        for (var p = t.parent; p != null; p = p.parent) if (p.name.ToLowerInvariant().Contains(token)) return true;
        return false;
    }
}
