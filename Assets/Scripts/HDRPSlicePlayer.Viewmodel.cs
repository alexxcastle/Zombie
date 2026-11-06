using System.Collections.Generic;
using UnityEngine;

// Phase 3 viewmodel feel: FOV-independent weapon framing, look sway, strafe tilt,
// draw animation on weapon change and pulling the weapon back near walls.
public sealed partial class HDRPSlicePlayer
{
    // Weapon framing was calibrated at this field of view.
    const float ViewmodelReferenceFov = 72f;
    const float EquipDuration = .42f;
    Transform viewmodelFov;
    float lookDeltaX, lookDeltaY, equipT = 1f, obstruct, strafeTilt;
    Vector2 sway;

    // Parent for every first-person model. Its camera-space Z scale keeps the
    // weapon's on-screen size constant when the player changes the FOV setting
    // (the same result as a separate viewmodel FOV, without a second camera).
    public Transform ViewmodelParent
    {
        get
        {
            EnsureViewmodelRoot();
            return viewmodelFov != null ? viewmodelFov : viewCamera != null ? viewCamera.transform : transform;
        }
    }
    public bool Equipping => equipT < .72f;
    public bool WeaponObstructed => obstruct > .8f;

    void EnsureViewmodelRoot()
    {
        if (viewmodelFov != null || viewCamera == null) return;
        var cameraTransform = viewCamera.transform;
        viewmodelFov = cameraTransform.Find("ViewmodelFov");
        if (viewmodelFov == null)
        {
            viewmodelFov = new GameObject("ViewmodelFov").transform;
            viewmodelFov.SetParent(cameraTransform, false);
        }
        var move = new List<Transform>();
        foreach (Transform child in cameraTransform)
            if (child != viewmodelFov && child.GetComponent<Light>() == null) move.Add(child);
        foreach (var child in move) child.SetParent(viewmodelFov, false);
    }

    void BeginEquip() { equipT = 0f; }

    void UpdateViewmodelExtras(float dt)
    {
        EnsureViewmodelRoot();
        if (viewmodelFov != null)
        {
            float k = Mathf.Tan(ViewmodelReferenceFov * .5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Clamp(pause != null ? pause.FieldOfView : 72f, 30f, 120f) * .5f * Mathf.Deg2Rad);
            viewmodelFov.localScale = new Vector3(1f, 1f, k);
        }
        // Look sway: the weapon lags slightly behind fast mouse movement.
        float ads = aiming ? .3f : 1f;
        Vector2 velocity = dt > 0f ? new Vector2(lookDeltaX, lookDeltaY) / dt : Vector2.zero;
        Vector2 target = Vector2.ClampMagnitude(-velocity * .011f * ads, 5f);
        sway = Vector2.Lerp(sway, target, 1f - Mathf.Exp(-11f * dt));
        strafeTilt = Mathf.Lerp(strafeTilt, -moveX * 2.4f * (aiming ? .3f : 1f), 1f - Mathf.Exp(-8f * dt));
        equipT = Mathf.Min(1f, equipT + dt / EquipDuration);
        // Pull the weapon in when the camera is right against a wall.
        float target01 = 0f;
        if (viewCamera != null && Physics.Raycast(viewCamera.transform.position, viewCamera.transform.forward, out var hit, .8f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<ElderboomZombie>() == null && !ElderboomCombat.IsOwner(hit.collider, transform))
            target01 = Mathf.Clamp01((.75f - hit.distance) / .45f);
        obstruct = Mathf.MoveTowards(obstruct, target01, dt * 5f);
    }

    Vector3 ViewmodelOffset()
    {
        float draw = 1f - Mathf.Pow(1f - equipT, 3f);
        return new Vector3(sway.x * .0022f, sway.y * .0022f, 0f)
             + new Vector3(0f, -.17f, -.05f) * (1f - draw)
             + new Vector3(.02f, -.07f, -.13f) * obstruct;
    }

    Quaternion ViewmodelRotation()
    {
        float draw = 1f - Mathf.Pow(1f - equipT, 3f);
        return Quaternion.Euler(-sway.y + 28f * (1f - draw) + 32f * obstruct, sway.x - 22f * obstruct, sway.x * .6f + strafeTilt - 10f * (1f - draw));
    }
}
