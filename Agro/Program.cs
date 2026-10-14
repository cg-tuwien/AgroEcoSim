using Agro.Collisions;
using System.Diagnostics;
using System.Numerics;
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

        [Option("sheet-distance-selftest", Required = false, HelpText =
            "Compare the soft engine's sampled blade-to-blade distance against the exact query on random blade pairs, and exit.")]
        public bool SheetDistanceSelfTest { get; set; }
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

        // Every engine, because the contact state each one checkpoints is its own; the wall stands
        // across the stand so the mixed engine's solid pass has something to enforce.
        foreach (var engine in new[] { ContactEngineKind.Mixed, ContactEngineKind.Certified, ContactEngineKind.Soft })
        foreach (var (steps, hoursPerTick, plants) in new[] { (40, 24, 4), (60, 12, 8), (90, 6, 2) })
        {
            SimulationRequest Request() => new()
            {
                ContactEngine = engine,
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
                Obstacles = [new ObstacleRequest { Type = "wall", Length = 3f, Height = 0.5f, Thickness = 0.05f,
                    Position = new Utils.Json.Vector3XYZ { X = 1.5f, Y = 0f, Z = 0.6f } }],
                RequestGeometry = true,
                RenderMode = 0,
                //The parallel tick is reproducible; sequential only keeps this test's timing stable.
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
                Console.WriteLine($"{engine,-9} {steps} steps @{hoursPerTick}h, {plants} plants, resume at {stop,3}: "
                    + $"{(same ? "identical" : "DIFFERS")} (checkpoint {checkpoint.Length:N0} B)");
            }
        }

        Console.WriteLine(failures == 0
            ? "Checkpoint self-test passed: resuming is byte-identical to never stopping."
            : $"Checkpoint self-test FAILED in {failures} case(s).");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Measures the soft engine's sampled sheet distance against the exact one.
    ///
    /// The sampled query is an upper bound within an edge length times the crossing angle, and
    /// that bound is what the soft engine's banded response relies on. Random pairs of real leaf
    /// templates - lying along each other, crossing, offset - at the separations the engine
    /// actually asks about, with the error distribution and the fraction of pairs whose
    /// touching verdict differs, so a change to either query is caught here before it shows up
    /// as a leaf passing through another.
    /// </summary>
    static int SheetDistanceSelfTest()
    {
        var rng = new Random(42);
        var failures = 0;
        const float band = 0.0011f; // two blade radii plus the default skin
        foreach (var species in SpeciesSettings.Predefined)
        {
            var template = LaminaTemplates.For(species, OrganTypes.Leaf);
            if (template == null) continue;
            var length = species.LeafLength; var width = species.LeafRadius * 2f;
            double sumErr = 0, maxErr = 0; int verdictDiffers = 0, sampled = 0, n = 0;
            long exactTicks = 0, sampledTicks = 0;
            for (int k = 0; k < 600; ++k)
            {
                var baseA = new Vector3(0.3f, 0.2f, 0.3f);
                var rotA = Quaternion.CreateFromYawPitchRoll((float)(rng.NextDouble() * 6.28), (float)(rng.NextDouble() - .5f), (float)(rng.NextDouble() - .5f));
                // Three families: along each other, crossing at an angle, and offset by a fraction of the length.
                var family = k % 3;
                var tilt = family == 1 ? (float)(rng.NextDouble() * 1.2 - .6) : (float)(rng.NextDouble() * .2 - .1);
                var rotB = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitY, rotA), tilt)
                    * Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitZ, rotA), (float)(rng.NextDouble() * .2 - .1)) * rotA);
                var gap = (float)(rng.NextDouble() * .004 - .001); // -1 mm (penetrating) to +3 mm
                var offset = Vector3.Transform(Vector3.UnitY, rotA) * gap
                    + Vector3.Transform(Vector3.UnitX, rotA) * (family == 2 ? (float)(rng.NextDouble() * length * .8) : (float)(rng.NextDouble() * length * .2))
                    + Vector3.Transform(Vector3.UnitZ, rotA) * (float)((rng.NextDouble() - .5) * width);
                var poseA = new OrganPose(baseA, rotA, length, width, AboveGroundAgent.LeafThickness * .5f);
                var poseB = new OrganPose(baseA + offset, rotB, length, width, AboveGroundAgent.LeafThickness * .5f);
                var a = new ContactShape(poseA, template); var b = new ContactShape(poseB, template);
                var t0 = Stopwatch.GetTimestamp();
                var exact = FoliageGeometry.DistanceUncached(a, b, band * 4);
                var t1 = Stopwatch.GetTimestamp();
                var approx = FoliageGeometry.DistanceSampled(a, b, band * 4, 1); // always the sampled path
                var t2 = Stopwatch.GetTimestamp();
                exactTicks += t1 - t0; sampledTicks += t2 - t1;
                if (exact.Distance >= band * 4 && approx.Distance >= band * 4) continue; // both out of range
                ++n; ++sampled;
                var err = approx.Distance - exact.Distance;
                // The exact query stops at the first witness inside the physical radii, so below
                // them it is not a minimum either; only compare where it is one.
                if (err < -1e-5f && exact.Distance >= AboveGroundAgent.LeafThickness) { ++failures; Console.WriteLine($"{species.Name}: sampled distance BELOW exact by {-err * 1000:F3} mm at {exact.Distance * 1000:F3} mm"); }
                sumErr += Math.Max(0, err); maxErr = Math.Max(maxErr, err);
                if ((exact.Distance < band) != (approx.Distance < band)) ++verdictDiffers;
            }
            var scale = 1000.0 / Stopwatch.Frequency;
            Console.WriteLine($"{species.Name,-32} template {template.Vertices.Length} vertices / {template.Indices.Length / 3} triangles; pairs in range {n,4}: mean overestimate {sumErr / Math.Max(1, n) * 1000:F3} mm, max {maxErr * 1000:F3} mm, touching verdict differs {verdictDiffers} ({100.0 * verdictDiffers / Math.Max(1, n):F1} %); exact {exactTicks * scale:F0} ms, sampled {sampledTicks * scale:F0} ms");
            if (maxErr > 0.0015) { ++failures; Console.WriteLine($"{species.Name}: max overestimate above 1.5 mm"); }
        }
        Console.WriteLine(failures == 0 ? "Sheet distance self-test passed." : $"Sheet distance self-test FAILED ({failures}).");
        return failures == 0 ? 0 : 1;
    }

    private static int Main(string[] args)
    {
        var exitCode = 0;
        //See AgroServer/Program.cs: the pool must already hold a thread per core when the first
        //parallel phase of the first tick asks for them.
        ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);
        Parser.Default.ParseArguments<Options>(args).WithParsed(options =>
        {
        if (options.CheckpointSelfTest) { exitCode = CheckpointSelfTest(); return; }
        if (options.SheetDistanceSelfTest) { exitCode = SheetDistanceSelfTest(); return; }

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