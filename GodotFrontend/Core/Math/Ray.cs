namespace GodotFrontend.Core.Math;

/// <summary>A world-space ray with a unit direction. Port of render/core/Ray.ts.</summary>
public readonly record struct Ray(Vec3 Origin, Vec3 Direction)
{
    public Vec3 At(double t) => Origin + Direction * t;

    /// <summary>
    /// The ray through a normalised-device-coordinate point of a perspective camera, equivalent to three's
    /// <c>new Vector3(ndcX, ndcY, 1).unproject(camera)</c> followed by aiming from the camera position: the point
    /// on the far plane is built in view space from the vertical field of view and the aspect ratio, then
    /// carried to world space by the camera basis.
    /// </summary>
    public static Ray FromNdc(Vec3 cameraPosition, Basis3 cameraBasis, double fovYDegrees, double aspect, double far, double ndcX, double ndcY)
    {
        var tanHalf = System.Math.Tan(fovYDegrees * 0.5 * System.Math.PI / 180.0);
        var view = new Vec3(ndcX * tanHalf * aspect * far, ndcY * tanHalf * far, -far);
        var world = cameraPosition + cameraBasis.Transform(view);
        return new Ray(cameraPosition, (world - cameraPosition).Normalized());
    }

    /// <summary>Picking.ts toNDC: client-space pixel to normalised device coordinates over a rect.</summary>
    public static Vec2 ToNdc(double clientX, double clientY, double left, double top, double width, double height) =>
        new(((clientX - left) / width) * 2 - 1, 1 - ((clientY - top) / height) * 2);

    /// <summary>
    /// Intersects the horizontal plane at the given height (three's Ray.intersectPlane with a +Y plane).
    /// Null when the ray is parallel to the plane or points away from it.
    /// </summary>
    public Vec3? IntersectPlaneY(double y)
    {
        var denom = Direction.Y;
        if (System.Math.Abs(denom) < 1e-12) return null;
        var t = (y - Origin.Y) / denom;
        if (t < 0) return null;
        return At(t);
    }

    /// <summary>Intersects an arbitrary plane given by a point and a normal; null when parallel or behind.</summary>
    public Vec3? IntersectPlane(Vec3 point, Vec3 normal)
    {
        var denom = Vec3.Dot(Direction, normal);
        if (System.Math.Abs(denom) < 1e-12) return null;
        var t = Vec3.Dot(point - Origin, normal) / denom;
        if (t < 0) return null;
        return At(t);
    }

    /// <summary>Closest point on an infinite axis to the ray, for axis-constrained gizmo dragging.</summary>
    public Vec3? ClosestPointOnAxis(Vec3 origin, Vec3 axis)
    {
        var a = axis.Normalized();
        var w0 = Origin - origin;
        var b = Vec3.Dot(Direction, a);
        var denom = 1 - b * b;
        if (System.Math.Abs(denom) < 1e-12) return null;
        var d = Vec3.Dot(Direction, w0);
        var e = Vec3.Dot(a, w0);
        var t = (b * d - e) / denom;
        return origin + a * t;
    }

    /// <summary>Squared distance from a point to the ray (clamped to the ray's origin side).</summary>
    public double DistanceSqToPoint(Vec3 p)
    {
        var w = p - Origin;
        var t = Vec3.Dot(w, Direction);
        var closest = Origin + Direction * System.Math.Max(0, t);
        return (p - closest).LengthSq;
    }
}
