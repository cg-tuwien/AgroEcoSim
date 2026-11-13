using GodotFrontend.Core.Math;

namespace GodotFrontend.Core.Camera;

/// <summary>Camera paths for offline rendering; a port of render/offline/CameraPath.ts.</summary>
public interface ICameraPath
{
    CameraPose PoseAt(int frame, int total);
}

/// <summary>The interactive camera, snapshotted once when the job starts.</summary>
public sealed class StaticCamera : ICameraPath
{
    private readonly CameraPose pose;
    public StaticCamera(CameraPose pose) => this.pose = pose;
    public CameraPose PoseAt(int frame, int total) => pose;
}

/// <summary>Orbits the target, keeping the starting radius and elevation.</summary>
public sealed class TurntableCamera : ICameraPath
{
    private readonly CameraPose basePose;
    private readonly double turns, radius, elevation, startAzimuth;

    public TurntableCamera(CameraPose basePose, double turns = 1)
    {
        this.basePose = basePose;
        this.turns = turns;
        var off = basePose.Position - basePose.Target;
        radius = off.Length;
        // Spherical: elevation from the horizontal plane, azimuth around Y.
        elevation = System.Math.Asin(radius > 0 ? off.Y / radius : 0);
        startAzimuth = System.Math.Atan2(off.Z, off.X);
    }

    public CameraPose PoseAt(int frame, int total)
    {
        var t = total > 1 ? (double)frame / total : 0;
        var azimuth = startAzimuth + 2 * System.Math.PI * turns * t;
        var horizontal = radius * System.Math.Cos(elevation);
        var offset = new Vec3(
            horizontal * System.Math.Cos(azimuth),
            radius * System.Math.Sin(elevation),
            horizontal * System.Math.Sin(azimuth));
        return basePose with { Position = basePose.Target + offset };
    }
}
