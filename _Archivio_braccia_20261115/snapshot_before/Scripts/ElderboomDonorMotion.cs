using System;
using System.Collections.Generic;
using UnityEngine;

// Retargets sampled joint positions. The operator's bind lengths and skin are kept,
// including rigs whose forearms and fingers are siblings in the imported hierarchy.
public sealed class ElderboomDonorMotion
{
    const int SidePoints = 23;
    const int JointValues = SidePoints * 6;

    [Serializable] class Library { public Pack[] sets; }
    [Serializable] public class Pack
    {
        public string weapon;
        public string[] parts;
        public Motion[] motions;
        public Motion Find(string role) => Array.Find(motions, m => m.role == role);
    }
    [Serializable] public class Motion
    {
        public string role, source;
        public float duration;
        public int count;
        public float[] samples;
    }

    static Library library;
    public static Pack ForWeapon(string id)
    {
        switch (id)
        {
            case "akmn": id = "ak74"; break;
            case "mp5vk":
            case "m4":
            case "hk53": id = "mp5"; break;
            case "glock19":
            case "p45": id = "m1911"; break;
        }
        if (library == null)
        {
            var resource = Resources.Load<TextAsset>("Elderboom/DonorWeaponMotions");
            if (resource == null) return null;
            library = JsonUtility.FromJson<Library>(resource.text);
        }
        return Array.Find(library.sets, p => p.weapon == id);
    }

    class Joint
    {
        public Transform node;
        public Vector3 bindPosition, position, displayedPosition;
        public Quaternion bindRotation, rotation, displayedRotation;
        public int depth;
    }
    class Arm
    {
        public Joint[] upper, fore, palm, fingers;
        public Joint hand;
        public Vector3 shoulder;
    }
    class Part
    {
        public Transform node;
        public Vector3 position;
        public Quaternion rotation;
        public Pose donorRest;
        public int index;
    }

    readonly Transform root;
    readonly Pack pack;
    readonly Arm[] arms = new Arm[2];
    readonly List<Joint> joints = new List<Joint>();
    readonly List<Part> parts = new List<Part>();
    readonly Vector3[] sample;
    readonly Vector3 rightAnchor;
    readonly float scale;
    readonly Vector3 origin;
    Quaternion alignment = Quaternion.identity;
    bool supportBound;
    Pose supportRest, donorSupportRest;
    Vector3 supportDelta;
    Vector3 rightWrist;
    Quaternion rightFrame;
    public Motion Idle { get; }
    public Motion Find(string role) => pack.Find(role);

    ElderboomDonorMotion(Transform root, Transform model, Pack pack, string operatorId, bool handgun)
    {
        this.root = root;
        this.pack = pack;
        Idle = pack.Find("idle");
        sample = new Vector3[2 * SidePoints + 3 * pack.parts.Length];
        var nodes = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in model.GetComponentsInChildren<Transform>(true))
            nodes.TryAdd(node.name.Replace('.', '_'), node);
        for (int s = 0; s < arms.Length; s++)
            arms[s] = BindArm(nodes, operatorId, s == 0 ? "L" : "R");

