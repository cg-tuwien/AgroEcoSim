using System;
using System.Collections.Generic;
using System.Numerics;

namespace Agro;


public enum RenderLeafLod
{
    BillboardQuad = 1,
    CoarseOutline = 2,
    LeafletsLowPoly = 3
}

public readonly record struct Transform3(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static Transform3 Identity => new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public Matrix4x4 ToMatrix() => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public Vector3 TransformPoint(Vector3 local) => Vector3.Transform(local, ToMatrix());

    public Vector3 TransformDirection(Vector3 dir) => Vector3.TransformNormal(dir, Matrix4x4.CreateFromQuaternion(Rotation));

    public static Transform3 Combine(in Transform3 parent, in Transform3 child)
    {
        var m = child.ToMatrix() * parent.ToMatrix();
        Matrix4x4.Decompose(m, out var scale, out var rot, out var pos);
        return new Transform3(pos, rot, scale);
    }
}

public sealed record CompoundLeafDefinition
{
    public CompoundLeafType Type { get; init; } = CompoundLeafType.Pinnate;

    public int LeafletCount { get; init; } = 5;

    /// <summary>
    /// Total length of rachis / span from attachment to furthest leaflet origin.
    /// </summary>
    public float RachisLengthM { get; init; } = 0.12f;

    /// <summary>
    /// Typical spread angle for left/right leaflets.
    /// </summary>
    public float SpreadAngleDeg { get; init; } = 45f;

    /// <summary>
    /// For pinnate leaves: whether there is a terminal leaflet.
    /// </summary>
    public bool HasTerminalLeaflet { get; init; } = true;

    /// <summary>
    /// Relative size profile from base to tip. If empty, a generated profile is used.
    /// </summary>
    public float[] RelativeSizeProfile { get; init; } = Array.Empty<float>();

    /// <summary>
    /// Morphology used by each leaflet.
    /// </summary>
    public LeafMorphology LeafletMorphology { get; init; } = new()
    {
        Complexity = LeafComplexity.Simple,
        Shape = new EllipticLeafShape(),
        Dimensions = new LeafDimensions(0.035f, 0.015f, 0.0002f, 0.002f, 0.10f),
        Margin = LeafMarginType.Entire,
        Venation = LeafVenationType.Pinnate
    };
}

public class LeafInstance
{
    public LeafMorphology Morphology { get; init; } = LeafMorphology.Default;
    //public LeafPhenology Phenology { get; init; } = LeafPhenology.Default;
    public LeafPhysiology Physiology { get; init; } = LeafPhysiology.Default;
    public List<LeafletInstance> Leaflets = [];

    public bool IsCompound => Morphology.Complexity == LeafComplexity.Compound;

    public void InitializeLeafletsIfNeeded()
    {
        if (!IsCompound || Leaflets.Count > 0)
            return;

        CompoundLeafletLayoutBuilder.BuildLeaflets(this);
    }

    // public float CurrentAreaM2()
    // {
    //     if (!IsCompound)
    //     {
    //         float baseArea = Morphology.BaseAreaM2;
    //         float ageScale = ComputeWholeLeafAreaScale();
    //         float damageFactor = 1f;
    //         return baseArea * ageScale * damageFactor;
    //     }

    //     InitializeLeafletsIfNeeded();
    //     float total = 0f;
    //     foreach (var lf in Leaflets)
    //         total += lf.CurrentAreaM2();
    //     return total;
    // }

    // private float ComputeWholeLeafAreaScale()
    // {
    //     // Placeholder growth curve. Replace with your carbon allocation model later.
    //     float age01 = Math.Clamp(State.AgeDays / 30f, 0f, 1f);
    //     return 0.02f + 0.98f * age01;
    // }
}


public sealed class LeafletState
{
    public float AgeDays { get; set; } = 0f;
    public LeafStage Stage { get; set; } = LeafStage.Bud;
    public float AreaScale { get; set; } = 0.02f;
    // public float Stress01 { get; set; } = 0f;
    // public float Damage01 { get; set; } = 0f;
}

public sealed class LeafState
{
    public float AgeDays { get; set; } = 0f;
    public LeafStage Stage { get; set; } = LeafStage.Bud;

