using Agro;

namespace AgroServer.Models;

/// <summary>
/// Leaf area index and solar load of one obstacle over the whole simulation, one entry per
/// recorded step of <see cref="SimulationResponse.ObstacleLightTimesteps"/>.
/// </summary>
/// <remarks>
/// The series are columnar so that a client can plot one metric without walking objects. Note the
/// volume: a facade model selected by a broad substring can hold a couple of thousand objects, and
/// every one of them carries six values per simulation step. Narrow
/// <see cref="SimulationRequest.LeafAreaIndexObstacles"/> when that is too much.
/// </remarks>
public class ObstacleLightResponse
{
    ///<summary>Name of the source object.</summary>
    public string Name { get; set; }
    ///<summary>Total triangle area of the obstacle in m², both faces of a solid included.</summary>
    public float Area { get; set; }
    ///<summary>Share of <see cref="Area"/> that can see the sky past the building itself and
    ///therefore carries the reported values, in m².</summary>
    public float ExposedArea { get; set; }
    ///<summary>Total one-sided leaf area per unit of facade area, the classic definition.</summary>
    public float[] LeafAreaIndex { get; set; }
    ///<summary>Leaf area projected onto the surface per unit of surface area.</summary>
    public float[] ProjectedLeafAreaIndex { get; set; }
    ///<summary>Fraction of the direct beam reaching the surface without meeting foliage.</summary>
    public float[] GapFractionBeam { get; set; }
    ///<summary>Fraction of the cosine-weighted sky hemisphere free of foliage.</summary>
    public float[] GapFractionDiffuse { get; set; }
    ///<summary>Beer-Lambert inversion of <see cref="GapFractionDiffuse"/>, the value a canopy
    ///analyser would report.</summary>
    public float[] LeafAreaIndexFromGaps { get; set; }
    ///<summary>Shortwave irradiation reaching the surface, Wh/m² per step.</summary>
    public float[] Irradiance { get; set; }
    ///<summary>The same with the vegetation removed, Wh/m² per step. The difference times
    ///<see cref="ExposedArea"/> is the radiant energy the plants kept off the surface.</summary>
    public float[] IrradianceOpenSky { get; set; }

    public static List<ObstacleLightResponse> From(ObstacleLightMeter? meter)
    {
        if (meter == null)
            return null;

        var result = new List<ObstacleLightResponse>(meter.Targets.Count);
        foreach (var target in meter.Targets)
        {
            var count = target.Samples.Count;
            var item = new ObstacleLightResponse
            {
                Name = target.Name,
                Area = target.Area_m2,
                ExposedArea = target.ExposedArea_m2,
                LeafAreaIndex = new float[count],
                ProjectedLeafAreaIndex = new float[count],
                GapFractionBeam = new float[count],
                GapFractionDiffuse = new float[count],
                LeafAreaIndexFromGaps = new float[count],
                Irradiance = new float[count],
                IrradianceOpenSky = new float[count],
            };
            for (int i = 0; i < count; ++i)
            {
                var sample = target.Samples[i];
                item.LeafAreaIndex[i] = sample.LeafAreaIndex;
                item.ProjectedLeafAreaIndex[i] = sample.ProjectedLeafAreaIndex;
                item.GapFractionBeam[i] = sample.GapFractionBeam;
                item.GapFractionDiffuse[i] = sample.GapFractionDiffuse;
                item.LeafAreaIndexFromGaps[i] = sample.LeafAreaIndexFromGaps;
                item.Irradiance[i] = sample.Irradiance_Wh_m2;
                item.IrradianceOpenSky[i] = sample.IrradianceOpenSky_Wh_m2;
            }
            result.Add(item);
        }
        return result;
    }
}
