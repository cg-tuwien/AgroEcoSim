using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using glTFLoader.Schema;
using NumericHelpers;
using System.Collections;

namespace Agro;

public readonly struct GatherDataBase
{
    public readonly float LifesupportEnergy;
    public readonly float PhotosynthWater;
    public readonly float CapacityEnergy;
    public readonly float CapacityWater;
    public readonly float ResourcesEfficiency;
    public readonly float ProductionEfficiency;

    public GatherDataBase(float lifesuppoprtEnergy, float photosynthWater, float capacityEnergy, float capacityWater, float resourcesEfficiency, float productionEfficiency)
    {
        LifesupportEnergy = lifesuppoprtEnergy;
        PhotosynthWater = photosynthWater;
        CapacityEnergy = capacityEnergy;
        CapacityWater = capacityWater;
        ResourcesEfficiency = resourcesEfficiency;
        ProductionEfficiency = productionEfficiency;
    }
}

// public readonly struct GatherEfficiency
// {
//     public readonly float ResourceEfficiency;
//     public readonly float ProductionEfficiency;

//     public GatherEfficiency(float resource, float production)
//     {
//         ResourceEfficiency = resource;
//         ProductionEfficiency = production;
//     }

//     public GatherEfficiency Max(GatherEfficiency other)
//     {
//         return new(
//             ResourceEfficiency > other.ResourceEfficiency ? ResourceEfficiency : other.ResourceEfficiency,
//             ProductionEfficiency > other.ProductionEfficiency ? ProductionEfficiency : other.ProductionEfficiency
//         );
//     }

//     public static readonly GatherEfficiency ONE = new (1f, 1f);
// }