    public float WholeLeafStress01 { get; set; } = 0f;
    public float Hydration01 { get; set; } = 1f;
    public float Nutrient01 { get; set; } = 1f;
}


public class LeafletInstance
{
    public int Index { get; init; }
    public Transform3 LocalTransform { get; set; } = Transform3.Identity;
    public LeafMorphology Morphology { get; init; }
    public LeafletState State { get; } = new();

    public LeafletInstance(int index, LeafMorphology morphology)
    {
        Index = index;
        Morphology = morphology;
    }

    public float CurrentAreaM2()
    {
        float baseArea = Morphology.BaseAreaM2();
        //float damageFactor = 1f - Math.Clamp(State.Damage01, 0f, 1f);
        //return baseArea * Math.Max(0f, State.AreaScale) * damageFactor;
        return baseArea * Math.Max(0f, State.AreaScale);
    }
}

public static class CompoundLeafletLayoutBuilder
{
    public static void BuildLeaflets(LeafInstance leaf)
    {
        var morph = leaf.Morphology;
        if (morph.Complexity != LeafComplexity.Compound || morph.Compound is null)
            return;

        leaf.Leaflets.Clear();

        switch (morph.Compound.Type)
        {
            case CompoundLeafType.Pinnate:
                BuildPinnate(leaf, morph.Compound);
                break;

            case CompoundLeafType.Palmate:
                BuildPalmate(leaf, morph.Compound);
                break;

            case CompoundLeafType.Trifoliate:
                BuildTrifoliate(leaf, morph.Compound);
                break;

            case CompoundLeafType.Bipinnate:
                BuildBipinnateApprox(leaf, morph.Compound);
                break;

            default:
                BuildPinnate(leaf, morph.Compound);
                break;
        }
    }

