using System;
using System.Collections.Generic;
using UnityEngine;

// Shared camera/muzzle and projectile queries. Viewmodels, triggers and the owner
// never count as cover; damageable actors do not permit penetration through walls.
public static class ElderboomCombat
{
    public const int WorldMask = ~(1 << 19);

    // Reused query buffers: shots run every few frames, so per-shot RaycastAll arrays,
    // lists and sets were steady garbage. Results are still sorted by distance.
    // Not re-entrant: callers consume a result before the next query.
    static readonly RaycastHit[] rayBuffer = new RaycastHit[128];
    static readonly RaycastHit[] traceBuffer = new RaycastHit[128];
    static readonly List<RaycastHit> traceResult = new List<RaycastHit>(8);
    static readonly HashSet<Component> traceSeen = new HashSet<Component>();
    static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
    public static bool IsOwner(Collider collider, Transform owner) => collider == null ||
        (owner != null && (collider.transform == owner || collider.transform.IsChildOf(owner)));

    public static bool Ray(Vector3 origin, Vector3 direction, float distance, Transform owner, out RaycastHit result, float radius = 0f)
    {
        int count = radius > 0f ? Physics.SphereCastNonAlloc(origin, radius, direction, rayBuffer, distance, WorldMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, rayBuffer, distance, WorldMask, QueryTriggerInteraction.Ignore);
        Array.Sort(rayBuffer, 0, count, ByDistance);
        for (int i = 0; i < count; i++) if (!IsOwner(rayBuffer[i].collider, owner)) { result = rayBuffer[i]; return true; }
        result = default; return false;
    }

    public static bool MuzzleObstruction(Vector3 camera, Vector3 muzzle, Transform owner, out RaycastHit hit)
    {
        Vector3 delta = muzzle-camera;
        return Ray(camera, delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward, delta.magnitude, owner, out hit);
    }

    public static Vector3 AimPoint(Camera camera, Vector3 direction, float range, Transform owner)
        => Ray(camera.transform.position,direction,range,owner,out var hit) ? hit.point : camera.transform.position+direction*range;

    // The returned list is reused by the next Trace call.
    public static List<RaycastHit> Trace(Camera camera, Vector3 muzzle, Vector3 direction, float range, int pierce, Transform owner)
    {
        var result = traceResult;
        result.Clear();
        if (MuzzleObstruction(camera.transform.position,muzzle,owner,out var obstruction)) { result.Add(obstruction); return result; }
        Vector3 aim = AimPoint(camera,direction,range,owner);
        Vector3 delta = aim-muzzle;
        if (delta.sqrMagnitude < .000001f) return result;
        int count = Physics.RaycastNonAlloc(muzzle,delta.normalized,traceBuffer,range,WorldMask,QueryTriggerInteraction.Ignore);
        Array.Sort(traceBuffer,0,count,ByDistance);
        var seen = traceSeen;
        seen.Clear();
        int remaining = Mathf.Max(1,pierce);
        for(int i=0;i<count;i++)
        {
            var hit = traceBuffer[i];
            if(IsOwner(hit.collider,owner)) continue;
            var zombie = hit.collider.GetComponentInParent<ElderboomZombie>();
            var legacy = hit.collider.GetComponentInParent<HDRPSliceZombie>();
            if(zombie != null && !zombie.IsAlive) continue;
            if(zombie == null && legacy == null) { result.Add(hit); break; }
            Component key = zombie != null ? (Component)zombie : legacy;
            if(!seen.Add(key)) continue;
            result.Add(hit);
            if(--remaining <= 0) break;
        }
        return result;
    }

    public static bool Damage(RaycastHit hit, float amount, HDRPSlicePlayer owner)
    {
        if(hit.collider == null || amount <= 0 || (owner != null && owner.IsDead)) return false;
        var zombie = hit.collider.GetComponentInParent<ElderboomZombie>();
        if(zombie != null)
        {
            if(!zombie.IsAlive) return false;
            zombie.TakeDamage(amount,owner != null ? owner.transform.position : hit.point,hit.point,hit.normal);
            owner?.ConfirmHit(); return true;
        }
        var legacy = hit.collider.GetComponentInParent<HDRPSliceZombie>();
        if(legacy == null) return false;
        if(legacy.TakeDamage(amount)) owner?.AddLegacyScore(100);
        owner?.ConfirmHit(); return true;
    }

    public static int AreaDamage(Vector3 center, float radius, float damage, HDRPSlicePlayer owner, bool falloff = true)
    {
        var seen = new HashSet<ElderboomZombie>();
        int damaged=0;
        foreach(var collider in Physics.OverlapSphere(center,radius,WorldMask,QueryTriggerInteraction.Ignore))
        {
            var zombie = collider.GetComponentInParent<ElderboomZombie>();
            if(zombie == null || !zombie.IsAlive || seen.Contains(zombie)) continue;
            Vector3 point = collider.ClosestPoint(center);
            Vector3 delta = point-center;
            if(delta.sqrMagnitude > .0001f && Ray(center,delta.normalized,delta.magnitude+.02f,owner != null ? owner.transform : null,out var obstruction)
                && obstruction.collider.GetComponentInParent<ElderboomZombie>() != zombie) continue;
            seen.Add(zombie);
            float scale = falloff ? Mathf.Lerp(1f,.35f,Mathf.Clamp01(delta.magnitude/radius)) : 1f;
            zombie.TakeDamage(damage*scale,center,point,delta.sqrMagnitude>.0001f?-delta.normalized:Vector3.up);
            owner?.ConfirmHit(); damaged++;
        }
        return damaged;
    }
}
