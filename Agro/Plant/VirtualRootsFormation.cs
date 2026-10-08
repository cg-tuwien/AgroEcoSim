using System.Numerics;
using System.Runtime.CompilerServices;
using AgentsSystem;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

public class VirtualRootsFormation : IPlantSubFormation<UnderGroundAgent>
{
    const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
    //Roots volume centered at [0.5, 1, 0.5] in local coordinates
    public Vector3 Size { get; private set; }= new(2e-3f, 1e-3f, 2e-3f);

    public bool HasUndeliveredPost => false;

    public int Count => 1;

    public bool Alive { get; private set; } = true;

    public float DailyResourceMax { get; private set; }
    public float DailyProductionMax { get; private set; }
    public float DailyEfficiencyMax { get; private set; }

    readonly PlantFormation2 Plant;

    float Water_g;
    float Energy;

    [M(AI)]public float Volume() => Size.X * Size.Y * Size.Z;
    /// <summary>
    /// How much of the bounding box volume is actually occupied by roots
    /// </summary>
    const float VolumeOccupancyFactor = 0.001f;
    [M(AI)]public float LifeSupportPerHour() => UnderGroundAgent.LifeSupportFactor * VolumeOccupancyFactor * Volume();

	[M(AI)]public float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

    [M(AI)]public float WaterStorageCapacity_g() => Volume() * UnderGroundAgent.WaterCapacityRatio * VolumeOccupancyFactor * UnderGroundAgent.CubicMetersToGrammsOfWater;

    public VirtualRootsFormation(PlantFormation2 plant)
    {
        Plant = plant;
    }

    public void Census() { }

    public void DeliverPost(uint timestep) { }

    /// <summary>
    /// The whole of this formation's state: a size, a liveness flag and two resource pools.
    ///
    /// It stands in for a root system rather than simulating one, so there are no agents to write -
    /// which is exactly why it needs its own implementation instead of inheriting the array blit.
    /// </summary>
    public void WriteState(BinaryWriter writer)
    {
        writer.Write(Alive);
        writer.Write(Size.X); writer.Write(Size.Y); writer.Write(Size.Z);
        writer.Write(Water_g);
        writer.Write(Energy);
        writer.Write(DailyResourceMax);
        writer.Write(DailyProductionMax);
        writer.Write(DailyEfficiencyMax);
    }

    public void ReadState(BinaryReader reader)
    {
        Alive = reader.ReadBoolean();
        Size = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        Water_g = reader.ReadSingle();
        Energy = reader.ReadSingle();
        DailyResourceMax = reader.ReadSingle();
        DailyProductionMax = reader.ReadSingle();
        DailyEfficiencyMax = reader.ReadSingle();
    }

    internal void Birth(float water_g, float energy)
    {
        Water_g = water_g;
        Energy = energy;
    }

