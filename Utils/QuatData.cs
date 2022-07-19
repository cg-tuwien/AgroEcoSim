using System.Numerics;
namespace Utils;

///<summary>
/// Helper struct for JSON serialization of quaternions
///<summary>
public readonly struct QuatData
{
    public readonly float X { get; }
    public readonly float Y { get; }
    public readonly float Z { get; }
    public readonly float W { get; }

    public QuatData(Quaternion q)
    {
        X = q.X;
        Y = q.Y;
        Z = q.Z;
        W = q.W;
    }
}