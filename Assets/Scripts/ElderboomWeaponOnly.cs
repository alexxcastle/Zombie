using System.Collections.Generic;
using UnityEngine;

// First-person weapons WITHOUT arms (the old arms / rig / animation / grip system is archived in
// _Archivio_braccia_20261115, to be rebuilt from scratch).
// Every weapon is its own prefab (Resources/<definition.resource>):
//  - prefabs that carry stored framing (ElderboomViewmodel.framingStored) keep it: their arm meshes are hidden and the
//    arm animations stop (the model stays in its first idle pose); reloads are a simple procedural dip-and-roll;
//  - prefabs without a viewmodel (bare weapon models) are turned bore-forward, framed big in the lower right of the
//    screen and given an aim pose solved on their sights.
public static class ElderboomWeaponOnly
{
    const float HipViewportX = .64f, HipViewportY = .27f;

    public static Transform Build(ElderboomWeaponDefinition weapon, Transform parent)
    {
        if (weapon == null || parent == null || string.IsNullOrEmpty(weapon.resource)) return null;
        var prefab = Resources.Load<GameObject>(weapon.resource);
        if (prefab == null) { Debug.LogWarning("[Elderboom] Weapon prefab missing: " + weapon.resource); return null; }
        return BuildArmed(weapon, parent) ?? BuildBare(prefab, weapon, parent);
    }

    // ---- Tactical arms on the Basic FPS clips, holding the weapon's mount (Resources/Elderboom/WeaponMounts/<id>)
    [System.Serializable] class MountEntry { public string id, family, mount; }
    [System.Serializable] class MountTable { public MountEntry[] mounts; }
    static Dictionary<string, MountEntry> mounts;
    public static string FamilyOf(string id)
    {
        if (mounts == null)
        {
            mounts = new Dictionary<string, MountEntry>();
            var text = Resources.Load<TextAsset>("Elderboom/WeaponMounts");
            if (text != null) foreach (var m in JsonUtility.FromJson<MountTable>(text.text).mounts ?? new MountEntry[0]) mounts[m.id] = m;
        }
        return mounts.TryGetValue(id, out var e) ? e.family : null;
    }
    static Transform BuildArmed(ElderboomWeaponDefinition weapon, Transform parent)
    {
        string fam = FamilyOf(weapon.id);
        if (fam == null) return null;
        var armsPrefab = Resources.Load<GameObject>("Elderboom/TacticalArms/TacticalArms");
        var mountPrefab = Resources.Load<GameObject>("Elderboom/WeaponMounts/" + weapon.id);
        var controller = Resources.Load<RuntimeAnimatorController>("Elderboom/TacticalArms/" + fam);
        if (armsPrefab == null || mountPrefab == null || controller == null) return null;
        var root = new GameObject(weapon.id + "_Armed").transform;
        root.SetParent(parent, false);
        root.gameObject.SetActive(false);
        var arms = Object.Instantiate(armsPrefab, root, false);
        arms.name = "TacticalArms";
        var oldWeapon = arms.transform.Find("Weapon");
        var mount = Object.Instantiate(mountPrefab, arms.transform, false);
        if (oldWeapon != null) { mount.transform.localPosition = oldWeapon.localPosition; mount.transform.localRotation = oldWeapon.localRotation; mount.transform.localScale = oldWeapon.localScale; Object.DestroyImmediate(oldWeapon.gameObject); }
        mount.name = "Weapon";
        if (weapon.id == "mp7") DimMp7Glass(mount.transform);
        var animator = arms.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();
        var view = root.gameObject.AddComponent<ElderboomViewmodel>();
        var driver = root.gameObject.AddComponent<ElderboomArmsDriver>();
        // calibrated with the hierarchy active (clip sampling), then parked until equipped
        root.gameObject.SetActive(true);
        driver.Init(animator, fam, weapon.id);
        root.gameObject.SetActive(false);
        view.model = arms; view.arms = driver;
        // Generic bounds-derived hip/ADS framing (no per-weapon offsets; ArmsOffsets.json stays empty for user "+" tuning).
        FrameArmed(root, driver, out view.hipPosition, out view.hipRotation, out view.adsPosition, out view.adsRotation);
        // aim: the driver plays the ADS clips and locks the sight line on the view axis itself (or braced FOV zoom for heavies)
        bool melee = weapon.category == ElderboomIds.Category.Melee;
        view.hasAds = !melee && Find(root, "MainWeapon_ADS_Rear") != null && Find(root, "MainWeapon_ADS_Front") != null;
        view.adsRelief = weapon.category == ElderboomIds.Category.Handgun ? .26f : .12f;
        view.keepAdsRelief = true;
        view.framingStored = true;
        return root;
    }

