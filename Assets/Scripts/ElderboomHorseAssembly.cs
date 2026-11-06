using System;
using UnityEngine;

// Scene-authored salvage locations. The host owns the shared inventory and assembly state.
public sealed class ElderboomHorseAssembly : MonoBehaviour
{
    [Serializable] public sealed class Part
    {
        public string label, area;
        public Transform anchor;
        public GameObject visual;
    }

    public const byte Assemble = 255;
    public Part[] parts = Array.Empty<Part>();
    public GameObject workshopVisual;
    public static ElderboomHorseAssembly Instance { get; private set; }
    public int CollectedMask { get; private set; }
    public bool Built { get; private set; }
    public uint Revision { get; private set; }
    public int RequiredMask => (1 << parts.Length) - 1;
    public int CollectedCount { get { int n = 0; for (int i = 0; i < parts.Length; i++) if ((CollectedMask & (1 << i)) != 0) n++; return n; } }
    public bool Ready => parts.Length > 0 && CollectedMask == RequiredMask;
    const float Reach = 2.8f;
    HDRPSlicePlayer player;
    int focused = -1;
    int changedFrame = -1;

    void Awake() { Instance = this; RefreshVisuals(); }
    void OnEnable() => ElderboomEvents.SessionStarted += ResetSession;
    void OnDisable() { ElderboomEvents.SessionStarted -= ResetSession; if (Instance == this) Instance = null; }

    public void ResetSession()
    {
        CollectedMask = 0; Built = false;
        Revision = ElderboomLan.IsClient ? 0 : Revision + 1;
        focused = -1;
        RefreshVisuals();
        var horse = ElderboomMount.Instance;
        if (horse != null) horse.ResetAtWorkshop(transform.position, transform.rotation);
    }

    void RefreshVisuals()
    {
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].visual != null) parts[i].visual.SetActive(!Built && (CollectedMask & (1 << i)) == 0);
        if (workshopVisual != null) workshopVisual.SetActive(!Built);
        if (ElderboomMount.Instance != null) ElderboomMount.Instance.SetAssembled(Built);
        changedFrame = Time.frameCount;
    }

    public void ApplySnapshot(int mask, bool built, uint revision)
    {
        if (revision < Revision || (built && mask != RequiredMask) || (mask & ~RequiredMask) != 0) return;
        if (Revision == revision && CollectedMask == mask && Built == built) return;
        bool completed = !Built && built;
        bool recovered = mask != CollectedMask;
        CollectedMask = mask; Built = built; Revision = revision;
        RefreshVisuals();
        if (completed) ElderboomGame.Active?.ShowNotice("MECHANICAL HORSE ASSEMBLED", 4f);
        else if (recovered && mask != 0) ElderboomGame.Active?.ShowNotice($"HORSE COMPONENTS {CollectedCount}/{parts.Length}" + (Ready ? " · RETURN TO THE WORKSHOP" : ""), 4f);
    }

    // Used by both local input and authenticated remote requests. Never trust a pickup index alone.
    public bool TryInteract(byte item, Vector3 actorPosition, bool alive, uint expectedRevision)
    {
        if (ElderboomLan.IsClient || !alive || Built || expectedRevision != Revision || ElderboomGame.Active == null || !ElderboomGame.Active.IsPlaying) return false;
        if (item != Assemble && item >= parts.Length) return false;
        if (item != Assemble && (CollectedMask & (1 << item)) != 0) return false;
        if (item == Assemble && !Ready) return false;
        if (!Reachable(item, actorPosition)) return false;
        if (item == Assemble) Built = true;
        else CollectedMask |= 1 << item;
        Revision++;
        RefreshVisuals();
        ElderboomGame.Active.ShowNotice(Built ? "MECHANICAL HORSE ASSEMBLED" : $"HORSE COMPONENTS {CollectedCount}/{parts.Length}" + (Ready ? " · RETURN TO THE WORKSHOP" : ""), 4f);
        return true;
    }

    public Vector3 InteractionPoint(byte item) => item == Assemble ? transform.position : parts[item].anchor.position;

    public bool Reachable(byte item, Vector3 actorPosition)
    {
        if (item != Assemble && (item >= parts.Length || parts[item].anchor == null)) return false;
        var area = item == Assemble ? null : ElderboomArea.Find(parts[item].area);
        if (area != null && !area.Unlocked) return false;
        Vector3 point = InteractionPoint(item);
        if (Vector3.Distance(actorPosition, point) > Reach) return false;
        Vector3 eye = actorPosition + Vector3.up * 1.3f;
        Vector3 delta = point + Vector3.up * .6f - eye;
        foreach (var hit in Physics.RaycastAll(eye, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.GetComponentInParent<HDRPSlicePlayer>() != null || hit.collider.GetComponentInParent<ElderboomLanAvatar>() != null) continue;
            if (hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        return true;
    }

    void Update()
    {
        focused = -1;
        if (Built || Time.timeScale == 0 || changedFrame == Time.frameCount) return;
        if (player == null) player = FindAnyObjectByType<HDRPSlicePlayer>();
        if (player == null || ElderboomGame.Active == null || !ElderboomGame.Active.CanInteract || player.IsDead || player.Seated || !player.enabled) return;
        float nearest = float.MaxValue;
        for (int i = 0; i <= parts.Length; i++)
        {
            byte item = i == parts.Length ? Assemble : (byte)i;
            if (item != Assemble && (CollectedMask & (1 << item)) != 0) continue;
            if (!Reachable(item, player.transform.position)) continue;
            Vector3 point = InteractionPoint(item) + Vector3.up * .6f;
            var cam = player.ViewCamera;
            if (cam != null && Vector3.Dot(cam.transform.forward, (point - cam.transform.position).normalized) < .15f) continue;
            float d = (point - player.transform.position).sqrMagnitude;
            if (d < nearest) { nearest = d; focused = item; }
        }
        if (focused < 0 || !player.ActionDown("interact")) return;
        if (focused == Assemble && !Ready) { ElderboomGame.Active.ShowNotice($"FIND THE MISSING COMPONENTS · {CollectedCount}/{parts.Length}", 3f); return; }
        if (ElderboomLan.IsClient) ElderboomLan.RequestHorsePart((byte)focused, Revision);
        else TryInteract((byte)focused, player.transform.position, true, Revision);
    }

    void OnGUI()
    {
        if (focused < 0 || Built || player == null || player.IsDead || player.SettingsOpen || Time.timeScale == 0 || ElderboomGame.Active == null || !ElderboomGame.Active.CanInteract) return;
        string text = focused == Assemble
            ? (Ready ? "ASSEMBLE MECHANICAL HORSE" : $"WORKSHOP · COMPONENTS {CollectedCount}/{parts.Length}")
            : "RECOVER " + parts[focused].label.ToUpperInvariant();
        GUI.Label(new Rect(Screen.width / 2 - 240, Screen.height * .67f, 480, 34), player.ActionLabel("interact") + " · " + text, GUI.skin.box);
    }
}
