using UnityEngine;

// Same four falling red shards, timing and gravity as Godot's crosshair_hud.gd.
public sealed class ElderboomHitMarker
{
    public const float Duration=.55f, RepeatInterval=.12f;
    float startedAt=float.NegativeInfinity;
    bool pending;
    public bool HasPending => pending;
    public void Reset(){startedAt=float.NegativeInfinity;pending=false;}
    public void Notify(float now)
    {
        Tick(now);
        if(now-startedAt<RepeatInterval){pending=true;return;}
        startedAt=now;
    }
    void Tick(float now)
    {
        if(pending && now>=startedAt+RepeatInterval)
        {startedAt+=RepeatInterval;pending=false;}
    }
    public float Age(float now){Tick(now);return Mathf.Max(0,now-startedAt);}
    public bool IsVisible(float now)=>Age(now)<Duration;
    public void Segment(int index,float now,out Vector2 from,out Vector2 to,out Color color)
    {
        float age=Mathf.Clamp(Age(now),0,Duration);
        var direction=new Vector2(index%2==0?-1:1,index<2?-1:1);
        var fall=new Vector2(direction.x*42*age,-34*age+.5f*680*age*age);
        var center=direction*12+fall;
        var axis=direction.normalized*6.5f;
        float angle=direction.x*age*.8f;
        axis=new Vector2(Mathf.Cos(angle)*axis.x-Mathf.Sin(angle)*axis.y,Mathf.Sin(angle)*axis.x+Mathf.Cos(angle)*axis.y);
        from=center-axis;to=center+axis;
        color=new Color(1,.035f,.025f,1-Mathf.Pow(age/Duration,1.35f));
    }
    public void Draw(Vector2 center,float now)
    {
        if(!IsVisible(now))return;
        var matrix=GUI.matrix;var tint=GUI.color;
        for(int i=0;i<4;i++)
        {
            Segment(i,now,out var a,out var b,out var color);a+=center;b+=center;
            GUI.color=color;
            // Compose in HUD coordinates before the existing screen/UI scale.
            // RotateAroundPivot treats its pivot differently under a scaled GUI
            // matrix and scattered the shards away from the reticle at HiDPI.
            GUI.matrix=matrix*Matrix4x4.TRS(new Vector3(a.x,a.y,0),
                Quaternion.Euler(0,0,Mathf.Atan2(b.y-a.y,b.x-a.x)*Mathf.Rad2Deg),Vector3.one);
            GUI.DrawTexture(new Rect(0,-2.1f,Vector2.Distance(a,b),4.2f),Texture2D.whiteTexture);
            GUI.matrix=matrix;
        }
        GUI.color=tint;
    }
}
