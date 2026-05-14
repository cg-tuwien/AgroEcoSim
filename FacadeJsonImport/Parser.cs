using System.Text.Json;

namespace FacadeJsonImport;

public static class Parser
{
    const string Example = @"
[
  [
    {
      ""x"": 0,
      ""value"": """",
      ""color"": ""#f3f3f3""
    },
  ]
]";

    public static void Read(string input = Example)=> Read(JsonSerializer.Deserialize<CellModel[][]>(input));

    public static void Read(CellModel[][] input)
    {

    }
}