using UnityEngine;

public sealed class ElderboomGrenadeProjectile : MonoBehaviour
{
    HDRPSlicePlayer owner;
    public HDRPSlicePlayer Owner => owner;
    Vector3 direction;
    float damage, remaining=2.8f;
    int session;
    bool detonated;
    public static ElderboomGrenadeProjectile Create(Vector3 origin, Vector3 forward, float amount, HDRPSlicePlayer shooter)
    {
        var go=GameObject.CreatePrimitive(PrimitiveType.Sphere); go.name="GrenadeProjectile";
        go.transform.position=origin; go.transform.localScale=Vector3.one*.12f;
        Destroy(go.GetComponent<Collider>());
        var renderer=go.GetComponent<Renderer>(); renderer.sharedMaterial=ElderboomCombatVfx.ParticleMaterial;
        var projectile=go.AddComponent<ElderboomGrenadeProjectile>();
        projectile.owner=shooter; projectile.damage=amount; projectile.direction=forward.normalized;
        projectile.session=ElderboomGame.Active != null ? ElderboomGame.Active.Session : 0;
        return projectile;
    }
    void Update()
    {
        if(owner == null || owner.IsDead || (ElderboomGame.Active != null && !ElderboomGame.Active.IsSessionActive(session))) { Destroy(gameObject); return; }
        float step=34f*Time.deltaTime;
        if(ElderboomCombat.Ray(transform.position,direction,step,owner.transform,out var hit,.06f))
        { Detonate(hit.point+hit.normal*.025f); return; }
        transform.position+=direction*step; remaining-=Time.deltaTime;
        if(remaining<=0) Detonate(transform.position);
    }
    void Detonate(Vector3 point) { if(detonated)return; detonated=true; Explode(point,damage,owner); Destroy(gameObject); }
    public static void Explode(Vector3 point,float damage,HDRPSlicePlayer owner)
    {
        if(owner == null || owner.IsDead)return;
        ElderboomCombat.AreaDamage(point,4f,damage,owner);
        ElderboomGame.Active?.ReportNoise(point,35f);
        owner.AddTrauma(Mathf.Clamp01(1f-Vector3.Distance(owner.transform.position,point)/16f)*.85f);
        ElderboomCombatVfx.Burst(point,new Color(1,.45f,.05f),2f);
    }
}
