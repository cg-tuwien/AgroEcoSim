using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;
using System.Text.Json.Serialization;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
[StructLayout(LayoutKind.Auto)]
public struct UnderGroundAgent : IPlantAgent
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	///////////////////////////
	#region DATA
	///////////////////////////
	/// <summary>
	/// Simulation step when the agent was created
	/// </summary>
	public uint BirthTime { get; private init; }
    public bool isRizome { get; private set; } = false;
	public bool flowerSupport { get; private set; } = false;
    public Vector3 Color { get; private set; }
    /// <summary>
    /// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
    /// </summary>
    [JsonIgnore]
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Length of the agent in meters.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>
	/// Radius of the bottom face in meters.
	/// </summary>
	public float Radius { get; private set; }

	public readonly byte DominanceLevel => 0;

	/// <summary>
	/// Non-uniform scale vector along the local axes
	/// </summary>
	[M(AI)]public readonly Vector3 Scale() => new(Length, 2f * Radius, 2f * Radius);

	/// <summary>
	/// Volume of the agent in m³.
	/// </summary>
	[M(AI)]public readonly float Volume() => 4f * Length * Radius * Radius;

	[M(AI)]public readonly float Surface() => Radius * Length;

	/// <summary>
	/// Agent energy (umbrella for mainly sugars created by photosynthesis, in custom units)
	/// </summary>
	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in gramms
	/// </summary>
	public float Water_g { get; private set; }

	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	public float Auxins { get; set; }
	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	//public float Cytokinins { get; set; }

	/// <summary>
	/// Allocation of water during the previous day, gramms of water per m² i.e. invariant of surface
	/// </summary>
	public float PreviousDayProductionInvariant {get; private set; }
	/// <summary>
	/// Allocation of water during ongoing day, gramms of water per m² i.e. invariant of surface
	/// </summary>
	public float CurrentDayProductionInvariant_g_per_m2 { get; set; }

	/// <summary>
	/// Resources available during the previous day, averaged, in gramms of water
	/// </summary>
	public float PreviousDayEnvResourcesInvariant { get; private set; }
	/// <summary>
	/// Resources available during the ongoing day, averaged, in gramms of water
	/// </summary>
	public float CurrentDayEnvResourcesInvariant { get; set; }

	public readonly float PreviousDayEnvResources => PreviousDayEnvResourcesInvariant;

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less water the root can absorb.
	/// </summary>
	float mWaterAbsorbtionFactor;

	/// <summary>
	/// Woodyness ∈ [0, 1].
	/// </summary>
	[M(AI)]public readonly float WoodRatio() => 1f - mWaterAbsorbtionFactor;

	public readonly OrganTypes Organ => OrganTypes.Root;

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }


    //gravity - not used in undergroundAgent
    public Quaternion baseOrientation { get; set; }
    public Quaternion restOrientation { get; set; }
    public Quaternion targetOrientation { get; set; }

    public Vector3 BaseOffset => throw new NotImplementedException();

    public float Stress => throw new NotImplementedException();
    #endregion

    #region Variances
    /// <summary>
    /// Precomputed random variance of maximum length for this agent
    /// </summary>
    readonly float LengthVar;
	#endregion

	///////////////////////////
	#region Constants and computed data
	///////////////////////////

	public const float CubicMetersToGrammsOfWater = 1e6f;

	/// <summary>
	/// Recommended initial length of the agent at birth in m.
	/// </summary>
	public const float InitialLength = 1e-5f;

	/// <summary>
	/// Recommended initial bottom face radius of the agent at birth in m.
	/// </summary>
	public const float InitialRadius = 0.2e-5f;

	/// <summary>
	/// The growth rate is reduced exponentially wrt. children count, i.e. childrenCount^GrowthDeclineByExpChildren
	/// </summary>
	public const float GrowthDeclineByExpChildren = 5;

	/// <summary>
	/// Water amount in g that can be absorbed per m² of root surface per hour
	/// </summary>
	public const float WaterAbsortionRatio_g_per_h_m2 = 1500f * UnderGroundAgent.CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water amount in g which can be absorbed from soil per hour
	/// </summary>
	[M(AI)]public readonly float WaterAbsorbtionPerHour_g() => Surface() * WaterAbsortionRatio_g_per_h_m2;

	/// <summary>
	/// Water amount in g which can be absorbed from soil per timestep
	/// </summary>
	[M(AI)]public readonly float WaterAbsorbtionPerTick_g(AgroWorld world) => WaterAbsorbtionPerHour_g() * world.HoursPerTick;

	//Let's assume (I might be fully wrong) that the plant can push the water 0.5mm in 1s, then in 1h it can push it 0.001 * 30 * 60 = 1.8m
	//also see - interestingly it states that while pholem is not photosensitive, xylem is
	//https://www.researchgate.net/publication/238417831_Simultaneous_measurement_of_water_flow_velocity_and_solute_transport_in_xylem_and_phloem_of_adult_plants_of_Ricinus_communis_during_day_time_course_by_nuclear_magnetic_resonance_NMR_spectrometry
	public const float WaterTransportDistance = 1.8f;

	/// <summary>
	/// Water amount in g which can be passed to the parent per hour
	/// </summary>
	[M(AI)]public readonly float WaterFlowToParentPerHour_g() => 4f * Radius * Radius * WaterTransportDistance * (2f - mWaterAbsorbtionFactor) * CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water volume in g which can be passed to the parent per timestep
	/// </summary>
	[M(AI)]public readonly float WaterFlowToParentPerTick_g(AgroWorld world) => WaterFlowToParentPerHour_g() * world.HoursPerTick;

	public const float EnergyTransportRatio = 2f;

	[M(AI)]public readonly float EnergyFlowToParentPerHour() => 4f * Radius * Radius * EnergyTransportRatio * (2f - mWaterAbsorbtionFactor);

	[M(AI)]public readonly float EnergyFlowToParentPerTick(AgroWorld world) => EnergyFlowToParentPerHour() * world.HoursPerTick;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	internal const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in gramms which can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterStorageCapacity_g() => Volume() * WaterCapacityRatio * CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water volume in g which can flow through per hour, or can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterTotalCapacityPerHour_g() => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportDistance) * CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water volume in g which can flow through per tick, or can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterTotalCapacityPerTick_g(AgroWorld world) => WaterTotalCapacityPerHour_g() * world.HoursPerTick;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

    [M(AI)]readonly float EnergyCapacityFunc(float radius, float length) => 4f * radius * radius * length * (1f - WaterCapacityRatio) * EnergyStorageCoef * MathF.Pow(2f - mWaterAbsorbtionFactor, 3);

	public readonly float EnergyStorageCapacity() => EnergyCapacityFunc(Radius, Length);

    internal const float LifeSupportFactor = 0.01f;
	[M(AI)]public readonly float LifeSupportPerHour() => LifeSupportFactor * Volume() * mWaterAbsorbtionFactor;

	[M(AI)]public readonly float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

	[M(AI)]public readonly float PhotosynthPerTick(AgroWorld world) => 0f;

	public readonly static Quaternion OrientationDown = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f);

	#endregion

	public UnderGroundAgent(PlantFormation2 plant, uint timestep, int parent, Quaternion orientation, float initialEnergy, float initialWater_g = 0f, float initialWaterIntake = 1f, float radius = InitialRadius, float length = InitialLength, float initialResources = 0f, float initialProduction = 0f)
	{
		BirthTime = timestep;
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Energy = initialEnergy;
		Water_g = initialWater_g;
		mWaterAbsorbtionFactor = initialWaterIntake;

		Auxins = 0f;
		//Cytokinins = 0f;

		PreviousDayProductionInvariant = WaterAbsorbtionPerTick_g(plant.World); //initialProduction;
		CurrentDayProductionInvariant_g_per_m2 = 0f;
		PreviousDayEnvResourcesInvariant = initialResources;
		CurrentDayEnvResourcesInvariant = 0f;

		LengthVar = PlantFormation2.RootSegmentLength * 0.6f + plant.RNG.NextFloatVar(PlantFormation2.RootSegmentLength * 0.4f);
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	[M(AI)]public static void Reindex(UnderGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	public void CensusUpdateParent(int newParent) => Parent = newParent;

	const float BranchingFactor = 0.02f;
	static readonly float[] BranchingByChildren = [
		BranchingFactor * 1024f,
		BranchingFactor * 6144f,
		BranchingFactor * 524288f,
		BranchingFactor * 100663296f,
		BranchingFactor * 1879048192f,
		//BranchingFactor * 34359738368f,
		BranchingFactor * 618475290624f,
		//BranchingFactor * 10995116277760f,
		//BranchingFactor * 193514046488576f,
		BranchingFactor * 3377699720527872f,
		//BranchingFactor * 58546795155816448f,
		//BranchingFactor * 1008806316530991104f,
		//BranchingFactor * 17293822569102704640f,
		float.MaxValue];

	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		//Console.WriteLine($"{timestep} x {formationID}: w={Water} e={Energy} waf={WaterAbsorbtionFactor}");
		var formation = (PlantSubFormation<UnderGroundAgent>)_formation;
		var plant = formation.Plant;
		var world = plant.World;
		var species = plant.Parameters;

		//TODO perhaps it should reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour();

		//life support
		Energy -= lifeSupportPerHour * world.HoursPerTick;

		var children = formation.GetChildren(formationID);
		//Debug.WriteLine($"{timestep} / {formationID}  W {Water} E {Energy} L {Length} R {Radius}");
		//var waterFactor = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
		var resourcesAvailability = formation.DailyResourceMax > 1e-6f ? PreviousDayEnvResourcesInvariant / formation.DailyResourceMax : 0f;
		///////////////////////////
		#region Growth
		///////////////////////////
		if (Energy > lifeSupportPerHour * 240) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			var childrenCount = children.Count + 1;
			//TDMI 2023-03-07 Incorporate water capacity factor
			if (formation.DailyProductionMax > 1e-6f)
			{
				var growthBase = (PreviousDayProductionInvariant / formation.DailyProductionMax + resourcesAvailability) * 0.5f;
				var radiusChildGrowth = childrenCount <= 1 ? 1 : MathF.Pow(childrenCount, GrowthDeclineByExpChildren / 2);
				var (radiusGrowthBase, lengthGrowthBase) = (1e-5f * growthBase, 2e-4f * growthBase);
				var newWaterAbsorbtion = mWaterAbsorbtionFactor;
				//Debug.WriteLine($"{formationID:D5}:\t{newWaterAbsorbtion:F4}");

				var ld = Length * Radius * 4f;
				var volume = ld * Length;
				if (volume * newWaterAbsorbtion < 288)
				{
					float maxRadius = Parent == -1 ? plant.AG.GetBaseRadius(0) * 1.25f : formation.GetBaseRadius(Parent);
					if (Radius <= maxRadius)
					{
						var d = 1f - 0.7f * formation.GetRelDepth(formationID);
						var radiusGrowth = radiusGrowthBase * d * d / (radiusChildGrowth * MathF.Pow(ld * Radius, 0.2f));
						Radius += radiusGrowth;
						if (Radius > maxRadius) Radius = maxRadius;
						newWaterAbsorbtion -= radiusGrowth * childrenCount;  //become wood faster with children
						if (newWaterAbsorbtion < 0f) newWaterAbsorbtion = 0f;
					}

					if (childrenCount == 1)
						Length += lengthGrowthBase / MathF.Pow(volume, 0.1f);
				}

				mWaterAbsorbtionFactor = newWaterAbsorbtion;
			}

			//Chaining
			if (children.Count == 0 && Length >= LengthVar)
			{
				var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
				formation.Birth(new(plant, timestep, formationID, RandomOrientation(plant, species, Orientation), energy, initialResources: PreviousDayEnvResourcesInvariant, initialProduction: PreviousDayProductionInvariant));
				Energy -= 2f * energy; //twice because some energy is needed for the birth itself
				//Console.WriteLine($"New root chained to {formationID} at time {timestep}");
			}

			//Branching
			if (childrenCount > 0 && childrenCount < 8 && Radius > 1e-3f)
			{
				// const float yFactor = 0.5f;
				// const float zFactor = 0.2f;
				//Debug.WriteLine($"{plant.WaterBalance * species.RootsSparsity * MathF.Pow(childrenCount, childrenCount << 2) / (world.HoursPerTick * PreviousDayProductionInv)} = {plant.WaterBalance} * {species.RootsSparsity} * {MathF.Pow(childrenCount, childrenCount << 2)} / ({world.HoursPerTick * PreviousDayProductionInv})");
				//Debug.WriteLine($"{PreviousDayEnvResourcesInv} {PreviousDayProductionInv}");
				//Debug.WriteLine($"{PreviousDayProductionInv} / ({plant.WaterBalanceUG} * {species.RootsSparsity} * {BranchingByChildren[children.Count]}) = {PreviousDayProductionInv / (plant.WaterBalance * plant.WaterBalance * species.RootsSparsity * BranchingByChildren[children.Count])} -> {Utils.Pcg.AccumulatedProbability(PreviousDayProductionInv / (plant.WaterBalance * species.RootsSparsity * BranchingByChildren[childrenCount]), world.HoursPerTick)}");
				if (plant.RNG.NextFloatAccum(resourcesAvailability * species.RootsDensity / (plant.WaterBalanceUG * BranchingByChildren[childrenCount]), world.HoursPerTick))
				{
					var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
					formation.Birth(new(plant, timestep, formationID, RandomOrientation(plant, species, Orientation), energy, initialResources: PreviousDayEnvResourcesInvariant, initialProduction: PreviousDayProductionInvariant));
					Energy -= 2f * energy; //twice because some energy is needed for the birth itself
					//Console.WriteLine($"New root branched to {formationID} at time {timestep}");
				}
			}
		}
		else if (Energy <= 0f) //Without energy the part dies
		{
			//Console.WriteLine($"Root {formationID} depleeted at time {timestep}");
			formation.Death(formationID);
			return;
		}
		#endregion

		///////////////////////////
		#region Absorb WATER from soil
		///////////////////////////
		if (mWaterAbsorbtionFactor > 0f)
		{
			var waterCapacity = WaterStorageCapacity_g();
			if (Water_g < waterCapacity)
			{
				var soil = plant.Soil;
				var baseCenter = formation.GetBaseCenter(formationID);
				var samplePoint = baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f;
				//find all soild cells that the shpere intersects
				var source = soil.IntersectPoint(samplePoint, plant.SoilIndex); //TODO make a tube intersection

				var vegetativeTemp = plant.VegetativeLowTemperature;

				if (source >= 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var amount = WaterAbsorbtionPerTick_g(world);
					var soilTemperature = soil.GetTemperature(source, plant.SoilIndex);
					if (soilTemperature > vegetativeTemp.X)
					{
						if (soilTemperature < vegetativeTemp.Y)
							amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
						soil.RequestWater(source, Math.Min(waterCapacity - Water_g, amount), formation, formationID, plant.SoilIndex); //TODO change to tube surface!
					}

					CurrentDayEnvResourcesInvariant += soil.GetWater_g(source, plant.SoilIndex);
				}
				else //growing outside of the world
					formation.Death(formationID);
			}
		}
		else
			mWaterAbsorbtionFactor = 0f;
		#endregion
	}

	private static Quaternion RandomOrientation(PlantFormation2 plant, SpeciesSettings species, Quaternion orientation)
	{
		//var range = 0.2f * MathF.PI * (species.TwigsBendingLevel * DominanceLevel - species.TwigsBendingApical);
		//var factor = species.TwigsBending * range;
		var a = plant.RNG.NextFloatVar(0.8f);
		var b = plant.RNG.NextFloatVar(0.8f);
		orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, a) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, b);

		var y = Vector3.Transform(Vector3.UnitX, orientation).Y;

		orientation = Quaternion.Slerp(orientation, PlantFormation2.AdjustUpBase(orientation, up: false), plant.RNG.NextPositiveFloat(y > 0 ? y : species.RootsGravitaxis));
		return orientation;
	}

	public bool CompleteDay(uint timestep, byte ticksPerDay)
	{
		var complete = timestep - BirthTime >= ticksPerDay;
		if (complete)
		{
			PreviousDayProductionInvariant = CurrentDayProductionInvariant_g_per_m2;
			PreviousDayEnvResourcesInvariant = CurrentDayEnvResourcesInvariant;
		}

		CurrentDayProductionInvariant_g_per_m2 = 0f;
		CurrentDayEnvResourcesInvariant = 0f;

		return complete;
	}

	[M(AI)]void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water_g += amount;
		CurrentDayProductionInvariant_g_per_m2 += amount / Surface();
	}

	[M(AI)]public void Distribute(float water, float energy)
	{
		Energy = energy;
		Water_g = water;
	}

	[M(AI)]public void IncAuxins(float amount) => Auxins += amount;
	//[M(AI)]public void IncCytokinins(float amount) => Cytokinins += amount;

	[M(AI)]public void DailyMax(float resources, float production)
	{
		if (resources > PreviousDayEnvResourcesInvariant) PreviousDayEnvResourcesInvariant = resources;
		if (production > PreviousDayProductionInvariant) PreviousDayProductionInvariant = production;
	}

	[M(AI)]public void DailyAdd(float resources, float production)
	{
		PreviousDayEnvResourcesInvariant += resources;
		PreviousDayProductionInvariant += production;
	}

	[M(AI)]public void DailySet(float resources, float production, float efficiency)
	{
		PreviousDayEnvResourcesInvariant = resources;
		PreviousDayProductionInvariant = production;
	}

	[M(AI)]public void DailyDiv(uint count)
	{
		PreviousDayEnvResourcesInvariant /= count;
		PreviousDayProductionInvariant /= count;
	}

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct WaterInc : IMessage<UnderGroundAgent>
    {
        /// <summary>
		/// In gramm
		/// </summary>
		public readonly float Amount;
        /// <summary>
		/// Fulfillment of the request (when there is not enough water for all)
		/// </summary>
		//public readonly float Factor;
        public WaterInc(float amount)
        {
            Amount = amount;
            //Factor = 1;
        }
        public WaterInc(float amount, float factor)
        {
            Amount = amount * factor;
            //Factor = factor;
        }
        public bool Valid => Amount > 0f;
        public Transaction Type => Transaction.Increase;
        [M(AI)]public void Receive(ref UnderGroundAgent dstAgent, uint timestep) => dstAgent.IncWater(Amount);
    }

    public void SetOrientation(Quaternion quaternion)
    {
        return;
    }

    [M(AI)]public readonly float Adulcy(uint timestep) => 0;

    [M(AI)]public readonly float Senescence(uint timestep) => 0f;
}
