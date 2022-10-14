using AgentsSystem;
using System.Numerics;
using System.Text;

namespace Agro;

public partial class Umbrella : IObstacle
{
    public readonly float Radius;
    public readonly float Height;
    public readonly float Thickness;
    public readonly Vector3 Position;

    const int DiskTriangles = 144;
    readonly List<Vector3> PointData;
    readonly static List<uint> IndexData;
    readonly byte[] PrimitiveData;

    public Umbrella(float radius, float height, float thickness, Vector3 position)
    {
        Radius = radius;
        Height = height;
        Thickness = thickness;
        Position = position;

        PointData = new(153);
        var x = new Vector3(Thickness, 0f, 0f);
        var y = new Vector3(0f, Height, 0f);
        var z = new Vector3(0f, 0f, x.X);

        PointData.Add(Position -x - z);
        PointData.Add(Position + x - z);
        PointData.Add(Position + x + z);
        PointData.Add(Position + -x + z);
        PointData.Add(Position + y - x - z);
        PointData.Add(Position + y + x - z);
        PointData.Add(Position + y + x + z);
        PointData.Add(Position + y - x + z);

        PointData.Add(Position + y);

        const float step = MathF.PI * 2f / DiskTriangles;
        for(int i = 0; i < DiskTriangles; ++i)
            PointData.Add(Position + y + new Vector3(MathF.Cos(i * step), 0f, MathF.Sin(i * step)));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.WriteU32(2);

        //pole
        writer.WriteU8(2);
        writer.Write(Height);
        writer.Write(Radius);
        writer.WriteV32(Vector3.UnitX, Position.X);
        writer.WriteV32(Vector3.UnitY, Position.Y);
        writer.WriteV32(Vector3.UnitZ, Position.Z);

        //disk
        writer.WriteU8(1);
        writer.WriteV32(Vector3.UnitX * Radius, Position.X);
        writer.WriteV32(Vector3.UnitY * Radius, Position.Y);
        writer.WriteV32(Vector3.UnitZ * Radius, Position.Z);

        PrimitiveData = stream.GetBuffer();
    }

    static Umbrella()
    {
        IndexData = new(24 + DiskTriangles * 3)
        {
            //front face
            0, 1, 5,   0, 5, 4,
            //right face
            1, 2, 6,   1, 6, 5,
            //back face
            2, 3, 7,   2, 7, 6,
            //left face
            3, 0, 4,   3, 4, 7
        };

        //top disk
        for (uint i = 0; i < DiskTriangles; ++i)
        {
            IndexData.Add(8);
            IndexData.Add(i + 9U);
            IndexData.Add(i + 10U);
        }
        IndexData[^1] = 9U;
    }

    public void ExportTriangles(List<Vector3> points, BinaryWriter writer, StringBuilder obji = null)
    {
        writer.WriteU32(1);
        writer.WriteU8(152); //WRITE NUMBER OF TRIANGLES in this surface
        uint p = (uint)points.Count;

        points.AddRange(PointData);

        foreach(var item in IndexData)
            writer.Write(item + p);

        if (obji != null)
        {
            var p1 = p + 1;
            for(int i = 0; i < IndexData.Count; i += 3)
                obji.AppendLine($"f {IndexData[i] + p1} {IndexData[i+1] + p1} {IndexData[i+2] + p1}");
        }
    }

    public void ExportPrimitives(BinaryWriter writer) => writer.Write(PrimitiveData);
}