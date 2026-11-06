using UnityEngine;

// Phase 2 movement: crouch, slide, vault, flashlight and surface-aware footsteps.
// Split out of HDRPSlicePlayer.cs; Move()/ApplyViewBob() call into these hooks.
public sealed partial class HDRPSlicePlayer
{
    const float CrouchHeight = 1.15f, CrouchCameraDrop = -.55f, SlideCameraDrop = -.72f;
    const float SlideDuration = .75f, SlideCooldown = .9f, VaultDuration = .4f;
    const float SprintDrainPerSecond = 18f, StaminaRecoveryPerSecond = 22f, StaminaRecoveryDelay = .7f;
    float standHeight = 1.8f, crouchOffset, slideRoll, slideUntil, slideReadyAt, vaultT = -1f, vaultApex, airSince;
    Vector3 standCenter = new Vector3(0f, .9f, 0f), slideDir, vaultFrom, vaultTo;
    bool crouched, sliding, movementReady, flashlightOn = true;
    Light flashlight;
    float flashlightIntensity, flashlightRange;
    int lastStep = -1;
    public bool Crouched => crouched;
    public bool Sliding => sliding;
    public bool Vaulting => vaultT >= 0f;
    public bool FlashlightOn => flashlightOn;

    void InitMovement()
    {
        if (movementReady || character == null) return;
        movementReady = true;
        standHeight = character.height; standCenter = character.center;
        if (viewCamera != null)
            foreach (var light in viewCamera.GetComponentsInChildren<Light>(true))
                if (light.type == LightType.Spot && light.transform.parent == viewCamera.transform) { flashlight = light; break; }
        if (flashlight != null)
        {
            flashlightIntensity = flashlight.intensity; flashlightRange = flashlight.range;
            // A camera-mounted beam must illuminate surfaces without filling the
            // player's view with an opaque cone of local/global volumetric fog.
            var hd = flashlight.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();
            if (hd != null) hd.volumetricDimmer = 0f;
        }
    }

    void ResetMovement()
    {
        InitMovement();
        crouched = sliding = false; crouchOffset = slideRoll = 0f; vaultT = -1f;
        if (character != null)
        {
            character.enabled = true;
            character.height = standHeight; character.center = standCenter;
        }
        SetFlashlight(true, false);
    }

    // ---- Flashlight ------------------------------------------------------------------
    void UpdateFlashlightInput() { if (Down("flashlight")) SetFlashlight(!flashlightOn, true); }

    void SetFlashlight(bool on, bool sound)
    {
        flashlightOn = on;
        if (flashlight == null) return;
        // "Off" keeps a faint close fill so the held weapon stays readable.
        flashlight.intensity = on ? flashlightIntensity : flashlightIntensity * .035f;
        flashlight.range = on ? flashlightRange : 1.2f;
        if (sound) ElderboomAudio.Play("kenney/impactPlate_light_00" + Random.Range(0, 5), transform.position, .22f, false, 1.7f);
    }

    // ---- Crouch and slide ------------------------------------------------------------
    void UpdateCrouch(float x, float z)
    {
        if (Down("crouch") && sprinting && character.isGrounded && !sliding && Time.time >= slideReadyAt) StartSlide(x, z);
        if (sliding && (Time.time >= slideUntil || !character.isGrounded && verticalSpeed > 0f)) EndSlide();
        bool want = Held("crouch") || sliding;
        if (!want && crouched && !CanStand()) want = true;
        crouched = want;
        float height = crouched ? CrouchHeight : standHeight;
        if (!Mathf.Approximately(character.height, height))
        {
            character.height = height;
            character.center = new Vector3(standCenter.x, standCenter.y - (standHeight - height) * .5f, standCenter.z);
        }
        float drop = sliding ? SlideCameraDrop : crouched ? CrouchCameraDrop : 0f;
        crouchOffset = Mathf.MoveTowards(crouchOffset, drop, Time.deltaTime * (sliding ? 4f : 2.6f));
        slideRoll = Mathf.Lerp(slideRoll, sliding ? -3.5f : 0f, 1f - Mathf.Exp(-10f * Time.deltaTime));
    }

    bool CanStand()
    {
        float r = character.radius * .95f;
        Vector3 start = transform.position + Vector3.up * (CrouchHeight - r);
        return !Physics.SphereCast(start, r, Vector3.up, out _, standHeight - CrouchHeight + .05f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore);
    }

