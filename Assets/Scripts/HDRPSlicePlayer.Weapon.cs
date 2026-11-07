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
            float blend = 1f - Mathf.Exp(-14f * Time.deltaTime);
            posePosition = Vector3.Lerp(posePosition, aiming ? adsPosition : hipPosition, blend);
            poseRotation = Quaternion.Slerp(poseRotation, aiming ? adsRotation : hipRotation, blend);
            float sprintTarget = sprinting && !reloading ? 1f : 0f;
            sprintPose = Mathf.Lerp(sprintPose, sprintTarget, 1f - Mathf.Exp(-9f * Time.deltaTime));
            recoil.Advance(Time.deltaTime);
            UpdateViewmodelExtras(Time.deltaTime);
            float gait = bobWeight * (aiming ? 0.28f : 1f);
            float pump = Mathf.Lerp(0.45f, 1f, viewSprint);
            float step = Mathf.Abs(Mathf.Sin(headBobTime));
            float sway = Mathf.Sin(headBobTime * 0.5f);
            Vector3 weaponBob = new Vector3(sway * 0.01f, step * 0.014f, Mathf.Sin(headBobTime) * 0.005f) * pump * gait;
            Quaternion weaponBobRotation = Quaternion.Euler(Mathf.Sin(headBobTime) * 0.55f * pump * gait, sway * -0.35f * pump * gait, sway * 0.9f * pump * gait);
            weaponRoot.localPosition = posePosition + recoil.position + SprintDrop * sprintPose + weaponBob + ViewmodelOffset();
            weaponRoot.localRotation = ViewmodelRotation() * Quaternion.Slerp(Quaternion.identity, SprintTilt, sprintPose) * weaponBobRotation * Quaternion.Euler(recoil.rotation * Mathf.Rad2Deg) * poseRotation;
        }
    }

    void UpdateWeaponActions(bool abilityBusy, bool handBusy)
    {
        SetWeaponState(ResolveWeaponState(abilityBusy, handBusy));
        if (WeaponDeposited) return;
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
        bool trigger=weaponDisplayName=="STRIKER-12" || audioCategory==ElderboomIds.Category.Handgun || audioCategory==ElderboomIds.Category.Melee ? Down("fire"):Held("fire");
        // Sprint-to-fire: the weapon must come back up from the sprint pose before it fires.
        SetWeaponState(ResolveWeaponState(abilityBusy, handBusy));
        if (trigger && weaponState == ElderboomWeaponState.Ready && Time.time >= nextShotTime)
            Fire();
    }

    void Fire()
    {
        nextShotTime = Time.time + fireInterval;
        if (ammo <= 0 && audioCategory!=ElderboomIds.Category.Melee)
        {
            ElderboomAudio.DryFire(audioCategory, transform.position);
            if (reserveAmmo > 0 && !(ElderboomGame.Active != null && ElderboomGame.Active.AbilityBusy)) StartCoroutine(Reload()); // the left hand is busy with the ability
            return;
        }
        if(audioCategory!=ElderboomIds.Category.Melee)ammo--;
        var director=ElderboomGame.Active;
        if(director!=null){director.ReportShot();director.ReportNoise(transform.position,audioCategory==ElderboomIds.Category.Melee?6f:audioCategory==ElderboomIds.Category.Handgun?22f:30f);}
        ElderboomAudio.FireWeapon(audioCategory, transform.position);
        if (muzzleFlash != null) StartCoroutine(Flash());
        recoil.Kick(weaponDisplayName == "RHINO 60DS" ? 1f : 0.65f);
        var profile=definition ?? new ElderboomWeaponDefinition { category=audioCategory,damage=damage,range=78,falloffStart=25,minimumDamage=.52f,spread=.85f,pellets=1,pierce=1,fireInterval=fireInterval };
        // View recoil: a snappy kick that springs back, part of it staying in the aim
        // (the player pulls down to compensate), plus a little trauma shake.
        ElderboomFeel.Recoil(profile,out float kickUp,out float kickSide,out float kickTrauma,out float climb);
        float adsScale=aiming?.6f:1f;
        viewKick.Kick(kickUp*adsScale,Random.Range(-kickSide,kickSide)*adsScale);
        pitch=Mathf.Clamp(pitch-kickUp*adsScale*climb,-82f,82f);
        viewKick.AddTrauma(kickTrauma*(aiming?.7f:1f));
        ElderboomWeaponFire.Fire(this,profile,viewCamera,rifleMuzzle,aiming,spreadBloom);
        spreadBloom=Mathf.Min(2.5f,spreadBloom+.18f);
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
        float duration = portViewmodel!=null ? portViewmodel.PlayReload() : clip != null ? Mathf.Clamp(clip.length, 1.2f, 3.2f) : 1.85f;
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
        definition=data;ConfigurePortedLoadout(operatorDisplayName,data.name,data.capacity,carried,data.damage,data.fireInterval);
        ammo=loaded;reserveAmmo=carried;audioCategory=data.category;
    }

    public void BindWeaponRoot(Transform selected)
    {
        weaponRoot = selected;
        poseReady = false;
        BeginEquip();
        recoil.Reset();spreadBloom=0;
        portViewmodel = selected.GetComponent<ElderboomViewmodel>();
        weaponAnimator = portViewmodel == null ? selected.GetComponentInChildren<Animator>(true) : null;
        weaponLegacyAnimation = portViewmodel == null ? selected.GetComponentInChildren<Animation>(true) : null;
        muzzleFlash = selected.GetComponentInChildren<Light>(true);
        if(muzzleFlash != null) muzzleFlash.enabled = false;
        adsRearSight = FindNamed(selected, "ads", "rear");
        adsFrontSight = FindNamed(selected, "ads", "front");
        rifleMuzzle = ElderboomWeaponFraming.FindNamed(selected,"Soph_Rifle_Muzzle","HK53_Muzzle","MainWeapon_Muzzle") ?? FindNamed(selected,"muzzle");
        ApplyWeaponPose(selected);
        selected.localPosition = hipPosition;
        selected.localRotation = hipRotation;
    }

    void ApplyWeaponPose(Transform selected)
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
