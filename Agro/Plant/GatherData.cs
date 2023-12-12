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