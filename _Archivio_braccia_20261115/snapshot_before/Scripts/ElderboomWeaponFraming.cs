using System;
using UnityEngine;

[Serializable]
public class ElderboomWeaponPose
{
    public Vector3 hipPosition;
    public Quaternion hipRotation = Quaternion.identity;
    public Vector3 adsPosition;
    public Quaternion adsRotation = Quaternion.identity;
    public float adsRelief = 0.22f;
    public bool hasAds;
}

public static class ElderboomWeaponFraming
{
    public struct Calibration
    {
        public string mesh;
        public Vector3 rear, front, hip;
        public float relief;
        public Vector3? hipRear, hipFront;
    }

    static readonly Calibration AkmnSoph = new Calibration { relief = 0.16f, hip = new Vector3(0.12f, -0.13f, 0.25f) };
    static readonly Calibration Sig550 = new Calibration
    {
        mesh = "output_unwrapped.001",
        rear = new Vector3(0.063f, 0.187f, 0.004f),
        front = new Vector3(-0.267f, 0.195f, 0.004f),
        relief = 0.18f,
        hip = new Vector3(0.12f, -0.13f, 0.25f)
    };
    static readonly Calibration Striker = new Calibration
    {
        mesh = "output_unwrapped.003",
        rear = new Vector3(-0.085f, 0.225f, 0f),
        front = new Vector3(-0.419f, 0.21241f, 0f),
        relief = 0.16f,
        hip = new Vector3(0.12f, -0.13f, 0.25f),
        hipRear = new Vector3(0.11f, 0.21081f, 0f),
        hipFront = new Vector3(-0.419f, 0.21241f, 0f)
    };
    static readonly Calibration Rhino = new Calibration
    {
        mesh = "Rhino 60DS",
        rear = new Vector3(0.041f, 0.15f, -0.0003f),
        front = new Vector3(-0.105f, 0.14781f, -0.0003f),
        relief = 0.29f,
        hip = new Vector3(0.13f, -0.14f, 0.42f)
    };
    static readonly Calibration M1911 = new Calibration
    {
        mesh = "1911_Rail",
        rear = new Vector3(0.076f, 0.046f, -0.00015f),
        front = new Vector3(-0.088f, 0.04119f, -0.000225f),
        relief = 0.28f,
        hip = new Vector3(0.13f, -0.14f, 0.42f)
    };

    public static bool TryCalibration(string weaponName, out Calibration calibration)
    {
        switch ((weaponName ?? "").Trim().ToUpperInvariant())
        {
            case "AKMN ZENITCO":
            case ElderboomIds.Soph:
                calibration = AkmnSoph;
                return true;
            case "SIG 550":
            case ElderboomIds.Eli:
                calibration = Sig550;
                return true;
            case "STRIKER-12":
            case ElderboomIds.Tactical:
                calibration = Striker;
                return true;
            case "RHINO 60DS":
            case "RHINO":
                calibration = Rhino;
                return true;
            case "1911":
            case "M1911":
                calibration = M1911;
                return true;
            default:
                calibration = default;
                return false;
        }
    }

