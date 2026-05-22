using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0f, y = 0f;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return new Vector2(x, y);

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                string? propertyName = reader.GetString();

                reader.Read();

                switch (propertyName?.ToLowerInvariant())
                {
                    case "x":
                        x = reader.GetSingle();
                        break;

                    case "y":
                        y = reader.GetSingle();
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException();
        }

        // Optional: also support compact array format: [x, y]
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            var x = reader.GetSingle();

            reader.Read();
            var y = reader.GetSingle();

            reader.Read();

            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException();

            return new Vector2(x, y);
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);

        writer.WriteEndObject();
    }
}

public sealed class Vector3JsonConverter : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0f, y = 0f, z = 0f;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                    return new Vector3(x, y, z);

                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException();

                string? propertyName = reader.GetString();

                reader.Read();

                switch (propertyName?.ToLowerInvariant())
                {
                    case "x":
                        x = reader.GetSingle();
                        break;

                    case "y":
                        y = reader.GetSingle();
                        break;

                    case "z":
                        z = reader.GetSingle();
                        break;

                    default:
                        reader.Skip();
                        break;
                }
            }

            throw new JsonException();
        }

        // Optional: also support compact array format: [x, y, z]
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            reader.Read();
            var x = reader.GetSingle();

            reader.Read();
            var y = reader.GetSingle();

            reader.Read();
            var z = reader.GetSingle();

            reader.Read();

            if (reader.TokenType != JsonTokenType.EndArray)
                throw new JsonException();

            return new Vector3(x, y, z);
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);

        writer.WriteEndObject();
    }
}