    // Derives default hip/ADS framing purely from the mounted weapon's bounds in Idle (no weapon-ID overrides).
    static void FrameArmed(Transform root, ElderboomArmsDriver driver, out Vector3 hipPos, out Quaternion hipRot, out Vector3 adsPos, out Quaternion adsRot)
    {
        hipPos = Vector3.zero; hipRot = Quaternion.identity;
        var wn = driver != null ? driver.WeaponNode : null;
        if (wn != null)
        {
            bool wasActive = root.gameObject.activeSelf;
            root.gameObject.SetActive(true);
            bool any = false; Bounds b = default;
            foreach (var r in wn.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
                var rb = r.bounds;
                if (rb.size.sqrMagnitude < 1e-6f) continue;
                var c = root.InverseTransformPoint(rb.center);
                var e = root.InverseTransformVector(rb.extents);
                e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
                var lb = new Bounds(c, e * 2f);
                if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
            }
            if (!wasActive) root.gameObject.SetActive(false);
            if (any)
            {
                // Weapons held far from the eye (e.g. stretched handgun idle): pull closer to ~0.48 m.
                if (b.min.z > .52f) hipPos.z = Mathf.Clamp(.48f - b.min.z, -.22f, 0f);
                // Keep the whole weapon (including underslung barrels like minigun) cleanly inside the lower-right third,
                // moving arms + weapon TOGETHER so hands stay glued to real handles.
                if (b.min.y < -.32f) hipPos.y = Mathf.Clamp(-.28f - b.min.y, 0f, .17f);
                else if (b.center.y < -.17f) hipPos.y = Mathf.Clamp(-.14f - b.center.y, 0f, .12f);
                else if (b.center.y > -.10f) hipPos.y = Mathf.Clamp(-.12f - b.center.y, -.05f, 0f);
                if (b.center.x < .08f) hipPos.x = Mathf.Clamp(.12f - b.center.x, 0f, .12f);
            }
        }
        bool locks = driver == null || driver.LocksSights;
        if (locks) { adsPos = Vector3.zero; adsRot = Quaternion.identity; }
        else { adsPos = hipPos + new Vector3(-.04f, .015f, -.02f); adsRot = Quaternion.identity; }
    }

