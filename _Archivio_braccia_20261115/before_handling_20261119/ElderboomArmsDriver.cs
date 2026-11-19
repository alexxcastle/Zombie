using System.Collections.Generic;
using UnityEngine;

// Plays the Basic FPS clips on the Tactical arms (ElderboomTacticalArms): one Animator, one controller per weapon
// family (Resources/Elderboom/TacticalArms/<Family>.controller, states named after the clip role: Idle, Fire,
// Reload, ReloadEmpty, Draw, Walk, Sprint, ReloadStart/ReloadLoop/ReloadEnd ...). The weapon is the mount prefab
// sitting on the clips' "Weapon" node, so it follows every clip.
public sealed class ElderboomArmsDriver : MonoBehaviour
{
    public Animator animator;
    public string family, weaponId;
    bool aiming; float aimWeight;
    Vector3 basePos, appliedPos = new Vector3(float.NaN, 0, 0); Quaternion baseRot, appliedRot;
    Transform arms, weaponNode, handR;
    readonly List<(Transform t, Vector3 p, Quaternion r)> magBase = new List<(Transform, Vector3, Quaternion)>();
    [System.Serializable] class RoleTable { public string[] roles; public float[] lengths; }
    readonly Dictionary<string, float> lengths = new Dictionary<string, float>();
    readonly Queue<(string role, float speed)> queue = new Queue<(string, float)>();
    string current = "", locoWant = "Idle";
    bool acting;
    float actionEnd;

    public void Init(Animator a, string fam, string id)
    {
        animator = a; family = fam; weaponId = id;
        arms = a.transform; weaponNode = arms.Find("Weapon");
        foreach (var t in arms.GetComponentsInChildren<Transform>(true)) if (t.name == "DEF-hand.R") { handR = t; break; }
        if (weaponNode != null)
            foreach (var n in new[] { "Magazine", "MagazineSpare" })
            {
                var node = weaponNode.Find(n); if (node == null) continue;
                foreach (Transform c in node) magBase.Add((c, c.localPosition, c.localRotation));
            }
        var text = Resources.Load<TextAsset>("Elderboom/TacticalArms/" + fam + "_roles");
        if (text != null)
        {
            var t = JsonUtility.FromJson<RoleTable>(text.text);
            for (int i = 0; i < t.roles.Length; i++) lengths[t.roles[i]] = t.lengths[i];
        }
    }
    public void SetAim(bool active) => aiming = active;
    public Transform WeaponNode => weaponNode;
    public Transform Arms => arms;

    // hand-tuned corrections (ElderboomArmsOffsets), applied after the clips every frame
    void LateUpdate()
    {
        if (arms == null) return;
        aimWeight = Mathf.MoveTowards(aimWeight, aiming ? 1f : 0f, Time.deltaTime * 6f);
        var all = ElderboomArmsOffsets.Get("*"); var own = ElderboomArmsOffsets.Get(weaponId ?? "");
        Vector3 hipP = all.hipPos + own.hipPos; Quaternion hipR = Quaternion.Euler(own.hipEuler) * Quaternion.Euler(all.hipEuler);
        arms.localPosition = Vector3.Lerp(hipP, own.adsPos, aimWeight);
        arms.localRotation = Quaternion.Slerp(hipR, Quaternion.Euler(own.adsEuler), aimWeight);
        if (weaponNode != null)
        {
            // the clips write the Weapon node every frame; if a frame did not, start again from the clip's pose
            if (weaponNode.localPosition == appliedPos && weaponNode.localRotation == appliedRot) { weaponNode.localPosition = basePos; weaponNode.localRotation = baseRot; }
            basePos = weaponNode.localPosition; baseRot = weaponNode.localRotation;
        }
        if (weaponNode != null && (own.weaponPos != Vector3.zero || own.weaponEuler != Vector3.zero))
        {
            var q = Quaternion.Euler(own.weaponEuler);
            Vector3 pivot = handR != null ? arms.InverseTransformPoint(handR.position) : Vector3.zero;
            Vector3 p = arms.InverseTransformPoint(weaponNode.position);
            Quaternion r = Quaternion.Inverse(arms.rotation) * weaponNode.rotation;
            weaponNode.position = arms.TransformPoint(pivot + q * (p - pivot) + own.weaponPos);
            weaponNode.rotation = arms.rotation * (q * r);
        }
        if (weaponNode != null) { appliedPos = weaponNode.localPosition; appliedRot = weaponNode.localRotation; }
        if (magBase.Count > 0)
        {
            foreach (var (t, bp, br) in magBase)
            {
                if (t == null || t.parent == null) continue;
                // offsets in the arms' space, turned into the magazine node's own space
                Vector3 d = t.parent.InverseTransformVector(arms.TransformVector(own.magPos));
                Quaternion e = Quaternion.Inverse(t.parent.rotation) * arms.rotation * Quaternion.Euler(own.magEuler) * Quaternion.Inverse(arms.rotation) * t.parent.rotation;
                t.localPosition = bp + d; t.localRotation = e * br;
            }
        }
    }

