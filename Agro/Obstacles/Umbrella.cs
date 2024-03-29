using AgentsSystem;
using System;
using System.Collections.Generic;
using System.IO;
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
    readonly ArraySegment<byte> PrimitiveDataClustered;
    readonly ArraySegment<byte> PrimitiveDataInterleaved;

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

        using var clusteredStream = new MemoryStream();
        using var clustered = new BinaryWriter(clusteredStream);
        using var interleavedStream = new MemoryStream();
        using var interleaved = new BinaryWriter(interleavedStream);

        clustered.WriteU32(2);
        interleaved.WriteU32(2);

        //pole
        clustered.WriteU8(2);
        clustered.Write(Height);
        clustered.Write(ht);
        clustered.WriteM32(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Position);

        interleaved.WriteU8(2);
        interleaved.Write(Height);
        interleaved.Write(ht);
        interleaved.WriteM32(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Position);
        interleaved.Write(false);

        //disk
        clustered.WriteU8(1);
        clustered.WriteM32(Vector3.UnitX * Radius, Vector3.UnitY * Radius, Vector3.UnitZ * Radius,
                Position.X, Position.Y + Height, Position.Z);

        interleaved.WriteU8(1);
        interleaved.WriteM32(Vector3.UnitX * Radius, Vector3.UnitY * Radius, Vector3.UnitZ * Radius,
                Position.X, Position.Y + Height, Position.Z);
        interleaved.Write(false);

        clusteredStream.TryGetBuffer(out PrimitiveDataClustered);
        interleavedStream.TryGetBuffer(out PrimitiveDataInterleaved);
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

    public void ExportTriangles(List<Vector3> points, BinaryWriter writer)
    {
        writer.WriteU32(1);
        writer.WriteU8(152); //WRITE NUMBER OF TRIANGLES in this surface
        uint p = (uint)points.Count;

        points.AddRange(PointData);

        foreach(var item in IndexData)
            writer.Write(item + p);
    }

    public void ExportObj(List<Vector3> points, StringBuilder obji)
    {
        var p1 = points.Count + 1;
        points.AddRange(PointData);
        for(int i = 0; i < IndexData.Count; i += 3)
            obji.AppendLine($"f {IndexData[i] + p1} {IndexData[i+1] + p1} {IndexData[i+2] + p1}");
    }

    public void ExportAsPrimitivesClustered(BinaryWriter writer) => writer.Write(PrimitiveDataClustered);
    public void ExportAsPrimitivesInterleaved(BinaryWriter writer) => writer.Write(PrimitiveDataInterleaved);
}