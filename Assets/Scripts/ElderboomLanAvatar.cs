using System.Collections.Generic;
using UnityEngine;

// Another player in a LAN game: full-body model (or a capsule when the operator has none), smoothed toward the
// positions received from the network, with idle / walk / run / downed clips chosen from its movement.
public sealed class ElderboomLanAvatar : MonoBehaviour
{
    public static readonly List<ElderboomLanAvatar> All = new List<ElderboomLanAvatar>();

    public int Id { get; private set; }
    public string Op { get; private set; }
    public bool IsDead { get; private set; }
    // Host: Eli's Ignore Signal used by this player; zombies leave them alone until then.
    float ignoredUntil;
    public bool Ignored => Time.time < ignoredUntil;
    public void IgnoreFor(float seconds) { ignoredUntil = Mathf.Max(ignoredUntil, Time.time + seconds); }

    Transform visual;
    ElderboomCharacterBody body;
    GameObject placeholder;
    Vector3 target, lastPos, velocity;
    float targetYaw, feet;
    int shots = -1;
    bool placed;
    string clip;
    static GUIStyle labelStyle;

    public static ElderboomLanAvatar Create(int id, string op)
    {
        var go = new GameObject("LanPlayer_" + (id + 1));
        var avatar = go.AddComponent<ElderboomLanAvatar>();
        avatar.Id = id;
        avatar.SetOperator(op);
        return avatar;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void SetOperator(string op)
    {
        if (op == Op && (body != null || placeholder != null)) return;
        Op = op;
        if (visual != null) Destroy(visual.gameObject);
        body = null; placeholder = null; clip = null;
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        visual.localPosition = new Vector3(0f, -feet, 0f);
        // Operators without a full-body model yet use Eli's.
        string model = ElderboomCharacterBody.Has(op) ? op : ElderboomCharacterBody.Has(ElderboomIds.Eli) ? ElderboomIds.Eli : null;
        if (model != null) body = ElderboomCharacterBody.Create(model, visual);
        if (body != null)
        {
            body.AttachProp("AKMN");
            foreach (var c in body.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        }
        else
        {
            placeholder = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var col = placeholder.GetComponent<Collider>(); if (col != null) Destroy(col);
            placeholder.transform.SetParent(visual, false);
            placeholder.transform.localPosition = new Vector3(0f, .9f, 0f);
            placeholder.transform.localScale = new Vector3(.6f, .9f, .6f);
        }
    }

    public int Seat { get; private set; } // 1 driving the horse, 2 passenger
    public void Apply(Vector3 position, float feetOffset, float yaw, bool dead, int shotCount, int seat = 0)
    {
        Seat = seat;
        if (!placed) { transform.position = lastPos = position; transform.rotation = Quaternion.Euler(0f, yaw, 0f); placed = true; }
        target = position; targetYaw = yaw; IsDead = dead;
        if (!Mathf.Approximately(feet, feetOffset)) { feet = feetOffset; if (visual != null) visual.localPosition = new Vector3(0f, -feet, 0f); }
        if (shots >= 0 && shotCount != shots && !dead) ElderboomAudio.FireWeapon("assault_rifle", position);
        shots = shotCount;
    }

    void Update()
    {
        if (!placed) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        float k = 1f - Mathf.Exp(-12f * dt);
        if ((transform.position - target).sqrMagnitude > 25f) transform.position = target;
        else transform.position = Vector3.Lerp(transform.position, target, k);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, targetYaw, 0f), k);
        var v = (transform.position - lastPos) / dt; v.y = 0f;
        lastPos = transform.position;
        velocity = Vector3.Lerp(velocity, v, 1f - Mathf.Exp(-8f * dt));
        Animate();
    }

    void Animate()
    {
        if (body == null) return;
        var local = Quaternion.Inverse(transform.rotation) * velocity;
        float speed = local.magnitude;
        string next; float rate = 1f;
        if (IsDead) next = "Downed_ReachHelp";
        else if (Seat == 1) next = "Ride";
        else if (Seat == 2) next = "Rifle_Idle";
        else if (speed < .35f) next = "Rifle_Idle";
        else if (local.z > Mathf.Abs(local.x) * .8f) { next = speed > 4.3f ? "Running" : "Rifle_WalkForward"; rate = Mathf.Clamp(speed / (speed > 4.3f ? 5.5f : 1.6f), .7f, 1.6f); }
        else if (local.z < -Mathf.Abs(local.x) * .8f) { next = "Walk_Backward_with_Gun"; rate = Mathf.Clamp(speed / 1.5f, .7f, 1.6f); }
        else { next = "Walk_Left_with_Gun"; rate = Mathf.Clamp(speed / 1.5f, .7f, 1.6f); }
        if (!body.HasClip(next)) next = IsDead ? "Idle_8" : speed < .35f ? "Idle_8" : "Walking";
        if (next != clip) { body.Play(next, .2f, rate, IsDead); clip = next; }
        else body.SetSpeed(rate);
    }

    void OnGUI()
    {
        var cam = Camera.main;
        if (cam == null || !placed) return;
        var head = transform.position + Vector3.up * (2.05f - feet);
        var p = cam.WorldToScreenPoint(head);
        if (p.z <= 0f || p.z > 70f) return;
        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
        }
        string text = "P" + (Id + 1) + "  " + (Op ?? "").ToUpperInvariant() + (IsDead ? "  ✖" : "");
        var rect = new Rect(p.x - 90f, Screen.height - p.y - 12f, 180f, 24f);
        labelStyle.normal.textColor = new Color(0, 0, 0, .8f);
        GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), text, labelStyle);
        labelStyle.normal.textColor = IsDead ? new Color(1f, .35f, .35f) : new Color(.85f, .95f, 1f);
        GUI.Label(rect, text, labelStyle);
    }
}
