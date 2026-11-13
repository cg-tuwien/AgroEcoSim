namespace GodotFrontend.Core.Math;

/// <summary>
/// A 3x3 matrix stored as three column vectors. For a camera basis the columns are the camera's right (+X),
/// up (+Y) and back (+Z) axes in world space, exactly the first three columns of three's camera matrix,
/// and exactly the three column vectors Godot's <c>Basis(x, y, z)</c> constructor takes.
/// </summary>
public readonly record struct Basis3(Vec3 Col0, Vec3 Col1, Vec3 Col2)
{
    public static readonly Basis3 Identity = new(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

    /// <summary>Row-major rows, i.e. what Godot calls <c>Basis.Row0..2</c> and what the scene wire stores.</summary>
    public Vec3 Row0 => new(Col0.X, Col1.X, Col2.X);
    public Vec3 Row1 => new(Col0.Y, Col1.Y, Col2.Y);
    public Vec3 Row2 => new(Col0.Z, Col1.Z, Col2.Z);

    public Vec3 Transform(Vec3 v) => Col0 * v.X + Col1 * v.Y + Col2 * v.Z;

    public double Determinant => Vec3.Dot(Col0, Vec3.Cross(Col1, Col2));

    /// <summary>
    /// three's Matrix4.lookAt rotation part for a camera at <paramref name="eye"/> looking at <paramref name="target"/>:
    /// column 2 points from the target back to the eye, so the camera looks down its local -Z, as a Godot
    /// Camera3D does. Reproduces the two degenerate-case nudges of the original so a pole pose never throws.
    /// </summary>
    public static Basis3 LookAt(Vec3 eye, Vec3 target, Vec3 up)
    {
        var z = eye - target;
        if (z.LengthSq == 0) z = new Vec3(z.X, z.Y, 1); // eye and target are in the same position
        z = z.Normalized();
        var x = Vec3.Cross(up, z);
        if (x.LengthSq == 0)
        {
            // up and z are parallel
            if (System.Math.Abs(up.Z) == 1) z = new Vec3(z.X + 0.0001, z.Y, z.Z);
            else z = new Vec3(z.X, z.Y, z.Z + 0.0001);
            z = z.Normalized();
            x = Vec3.Cross(up, z);
        }
        x = x.Normalized();
        var y = Vec3.Cross(z, x);
        return new Basis3(x, y, z);
    }
}
