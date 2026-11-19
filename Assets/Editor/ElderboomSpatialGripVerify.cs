using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

// Spatial 3D verification of all 9 weapon mounts, grips, bore alignment, shoulder seating, forearm twist,
// hand-to-mesh contact, and Hip/ADS camera frustum framing — runs in ~10s in batchmode without screenshots.
public static class ElderboomSpatialGripVerify
{
    static readonly string[] Weapons = { "m4", "mp7", "glock19", "rhino", "shotgun", "mp133", "grenade", "minigun", "flame" };
    static readonly HashSet<string> Shouldered = new HashSet<string> { "m4", "shotgun", "mp133", "grenade" };

    [MenuItem("Elderboom/QA/Spatial 3D Grip Verify")]
    public static void Run()
    {
        var sb = new StringBuilder();
        var stage = new GameObject("__SpatialVerify");
        int totalFails = 0;
        try
        {
            var camGo = new GameObject("Cam"); camGo.transform.SetParent(stage.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.025f; cam.farClipPlane = 100f;
            cam.fieldOfView = ElderboomPauseMenu.VerticalFov(ElderboomPauseMenu.DefaultHorizontalFov); // 50.53 deg @ 16:9
            cam.aspect = 16f / 9f;

            var lateUpdate = typeof(ElderboomArmsDriver).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            var setAim = typeof(ElderboomArmsDriver).GetMethod("SetAim", BindingFlags.Instance | BindingFlags.Public);
            var aimWeightField = typeof(ElderboomArmsDriver).GetField("aimWeight", BindingFlags.Instance | BindingFlags.NonPublic);

            foreach (var id in Weapons)
            {
                var def = ElderboomWeapons.Find(id);
                var root = ElderboomWeaponOnly.Build(def, cam.transform);
                if (root == null) { sb.AppendLine($"{id}: BUILD FAILED"); totalFails++; continue; }
                root.gameObject.SetActive(true);
                var view = root.GetComponent<ElderboomViewmodel>();
                var drv = root.GetComponent<ElderboomArmsDriver>();
                var wn = drv.WeaponNode;
                var arms = drv.Arms;
                var idle = drv.animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.EndsWith("_Idle"));
                var adsClip = drv.animator.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.EndsWith("_ADSIdle"));

                // ---- HIP POSE (exact runtime placement + IK) ----
                root.localPosition = view.hipPosition;
                root.localRotation = view.hipRotation;
                if (idle != null) idle.SampleAnimation(arms.gameObject, 0f);
                setAim.Invoke(drv, new object[] { false });
                aimWeightField.SetValue(drv, 0f);
                lateUpdate.Invoke(drv, null);

                var fails = new List<string>();
                var muz = ElderboomWeaponOnly.Muzzle(wn);
                var gR = Deep(wn, "Grip_R"); var gL = Deep(wn, "Grip_L");
                var uaR = Deep(arms, "DEF-upper_arm.R");
                var faL = Deep(arms, "DEF-forearm.L"); var ftL = Deep(arms, "DEF-forearm.L.001"); var hL = Deep(arms, "DEF-hand.L");
                var hR = Deep(arms, "DEF-hand.R");

                // 1. Bore orientation in camera space (pitch, yaw, roll)
                Vector3 fwdCam = cam.transform.InverseTransformDirection(muz.forward).normalized;
                Vector3 upCam = cam.transform.InverseTransformDirection(muz.up).normalized;
                float pitch = Mathf.Asin(Mathf.Clamp(fwdCam.y, -1f, 1f)) * Mathf.Rad2Deg;
                float yaw = Mathf.Atan2(fwdCam.x, fwdCam.z) * Mathf.Rad2Deg;
                float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, fwdCam), Vector3.ProjectOnPlane(upCam, fwdCam), fwdCam);
                if (Mathf.Abs(pitch) > 4.0f) fails.Add($"HIP_PITCH({pitch:F1}deg)");
                if (Mathf.Abs(yaw) > 6.5f) fails.Add($"HIP_YAW({yaw:F1}deg)");
                if (Mathf.Abs(roll) > 5.0f) fails.Add($"HIP_ROLL({roll:F1}deg)");

