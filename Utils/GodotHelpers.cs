#if GODOT
namespace NumericHelpers;

public static class NumericExtensionsForGodot
{
    public static Godot.Vector3 ToGodot(this System.Numerics.Vector3 input) => new(input.X, input.Y, input.Z);
    public static Godot.Quat ToGodot(this System.Numerics.Quaternion input) => new(input.X, input.Y, input.Z, input.W);
}
#endif