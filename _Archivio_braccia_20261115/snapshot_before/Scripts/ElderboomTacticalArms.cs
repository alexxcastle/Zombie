using System.Collections.Generic;
using UnityEngine;

public sealed class ElderboomTacticalArms : MonoBehaviour
{
    public const float ShockIdle = 1f / 60f;
    public const float ShockSound = 6f / 60f;
    public const float ShockImpact = 45f / 60f;
    public const float LiftCharge = 8f / 60f;
    public const float LiftImpact = 100f / 60f;
    public const float KeepFieldStart = 20f / 60f;
    public const float KeepDuration = 7f;

    Transform strikerPivot;
    ElderboomViewmodel striker;
    ElderboomAbilityView lift;
    ElderboomAbilityView keep;
    Renderer[] strikerLeds, shockLeds;
    ElderboomLeftArmAbility overlay;
    Transform hidden;
    LineRenderer ring;
    string mode = "";
    float elapsed;
    float duration;
    bool fieldOn;
    float fieldFade;

    public bool Playing { get; private set; }
    public bool LocksAim => Playing && mode == "shock";
    // The Striker's own shockwave clip, or an ability that swapped the weapon out: the right hand is busy.
    public bool HidesWeapon => Playing && (hidden != null || (mode == "shock" && shockLeds == null));
    public string Mode => mode;

    public void BindStriker(Transform pivot)
    {
        strikerPivot = pivot;
        striker = pivot != null ? pivot.GetComponent<ElderboomViewmodel>() : null;
        strikerLeds = pivot != null ? Leds(pivot.gameObject) : null;
    }

    public float BeginShockwave(Transform activeWeapon)
    {
        Finish();
        mode = "shock";
        elapsed = ShockIdle;
        duration = 1.32f;
        shockLeds = null;
        if (striker != null && activeWeapon == strikerPivot)
        {
            float length = striker.PlayAbility();
            if (length > 0.2f) duration = length;
        }
        else if (striker != null && striker.ability != null && activeWeapon != null)
        {
            // Any other weapon: the glove arm does the shockwave, the right hand keeps the weapon.
            var arsenal = GetComponent<ElderboomArsenal>();
            if (arsenal != null && arsenal.PlayAbilityOverWeapon(striker.ability.length, false))
            {
                duration = striker.ability.length;
                shockLeds = Leds(activeWeapon.gameObject);
            }
        }
        Playing = true;
        return duration;
    }

    public float BeginLift(Transform activeWeapon)
    {
        return BeginSwap("lift", Ensure("Elderboom/Abilities/LiftSmash", ref lift), activeWeapon, 2.65f);
    }

    public float BeginKeep(Transform activeWeapon)
    {
        return BeginSwap("keep", Ensure("Elderboom/Abilities/KeepAway", ref keep), activeWeapon, KeepFieldStart + KeepDuration);
    }

    public void SetField(bool on, float fade)
    {
        fieldOn = on;
        fieldFade = fade;
        if (!on && ring != null) ring.enabled = false;
    }

    // Weapon roots held by the operator's own glove arms (authored grips).
    static bool HeldByOwnArms(Transform root)
    {
        var view = root != null ? root.GetComponent<ElderboomViewmodel>() : null;
        return view != null && view.model != null; // the arm-bone match in Bind decides
    }