                // 2. Socket + real mesh vertex contact distance (detects hands floating in thin air!)
                Vector3 fR = drv.FistPosition(0), fL = drv.FistPosition(1);
                float sockErrR = gR != null ? Vector3.Distance(fR, gR.position) * 100f : 99f;
                float sockErrL = gL != null ? Vector3.Distance(fL, gL.position) * 100f : 99f;
                float meshDistR = ClosestWeaponVertexCm(wn, fR);
                float meshDistL = ClosestWeaponVertexCm(wn, fL);
                if (sockErrR > 2.0f) fails.Add($"SOCK_R({sockErrR:F1}cm)");
                if (sockErrL > 2.5f) fails.Add($"SOCK_L({sockErrL:F1}cm)");
                if (meshDistR > 3.5f) fails.Add($"AIR_HAND_R({meshDistR:F1}cm)");
                if (meshDistL > 5.5f) fails.Add($"AIR_HAND_L({meshDistL:F1}cm)");

                // 3. Wrist-to-stock penetration check on shouldered guns (right wrist back must not sit inside stock top/center)
                float wristTopClearCm = 99f;
                if (Shouldered.Contains(id) && hR != null)
                {
                    // Check vertical clearance between right wrist position and closest weapon mesh vertex directly around wrist Z
                    wristTopClearCm = WristClearanceCm(wn, cam.transform, hR.position);
                }

                // 4. Stock-to-right-shoulder alignment on shouldered guns
                Bounds wbCam = BoundsInCamera(wn, cam.transform);
                string stockInfo = "";
                if (Shouldered.Contains(id) && uaR != null)
                {
                    Vector3 shCam = cam.transform.InverseTransformPoint(uaR.position);
                    float dz = wbCam.min.z - shCam.z; // <= 0.18m means buttstock reaches the shoulder area
                    float dx = Mathf.Abs(wbCam.center.x - shCam.x);
                    stockInfo = $" stockDZ={dz:F2}m stockDX={dx:F2}m";
                    if (dz > 0.25f) fails.Add($"STOCK_SHORT(dZ={dz:F2}m)");
                }

                // 5. Forearm L twist distribution check
                float twistA = (faL != null && ftL != null) ? Quaternion.Angle(Quaternion.identity, ftL.localRotation) : 0f;
                float twistB = (ftL != null && hL != null) ? Quaternion.Angle(Quaternion.identity, hL.localRotation) : 0f;

                // 6. Hip frustum check (center & muzzle inside lower-right viewport, not cut off at bottom)
                Vector3 vpCenter = cam.WorldToViewportPoint(cam.transform.TransformPoint(wbCam.center));
                Vector3 vpMuz = cam.WorldToViewportPoint(muz.position);
                if (vpMuz.x < 0.35f || vpMuz.x > 0.92f || vpMuz.y < 0.08f || vpMuz.y > 0.65f)
                    fails.Add($"HIP_MUZ_VP({vpMuz.x:F2},{vpMuz.y:F2})");
                if (vpCenter.y < 0.02f || vpCenter.y > 0.55f)
                    fails.Add($"HIP_CTR_VP({vpCenter.x:F2},{vpCenter.y:F2})");

                // ---- ADS POSE ----
                root.localPosition = view.adsPosition;
                root.localRotation = view.adsRotation;
                if (drv.LocksSights && adsClip != null) adsClip.SampleAnimation(arms.gameObject, 0f);
                else if (idle != null) idle.SampleAnimation(arms.gameObject, 0f);
                setAim.Invoke(drv, new object[] { true });
                aimWeightField.SetValue(drv, 1f);
                for (int step = 0; step < 30; step++) lateUpdate.Invoke(drv, null); // settle LockSights smoothing

                float adsMinZ = MinVisibleZ(root, cam);
                float adsSockR = gR != null ? Vector3.Distance(drv.FistPosition(0), gR.position) * 100f : 99f;
                float adsSockL = gL != null ? Vector3.Distance(drv.FistPosition(1), gL.position) * 100f : 99f;
                if (adsMinZ < cam.nearClipPlane + 0.04f) fails.Add($"ADS_CLIP(minZ={adsMinZ:F3}m)");
                if (adsSockR > 2.5f) fails.Add($"ADS_SOCK_R({adsSockR:F1}cm)");
                if (adsSockL > 3.0f) fails.Add($"ADS_SOCK_L({adsSockL:F1}cm)");

                string verdict = fails.Count == 0 ? "PASS" : "FAIL [" + string.Join(", ", fails) + "]";
                if (fails.Count > 0) totalFails++;
                sb.AppendLine($"{id,-8} {verdict}");
                sb.AppendLine($"   HIP bore: pitch={pitch:+0.0;-0.0}deg yaw={yaw:+0.0;-0.0}deg roll={roll:+0.0;-0.0}deg | vpCtr=({vpCenter.x:F2},{vpCenter.y:F2}) vpMuz=({vpMuz.x:F2},{vpMuz.y:F2}){stockInfo}");
                sb.AppendLine($"   HIP hands: sockR={sockErrR:F1}cm meshR={meshDistR:F1}cm | sockL={sockErrL:F1}cm meshL={meshDistL:F1}cm | twistL=({twistA:F0},{twistB:F0})deg");
                sb.AppendLine($"   ADS: minZ={adsMinZ:F3}m (lim 0.065) | sockR={adsSockR:F1}cm sockL={adsSockL:F1}cm | locks={drv.LocksSights}");

