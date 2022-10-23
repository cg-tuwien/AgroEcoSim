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
        var hx = Length * 0.5f;
        var hy = Height * 0.5f;
        var hz = Thickness * 0.5f;

        var vy = new Vector3(0f, Height, 0f);

        var vhx = new Vector3(hx, 0f, 0f);
        var vhy = new Vector3(0f, hy, 0f);
        var vhz = new Vector3(0f, 0f, hz);

        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, Orientation);

        var p0 = Vector3.Transform(- vhx - vhz, rotation);
        var p1 = Vector3.Transform(vhx - vhz, rotation);
        var p2 = Vector3.Transform(vhx + vhz, rotation);
        var p3 = Vector3.Transform(- vhx + vhz, rotation);

        PointData[0] = Position + p0;
        PointData[1] = Position + p1;
        PointData[2] = Position + p2;
        PointData[3] = Position + p3;
        PointData[4] = PointData[0] + vy;
        PointData[5] = PointData[1] + vy;
        PointData[6] = PointData[2] + vy;
        PointData[7] = PointData[3] + vy;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.WriteU32(5);

        //apply orientation
        vhx = Vector3.Transform(Vector3.UnitX, rotation) * hx;
        vhz = Vector3.Transform(Vector3.UnitZ, rotation) * hz;

        //front
        writer.WriteU8(8);
        writer.WriteM32(vhx, vhy, vhz,
                Position.X + vhz.X,  Position.Y + hy, Position.Z + vhz.Z);

        //back
        writer.WriteU8(8);
        writer.WriteM32(-vhx, vhy, -vhz,
                Position.X - vhz.X, Position.Y + hy, Position.Z - vhz.Z);

        //left
        writer.WriteU8(8);
        writer.WriteM32(vhz, vhy, -vhx,
                Position.X - vhx.X, Position.Y + hy, Position.Z - vhx.Z);

        //right
        writer.WriteU8(8);
        writer.WriteM32(-vhz, vhy, vhx,
                Position.X + vhx.X, Position.Y + hy , Position.Z + vhx.Z);

        //top
        writer.WriteU8(8);
        writer.WriteM32(vhx, -vhz, vhy,
                Position.X, Position.Y + Height, Position.Z);

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