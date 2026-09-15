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

        [Option("checkpoint-selftest", Required = false, HelpText =
            "Verify that resuming from a checkpoint is byte-identical to never stopping, and exit.")]
        public bool CheckpointSelfTest { get; set; }
    }

    /// <summary>
    /// Proves that a resumed simulation is the same simulation.
    ///
    /// This is the only test the checkpoint can have. Its correctness is not a property of any one
    /// field but of the whole set: miss one and the resumed world carries on plausibly, diverging
    /// from the run it claims to continue, and every frame after it decodes perfectly and is simply
    /// wrong. So the check is behavioural - run straight through, run again stopping and resuming,
    /// and compare the exported frames byte for byte.
    ///
    /// It runs several scenarios and several resume points because the state that goes missing
    /// tends to be conditional: a pending death only exists when a plant is pruning, flowering
    /// history only after a season boundary, the previous step's irradiance only once leaves exist.
    /// </summary>
    static int CheckpointSelfTest()
    {
        const byte flags = 2; //Analytics, matching what the server exports
        var failures = 0;

        foreach (var (steps, hoursPerTick, plants) in new[] { (40, 24, 4), (60, 12, 8), (90, 6, 2) })
        {
            SimulationRequest Request() => new()
            {
                HoursPerTick = hoursPerTick,
                TotalHours = hoursPerTick * steps,
                FieldSize = new Utils.Json.Vector3XDZ { X = 3, D = 1, Z = 3 },
                FieldResolution = 0.5f,
                Seed = 42,
                InitialDate = new DateTime(2026, 3, 15),
                Plants = Enumerable.Range(0, plants).Select(i => new PlantRequest
                {
                    SpeciesName = "Default",
                    Position = new Utils.Json.Vector3XYZ
                        { X = 0.4f + 0.3f * (i % 4), Y = -0.01f, Z = 0.4f + 0.3f * (i / 4) },
                }).ToArray(),
                Obstacles = [],
                RequestGeometry = true,
                RenderMode = 0,
                //Required: the parallel tick is not reproducible, so without this the comparison
                //would be measuring the thread pool rather than the checkpoint.
                Deterministic = true,
            };

            static AgroWorld Fresh(SimulationRequest request)
            {
                var world = Initialize.World(request);
                world.Deterministic = true;
                return world;
            }

            var straight = Fresh(Request());
            straight.Run((uint)steps);
            var expected = straight.ExportToStream(flags);

            foreach (var stop in new[] { 1, steps / 4, steps / 2, steps - 1 })
            {
                var first = Fresh(Request());
                first.Run((uint)stop);
                var checkpoint = first.SerializeState();

                var resumed = Fresh(Request());
                resumed.RestoreState(checkpoint);
                resumed.Run((uint)(steps - stop));
                var actual = resumed.ExportToStream(flags);

                var same = expected.AsSpan().SequenceEqual(actual);
                if (!same) ++failures;
                Console.WriteLine($"{steps} steps @{hoursPerTick}h, {plants} plants, resume at {stop,3}: "
                    + $"{(same ? "identical" : "DIFFERS")} (checkpoint {checkpoint.Length:N0} B)");
            }
        }

        Console.WriteLine(failures == 0
            ? "Checkpoint self-test passed: resuming is byte-identical to never stopping."
            : $"Checkpoint self-test FAILED in {failures} case(s).");
        return failures == 0 ? 0 : 1;
    }

    private static int Main(string[] args)
    {
        var exitCode = 0;
        Parser.Default.ParseArguments<Options>(args).WithParsed(options =>
        {
        if (options.CheckpointSelfTest) { exitCode = CheckpointSelfTest(); return; }

        AgroWorld world;
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
        world.Run((uint)world.TimestepsTotal());
        var stop = DateTime.UtcNow.Ticks;
        Console.WriteLine($"Simulation time: {(stop - start) / TimeSpan.TicksPerMillisecond} ms");

        if (options.ExportFile != null)
        {
            var plantData = new List<string>();
            world.ForEach(formation =>
            {
                if (formation is PlantFormation2 plant)
                    plantData.Add(@$"{{""P"":{JsonSerializer.Serialize(new Vector3Data(plant.Position))},""V"":{plant.AG.GetVolume()}}}");
            });

            File.WriteAllText(options.ExportFile, $"[{string.Join(",",plantData)}]");
        }

        Console.WriteLine($"RENDER TIME: {world.Irradiance.ElapsedMilliseconds} ms");
        });
        return exitCode;
    }
}