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

File.WriteAllText(interfaceOutputPath, speciesInterface);
File.WriteAllText(formOutputPath, speciesForm);

return 0;