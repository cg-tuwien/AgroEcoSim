using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Agro;

/// <summary>Content-addressed immutable geometry for self-contained V8 keyframes.</summary>
internal static class SceneGeometryAssets
{
    internal sealed record Asset(string Id, byte[] Payload);
    static readonly ConditionalWeakTable<SpeciesSettings, Asset> Cache = new();

    internal static Asset For(SpeciesSettings species) => Cache.GetValue(species, static s =>
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write((byte)s.ContactDeformationMode);
        // Version one remains byte-identical for existing presets. Version two adds
        // fruit meshes after the existing leaf/petal table; deformation still uses V1.
        var fruit = s.FruitSettings?.Enabled == true;
        writer.Write(fruit ? (byte)2 : ContactDeformation.ProfileVersion);
        var count = Math.Max(1, s.LeafSegmentCount);
        writer.Write(count);
        for (var i = 0; i < count; ++i) Write(LaminaTemplates.For(s,OrganTypes.Leaf,i)!);
        Write(LaminaTemplates.For(s,OrganTypes.FlowerPadel)!);
        if (fruit)
        {
            Write(FruitGeometry.Body);
            var wings = s.FruitSettings!.WingsPerFruit > 0;
            writer.Write(wings);
            if (wings) Write(LaminaTemplates.For(s, OrganTypes.FruitWing)!);
        }
        void Write(LaminaTemplate template)
        {
            template.AuthoredMesh().WriteMesh(writer);
            foreach(var height in template.HingeHeights) writer.Write(height);
        }
        writer.Flush();
        var data = stream.ToArray();
        return new(Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), data);
    });

    internal static byte[] Table(IEnumerable<SpeciesSettings> species)
    {
        var assets = species.Select(For).DistinctBy(a => a.Id).OrderBy(a => a.Id, StringComparer.Ordinal).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(assets.Length);
        foreach (var asset in assets)
        { writer.Write(asset.Id); writer.Write(asset.Payload.Length); writer.Write(asset.Payload); }
        writer.Flush();
        return stream.ToArray();
    }
}
