using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgentsSystem;

public interface IObstacle
{
    /// <summary>
    /// Name of the obstacle. Measurement targets such as the facades of a green facade study are
    /// selected by matching a substring against it, so it carries the name of the source object
    /// (an OBJ group, for imported models). Never null.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Appends the obstacle's world space triangle mesh. Indices are relative to the start of
    /// <paramref name="vertices"/>, so the current count has to be added while appending.
    /// </summary>
    void AppendTriangles(List<System.Numerics.Vector3> vertices, List<int> indices);

    void ExportObj(List<System.Numerics.Vector3> points, StringBuilder obji);
    void ExportTriangles(List<System.Numerics.Vector3> points, BinaryWriter writer);
    void ExportAsPrimitivesClustered(BinaryWriter writer);
    void ExportAsPrimitivesInterleaved(BinaryWriter writer);
}
