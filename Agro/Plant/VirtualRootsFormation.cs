using System.Numerics;
using System.Runtime.CompilerServices;
using AgentsSystem;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

public class VirtualRootsFormation : IPlantSubFormation<UnderGroundAgent>
{
    const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
    //Roots volume centered at [0.5, 1, 0.5] in local coordinates
    public Vector3 Size { get; private set; } = new(2e-3f, 1e-3f, 2e-3f);

    public bool HasUndeliveredPost => false;

    public int Count => 1;

    public bool Alive { get; private set; } = true;

    public float DailyResourceMax { get; private set; }
    public float DailyProductionMax { get; private set; }
    public float DailyEfficiencyMax { get; private set; }

    readonly PlantFormation2 Plant;

    float Water_g;
    float Energy;
    float StarvationHours;
    readonly PlantGlobalStats Stats = new();
    readonly List<int> UptakeCells = new(27);

    [M(AI)]public float Volume() => Size.X * Size.Y * Size.Z;
    /// <summary>
    /// How much of the bounding box volume is actually occupied by roots
    /// </summary>
    const float VolumeOccupancyFactor = 0.001f;
    [M(AI)]public float LifeSupportPerHour() => UnderGroundAgent.LifeSupportFactor * VolumeOccupancyFactor * Volume();

	[M(AI)]public float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

    [M(AI)]public float WaterStorageCapacity_g() => Volume() * UnderGroundAgent.WaterCapacityRatio * VolumeOccupancyFactor * UnderGroundAgent.CubicMetersToGrammsOfWater;

