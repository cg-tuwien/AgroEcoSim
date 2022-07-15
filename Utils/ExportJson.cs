using System.Text.Json;

namespace Utils;

public static class Export
{
    // static JsonSerializerOptions Options;
    // static Export()
    // {
    //     Options.
    // }
    public static string Json<T>(T input) => JsonSerializer.Serialize(input);
}