        joints.Sort((a, b) => a.depth.CompareTo(b.depth));
        foreach (var arm in arms)
            if (arm == null) return;
        float length = (Length(arms[0]) + Length(arms[1])) * .5f;
        scale = length * .88f;
        float depth = Mathf.Min(.37f, length * .82f - .025f);
        rightAnchor = handgun ? new Vector3(.12f, -.15f, depth) : new Vector3(.18f, -.19f, depth);
        Read(Idle, 0f);
        origin = sample[SidePoints + 2];
        foreach (var arm in arms)
        {
            // Set shoulders behind the eye, outside the central view. The source
            // supplies the elbow pole and the wrist target on each frame.
            float sign = arm == arms[0] ? -1f : 1f;
            arm.shoulder = new Vector3(sign * Mathf.Min(.22f, length * .48f), -.24f, -.025f);
        }
    }

    public static ElderboomDonorMotion Create(Transform root, Transform model, Pack pack, string operatorId, bool handgun)
    {
        var motion = new ElderboomDonorMotion(root, model, pack, operatorId, handgun);
        return motion.arms[0] != null && motion.arms[1] != null && motion.scale > .01f ? motion : null;
    }

    Arm BindArm(Dictionary<string, Transform> nodes, string id, string side)
    {
        string[] upper, fore, palm, fingers;
        string hand;
        // Unified arms rig (all operators' Grips models since Nov 2026): Rigify-style DEF- names on every operator.
        bool unified = nodes.ContainsKey("DEF-hand_" + side) && nodes.ContainsKey("DEF-f_index_01_" + side);
        if (!unified && id == ElderboomIds.Tactical)
        {
            upper = new[] { "Upper_Arm", "Upper_Arm.001" };
            fore = new[] { "Bone.002", "Bone.003", "ForeArm" };
            hand = "Hand";
            palm = new[] { "Bone.005", "Bone.013", "Bone.017", "Bone.021" };
            fingers = new[] { "Bone.023", "Bone.022", "Bone.024", "Middle_Finger_1", "Middle_Finger_2", "Middle_Finger_3",
                "Bone.015", "Bone.014", "Bone.016", "Pinky1", "Pinky2", "Pinky3", "Bone.009", "Bone.011", "Bone.010" };
        }
        else
        {
            bool soph = unified || id == ElderboomIds.Soph;
            upper = soph ? new[] { "DEF-upper_arm", "DEF-upper_arm." + side + ".001" } : new[] { "DEF_upper_arm" };
            fore = soph ? new[] { "DEF-forearm", "DEF-forearm." + side + ".001" } : new[] { "DEF_forearm" };
            hand = soph ? "DEF-hand" : "DEF_hand";
            palm = soph ? new[] { "DEF-palm.01", "DEF-palm.02", "DEF-palm.03", "DEF-palm.04" } : Array.Empty<string>();
            fingers = new string[15];
            var names = new[] { "index", "middle", "ring", soph ? "pinky" : "little", "thumb" };
            for (int i = 0; i < fingers.Length; i++)
            {
                string digit = names[i / 3];
                fingers[i] = soph ? "DEF-" + (digit == "thumb" ? digit : "f_" + digit) + ".0" + (i % 3 + 1)
                                  : "DEF_" + digit + "_0" + (i % 3 + 1);
            }
        }

        Joint Bind(string name)
        {
            if (!name.EndsWith("." + side + ".001", StringComparison.Ordinal)) name += "." + side;
            if (!nodes.TryGetValue(name.Replace('.', '_'), out var node)) return null;
            var joint = new Joint { node = node, bindPosition = root.InverseTransformPoint(node.position),
                bindRotation = Quaternion.Inverse(root.rotation) * node.rotation };
            for (var parent = node.parent; parent != null; parent = parent.parent) joint.depth++;
            joints.Add(joint);
            return joint;
        }

        var arm = new Arm { upper = Array.ConvertAll(upper, Bind), fore = Array.ConvertAll(fore, Bind),
            palm = Array.ConvertAll(palm, Bind), hand = Bind(hand), fingers = Array.ConvertAll(fingers, Bind) };
        if (arm.hand == null || arm.upper[0] == null || arm.fore[0] == null || Array.Exists(arm.fingers, f => f == null)) return null;
        return arm;
    }

    static float Length(Arm arm) => Vector3.Distance(arm.upper[0].bindPosition, arm.fore[0].bindPosition)
                                 + Vector3.Distance(arm.fore[0].bindPosition, arm.hand.bindPosition);

    static Quaternion HandFrame(Vector3 wrist, Vector3 index, Vector3 middle, Vector3 pinky)
    {
        var across = (index - pinky).normalized;
        var along = (middle - wrist).normalized;
        var normal = Vector3.Cross(across, along).normalized;
        return Quaternion.LookRotation(normal, Vector3.Cross(normal, across));
    }

    Pose DonorHand(int offset) => new Pose(sample[offset + 2],
        HandFrame(sample[offset + 2], sample[offset + 3], sample[offset + 6], sample[offset + 12]));

    void Read(Motion motion, float time)
    {
        float index = Mathf.Clamp01(time / Mathf.Max(.001f, motion.duration)) * (motion.count - 1);
        int frame = Mathf.Min((int)index, motion.count - 2);
        float f = index - frame;
        int stride = JointValues + pack.parts.Length * 9;
        for (int i = 0; i < sample.Length; i++)
        {
            int a = frame * stride + i * 3;
            int b = a + stride;
            sample[i] = new Vector3(
                Mathf.Lerp(motion.samples[a], motion.samples[b], f),
                Mathf.Lerp(motion.samples[a + 1], motion.samples[b + 1], f),
                Mathf.Lerp(motion.samples[a + 2], motion.samples[b + 2], f));
        }
    }

    // Align the source's hold using the actual gun's sight axis, then rebuild both arms.
    public void Align(Vector3 axis, Vector3 up)
    {
        if (axis.sqrMagnitude < .0001f || up.sqrMagnitude < .0001f) return;
        alignment = Quaternion.Euler(2f, -3f, 0f) * Quaternion.Inverse(Quaternion.LookRotation(axis, up));
    }

    void Put(Joint joint, Vector3 position, Quaternion delta)
    {
        if (joint == null) return;
        joint.position = position;
        joint.rotation = delta * joint.bindRotation;
    }

    static Vector3 Bend(Vector3 shoulder, Vector3 wrist, Vector3 pole, float a, float b, out Vector3 reached)
    {
        Vector3 offset = wrist - shoulder;
        Vector3 direction = offset.sqrMagnitude > .00001f ? offset.normalized : Vector3.forward;
        float length = Mathf.Clamp(offset.magnitude, Mathf.Abs(a - b) + .001f, (a + b) * .998f);
        float distance = (a * a - b * b + length * length) / (2f * length);
        Vector3 bend = Vector3.ProjectOnPlane(pole - shoulder, direction);
        if (bend.sqrMagnitude < .00001f) bend = Vector3.ProjectOnPlane(Vector3.down, direction);
        reached = shoulder + direction * length;
        return shoulder + direction * distance + bend.normalized * Mathf.Sqrt(Mathf.Max(0f, a * a - distance * distance));
    }

    public void BindSupport(Transform marker)
    {
        Read(Idle, 0f);
        var left = DonorHand(0);
        var right = DonorHand(SidePoints);
        var inverse = Quaternion.Inverse(rightFrame);
        supportRest = new Pose(inverse * (root.InverseTransformPoint(marker.position) - rightWrist),
            inverse * Quaternion.Inverse(root.rotation) * marker.rotation);
        donorSupportRest = new Pose(Quaternion.Inverse(right.rotation) * (left.position - right.position),
            Quaternion.Inverse(right.rotation) * left.rotation);
        supportDelta = supportRest.position - donorSupportRest.position * scale;
        // Different operator proportions and gun lengths can put the reference
        // wrist outside reach. Reposition the shoulder below the camera before
        // solving the limb so the hand can actually make contact.
        var arm = arms[0];
        var contact = rightWrist + rightFrame * supportRest.position;
        float limit = Length(arm) * .92f;
        if (Vector3.Distance(contact, arm.shoulder) > limit)
        {
            float x = Mathf.Lerp(arm.shoulder.x, contact.x, .48f);
            float y = Mathf.Min(-.32f, arm.shoulder.y);
            float spare = limit * limit - (x - contact.x) * (x - contact.x) - (y - contact.y) * (y - contact.y);
            arm.shoulder = new Vector3(x, y, Mathf.Max(arm.shoulder.z, contact.z - Mathf.Sqrt(Mathf.Max(.005f, spare))));
        }
        supportBound = true;
    }

    static Quaternion Twist(Quaternion rotation, Vector3 axis)
    {
        var projection = Vector3.Project(new Vector3(rotation.x, rotation.y, rotation.z), axis);
        var twist = new Quaternion(projection.x, projection.y, projection.z, rotation.w);
        return Quaternion.Normalize(twist);
    }

    void PoseArm(Arm arm, int offset, string role)
    {
        Vector3 P(int i) => rightAnchor + alignment * (sample[offset + i] - origin) * scale;
        var sh = arm.upper[0].bindPosition;
        var el = arm.fore[0].bindPosition;
        var wr = arm.hand.bindPosition;
        var donor = DonorHand(offset);
        var target = P(2);
        var frame = alignment * donor.rotation;
        if (offset == 0 && supportBound)
        {
            var right = DonorHand(SidePoints);
            var relative = Quaternion.Inverse(right.rotation) * (donor.position - right.position);
            var departure = Vector3.Distance(relative, donorSupportRest.position);
            float contact = role == "reload" || role == "empty" || role == "inspect"
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.08f, .4f, departure)) : 1f;
            target = rightWrist + rightFrame * (relative * scale + supportDelta * contact);
            var correction = rightFrame * supportRest.rotation * Quaternion.Inverse(donorSupportRest.rotation) * Quaternion.Inverse(rightFrame);
            frame = Quaternion.Slerp(Quaternion.identity, correction, contact) * frame;
        }
        var pole = Vector3.Lerp(P(1), arm.shoulder + new Vector3(offset == 0 ? -.16f : .16f, -.4f, .18f), .57f);
        var elbow = Bend(arm.shoulder, target, pole, Vector3.Distance(sh, el), Vector3.Distance(el, wr), out var wrist);
        var upper = Quaternion.FromToRotation(el - sh, elbow - arm.shoulder);
        var fore = Quaternion.FromToRotation(wr - el, wrist - elbow);
        var hand = frame * Quaternion.Inverse(HandFrame(wr, arm.fingers[0].bindPosition, arm.fingers[3].bindPosition, arm.fingers[9].bindPosition));
        var fingersBasis = frame * Quaternion.Inverse(donor.rotation);
        foreach (var joint in arm.upper)
            if (joint != null) Put(joint, arm.shoulder + upper * (joint.bindPosition - sh), upper);
        var twist = Twist(hand * Quaternion.Inverse(fore), wrist - elbow);
        for (int i = 0; i < arm.fore.Length; i++)
        {
            var joint = arm.fore[i];
            if (joint == null) continue;
            var turn = Quaternion.Slerp(Quaternion.identity, twist, (i + 1f) / (arm.fore.Length + 1)) * fore;
            Put(joint, elbow + fore * (joint.bindPosition - el), turn);
        }
        Put(arm.hand, wrist, hand);
        if (offset != 0) { rightFrame = frame; rightWrist = wrist; }
        foreach (var joint in arm.palm)
            if (joint != null) Put(joint, wrist + hand * (joint.bindPosition - wr), hand);

        for (int i = 0; i < arm.fingers.Length; i += 3)
        {
            var first = arm.fingers[i];
            var second = arm.fingers[i + 1];
            var third = arm.fingers[i + 2];
            var bindA = second.bindPosition - first.bindPosition;
            var bindB = third.bindPosition - second.bindPosition;
            var aimA = fingersBasis * (sample[offset + i + 4] - sample[offset + i + 3]);
            var aimB = fingersBasis * (sample[offset + i + 5] - sample[offset + i + 4]);
            var aimC = fingersBasis * (sample[offset + 18 + i / 3] - sample[offset + i + 5]);
            var turnA = Quaternion.FromToRotation(hand * bindA, aimA) * hand;
            var turnB = Quaternion.FromToRotation(hand * bindB, aimB) * hand;
            var turnC = aimC.sqrMagnitude < .000001f ? turnB : Quaternion.FromToRotation(hand * bindB, aimC) * hand;
            var knuckle = wrist + hand * (first.bindPosition - wr);
            var middle = knuckle + turnA * bindA;
            Put(first, knuckle, turnA);
            Put(second, middle, turnB);
            Put(third, middle + turnB * bindB, turnC);
        }
    }

    Pose PartPose(int index)
    {
        int i = SidePoints * 2 + index * 3;
        var hand = DonorHand(SidePoints);
        var local = Quaternion.Inverse(hand.rotation);
        return new Pose(local * (sample[i] - hand.position),
            local * Quaternion.LookRotation(sample[i + 2] - sample[i], sample[i + 1] - sample[i]));
    }

    // What a weapon node is to the motions: "magazine", "bolt", "pump", "shell", "cylinder", "ejector" or null.
    public static string PartRole(string name) => Role(name);
    static string Role(string name)
    {
        name = name.ToLowerInvariant();
        if (name.Contains("spare") || name.Contains("paddle") || name.Contains("stock") || name.Contains("well") || name.Contains("release")) return null;
        if (name.Contains("magazine") || name.Contains("caricatore") || name.Contains("_mag") || name == "mag") return "magazine";
        if (name.Contains("cylinder") || name.Contains("tamburo")) return "cylinder";
        if (name.Contains("ejector")) return "ejector";
        if (name.Contains("pump") || name == "foregrip") return "pump";
        if (name.Contains("bolt") || name.Contains("slide") || name == "carrello") return "bolt";
        if (name == "shell" || name == "cartuccia" || name == "slug") return "shell";
        return null;
    }

    public void BindParts(Transform holder)
    {
        Read(Idle, 0f);
        foreach (var node in holder.GetComponentsInChildren<Transform>(true))
        {
            int i = Array.IndexOf(pack.parts, Role(node.name));
            if (i < 0 || parts.Exists(p => node.IsChildOf(p.node))) continue;
            parts.Add(new Part { node = node, index = i, position = node.localPosition,
                rotation = node.localRotation, donorRest = PartPose(i) });
        }
    }

    public void Apply(Motion motion, float time, float blend = 1f)
    {
        Read(motion, time);
        PoseArm(arms[1], SidePoints, motion.role);
        PoseArm(arms[0], 0, motion.role);
        foreach (var joint in joints)
        {
            joint.displayedPosition = Vector3.Lerp(joint.displayedPosition, joint.position, blend);
            joint.displayedRotation = blend >= 1f ? joint.rotation : Quaternion.Slerp(joint.displayedRotation, joint.rotation, blend);
            joint.node.SetPositionAndRotation(root.TransformPoint(joint.displayedPosition), root.rotation * joint.displayedRotation);
        }

        var handFrame = alignment * DonorHand(SidePoints).rotation;
        foreach (var part in parts)
        {
            var value = PartPose(part.index);
            var space = Quaternion.Inverse(part.node.parent.rotation) * root.rotation * handFrame;
            var move = root.TransformVector(handFrame * (value.position - part.donorRest.position) * scale);
            var turn = space * value.rotation * Quaternion.Inverse(part.donorRest.rotation) * Quaternion.Inverse(space);
            if (motion == Idle || motion.role == "walk" || motion.role == "run" || motion.role == "equip")
            {
                part.node.localPosition = part.position;
                part.node.localRotation = part.rotation;
            }
            else
            {
                part.node.localPosition = part.position + part.node.parent.InverseTransformVector(move);
                part.node.localRotation = turn * part.rotation;
            }
        }
    }
}
