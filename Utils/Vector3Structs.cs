using System.Numerics;

namespace Utils.Json;

///<summary>
/// Helper struct for JSON serialization of vectors
///<summary>
public readonly struct Vector3Data
{
    public readonly float X { get; }
    public readonly float Y { get; }
    public readonly float Z { get; }

    public Vector3Data(Vector3 v)
    {
        X = v.X;
        Y = v.Y;
        Z = v.Z;
    }
}

///<summary>
/// Helper struct for JSON serialization of vectors
///<summary>
public struct Vector3XDZ
{
    public float X { get; set; }
    public float D { get; set; }
    public float Z { get; set; }

    public static implicit operator Vector3(Vector3XDZ input) => new(input.X, input.Z, input.D);
}