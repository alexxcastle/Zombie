using System;
using UnityEngine;

public sealed class ElderboomViewmodel : MonoBehaviour
{
    public GameObject model;
    public string operatorId;
    public AnimationClip idle, reload, ability;
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
    string action = "idle";
    float elapsed;
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
    public float ReloadDuration => authoredReloadTiming && reload != null ? reload.length : operatorId == ElderboomIds.Soph ? 2.5f : 2.3f;
    public void ResetToIdle()
    {
        action = "idle"; elapsed = 0f;
        RestoreModelBase();
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
    void OnEnable() { if (mixer != null && mixer.Valid && model != null) ResetToIdle(); }
    void OnDestroy() { mixer?.Dispose(); mixer = null; }
    public float PlayReload() { action = "reload"; elapsed = 0; return ReloadDuration; }
    public float PlayAbility() { action = "ability"; elapsed = 0; return operatorId == ElderboomIds.Soph ? 6.867f : operatorId == ElderboomIds.Eli ? 3.1f : ability != null ? ability.length : 1f; }
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
        elapsed += Time.deltaTime;
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