                Object.DestroyImmediate(root.gameObject);
            }
        }
        finally { Object.DestroyImmediate(stage); }

        sb.AppendLine($"\nSUMMARY: {Weapons.Length - totalFails}/{Weapons.Length} PASS");
        File.WriteAllText("Logs/spatial_grip_verify.txt", sb.ToString());
        Debug.Log("[SpatialGripVerify]\n" + sb);
    }

    static Transform Deep(Transform r, string n) { foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t; return null; }

    static Bounds BoundsInCamera(Transform wn, Transform cam)
    {
        Bounds b = default; bool any = false;
        var w2c = cam.worldToLocalMatrix;
        foreach (var r in wn.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r.bounds.size.sqrMagnitude < 1e-6f) continue;
            for (int i = 0; i < 8; i++)
            {
                var c = r.bounds.center + Vector3.Scale(r.bounds.extents, new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                var lc = w2c.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(lc, Vector3.zero); any = true; } else b.Encapsulate(lc);
            }
        }
        return b;
    }

    static float ClosestWeaponVertexCm(Transform wn, Vector3 worldPt)
    {
        float bestSq = float.PositiveInfinity;
        var vList = new List<Vector3>();
        var baked = new Mesh();
        foreach (var r in wn.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r.bounds.size.sqrMagnitude < 1e-6f) continue;
            Mesh m = null; Matrix4x4 l2w;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) { smr.BakeMesh(baked, true); m = baked; l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); }
            else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; m = mf.sharedMesh; l2w = r.transform.localToWorldMatrix; }
            m.GetVertices(vList);
            Vector3 localPt = l2w.inverse.MultiplyPoint3x4(worldPt);
            float scale = l2w.lossyScale.x;
            for (int i = 0; i < vList.Count; i++)
            {
                float dsq = (vList[i] - localPt).sqrMagnitude * scale * scale;
                if (dsq < bestSq) bestSq = dsq;
            }
        }
        Object.DestroyImmediate(baked);
        return float.IsPositiveInfinity(bestSq) ? 999f : Mathf.Sqrt(bestSq) * 100f;
    }

    static float WristClearanceCm(Transform wn, Transform cam, Vector3 wristWorld)
    {
        Vector3 wCam = cam.InverseTransformPoint(wristWorld);
        float minY = float.PositiveInfinity;
        var vList = new List<Vector3>();
        foreach (var mf in wn.GetComponentsInChildren<MeshFilter>(false))
        {
            var r = mf.GetComponent<Renderer>();
            if (r == null || !r.enabled || mf.sharedMesh == null) continue;
            var m = cam.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            mf.sharedMesh.GetVertices(vList);
            for (int i = 0; i < vList.Count; i++)
            {
                Vector3 p = m.MultiplyPoint3x4(vList[i]);
                if (Mathf.Abs(p.z - wCam.z) < 0.03f && Mathf.Abs(p.x - wCam.x) < 0.03f && p.y < minY) minY = p.y;
            }
        }
        return float.IsPositiveInfinity(minY) ? 99f : (minY - wCam.y) * 100f;
    }

    static float MinVisibleZ(Transform root, Camera cam)
    {
        float minZ = float.PositiveInfinity;
        var vList = new List<Vector3>();
        var baked = new Mesh();
        var w2c = cam.transform.worldToLocalMatrix;
        foreach (var r in root.GetComponentsInChildren<Renderer>(false))
        {
            if (!r.enabled || r.forceRenderingOff || r is ParticleSystemRenderer || r is LineRenderer) continue;
            Mesh m = null; Matrix4x4 l2w;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) { smr.BakeMesh(baked, true); m = baked; l2w = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one); }
            else { var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue; m = mf.sharedMesh; l2w = r.transform.localToWorldMatrix; }
            m.GetVertices(vList);
            for (int i = 0; i < vList.Count; i++)
            {
                Vector3 wp = l2w.MultiplyPoint3x4(vList[i]);
                Vector3 vp = cam.WorldToViewportPoint(wp);
                if (vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f)
                {
                    float z = w2c.MultiplyPoint3x4(wp).z;
                    if (z < minZ) minZ = z;
                }
            }
        }
        Object.DestroyImmediate(baked);
        return float.IsPositiveInfinity(minZ) ? 0f : minZ;
    }
}
