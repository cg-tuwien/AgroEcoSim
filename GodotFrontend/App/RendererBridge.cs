using Godot;
using GodotFrontend.App.Render;
using GodotFrontend.Core.History;
using GodotFrontend.Core.Packing;
using GodotFrontend.Core.Run;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.App;

/// <summary>
/// The renderer side of the run controller's contract: frames go to the decode worker and, once uploaded, are
/// reported as presented; terrain and the field box go to the terrain builder. Keeps the live decoded scene for
/// the picker and the hover readout.
/// </summary>
public sealed class RendererBridge : IFrameSink
{
    private readonly AppRoot app;
    private long nextFrameId;
    private PackedFrame? livePacked;
    private int pendingIndex = -1;

    public DecodedScene? LiveScene { get; private set; }
    public uint LiveStep { get; private set; }
    public FrameReady? LastReady { get; private set; }
    public PackedFrame? LivePacked => livePacked;

    public event Action<int>? FramePresented;
    /// <summary>Raised after a frame or the terrain changed on screen (HUD counters).</summary>
    public event Action? SceneChanged;

    public RendererBridge(AppRoot app)
    {
        this.app = app;
    }

    public void ShowFrame(int historyIndex, uint step, ResolvedFrame frame)
    {
        var id = ++nextFrameId;
        pendingIndex = historyIndex;
        app.Decoder.Request(id, frame.Bytes, frame.Deflated,
            ready =>
            {
                if (ready.FrameId != nextFrameId)
                {
                    // superseded while decoding: report it presented anyway, the preview pump re-arms on it
                    FramePresented?.Invoke(historyIndex);
                    return;
                }
                var sw = System.Diagnostics.Stopwatch.StartNew();
                app.Batches.Apply(ready.Packed);
                app.LastUploadMs = sw.Elapsed.TotalMilliseconds;
                if (app.Args.Screenshot != null || app.Args.FrameFile != null)
                    GD.Print($"FRAME step={step} plants={ready.Scene.PlantCount} objects={ready.Packed.InstanceCount} triangles={ready.Packed.TriangleCount} " +
                             $"inflate={ready.InflateMs:F1}ms decode={ready.DecodeMs:F1}ms pack={ready.PackMs:F1}ms upload={app.LastUploadMs:F1}ms");
                if (livePacked != null && !ReferenceEquals(livePacked, ready.Packed)) app.Decoder.Recycle(livePacked);
                livePacked = ready.Packed;
                LiveScene = ready.Scene;
                LiveStep = step;
                LastReady = ready;
                SceneChanged?.Invoke();
                FramePresented?.Invoke(historyIndex);
            },
            (fid, ex) =>
            {
                GD.PrintErr($"frame {step}: {ex.Message}");
                if (fid == nextFrameId) FramePresented?.Invoke(historyIndex);
            });
    }

    public void ShowNothing()
    {
        nextFrameId++;
        app.Batches.Clear();
        LiveScene = null;
        SceneChanged?.Invoke();
    }

    public void ShowTerrain(TerrainData terrain)
    {
        app.Terrain.Show(terrain);
        SceneChanged?.Invoke();
    }

    public void ShowFieldBox(float sizeX, float sizeD, float sizeZ)
    {
        if (app.Terrain.Terrain != null) return; // a loaded terrain wins over the plain box
        app.Terrain.ShowFieldBox(sizeX, sizeD, sizeZ);
        SceneChanged?.Invoke();
    }
}