    private static void BuildPinnate(LeafInstance leaf, CompoundLeafDefinition def)
    {
        int count = Math.Max(2, def.LeafletCount);
        float rachis = Math.Max(0.01f, def.RachisLengthM);
        float angleRad = DegreesToRadians(def.SpreadAngleDeg);

        int created = 0;
        int pairedCount = def.HasTerminalLeaflet ? count - 1 : count;
        int pairs = Math.Max(1, pairedCount / 2);

        for (int i = 0; i < pairs; i++)
        {
            float t = pairs == 1 ? 0.6f : (i + 1f) / (pairs + 1f);
            float y = t * rachis;

            float relSize = ResolveRelativeSize(def, i, pairs);
            var leafletMorph = ScaleLeafletMorphology(def.LeafletMorphology, relSize);

            // left
            {
                var lf = new LeafletInstance(created++, leafletMorph);
                lf.LocalTransform = new Transform3(
                    new Vector3(-0.02f * relSize, 0f, y),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, +angleRad),
                    Vector3.One);
                leaf.Leaflets.Add(lf);
            }

            // right
            {
                var lf = new LeafletInstance(created++, leafletMorph);
                lf.LocalTransform = new Transform3(
                    new Vector3(+0.02f * relSize, 0f, y),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, -angleRad),
                    Vector3.One);
                leaf.Leaflets.Add(lf);
            }
        }

        if (def.HasTerminalLeaflet)
        {
            float relSize = ResolveRelativeSize(def, pairs, pairs + 1);
            var terminalMorph = ScaleLeafletMorphology(def.LeafletMorphology, relSize);

            var lf = new LeafletInstance(created, terminalMorph);
            lf.LocalTransform = new Transform3(
                new Vector3(0f, 0f, rachis),
                Quaternion.Identity,
                Vector3.One);
            leaf.Leaflets.Add(lf);
        }

        SeedInitialState(leaf);
    }

    private static void BuildPalmate(LeafInstance leaf, CompoundLeafDefinition def)
    {
        int count = Math.Max(3, def.LeafletCount);
        float fan = DegreesToRadians(Math.Clamp(def.SpreadAngleDeg * 2f, 40f, 170f));
        float radius = Math.Max(0.02f, def.RachisLengthM * 0.65f);

        for (int i = 0; i < count; i++)
        {
            float u = count == 1 ? 0.5f : i / (float)(count - 1);
            float angle = -fan * 0.5f + fan * u;

            float relSize = ResolveRelativeSize(def, i, count);
            var leafletMorph = ScaleLeafletMorphology(def.LeafletMorphology, relSize);

            Vector3 pos = new(
                MathF.Sin(angle) * radius * 0.65f,
                0f,
                MathF.Cos(angle) * radius);

            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
            var lf = new LeafletInstance(i, leafletMorph)
            {
                LocalTransform = new Transform3(pos, rot, Vector3.One)
            };
            leaf.Leaflets.Add(lf);
        }

        SeedInitialState(leaf);
    }

    private static void BuildTrifoliate(LeafInstance leaf, CompoundLeafDefinition def)
    {
        int count = 3;
        float angle = DegreesToRadians(Math.Clamp(def.SpreadAngleDeg, 20f, 70f));
        float radius = Math.Max(0.02f, def.RachisLengthM * 0.6f);

        for (int i = 0; i < count; i++)
        {
            float yaw = i switch
            {
                0 => 0f,
                1 => -angle,
                _ => angle
            };

            float relSize = (i == 0) ? 1.0f : 0.85f;
            var leafletMorph = ScaleLeafletMorphology(def.LeafletMorphology, relSize);

            Vector3 pos = i switch
            {
                0 => new Vector3(0f, 0f, radius),
                1 => new Vector3(-radius * 0.35f, 0f, radius * 0.55f),
                _ => new Vector3(+radius * 0.35f, 0f, radius * 0.55f),
            };

            var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
            var lf = new LeafletInstance(i, leafletMorph)
            {
                LocalTransform = new Transform3(pos, rot, Vector3.One)
            };
            leaf.Leaflets.Add(lf);
        }

        SeedInitialState(leaf);
    }

    private static void BuildBipinnateApprox(LeafInstance leaf, CompoundLeafDefinition def)
    {
        // Approximation for now:
        // treat it as many small pinnate groups along a main rachis.
        int clusters = Math.Max(2, def.LeafletCount / 4);
        float mainRachis = Math.Max(0.04f, def.RachisLengthM);
        float spread = DegreesToRadians(def.SpreadAngleDeg);

        int idx = 0;
        for (int c = 0; c < clusters; c++)
        {
            float t = (c + 1f) / (clusters + 1f);
            float z = t * mainRachis;

            for (int side = -1; side <= 1; side += 2)
            {
                for (int j = 0; j < 2; j++)
                {
                    float localScale = 0.55f - j * 0.08f;
                    var leafletMorph = ScaleLeafletMorphology(def.LeafletMorphology, localScale);

                    Vector3 pos = new(
                        side * (0.015f + 0.01f * j),
                        0f,
                        z + j * 0.01f);

                    float yaw = side * spread * (0.8f + 0.2f * j);
                    var rot = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);

                    var lf = new LeafletInstance(idx++, leafletMorph)
                    {
                        LocalTransform = new Transform3(pos, rot, Vector3.One)
                    };
                    leaf.Leaflets.Add(lf);
                }
            }
        }

        SeedInitialState(leaf);
    }

    private static void SeedInitialState(LeafInstance leaf)
    {
        // foreach (var lf in leaf.Leaflets)
        // {
        //     lf.State.AgeDays = leaf.State.AgeDays;
        //     lf.State.Stage = leaf.State.Stage;
        //     lf.State.AreaScale = Math.Clamp(leaf.State.AgeDays / 20f, 0.03f, 1f);
        //     // lf.State.Stress01 = leaf.State.WholeLeafStress01;
        // }
    }

    private static float ResolveRelativeSize(CompoundLeafDefinition def, int index, int total)
    {
        if (def.RelativeSizeProfile is { Length: > 0 })
        {
            int i = Math.Clamp(index, 0, def.RelativeSizeProfile.Length - 1);
            return Math.Max(0.1f, def.RelativeSizeProfile[i]);
        }

        // default profile: medium at base, largest around mid-upper, slightly smaller at tip
        float u = total <= 1 ? 0.5f : index / (float)(total - 1);
        float val = 0.75f + 0.35f * MathF.Sin(u * MathF.PI);
        return Math.Clamp(val, 0.35f, 1.2f);
    }

    private static LeafMorphology ScaleLeafletMorphology(LeafMorphology baseMorph, float scale)
    {
        var d = baseMorph.Dimensions;
        return baseMorph with
        {
            Dimensions = new LeafDimensions(
                d.LengthM * scale,
                d.MaxWidthM * scale,
                d.ThicknessM,
                d.PetioleLengthM * scale,
                d.Curvature01)
        };
    }

    private static float DegreesToRadians(float deg) => deg * (MathF.PI / 180f);
}