    public static Transform FindNamed(Transform root, params string[] names)
    {
        if (root == null) return null;
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            foreach (var name in names)
            {
                if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase)
                    || node.name.Replace('_', '.') == name
                    || node.name.Replace('.', '_') == name)
                    return node;
            }
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
            foreach (var name in names)
                if (!string.IsNullOrEmpty(name) && node.name.IndexOf(name.Replace('.', '_'), StringComparison.OrdinalIgnoreCase) >= 0)
                    return node;
        return null;
    }

    public static bool AlignAuthoredCamera(Transform model)
    {
        if (model == null) return false;
        Transform authored = null;
        foreach (var camera in model.GetComponentsInChildren<Camera>(true))
        {
            authored = camera.transform;
            break;
        }
        if (authored == null)
        {
            foreach (var node in model.GetComponentsInChildren<Transform>(true))
            {
                if (node == model) continue;
                if (node.name == "Camera" || node.name == "Camera3D" || node.name == "Camera_FPS")
                {
                    authored = node;
                    break;
                }
            }
        }
        if (authored == null) return false;
        Vector3 localPoint = model.InverseTransformPoint(authored.position);
        Quaternion localRotation = Quaternion.Inverse(model.rotation) * authored.rotation;
        Quaternion aligned = Quaternion.Inverse(localRotation);
        model.localRotation = aligned;
        model.localPosition = -(aligned * Vector3.Scale(model.localScale, localPoint));
        return true;
    }

    public static Transform EnsureMarker(Transform parent, string name, Vector3 localPosition)
    {
        var marker = FindNamed(parent, name);
        if (marker == null)
        {
            marker = new GameObject(name).transform;
            marker.SetParent(parent, false);
        }
        marker.localPosition = localPosition;
        marker.localRotation = Quaternion.identity;
        return marker;
    }

    public static void HideNonViewmodelBodies(Transform root)
    {
        foreach (var node in root.GetComponentsInChildren<Transform>(true))
        {
            string name = node.name;
            if (name == "P_Body" || name == "P_head" || name == "P_feet" || name == "b13")
                foreach (var renderer in node.GetComponents<Renderer>())
                    renderer.enabled = false;
        }
    }

    public static bool SolveSightPose(Transform root, Transform rear, Transform front, Vector3 cameraPoint, out Vector3 localPosition, out Quaternion localRotation)
    {
        localPosition = root.localPosition;
        localRotation = root.localRotation;
        if (root == null || rear == null) return false;
        Vector3 rearLocal = root.InverseTransformPoint(rear.position);
        Vector3 axis = Vector3.forward;
        if (front != null)
        {
            Vector3 frontLocal = root.InverseTransformPoint(front.position);
            Vector3 delta = frontLocal - rearLocal;
            if (delta.sqrMagnitude > 1e-8f) axis = delta.normalized;
        }
        // Optional "<..>ADS_Up" marker next to the rear sight: keeps the weapon level instead of the root's own roll.
        Vector3 upLocal = Vector3.up;
        var upMarker = rear.parent != null ? rear.parent.Find(rear.name.Replace("Rear", "Up")) : null;
        if (upMarker != null && upMarker != rear)
        {
            Vector3 u = root.InverseTransformPoint(upMarker.position) - rearLocal;
            if (Vector3.Cross(u, axis).sqrMagnitude > 1e-10f) upLocal = u;
        }
        Quaternion sight = Quaternion.LookRotation(axis, upLocal);
        localRotation = Quaternion.Inverse(sight);
        localPosition = cameraPoint - localRotation * rearLocal;
        return true;
    }

    public static ElderboomWeaponPose FromViewmodel(ElderboomViewmodel viewmodel, Transform root)
    {
        var pose = new ElderboomWeaponPose();
        if (viewmodel != null && viewmodel.framingStored)
        {
            pose.hipPosition = viewmodel.hipPosition;
            pose.hipRotation = viewmodel.hipRotation;
            pose.adsPosition = viewmodel.adsPosition;
            pose.adsRotation = viewmodel.adsRotation;
            pose.adsRelief = viewmodel.adsRelief;
            pose.hasAds = viewmodel.hasAds;
            return pose;
        }
        pose.hipPosition = root != null ? root.localPosition : Vector3.zero;
        pose.hipRotation = root != null ? root.localRotation : Quaternion.identity;
        pose.adsPosition = pose.hipPosition;
        pose.adsRotation = pose.hipRotation;
        return pose;
    }

    public static void Store(ElderboomViewmodel viewmodel, ElderboomWeaponPose pose)
    {
        if (viewmodel == null) return;
        viewmodel.hipPosition = pose.hipPosition;
        viewmodel.hipRotation = pose.hipRotation;
        viewmodel.adsPosition = pose.adsPosition;
        viewmodel.adsRotation = pose.adsRotation;
        viewmodel.adsRelief = pose.adsRelief;
        viewmodel.hasAds = pose.hasAds;
        viewmodel.framingStored = true;
    }

    public static Vector3 ShopPosition(string id)
    {
        switch (id)
        {
            case "service": return new Vector3(0f, -0.22f, 0.28f);
            case "revolver": return new Vector3(0.08f, -0.18f, 0.32f);
            case "wrench": return new Vector3(0.18f, -0.20f, 0.32f);
            case "crowbar": return new Vector3(0.08f, -0.18f, 0.34f);
            case "sawnoff": return new Vector3(0.07f, -0.20f, 0.36f);
            case "shotgun": return new Vector3(0.06f, -0.18f, 0.38f);
            case "grenade":
            case "minigun":
            case "flame": return new Vector3(0.06f, -0.20f, 0.40f);
            default: return new Vector3(0.06f, -0.18f, 0.38f);
        }
    }

    public static Vector3 ShopEuler(string id)
    {
        if (id == "wrench") return new Vector3(8f, 0f, -12f);
        return Vector3.zero;
    }

    public static float ShopScale(string id)
    {
        switch (id)
        {
            case "service": return 0.45f;
            case "wrench": return 0.40f;
            default: return 0.68f;
        }
    }

    public static Vector3 TargetBoundsCenter(string category)
    {
        if (category == ElderboomIds.Category.Handgun) return new Vector3(0.11f, -0.15f, 0.36f);
        if (category == ElderboomIds.Category.Melee) return new Vector3(0.16f, -0.18f, 0.32f);
        if (category == ElderboomIds.Category.Shotgun) return new Vector3(0.10f, -0.16f, 0.42f);
        return new Vector3(0.09f, -0.15f, 0.44f);
    }
}