    public void Tick(uint timestep)
    {
		var world = Plant.World;
		var species = Plant.Parameters;

		//TODO perhaps it should reflect temperature
		var lifeSupportPerHour = ResourceUnits.Enabled(species)
			? ResourceUnits.MaintenancePerHour(VolumeOccupancyFactor * Volume(), 0f, species)
			: LifeSupportPerHour();

		//life support
		Energy -= lifeSupportPerHour * world.HoursPerTick;

		//Debug.WriteLine($"{timestep} / {formationID}  W {Water} E {Energy} L {Length} R {Radius}");
		//var waterFactor = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
		///////////////////////////
		// #region Growth
		// ///////////////////////////
		// if (Energy > lifeSupportPerHour * 240) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		// {
		// 	//TDMI 2023-03-07 Incorporate water capacity factor
		// 	if (DailyProductionMax > 0)
		// 	{

		// 		var growthBase = (PreviousDayProductionInvariant / DailyProductionMax + resourcesAvailability) * 0.5f;
		// 		var radiusChildGrowth = childrenCount <= 1 ? 1 : MathF.Pow(childrenCount, GrowthDeclineByExpChildren / 2);
		// 		var (radiusGrowthBase, lengthGrowthBase) = (1e-5f * growthBase, 2e-4f * growthBase);
		// 		var newWaterAbsorbtion = mWaterAbsorbtionFactor;
		// 		//Debug.WriteLine($"{formationID:D5}:\t{newWaterAbsorbtion:F4}");

		// 		var ld = Length * Radius * 4f;
		// 		var volume = ld * Length;
		// 		if (volume * newWaterAbsorbtion < 288)
		// 		{
		// 			float maxRadius = Parent == -1 ? plant.AG.GetBaseRadius(0) * 1.25f : formation.GetBaseRadius(Parent);
		// 			if (Radius <= maxRadius)
		// 			{
		// 				var d = 1f - 0.7f * formation.GetRelDepth(formationID);
		// 				var radiusGrowth = radiusGrowthBase * d * d / (radiusChildGrowth * MathF.Pow(ld * Radius, 0.2f));
		// 				Radius += radiusGrowth;
		// 				if (Radius > maxRadius) Radius = maxRadius;
		// 				newWaterAbsorbtion -= radiusGrowth * childrenCount;  //become wood faster with children
		// 				if (newWaterAbsorbtion < 0f) newWaterAbsorbtion = 0f;
		// 			}

		// 			if (childrenCount == 1)
		// 				Length += lengthGrowthBase / MathF.Pow(volume, 0.1f);
		// 		}

		// 		mWaterAbsorbtionFactor = newWaterAbsorbtion;
		// 	}
		// }
		// else if (Energy <= 0f) //Without energy the part dies
		// {
		// 	//Console.WriteLine($"Root {formationID} depleeted at time {timestep}");
        //     Alive = false;
		// 	return;
		// }
		// #endregion

		// ///////////////////////////
		// #region Absorb WATER from soil
		// ///////////////////////////
		// {
		// 	var waterCapacity = WaterStorageCapacity_g();
		// 	if (Water_g < waterCapacity)
		// 	{
		// 		var soil = plant.Soil;
		// 		var baseCenter = formation.GetBaseCenter(formationID);
		// 		var samplePoint = baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f;
		// 		//find all soild cells that the shpere intersects
		// 		var source = soil.IntersectPoint(samplePoint, plant.SoilIndex); //TODO make a tube intersection

		// 		var vegetativeTemp = plant.VegetativeLowTemperature;

		// 		if (source >= 0) //TODO this is a rough approximation taking only the first intersected soil cell
		// 		{
		// 			var amount = WaterAbsorbtionPerTick_g(world);
		// 			var soilTemperature = soil.GetTemperature(source, plant.SoilIndex);
		// 			if (soilTemperature > vegetativeTemp.X)
		// 			{
		// 				if (soilTemperature < vegetativeTemp.Y)
		// 					amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
		// 				soil.RequestWater(source, Math.Min(waterCapacity - Water_g, amount), formation, formationID, plant.SoilIndex); //TODO change to tube surface!
		// 			}

		// 			CurrentDayEnvResourcesInvariant += soil.GetWater_g(source, plant.SoilIndex);
		// 		}
		// 		else //growing outside of the world
		// 			formation.Death(formationID);
		// 	}
		// }
		// else
		// 	mWaterAbsorbtionFactor = 0f;
		// #endregion
    }

    public void FirstDay()
    {
        throw new NotImplementedException();
    }

    public void NewDay(uint timestep, byte ticksPerDay)
    {
		DailyResourceMax = float.MinValue;
		DailyProductionMax = float.MinValue;
		DailyEfficiencyMax = float.MinValue;
    }

    public void Distribute(PlantGlobalStats stats)
    {
        throw new NotImplementedException();
    }

    public PlantGlobalStats Gather()
    {
        throw new NotImplementedException();
    }

    public bool SendProtected(int part, IMessage<UnderGroundAgent> msg)
    {
        throw new NotImplementedException();
    }
}