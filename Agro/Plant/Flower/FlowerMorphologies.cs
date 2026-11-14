using System.Numerics;

namespace Agro;

/// <summary>Small shared lamina meshes for floral structures, served through the petal mesh endpoint.</summary>
public static class FlowerMorphologies
{
    /// <summary>
    /// The exposed strap of one ligulate floret, with a narrow basal tube junction and
    /// five shallow distal teeth. Length and width describe the reference mesh relief;
    /// each organ's actual dimensions still come from FlowerSettings.
    /// </summary>
    /// <remarks>
    /// Taraxacum: NSW Flora, genus description, truncate ligules with five apical teeth.
    /// https://plantnet.rbgsyd.nsw.gov.au/cgi-bin/NSWfl.pl?page=nswfl&amp;lvl=gn&amp;name=Taraxacum
    /// Lactuca serriola: Gibson and Prigge, Vascular Plants of Williamson County,
    /// strap-shaped limb with four or five shallow teeth.
    /// https://w3.biosci.utexas.edu/prc/K12/pages/Lactuca%20serriola.html
    /// Tubes, styles and the involucre are not separate organs in this approximation.
    /// </remarks>
    public static LeafMorphology Ligule(float length, float width)
    {
        if (!float.IsFinite(length) || length <= 0f)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (!float.IsFinite(width) || width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));

        // Coordinates follow the existing petal contract: attachment x=-1, distal
        // extent x=1, transverse extent +/-1. Eighteen triangles retain the five teeth
        // even at the coarse LOD used by collision, export and both viewport backends.
        Vector2[] outline =
        [
            new(-1f, 0f), new(-.82f, -.18f), new(-.3f, -.55f), new(.4f, -.95f),
            new(.88f, -1f), new(1f, -.8f), new(.9f, -.6f), new(1f, -.4f),
            new(.9f, -.2f), new(1f, 0f), new(.9f, .2f), new(1f, .4f),
            new(.9f, .6f), new(1f, .8f), new(.88f, 1f), new(.4f, .95f),
            new(-.3f, .55f), new(-.82f, .18f)
        ];
        var vertices = new List<Vector3>(outline.Length + 1) { Vertex(new(.2f, 0f)) };
        foreach (var point in outline) vertices.Add(Vertex(point));
        var indices = new List<int>(outline.Length * 3);
        for (var i = 0; i < outline.Length; ++i)
        {
            indices.Add(0);
            indices.Add(i + 1);
            indices.Add((i + 1) % outline.Length + 1);
        }
        return new LeafMorphology
        {
            Shape = new LanceolateLeafShape(),
            Margin = LeafMarginType.Dentate,
            Venation = LeafVenationType.Parallel,
            Apex = LeafApexType.Obtuse,
            Base = LeafBaseType.Cuneate,
            Dimensions = LeafDimensions.Create(lengthM: length, widthM: width,
                thicknessM: .0001f, petioleM: 0f, curvature01: .1f),
            LowResVertices = vertices,
            LowResIndices = indices,
            HighResVertices = vertices,
            HighResIndices = indices
        };

        Vector3 Vertex(Vector2 point) => new(point.X, point.Y,
            width * (.04f * (point.X + 1f) + .05f * point.Y * point.Y));
    }
}
