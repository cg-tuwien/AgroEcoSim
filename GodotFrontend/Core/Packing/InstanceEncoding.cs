using GodotFrontend.Core.Scene;

namespace GodotFrontend.Core.Packing;

/// <summary>
/// How one primitive becomes 20 floats. The wire's row-major 3x4 IS the MultiMesh transform layout (row i = basis
/// row i followed by origin i), so planar primitives copy through; cylinders and roots get the column scale by
/// (radius, length, radius) and the half-length recentring of PrimitiveTransform.ts decodeCylinderish, because the
/// unit cylinder and box meshes are centred while the wire stores the base.
/// </summary>
public static class InstanceEncoding
{
    public const int OffColor = 12;
    public const int OffCustom = 16;

    public static void WritePlanar(ReadOnlySpan<float> wire, Span<float> dst)
    {
        wire.Slice(0, 12).CopyTo(dst);
    }

    public static void WriteCylinder(ReadOnlySpan<float> wire, float radius, float length, Span<float> dst)
    {
        // columns are (m0,m4,m8), (m1,m5,m9), (m2,m6,m10); translation (m3,m7,m11)
        float c1x = wire[1] * length, c1y = wire[5] * length, c1z = wire[9] * length;
        dst[0] = wire[0] * radius; dst[1] = c1x; dst[2] = wire[2] * radius; dst[3] = wire[3] + 0.5f * c1x;
        dst[4] = wire[4] * radius; dst[5] = c1y; dst[6] = wire[6] * radius; dst[7] = wire[7] + 0.5f * c1y;
        dst[8] = wire[8] * radius; dst[9] = c1z; dst[10] = wire[10] * radius; dst[11] = wire[11] + 0.5f * c1z;
    }

    /// <summary>Uniform scale s about a centre, for sphere joints.</summary>
    public static void WriteUniform(float cx, float cy, float cz, float s, Span<float> dst)
    {
        dst[0] = s; dst[1] = 0; dst[2] = 0; dst[3] = cx;
        dst[4] = 0; dst[5] = s; dst[6] = 0; dst[7] = cy;
        dst[8] = 0; dst[9] = 0; dst[10] = s; dst[11] = cz;
    }

    /// <summary>Two unit values into one exact integer below 2^24: round(a*4095)*4096 + round(b*4095).</summary>
    public static float Pack12(float a, float b)
    {
        var ia = (int)System.Math.Round(System.Math.Clamp(a, 0f, 1f) * 4095f);
        var ib = (int)System.Math.Round(System.Math.Clamp(b, 0f, 1f) * 4095f);
        return ia * 4096 + ib;
    }

    /// <summary>Organ class carried in the custom data so the shader can apply per-organ mapping exceptions.</summary>
    public const int ClassLamina = 0, ClassFruitish = 1, ClassCylinder = 2, ClassRoot = 3;

    public static int OrganClass(PrimitiveType kind, OrganType organ) => kind switch
    {
        PrimitiveType.Rectangle => organ == OrganType.FruitWing ? ClassFruitish : ClassLamina,
        PrimitiveType.Ellipsoid => ClassFruitish,
        PrimitiveType.Box => ClassRoot,
        _ => ClassCylinder,
    };

    /// <summary>
    /// custom = (pack12(water, energy), irradianceOrWood, pack12(relResource, relProduction),
    ///           round(clamp(auxins-1))*16 + organClass*2 + hasColor).
    /// </summary>
    public static void WriteCustom(ReadOnlySpan<float> stats, int organClass, bool hasColor, Span<float> dst)
    {
        if (stats.IsEmpty)
        {
            dst[0] = 0; dst[1] = 0; dst[2] = 0;
            dst[3] = organClass * 2 + (hasColor ? 1 : 0);
            return;
        }
        dst[0] = Pack12(stats[Stat.Water], stats[Stat.Energy]);
        dst[1] = stats[Stat.IrradianceOrWood];
        dst[2] = Pack12(stats[Stat.RelResource], stats[Stat.RelProduction]);
        var aux = (int)System.Math.Round(System.Math.Clamp(stats[Stat.Auxins] - 1f, 0f, 1f) * 4095f);
        dst[3] = aux * 16 + organClass * 2 + (hasColor ? 1 : 0);
    }
}
