#if !GODOT
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Utils.Json;

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
#endif