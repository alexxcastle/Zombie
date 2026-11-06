// Test / preview tooling: compiled in the Editor and development builds only.
#if UNITY_EDITOR || DEBUG
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public sealed class ElderboomViewAuditRig : MonoBehaviour
{
    public bool captureOnPlay;
    public string mode = "";
    static readonly string[] WeaponResources =
    {
        "rhino", "m1911", "service", "revolver", "ar9", "carbine", "shotgun", "sawnoff",
        "wrench", "crowbar", "grenade", "minigun", "flame"
    };

    IEnumerator Start()
    {
        if (!captureOnPlay) yield break;
        yield return null;
        var camera = GetComponent<Camera>();
        var report = new StringBuilder();
        report.AppendLine("audit gfx=" + SystemInfo.graphicsDeviceType);
        if (mode == "recheck" || mode == "sweep")
        {
            if (mode == "sweep") yield return Sweep(camera, report);
            else yield return Recheck(camera, report);
            Directory.CreateDirectory("Logs/view_audit");
            File.WriteAllText(mode == "sweep" ? "Logs/view_audit/sweep.txt" : "Logs/view_audit/recheck.txt", report.ToString());
            File.WriteAllText("Logs/view_audit/play_done.txt", "ok");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#endif
            yield break;
        }
        foreach (var pivotName in new[] { "Soph_Viewmodel_Pivot", "Eli_Viewmodel_Pivot", "Tactical_Viewmodel_Pivot" })
        {
            var pivot = transform.Find(pivotName);
            if (pivot == null) { report.AppendLine("MISSING " + pivotName); continue; }
            Prepare(pivot.gameObject);
            var view = pivot.GetComponent<ElderboomViewmodel>();
            if (view != null) view.enabled = false;
            yield return AbilityPass(camera, pivot, view, report);
            yield return WeaponPass(camera, pivot.gameObject, pivot.name.Replace("_Viewmodel_Pivot", ""), view, report);
            pivot.gameObject.SetActive(false);
        }
        var lift = transform.Find("LiftSmash");
        var keep = transform.Find("KeepAway");
        if (lift != null) yield return AbilityModelPass(camera, lift.GetComponent<ElderboomAbilityView>(), "lift", new[] { 0.05f, 0.85f, 1.67f, 2.45f }, report);
        if (keep != null) yield return AbilityModelPass(camera, keep.GetComponent<ElderboomAbilityView>(), "keep", new[] { 0.05f, 2.2f, 4.6f, 7.3f }, report);
        foreach (var id in WeaponResources)
        {
            var prefab = Resources.Load<GameObject>("Elderboom/Weapons/" + id);
            if (prefab == null) { report.AppendLine("MISSING weapon " + id); continue; }
            var instance = Instantiate(prefab, transform, false);
            instance.name = id;
            Prepare(instance);
            var view = instance.GetComponent<ElderboomViewmodel>();
            if (view != null) view.enabled = false;
            yield return WeaponPass(camera, instance, id, view, report);
            Destroy(instance);
        }
        Directory.CreateDirectory("Logs/view_audit");
        File.WriteAllText("Logs/view_audit/report.txt", report.ToString());
        File.WriteAllText("Logs/view_audit/play_done.txt", "ok");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    IEnumerator Recheck(Camera camera, StringBuilder report)
    {
        foreach (var name in new[] { "Eli_Viewmodel_Pivot", "Tactical_Viewmodel_Pivot" })
        {
            var pivot = transform.Find(name);
            if (pivot == null) { report.AppendLine("MISSING " + name); continue; }
            Prepare(pivot.gameObject);
            var view = pivot.GetComponent<ElderboomViewmodel>();
            if (view != null) view.enabled = false;
            yield return WeaponPass(camera, pivot.gameObject, name, view, report);
        }
        var prefab = Resources.Load<GameObject>("Elderboom/Weapons/rhino");
        if (prefab != null)
        {
            var instance = Instantiate(prefab, transform, false);
            Prepare(instance);
            var view = instance.GetComponent<ElderboomViewmodel>();
            if (view != null) view.enabled = false;
            yield return WeaponPass(camera, instance, "rhino", view, report);
            var pose = ElderboomWeaponFraming.FromViewmodel(view, instance.transform);
            instance.SetActive(true);
            instance.transform.localPosition = pose.hipPosition;
            instance.transform.localRotation = pose.hipRotation;
            var spring = new ElderboomRecoil();
            spring.Settle(0.4f);
            instance.transform.localPosition = pose.hipPosition + spring.position;
            instance.transform.localRotation = Quaternion.Euler(spring.rotation * Mathf.Rad2Deg) * pose.hipRotation;
            yield return null;
            report.AppendLine("weapon rhino spring depth=" + Depth(camera, instance).ToString("0.00") + " kick=" + spring.position.ToString("F3")
                + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/weapon_rhino_spring.png"));
        }
    }

    IEnumerator Sweep(Camera camera, StringBuilder report)
    {
        yield return SweepOperator(camera, "Eli_Viewmodel_Pivot", "output_unwrapped.001", 0.18f, report);
            yield return SweepOperator(camera, "Tactical_Viewmodel_Pivot", "output_unwrapped.003", 0.16f, report);
        var prefab = Resources.Load<GameObject>("Elderboom/Weapons/rhino");
        if (prefab != null)
        {
            var instance = Instantiate(prefab, transform, false);
            instance.name = "rhino";
            Prepare(instance);
            var view = instance.GetComponent<ElderboomViewmodel>();
            if (view != null) view.enabled = false;
            yield return SweepRhino(camera, instance, view, report);
            Destroy(instance);
        }
    }

    IEnumerator SweepOperator(Camera camera, string pivotName, string meshName, float relief, StringBuilder report)
    {
        var pivot = transform.Find(pivotName);
        if (pivot == null) { report.AppendLine("MISSING " + pivotName); yield break; }
        Prepare(pivot.gameObject);
        var view = pivot.GetComponent<ElderboomViewmodel>();
        if (view != null) view.enabled = false;
        HideAll();
        pivot.gameObject.SetActive(true);
        if (view != null)
        {
            pivot.localPosition = view.hipPosition;
            pivot.localRotation = view.hipRotation;
            if (view.idle != null && view.model != null)
                view.idle.SampleAnimation(view.model, view.operatorId == "TACTICAL" ? 1f / 60f : 0f);
        }
        yield return null;
        report.AppendLine(pivotName + " hip barrel " + DescribeBarrel(camera, pivot, meshName));
        float[] alongs = pivotName.StartsWith("Eli") ? new[] { 0.2f, 0.45f, 0.7f } : new[] { 0f };
        foreach (var renderer in pivot.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.enabled) continue;
            var size = renderer.bounds.size;
            if (size.magnitude < 0.25f) continue;
            report.AppendLine(pivotName + " renderer " + renderer.name + " size=" + size.ToString("F2"));
        }
        foreach (float along in alongs)
            yield return CaptureBarrelAds(camera, pivot.gameObject, view, meshName, relief, pivotName + (along > 0f ? "_a" + along.ToString("0.00") : ""), report, along);
        if (view != null && view.operatorId == "ELI" && view.ability != null && view.model != null)
        {
            pivot.gameObject.SetActive(true);
            pivot.localPosition = view.hipPosition;
            pivot.localRotation = view.hipRotation;
            float[] elapsed = { 0.15f, 1.1f, 1.85f, 2.95f };
            float first = 0f;
            for (int i = 0; i < elapsed.Length; i++)
            {
                float source = Mathf.Clamp(EliSource(elapsed[i]), 0f, Mathf.Max(0.01f, view.ability.length - 0.001f));
                view.ability.SampleAnimation(view.model, source);
                yield return null;
                float motion = PoseDelta(view.model.transform);
                if (i == 0) first = motion;
                report.AppendLine("ability ELI t=" + elapsed[i].ToString("0.00") + " src=" + source.ToString("0.00")
                    + " legacy=" + view.ability.legacy + " depth=" + Depth(camera, pivot.gameObject).ToString("0.00")
                    + " bone=" + motion.ToString("0.0") + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_ELI_" + i + ".png"));
            }
            report.AppendLine("ability ELI span=" + Mathf.Abs(PoseDelta(view.model.transform) - first).ToString("0.0"));
        }
        pivot.gameObject.SetActive(false);
    }

    IEnumerator SweepRhino(Camera camera, GameObject root, ElderboomViewmodel view, StringBuilder report)
    {
        HideAll();
        root.SetActive(true);
        var pose = ElderboomWeaponFraming.FromViewmodel(view, root.transform);
        root.transform.localPosition = pose.hipPosition;
        root.transform.localRotation = pose.hipRotation;
        var model = view != null && view.model != null ? view.model.transform : root.transform.childCount > 0 ? root.transform.GetChild(0) : root.transform;
        Quaternion baseRot = model.localRotation;
        Vector3 basePos = model.localPosition;
        if (BarrelForward(camera, root.transform, "Rhino 60DS") > 0.75f)
        {
            yield return null;
            report.AppendLine("rhino stored hip depth=" + Depth(camera, root).ToString("0.00")
                + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_rhino_yaw0.png"));
            root.transform.localPosition = pose.adsPosition;
            root.transform.localRotation = pose.adsRotation;
            yield return null;
            report.AppendLine("rhino stored ads depth=" + Depth(camera, root).ToString("0.00")
                + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_rhino_ads.png"));
            yield break;
        }
        int bestYaw = 0;
        float bestScore = -100f;
        foreach (int yaw in new[] { 0, 90, -90 })
        {
            TurnInPlace(camera, root, model, basePos, baseRot, yaw);
            if (view != null && view.idle != null && view.model != null)
                view.idle.SampleAnimation(view.model, 0f);
            yield return null;
            float dot = BarrelForward(camera, root.transform, "Rhino 60DS");
            float depth = Depth(camera, root);
            Vector3 center = CameraCenter(camera, root);
            float score = dot + (depth > 0.12f ? 1f : -4f) + (center.x > -0.05f ? 0.4f : -0.4f);
            report.AppendLine("rhino yaw " + yaw + " dot=" + dot.ToString("0.00") + " depth=" + depth.ToString("0.00")
                + " center=" + center.ToString("F2") + " score=" + score.ToString("0.00") + " " + DescribeBarrel(camera, root.transform, "Rhino 60DS"));
            ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_rhino_yaw" + yaw + ".png");
            if (score > bestScore)
            {
                bestScore = score;
                bestYaw = yaw;
            }
        }
        TurnInPlace(camera, root, model, basePos, baseRot, bestYaw);
        if (view != null && view.idle != null && view.model != null)
            view.idle.SampleAnimation(view.model, 0f);
        yield return null;
        report.AppendLine("rhino chosen yaw " + bestYaw + " modelPos=" + model.localPosition.ToString("F4") + " modelRot=" + model.localRotation.ToString("F4"));
        yield return CaptureBarrelAds(camera, root, view, "Rhino 60DS", 0.29f, "rhino", report);
    }

    IEnumerator CaptureBarrelAds(Camera camera, GameObject root, ElderboomViewmodel view, string meshName, float relief, string label, StringBuilder report, float along = 0f)
    {
        HideAll();
        root.SetActive(true);
        if (view != null)
        {
            root.transform.localPosition = view.hipPosition;
            root.transform.localRotation = view.hipRotation;
            if (view.idle != null && view.model != null)
                view.idle.SampleAnimation(view.model, view.operatorId == "TACTICAL" ? 1f / 60f : 0f);
            var eyes = view.model != null && meshName.StartsWith("output_unwrapped")
                ? ElderboomWeaponFraming.FindNamed(view.model.transform, "Camera_FPS", "Eyes") : null;
            if (eyes != null)
            {
                report.AppendLine(label + " eyes before " + camera.transform.InverseTransformPoint(eyes.position).ToString("F3"));
                view.model.transform.position += camera.transform.position - eyes.position;
            }
        }
        yield return null;
        if (!TryBarrelAds(camera, root.transform, meshName, relief, along, out var position, out var rotation, out var note))
        {
            report.AppendLine(label + " barrel ads failed " + note);
            yield break;
        }
        root.transform.localPosition = position;
        root.transform.localRotation = rotation;
        yield return null;
        report.AppendLine(label + " ads " + note + " pos=" + position.ToString("F4") + " rot=" + rotation.ToString("F4")
            + " depth=" + Depth(camera, root).ToString("0.00")
            + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_" + label + "_ads.png"));
        var spring = new ElderboomRecoil();
        spring.Settle(0.4f);
        root.transform.localPosition = position + spring.position;
        root.transform.localRotation = Quaternion.Euler(spring.rotation * Mathf.Rad2Deg) * rotation;
        yield return null;
        report.AppendLine(label + " ads spring kick=" + spring.position.ToString("F3")
            + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/sweep_" + label + "_spring.png"));
        root.SetActive(false);
    }

    static bool TryBarrelAds(Camera camera, Transform pivot, string meshName, float relief, float along, out Vector3 localPosition, out Quaternion localRotation, out string note)
    {
        localPosition = pivot.localPosition;
        localRotation = pivot.localRotation;
        note = "";
        var mesh = FindExact(pivot, meshName);
        var renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            note = "no renderer " + meshName;
            return false;
        }
        Bounds local = renderer.localBounds;
        int axis = 0;
        if (local.size.y > local.size[axis]) axis = 1;
        if (local.size.z > local.size[axis]) axis = 2;
        Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        float sign = Vector3.Dot(renderer.transform.TransformDirection(localAxis), camera.transform.forward) >= 0f ? 1f : -1f;
        Vector3 barrel = renderer.transform.TransformDirection(localAxis * sign);
        Vector3 upAxis = Vector3.up;
        float upDot = Mathf.Abs(Vector3.Dot(renderer.transform.up, barrel));
        if (upDot > 0.8f)
        {
            upAxis = Mathf.Abs(Vector3.Dot(renderer.transform.right, camera.transform.up)) > Mathf.Abs(Vector3.Dot(renderer.transform.forward, camera.transform.up))
                ? Vector3.right : Vector3.forward;
        }
        Vector3 up = renderer.transform.TransformDirection(upAxis);
        if (Vector3.Dot(up, camera.transform.up) < 0f) up = -up;
        Transform rearMarker = null, frontMarker = null;
        foreach (var node in pivot.GetComponentsInChildren<Transform>(true))
        {
            if (node.name == "MainWeapon_ADS_Rear" || node.name == "Soph_ADS_Rear") rearMarker = node;
            if (node.name == "MainWeapon_ADS_Front" || node.name == "Soph_ADS_Front") frontMarker = node;
        }
        Vector3 sightWorld;
        string sightNote = "bounds";
        if (rearMarker != null && frontMarker != null)
        {
            if (Vector3.Dot(rearMarker.position, barrel) > Vector3.Dot(frontMarker.position, barrel))
            {
                var swap = rearMarker;
                rearMarker = frontMarker;
                frontMarker = swap;
                sightNote = "markers-swapped";
            }
            else sightNote = "markers";
            sightWorld = Vector3.Lerp(rearMarker.position, frontMarker.position, Mathf.Clamp01(along));
            Vector3 markerBarrel = frontMarker.position - rearMarker.position;
            if (markerBarrel.sqrMagnitude > 1e-6f) barrel = markerBarrel.normalized;
        }
        else
        {
            int upIndex = upAxis == Vector3.right ? 0 : upAxis == Vector3.up ? 1 : 2;
            float upSign = Vector3.Dot(renderer.transform.TransformDirection(upAxis), camera.transform.up) >= 0f ? 1f : -1f;
            Vector3 sight = local.center - localAxis * sign * local.size[axis] * 0.22f + upAxis * upSign * (local.extents[upIndex] + 0.012f);
            sightWorld = renderer.transform.TransformPoint(sight);
        }
        if (barrel.sqrMagnitude < 1e-6f)
        {
            note = "degenerate barrel";
            return false;
        }
        Quaternion delta = Quaternion.LookRotation(camera.transform.forward, camera.transform.up) * Quaternion.Inverse(Quaternion.LookRotation(barrel, up));
        pivot.rotation = delta * pivot.rotation;
        Vector3 aimed = rearMarker != null && frontMarker != null
            ? Vector3.Lerp(rearMarker.position, frontMarker.position, Mathf.Clamp01(along))
            : sightWorld;
        pivot.position += camera.transform.position + camera.transform.forward * relief - aimed;
        localPosition = pivot.localPosition;
        localRotation = pivot.localRotation;
        Vector3 sightDepth = camera.transform.InverseTransformPoint(rearMarker != null ? rearMarker.position : aimed);
        note = sightNote + " axis=" + axis + " sign=" + sign.ToString("0") + " sight=" + sightDepth.ToString("F2");
        return true;
    }

    static string DescribeBarrel(Camera camera, Transform pivot, string meshName)
    {
        var mesh = FindExact(pivot, meshName);
        var renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
        if (renderer == null) return "missing " + meshName;
        return "fwd=" + BarrelForward(camera, pivot, meshName).ToString("0.00") + " size=" + renderer.localBounds.size.ToString("F2")
            + " right=" + camera.transform.InverseTransformDirection(renderer.transform.right).ToString("F2")
            + " up=" + camera.transform.InverseTransformDirection(renderer.transform.up).ToString("F2")
            + " forward=" + camera.transform.InverseTransformDirection(renderer.transform.forward).ToString("F2");
    }

    static float BarrelForward(Camera camera, Transform pivot, string meshName)
    {
        var mesh = FindExact(pivot, meshName);
        var renderer = mesh != null ? mesh.GetComponent<Renderer>() : null;
        if (renderer == null) return -2f;
        Bounds local = renderer.localBounds;
        int axis = 0;
        if (local.size.y > local.size[axis]) axis = 1;
        if (local.size.z > local.size[axis]) axis = 2;
        Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
        Vector3 world = renderer.transform.TransformDirection(localAxis);
        return Mathf.Abs(Vector3.Dot(world.normalized, camera.transform.forward));
    }

    static void TurnInPlace(Camera camera, GameObject root, Transform model, Vector3 basePos, Quaternion baseRot, int yaw)
    {
        model.localRotation = baseRot;
        model.localPosition = basePos;
        if (yaw == 0) return;
        Bounds bounds = BoundsOf(root);
        Vector3 center = bounds.center;
        Quaternion turn = Quaternion.AngleAxis(yaw, camera.transform.up);
        Vector3 oldPos = model.position;
        model.rotation = turn * model.rotation;
        model.position = center + turn * (oldPos - center);
    }

    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled).ToArray();
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Vector3 CameraCenter(Camera camera, GameObject root)
    {
        return camera.transform.InverseTransformPoint(BoundsOf(root).center);
    }

    static Transform FindExact(Transform root, string name)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            if (node.name == name) return node;
        return null;
    }

    IEnumerator AbilityPass(Camera camera, Transform pivot, ElderboomViewmodel view, StringBuilder report)
    {
        if (view == null || view.ability == null || view.model == null)
        {
            report.AppendLine(pivot.name + " ability missing clip=" + (view != null && view.ability != null) + " model=" + (view != null && view.model != null));
            yield break;
        }
        float[] elapsed = view.operatorId == "SOPH" ? new[] { 0.2f, 1.6f, 3.4f, 6.4f }
            : view.operatorId == "ELI" ? new[] { 0.15f, 1.1f, 1.85f, 2.95f }
            : new[] { 0.02f, 0.4f, 0.75f, 1.15f };
        HideAll();
        pivot.gameObject.SetActive(true);
        pivot.localPosition = view.hipPosition;
        pivot.localRotation = view.hipRotation;
        float first = 0f;
        bool have = false;
        for (int i = 0; i < elapsed.Length; i++)
        {
            float source = view.operatorId == "SOPH" ? SophSource(elapsed[i]) : view.operatorId == "ELI" ? EliSource(elapsed[i]) : elapsed[i];
            source = Mathf.Clamp(source, 0f, Mathf.Max(0.01f, view.ability.length - 0.001f));
            view.ability.SampleAnimation(view.model, source);
            yield return null;
            float motion = PoseDelta(view.model.transform);
            if (!have) { first = motion; have = true; }
            string path = "Logs/view_audit/ability_" + view.operatorId + "_" + i + ".png";
            string stats = ElderboomAbilityShots.Capture(camera, path);
            report.AppendLine("ability " + view.operatorId + " t=" + elapsed[i].ToString("0.00") + " src=" + source.ToString("0.00")
                + " legacy=" + view.ability.legacy + " depth=" + Depth(camera, pivot.gameObject).ToString("0.00")
                + " bone=" + motion.ToString("0.0") + " " + FlagDepth(Depth(camera, pivot.gameObject)) + " " + stats);
        }
        report.AppendLine("ability " + view.operatorId + " span=" + Mathf.Abs(PoseDelta(view.model.transform) - first).ToString("0.0"));
    }

    IEnumerator AbilityModelPass(Camera camera, ElderboomAbilityView view, string name, float[] times, StringBuilder report)
    {
        if (view == null) { report.AppendLine("MISSING " + name); yield break; }
        HideAll();
        view.gameObject.SetActive(true);
        Prepare(view.gameObject);
        for (int i = 0; i < times.Length; i++)
        {
            view.Sample(times[i]);
            yield return null;
            string path = "Logs/view_audit/ability_" + name + "_" + i + ".png";
            report.AppendLine("ability " + name + " t=" + times[i].ToString("0.00") + " legacy=" + (view.clip != null && view.clip.legacy)
                + " depth=" + Depth(camera, view.gameObject).ToString("0.00") + " bone=" + (view.model != null ? PoseDelta(view.model.transform).ToString("0.0") : "0")
                + " " + FlagDepth(Depth(camera, view.gameObject)) + " " + ElderboomAbilityShots.Capture(camera, path));
        }
        view.gameObject.SetActive(false);
    }

    IEnumerator WeaponPass(Camera camera, GameObject root, string name, ElderboomViewmodel view, StringBuilder report)
    {
        HideAll();
        root.SetActive(true);
        var pose = ElderboomWeaponFraming.FromViewmodel(view, root.transform);
        if (view != null && view.idle != null && view.model != null)
            view.idle.SampleAnimation(view.model, view.operatorId == "TACTICAL" ? 1f / 60f : 0f);
        root.transform.localPosition = pose.hipPosition;
        root.transform.localRotation = pose.hipRotation;
        yield return null;
        float hip = Depth(camera, root);
        report.AppendLine("weapon " + name + " hip depth=" + hip.ToString("0.00") + " " + FlagDepth(hip) + " muzzle=" + (FindMuzzle(root.transform) != null)
            + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/weapon_" + name + "_hip.png"));
        if (pose.hasAds)
        {
            root.transform.localPosition = pose.adsPosition;
            root.transform.localRotation = pose.adsRotation;
            yield return null;
            float ads = Depth(camera, root);
            report.AppendLine("weapon " + name + " ads depth=" + ads.ToString("0.00") + " " + FlagDepth(ads)
                + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/weapon_" + name + "_ads.png"));
        }
        else report.AppendLine("weapon " + name + " ads NONE");
        root.transform.localPosition = pose.hipPosition + new Vector3(-0.006f, 0.008f, -0.035f);
        root.transform.localRotation = pose.hipRotation;
        yield return null;
        report.AppendLine("weapon " + name + " recoil depth=" + Depth(camera, root).ToString("0.00")
            + " " + ElderboomAbilityShots.Capture(camera, "Logs/view_audit/weapon_" + name + "_recoil.png"));
        root.SetActive(false);
    }

    void HideAll()
    {
        foreach (Transform child in transform)
            child.gameObject.SetActive(false);
    }

    static void Prepare(GameObject root)
    {
        ElderboomWeaponFraming.HideNonViewmodelBodies(root.transform);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            renderer.gameObject.layer = 0;
            if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            string name = renderer.name.ToLowerInvariant();
            if (name.Contains("head") || name.Contains("hair") || name.Contains("helmet") || name.Contains("body")
                || name.Contains("face") || name.Contains("teeth") || name.Contains("skull") || name.Contains("neck")
                || name.Contains("eye"))
                renderer.enabled = false;
        }
    }

    static float PoseDelta(Transform root)
    {
        float best = 0f;
        foreach (var bone in root.GetComponentsInChildren<Transform>(true))
        {
            if (bone.name != "Hand.L" && bone.name != "Upper_Arm.L" && bone.name != "Hand.R") continue;
            best = Mathf.Max(best, Quaternion.Angle(Quaternion.identity, bone.localRotation));
        }
        return best;
    }

    static float MinDepth(Camera camera, GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
        if (renderers.Length == 0) return -999f;
        float min = float.PositiveInfinity;
        foreach (var renderer in renderers)
        {
            var bounds = renderer.bounds;
            foreach (var corner in new[]
            {
                bounds.min,
                bounds.max,
                new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.max.x, bounds.max.y, bounds.min.z)
            })
                min = Mathf.Min(min, camera.transform.InverseTransformPoint(corner).z);
        }
        return min;
    }

    static float Depth(Camera camera, GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
        if (renderers.Length == 0) return -999f;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return camera.transform.InverseTransformPoint(bounds.center).z;
    }

    static string FlagDepth(float depth) => depth < 0.05f || depth > 4f ? "DEPTH_BAD" : "DEPTH_OK";

    static Transform FindMuzzle(Transform root)
    {
        foreach (var item in root.GetComponentsInChildren<Transform>(true))
            if (item.name.ToLowerInvariant().Contains("muzzle")) return item;
        return null;
    }

    static float SophSource(float elapsed)
    {
        if (elapsed < 1.65556f) return elapsed * 1.5f;
        if (elapsed < 4.65556f) return 2.48333f + elapsed - 1.65556f;
        return 5.48333f + (elapsed - 4.65556f) * 1.5f;
    }

    static float EliSource(float elapsed)
    {
        if (elapsed < 1.83333f) return elapsed * 1.5f;
        return 2.75f + (elapsed - 1.83333f) * 2.8f;
    }
}
#endif
