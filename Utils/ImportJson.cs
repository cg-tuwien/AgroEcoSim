using System.IO;
using System.Text.Json;

namespace Utils;

public static class Import
{
    static readonly JsonSerializerOptions Options = new() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };
    public static T Json<T>(string input) => JsonSerializer.Deserialize<T>(input, Options);
    public static T JsonFile<T>(string fileName) => JsonSerializer.Deserialize<T>(File.ReadAllText(fileName), Options);
}