public sealed class MeshData
{
    public List<Vector3> Positions { get; init; } = [];
    // public List<Vector3> Normals { get; } = new();
    // public List<Vector2> UV0 { get; } = new();
    public List<int> Indices { get; init; } = [];

    public void Append(MeshData other, Matrix4x4 transform)
    {
        int baseIndex = Positions.Count;

        // Matrix4x4.Invert(transform, out var inv);
        // Matrix4x4 normalMat = Matrix4x4.Transpose(inv);

        for (int i = 0; i < other.Positions.Count; i++)
        {
            Positions.Add(Vector3.Transform(other.Positions[i], transform));

            // Vector3 n = Vector3.TransformNormal(other.Normals[i], normalMat);
            // if (n.LengthSquared() > 1e-8f)
            //     n = Vector3.Normalize(n);
            // else
            //     n = Vector3.UnitY;

            // Normals.Add(n);
            // UV0.Add(other.UV0[i]);
        }

        foreach (int idx in other.Indices)
            Indices.Add(baseIndex + idx);
    }

    public void WriteMesh(BinaryWriter writer)
    {
        writer.Write(Positions.Count);
        for(int i = 0; i < Positions.Count; ++i)
            writer.WriteV32(Positions[i]);

        writer.Write(Indices.Count);
        for(int i = 0; i < Indices.Count; ++i)
            writer.Write(Indices[i]);

        // writer.Write(4);
        // writer.WriteV32(new Vector3(-1f, -1f, 0f));
        // writer.WriteV32(new Vector3(1f, -1f, 0f));
        // writer.WriteV32(new Vector3(1f, 1f, 0f));
        // writer.WriteV32(new Vector3(-1f, 1f, 0f));

        // writer.Write(6);
        // writer.Write(0);
        // writer.Write(1);
        // writer.Write(2);
        // writer.Write(0);
        // writer.Write(2);
        // writer.Write(3);
    }
}

public static class LeafMeshBuilder
{
    public static MeshData Build(LeafInstance leaf, RenderLeafLod lod)
    {
        leaf.InitializeLeafletsIfNeeded();

        return lod switch
        {
            RenderLeafLod.BillboardQuad => BuildBillboardQuad(leaf),
            RenderLeafLod.CoarseOutline => BuildCoarseOutline(leaf),
            RenderLeafLod.LeafletsLowPoly => BuildLeafletsLowPoly(leaf),
            _ => BuildBillboardQuad(leaf)
        };
    }

    private static MeshData BuildBillboardQuad(LeafInstance leaf)
    {
        var halfSize = EstimateWholeLeafHalfSize(leaf);

        var mesh = new MeshData();

        var hsy2 = halfSize.Y * 2f;
        mesh.Positions.Add(new(-halfSize.X, 0f, 0f));
        mesh.Positions.Add(new(+halfSize.X, 0f, 0f));
        mesh.Positions.Add(new(+halfSize.X, 0f, hsy2));
        mesh.Positions.Add(new(-halfSize.X, 0f, hsy2));

        // for (int i = 0; i < 4; i++)
        //     mesh.Normals.Add(Vector3.UnitY);

        // mesh.UV0.Add(new Vector2(0, 0));
        // mesh.UV0.Add(new Vector2(1, 0));
        // mesh.UV0.Add(new Vector2(1, 1));
        // mesh.UV0.Add(new Vector2(0, 1));

        mesh.Indices.AddRange([0, 1, 2, 0, 2, 3]);

        return mesh;
    }

    private static MeshData BuildCoarseOutline(LeafInstance leaf)
    {
        if (!leaf.IsCompound)
            return BuildSimpleLeafMesh(leaf.Morphology, 12);

        // Compound leaf coarse envelope:
        // create a silhouette around all leaflet origins and extents,
        // but without modeling individual leaflets.
        return BuildCompoundEnvelopeMesh(leaf, 16);
    }

