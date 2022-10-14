using System.Text;

namespace AgentsSystem;

public interface IObstacle
{
    void ExportTriangles(List<System.Numerics.Vector3> points, BinaryWriter writer, StringBuilder obji = null);
    void ExportPrimitives(BinaryWriter writer);
}
