using SchemeGenerator;

if (args.Length < 3)
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine(" SpeciesTsGenerator <SpeciesSettings.cs> <Species.generated.ts> <Species.generated.tsx>");
    return 1;
}

var inputPath = args[0];
var interfaceOutputPath = args[1];
var formOutputPath = args[2];

if (!File.Exists(inputPath))
    throw new FileNotFoundException($"The input file for Typescript generation does not exist: {inputPath}");

if (!Directory.Exists(Path.GetDirectoryName(interfaceOutputPath)))
    throw new FileNotFoundException($"The output directory for Typescript interface generation does not exist: {Path.GetDirectoryName(interfaceOutputPath)}");

if (!Directory.Exists(Path.GetDirectoryName(formOutputPath)))
    throw new FileNotFoundException($"The output directory for Typescript form generation does not exist: {Path.GetDirectoryName(formOutputPath)}");

Console.WriteLine($"Generating Typescript for {inputPath} and storing it to {interfaceOutputPath} and {formOutputPath}");

var sourceCode = File.ReadAllText(inputPath);

var classNode = Reader.ReadSpeciesSettingsClass(sourceCode);
var members = Reader.Extract(classNode);
var speciesInterface = Reader.GenerateInterface(members);
var speciesForm = Reader.GenerateForm(members);

WriteIfChanged(interfaceOutputPath, speciesInterface);
WriteIfChanged(formOutputPath, speciesForm);

return 0;

/// <summary>
/// Writes the file only when its content would change, and never leaves a partial one behind.
///
/// Both halves matter. Unchanged output must not be rewritten, or every build touches two files
/// that the frontend's watcher is watching and the whole bundle rebuilds for nothing.
///
/// And the write has to be atomic, because this program runs as a build step that MSBuild fires
/// once per target framework - two copies of it can be writing these exact two paths at the same
/// moment. WriteAllText truncates first, so the loser of that race leaves a half-written .ts that
/// compiles to nothing and a build error pointing at generated code nobody edited. Writing beside
/// the target and moving into place makes the switch a rename, which cannot be observed halfway.
/// </summary>
static void WriteIfChanged(string path, string content)
{
    if (File.Exists(path) && File.ReadAllText(path) == content) return;

    var temp = $"{path}.{Environment.ProcessId}.tmp";
    File.WriteAllText(temp, content);
    File.Move(temp, path, overwrite: true);
}