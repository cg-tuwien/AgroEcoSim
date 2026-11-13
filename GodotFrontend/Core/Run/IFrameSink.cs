using GodotFrontend.Core.History;
using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Run;

/// <summary>
/// What the renderer offers the run controller. ShowFrame hands over bytes to decode and upload; FramePresented must
/// fire for every ShowFrame, even one the renderer skipped, because the preview pump re-arms on it.
/// </summary>
public interface IFrameSink
{
    void ShowFrame(int historyIndex, uint step, ResolvedFrame frame);
    void ShowNothing();
    void ShowTerrain(TerrainData terrain);
    void ShowFieldBox(float sizeX, float sizeD, float sizeZ);
    event Action<int>? FramePresented;
}
