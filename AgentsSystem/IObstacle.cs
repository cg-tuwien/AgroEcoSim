using System.Text;

namespace AgentsSystem;

public interface IObstacle
{
    void ExportTriangles(List<System.Numerics.Vector3> points, BinaryWriter writer, StringBuilder obji = null);
    void ExportAsPrimitivesClustered(BinaryWriter writer);
    void ExportAsPrimitivesInterleaved(BinaryWriter writer);
#if GODOT
	void GodotReady();
#endif
}