    // The occupied horizontal cross-section represents the aggregate basal
    // roots. Use the same modeled transport velocity as explicit root axes.
    [M(AI)]float WaterFlowToShootPerTick_g() => Size.X * Size.Z * VolumeOccupancyFactor
        * UnderGroundAgent.WaterTransportDistance * UnderGroundAgent.CubicMetersToGrammsOfWater * Plant.World.HoursPerTick;

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
        writer.Write(StarvationHours);
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
        StarvationHours = reader.ReadSingle();
    }

    internal void Birth(float water_g, float energy)
    {
        Water_g = water_g;
        Energy = energy;
    }

    internal void InitializeWoody(float radius, float depth)
        => Size = new(radius * 2f, depth, radius * 2f);

    internal void EndLife()
    {
        Alive = false;
        Water_g = Energy = 0f;
    }

    public void Tick(uint timestep)
    {
		var world = Plant.World;
		var species = Plant.Parameters;
        if (!Alive) return;

		//TODO perhaps it should reflect temperature
		var lifeSupportPerHour = ResourceUnits.Enabled(species)
			? ResourceUnits.MaintenancePerHour(VolumeOccupancyFactor * Volume(), 0f, species)
			: LifeSupportPerHour();

		//life support
		Energy -= lifeSupportPerHour * world.HoursPerTick;
        if (Energy < 0f)
        {
            StarvationHours += world.HoursPerTick;
            Energy = 0f;
            if (StarvationHours >= 24f * 60f)
            {
                EndLife();
                return;
            }
        }
        else
            StarvationHours = 0f;

        // A bounded aggregate root volume. The occupied fraction represents roots,
        // not a solid block of root tissue. Sample at most 27 soil cells per tick.
        var thermal = Plant.VegetativeTemperatureFactor(timestep);
        if (Energy > lifeSupportPerHour * 240f && thermal > 0f)
        {
            var rate = species.Behavior == Behavior.Woody ? .25f : .08f;
            var step = rate * world.HoursPerTick / (365f * 24f) * thermal;
            var maxRadius = species.Behavior == Behavior.Woody ? MathF.Max(1f, species.TreeCrownRadius) : .5f;
            var target = new Vector3(maxRadius * 2f, MathF.Min(2f, maxRadius), maxRadius * 2f);
            var proposed = Vector3.Min(target, Size + new Vector3(step, step * .5f, step));
            if (species.ConstructionEnergyPerDryGram > 0f)
            {
                var before = Volume();
                var available = MathF.Max(0f, Energy - lifeSupportPerHour * 240f);
                float Cost(Vector3 box) => ConstructionBudget.Cost((box.X * box.Y * box.Z - before) * VolumeOccupancyFactor, species, 0f);
                if (Cost(proposed) > available)
                {
                    var low = 0f;
                    var high = 1f;
                    for (int iteration = 0; iteration < 24; iteration++)
                    {
                        var middle = (low + high) * .5f;
                        if (Cost(Vector3.Lerp(Size, proposed, middle)) <= available)
                            low = middle;
                        else
                            high = middle;
                    }
                    proposed = Vector3.Lerp(Size, proposed, low);
                }
                Energy -= Cost(proposed);
            }
            Size = proposed;
        }
        UptakeCells.Clear();
        // Soil intersection queries use tray-local coordinates, as explicit root agents do.
        var origin = Plant.Position;
        for (int x = -1; x <= 1; x++)
        for (int z = -1; z <= 1; z++)
        for (int depth = 1; depth <= 3; depth++)
        {
            var point = origin + new Vector3(x * Size.X * .35f, -depth * Size.Y * .25f, z * Size.Z * .35f);
            var cell = Plant.Soil.IntersectPoint(point, Plant.SoilIndex);
            if (cell >= 0 && !UptakeCells.Contains(cell))
                UptakeCells.Add(cell);
        }
        var capacity = WaterStorageCapacity_g();
        if (UptakeCells.Count > 0 && Plant.CanRequestSoilWater)
        {
            var woody = species.Behavior == Behavior.Woody;
            var requested = woody
                ? MathF.Max(0, capacity - Water_g) + WaterFlowToShootPerTick_g()
                : MathF.Min(MathF.Max(0, capacity - Water_g), capacity * world.HoursPerTick * .25f);
            var share = requested / UptakeCells.Count;
            float available = 0;
            foreach (var cell in UptakeCells)
            {
                available += Plant.Soil.GetWater_g(cell, Plant.SoilIndex);
                var temperatureFactor = woody
                    ? Math.Clamp((Plant.Soil.GetTemperature(cell, Plant.SoilIndex) - Plant.VegetativeLowTemperature.X)
                        / (Plant.VegetativeLowTemperature.Y - Plant.VegetativeLowTemperature.X), 0f, 1f)
                    : 1f;
                if (share > 0 && temperatureFactor > 0)
                    Plant.WaterRequests.Add((cell, 0, share * temperatureFactor));
            }
            DailyResourceMax = MathF.Max(1f, available);
        }

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
        DailyResourceMax = DailyProductionMax = DailyEfficiencyMax = 1f;
    }

    public void NewDay(uint timestep, byte ticksPerDay)
    {
		DailyResourceMax = MathF.Max(1f, DailyResourceMax);
		DailyProductionMax = MathF.Max(1f, DailyProductionMax);
		DailyEfficiencyMax = 1f;
    }

    public void Distribute(PlantGlobalStats stats)
    {
        Energy = stats.ReceivedEnergy[0];
        Water_g = stats.ReceivedWater[0];
    }

    public PlantGlobalStats Gather()
    {
        var species = Plant.Parameters;
        var maintenance = (ResourceUnits.Enabled(species)
            ? ResourceUnits.MaintenancePerHour(VolumeOccupancyFactor * Volume(), 0f, species)
            : LifeSupportPerHour()) * Plant.World.HoursPerTick;
        var energyCapacity = VolumeOccupancyFactor * Volume() * 24f * 31f * 3f;
        var waterCapacity = WaterStorageCapacity_g();
        Stats.Energy = Alive ? MathF.Max(0, Energy) : 0;
        Stats.Water = Alive ? Water_g : 0;
        Stats.EnergyRequirementPerTick = Alive ? maintenance : 0;
        Stats.WaterRequirementPerTick = 0;
        Stats.EnergyCapacity = Alive ? energyCapacity : 0;
        Stats.WaterCapacity = Alive ? waterCapacity : 0;
        Stats.WaterPriorityPerTick = 0;
        Stats.BasalWaterFlowPerTick = Alive && species.Behavior == Behavior.Woody ? WaterFlowToShootPerTick_g() : 0d;
        Stats.EnergyDiff = Stats.WaterDiff = 0;
        Stats.Gathering.Clear();
        Stats.Gathering.Add(new(Alive ? maintenance : 0, 0, Alive ? energyCapacity : 0, Alive ? waterCapacity : 0, 1, 1));
        return Stats;
    }

    public bool SendProtected(int part, IMessage<UnderGroundAgent> msg)
    {
        if (!Alive || part != 0 || msg is not UnderGroundAgent.WaterInc receipt)
            return false;
        Water_g += receipt.Amount;
        DailyProductionMax = MathF.Max(DailyProductionMax, receipt.Amount / MathF.Max(1e-8f, Size.X * Size.Z));
        return true;
    }
}
