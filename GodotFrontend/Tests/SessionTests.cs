using System.Text.Json.Nodes;
using GodotFrontend.Core.History;
using GodotFrontend.Core.Net;
using GodotFrontend.Core.Persist;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;
using GodotFrontend.Core.Threading;
using Xunit;

namespace GodotFrontend.Tests;

public class SessionTests
{
    private sealed class NullSink : IFrameSink
    {
        public int Shown;
        public event Action<int>? FramePresented;
        public void ShowFrame(int historyIndex, uint step, ResolvedFrame frame) { Shown++; FramePresented?.Invoke(historyIndex); }
        public void ShowNothing() { }
        public void ShowTerrain(TerrainData terrain) { }
        public void ShowFieldBox(float sizeX, float sizeD, float sizeZ) { }
    }

    private static (RunController c, ManualPump pump) Controller()
    {
        var pump = new ManualPump();
        var ep = new ServerEndpoint("http://127.0.0.1:1");
        var c = new RunController(pump, new RestClient(ep), new HubClient(ep), new NullSink(), null);
        return (c, pump);
    }

    private static void Run(ManualPump pump, Task t)
    {
        Assert.True(pump.DrainUntil(() => t.IsCompleted, TimeSpan.FromSeconds(30)), "timed out");
        t.GetAwaiter().GetResult();
    }

    [Fact]
    public void LegacySessionRoundTripsThroughArchives()
    {
        var dir = Directory.CreateTempSubdirectory("agro-session");
        try
        {
            var legacy = Fixtures.Path("fixture-session.json");
            var (c1, p1) = Controller();
            Run(p1, c1.LoadSessionAsync(legacy));
            Assert.Equal(2, c1.History.Count);
            Assert.Equal(1094u, c1.History[1].Step);
            Assert.Equal(4, c1.Session.SimHoursPerTick);
            Assert.Single(c1.Request.Seeds);
            Assert.Equal("Geranium Macrorrhizum", c1.Request.Seeds[0].Species);
            Assert.True(c1.Request.Species.Count > 5);

            var a = Path.Combine(dir.FullName, "a.agro");
            Run(p1, c1.SaveSessionAsync(a));

            var (c2, p2) = Controller();
            Run(p2, c2.LoadSessionAsync(a));
            Assert.Equal(2, c2.History.Count);
            Assert.Equal(PayloadKind.Archive, c2.History[1].Kind);
            var b = Path.Combine(dir.FullName, "b.agro");
            Run(p2, c2.SaveSessionAsync(b));

            var (c3, p3) = Controller();
            Run(p3, c3.LoadSessionAsync(b));
            var c = Path.Combine(dir.FullName, "c.agro");
            Run(p3, c3.SaveSessionAsync(c));
            Assert.Equal(File.ReadAllBytes(b), File.ReadAllBytes(c));

            // frames survive byte for byte (raw in the fixture, deflated in the archive)
            var fixture = JsonNode.Parse(File.ReadAllText(legacy))!["history"]!.AsArray();
            using var reader = RunArchiveReader.Open(c);
            Assert.Equal(2, reader.Frames.Count);
            for (var i = 0; i < 2; ++i)
            {
                var expected = Convert.FromBase64String(fixture[i]!["s"]!.GetValue<string>());
                var e = reader.Frames[i];
                var bytes = reader.Read(e.Offset, e.Length);
                Assert.Equal(expected, e.Plain ? bytes : Compressor.Inflate(bytes));
            }
            // keys the web load() needs are present
            var meta = reader.Metadata.AsObject();
            foreach (var key in new[] { "species", "seeds", "obstacles", "hoursPerTick", "simHoursPerTick", "visualMapping", "lighting" })
                Assert.True(meta.ContainsKey(key), key);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void InvalidArchivesAreRejected()
    {
        var dir = Directory.CreateTempSubdirectory("agro-bad");
        try
        {
            var (c, p) = Controller();
            Run(p, c.LoadSessionAsync(Fixtures.Path("fixture-session.json")));
            var good = Path.Combine(dir.FullName, "good.agro");
            Run(p, c.SaveSessionAsync(good));
            var bytes = File.ReadAllBytes(good);
            var truncated = Path.Combine(dir.FullName, "truncated.agro");
            File.WriteAllBytes(truncated, bytes.AsSpan(0, bytes.Length - 5).ToArray());
            Assert.Throws<InvalidDataException>(() => RunArchiveReader.Open(truncated).Dispose());
            var corrupt = (byte[])bytes.Clone();
            corrupt[^30] ^= 0xff; // inside the index
            var corruptPath = Path.Combine(dir.FullName, "corrupt.agro");
            File.WriteAllBytes(corruptPath, corrupt);
            Assert.ThrowsAny<Exception>(() => RunArchiveReader.Open(corruptPath).Dispose());
        }
        finally { dir.Delete(true); }
    }
}
