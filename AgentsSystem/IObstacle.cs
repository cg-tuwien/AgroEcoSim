using System.Text;

namespace AgentsSystem;

public interface IObstacle
{
    void ExportObj(List<System.Numerics.Vector3> points, StringBuilder obji);
    void ExportTriangles(List<System.Numerics.Vector3> points, BinaryWriter writer);
    void ExportAsPrimitivesClustered(BinaryWriter writer);
    void ExportAsPrimitivesInterleaved(BinaryWriter writer);
    #if GODOT
	void GodotReady();
    #endif
}
