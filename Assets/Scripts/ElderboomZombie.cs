using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public sealed class ElderboomZombie : MonoBehaviour
{
    ElderboomGame game;
    HDRPSlicePlayer player;
    NavMeshAgent agent;
    Animator animator;
    Transform hips;
    float liftGround;
    bool liftUpdatePosition;
    public float RetiredAt { get; private set; } = -1f;
    public bool IsFrozen => Time.time < frozenUntil;
    public void RetireFromWave(){CountsForWave=false;RetiredAt=Time.time;}
    float health, damage, moveSpeed, attackInterval, nextAttack;
    float frozenUntil, pushUntil, repathAt, voiceAt, ignoreUntil, hitSoundAt, reactionUntil, keepUntil, keepRadius, bloodAt;
    Vector3 pushVelocity, keepCenter;
    bool dead, melee, lifted;
    int attackToken;
    bool hitReacting;
    float shotReactionUntil, shotReactionDuration=.65f, animationWakeUntil;
    const float ShotReactionSpeed=1.3f;
    AnimatorCullingMode normalCulling;
    static readonly int ShotHitState=Animator.StringToHash("ShotHit");
    public bool IsReactingToHit => hitReacting;
    int ambientVariant=-1, attackVariant=-1, spawnVariant=-1;
    public bool Registered { get; set; }
    // Variants (phase 4): walker (default), runner (fast, fragile), brute (slow, tough, resists pushes).
    public string Variant { get; private set; } = "walker";
    float pushResist = 1f, offMeshTime, progressAt, investigateUntil;
    Vector3 progressPos, investigatePoint;
    int slot, stuckStrikes;
    static int nextSlot;
    public void SetVariant(string kind)
    {
        Variant = string.IsNullOrEmpty(kind) ? "walker" : kind;
        if (Variant == "brute") { transform.localScale *= 1.18f; pushResist = .45f; }
        else if (Variant == "runner") { transform.localScale *= .96f; pushResist = 1.15f; }
    }
    // Gunshots and explosions draw blinded or wandering zombies toward the noise.
    public void HearNoise(Vector3 position) => HearNoise(position, 7f);
    public void HearNoise(Vector3 position, float seconds)
    {
        if (dead) return;
        if (NetProxy) { if (Time.time >= noiseSentAt) { noiseSentAt = Time.time + 1f; Forward(1, position.x, position.y, position.z, seconds); } return; }
        investigatePoint = position + new Vector3(Random.Range(-2f, 2f), 0f, Random.Range(-2f, 2f));
        investigateUntil = Time.time + seconds;
    }
    public bool CountsForWave { get; set; } = true;
    // LAN (ElderboomLan): on clients zombies are proxies moved by the host's snapshots; on the host they
    // chase the nearest player, local or remote.
    public bool NetProxy { get; set; }
    public ushort NetId { get; set; }
    public bool NetFemale { get; set; }
    public int NetModel { get; set; } // ElderboomZombieModels id, 0 = legacy prefab
    ElderboomLanAvatar chaseAvatar;
    float retargetAt;
    public void NetDie()
    {
        if (dead) return;
        dead = true; CancelMelee(); ClearShotReaction();
        foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
        SetMotion("DeathA");
        int death = -1; ElderboomAudio.Zombie("death", ref death, transform.position);
        Destroy(gameObject, 3f);
    }
    void Retarget()
    {
        if (Time.time < retargetAt && (chaseAvatar == null || !chaseAvatar.IsDead)) return;
        retargetAt = Time.time + .5f;
        chaseAvatar = null;
        if (ElderboomLanAvatar.All.Count == 0) return;
        float best = player != null && !player.IsDead && !(game != null && game.PlayerIgnored) ? (player.transform.position - transform.position).sqrMagnitude : float.MaxValue;
        foreach (var a in ElderboomLanAvatar.All)
        {
            if (a == null || a.IsDead || a.Ignored) continue;
            float d = (a.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; chaseAvatar = a; }
        }
    }
    // LAN: ability effects on a client's proxy go to the host's zombie (ElderboomLan.ApplyEffect).
    float noiseSentAt, keepSentAt;
    bool Forward(byte kind, params float[] values)
    {
        if (!NetProxy) return false;
        if (!dead) ElderboomLan.SendEffect(this, kind, values);
        return true;
    }
    public void ApplyNetEffect(byte kind, float[] v)
    {
        switch (kind)
        {
            case 1: HearNoise(new Vector3(v[0], v[1], v[2]), v[3]); break;
            case 2: FreezeFor(v[0]); break;
            case 3: IgnorePlayerFor(v[0]); break;
            case 4: ApplyPlasmaBlind(v[0]); break;
            case 5: PushFrom(new Vector3(v[0], v[1], v[2]), v[3], v[4]); break;
            case 6: ApplyShockwave(new Vector3(v[0], v[1], v[2]), v[3], v[4], v[5]); break;
            case 7: EnforceKeepAway(new Vector3(v[0], v[1], v[2]), v[3]); break;
            case 8: BeginLift(v[0], v[1], v[2], v[3], v[4], v[5], new Vector3(v[6], v[7], v[8])); break;
        }
    }
    // Eli's Ignore Signal hides only the player who used it.
    bool TargetIgnored => chaseAvatar != null ? chaseAvatar.Ignored : (game != null && game.PlayerIgnored);
    bool TargetDown => chaseAvatar != null ? chaseAvatar.IsDead : player == null || player.IsDead;
    Vector3 TargetPos => chaseAvatar != null ? chaseAvatar.transform.position : player.transform.position;
    public bool PlasmaBlinded { get; private set; }
    public bool IsAlive => !dead;
    public float Health => health;

    public void Initialize(ElderboomGame owner,HDRPSlicePlayer target,float maxHealth,float hitDamage,float speed,float interval)
    {
        game=owner;player=target;health=maxHealth;damage=hitDamage;moveSpeed=speed;attackInterval=interval;
        agent=GetComponent<NavMeshAgent>(); if(agent==null)agent=gameObject.AddComponent<NavMeshAgent>();
        agent.radius=.38f;agent.height=1.8f;agent.speed=speed;agent.acceleration=10;agent.angularSpeed=420;agent.stoppingDistance=1.15f;
        agent.obstacleAvoidanceType=ObstacleAvoidanceType.MedQualityObstacleAvoidance;
        animator=GetComponentInChildren<Animator>(); if(animator!=null){animator.applyRootMotion=false;animator.speed=1f;normalCulling=animator.cullingMode==AnimatorCullingMode.AlwaysAnimate?AnimatorCullingMode.CullUpdateTransforms:animator.cullingMode;}
        if(NavMesh.SamplePosition(transform.position,out var hit,5f,NavMesh.AllAreas))agent.Warp(hit.position);
        if(animator!=null && animator.runtimeAnimatorController!=null)
            foreach(var clip in animator.runtimeAnimatorController.animationClips)
                if(clip.name=="ShotHit"){shotReactionDuration=Mathf.Max(.1f,clip.length/ShotReactionSpeed);break;}
        voiceAt=Time.time+Random.Range(4f,9f);SetMotion(Locomotion());
        slot=nextSlot++;progressPos=transform.position;progressAt=Time.time;
        if(player!=null && Vector3.Distance(transform.position,player.transform.position)<=6f)ElderboomAudio.Zombie("spawn", ref spawnVariant, transform.position);
    }

    // ---- State machine ---------------------------------------------------------------
    // The state is resolved every frame by priority (first match wins); each state has
    // its own tick. Ability effects (freeze, push, lift, keep-away) and hit reactions
    // only set timers or flags, so they override chasing without extra bookkeeping.
    ElderboomZombieState state = ElderboomZombieState.Idle;
    public ElderboomZombieState State => state;
    public event System.Action<ElderboomZombie, ElderboomZombieState, ElderboomZombieState> StateChanged;

    ElderboomZombieState ResolveState(bool chase, bool noisesIgnored)
    {
        if (dead) return ElderboomZombieState.Dead;
        if (lifted) return ElderboomZombieState.Lifted;
        if (Time.time < frozenUntil) return ElderboomZombieState.Stunned;
        if (hitReacting) return ElderboomZombieState.HitReact;
        if (Time.time < pushUntil || pushVelocity.sqrMagnitude > 0.04f) return ElderboomZombieState.Pushed;
        if (InsideKeepAway()) return ElderboomZombieState.KeepAway;
        if (melee) return ElderboomZombieState.Attack;
        if (chase) return ElderboomZombieState.Chase;
        if (Time.time < investigateUntil && !noisesIgnored) return ElderboomZombieState.Investigate;
        return ElderboomZombieState.Wander;
    }

    void SetState(ElderboomZombieState next)
    {
        if (next == state) return;
        var previous = state;
        state = next;
        // Picking up the player (or a noise) re-paths at once instead of waiting for the timer.
        if ((next == ElderboomZombieState.Chase || next == ElderboomZombieState.Investigate) && previous == ElderboomZombieState.Wander) repathAt = 0f;
        StateChanged?.Invoke(this, previous, next);
    }

    void Update()
    {
        if (dead) { SetState(ElderboomZombieState.Dead); return; }
        if (player == null || agent == null) { SetState(ElderboomZombieState.Idle); return; }
        Retarget();
        if (TargetDown) { SetState(ElderboomZombieState.Idle); return; }
        if (!lifted && !agent.isOnNavMesh) { SetState(ElderboomZombieState.OffMesh); TickOffMesh(); return; }
        offMeshTime = 0f;
        float distance = Vector3.Distance(transform.position, TargetPos);
        TickBody(distance);
        // Global ignore (Eli's Ignore Signal) also silences noises; a per-zombie ignore
        // (Acoustic Pulse decoy) only stops the chase, so the zombie still investigates.
        bool globallyIgnored = TargetIgnored;
        bool ignored = Time.time < ignoreUntil || globallyIgnored;
        bool chase = !ignored && (!PlasmaBlinded || distance < 2f);
        SetState(ResolveState(chase, globallyIgnored));
        switch (state)
        {
            case ElderboomZombieState.Lifted:
            case ElderboomZombieState.Stunned:
            case ElderboomZombieState.HitReact:
                return; // driven by their coroutines / timers
            case ElderboomZombieState.Pushed: TickPushed(); return;
            case ElderboomZombieState.KeepAway: TickKeepAway(); return;
        }
        if (chase && distance <= 1.45f && Time.time >= nextAttack && HasLineOfSight())
        {
            StartCoroutine(Strike());
            SetState(ElderboomZombieState.Attack);
            return;
        }
        TickNavigation(distance, chase, globallyIgnored);
    }

    // Watchdog: an agent that left the NavMesh is put back, or retired so the wave can end.
    void TickOffMesh()
    {
        offMeshTime += Time.deltaTime;
        if (offMeshTime > 3f) { offMeshTime = 0f; if (NavMesh.SamplePosition(transform.position, out var back, 6f, NavMesh.AllAreas)) agent.Warp(back.position); else game?.RetireStuck(this); }
    }

    // Shared every-frame work: hit-reaction expiry, push decay, voice, animation speed, agent stop.
    void TickBody(float distance)
    {
        if(hitReacting && Time.time>=shotReactionUntil && !IsFrozen && !lifted)
        {
            ClearShotReaction();
            if(Time.time>=reactionUntil)SetMotion(Locomotion());
        }
        bool stunned=Time.time<frozenUntil;
        bool reacting=Time.time<reactionUntil;
        bool pushing=Time.time<pushUntil || pushVelocity.sqrMagnitude>0.04f;
        if(pushVelocity.sqrMagnitude>0.04f)pushVelocity=Vector3.MoveTowards(pushVelocity,Vector3.zero,28f*Time.deltaTime);
        if(Time.time>=voiceAt && !melee && !stunned && !lifted && distance<=23f){ElderboomAudio.Zombie("ambient", ref ambientVariant, transform.position);voiceAt=Time.time+Random.Range(9f,16f);}
        if(animator!=null)
        {
            bool waking=Time.time<animationWakeUntil;
            if(!waking && !hitReacting)animator.cullingMode=normalCulling;
            float travel=agent.velocity.magnitude;
            bool moving=travel>0.18f && !melee && !stunned && !lifted && !hitReacting;
            animator.speed=stunned?0f:hitReacting?ShotReactionSpeed:(lifted||melee||reacting||waking||pushing||moving)?(moving&&!melee&&!lifted&&!reacting?Mathf.Clamp(travel/2.2f*1.5f,.12f,2.6f):1f):0f;
        }
        agent.isStopped=stunned || melee || lifted || hitReacting || InsideKeepAway() || Time.time<pushUntil;
    }

    void TickPushed()
    {
        agent.ResetPath();
        agent.velocity=Vector3.zero;
        // agent.Move stays on the NavMesh and stops at its edges, so pushes can no
        // longer carry a zombie through thin walls or a closed gate.
        agent.Move(pushVelocity*Time.deltaTime);
    }

    void TickKeepAway()
    {
        CancelMelee();
        Vector3 away=transform.position-keepCenter;away.y=0f;
        float fieldDistance=away.magnitude;
        if(fieldDistance<0.01f)away=Vector3.forward;else away/=fieldDistance;
        float spring=Mathf.Clamp((keepRadius+0.10f-fieldDistance)*2f,-0.5f,1f);
        agent.Move(away*Mathf.Max(moveSpeed,5f)*spring*Time.deltaTime);
    }

    // Chase, investigate and wander share the re-path timer; Attack keeps tracking
    // the player while the agent is stopped, as before.
    void TickNavigation(float distance, bool chase, bool noisesIgnored)
    {
        if(Time.time<repathAt)return;
        repathAt=Time.time+.4f+Random.Range(0f,.1f);
        if(chase){agent.speed=moveSpeed;agent.SetDestination(ChaseTarget(distance));CheckProgress(distance);}
        else if(Time.time<investigateUntil && !noisesIgnored){agent.speed=moveSpeed*.85f;agent.SetDestination(investigatePoint);}
        else if(!agent.hasPath || agent.remainingDistance<.6f)
        {
            agent.speed=moveSpeed*.65f;
            var point=transform.position+Random.insideUnitSphere*5f;
            if(NavMesh.SamplePosition(point,out var hit,3f,NavMesh.AllAreas))agent.SetDestination(hit.position);
        }
    }

    // Spread the horde around the player at mid range instead of a single conga line.
    Vector3 ChaseTarget(float distance)
    {
        Vector3 target=TargetPos;
        if(distance<3.5f || distance>18f)return target;
        float radius=Mathf.Lerp(1.2f,2.6f,(distance-3.5f)/14.5f);
        Vector3 offset=Quaternion.Euler(0f,slot*137.5f%360f,0f)*Vector3.forward*radius;
        return NavMesh.SamplePosition(target+offset,out var hit,1.5f,NavMesh.AllAreas)?hit.position:target;
    }
    // Stuck while chasing: re-place on the NavMesh, then retire after repeated failures.
    void CheckProgress(float distance)
    {
        if(Time.time-progressAt<8f)return;
        bool stuck=distance>3f && (transform.position-progressPos).sqrMagnitude<.25f && !melee && Time.time>=pushUntil;
        progressPos=transform.position;progressAt=Time.time;
        if(!stuck){stuckStrikes=0;return;}
        if(++stuckStrikes>=3){game?.RetireStuck(this);return;}
        agent.ResetPath();
        if(NavMesh.SamplePosition(transform.position+Random.insideUnitSphere*1.5f,out var hit,3f,NavMesh.AllAreas))agent.Warp(hit.position);
    }

    bool HasLineOfSight()
    {
        var from=transform.position+Vector3.up*1.3f;var to=TargetPos+Vector3.up*1.0f;
        if(!Physics.Linecast(from,to,out var hit,ElderboomCombat.WorldMask,QueryTriggerInteraction.Ignore))return true;
        if(chaseAvatar!=null)return hit.collider.GetComponentInParent<ElderboomLanAvatar>()==chaseAvatar;
        return hit.collider.GetComponentInParent<HDRPSlicePlayer>()==player;
    }

    IEnumerator Strike()
    {
        int token=attackToken;
        melee=true;nextAttack=Time.time+attackInterval;SetMotion(Random.value>.5f?"AttackPunch":"AttackA");
        ElderboomAudio.Zombie("attack", ref attackVariant, transform.position);
        voiceAt=Mathf.Max(voiceAt, Time.time+2.5f);
        yield return new WaitForSeconds(.42f);
        if(token==attackToken && !dead && Time.time>=frozenUntil && Time.time>=ignoreUntil && !TargetIgnored && !lifted && !InsideKeepAway() && player!=null && !TargetDown && Vector3.Distance(transform.position,TargetPos)<=1.55f && HasLineOfSight())
        {
            int contact=-1; ElderboomAudio.Zombie("contact", ref contact, transform.position);
            if(chaseAvatar!=null)ElderboomLan.DamageAvatar(chaseAvatar,damage,transform.position);
            else player.TakeDamage(damage,transform.position);
        }
        yield return new WaitForSeconds(Mathf.Max(.1f,attackInterval-.42f));
        if(token!=attackToken)yield break;
        melee=false;if(!dead && Time.time>=reactionUntil)SetMotion(Locomotion());
    }

    public void CancelAbilityEffects()
    {
        StopAllCoroutines();CancelMelee();ClearShotReaction();frozenUntil=ignoreUntil=keepUntil=pushUntil=reactionUntil=0;pushVelocity=Vector3.zero;
        if(lifted)
        {
            var p=transform.position;p.y=liftGround;transform.position=p;lifted=false;
            if(agent!=null && agent.enabled){agent.updatePosition=liftUpdatePosition;if(agent.isOnNavMesh)agent.Warp(p);}
        }
        if(animator!=null)animator.speed=1;
    }
    void CancelMelee(){attackToken++;melee=false;}
    bool InsideKeepAway()
    {
        if(Time.time>keepUntil)return false;
        Vector3 flat=transform.position-keepCenter;flat.y=0f;
        float limit=keepRadius+0.55f;
        return flat.sqrMagnitude<limit*limit;
    }

    void LateUpdate()
    {
        if(dead||melee||lifted||animator==null)return;
        var state=animator.GetCurrentAnimatorStateInfo(0);
        if(!state.IsName("WalkA")&&!state.IsName("WalkB"))return;
        if(hips==null)
        {
            hips=animator.isHuman?animator.GetBoneTransform(HumanBodyBones.Hips):null;
            if(hips==null)foreach(var bone in animator.GetComponentsInChildren<Transform>(true))
                if(bone.name=="Hips"||bone.name=="mixamorig:Hips"){hips=bone;break;}
        }
        if(hips==null)return;
        var pinned=hips.position;
        pinned.x=transform.position.x;
        pinned.z=transform.position.z;
        hips.position=pinned;
    }

    string Locomotion()
    {
        if(animator!=null && animator.HasState(0,Animator.StringToHash("WalkB")) && Random.value>.5f)return "WalkB";
        return "WalkA";
    }

    public bool TakeDamage(float amount,Vector3 origin)
    {
        var point = transform.position + Vector3.up * 1.1f;
        return TakeDamage(amount, origin, point, (origin - point).normalized);
    }

    public bool TakeDamage(float amount, Vector3 origin, Vector3 hitPoint, Vector3 hitNormal)
    {
        if(NetProxy)
        {
            // LAN client: show the hit here, the host applies the damage to the real zombie.
            if(dead || amount <= 0f)return false;
            if(Time.time>=bloodAt){ElderboomImportedVfx.Blood(hitPoint,hitNormal,false);bloodAt=Time.time+.065f;}
            if(Time.time>=hitSoundAt){int h=-1;ElderboomAudio.Zombie("hit", ref h, transform.position);hitSoundAt=Time.time+.48f;}
            ElderboomLan.SendHit(this,amount,hitPoint,hitNormal);
            return false;
        }
        if(dead || amount <= 0f)return false;
        if(game!=null && game.InstaKill)amount=Mathf.Max(amount,health+1f);
        health-=amount;
        if(health>0)
        {
            if(Time.time>=bloodAt){ElderboomImportedVfx.Blood(hitPoint,hitNormal,false);bloodAt=Time.time+.065f;}
            ReactToShot();
            if(Time.time>=hitSoundAt){int hit=-1;ElderboomAudio.Zombie("hit", ref hit, transform.position);hitSoundAt=Time.time+.48f;}
            return false;
        }
        dead=true;CancelMelee();ClearShotReaction();ElderboomImportedVfx.Blood(hitPoint,hitNormal,true);game?.RegisterKill(this);
        if(agent!=null && agent.isOnNavMesh)agent.isStopped=true;
        foreach(var collider in GetComponentsInChildren<Collider>())collider.enabled=false;
        SetMotion("DeathA");int death=-1;ElderboomAudio.Zombie("death", ref death, transform.position);
        Destroy(gameObject,3f);return true;
    }
    void ClearShotReaction(){hitReacting=false;shotReactionUntil=0f;}
    void ReactToShot()
    {
        // Keep ability reactions intact. Pellets/repeated hits must not keep
        // restarting ShotHit at frame zero while it is already playing.
        if(hitReacting || lifted || IsFrozen || Time.time<reactionUntil || Time.time<pushUntil)return;
        if(animator==null || animator.runtimeAnimatorController==null || !animator.HasState(0,ShotHitState))return;
        CancelMelee();
        hitReacting=true;shotReactionUntil=Time.time+shotReactionDuration;
        nextAttack=Mathf.Max(nextAttack,shotReactionUntil+.1f);
        if(agent!=null && agent.enabled && agent.isOnNavMesh)
        {agent.isStopped=true;agent.ResetPath();agent.velocity=Vector3.zero;}
        WakeAnimation(shotReactionDuration+.12f);animator.speed=ShotReactionSpeed;
        animator.CrossFadeInFixedTime(ShotHitState,.055f,0,0f);
    }
    void WakeAnimation(float seconds)
    {
        animationWakeUntil=Mathf.Max(animationWakeUntil,Time.time+seconds);
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
    }
    void SetMotion(string name)
    {
        if(animator!=null && animator.runtimeAnimatorController!=null && animator.HasState(0,Animator.StringToHash(name)))
        {WakeAnimation(.12f);animator.speed=1f;animator.CrossFadeInFixedTime(name,.1f); }
    }
    public void FreezeFor(float seconds){if(Forward(2,seconds))return;frozenUntil=Mathf.Max(frozenUntil,Time.time+seconds);}
    public void IgnorePlayerFor(float seconds){if(Forward(3,seconds))return;ignoreUntil=Mathf.Max(ignoreUntil,Time.time+seconds);}
    public void ApplyPlasmaBlind(float proximity){if(Forward(4,proximity))return;PlasmaBlinded=true;if(agent!=null && agent.isOnNavMesh)agent.ResetPath();}
    public void PushFrom(Vector3 origin,float force,float stun)
    {if(Forward(5,origin.x,origin.y,origin.z,force,stun))return;ClearShotReaction();pushVelocity=Vector3.ProjectOnPlane(transform.position-origin,Vector3.up).normalized*force;pushUntil=Time.time+stun;}
    public void ApplyShockwave(Vector3 origin,float force,float stun,float amount)
    {
        if(Forward(6,origin.x,origin.y,origin.z,force,stun,amount))return;
        if(dead||lifted)return;
        ClearShotReaction();CancelMelee();
        Vector3 away=transform.position-origin;away.y=0f;
        if(away.sqrMagnitude<0.001f)away=Vector3.forward;
        pushVelocity=away.normalized*force*pushResist;
        pushUntil=Mathf.Max(pushUntil,Time.time+Mathf.Max(stun,force/28f));
        if(agent!=null && agent.isOnNavMesh){agent.ResetPath();agent.velocity=Vector3.zero;agent.isStopped=true;}
        reactionUntil=Mathf.Max(reactionUntil,Time.time+1.2f);
        SetMotion("ShockwaveHit");
        if(amount>0f)TakeDamage(amount,origin);
    }
    public void EnforceKeepAway(Vector3 center,float radius)
    {
        if(NetProxy){if(Time.time>=keepSentAt){keepSentAt=Time.time+0.08f;Forward(7,center.x,center.y,center.z,radius);}return;}
        if(dead||lifted)return;
        keepCenter=center;keepRadius=Mathf.Max(0f,radius);keepUntil=Time.time+0.15f;
        if(InsideKeepAway())CancelMelee();
    }
    public void BeginLift(float height,float rise,float hang,float slam,float impactDamage,float recovery,Vector3 pushDirection)
    {
        if(Forward(8,height,rise,hang,slam,impactDamage,recovery,pushDirection.x,pushDirection.y,pushDirection.z))return;
        if(dead||lifted)return;
        ClearShotReaction();CancelMelee();
        StartCoroutine(LiftRoutine(height,rise,hang,slam,impactDamage,recovery,pushDirection));
    }
    IEnumerator LiftRoutine(float height,float rise,float hang,float slam,float impactDamage,float recovery,Vector3 pushDirection)
    {
        ClearShotReaction();lifted=true;
        if(Physics.Raycast(transform.position+Vector3.up*1.85f,Vector3.up,out var ceiling,height,ElderboomCombat.WorldMask,QueryTriggerInteraction.Ignore))
            height=Mathf.Clamp(ceiling.distance-0.15f,0f,height);
        float ground=liftGround=transform.position.y;
        bool updatePosition=liftUpdatePosition=agent!=null && agent.updatePosition;
        if(agent!=null){agent.updatePosition=false;agent.isStopped=true;}
        SetMotion("LiftFloat");
        float elapsed=0f,holdEnd=rise+hang,slamEnd=holdEnd+slam;
        bool slammed=false;
        while(elapsed<slamEnd && !dead)
        {
            elapsed+=Time.deltaTime;
            if(!slammed && elapsed>=holdEnd){slammed=true;SetMotion("LiftSlam");}
            float current=0f;
            if(elapsed<rise){float t=Mathf.Clamp01(elapsed/Mathf.Max(0.01f,rise));current=height*(1f-Mathf.Pow(1f-t,2f));}
            else if(elapsed<holdEnd)current=height;
            else {float t=Mathf.Clamp01((elapsed-holdEnd)/Mathf.Max(0.01f,slam));current=height*(1f-t*t);}
            var position=transform.position;position.y=ground+current;transform.position=position;
            yield return null;
        }
        var landed=transform.position;landed.y=ground;transform.position=landed;
        lifted=false;
        if(agent!=null)
        {
            agent.updatePosition=updatePosition;
            if(agent.isOnNavMesh)agent.Warp(transform.position);
        }
        pushDirection.y=0f;
        if(pushDirection.sqrMagnitude>0.001f)pushVelocity=pushDirection.normalized*2.2f;
        pushUntil=Time.time+recovery;
        yield return new WaitForSeconds(0.22f);
        if(!dead)TakeDamage(impactDamage,transform.position);
        if(!dead){reactionUntil=Time.time+1.8f;SetMotion("GetUp");}
    }
}

public enum ElderboomZombieState { Idle, Chase, Attack, Investigate, Wander, HitReact, Stunned, Lifted, Pushed, KeepAway, OffMesh, Dead }
