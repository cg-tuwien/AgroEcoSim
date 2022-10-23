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
    readonly ArraySegment<byte> PrimitiveData;

    public Umbrella(float radius, float height, float thickness, Vector3 position)
    {
        Radius = radius;
        Height = height;
        Thickness = thickness;
        Position = position;

        var ht = Thickness * 0.5f;

        PointData = new(153);
        var vhx = new Vector3(ht, 0f, 0f);
        var vy = new Vector3(0f, Height, 0f);
        var vhz = new Vector3(0f, 0f, ht);

        PointData.Add(Position - vhx - vhz);
        PointData.Add(Position + vhx - vhz);
        PointData.Add(Position + vhx + vhz);
        PointData.Add(Position - vhx + vhz);
        PointData.Add(Position + vy - vhx - vhz);
        PointData.Add(Position + vy + vhx - vhz);
        PointData.Add(Position + vy + vhx + vhz);
        PointData.Add(Position + vy - vhx + vhz);

        var diskCenter = Position + vy;
        PointData.Add(diskCenter);

        const float step = MathF.PI * 2f / DiskTriangles;
        for(int i = 0; i < DiskTriangles; ++i)
            PointData.Add(diskCenter + new Vector3(MathF.Cos(i * step) * Radius, 0f, MathF.Sin(i * step) * Radius));

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.WriteU32(2);

        //pole
        writer.WriteU8(2);
        writer.Write(Height);
        writer.Write(ht);
        // writer.WriteV32(Vector3.UnitX, Position.X);
        // writer.WriteV32(Vector3.UnitY, Position.Y);
        // writer.WriteV32(Vector3.UnitZ, Position.Z);
        writer.WriteM32(Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                Position);

        //disk
        writer.WriteU8(1);
        // writer.WriteV32(Vector3.UnitX * Radius, Position.X);
        // writer.WriteV32(Vector3.UnitY * Radius, Position.Y + Height);
        // writer.WriteV32(Vector3.UnitZ * Radius, Position.Z);
        writer.WriteM32(Vector3.UnitX * Radius,
                Vector3.UnitY * Radius,
                Vector3.UnitZ * Radius,
                Position.X, Position.Y + Height, Position.Z);

        stream.TryGetBuffer(out PrimitiveData);
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