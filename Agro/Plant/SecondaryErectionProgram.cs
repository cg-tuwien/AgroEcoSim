using System.Numerics;

namespace Agro;

/// <summary>
/// Provisional instantaneous ring-maturation algebra. StrainAmplitude is the
/// first sinusoidal free longitudinal strain amplitude, not peak-to-peak strain.
/// Circular equal-modulus rings give dK=4*epsilon*dR/R². This is not a Celtis fit.
/// </summary>
internal static class SecondaryErectionProgram
{
    internal readonly record struct Endpoint(double AreaM2, double CurvaturePathPerM, Quaternion Turn);

    internal static Vector3 FrozenDrive(Quaternion loaded, Quaternion parentLoaded,
        Quaternion material, float length, float proprioceptiveLength)
    {
        static bool Unit(Quaternion q) => float.IsFinite(q.LengthSquared()) && MathF.Abs(q.LengthSquared() - 1f) < .0001f;
        if (!(length > 0f) || !float.IsFinite(length) || !(proprioceptiveLength > 0f)
            || !float.IsFinite(proprioceptiveLength) || !Unit(loaded) || !Unit(parentLoaded) || !Unit(material))
            throw new InvalidDataException("Invalid reacting beam length or frame.");
        var axis = Vector3.Transform(Vector3.UnitX, loaded);
        var parent = Vector3.Transform(Vector3.UnitX, parentLoaded);
        var cross = Vector3.Cross(parent, axis); var sine = cross.Length();
        var curvature = sine > 1e-8f ? cross * (MathF.Atan2(sine, Math.Clamp(Vector3.Dot(parent, axis), -1f, 1f)) / (sine * length)) : Vector3.Zero;
        // Sine graviception plus local centerline decurving; a provisional 3D
        // extension of the planar AC control, with the drive frozen per trial.
        var world = Vector3.Cross(axis, Vector3.UnitY) - proprioceptiveLength * curvature;
        var local = Vector3.Transform(world, Quaternion.Conjugate(material));
        local.X = 0f; // Differential longitudinal strain cannot supply axial twist.
        var norm = local.Length();
        if (!float.IsFinite(norm)) throw new InvalidDataException("Nonfinite maturation drive.");
        return norm > 1f ? local / norm : local;
    }

    internal static Endpoint At(float radius0, float acceptedRadius, float reactingLength,
        float strainAmplitude, Vector3 frozenLocalDrive)
    {
        if (!(radius0 > 0f) || !float.IsFinite(radius0) || !float.IsFinite(acceptedRadius)
            || acceptedRadius < radius0 || !(reactingLength > 0f) || !float.IsFinite(reactingLength)
            || strainAmplitude < 0f || !float.IsFinite(strainAmplitude)
            || frozenLocalDrive.X != 0f || !float.IsFinite(frozenLocalDrive.LengthSquared()) || frozenLocalDrive.LengthSquared() > 1.00001f)
            throw new InvalidDataException("Invalid secondary material endpoint.");
        if (acceptedRadius == radius0) return new(0d, 0d, Quaternion.Identity);
        double delta = (double)acceptedRadius - radius0;
        double area = Math.PI * delta * ((double)acceptedRadius + radius0);
        double curvature = 4d * strainAmplitude * delta / ((double)radius0 * acceptedRadius);
        double magnitude = frozenLocalDrive.Length();
        double path = curvature * magnitude, angle = path * reactingLength;
        // A single quaternion cannot certify an aliased full-turn sweep. Refuse
        // an under-resolved request rather than clipping angle away from its wood.
        if (!double.IsFinite(area) || !double.IsFinite(angle) || angle >= Math.PI)
            throw new InvalidDataException("Secondary material step exceeds the unambiguous prototype sweep.");
        var turn = angle == 0d ? Quaternion.Identity
            : Quaternion.CreateFromAxisAngle(frozenLocalDrive / (float)magnitude, (float)angle);
        return new(area, path, turn);
    }

    internal static Quaternion Delta(Quaternion currentMaterial, Quaternion oldLocal, Quaternion nextLocal)
    {
        if (oldLocal == nextLocal) return Quaternion.Identity;
        return Quaternion.Normalize(currentMaterial * Quaternion.Conjugate(oldLocal) * nextLocal * Quaternion.Conjugate(currentMaterial));
    }
}
