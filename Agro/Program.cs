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

        [Option("color-selftest", Required = false, HelpText =
            "Check every predefined species' leaf, dead-leaf, petal and stem colours against measured ranges, and exit.")]
        public bool ColorSelfTest { get; set; }

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
        // across the stand so the mixed engine's solid pass has something to enforce. The last two
        // scenarios run the internal light model with the wall selected for the leaf area index, so
        // the meter's accumulators, the static shading and the tray survey are carried across; the
        // last ones resume onto a LONGER horizon, which lays the weekly buckets onto a longer
        // calendar and crosses into weeks the original run never had. The 48-hour scenario asks for
        // 168 hours, which is not a multiple of its tick: the last step's midpoint falls on the day
        // AFTER the requested horizon, so the calendar must reach that day or the step's bucket
        // depends on the horizon and a resume onto a longer one files it in the wrong week.
        // The drought scenarios use a herbaceous plant in a rainless field: the drought response
        // commits leaves to senescence, kills them, sheds them and regrows node buds within the
        // first months, and every resume point lands inside that episode. The BroadLeafV1 variant
        // stores contact deformations keyed by organs that later turn from petiole into bud.
        var scenarios = new (int Steps, int HoursPerTick, int Plants, byte RenderMode, string? Filter, int ExtraSteps, int TotalHours, string Species, bool Dry, bool Deformable, DateTime? Start)[]
        {
            (40, 24, 4, 0, null, 0, 0, "Default", false, false, null), (60, 12, 8, 0, null, 0, 0, "Default", false, false, null), (90, 6, 2, 0, null, 0, 0, "Default", false, false, null),
            (40, 24, 4, 3, "wall", 0, 0, "Default", false, false, null), (36, 6, 4, 3, "wall", 40, 0, "Default", false, false, null), (4, 48, 2, 3, "wall", 4, 168, "Default", false, false, null),
            (540, 4, 1, 0, null, 0, 0, "Campanula poscharskyana", true, false, null), (540, 4, 1, 0, null, 0, 0, "Campanula poscharskyana", true, true, null),
            // Mesh LAI scratch is rebuilt after restore, including opted-in deformed foliage;
            // extending the horizon must preserve the original measured weeks as well as frames.
            (180, 4, 1, 3, "wall", 40, 0, "Campanula poscharskyana", false, true, null),
            // Autumn leaf loss of a perennial herb: sown 1 August, resumed on 9 November with
            // summer leaves committed, colouring and hanging dead. Mixed engine only.
            (600, 4, 1, 0, null, 0, 0, "Fragaria vesca 'Rügen'", false, false, new DateTime(2026, 8, 1)),
        };
        foreach (var engine in new[] { ContactEngineKind.Mixed, ContactEngineKind.Certified, ContactEngineKind.Soft })
        foreach (var (steps, hoursPerTick, plants, renderMode, filter, extraSteps, totalHours, speciesName, dry, deformable, start) in scenarios)
        {
            if ((deformable || start != null) && engine != ContactEngineKind.Mixed) continue;
            SpeciesSettings[]? species = null;
            if (deformable)
            {
                var custom = Agro.Species.Campanula_Poscharskyana.Init();
                custom.ContactDeformationMode = ContactDeformationMode.BroadLeafV1;
                species = [custom];
            }
            SimulationRequest Request(bool extended = false) => new()
            {
                ContactEngine = engine,
                HoursPerTick = hoursPerTick,
                TotalHours = (totalHours > 0 ? totalHours : hoursPerTick * steps) + (extended ? hoursPerTick * extraSteps : 0),
                FieldSize = dry ? new Utils.Json.Vector3XDZ { X = 1, D = 1, Z = 1 } : new Utils.Json.Vector3XDZ { X = 3, D = 1, Z = 3 },
                FieldResolution = dry ? 0.05f : 0.5f,
                Seed = 42,
                InitialDate = start ?? new DateTime(2026, 3, 15),
                Species = species,
                Plants = Enumerable.Range(0, plants).Select(i => new PlantRequest
                {
                    SpeciesName = speciesName,
                    Position = new Utils.Json.Vector3XYZ
                        { X = 0.4f + 0.3f * (i % 4), Y = -0.01f, Z = 0.4f + 0.3f * (i / 4) },
                }).ToArray(),
                Obstacles = [new ObstacleRequest { Type = "wall", Length = 3f, Height = 0.5f, Thickness = 0.05f,
                    Position = new Utils.Json.Vector3XYZ { X = 1.5f, Y = 0f, Z = 0.6f } }],
                RequestGeometry = true,
                RenderMode = renderMode,
                LeafAreaIndexObstacles = filter,
                LeafAreaIndexSensors = 1024,
                //The parallel tick is reproducible; sequential only keeps this test's timing stable.
                Deterministic = true,
            };

            AgroWorld Fresh(SimulationRequest request)
            {
                var world = Initialize.World(request);
                world.Deterministic = true;
                if (dry) world.ScalePrecipitation(0f);
                return world;
            }

            //The straight run already has the extended horizon: a longer weather table has a
            //byte-identical prefix, so it is the same simulation the resumed one claims to continue.
            var straight = Fresh(Request(extended: true));
            straight.Run((uint)steps);
            var expected = straight.ExportToStream(flags);
            var expectedMeasurement = MeasurementState(straight);

            //The final checkpoint too, when the horizon is extended: that is the server's "continue"
            //path, the one that resumes a COMPLETED run, and the only one whose last step can have
            //been credited past the original horizon.
            var stops = extraSteps > 0 ? new[] { 1, steps / 4, steps / 2, steps - 1, steps } : new[] { 1, steps / 4, steps / 2, steps - 1 };
            foreach (var stop in stops)
            {
                var first = Fresh(Request());
                first.Run((uint)stop);
                var checkpoint = first.SerializeState();

                var resumed = Fresh(Request(extended: true));
                resumed.RestoreState(checkpoint);
                resumed.Run((uint)(steps - stop));
                var actual = resumed.ExportToStream(flags);
                var actualMeasurement = MeasurementState(resumed);

                var same = expected.AsSpan().SequenceEqual(actual);
                var sameMeasurement = expectedMeasurement.AsSpan().SequenceEqual(actualMeasurement);
                if (!same || !sameMeasurement) ++failures;
                var scenario = $"mode {renderMode}{(filter != null ? $", filter \"{filter}\"" : "")}{(extraSteps > 0 ? $", horizon +{extraSteps} steps" : "")}"
                    + (dry ? $", {speciesName}, no rain{(deformable ? ", BroadLeafV1" : "")}" : "")
                    + (start != null ? $", {speciesName} from {start:yyyy-MM-dd}" : "");
                Console.WriteLine($"{engine,-9} {steps} steps @{hoursPerTick}h, {plants} plants, {scenario}, resume at {stop,3}: "
                    + $"frame {(same ? "identical" : "DIFFERS")}, measurement {(sameMeasurement ? "identical" : "DIFFERS")} (checkpoint {checkpoint.Length:N0} B)");
            }
        }

        Console.WriteLine(failures == 0
            ? "Checkpoint self-test passed: resuming is byte-identical to never stopping."
            : $"Checkpoint self-test FAILED in {failures} case(s).");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// The leaf area index meter's and the tray survey's closed state, which the exported frame does
    /// not contain: the aggregates a resumed run reports must be the uninterrupted run's, to the byte.
    /// </summary>
    static byte[] MeasurementState(AgroWorld world)
    {
        world.ObstacleLight?.Finish();
        world.Vegetation.Finish();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        world.Irradiance.WriteObstacleLightState(writer, world.RendererName);
        world.Vegetation.WriteState(writer);
        return stream.ToArray();
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
    /// <summary>
    /// Checks the colour every predefined species writes into a frame against what the organs
    /// measure, as the viewer shows it under the default light. No simulation: it evaluates the
    /// same functions the export calls.
    /// </summary>
    /// <remarks>
    /// The colours went wrong twice in ways no other check could see: decoded twice so old leaves
    /// were black, then authored pastel so healthy leaves looked washed out and dead ones grey.
    /// The bands are CIELAB: healthy leaves L* 25-54 (Munsell plant-tissue 5GY 4/4 is L* 41),
    /// green unless anthocyanic; dead leaves on either path L* 38-58, chroma at least 18 and a
    /// brown hue (Munsell 10YR 5/4 - 7.5YR 4/4); nothing on the way darker than L* 30; open
    /// petals either white (L* 75 and up) or coloured (chroma 35 and up); stems L* 30-85.
    /// See Agro/ORGAN_COLORS.md.
    /// </remarks>
    static int ColorSelfTest()
    {
        static Vector3 Shown(RGB8 wire, float exposure)
        {
            static float Linear(byte b) => ColorUtils.SrgbToLinear(b / 255f);
            var appearance = new Vector3(Linear(wire.Red), Linear(wire.Green), Linear(wire.Blue)) * exposure;
            return ColorUtils.CieLabFromOkLab(ColorUtils.LinearRgbToOklab(Vector3.Min(appearance, Vector3.One)));
        }
        static float Chroma(Vector3 lab) => MathF.Sqrt(lab.Y * lab.Y + lab.Z * lab.Z);
        static float Hue(Vector3 lab) => (MathF.Atan2(lab.Z, lab.Y) * 180f / MathF.PI + 360f) % 360f;
        static string F(Vector3 lab) => $"({lab.X,4:F0},{lab.Y,4:F0},{lab.Z,4:F0})";

        const float Summer = 0.35f, Lamina = ColorUtils.LaminaExposure, Round = ColorUtils.RoundOrganExposure;
        var failures = new List<string>();
        Console.WriteLine($"{"species",-34} {"leaf",-16} {"dead (season)",-16} {"dead (drought)",-16} {"petal",-16} stems");
        foreach (var species in SpeciesSettings.Predefined)
        {
            var name = species.Name;
            void Require(bool ok, string what) { if (!ok) failures.Add($"{name}: {what}"); }
            var leaves = species.LeafPhenology;

            var leaf = Shown(leaves.WireColor(Summer, 1f, 0f, 0f, Lamina), Lamina);
            Require(leaf.X >= 25f && leaf.X <= 54f, $"healthy leaf L* {leaf.X:F0} outside 25-54");
            Require(leaf.Y < -5f || (leaf.Y > 5f && leaf.X < 40f), $"healthy leaf neither green nor anthocyanic {F(leaf)}");

            var dead = new Vector3[2];
            for (int path = 0; path < 2; ++path)
            {
                for (int k = 1; k <= 10; ++k)
                {
                    var c = Shown(leaves.WireColor(Summer, 1f, 0f, k / 10f, Lamina, path), Lamina);
                    Require(c.X >= 30f, $"senescing leaf (path {path}, {k * 10} %) L* {c.X:F0} under 30");
                    dead[path] = c;
                }
                var d = dead[path];
                Require(d.X >= 38f && d.X <= 58f, $"dead leaf (path {path}) L* {d.X:F0} outside 38-58");
                Require(Chroma(d) >= 18f, $"dead leaf (path {path}) chroma {Chroma(d):F0} under 18: grey");
                Require(Hue(d) >= 40f && Hue(d) <= 90f, $"dead leaf (path {path}) hue {Hue(d):F0} not brown");
            }

            var petal = Shown(species.FlowerSettings.FlowerPhenology.WireColor(Summer, 1f, 0f, 0f, Lamina), Lamina);
            //A sedge has no perianth: Carex's petal slot stands for its scales and anthers.
            Require(petal.X >= 75f || Chroma(petal) >= 35f || name.StartsWith("Carex"), $"open petal {F(petal)} neither white nor coloured");
            var shed = Shown(species.FlowerSettings.FlowerPhenology.WireColor(Summer, 1f, 0f, 1f, Lamina), Lamina);
            Require(shed.X >= 38f, $"petal at its drop L* {shed.X:F0} under 38");

            var stems = species.EffectiveStemColors;
            var stemRange = (Min: 100f, Max: 0f);
            foreach (var (m, o, d) in new[] { (0f, 0f, 0f), (1f, 0f, 0f), (1f, 1f, 0f), (1f, 1f, 1f) })
            {
                var c = Shown(stems.Wire(m, o, d), Round);
                stemRange = (MathF.Min(stemRange.Min, c.X), MathF.Max(stemRange.Max, c.X));
                Require(c.X >= 30f && c.X <= 85f, $"stem (maturity {m}, age {o}, dying {d}) L* {c.X:F0} outside 30-85");
            }
            Console.WriteLine($"{name,-34} {F(leaf),-16} {F(dead[0]),-16} {F(dead[1]),-16} {F(petal),-16} L* {stemRange.Min:F0}-{stemRange.Max:F0}");
        }

        foreach (var failure in failures) Console.WriteLine("FAIL " + failure);
        Console.WriteLine(failures.Count == 0 ? "Colour self-test passed." : $"Colour self-test FAILED in {failures.Count} case(s).");
        return failures.Count == 0 ? 0 : 1;
    }

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
        if (options.ColorSelfTest) { exitCode = ColorSelfTest(); return; }

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
