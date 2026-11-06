using UnityEngine;

public sealed class ElderboomPlasmaBolt : MonoBehaviour
{
    public float Damage { get; set; }
    public HDRPSlicePlayer Owner { get; private set; }
    Vector3 direction;
    ElderboomGame game;
    int session;
    float remaining=3f;

    public static ElderboomPlasmaBolt Fire(Camera camera,Vector3 muzzle,float damage,HDRPSlicePlayer shooter,ElderboomGame game)
    {
        Vector3 aim=ElderboomCombat.AimPoint(camera,camera.transform.forward,120f,shooter.transform);
        if(ElderboomCombat.MuzzleObstruction(camera.transform.position,muzzle,shooter.transform,out var obstruction))
        {
            ElderboomCombat.Damage(obstruction,damage,shooter);
            ElderboomCombatVfx.Burst(obstruction.point+obstruction.normal*.02f,new Color(1,.25f,.03f),.3f);
            return null;
        }
        var bolt=Create(muzzle,(aim-muzzle).normalized,game);
        bolt.Owner=shooter;bolt.Damage=damage;return bolt;
    }
    public static ElderboomPlasmaBolt Create(Vector3 position,Vector3 forward,ElderboomGame owner)
    {
        forward=forward.sqrMagnitude>.001f?forward.normalized:Vector3.forward;
        var go=ElderboomImportedVfx.Fireball(position,forward);
        if(go==null)go=new GameObject("Soph_Plasma_Bolt");
        go.name="Soph_Plasma_Bolt";
        go.transform.SetPositionAndRotation(position,Quaternion.LookRotation(forward));
        var light=go.AddComponent<Light>();light.type=LightType.Point;light.color=new Color(1,.24f,.015f);light.intensity=8000;light.range=6;
        var bolt=go.AddComponent<ElderboomPlasmaBolt>();bolt.direction=forward;bolt.game=owner;
        bolt.Owner=owner!=null?owner.Player:null;bolt.session=owner!=null?owner.Session:0;
        return bolt;
    }
    void Update()
    {
        if((Owner!=null && Owner.IsDead)||(game!=null && !game.IsSessionActive(session))){Destroy(gameObject);return;}
        // Include initial overlap: a muzzle touching a thin wall is still obstructed.
        foreach(var collider in Physics.OverlapSphere(transform.position,.12f,ElderboomCombat.WorldMask,QueryTriggerInteraction.Ignore))
        {
            if(ElderboomCombat.IsOwner(collider,Owner!=null?Owner.transform:null)||collider.transform.IsChildOf(transform))continue;
            var zombie=collider.GetComponentInParent<ElderboomZombie>();
            if(zombie!=null && !zombie.IsAlive)continue;
            var point=collider.ClosestPoint(transform.position);
            if(zombie!=null){zombie.TakeDamage(Damage,transform.position,point,-direction);Owner?.ConfirmHit();}
            Destroy(gameObject);return;
        }
        float step=38f*Time.deltaTime;
        if(ElderboomCombat.Ray(transform.position,direction,step,Owner!=null?Owner.transform:null,out var hit,.12f))
        {
            ElderboomCombat.Damage(hit,Damage,Owner);
            ElderboomCombatVfx.Burst(hit.point+hit.normal*.02f,new Color(1,.25f,.03f),.25f);
            Destroy(gameObject);return;
        }
        transform.position+=direction*step;remaining-=Time.deltaTime;
        if(remaining<=0f)Destroy(gameObject);
    }
}
