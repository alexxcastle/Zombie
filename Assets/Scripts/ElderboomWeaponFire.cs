using System.Collections.Generic;
using UnityEngine;

public enum ElderboomFireMode { Hitscan, Explosive, Flame, Pulse, Melee }

public static class ElderboomWeaponFire
{
    sealed class Impact { public RaycastHit hit; public float amount; }
    static readonly Dictionary<Component,Impact> Impacts = new Dictionary<Component,Impact>(); // reused per shot
    public static void Fire(HDRPSlicePlayer owner, ElderboomWeaponDefinition definition, Camera camera, Transform muzzle, bool aiming, float bloom)
    {
        if(camera == null || definition == null || owner.IsDead) return;
        Vector3 origin = muzzle != null ? muzzle.position : camera.transform.position;
        float range = Mathf.Max(.1f,definition.range);
        if(definition.FireMode == ElderboomFireMode.Explosive)
        {
            if(ElderboomCombat.MuzzleObstruction(camera.transform.position,origin,owner.transform,out var blocked))
                ElderboomGrenadeProjectile.Explode(blocked.point+blocked.normal*.025f,definition.damage,owner);
            else ElderboomGrenadeProjectile.Create(origin,(ElderboomCombat.AimPoint(camera,camera.transform.forward,range,owner.transform)-origin).normalized,definition.damage,owner);
            return;
        }
        if(definition.FireMode == ElderboomFireMode.Pulse)
        {
            if(ElderboomCombat.MuzzleObstruction(camera.transform.position,origin,owner.transform,out var blocked))
                origin = blocked.point+blocked.normal*.02f;
            else if(ElderboomCombat.Ray(origin,camera.transform.forward,6f,owner.transform,out var pulseHit))origin=pulseHit.point+pulseHit.normal*.025f;
            else origin += camera.transform.forward*6f;
            ElderboomCombat.AreaDamage(origin,7f,definition.damage,owner,false);
            ElderboomCombatVfx.Burst(origin,new Color(.2f,.7f,1f),1.5f);
            return;
        }
        bool melee = definition.FireMode == ElderboomFireMode.Melee;
        bool flame = definition.FireMode == ElderboomFireMode.Flame;
        if(!melee && !flame && muzzle != null)
            ElderboomFeel.MuzzleFlash(origin,camera.transform.forward,definition.category==ElderboomIds.Category.Shotgun?1.4f:definition.category==ElderboomIds.Category.Handgun?.8f:1f);
        if(definition.FireMode == ElderboomFireMode.Flame) ElderboomFireStream.Pulse(owner,muzzle != null ? muzzle : camera.transform,range,definition.fireInterval);
        int count = Mathf.Max(1,definition.pellets);
        float spread = definition.spread + bloom*(aiming?.55f:1f);
        if(aiming && !melee && definition.FireMode != ElderboomFireMode.Flame) spread *= .42f;
        var impacts = Impacts;
        impacts.Clear();
        for(int i=0;i<count;i++)
        {
            Vector2 disk = Random.insideUnitCircle*Mathf.Tan(spread*Mathf.Deg2Rad);
            Vector3 direction = (camera.transform.forward+camera.transform.right*disk.x+camera.transform.up*disk.y).normalized;
            Vector3 end = camera.transform.position+direction*range;
            foreach(var hit in ElderboomCombat.Trace(camera,origin,direction,range,definition.pierce,owner.transform))
            {
                end = hit.point;
                Component target = hit.collider.GetComponentInParent<ElderboomZombie>();
                if(target == null) target = hit.collider.GetComponentInParent<HDRPSliceZombie>();
                if(target == null) { if(!flame) ElderboomFeel.Impact(hit,melee); continue; }
                float distance = Vector3.Distance(origin,hit.point);
                float scale = melee ? 1f : Mathf.Lerp(1f,definition.minimumDamage,Mathf.Clamp01((distance-definition.falloffStart)/Mathf.Max(.01f,range-definition.falloffStart)));
                float amount = (melee ? definition.damage : definition.damage/count)*scale;
                if(hit.collider.name.IndexOf("head",System.StringComparison.OrdinalIgnoreCase)>=0) amount*=2f;
                Component id = target;
                if(!impacts.TryGetValue(id,out var impact)) impacts[id] = impact = new Impact { hit = hit };
                impact.amount = Mathf.Min(definition.damage*2f,impact.amount+amount);
                if(melee) impact.amount = Mathf.Min(definition.damage,impact.amount);
            }
            if(!melee && !flame && muzzle != null && (count == 1 || i < 2))
                ElderboomFeel.Tracer(origin,end,definition.fireInterval >= .11f || count > 1);
        }
        foreach(var impact in impacts.Values) ElderboomCombat.Damage(impact.hit,impact.amount,owner);
        foreach(var impact in impacts.Values)
        {   // one flesh thud per shot, heavier for melee
            ElderboomAudio.Play((melee?"kenney/impactPunch_heavy_00":"kenney/impactPunch_medium_00")+Random.Range(0,5),impact.hit.point,melee?.6f:.32f,true,Random.Range(.92f,1.06f));
            break;
        }
    }
}