    float BeginSwap(string next, ElderboomAbilityView view, Transform activeWeapon, float fallback)
    {
        Finish();
        if (view == null) return 0f;
        mode = next;
        elapsed = 0f;
        duration = view.Length > 0.2f ? view.Length : fallback;
        // Any weapon in the glove arms: the ability model plays unseen and its glove arm is copied onto the arms
        // holding the weapon, which stays in the right hand. Otherwise (Striker, fallback rigs) the weapon is swapped out.
        overlay = null;
        if (activeWeapon != null && activeWeapon != strikerPivot && HeldByOwnArms(activeWeapon))
        {
            var left = activeWeapon.GetComponent<ElderboomLeftArmAbility>();
            if (left == null) left = activeWeapon.gameObject.AddComponent<ElderboomLeftArmAbility>();
            if (left.Bind(view.transform, false, true) >= 8) overlay = left;
        }
        if (overlay == null)
        {
            hidden = activeWeapon;
            if (hidden != null) hidden.gameObject.SetActive(false);
        }
        foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.layer = 0;
            renderer.forceRenderingOff = overlay != null;
        }
        if (overlay != null) { shockLeds = Leds(activeWeapon.gameObject); overlay.Begin(duration); }
        view.gameObject.SetActive(true);
        view.Sample(0f);
        view.SetCharge(0f);
        Playing = true;
        return duration;
    }

    void LateUpdate()
    {
        if (!Playing) return;
        elapsed += Time.deltaTime;
        if (mode == "shock") SetLedCharge(shockLeds ?? strikerLeds, null, ShockEnergy(elapsed));
        else if (mode == "lift" && lift != null)
        {
            lift.Sample(elapsed);
            lift.SetCharge(LiftEnergy(elapsed));
            if (overlay != null) SetLedCharge(shockLeds, null, LiftEnergy(elapsed));
        }
        else if (mode == "keep" && keep != null)
        {
            keep.Sample(elapsed);
            keep.SetCharge(KeepEnergy(elapsed));
            if (overlay != null) SetLedCharge(shockLeds, null, KeepEnergy(elapsed));
        }
        if (fieldOn) PlaceRing();
        if (elapsed >= duration - 0.015f) Finish();
    }

    public void Finish()
    {
        if (overlay != null)
        {
            overlay.Stop(); overlay = null;
            foreach (var v in new[] { lift, keep })
                if (v != null) foreach (var r in v.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = false;
        }
        if (lift != null)
        {
            lift.SetCharge(0f);
            lift.gameObject.SetActive(false);
        }
        if (keep != null)
        {
            keep.SetCharge(0f);
            keep.gameObject.SetActive(false);
        }
        SetLedCharge(strikerLeds, null, 0f);
        if (shockLeds != null) { SetLedCharge(shockLeds, null, 0f); shockLeds = null; }
        if (ring != null) ring.enabled = false;
        fieldOn = false;
        if (hidden != null)
        {
            hidden.gameObject.SetActive(true);
            hidden = null;
        }
        Playing = false;
        mode = "";
        elapsed = 0f;
    }

    static float ShockEnergy(float time)
    {
        if (time < ShockImpact)
        {
            float charge = Mathf.Clamp01(time / ShockImpact);
            return 2f + charge * charge * 16f;
        }
        return Mathf.Max(0f, 16f * (1f - (time - ShockImpact) / 0.48f));
    }

    static float LiftEnergy(float time)
    {
        if (time < LiftCharge) return 0f;
        if (time < LiftImpact)
        {
            float charge = Mathf.Clamp01((time - LiftCharge) / (LiftImpact - LiftCharge));
            return Mathf.Lerp(0.8f, 3.8f, charge * charge);
        }
        return 12f * Mathf.Clamp01(1f - (time - LiftImpact) / 0.18f);
    }

    float KeepEnergy(float time)
    {
        float fadeIn = Mathf.Clamp01(time / KeepFieldStart);
        float timeLeft = Mathf.Clamp(KeepFieldStart + KeepDuration - time, 0f, KeepDuration);
        float fade = Mathf.Min(fadeIn, Mathf.Clamp01(timeLeft / 0.25f));
        return fade * (2.2f + 0.35f * Mathf.Sin(time * 8f));
    }

    ElderboomAbilityView Ensure(string resource, ref ElderboomAbilityView slot)
    {
        if (slot != null) return slot;
        var prefab = Resources.Load<GameObject>(resource);
        var camera = GetComponentInChildren<Camera>(true);
        if (prefab == null || camera == null) return null;
        var owner = GetComponent<HDRPSlicePlayer>();
        var instance = Instantiate(prefab, owner != null ? owner.ViewmodelParent : camera.transform, false);
        instance.SetActive(false);
        slot = instance.GetComponent<ElderboomAbilityView>();
        if (slot != null && slot.model == null && instance.transform.childCount > 0)
            slot.model = instance.transform.GetChild(0).gameObject;
        return slot;
    }

    void PlaceRing()
    {
        if (ring == null)
        {
            var shader = Shader.Find("HDRP/Unlit");
            ring = new GameObject("KeepAwayBoundary").AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.positionCount = 64;
            ring.widthMultiplier = 0.07f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.receiveShadows = false;
            if (shader != null)
            {
                var material = new Material(shader);
                material.SetColor("_UnlitColor", new Color(0.02f, 0.85f, 1f, 1f));
                ring.material = material;
            }
            ring.startColor = ring.endColor = new Color(0.02f, 0.85f, 1f, 0.9f);
        }
        ring.enabled = true;
        var color = new Color(0.02f, 0.85f, 1f, Mathf.Clamp01(fieldFade));
        ring.startColor = ring.endColor = color;
        Vector3 center = transform.position + Vector3.up * 0.06f;
        const float radius = 3f;
        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
        }
    }

    static void SetLedCharge(Renderer[] leds, Light glow, float energy)
    {
        if (leds != null)
        {
            var block = new MaterialPropertyBlock();
            Color color = new Color(0.06f, 0.55f, 1f) * Mathf.Max(0f, energy);
            foreach (var led in leds)
            {
                if (led == null) continue;
                led.GetPropertyBlock(block);
                block.SetColor("_EmissiveColor", color);
                block.SetColor("_EmissionColor", color);
                led.SetPropertyBlock(block);
            }
        }
        if (glow != null) glow.intensity = energy * 800f;
    }

    static Renderer[] Leds(GameObject root)
    {
        var found = new List<Renderer>();
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] != null && materials[i].name.ToLowerInvariant().Contains("shockwave-led"))
                {
                    found.Add(renderer);
                    break;
                }
            }
        }
        return found.ToArray();
    }
}
