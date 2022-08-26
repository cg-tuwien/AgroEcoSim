#if !GODOT

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Agro;
using CommandLine;
using Utils;
using Utils.Json;

internal class Program
{
    public class Options
    {
        [Option('i', "import", Required = false, HelpText = "Import simulation settings from a json file.")]
        public string? ImportFile { get; set; }

        [Option('e', "export", Required = false, HelpText = "Export aggregated results to a json file.")]
        public string? ExportFile { get; set; }
    }

    private static void Main(string[] args) => Parser.Default.ParseArguments<Options>(args).WithParsed(options =>
    {
        AgentsSystem.SimulationWorld world;
        if (options.ImportFile != null)
        {
            if (!File.Exists(options.ImportFile))
                throw new FileNotFoundException("Simulation settings file not found.", options.ImportFile);
            var settings = Import.JsonFile<SimulationRequest>(options.ImportFile);
            world = Initialize.World(settings);
        }
        else
            world = Initialize.World();

        var start = DateTime.UtcNow.Ticks;
        world.Run(AgroWorld.TimestepsTotal);
        var stop = DateTime.UtcNow.Ticks;
        Console.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");
#if HISTORY_LOG || HISTORY_TICK
            //var exported = world.HistoryToJSON();
            //File.WriteAllText("export.json", exported.Replace("},", "},\n").Replace("],", "],\n"));
#endif
        if (options.ExportFile != null)
        {
            var plantData = new List<string>();
            world.ForEach(formation =>
            {
                if (formation is PlantFormation plant)
                    plantData.Add(@$"{{""P"":{JsonSerializer.Serialize(new Vector3Data(plant.Position))},""V"":{plant.AG.GetVolume()}}}");
            });

            File.WriteAllText(options.ExportFile, $"[{string.Join(",",plantData)}]");
        }

        Console.WriteLine($"RENDER TIME: {IrradianceClient.Singleton.SW.ElapsedMilliseconds} ms");
    });
}

#endif