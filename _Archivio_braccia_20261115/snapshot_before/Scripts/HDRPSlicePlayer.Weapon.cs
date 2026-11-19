using System.Collections;
using UnityEngine;

public enum ElderboomWeaponState { Ready, Lowered, Equipping, Reloading, Ability, Disabled }

// Weapon side of the player: an explicit state machine gates firing, then fire,
// reload, clip playback and weapon binding.
public sealed partial class HDRPSlicePlayer
{
    ElderboomWeaponState weaponState;
    public ElderboomWeaponState WeaponState => weaponState;
    public event System.Action<ElderboomWeaponState, ElderboomWeaponState> WeaponStateChanged;

    // Priority order: the first matching condition wins.
    ElderboomWeaponState ResolveWeaponState(bool abilityBusy)
    {
        return ResolveWeaponState(abilityBusy, abilityBusy);
    }

    ElderboomWeaponState ResolveWeaponState(bool abilityBusy, bool handBusy)
    {
        if (dead || WeaponDeposited) return ElderboomWeaponState.Disabled;
        if (reloading) return ElderboomWeaponState.Reloading;
        if (handBusy) return ElderboomWeaponState.Ability;
        if (Equipping) return ElderboomWeaponState.Equipping;
        // Sprint-to-fire: the weapon must come back up from the sprint pose before it fires.
        if (sprinting || sprintPose >= .35f || Vaulting || WeaponObstructed) return ElderboomWeaponState.Lowered;
        return ElderboomWeaponState.Ready;
    }

    void SetWeaponState(ElderboomWeaponState next)
    {
        if (next == weaponState) return;
        var previous = weaponState;
        weaponState = next;
        if (next != ElderboomWeaponState.Ready) spreadBloom = Mathf.Min(spreadBloom, 1f);
        WeaponStateChanged?.Invoke(previous, next);
    }

    void UpdateWeaponPose(ElderboomTacticalArms arms, bool abilityBusy)
    {
        aiming = !WeaponDeposited && hasAdsReference && Held("aim") && !reloading && !abilityBusy && (arms == null || !arms.LocksAim);
        portViewmodel?.SetAiming(aiming);
        portViewmodel?.SetLocomotion(bobWeight, sprinting);
        if (aiming != wasAiming && audioCategory != ElderboomIds.Category.Melee) ElderboomAudio.AimFoley(audioCategory, transform.position);
        wasAiming = aiming;
        if (weaponRoot != null && (arms == null || !arms.Playing || arms.Mode == "shock"))
        {
            if (!poseReady)
            {
                posePosition = weaponRoot.localPosition;
                poseRotation = weaponRoot.localRotation;
                poseReady = true;
            }
            // Aim time per weapon (pistols fast, long guns slower) and slower with heavy attachments (ElderboomWeaponMods).
            float blend = 1f - Mathf.Exp(-(3f / Mathf.Max(.05f, ElderboomWeaponMods.AimTime(definition))) * Time.deltaTime);
            posePosition = Vector3.Lerp(posePosition, aiming ? adsPosition : hipPosition, blend);
            poseRotation = Quaternion.Slerp(poseRotation, aiming ? adsRotation : hipRotation, blend);
            float sprintTarget = sprinting && !reloading ? 1f : 0f;
            sprintPose = Mathf.Lerp(sprintPose, sprintTarget, 1f - Mathf.Exp(-14f * Time.deltaTime));
            recoil.Advance(Time.deltaTime);
            UpdateViewmodelExtras(Time.deltaTime);
            float gait = portViewmodel != null && portViewmodel.UsesDonorWalk && !aiming ? 0f : bobWeight * (aiming ? 0.28f : 1f);
            float sprintOffset = portViewmodel != null && portViewmodel.UsesDonorSprint ? 0f : sprintPose;
            float pump = Mathf.Lerp(0.45f, 1f, viewSprint);
            float step = Mathf.Abs(Mathf.Sin(headBobTime));
            float sway = Mathf.Sin(headBobTime * 0.5f);
            Vector3 weaponBob = new Vector3(sway * 0.01f, step * 0.014f, Mathf.Sin(headBobTime) * 0.005f) * pump * gait;
            Quaternion weaponBobRotation = Quaternion.Euler(Mathf.Sin(headBobTime) * 0.55f * pump * gait, sway * -0.35f * pump * gait, sway * 0.9f * pump * gait);
            // In aim the sight is already a few centimetres from the lens. The backward kick would push the
            // hollow receiver over the camera. Keep the tilt; drop most of the slide toward the eye.
            Vector3 kick = recoil.position;
            if (aiming) kick.z *= .15f;
            weaponRoot.localPosition = posePosition + kick + SprintDrop * sprintOffset + weaponBob + ViewmodelOffset();
            weaponRoot.localRotation = ViewmodelRotation() * Quaternion.Slerp(Quaternion.identity, SprintTilt, sprintOffset) * weaponBobRotation * Quaternion.Euler(recoil.rotation * Mathf.Rad2Deg) * poseRotation;
            // KeepGunOutsideCamera(); // off: the hold editor decides how close the gun sits to the eye
        }
    }

