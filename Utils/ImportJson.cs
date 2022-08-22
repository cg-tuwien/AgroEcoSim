#if !GODOT
using System.IO;
using System.Text.Json;

namespace Utils;

public static class Import
{
    public static T Json<T>(string input) => JsonSerializer.Deserialize<T>(input);
    public static T JsonFile<T>(string fileName) => JsonSerializer.Deserialize<T>(File.ReadAllText(fileName));
}
#endif