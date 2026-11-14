using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Agro.Vegetation;

/// <summary>
/// One-sided area of a species' blade outline relative to the rectangle the simulation otherwise
/// stands it in.
/// </summary>
/// <remarks>
/// <para>
/// An organ's nominal area is the rectangle <c>Length x 2 Radius</c>; its flat outline covers
/// 0.61 (Fragaria) to 0.97 (Heuchera) of it. The tray survey reports this outline and carries the
/// factors in the document's meta. Direct ray-traced leaf area indices use the curved mesh
/// surface instead, so these factors do not convert those indices to the survey's flat area.
/// </para>
/// <para>
/// One factor per blade slice, because a segmented blade (Carex) is a chain of organs each carrying
/// its own part of the outline. Computed once per species from the same immutable templates the
/// contact geometry uses, at a unit pose with the relief ignored, because the survey reports
/// the flat blade outline rather than the curved mesh surface.
/// </para>
/// </remarks>
public static class LaminaArea
{
	static readonly ConditionalWeakTable<SpeciesSettings, float[]> Factors = new();

	/// <summary>Outline area over <c>Length x 2 Radius</c>, per blade slice.</summary>
	public static float[] OutlineFactors(SpeciesSettings species) => Factors.GetValue(species, Compute);

	static float[] Compute(SpeciesSettings species)
	{
		var count = Math.Max(1, species.LeafSegmentCount);
		var result = new float[count];
		for (int slice = 0; slice < count; ++slice)
		{
			var template = LaminaTemplates.For(species, OrganTypes.Leaf, slice);
			if (template == null || template.Indices.Length < 3)
			{
				result[slice] = 1f;
				continue;
			}
			//LocalPoint maps a template vertex to ((x + 1) L / 2, relief, -y W) with W the half
			//width, so at L = 1, W = 1 the rectangle proxy has area 2.
			var area = 0.0;
			var vertices = template.Vertices;
			var indices = template.Indices;
			for (int i = 0; i + 2 < indices.Length; i += 3)
			{
				var a = vertices[indices[i]];
				var b = vertices[indices[i + 1]];
				var c = vertices[indices[i + 2]];
				double ax = (a.X + 1f) * 0.5f, az = -a.Y;
				double bx = (b.X + 1f) * 0.5f, bz = -b.Y;
				double cx = (c.X + 1f) * 0.5f, cz = -c.Y;
				area += 0.5 * Math.Abs((bx - ax) * (cz - az) - (bz - az) * (cx - ax));
			}
			result[slice] = area > 0.0 && double.IsFinite(area) ? (float)(area / 2.0) : 1f;
		}
		return result;
	}
}
