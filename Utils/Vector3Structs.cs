using System.Numerics;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Utils.Json;

///<summary>
/// Helper struct for JSON serialization of vectors
///</summary>
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
///Helper struct for JSON serialization of vectors
///</summary>
public struct Vector3XYZ
{
    ///<summary>Coordinate along the right vector</summary>
    ///<example>0</example>
    public float X { get; set; }
    ///<summary>Coordinate along the up vector</summary>
    ///<example>0</example>
    public float Y { get; set;}
    ///<summary>Coordinate along the front vector</summary>
    ///<example>0</example>
    public float Z { get; set; }

   [M(MethodImplOptions.AggressiveInlining)] public static implicit operator Vector3(Vector3XYZ input) => new(input.X, input.Y, input.Z);
}

///<summary>
///Helper struct for JSON serialization of vectors
///</summary>
public struct Vector3XDZ
{
    ///<summary>Coordinate along the right vector</summary>
    ///<example>1</example>
    public float X { get; set; }
    ///<summary>Depth coordinate, i.e. along the negative up vector</summary>
    ///<example>1</example>
    public float D { get; set; }
    ///<summary>Coordinate along the front vector</summary>
    ///<example>1</example>
    public float Z { get; set; }

    [M(MethodImplOptions.AggressiveInlining)]public static implicit operator Vector3(Vector3XDZ input) => new(input.X, input.Z, input.D);
}