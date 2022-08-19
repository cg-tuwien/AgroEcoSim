using System;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

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

public class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Vector3();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                return result;
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();
                switch (propertyName)
                {
                    case "X": result.X = reader.GetSingle(); break;
                    case "Y": result.Y = reader.GetSingle(); break;
                    case "Z": result.Z = reader.GetSingle(); break;
                }
            }
        }
        return result;
    }


    public override void Write(Utf8JsonWriter writer, Vector3 v, JsonSerializerOptions options) =>
        writer.WriteStringValue(JsonSerializer.Serialize(new Vector3Data(v), options));
}