using AgentsSystem;
using System.Numerics;
using System.Text;

namespace Agro;

public partial class Wall : IObstacle
{
    public readonly float Length;
    public readonly float Height;
    public readonly float Thickness;
    public readonly Vector3 Position;
    public readonly float Orientation;

    readonly Vector3[] PointData;
    readonly static uint[] IndexData;
    readonly ArraySegment<byte> PrimitiveData;

    public Wall(float length, float height, float thickness, Vector3 position, float orientation)
    {
        Length = length;
        Height = height;
        Thickness = thickness;
        Position = position;
        Orientation = orientation;

        PointData = new Vector3[8];
        var x = new Vector3(Length * 0.5f, 0f, 0f);
        var y = new Vector3(0f, Height, 0f);
        var z = new Vector3(0f, 0f, Thickness * 0.5f);

        PointData[0] = Position -x - z;
        PointData[1] = Position + x - z;
        PointData[2] = Position + x + z;
        PointData[3] = Position + -x + z;
        PointData[4] = Position + y - x - z;
        PointData[5] = Position + y + x - z;
        PointData[6] = Position + y + x + z;
        PointData[7] = Position + y - x + z;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        var hx = Length * 0.5f;
        var hy = Height * 0.5f;
        var hz = Thickness * 0.5f;

        var vhx = new Vector3(hx, 0f, 0f);
        var vhy = new Vector3(0f, Height, 0f);

        writer.WriteU32(5);

        //front
        writer.WriteU8(8);
        writer.WriteV32(vhx, Position.X);
        writer.WriteV32(vhy, Position.Y + hy);
        writer.WriteV32(Vector3.UnitZ, Position.Z + hz);

        //back
        writer.WriteU8(8);
        writer.WriteV32(-vhx, Position.X);
        writer.WriteV32(vhy, Position.Y + hy);
        writer.WriteV32(-Vector3.UnitZ, Position.Z - hz);

        //left
        writer.WriteU8(8);
        writer.WriteV32(-Vector3.UnitZ, Position.X - hx);
        writer.WriteV32(vhy, Position.Y + hy);
        writer.WriteV32(vhx, Position.Z);

        //right
        writer.WriteU8(8);
        writer.WriteV32(Vector3.UnitZ, Position.X + hx);
        writer.WriteV32(vhy, Position.Y + hy);
        writer.WriteV32(-vhx, Position.Z);

        //top
        writer.WriteU8(8);
        writer.WriteV32(vhx, Position.X);
        writer.WriteV32(Vector3.UnitZ, Position.Y + Height);
        writer.WriteV32(-vhy, Position.Z);

        stream.TryGetBuffer(out PrimitiveData);
    }

    static Wall()
    {
        IndexData = new uint[]
        {
            //front face
            0, 1, 5,   0, 5, 4,
            //right face
            1, 2, 6,   1, 6, 5,
            //back face
            2, 3, 7,   2, 7, 6,
            //left face
            3, 0, 4,   3, 4, 7,
            //top face
            4, 5, 6,   4, 6, 7
        };
    }

    public void ExportTriangles(List<Vector3> points, BinaryWriter writer, StringBuilder obji = null)
    {
        writer.WriteU32(1);
        writer.WriteU8(10); //WRITE NUMBER OF TRIANGLES in this surface
        uint p = (uint)points.Count;

        points.AddRange(PointData);

        foreach(var item in IndexData)
            writer.Write(item + p);

        if (obji != null)
        {
            var p1 = p + 1;
            for(int i = 0; i < IndexData.Length; i += 3)
                obji.AppendLine($"f {IndexData[i] + p1} {IndexData[i+1] + p1} {IndexData[i+2] + p1}");
        }
    }

    public void ExportPrimitives(BinaryWriter writer) => writer.Write(PrimitiveData);
}