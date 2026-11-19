using System;
using UnityEngine;

public sealed class ElderboomViewmodel : MonoBehaviour
{
    public GameObject model;
    public string operatorId;
    public AnimationClip idle, reload, ability;
    // Aim distance set by the hold itself (ElderboomAuthoredGrip own-pose holds): the player does not pull it closer.
    [System.NonSerialized] public bool keepAdsRelief;
    public Vector3 hipPosition;
    public Quaternion hipRotation = Quaternion.identity;
    public Vector3 adsPosition;
    public Quaternion adsRotation = Quaternion.identity;
    public float adsRelief = 0.22f;
    public bool hasAds;
    public bool framingStored;
    public bool authoredReloadTiming;
    // Authored grips (ElderboomAuthoredGrip) have no reload clip: the whole model dips and
    // rolls around the right hand while the idle keeps playing.
    public bool proceduralReload;
    public Vector3 reloadPivot;
    Vector3 modelBasePosition;
    Quaternion modelBaseRotation = Quaternion.identity;
    bool modelBaseStored;
    public void StoreModelBase()
    {
        if (model == null) return;
        modelBasePosition = model.transform.localPosition;
        modelBaseRotation = model.transform.localRotation;
        modelBaseStored = true;
    }
    void ApplyProceduralReload(float progress)
    {
        if (!modelBaseStored || model == null) return;
        // Down and away, a quarter turn of roll, back up: weight peaks mid reload.
        float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / .22f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - progress) / .25f));
        Quaternion turn = Quaternion.Euler(18f * w, -8f * w, -28f * w);
        var t = model.transform;
        t.localRotation = turn * modelBaseRotation;
        t.localPosition = turn * (modelBasePosition - reloadPivot) + reloadPivot + new Vector3(.02f, -.07f, -.02f) * w;
    }
    void RestoreModelBase()
    {
        if (!modelBaseStored || model == null) return;
        model.transform.localPosition = modelBasePosition;
        model.transform.localRotation = modelBaseRotation;
    }
    [NonSerialized] public ElderboomDonorMotion donor;
    // Operator arms on the Basic FPS rig, animated by the shared Animator Controller (ElderboomBasicArms).
    [NonSerialized] public ElderboomBasicArms basic;
    ElderboomDonorMotion.Motion donorAction, donorCurrent;
    float donorTime, donorTransition, donorLocomotion, donorFireDuration;
    bool donorSprinting;
    public bool UsesDonorSprint => basic != null ? basic.SprintActive : donorSprinting && donor?.Find("run") != null;
    public bool UsesDonorWalk => basic != null ? basic.WalkActive : donorLocomotion > .05f && donor?.Find("walk") != null;
    public bool UsesDonorDraw => basic != null || donor?.Find("equip") != null;

    public void SetLocomotion(float amount, bool sprinting)
    {
        donorLocomotion = amount;
        donorSprinting = sprinting;
        basic?.SetLocomotion(amount, sprinting);
    }

    public void PlayDonorDraw()
    {
        if (basic != null) { basic.PlayDraw(); return; }
        if (donor == null) return;
        donorAction = donor.Find("equip");
        elapsed = 0f;
    }

    public void PlayDonorInspect()
    {
        var inspect = donor?.Find("inspect");
        if (inspect == null || donorAction != null) return;
        donorAction = inspect;
        elapsed = 0f;
    }

    public bool PlayDonorFire(float interval)
    {
        if (basic != null) return basic.PlayFire(interval);
        var fire = donor?.Find("fire");
        if (fire == null) return false;
        donorAction = fire;
        donorFireDuration = Mathf.Min(interval, fire.duration);
        elapsed = 0f;
        donorTransition = 0f;
        return true;
    }

    void UpdateDonor(float delta)
    {
        elapsed += delta;
        var motion = donorAction;
        if (motion?.role == "inspect" && (aiming || donorSprinting)) { donorAction = null; motion = null; }
        float time = elapsed;
        if (motion != null)
        {
            float duration = motion.role == "fire" ? donorFireDuration : motion.role == "equip" ? .42f : IsReload(motion) ? DonorReloadTime(motion) : motion.duration;
            time = elapsed * motion.duration / Mathf.Max(.01f, duration);
            if (elapsed >= duration) { donorAction = null; action = "idle"; motion = null; }
        }
        if (motion == null)
        {
            bool moving = donorLocomotion > .05f && !aiming;
            var run = donorSprinting && moving ? donor.Find("run") : null;
            motion = run ?? (moving ? donor.Find("walk") : null) ?? donor.Idle;
            if (motion != donorCurrent) donorTime = 0f;
            if (!aiming) donorTime += delta * (donorSprinting && run == null && moving ? 1.55f : 1f);
            time = aiming ? 0f : Mathf.Repeat(donorTime, motion.duration);
        }
        if (motion != donorCurrent) { donorCurrent = motion; donorTransition = 0f; }
        donorTransition = Mathf.Min(1f, donorTransition + delta * (motion.role == "fire" ? 80f : 18f));
        donor.Apply(motion, time, donorTransition);
    }

    string action = "idle";
    float elapsed;
    const float AdsIdleHoldSeconds = 3f;
    float adsIdleHoldRemaining;
    bool aiming;
    AnimationClip classifiedIdle;
    bool frozenIdle;
    // Playables: idle (0), reload (1) and ability (2) on one mixer with short cross-fades.
    // The time of each clip is still computed below (calibrated remaps), so timing and
    // framing are unchanged; the mixer removes the hard snaps between actions.
    const float CrossFade = .12f;
    ElderboomClipMixer mixer;
    bool mixerTried;
    GameObject mixerModel;
    public bool Busy => action != "idle";
    // Donor reload clips run up to 7 s (tube shotguns, revolver): gameplay keeps them short by playing the clip faster.
    const float DonorReloadCap = 3.2f, DonorEmptyReloadCap = 3.8f;
    static bool IsReload(ElderboomDonorMotion.Motion m) => m != null && (m.role == "reload" || m.role == "empty");
    static float DonorReloadTime(ElderboomDonorMotion.Motion m) => Mathf.Min(m.duration, m.role == "empty" ? DonorEmptyReloadCap : DonorReloadCap);
    public float ReloadDuration => basic != null ? basic.ReloadDuration : donor != null ? DonorReloadTime(IsReload(donorAction) ? donorAction : donor.Find("reload")) : authoredReloadTiming && reload != null ? reload.length : operatorId == ElderboomIds.Soph ? 2.5f : 2.3f;
    public void ResetToIdle()
    {
        action = "idle"; elapsed = 0f;
        SetAiming(false);
        if (basic != null) { basic.ResetToIdle(); return; }
        RestoreModelBase();
        if (donor != null)
        {
            donorAction = null;
            donorCurrent = donor.Idle;
            donorTime = 0f;
            donorTransition = 1f;
            donor.Apply(donor.Idle, 0f);
            return;
        }
        if (model == null || idle == null) return;
        if (EnsureMixer()) { mixer.SetTime(0, IdleTime(0f)); mixer.Snap(0); }
        else ElderboomAnimationSampling.Sample(idle,model,IdleTime(0f));
    }
    bool EnsureMixer()
    {
        if (mixerModel != model) { mixer?.Dispose(); mixer = null; mixerTried = false; mixerModel = model; }
        if (!mixerTried) { mixerTried = true; mixer = ElderboomClipMixer.Create(model, name + "_Viewmodel", idle, reload, ability); }
        return mixer != null && mixer.Valid && mixer.Has(0);
    }
    void OnEnable() { if (basic != null) return; if (mixer != null && mixer.Valid && model != null) ResetToIdle(); }
    void OnDisable() => SetAiming(false);
    void OnDestroy() { mixer?.Dispose(); mixer = null; }
    public float PlayReload(bool empty = false, bool tube = false, int shells = 0)
    {
        action = "reload";
        elapsed = 0f;
        if (basic != null) return basic.PlayReload(empty, shells);
        if (donor != null)
            // Empty: the full reload. Otherwise the short one (tube shotguns: the single-shell top-up).
            donorAction = (empty ? donor.Find("empty") : null) ?? donor.Find("reload");
        return ReloadDuration;
    }
    public float PlayAbility() { action = "ability"; elapsed = 0; return operatorId == ElderboomIds.Soph ? 6.867f : operatorId == ElderboomIds.Eli ? 3.1f : ability != null ? ability.length : 1f; }

    public void SetAiming(bool active)
    {
        basic?.SetAiming(active);
        if (aiming == active) return;
        aiming = active;
        adsIdleHoldRemaining = active ? AdsIdleHoldSeconds : 0f;
    }

    void AdvancePlayback(float deltaTime)
    {
        float held = Mathf.Min(deltaTime, adsIdleHoldRemaining);
        adsIdleHoldRemaining -= held;
        // Hold the current idle sample and resume from it. Action clocks keep running.
        elapsed += action == "idle" ? deltaTime - held : deltaTime;
    }

    float IdleTime(float time)
    {
        if(operatorId==ElderboomIds.Tactical)return 1f/60f;
        if(classifiedIdle!=idle)
        {
            classifiedIdle=idle;
            var name=idle!=null?idle.name:"";
            // These FBXs contain one complete demonstration, not a looping idle.
            // Godot freezes its first pose too; playing it moved the arms out of view.
            frozenIdle=name.IndexOf("idle",StringComparison.OrdinalIgnoreCase)<0
                && name.IndexOf("stand",StringComparison.OrdinalIgnoreCase)<0;
        }
        return frozenIdle?0f:Mathf.Repeat(time,Mathf.Max(.01f,idle!=null?idle.length:1f));
    }
    void LateUpdate()
    {
        if (basic != null) { if (action != "idle" && !basic.Busy) action = "idle"; return; }
        if (donor != null)
        {
            UpdateDonor(Time.deltaTime);
            return;
        }
        AdvancePlayback(Time.deltaTime);
        bool procedural = proceduralReload && action == "reload" && reload == null;
        AnimationClip clip = action == "reload" ? (procedural ? idle : reload) : action == "ability" ? ability : idle;
        if (model == null || !model.activeInHierarchy || clip == null) { if (action != "idle" && elapsed > 2.5f) action = "idle"; return; }
        float source = elapsed;
        int slot = action == "reload" && !procedural ? 1 : action == "ability" ? 2 : 0;
        if (procedural)
        {
            float duration = ReloadDuration;
            ApplyProceduralReload(Mathf.Clamp01(elapsed / duration));
            source = IdleTime(elapsed);
            if (elapsed >= duration) { action = "idle"; elapsed = 0; RestoreModelBase(); }
        }
        else if (action == "reload")
        {
            float duration = ReloadDuration;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (operatorId == ElderboomIds.Soph && !authoredReloadTiming) source = progress < .46875f ? Mathf.Lerp(0, .9f, progress / .46875f) : Mathf.Lerp(.9f, clip.length, (progress - .46875f) / .53125f);
            else source = progress * clip.length;
            if (elapsed >= duration) { action = "idle"; elapsed = 0; }
        }
        else if (action == "ability")
        {
            if (operatorId == ElderboomIds.Soph) source = elapsed < 1.65556f ? elapsed * 1.5f : elapsed < 4.65556f ? 2.48333f + elapsed - 1.65556f : 5.48333f + (elapsed - 4.65556f) * 1.5f;
            else if (operatorId == ElderboomIds.Eli) source = elapsed < 1.83333f ? elapsed * 1.5f : 2.75f + (elapsed - 1.83333f) * 2.8f;
            if (source >= clip.length) { action = "idle"; elapsed = 0; }
        }
        else source = IdleTime(elapsed);
        source = Mathf.Clamp(source,0,clip.length);
        if (EnsureMixer() && mixer.Has(slot))
        {
            mixer.SetTime(slot, source);
            mixer.Evaluate(slot, CrossFade, Time.deltaTime);
        }
        else ElderboomAnimationSampling.Sample(clip,model,source);
    }
}