    private static MeshData BuildLeafletsLowPoly(LeafInstance leaf)
    {
        if (!leaf.IsCompound)
            return BuildSimpleLeafMesh(leaf.Morphology, 10);

        var mesh = new MeshData();

        foreach (var leaflet in leaf.Leaflets)
        {
            var localMesh = BuildSimpleLeafMesh(leaflet.Morphology, 8);
            var m = leaflet.LocalTransform.ToMatrix();
            mesh.Append(localMesh, m);
        }

        return mesh;
    }

    private static MeshData BuildSimpleLeafMesh(LeafMorphology morph, int segments)
    {
        var dims = morph.Dimensions;
        var shape = morph.Shape;

        segments = Math.Max(6, segments);

        var mesh = new MeshData();

        // center
        mesh.Positions.Add(Vector3.Zero);
        // mesh.Normals.Add(Vector3.UnitY);
        //mesh.UV0.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < segments; i++)
        {
            var t = i / (float)segments;
            var p = shape.OutlinePointInvariant(t);

            // x across width, z along length
            var zNorm = p.Y / Math.Max(1e-6f, 0.5f);
            var y = dims.Curvature01 * 0.08f * (1f - zNorm * zNorm);

            mesh.Positions.Add(new Vector3(p.X, p.Y, y));
            // mesh.Normals.Add(Vector3.UnitY);
            // mesh.UV0.Add(new Vector2(
            //     0.5f + p.X / Math.Max(1e-6f, dims.MaxWidthM),
            //     0.5f + p.Y / Math.Max(1e-6f, dims.LengthM)));
        }

        for (int i = 0; i < segments; i++)
        {
            var a = 0;
            var b = i + 1;
            var c = ((i + 1) % segments) + 1;
            mesh.Indices.Add(a);
            mesh.Indices.Add(b);
            mesh.Indices.Add(c);
        }

        return mesh;
    }

    private static MeshData BuildCompoundEnvelopeMesh(LeafInstance leaf, int segments)
    {
        Vector2 halfSize = EstimateWholeLeafHalfSize(leaf);

        // A leaf-like envelope. Intentionally simple:
        // wider in the middle, narrower at base and tip.
        var mesh = new MeshData();

        mesh.Positions.Add(Vector3.Zero);
        // mesh.Normals.Add(Vector3.UnitY);
        // mesh.UV0.Add(new Vector2(0.5f, 0.5f));

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)segments;
            float theta = t * MathF.Tau;

            float x = MathF.Cos(theta);
            float z = MathF.Sin(theta);

            float width = halfSize.X * (0.75f + 0.25f * MathF.Pow(MathF.Abs(z), 0.8f));
            float length = halfSize.Y;

            Vector3 pos = new(x * width, 0.005f * (1f - z * z), z * length + length);

            mesh.Positions.Add(pos);
            // mesh.Normals.Add(Vector3.UnitY);
            // mesh.UV0.Add(new Vector2(0.5f + x * 0.5f, 0.5f + z * 0.5f));
        }

        for (int i = 0; i < segments; i++)
        {
            int a = 0;
            int b = i + 1;
            int c = ((i + 1) % segments) + 1;
            mesh.Indices.Add(a);
            mesh.Indices.Add(b);
            mesh.Indices.Add(c);
        }

        return mesh;
    }

    private static Vector2 EstimateWholeLeafHalfSize(LeafInstance leaf)
    {
        if (!leaf.IsCompound)
        {
            var d = leaf.Morphology.Dimensions;
            return new Vector2(d.MaxWidthM * 0.5f, d.LengthM * 0.5f);
        }

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        foreach (var lf in leaf.Leaflets)
        {
            var pos = lf.LocalTransform.Position;
            float hw = lf.Morphology.Dimensions.MaxWidthM * 0.5f;
            float hl = lf.Morphology.Dimensions.LengthM * 0.5f;

            minX = MathF.Min(minX, pos.X - hw);
            maxX = MathF.Max(maxX, pos.X + hw);
            minZ = MathF.Min(minZ, pos.Z - hl);
            maxZ = MathF.Max(maxZ, pos.Z + hl);
        }

        if (minX > maxX || minZ > maxZ)
            return new Vector2(0.03f, 0.06f);

        float width = maxX - minX;
        float length = maxZ - minZ;

        return new Vector2(width * 0.5f, length * 0.5f);
    }
}