    // The gun meshes are open at the back. If a kick, a sway or the aim pose slides one closer than the near
    // plane, the camera looks into the empty shell. Push the whole hold forward until the closest gun point
    // is just outside that plane. Arms are not part of the measure, so a sleeve does not shove the sight.
    Renderer[] gunShells;
    void CacheGunShell(Transform selected)
    {
        Transform holder = null;
        foreach (var t in selected.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("OperatorWeapon_")) { holder = t; break; }
        var all = (holder != null ? holder : selected).GetComponentsInChildren<Renderer>(true);
        int n = 0;
        var keep = new Renderer[all.Length];
        foreach (var r in all)
        {
            if (r is ParticleSystemRenderer || r is LineRenderer) continue;
            if (holder == null)
            {
                string name = r.name.ToLowerInvariant();
                if (name.Contains("arm") || name.Contains("hand") || name.Contains("sleeve") || name.Contains("glove")) continue;
            }
            keep[n++] = r;
        }
        gunShells = new Renderer[n];
        System.Array.Copy(keep, gunShells, n);
    }

    void KeepGunOutsideCamera()
    {
        if ((gunShells == null || gunShells.Length == 0) && weaponRoot != null) CacheGunShell(weaponRoot);
        if (gunShells == null || gunShells.Length == 0 || weaponRoot.parent == null) return;
        float safe = (viewCamera != null ? viewCamera.nearClipPlane : .025f) + .02f;
        var parent = weaponRoot.parent;
        float minZ = float.PositiveInfinity;
        // The gun's own box corners in the camera's space: a world-aligned box (Renderer.bounds) grows and shrinks
        // as the view turns, which pushed the gun and the arms back and forth when looking left and right.
        var toParent = parent.worldToLocalMatrix;
        foreach (var r in gunShells)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            Bounds local;
            if (r is SkinnedMeshRenderer sk) local = sk.localBounds;
            else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; local = mf.sharedMesh.bounds; }
            var m = toParent * r.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float z = m.MultiplyPoint3x4(corner).z;
                if (z < minZ) minZ = z;
            }
        }
        if (minZ < safe) weaponRoot.localPosition += new Vector3(0f, 0f, safe - minZ);
    }

    void UpdateWeaponActions(bool abilityBusy, bool handBusy)
    {
        SetWeaponState(ResolveWeaponState(abilityBusy, handBusy));
        if (WeaponDeposited) return;
        if (weaponState == ElderboomWeaponState.Ready && !abilityBusy && !aiming && Down("inspect_weapon"))
            portViewmodel?.PlayDonorInspect();
        if (Down("reload") && weaponState != ElderboomWeaponState.Reloading && !abilityBusy && ammo < magazineSize && reserveAmmo > 0)
            StartCoroutine(Reload());
        if (Input.GetKeyDown(KeyCode.Alpha1)) ElderboomGame.Active?.SelectAbility(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) ElderboomGame.Active?.SelectAbility(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) ElderboomGame.Active?.SelectAbility(2);
        if (Down("shockwave") && !reloading)
        {
            if (ElderboomGame.Active != null) ElderboomGame.Active.UseSelectedAbility(this);
            else PlayOneShot(abilityClipName);
        }
        bool trigger=weaponDisplayName=="STRIKER-12" || weaponDisplayName=="BENELLI M4" || audioCategory==ElderboomIds.Category.Handgun || audioCategory==ElderboomIds.Category.Melee ? Down("fire"):Held("fire");
        // Sprint-to-fire: the weapon must come back up from the sprint pose before it fires.
        SetWeaponState(ResolveWeaponState(abilityBusy, handBusy));
        if (trigger && weaponState == ElderboomWeaponState.Ready && Time.time >= nextShotTime)
            Fire();
    }

    void Fire()
    {
        nextShotTime = Time.time + fireInterval;
        if (ammo > 0) ElderboomGamepad.Rumble(.25f, .45f, .07f);
        if (ammo <= 0 && audioCategory!=ElderboomIds.Category.Melee)
        {
            ElderboomAudio.DryFire(audioCategory, transform.position);
            if (reserveAmmo > 0 && !(ElderboomGame.Active != null && ElderboomGame.Active.AbilityBusy)) StartCoroutine(Reload()); // the left hand is busy with the ability
            return;
        }
        if(audioCategory!=ElderboomIds.Category.Melee)ammo--;
        var director=ElderboomGame.Active;
        if(director!=null){director.ReportShot();director.ReportNoise(transform.position,audioCategory==ElderboomIds.Category.Melee?6f:audioCategory==ElderboomIds.Category.Handgun?22f:30f);}
        // The flamethrower has its own continuous sound. Melee has the impact, not a rifle shot.
        bool melee = audioCategory == ElderboomIds.Category.Melee || (definition != null && definition.FireMode == ElderboomFireMode.Melee);
        bool flame = definition != null && definition.FireMode == ElderboomFireMode.Flame;
        if (!melee && !flame)
        {
            if (ElderboomWeaponMods.Suppressed(definition)) ElderboomAudio.FireSuppressed(audioCategory, transform.position, definition != null && definition.upgraded);
            else ElderboomAudio.FireWeapon(audioCategory, transform.position, definition != null && definition.upgraded);
        }
        if (muzzleFlash != null) StartCoroutine(Flash());
        float modRecoil=ElderboomWeaponMods.RecoilScale(definition);
        if (portViewmodel == null || !portViewmodel.PlayDonorFire(fireInterval)) recoil.Kick((weaponDisplayName == "RHINO 60DS" ? 1f : 0.65f)*modRecoil);
        var profile=definition ?? new ElderboomWeaponDefinition { category=audioCategory,damage=damage,range=78,falloffStart=25,minimumDamage=.52f,spread=.85f,pellets=1,pierce=1,fireInterval=fireInterval };
        // View recoil: a snappy kick that springs back, part of it staying in the aim
        // (the player pulls down to compensate), plus a little trauma shake.
        ElderboomFeel.Recoil(profile,out float kickUp,out float kickSide,out float kickTrauma,out float climb);
        float adsScale=(aiming?.6f:1f)*modRecoil;
        viewKick.Kick(kickUp*adsScale,Random.Range(-kickSide,kickSide)*adsScale);
        pitch=Mathf.Clamp(pitch-kickUp*adsScale*climb,-82f,82f);
        viewKick.AddTrauma(kickTrauma*(aiming?.7f:1f));
        ElderboomWeaponFire.Fire(this,profile,viewCamera,rifleMuzzle,aiming,spreadBloom);
        spreadBloom=Mathf.Min(2.5f,spreadBloom+.18f*ElderboomWeaponMods.BloomScale(definition));
    }

    IEnumerator Flash()
    {
        muzzleFlash.enabled = true;
        yield return new WaitForSeconds(0.045f);
        if (muzzleFlash != null) muzzleFlash.enabled = false;
    }

    IEnumerator Reload()
    {
        reloading = true;
        aiming = false;
        var reloadingRoot = weaponRoot;
        var clip = PlayOneShot(reloadClipName);
        float duration = portViewmodel!=null ? portViewmodel.PlayReload(ammo <= 0, definition?.id == "shotgun", Mathf.Min(magazineSize - ammo, reserveAmmo)) : clip != null ? Mathf.Clamp(clip.length, 1.2f, 3.2f) : 1.85f;
        StartCoroutine(ReloadSounds(duration));
        yield return new WaitForSeconds(duration);
        if (dead || weaponRoot != reloadingRoot) { reloading = false; yield break; }
        int load = Mathf.Min(magazineSize - ammo, reserveAmmo);
        ammo += load;
        reserveAmmo -= load;
        reloading = false;
        PlayIdle();
    }

    IEnumerator ReloadSounds(float duration)
    {
        var cues = ReloadCues();
        float last = 0f;
        for (int i = 0; i < cues.Length; i++)
        {
            yield return new WaitForSeconds((cues[i].fraction - last) * duration);
            last = cues[i].fraction;
            if (!reloading) yield break;
            ElderboomAudio.ReloadCue(audioCategory, cues[i].stage, transform.position);
        }
    }

    (float fraction, string stage)[] ReloadCues()
    {
        if (weaponDisplayName == "HK53")
            return new[] { (.18f, "bolt"), (.38f, "mag_out"), (.52f, "mag_draw"), (.67f, "mag_in"), (.80f, "bolt") };
        if (audioCategory == ElderboomIds.Category.Shotgun)
            return new[] { (0.20f, "open"), (0.39f, "shell_draw"), (0.61f, "shell_in"), (0.81f, "close") };
        if (weaponDisplayName != null && weaponDisplayName.Contains("SIG"))
            return new[] { (0.294f, "mag_out"), (0.571f, "mag_draw"), (0.782f, "mag_in") };
        if (weaponDisplayName != null && weaponDisplayName.Contains("RHINO"))
            return new[] { (0.145f, "open"), (0.40f, "mag_draw"), (0.76f, "mag_in"), (0.90f, "bolt") };
        if (weaponDisplayName != null && weaponDisplayName.Contains("1911"))
        {
            if (ammo <= 0)
                return new[] { (0.06f, "open"), (0.20f, "mag_out"), (0.39f, "mag_draw"), (0.62f, "mag_in"), (0.81f, "bolt") };
            return new[] { (0.20f, "mag_out"), (0.39f, "mag_draw"), (0.62f, "mag_in"), (0.81f, "bolt") };
        }
        return new[] { (0.16f, "mag_out"), (0.40f, "mag_draw"), (0.55f, "mag_in"), (0.78f, "bolt") };
    }

    AnimationClip PlayOneShot(string clipName)
    {
        if (weaponAnimator != null && weaponAnimator.runtimeAnimatorController != null)
        {
            var clips = weaponAnimator.runtimeAnimatorController.animationClips;
            var clip = FindClip(clips, clipName);
            if (clip != null) weaponAnimator.CrossFade(clip.name, 0.08f);
            return clip;
        }
        if (weaponLegacyAnimation != null)
        {
            foreach (AnimationState state in weaponLegacyAnimation)
                if (NameMatches(state.name, clipName))
                {
                    weaponLegacyAnimation.CrossFade(state.name, 0.08f);
                    return state.clip;
                }
        }
        return null;
    }

    void PlayIdle()
    {
        if (weaponAnimator != null && weaponAnimator.runtimeAnimatorController != null)
        {
            var clip = FindClip(weaponAnimator.runtimeAnimatorController.animationClips, idleClipName);
            if (clip != null) weaponAnimator.CrossFade(clip.name, 0.14f);
        }
        else if (weaponLegacyAnimation != null)
        {
            foreach (AnimationState state in weaponLegacyAnimation)
                if (NameMatches(state.name, idleClipName)) { weaponLegacyAnimation.CrossFade(state.name, 0.14f); break; }
        }
    }

    static AnimationClip FindClip(AnimationClip[] clips, string requested)
    {
        foreach (var clip in clips) if (clip != null && NameMatches(clip.name, requested)) return clip;
        return null;
    }

    static bool NameMatches(string actual, string requested)
        => string.Equals(actual, requested, System.StringComparison.OrdinalIgnoreCase)
           || (actual != null && requested != null && actual.Replace(" ", "_").IndexOf(requested.Replace(" ", "_"), System.StringComparison.OrdinalIgnoreCase) >= 0);

    public void ConfigurePortedLoadout(string operatorName, string weaponName, int capacity, int carried, float baseDamage, float interval)
    {
        operatorDisplayName = operatorName;
        weaponDisplayName = weaponName;
        magazineSize = Mathf.Max(1, capacity);
        ammo = Mathf.Clamp(ammo > 0 ? ammo : magazineSize, 0, magazineSize);
        reserveAmmo = Mathf.Max(0, carried);
        damage = Mathf.Max(1f, baseDamage);
        fireInterval = Mathf.Max(0.035f, interval);
        audioCategory=weaponName=="STRIKER-12"?"shotgun":"assault_rifle";
    }

    public void AddFovKick(float degrees) { fovKick = degrees; }

    // seconds: how long the operator's ability viewmodel stays up (from its definition).
    public void TriggerAbilityAnimation(float seconds = -1f)
    {
        if (reloading) return;
        if (seconds < 0f) seconds = operatorDisplayName == ElderboomIds.Soph ? 6.87f : 3.1f;
        var arsenal=GetComponent<ElderboomArsenal>();
        if(arsenal!=null && arsenal.ShowOperatorAbility(seconds))return; // left arm only, weapon stays in the right hand
        if(portViewmodel!=null)portViewmodel.PlayAbility();else PlayOneShot(abilityClipName);
    }

    public void ConfigurePortedViewmodels(Transform soph, Transform eli, Transform tactical)
    {
        sophViewmodel = soph;
        eliViewmodel = eli;
        tacticalViewmodel = tactical;
    }

    public void SelectPortedViewmodel(string operatorId)
    {
        Transform selected = operatorId == ElderboomIds.Eli ? eliViewmodel : operatorId == ElderboomIds.Tactical ? tacticalViewmodel : sophViewmodel;
        if (selected == null) return;
        foreach (var candidate in new[] { sophViewmodel, eliViewmodel, tacticalViewmodel })
            if (candidate != null) candidate.gameObject.SetActive(candidate == selected);
        BindWeaponRoot(selected);
    }

    public void ConfigureWeapon(ElderboomWeaponDefinition data,int loaded,int carried)
    {
        definition=data;adsKey=portViewmodel?.basic != null ? null : ElderboomAuthoredGrip.AdjustKey(operatorDisplayName,data.id,portViewmodel?.donor != null);ConfigurePortedLoadout(operatorDisplayName,data.name,data.capacity,carried,data.damage,data.fireInterval);
        ammo=loaded;reserveAmmo=carried;audioCategory=data.category;
        if(weaponRoot!=null)ApplyWeaponPose(weaponRoot); // aim distance depends on the category (handgun or long gun)
    }

    public void BindWeaponRoot(Transform selected)
    {
        weaponRoot = selected;
        poseReady = false;
        CacheGunShell(selected);
        if (selected.GetComponent<ElderboomBasicArms>() == null) ElderboomShoulderTrim.Ensure(selected, operatorDisplayName);
        ScaleStriker(selected);
        BeginEquip();
        recoil.Reset();spreadBloom=0;
        portViewmodel = selected.GetComponent<ElderboomViewmodel>();
        if (portViewmodel != null && (portViewmodel.donor != null || portViewmodel.basic != null)) adsKey = null;

        weaponAnimator = portViewmodel == null ? selected.GetComponentInChildren<Animator>(true) : null;
        weaponLegacyAnimation = portViewmodel == null ? selected.GetComponentInChildren<Animation>(true) : null;
        muzzleFlash = ElderboomWeaponMods.ShotLight(selected); // never an attachment's torch or laser
        if(muzzleFlash != null) muzzleFlash.enabled = false;
        adsRearSight = FindNamed(selected, "ads", "rear");
        adsFrontSight = FindNamed(selected, "ads", "front");
        rifleMuzzle = ElderboomWeaponFraming.FindNamed(selected,"Soph_Rifle_Muzzle","HK53_Muzzle","MainWeapon_Muzzle") ?? FindNamed(selected,"muzzle");
        ApplyWeaponPose(selected);
        selected.localPosition = hipPosition;
        selected.localRotation = hipRotation;
        portViewmodel?.PlayDonorDraw();
    }

    // STRIKER-12 a little smaller (10%), in the Tactical's own viewmodel and in every other hold of it.
    const float StrikerScale = .9f;
    void ScaleStriker(Transform selected)
    {
        if (selected == null) return;
        Transform holder = null;
        foreach (var t in selected.GetComponentsInChildren<Transform>(true)) if (t.name == "OperatorWeapon_striker") { holder = t; break; }
        if (holder != null) { if (holder.localScale.x > StrikerScale + .001f || holder.localScale.x < StrikerScale - .001f) holder.localScale = Vector3.one * StrikerScale; return; }
        bool tacticalOwn = selected.name.StartsWith("Tactical_Viewmodel_Pivot") && (selected.name == "Tactical_Viewmodel_Pivot" || selected.name.EndsWith("_striker"));
        if (!tacticalOwn) return;
        var mesh = ElderboomWeaponFraming.FindNamed(selected, "output_unwrapped.003");
        var hand = ElderboomWeaponFraming.FindNamed(selected, "Hand.R");
        if (mesh == null || hand == null) return;
        ElderboomScaleAbout.Apply(selected, mesh, hand, StrikerScale);
        foreach (var t in selected.GetComponentsInChildren<Transform>(true))
            if (t.name.StartsWith("MainWeapon_") && !t.IsChildOf(mesh) && (t.parent == null || !t.parent.name.StartsWith("MainWeapon_")))
                ElderboomScaleAbout.Apply(selected, t, hand, StrikerScale);
    }

    void ApplyWeaponPose(Transform selected)
    {
        ApplyWeaponPoseBase(selected);
        TightenAds(selected);
        adsBasePosition = adsPosition; adsBaseRotation = adsRotation;
        ApplyAdsOffset();
    }

    // Hand-tuned camera for this operator and weapon (ElderboomGripEditor, CAMERA): the eye moves and turns (view
    // space), arms and weapon stay where they are. Faded out while aiming, so the sights stay centred.
    float camAim;
    void CameraTweak(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero; rot = Quaternion.identity;
        camAim = Mathf.Lerp(camAim, aiming ? 0f : 1f, 1f - Mathf.Exp(-12f * Time.deltaTime));
        if (string.IsNullOrEmpty(adsKey) || camAim < .001f) return;
        var o = ElderboomGripAdjust.Load(adsKey);
        if (o.camPos == Vector3.zero && o.camEuler == Vector3.zero) return;
        pos = o.camPos * camAim;
        rot = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(o.camEuler), camAim);
    }

    // Hand-tuned aim correction for this operator and weapon (ElderboomGripAdjust, edited in the test range).
    Vector3 adsBasePosition; Quaternion adsBaseRotation = Quaternion.identity;
    string adsKey;
    public string AdsKey => adsKey;
    public void ApplyAdsOffset()
    {
        if (string.IsNullOrEmpty(adsKey)) return;
        var o = ElderboomGripAdjust.Load(adsKey);
        adsPosition = adsBasePosition + o.adsPos;
        adsRotation = Quaternion.Euler(o.adsEuler) * adsBaseRotation;
    }

    // Hip pose, framed like a modern military shooter's idle: the gun big in the lower right, its back end very
    // close to the eye, the body of the weapon centred low-right on screen. Measured on the weapon's own meshes
    // (sight markers are not reliable on every pack); keeps the authored hold's orientation, arms come along.
    const float HipViewportX = .64f, HipViewportY = .27f;
    void FrameHip(Transform selected)
    {
        if (selected == null || selected.parent == null || audioCategory == ElderboomIds.Category.Melee) return;
        Vector3 keepP = selected.localPosition; Quaternion keepR = selected.localRotation;
        selected.localPosition = hipPosition; selected.localRotation = hipRotation;
        var parent = selected.parent; bool any = false; Bounds b = default;
        Transform holder = null;
        foreach (var tr in selected.GetComponentsInChildren<Transform>(true)) if (tr.name.StartsWith("OperatorWeapon_")) { holder = tr; break; }
        foreach (var r in (holder != null ? holder : selected).GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r.forceRenderingOff || r is ParticleSystemRenderer || r is LineRenderer || r is SkinnedMeshRenderer && holder == null) continue;
            string n = r.name.ToLowerInvariant();
            if (n.Contains("arm") || n.Contains("hand") || n.Contains("sleeve") || n.Contains("glove") || n.StartsWith("p_") || n.Contains("body")) continue;
            var c = parent.InverseTransformPoint(r.bounds.center);
            var e = parent.InverseTransformVector(r.bounds.extents); e = new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z));
            var lb = new Bounds(c, e * 2f);
            if (!any) { b = lb; any = true; } else b.Encapsulate(lb);
        }
        selected.localPosition = keepP; selected.localRotation = keepR;
        if (!any || b.size.z < .05f) return;
        float near = (audioCategory == ElderboomIds.Category.Handgun ? .16f : .05f) / ViewmodelDepth; // back of the gun, as seen
        float shiftZ = near - b.min.z;
        float centreZ = (b.center.z + shiftZ) * ViewmodelDepth;
        float t = Mathf.Tan(ViewmodelReferenceFov * .5f * Mathf.Deg2Rad), aspect = viewCamera != null ? viewCamera.aspect : 16f / 9f;
        var want = new Vector3((HipViewportX - .5f) * 2f * t * aspect * centreZ, (HipViewportY - .5f) * 2f * t * centreZ, b.center.z + shiftZ);
        hipPosition += want - b.center;
    }

    // Aiming puts the eye right behind the rear sight (rifles ~8 cm, handguns ~19 cm): less stock and arm in view.
    // Works on every pose source (authored grips, packs' stored framing, solved sights) by sliding the aim pose along
    // the view axis until the rear sight sits at that distance.
    void TightenAds(Transform selected)
    {
        if (!hasAdsReference || selected == null || selected.parent == null) return;
        if (portViewmodel != null && portViewmodel.keepAdsRelief) return;
        var rear = adsRearSight != null ? adsRearSight : ElderboomWeaponFraming.FindNamed(selected, "Soph_ADS_Rear", "MainWeapon_ADS_Rear");
        if (rear == null || !rear.IsChildOf(selected)) return;
        Vector3 keepP = selected.localPosition; Quaternion keepR = selected.localRotation;
        selected.localPosition = adsPosition; selected.localRotation = adsRotation;
        Vector3 rearInParent = selected.parent.InverseTransformPoint(rear.position);
        selected.localPosition = keepP; selected.localRotation = keepR;
        float want = audioCategory == ElderboomIds.Category.Handgun ? .19f : .12f;
        if (portViewmodel != null && portViewmodel.adsRelief >= ElderboomAuthoredGrip.FlatRigRelief - .001f) want = portViewmodel.adsRelief;
        if (rearInParent.z > want) adsPosition.z -= rearInParent.z - want;
    }

    void ApplyWeaponPoseBase(Transform selected)
    {
        var pose = ElderboomWeaponFraming.FromViewmodel(portViewmodel, selected);
        hipPosition = pose.hipPosition;
        hipRotation = pose.hipRotation;
        adsPosition = pose.adsPosition;
        adsRotation = pose.adsRotation;
        hasAdsReference = pose.hasAds;
        if (hasAdsReference) return;
        if (adsRearSight == null)
            adsRearSight = ElderboomWeaponFraming.FindNamed(selected, "Soph_ADS_Rear", "MainWeapon_ADS_Rear");
        if (adsFrontSight == null)
            adsFrontSight = ElderboomWeaponFraming.FindNamed(selected, "Soph_ADS_Front", "MainWeapon_ADS_Front");
        float relief = portViewmodel != null && portViewmodel.adsRelief > 0.01f ? portViewmodel.adsRelief : 0.18f;
        hasAdsReference = ElderboomWeaponFraming.SolveSightPose(selected, adsRearSight, adsFrontSight, new Vector3(0f, 0f, relief), out adsPosition, out adsRotation);
        if (!hasAdsReference)
        {
            adsPosition = hipPosition;
            adsRotation = hipRotation;
        }
    }

    static Transform FindNamed(Transform root, params string[] terms)
    {
        foreach (var item in root.GetComponentsInChildren<Transform>(true))
        {
            string name = item.name.ToLowerInvariant();
            bool matches = true;
            foreach (string term in terms) if (!name.Contains(term)) { matches = false; break; }
            if (matches) return item;
        }
        return null;
    }

    // Restores the catalog reserve; never lowers it. Returns false when nothing changed.
    public bool RefillAmmo()
    {
        if(audioCategory==ElderboomIds.Category.Melee)return false;
        int full=definition!=null?definition.reserve:magazineSize*4;
        if(reserveAmmo>=full)return false;
        reserveAmmo=full;return true;
    }

    public void SetReserveAmmo(int value){reserveAmmo=Mathf.Max(0,value);}
}
