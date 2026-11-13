using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace GodotFrontend.Core.Net;

/// <summary>
/// The two serializer configurations: what comes off the wire (camelCase, read case-insensitively, enums as
/// numbers or strings) and what goes into a request (PascalCase property names, and only the members the server
/// can set - SpeciesSettings carries public getters for its morphology blobs that have internal setters, and the
/// web app never sends them).
/// </summary>
public static class Json
{
    public static readonly JsonSerializerOptions WireIn = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static readonly JsonSerializerOptions RequestOut = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { OnlySettableProperties } },
    };

    private static void OnlySettableProperties(JsonTypeInfo info)
    {
        if (info.Kind != JsonTypeInfoKind.Object) return;
        for (var i = info.Properties.Count - 1; i >= 0; --i)
            if (info.Properties[i].Set is null) info.Properties.RemoveAt(i);
    }
}