    public bool Has(string role) => lengths.ContainsKey(role);
    float Len(string role) => lengths.TryGetValue(role, out var l) ? l : 1f;

    void Play(string role, float fade, float speed)
    {
        if (animator == null || !Has(role)) return;
        animator.speed = speed;
        if (fade <= 0f) animator.Play(role, 0, 0f); else animator.CrossFadeInFixedTime(role, fade, 0, 0f);
        current = role;
    }
    void Act(string role, float speed, float fade)
    {
        queue.Clear();
        Play(role, fade, speed);
        acting = true; actionEnd = Time.time + Len(role) / speed;
    }

    public void ResetIdle()
    {
        queue.Clear(); acting = false;
        if (animator != null && animator.isActiveAndEnabled) Play(Has(locoWant) ? locoWant : "Idle", 0f, 1f);
    }
    public void Draw() { if (Has("Draw")) Act("Draw", 1.25f, 0f); }
    public bool Fire()
    {
        if (!Has("Fire")) return false;
        Act("Fire", 1f, .02f);
        return true;
    }
    // Returns the gameplay duration of the reload.
    public float Reload(bool empty, int shells)
    {
        if (Has("ReloadLoop"))
        {   // tube: start, one loop per shell, end
            string start = empty && Has("ReloadStartEmpty") ? "ReloadStartEmpty" : "ReloadStart";
            int n = Mathf.Clamp(shells, 1, 8); const float sp = 1.3f;
            Act(start, sp, .08f);
            float total = Len(start) / sp;
            for (int i = 0; i < n; i++) { queue.Enqueue(("ReloadLoop", sp)); total += Len("ReloadLoop") / sp; }
            if (Has("ReloadEnd")) { queue.Enqueue(("ReloadEnd", sp)); total += Len("ReloadEnd") / sp; }
            actionEnd = Time.time + Len(start) / sp;
            return total;
        }
        string role = empty && Has("ReloadEmpty") ? "ReloadEmpty" : "Reload";
        if (!Has(role)) return 2.3f;
        float len = Len(role), speed = Mathf.Max(1f, len / 3.0f);
        Act(role, speed, .08f);
        return len / speed;
    }
    public void SetLocomotion(float amount, bool sprinting)
    {
        locoWant = sprinting && amount > .3f && Has("Sprint") ? "Sprint" : amount > .35f && Has("Walk") ? "Walk" : "Idle";
    }

    void Update()
    {
        if (animator == null) return;
        if (acting)
        {
            if (Time.time < actionEnd) return;
            if (queue.Count > 0)
            {
                var (role, speed) = queue.Dequeue();
                Play(role, .05f, speed); actionEnd = Time.time + Len(role) / speed;
                return;
            }
            acting = false;
            Play(locoWant, .2f, 1f);
            return;
        }
        if (current != locoWant) { Play(locoWant, .25f, 1f); return; }
        var st = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && st.normalizedTime >= .999f) Play(locoWant, 0f, 1f); // loop
    }
}
