using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public partial struct UnderGroundAgent2 : IPlantAgent
{
	///////////////////////////
	#region DATA
	///////////////////////////

	readonly uint BirthTime;

	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Length of the agent in m.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	public float Radius { get; private set; }

	public readonly byte DominanceLevel => 0;

	public readonly Vector3 Scale() => new(Length, 2f * Radius, 2f * Radius);
	public readonly float Volume() => 4f * Length * Radius * Radius;

	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	public float Water { get; private set; }

	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	public float Auxins { get; set; }
	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	public float Cytokinins { get; set; }

	/// <summary>
	/// Allocation of water during the previous day, m³ of water per m² i.e. invariant of surface
	/// </summary>
	public float PreviousDayProductionInv {get; private set; }
	public float CurrentDayProductionInv { get; set; }

	/// <summary>
	/// Resources available during the previous day, averaged, in m³ of water
	/// </summary>
	public float PreviousDayEnvResourcesInv { get; private set; }
	public float CurrentDayEnvResourcesInv { get; set; }

	public readonly float PreviousDayEnvResources => PreviousDayProductionInv;

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less water the root can absorb.
	/// </summary>
	float mWaterAbsorbtionFactor;

	public readonly float WoodRatio() => 1f - mWaterAbsorbtionFactor;

	public readonly OrganTypes Organ => OrganTypes.Root;

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }

	#endregion

	#region Variances
	float LengthVar;
	#endregion

	///////////////////////////
	#region Constants and computed data
	///////////////////////////

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
	/// Water volume in m³ that can be absorbed per m² of root surface per hour
	/// </summary>
	public const float WaterAbsortionRatio = 500f;

	/// <summary>
	/// Water volume in m³ which can be absorbed from soil per hour
	/// </summary>
	public readonly float WaterAbsorbtionPerHour() => Radius * Length * WaterAbsortionRatio;

	/// <summary>
	/// Water volume in m³ which can be absorbed from soil per timestep
	/// </summary>
	public readonly float WaterAbsorbtionPerTick(AgroWorld world) => WaterAbsorbtionPerHour() * world.HoursPerTick;

	//Let's assume (I might be fully wrong) that the plan can push the water 0.5mm in 1s, then in 1h it can push it 0.001 * 30 * 60 = 1.8m
	//also see - interestingly it states that while pholem is not photosensitive, xylem is
	//https://www.researchgate.net/publication/238417831_Simultaneous_measurement_of_water_flow_velocity_and_solute_transport_in_xylem_and_phloem_of_adult_plants_of_Ricinus_communis_during_day_time_course_by_nuclear_magnetic_resonance_NMR_spectrometry
	public const float WaterTransportRatio = 1.8f;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public readonly float WaterFlowToParentPerHour() => 4f * Radius * Radius * WaterTransportRatio * (2f - mWaterAbsorbtionFactor);

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	public readonly float WaterFlowToParentPerTick(AgroWorld world) => WaterFlowToParentPerHour() * world.HoursPerTick;

	public const float EnergyTransportRatio = 2f;

	public readonly float EnergyFlowToParentPerHour() => 4f * Radius * Radius * EnergyTransportRatio * (2f - mWaterAbsorbtionFactor);

	public readonly float EnergyFlowToParentPerTick(AgroWorld world) => EnergyFlowToParentPerHour() * world.HoursPerTick;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public readonly float WaterStorageCapacity() => 4f * Radius * Radius * Length * WaterCapacityRatio;

	/// <summary>
	/// Water volume in m³ which can flow through per hour, or can be stored in this agent
	/// </summary>
	public readonly float WaterTotalCapacityPerHour() => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio);

	/// <summary>
	/// Water volume in m³ which can flow through per tick, or can be stored in this agent
	/// </summary>
	public readonly float WaterTotalCapacityPerTick(AgroWorld world) => WaterTotalCapacityPerHour() * world.HoursPerTick;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

    readonly float EnergyCapacityFunc(float radius, float length) => 4f * radius * radius * length * (1f - WaterCapacityRatio) * EnergyStorageCoef * MathF.Pow(2f - mWaterAbsorbtionFactor, 3);

	public readonly float EnergyStorageCapacity() => EnergyCapacityFunc(Radius, Length);

    public readonly float LifeSupportPerHour() => 0.01f * Length * Radius * Radius * 4f * mWaterAbsorbtionFactor;

	public readonly float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

	public readonly float PhotosynthPerTick(AgroWorld world) => 0f;

	public readonly static Quaternion OrientationDown = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f);

	#endregion

	public UnderGroundAgent2(PlantFormation2 plant, uint timestep, int parent, Quaternion orientation, float initialEnergy, float initialWater = 0f, float initialWaterIntake = 1f, float radius = InitialRadius, float length = InitialLength, float initialResources = 0f, float initialProduction = 0f)
	{
		BirthTime = timestep;
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Energy = initialEnergy;
		Water = initialWater;
		mWaterAbsorbtionFactor = initialWaterIntake;

		Auxins = 0f;
		Cytokinins = 0f;

		PreviousDayProductionInv = initialProduction;
		CurrentDayProductionInv = 0f;
		PreviousDayEnvResourcesInv = initialResources;
		CurrentDayEnvResourcesInv = 0f;

		LengthVar = PlantFormation2.RootSegmentLength * 0.6f + plant.RNG.NextFloatVar(PlantFormation2.RootSegmentLength * 0.4f);
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(UnderGroundAgent2[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	public void CensusUpdateParent(int newParent) => Parent = newParent;

	const float BranchingFactor = 0.02f;
	static float[] BranchingByChildren = new[]{
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
		float.MaxValue};
	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		//Console.WriteLine($"{timestep} x {formationID}: w={Water} e={Energy} waf={WaterAbsorbtionFactor}");
		var formation = (PlantSubFormation2<UnderGroundAgent2>)_formation;
		var plant = formation.Plant;
		var world = plant.World;
		var species = plant.Parameters;

		//TODO perhaps it should somehow reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour();

		//life support
		Energy -= lifeSupportPerHour * world.HoursPerTick;

		var children = formation.GetChildren(formationID);
		//Debug.WriteLine($"{timestep} / {formationID}  W {Water} E {Energy} L {Length} R {Radius}");
		//var waterFactor = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
		///////////////////////////
		#region Growth
		///////////////////////////
		if (Energy > lifeSupportPerHour * 240) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			var childrenCount = children.Count + 1;
			//TODO MI 2023-03-07 Incorporate water capacity factor
			if (formation.DailyProductionMax > 0)
			{
				var growthBase = PreviousDayProductionInv / formation.DailyProductionMax;
				var radiusChildGrowth = childrenCount <= 1 ? 1 : MathF.Pow(childrenCount, GrowthDeclineByExpChildren / 2);
				var (radiusGrowthBase, lengthGrowthBase) = (3e-6f * growthBase, 8e-5f * growthBase);
				var newRadius = Radius;
				var newLength = Length;
				var newWaterAbsorbtion = mWaterAbsorbtionFactor;
				bool grows = true;
				var localSubtree = new List<UnderGroundAgent2>();
				//for(int i = 0; i < world.HoursPerTick && grows; ++i)
				{
					var ld = newLength * newRadius * 4f;
					var volume = ld * newLength;
					grows = false;
					if (volume * newWaterAbsorbtion < 288)
					{
						float maxRadius = Parent == -1 ? plant.AG.GetBaseRadius(0) * 1.25f : formation.GetBaseRadius(Parent);
						var compute = newRadius <= maxRadius;

						if (compute)
						{
							var d = 1f - 0.7f * formation.GetRelDepth(formationID);
							var radiusGrowth = radiusGrowthBase * d * d / (radiusChildGrowth * MathF.Pow(ld * newRadius, 0.2f));
							newRadius += radiusGrowth;
							if (newRadius > maxRadius) newRadius = maxRadius;
							newWaterAbsorbtion -= radiusGrowth * childrenCount;  //become wood faster with children
							grows = true;
							if (newWaterAbsorbtion < 0f) newWaterAbsorbtion = 0f;
						}

						if (childrenCount == 1)
						{
							newLength += lengthGrowthBase / MathF.Pow(ld * newLength, 0.1f);
							grows = true;
						}
					}
				}

				Radius = newRadius;
				Length = newLength;
				mWaterAbsorbtionFactor = newWaterAbsorbtion;
			}

			//Chaining
			if (children.Count == 0 && Length >= LengthVar)
			{
				var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
				formation.Birth(new(plant, timestep, formationID, RandomOrientation(plant, species, Orientation), energy, initialResources: PreviousDayEnvResourcesInv, initialProduction: PreviousDayProductionInv));
				Energy -= 2f * energy; //twice because some energy is needed for the birth itself
				//Console.WriteLine($"New root chained to {formationID} at time {timestep}");
			}

			//Branching
			if (children.Count != 0 && children.Count <= 8 && Radius > 1e-3f)
			{
				// const float yFactor = 0.5f;
				// const float zFactor = 0.2f;
				//Debug.WriteLine($"{plant.WaterBalance * species.RootsSparsity * MathF.Pow(childrenCount, childrenCount << 2) / (world.HoursPerTick * PreviousDayProductionInv)} = {plant.WaterBalance} * {species.RootsSparsity} * {MathF.Pow(childrenCount, childrenCount << 2)} / ({world.HoursPerTick * PreviousDayProductionInv})");
				//Debug.WriteLine($"{PreviousDayEnvResourcesInv} {PreviousDayProductionInv}");
				//Debug.WriteLine($"{PreviousDayProductionInv} / ({plant.WaterBalanceUG} * {species.RootsSparsity} * {BranchingByChildren[children.Count]}) = {PreviousDayProductionInv / (plant.WaterBalance * plant.WaterBalance * species.RootsSparsity * BranchingByChildren[children.Count])} -> {Utils.Pcg.AccumulatedProbability(PreviousDayProductionInv / (plant.WaterBalance * species.RootsSparsity * BranchingByChildren[childrenCount]), world.HoursPerTick)}");
				if (plant.RNG.NextFloatAccum(PreviousDayProductionInv / (plant.WaterBalanceUG * species.RootsSparsity * BranchingByChildren[childrenCount]), world.HoursPerTick))
				{
					// var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, plant.RNG.NextFloat(-MathF.PI * yFactor, MathF.PI * yFactor));
					// var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloat(MathF.PI * zFactor, MathF.PI * zFactor * 2f));
					// //var q = qz * qx * Orientation;
					// var orientation = Orientation * qx * qz;
					var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
					formation.Birth(new(plant, timestep, formationID, RandomOrientation(plant, species, Orientation), energy, initialResources: PreviousDayEnvResourcesInv, initialProduction: PreviousDayProductionInv));
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
			var waterCapacity = WaterStorageCapacity();
			if (Water < waterCapacity)
			{
				var soil = plant.Soil;
				var baseCenter = formation.GetBaseCenter(formationID);
				var samplePoint = baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f;
				//find all soild cells that the shpere intersects
				var source = soil.IntersectPoint(samplePoint); //TODO make a tube intersection

				var vegetativeTemp = plant.VegetativeLowTemperature;

				if (source >= 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var amount = WaterAbsorbtionPerTick(world);
					var soilTemperature = soil.GetTemperature(source);
					if (soilTemperature > vegetativeTemp.X)
					{
						if (soilTemperature < vegetativeTemp.Y)
							amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
						soil.RequestWater(source, Math.Min(waterCapacity - Water, amount), formation, formationID); //TODO change to tube surface!
					}

					CurrentDayEnvResourcesInv += soil.GetWater(source);
				}
				else
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

		if (y > 0)
			orientation = Quaternion.Slerp(orientation, PlantFormation2.AdjustUpBase(orientation, up: false), plant.RNG.NextPositiveFloat(y));
		else //if (species.RootsGravitaxis > 0)
			orientation = Quaternion.Slerp(orientation, PlantFormation2.AdjustUpBase(orientation, up: false), plant.RNG.NextPositiveFloat(species.RootsGravitaxis));

		return orientation;
	}

	public bool NewDay(uint timestep, byte ticksPerDay)
	{
		var complete = timestep - BirthTime >= ticksPerDay;
		if (complete)
		{
			PreviousDayProductionInv = CurrentDayProductionInv;
			PreviousDayEnvResourcesInv = CurrentDayEnvResourcesInv;
		}

		CurrentDayProductionInv = 0f;
		CurrentDayEnvResourcesInv = 0f;

		return complete;
	}

	void IncWater(float amount, float factor)
	{
		Debug.Assert(amount >= 0f);
		Water += amount;
		CurrentDayProductionInv += factor;
	}
	void IncEnergy(float amount)
	{
		Debug.Assert(amount >= 0f);
		Energy += amount;
	}

	float TryDecWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		if (Water > amount)
		{
			Water -= amount;
			return amount;
		}
		else
		{
			var w = Water;
			Water = 0f;
			return w;
		}
	}

	float TryDecEnergy(float amount)
	{
		Debug.Assert(amount >= 0f);
		if (Energy > amount)
		{
			Energy -= amount;
			return amount;
		}
		else
		{
			var e = Energy;
			Energy = 0f;
			return e;
		}
	}

	public readonly bool ChangeAmount(PlantFormation2 plant, int index, int substanceIndex, float amount, bool inc) => substanceIndex switch {
		(byte)PlantSubstances.Water => plant.Send(index, inc ? new WaterInc(amount) : new WaterDec(amount)),
		(byte)PlantSubstances.Energy => plant.Send(index, inc ? new EnergyInc(amount) : new EnergyDec(amount)),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	public void Distribute(float water, float energy)
	{
		Energy = energy;
		Water = water;
	}

	public void IncAuxins(float amount) => Auxins += amount;
	public void IncCytokinins(float amount) => Cytokinins += amount;

	public void DailyMax(float resources, float production)
	{
		if (resources > PreviousDayEnvResourcesInv) PreviousDayEnvResourcesInv = resources;
		if (production > PreviousDayProductionInv) PreviousDayProductionInv = production;
	}

	public void DailyAdd(float resources, float production)
	{
		PreviousDayEnvResourcesInv += resources;
		PreviousDayProductionInv += production;
	}

	public void DailySet(float resources, float production, float efficiency)
	{
		PreviousDayEnvResourcesInv = resources;
		PreviousDayProductionInv = production;
	}

	public void DailyDiv(uint count)
	{
		PreviousDayEnvResourcesInv /= count;
		PreviousDayProductionInv /= count;
	}

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG || TICK_LOG
	public readonly ulong ID { get; } = Utils.UID.Next();
	public Utils.QuatData OrienTaTion => new(Orientation);
	#endif
	#endregion
}