    // MP7's glTF EOTech glass ships with emissiveFactor=(1,1,1), which blooms heavily under HDRP night exposure.
    static void DimMp7Glass(Transform mount)
    {
        var dim = new Color(0.06f, 0.06f, 0.06f, 1f);
        foreach (var r in mount.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.name.Contains("Glass")) continue;
            if (Application.isPlaying)
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    if (mats[i].HasProperty("emissiveFactor")) mats[i].SetColor("emissiveFactor", dim);
                    if (mats[i].HasProperty("_EmissionColor")) mats[i].SetColor("_EmissionColor", dim);
                }
            }
            else
            {
                var mpb = new MaterialPropertyBlock();
                r.GetPropertyBlock(mpb);
                mpb.SetColor("emissiveFactor", dim);
                mpb.SetColor("_EmissionColor", dim);
                r.SetPropertyBlock(mpb);
            }
        }
    }

    // ---- prefab with its own framing: same place on screen as before, arms hidden
    static Transform BuildStored(GameObject prefab, ElderboomWeaponDefinition weapon, Transform parent)
    {
        var root = Object.Instantiate(prefab, parent, false).transform;
        root.name = prefab.name + "_WeaponOnly";
        root.gameObject.SetActive(false);
        var view = root.GetComponent<ElderboomViewmodel>();
        if (view.model != null && view.idle != null) ElderboomAnimationSampling.Sample(view.idle, view.model, 0f);
        view.idle = null; view.reload = null; view.ability = null; // no arm animation: the weapon stays put
        HideArms(root);
        if (weapon.category != ElderboomIds.Category.Melee) EnsureSights(root, weapon);
        if (view.model != null)
        {
            view.proceduralReload = true;
            view.reloadPivot = view.model.transform.localPosition;
            view.StoreModelBase();
        }
        root.localPosition = view.hipPosition; root.localRotation = view.hipRotation;
        return root;
    }

    // ---- bare weapon model: turned bore-forward and framed
    static Transform BuildBare(GameObject prefab, ElderboomWeaponDefinition weapon, Transform parent)
    {
        var root = new GameObject(weapon.id + "_WeaponOnly").transform;
        root.SetParent(parent, false);
        root.gameObject.SetActive(false);
        var model = Object.Instantiate(prefab, root, false).transform;
        model.name = prefab.name;
        model.localPosition = Vector3.zero; model.localRotation = Quaternion.identity;
        // prefabs built around the old arms: put the weapon in its first idle pose, then drop the arm viewmodel
        var old = model.GetComponent<ElderboomViewmodel>();
        if (old != null)
        {
            if (old.model != null && old.idle != null) ElderboomAnimationSampling.Sample(old.idle, old.model, 0f);
            old.idle = null; old.reload = null; old.ability = null; old.enabled = false;
            if (Application.isPlaying) Object.Destroy(old); else Object.DestroyImmediate(old);
        }
        foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (var a in model.GetComponentsInChildren<Animation>(true)) a.enabled = false;
        HideArms(model);
        bool melee = weapon.category == ElderboomIds.Category.Melee;
        if (!melee) EnsureSights(model, weapon);

        // bore forward, sights up
        var rear = Find(model, "MainWeapon_ADS_Rear"); var front = Find(model, "MainWeapon_ADS_Front"); var up = Find(model, "MainWeapon_ADS_Up");
        var muzzle = Muzzle(model);
        Vector3 fwd, upDir;
        if (rear != null && front != null && (front.position - rear.position).sqrMagnitude > 1e-8f)
        {
            fwd = (front.position - rear.position).normalized;
            upDir = up != null ? up.position - rear.position : muzzle != null ? muzzle.up : Vector3.up;
        }
        else if (muzzle != null) { fwd = muzzle.forward; upDir = muzzle.up; }
        else { fwd = Vector3.forward; upDir = Vector3.up; }
        upDir = Vector3.ProjectOnPlane(upDir, fwd); if (upDir.sqrMagnitude < 1e-8f) upDir = Vector3.up;
        var align = Quaternion.Inverse(Quaternion.LookRotation(model.InverseTransformDirection(fwd), model.InverseTransformDirection(upDir)));
        model.localRotation = align;
        model.localPosition = Vector3.zero;

        var view = root.gameObject.AddComponent<ElderboomViewmodel>();
        view.model = model.gameObject;
        view.proceduralReload = true;
        view.reloadPivot = Vector3.zero;
        view.StoreModelBase();

        // hip: the weapon's bounds (in parent space) big in the lower right, slightly turned towards the centre
        bool handgun = weapon.category == ElderboomIds.Category.Handgun;
        var turn = Quaternion.Euler(0f, -5f, -3f);
        root.localPosition = Vector3.zero; root.localRotation = turn;
        var b = LocalBounds(root, parent);
        float near = handgun ? .30f : .14f; // back end of the weapon, metres from the eye
        float shiftZ = near - b.min.z;
        float centreZ = b.center.z + shiftZ;
        float t = Mathf.Tan(ElderboomPauseMenu.DefaultHorizontalFov * .5f * Mathf.Deg2Rad), aspect = 16f / 9f;
        var want = new Vector3((HipViewportX - .5f) * 2f * t * centreZ, (HipViewportY - .5f) * 2f * t / aspect * centreZ, centreZ);
        view.hipPosition = want - b.center;
        view.hipRotation = turn;

        // aim: eye behind the rear sight
        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity;
        rear = Find(model, "MainWeapon_ADS_Rear"); front = Find(model, "MainWeapon_ADS_Front");
        float relief = handgun ? .19f : .12f;
        view.adsRelief = relief;
        view.hasAds = !melee && rear != null && front != null &&
                      ElderboomWeaponFraming.SolveSightPose(root, rear, front, new Vector3(0f, 0f, relief), out view.adsPosition, out view.adsRotation);
        if (!view.hasAds) { view.adsPosition = view.hipPosition; view.adsRotation = view.hipRotation; }
        view.framingStored = true;
        root.localPosition = view.hipPosition; root.localRotation = view.hipRotation;
        return root;
    }

    static Bounds LocalBounds(Transform root, Transform parent)
    {
        bool any = false; var b = new Bounds();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer) continue;
            var c = parent.InverseTransformPoint(r.bounds.center);
            var e = parent.InverseTransformVector(r.bounds.extents); e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
            var lb = new Bounds(c, e * 2f);
            if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
        }
        return b;
    }

    // ---- arms
    public static bool IsArm(Renderer r)
    {
        string n = r.name.ToLowerInvariant();
        if (n.StartsWith("p_")) return true; // mannequin boxes of the 1911 pack
        if (n.StartsWith("pistol") || n.Contains("guard") || n.Contains("handle")) return false;
        return n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove") || n.Contains("watch");
    }
    public static void HideArms(Transform root)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (IsArm(r)) r.enabled = false;
    }

    // ---- sights (unchanged from the archived grip system: weapon data, not arms)
    [System.Serializable] public class SightLine { public string id; public Vector3 rear, front, up; }
    [System.Serializable] class SightTable { public SightLine[] sights; }
    static Dictionary<string, SightLine> sightLines;
    public static SightLine Calibrated(string id)
    {
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
    public static Transform Muzzle(Transform holder)
    {
        foreach (var t in holder.GetComponentsInChildren<Transform>(true))
            if (t.name.ToLowerInvariant().Contains("muzzle")) return t;
        return null;
    }
    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }
    public static void EnsureSights(Transform holder, ElderboomWeaponDefinition weapon)
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
            var r = new GameObject("MainWeapon_ADS_Rear").transform; r.SetParent(muzzleMarker, false); r.localPosition = calibrated.rear;
            var f = new GameObject("MainWeapon_ADS_Front").transform; f.SetParent(muzzleMarker, false); f.localPosition = calibrated.front;
            if (calibrated.up != Vector3.zero) { var u = new GameObject("MainWeapon_ADS_Up").transform; u.SetParent(muzzleMarker, false); u.localPosition = calibrated.up; }
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
        Vector3 axis = muzzle.forward, upv = muzzle.up;
        float top = 0f, back = 0f;
        foreach (var rr in holder.GetComponentsInChildren<Renderer>(true))
        {
            if (!rr.enabled) continue;
            var bb = rr.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                top = Mathf.Max(top, Vector3.Dot(c - muzzle.position, upv));
                back = Mathf.Min(back, Vector3.Dot(c - muzzle.position, axis));
            }
        }
        float height = Mathf.Min(top * 0.8f, 0.06f);
        rear = new GameObject("MainWeapon_ADS_Rear").transform; rear.SetParent(holder, false);
        rear.position = muzzle.position + upv * height + axis * (back * 0.55f);
        front = new GameObject("MainWeapon_ADS_Front").transform; front.SetParent(holder, false);
        front.position = muzzle.position + upv * height - axis * 0.03f;
    }
}
