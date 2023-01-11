using System.Numerics;

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

    public static implicit operator Vector3(Vector3XYZ input) => new(input.X, input.Y, input.Z);
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

    public static implicit operator Vector3(Vector3XDZ input) => new(input.X, input.Z, input.D);
}


/*
I'm trying to force Swashbuckle to show a proper example for a POST request in my `ApiController`. It has no parameters, just `[FromBody] SimulationRequest`. All primitive fields work fine, also top-level custom types but furtherdown the hierarchy for `System.Numerics.Vector3` won't show at all. I tried several approaches, even replaced it by a custom `struct`. Either it is a very specific bug, or I'm missing something in the settings.

```cs
public class SimulationRequest {
    ///<summary>
    ///Field size in meters. Note "D" is depth. (default: 1x1x1)
    ///</summary>
    public Utils.Json.Vector3XDZ? FieldSize { get; set; }

    //... some other properties ... all work well also with JsonPropertyName

    public ObstacleRequest[]? Plants { get; set; }
}

public class ObstacleRequest {
    //This works fine

    ///<summary>
    ///Height of the obstacle (default: 1)
    ///</summary>
    ///<example>1</example>
    [System.Text.Json.Serialization.JsonPropertyName("H")]
    public float? Height { get; set; }

    //This looks broken
    ///<summary>
    ///Position of the obstacle (OpenGL-like coordinates) (default: 0,0,0)
    ///</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(Utils.Json.Vector3JsonConverter))]
    [System.Text.Json.Serialization.JsonPropertyName("P")]
    public System.Numerics.Vector3 Position { get; set; }
}
```

The generated *Example Value* shows the following:
```
{
  "ticksPerHour": 1,
  "totalHours": 744,
  "fieldSize": {
    "x": 0,
    "d": 0,
    "z": 0
  },
  "fieldResolution": 0.5,
  "seed": 42,
  "plants": [
    {
      "P": {}
    }
  ],
  "obstacles": [
    {
      "T": "wall",
      "O": 0,
      "D": 0.1,
      "L": 1,
      "R": 0.5,
      "H": 1,
      "P": {}
    }
  ]
}
```


*/