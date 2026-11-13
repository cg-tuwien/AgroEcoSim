using GodotFrontend.Core.History;
using GodotFrontend.Core.Playback;
using Xunit;

namespace GodotFrontend.Tests;

public class HistoryTests
{
    private static byte[] Frame(int size) => new byte[size];

    [Fact]
    public void UpsertFilesByStepAndCountsBytesOnce()
    {
        var h = new History();
        Assert.Equal(0, h.Upsert(5, Frame(100)));
        Assert.Equal(1, h.Upsert(9, Frame(50)));
        Assert.Equal(1, h.Upsert(7, Frame(10)));      // inserted between 5 and 9
        Assert.Equal(3, h.Count);
        Assert.Equal(new uint[] { 5, 7, 9 }, h.Frames.Select(f => f.Step).ToArray());
        Assert.Equal(160 + 3 * History.EntryOverhead, h.ResidentBytes);
        Assert.Equal(1, h.Upsert(7, Frame(30)));      // duplicate step: replaced, bytes re-counted
        Assert.Equal(180 + 3 * History.EntryOverhead, h.ResidentBytes);
        Assert.Equal(0, h.EvictedCount);
        Assert.Equal(2, h.IndexOfStep(9));
        Assert.True(h.IndexOfStep(8) < 0);
        Assert.Equal(9u, h.StepAt(99));
    }

    [Fact]
    public void MergeIndexIsAUnionByStep()
    {
        var h = new History();
        h.Upsert(0, Frame(10));
        h.Upsert(4, Frame(10));
        var o = h.MergeIndex(new uint[] { 0, 1, 2, 3, 4, 5 }, false, 0);
        Assert.True(o.Changed);
        Assert.Equal(6, h.Count);
        Assert.Equal(4, h.EvictedCount);
        Assert.Equal(new uint[] { 0, 1, 2, 3, 4, 5 }, h.Frames.Select(f => f.Step).ToArray());
        Assert.True(h[1].Kind == PayloadKind.None);
        Assert.True(h[4].Kind == PayloadKind.Raw);
        // a torn index (fewer steps) changes nothing
        Assert.False(h.MergeIndex(new uint[] { 0, 1 }, false, 0).Changed);
        // a replaced tail drops held entries at or past the restart step
        var r = h.MergeIndex(new uint[] { 0, 1, 2, 3 }, true, 3);
        Assert.True(r.Replaced);
        Assert.Equal(4, h.Count);
        Assert.Equal(3u, h.Frames[^1].Step);
    }

    [Fact]
    public void EvictionSparesTheWindowThenEscalates()
    {
        var h = new History();
        for (uint s = 0; s < 20; ++s) h.Upsert(s, Frame(1000));
        var budget = 14 * (1000 + History.EntryOverhead); // target 10.5 frames; the spared set is 10 frames
        var dropped = h.EvictToBudget(pointer: 10, budget, recoverable: true, out var stillOver);
        Assert.False(stillOver);
        Assert.Equal(10, dropped);
        // the newest and the +-4 window around the playhead keep their bytes
        Assert.True(h[19].HasPayload);
        for (var i = 6; i <= 14; ++i) Assert.True(h[i].HasPayload, $"frame {i} should be spared");
        Assert.True(h.ResidentBytes <= budget * 0.75 + (1000 + History.EntryOverhead));
        // an impossible budget with recoverable frames drops everything but the shown frame
        h.EvictToBudget(10, 1, true, out stillOver);
        Assert.True(h[10].HasPayload);
        Assert.Equal(19, h.EvictedCount);
        Assert.True(stillOver);
    }

    [Fact]
    public void CompressedPayloadsAreCountedBySize()
    {
        var h = new History();
        h.Upsert(0, Frame(1000));
        h.ApplyCompressed(h[0], new byte[100]);
        Assert.Equal(PayloadKind.Zlib, h[0].Kind);
        Assert.Equal(100 + History.EntryOverhead, h.ResidentBytes);
        var raw = new byte[500];
        Array.Fill(raw, (byte)7);
        var packed = Compressor.Deflate(raw);
        Assert.True(packed.Length < raw.Length);
        Assert.Equal(raw, Compressor.Inflate(packed));
    }
}

public class PlaybackMachineTests
{
    [Fact]
    public void ForwardStepsAndRestsAtTheEnd()
    {
        var m = new PlaybackMachine();
        var shown = new List<int>();
        m.Show += i => shown.Add(i);
        m.Request(PlayState.Forward, 3);
        Assert.True(m.Step(3)); Assert.Equal(1, m.Pointer);
        Assert.True(m.Step(3)); Assert.Equal(2, m.Pointer);
        Assert.False(m.Step(3)); // past the end, not computing -> None
        Assert.Equal(PlayState.None, m.Playing);
        Assert.Equal(2, m.Pointer);
        Assert.Equal(new[] { 1, 2 }, shown);
    }

    [Fact]
    public void ForwardAtTheEndWhileComputingWaitsForTheLiveEdge()
    {
        var m = new PlaybackMachine { Computing = true };
        m.SetPointer(2);
        m.Request(PlayState.Forward, 3);
        Assert.Equal(PlayState.ForwardWaiting, m.Playing);
        Assert.True(m.AtLiveEdge);
        m.LastReceivedIndex = 5;
        Assert.True(m.Step(6));
        Assert.Equal(5, m.Pointer);
        Assert.Equal(PlayState.ForwardWaiting, m.Playing);
        m.RunEnded(6);
        Assert.Equal(PlayState.None, m.Playing);
        Assert.Equal(5, m.Pointer);
    }

    [Fact]
    public void NextSeekAndBackwardTransitions()
    {
        var m = new PlaybackMachine();
        m.SetPointer(1);
        m.Request(PlayState.NextForward, 4); m.Step(4);
        Assert.Equal(2, m.Pointer); Assert.Equal(PlayState.None, m.Playing);
        m.Request(PlayState.SeekForward, 4); m.Step(4);
        Assert.Equal(3, m.Pointer);
        m.Request(PlayState.Backward, 4); m.Step(4);
        Assert.Equal(2, m.Pointer); Assert.Equal(PlayState.Backward, m.Playing);
        m.Request(PlayState.SeekBackward, 4); m.Step(4);
        Assert.Equal(0, m.Pointer); Assert.Equal(PlayState.None, m.Playing);
        m.Request(PlayState.NextBackward, 4); m.Step(4);
        Assert.Equal(0, m.Pointer); Assert.Equal(PlayState.None, m.Playing);
        m.SeekTo(2, 4); m.Step(4);
        Assert.Equal(2, m.Pointer); Assert.Equal(PlayState.None, m.Playing);
    }
}
