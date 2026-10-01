using System.Numerics;
using System.Text.Json;

namespace Festival.ContentAdapter;

public sealed record CleanupArmRig(Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist, float UpperLength, float ForeLength);
public sealed record CleanupRig(Vector3 Hip, float Height, CleanupArmRig Left, CleanupArmRig Right)
{
    public static CleanupRig Parse(string manifest, string variant)
    {
        using var json = JsonDocument.Parse(manifest); var data = json.RootElement.GetProperty("variants").GetProperty(variant);
        static Vector3 Vec(JsonElement a) => new(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
        CleanupArmRig Arm(string side)
        {
            var arm = data.GetProperty("joint_nodes").GetProperty(side);
            return new(Vec(arm.GetProperty("shoulder_godot")), Vec(arm.GetProperty("elbow_godot")),
                Vec(arm.GetProperty("hand_pivot_godot")), arm.GetProperty("upper_length").GetSingle(), arm.GetProperty("fore_length").GetSingle());
        }
        return new(Vec(data.GetProperty("hip_godot")), data.GetProperty("height_m").GetSingle(), Arm("Left"), Arm("Right"));
    }
}
public sealed record CleanupJointTransform(Vector3 Position, Quaternion Rotation);
public sealed record CleanupArmPose(CleanupJointTransform Upper, CleanupJointTransform Fore, CleanupJointTransform Hand, bool Reachable);
public sealed record CleanupPoseFrame(CleanupJointTransform Hip, CleanupArmPose Left, CleanupArmPose Right,
    CleanupJointTransform Picker, CleanupJointTransform Bag, Vector3 Jaw, Vector3 BagMouth);

// Designer's rigless two-segment reference, fed by a normalized authoritative action phase.
// Root, lower body and collision/navigation geometry are deliberately absent from the output.
public static class StewardCleanupKinematics
{
    private static float Ease(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }
    private static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        from = Vector3.Normalize(from); to = Vector3.Normalize(to); var dot = Math.Clamp(Vector3.Dot(from, to), -1, 1);
        if (dot > .999999f) return Quaternion.Identity;
        if (dot < -.999999f)
        {
            var axis = Vector3.Cross(from, Vector3.UnitX); if (axis.LengthSquared() < .00001f) axis = Vector3.Cross(from, Vector3.UnitZ);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        return Quaternion.Normalize(new Quaternion(Vector3.Cross(from, to), 1 + dot));
    }
    private static CleanupArmPose Arm(CleanupArmRig rig, bool left, Vector3 hip, Quaternion lean, CleanupJointTransform hand)
    {
        var shoulder = hip + Vector3.Transform(rig.Shoulder - hip, lean); var delta = hand.Position - shoulder;
        var distance = delta.Length(); var axis = Vector3.Normalize(delta);
        var min = Math.Abs(rig.UpperLength - rig.ForeLength) + .0001f; var max = rig.UpperLength + rig.ForeLength - .0001f;
        var reachable = distance > min && distance < max; var d = Math.Clamp(distance, min, max);
        var pole = new Vector3(left ? -.2f : .2f, -.2f, -1); pole = Vector3.Normalize(pole - axis * Vector3.Dot(pole, axis));
        var along = (rig.UpperLength * rig.UpperLength - rig.ForeLength * rig.ForeLength + d * d) / (2 * d);
        var elbow = shoulder + axis * along + pole * MathF.Sqrt(Math.Max(0, rig.UpperLength * rig.UpperLength - along * along));
        return new(new(shoulder, FromTo(rig.Elbow - rig.Shoulder, elbow - shoulder)),
            new(elbow, FromTo(rig.Wrist - rig.Elbow, hand.Position - elbow)), hand, reachable);
    }
    public static CleanupPoseFrame Evaluate(CleanupRig rig, double phase, Vector3 contact)
    {
        var female = rig.Height < 1.75f;
        var bagGrip = new Vector3(female ? -.26f : -.28f, female ? .89f : .93f, -.07f);
        var mouth = bagGrip + new Vector3(-.175f, 0, 0);
        var ready = new Vector3(female ? .22f : .24f, female ? .91f : .95f, -.1f);
        var reach = new Vector3(female ? .17f : .18f, 0, -.14f - .25f * new Vector2(contact.X, contact.Z).Length());
        var horizontal = new Vector2(contact.X - reach.X, contact.Z - reach.Z).Length();
        reach.Y = contact.Y + MathF.Sqrt(Math.Max(.01f, .8f * .8f - horizontal * horizontal));
        var transfer = new Vector3(female ? .24f : .26f, 0, -.34f);
        horizontal = new Vector2(mouth.X - transfer.X, mouth.Z - transfer.Z).Length();
        transfer.Y = mouth.Y + MathF.Sqrt(Math.Max(.01f, .8f * .8f - horizontal * horizontal));
        var readyDirection = Vector3.Normalize(new(.05f, -1, -.08f));
        Vector3 grip, direction; float bend;
        if (phase < 0) { grip = ready; direction = readyDirection; bend = 0; }
        else if (phase <= .5)
        {
            var t = Ease((float)(phase / .45)); grip = Vector3.Lerp(ready, reach, t);
            direction = Vector3.Normalize(Vector3.Lerp(readyDirection, Vector3.Normalize(contact - reach), t)); bend = MathF.PI * 32 / 180 * t;
        }
        else
        {
            var t = Ease((float)((phase - .5) / .35)); grip = Vector3.Lerp(reach, transfer, t);
            direction = Vector3.Normalize(Vector3.Lerp(Vector3.Normalize(contact - reach), Vector3.Normalize(mouth - transfer), t)); bend = MathF.PI * 32 / 180 * (1 - t);
        }
        var lean = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -bend); var tool = FromTo(-Vector3.UnitY, direction);
        var rightHand = new CleanupJointTransform(grip + Vector3.Transform(new(0, .044f, 0), tool), tool);
        var leftHand = new CleanupJointTransform(bagGrip + new Vector3(0, .044f, 0), Quaternion.Identity);
        return new(new(rig.Hip, lean), Arm(rig.Left, true, rig.Hip, lean, leftHand), Arm(rig.Right, false, rig.Hip, lean, rightHand),
            new(grip, tool), new(bagGrip, Quaternion.Identity), grip + direction * .8f, mouth);
    }
}