    void StartSlide(float x, float z)
    {
        sliding = true; slideUntil = Time.time + SlideDuration;
        Vector3 dir = transform.right * x + transform.forward * z; dir.y = 0f;
        slideDir = dir.sqrMagnitude > .01f ? dir.normalized : transform.forward;
        ElderboomAudio.Play("kenney/impactSoft_medium_00" + Random.Range(0, 5), transform.position, .4f, false, .75f);
        AddTrauma(.08f);
    }

    void EndSlide() { sliding = false; slideReadyAt = Time.time + SlideCooldown; }

    float SlideSpeed()
    {
        float t = 1f - Mathf.Clamp01((slideUntil - Time.time) / SlideDuration);
        return Mathf.Lerp(sprintSpeed * 1.45f, walkSpeed * .5f, t * t);
    }

    float MoveSpeed() => sliding ? SlideSpeed() : crouched ? walkSpeed * .5f : sprinting ? sprintSpeed : walkSpeed;

    // ---- Vault / mantle --------------------------------------------------------------
    // Jumping while moving into a waist-high obstacle climbs onto it, or over it when
    // it is thin (village stone walls, fences, benches).
    bool TryVault(Vector3 planar)
    {
        Vector3 forward = transform.forward; forward.y = 0f; forward.Normalize();
        if (planar.sqrMagnitude < .01f || Vector3.Dot(planar.normalized, forward) < .5f) return false;
        int mask = ElderboomCombat.WorldMask;
        Vector3 feet = transform.position;
        if (!Physics.Raycast(feet + Vector3.up * .55f, forward, out var wall, character.radius + .65f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (wall.collider.GetComponentInParent<ElderboomZombie>() != null || wall.collider.GetComponentInParent<ElderboomGate>() != null) return false;
        Vector3 probe = wall.point + forward * .3f; probe.y = feet.y + 1.7f;
        if (!Physics.Raycast(probe, Vector3.down, out var top, 1.7f, mask, QueryTriggerInteraction.Ignore)) return false;
        float rise = top.point.y - feet.y;
        if (rise < .45f || rise > 1.35f || Vector3.Dot(top.normal, Vector3.up) < .7f) return false;
        float r = character.radius;
        Vector3 beyond = wall.point + forward * (.3f + r + .5f);
        Vector3 destination = top.point;
        if (Physics.Raycast(new Vector3(beyond.x, top.point.y + .5f, beyond.z), Vector3.down, out var far, 3f, mask, QueryTriggerInteraction.Ignore)
            && far.point.y < top.point.y - .3f)
            destination = far.point; // thin obstacle: drop down on the far side
        if (Physics.CheckCapsule(destination + Vector3.up * (r + .06f), destination + Vector3.up * (standHeight - r), r * .95f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (Physics.CheckSphere(top.point + Vector3.up * (r + .2f), r * .8f, mask, QueryTriggerInteraction.Ignore)) return false;
        vaultFrom = feet; vaultTo = destination; vaultApex = top.point.y + .22f; vaultT = 0f;
        if (sliding) EndSlide();
        character.enabled = false; verticalSpeed = 0f;
        ElderboomAudio.Play("kenney/impactSoft_heavy_00" + Random.Range(0, 5), transform.position, .32f);
        AddTrauma(.07f);
        return true;
    }

    bool UpdateVault()
    {
        if (vaultT < 0f) return false;
        vaultT += Time.deltaTime / VaultDuration;
        float t = Mathf.Clamp01(vaultT);
        Vector3 p = Vector3.Lerp(vaultFrom, vaultTo, Mathf.SmoothStep(0f, 1f, t));
        p.y = t < .5f ? Mathf.Lerp(vaultFrom.y, vaultApex, 1f - Mathf.Pow(1f - t * 2f, 2f))
                      : Mathf.Lerp(vaultApex, vaultTo.y, Mathf.Pow((t - .5f) * 2f, 2f));
        transform.position = p;
        moveX = moveZ = 0f; locomoting = false;
        if (vaultT >= 1f) { vaultT = -1f; character.enabled = true; verticalSpeed = -1.5f; }
        return true;
    }

    // ---- Footsteps -------------------------------------------------------------------
    void PlayFootstep(float loudness = 1f)
    {
        string clip = "footsteps/Fantozzi-Sand" + (Random.value > .5f ? "L" : "R") + Random.Range(1, 4);
        if (Physics.Raycast(transform.position + Vector3.up * .3f, Vector3.down, out var ground, 1.2f, ElderboomCombat.WorldMask, QueryTriggerInteraction.Ignore))
        {
            var surface = ElderboomFeel.Classify(ground);
            string set = surface == ElderboomSurface.Wood ? "footstep_wood_00" : surface == ElderboomSurface.Dirt ? "footstep_grass_00" : "footstep_concrete_00";
            int index = Random.Range(0, 5); if (index == lastStep) index = (index + 1) % 5; lastStep = index;
            clip = "kenney/" + set + index;
        }
        float volume = Mathf.Lerp(.16f, .3f, viewSprint) * (crouched ? .45f : 1f) * loudness;
        ElderboomAudio.Play(clip, transform.position, volume, false, Random.Range(.94f, 1.06f));
    }

    // Landing after a fall or a vault.
    void TrackAirborne(bool grounded)
    {
        if (wasGrounded && !grounded) airSince = Time.time;
        if (!wasGrounded && grounded && Time.time - airSince > .28f) PlayFootstep(1.5f);
    }

    // ---- Motor state machine ---------------------------------------------------------
    ElderboomMotorState motorState;
    public ElderboomMotorState MotorState => motorState;
    public event System.Action<ElderboomMotorState, ElderboomMotorState> MotorStateChanged;

    // Priority order: the first matching condition wins.
    ElderboomMotorState ResolveMotorState()
    {
        if (Vaulting) return ElderboomMotorState.Vaulting;
        if (sliding) return ElderboomMotorState.Sliding;
        if (character == null || !character.enabled || !character.isGrounded) return ElderboomMotorState.Airborne;
        if (crouched) return ElderboomMotorState.Crouching;
        if (sprinting) return ElderboomMotorState.Sprinting;
        return locomoting ? ElderboomMotorState.Walking : ElderboomMotorState.Idle;
    }

    void UpdateMotorState()
    {
        var next = ResolveMotorState();
        if (next == motorState) return;
        var previous = motorState;
        motorState = next;
        MotorStateChanged?.Invoke(previous, next);
    }

    // ---- Look, locomotion and camera ---------------------------------------------------
    void Look()
    {
        float yaw = Input.GetAxisRaw("Mouse X") * pause.Sensitivity;
        float mouseY = Input.GetAxisRaw("Mouse Y") * pause.Sensitivity;
        transform.Rotate(0f, yaw, 0f, Space.Self);
        pitch = Mathf.Clamp(pitch - mouseY, -82f, 82f);
        lookDeltaX = yaw; lookDeltaY = mouseY;
    }

    void Move()
    {
        float x = (Held("move_right") ? 1f : 0f) - (Held("move_left") ? 1f : 0f);
        float z = (Held("move_forward") ? 1f : 0f) - (Held("move_back") ? 1f : 0f);
        InitMovement();
        if (UpdateVault()) return;
        UpdateCrouch(x, z);
        bool request=Held("sprint") && z>.1f && !aiming && !crouched;
        if(!request)exhausted=false;
        bool wasSprinting=sprinting;sprinting=request && !exhausted && stamina>0;
        if(sprinting){stamina=Mathf.Max(0,stamina-SprintDrainPerSecond*Time.deltaTime);regenAt=Time.time+StaminaRecoveryDelay;if(stamina<=0){exhausted=true;sprinting=false;}}
        else if(Time.time>=regenAt)stamina=Mathf.Min(100,stamina+StaminaRecoveryPerSecond*Time.deltaTime);
        if(sprinting && !wasSprinting)sprintStarted=Time.time;
        if(!sprinting && wasSprinting && Time.time-sprintStarted>1.2f)ElderboomAudio.Play("player/exhausted_gasp_0"+Random.Range(1,3),transform.position,.5f);
        viewSprint = Mathf.Lerp(viewSprint, sprinting ? 1f : 0f, 1f - Mathf.Exp(-6f * Time.deltaTime));
        moveX = x;
        moveZ = z;
        float speed = MoveSpeed();
        Vector3 planar = sliding ? slideDir : transform.right * x + transform.forward * z;
        if (planar.sqrMagnitude > 1f) planar.Normalize();
        if (character.isGrounded)
        {
            if (verticalSpeed < 0f) verticalSpeed = -1.5f;
            if (Down("jump"))
            {
                if (TryVault(planar)) return;
                if (!crouched || sliding)
                {
                    if (sliding) EndSlide();
                    verticalSpeed = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
                }
            }
        }
        verticalSpeed += Physics.gravity.y * Time.deltaTime;
        character.Move((planar * speed + Vector3.up * verticalSpeed) * Time.deltaTime);
        locomoting = character.isGrounded && planar.sqrMagnitude > .1f;
    }

    void ApplyViewBob()
    {
        if (viewCamera == null) return;
        if (!cameraBaseReady)
        {
            cameraBaseLocal = viewCamera.transform.localPosition;
            cameraBaseReady = true;
        }
        float blend = 1f - Mathf.Exp(-8f * Time.deltaTime);
        viewKick.Advance(Time.deltaTime);
        bobWeight = Mathf.Lerp(bobWeight, locomoting ? 1f : 0f, blend);
        float frequency = Mathf.Lerp(6.8f, 11.2f, viewSprint) * (crouched ? .72f : 1f);
        float previous = headBobTime;
        if (locomoting) headBobTime += Time.deltaTime * frequency;
        float aimDamp = aiming ? 0.4f : 1f;
        // Halve sprint camera travel and tilt for a steadier running view.
        float vertical = Mathf.Lerp(0.028f, 0.025f, viewSprint) * aimDamp;
        float lateral = Mathf.Lerp(0.012f, 0.011f, viewSprint) * aimDamp;
        float depth = 0.005f * aimDamp;
        float step = Mathf.Abs(Mathf.Sin(headBobTime));
        float sway = Mathf.Sin(headBobTime * 0.5f);
        TrackAirborne(character.isGrounded);
        if (!wasGrounded && character.isGrounded) landingVelocity = Mathf.Lerp(-0.7f, -1.1f, viewSprint);
        wasGrounded = character.isGrounded;
        landingOffset += landingVelocity * Time.deltaTime;
        landingVelocity = Mathf.Lerp(landingVelocity, 0f, blend);
        landingOffset = Mathf.Lerp(landingOffset, 0f, 1f - Mathf.Exp(-10f * Time.deltaTime));
        Vector3 bob = new Vector3(sway * lateral - moveX * 0.006f * viewSprint, step * vertical * bobWeight + landingOffset, Mathf.Sin(headBobTime) * depth * bobWeight);
        viewCamera.transform.localPosition = cameraBaseLocal + bob + Vector3.up * crouchOffset;
        float roll = sway * Mathf.Lerp(0.35f, 0.5f, viewSprint) * bobWeight * aimDamp - moveX * Mathf.Lerp(0.25f, 0.4f, viewSprint);
        float lean = Mathf.Lerp(0.15f, 0.35f, viewSprint);
        var shake = viewKick.Shake;
        viewCamera.transform.localRotation = Quaternion.Euler(pitch + lean - viewKick.Pitch + shake.x, viewKick.Yaw + shake.y, roll + slideRoll + viewKick.Roll + shake.z);
        fovKick = Mathf.MoveTowards(fovKick, 0f, Time.deltaTime * 25f);
        float zoom = 0f;
        if (aiming && audioCategory != ElderboomIds.Category.Melee)
        {
            zoom = weaponDisplayName != null && weaponDisplayName.Contains("1911") ? 10f : 17f;
            zoom = Mathf.Min(zoom, Mathf.Max(0f, pause.FieldOfView - 45f));
        }
        float targetFov = pause.FieldOfView - zoom + fovKick + 2.5f * viewSprint + (sliding ? 5f : 0f);
        viewCamera.fieldOfView = Mathf.Lerp(viewCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (!locomoting) return;
        const float plant = Mathf.PI * 0.5f;
        float now = Mathf.Repeat(headBobTime, Mathf.PI);
        float before = Mathf.Repeat(previous, Mathf.PI);
        bool planted = now >= before ? before < plant && now >= plant : before < plant || now >= plant;
        if (planted && !sliding) PlayFootstep();
    }
}

public enum ElderboomMotorState { Idle, Walking, Sprinting, Crouching, Sliding, Vaulting, Airborne }
