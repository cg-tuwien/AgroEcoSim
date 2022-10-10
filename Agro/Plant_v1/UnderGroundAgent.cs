using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public partial struct UnderGroundAgent : IPlantAgent
{
	///////////////////////////
	#region DATA
	///////////////////////////

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

	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public Vector3 Scale => new(Length, 2f * Radius, 2f * Radius);

	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	public float Water { get; private set; }

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less water the root can absorb.
	/// </summary>
	float mWaterAbsorbtionFactor;

	public float WoodRatio => 1f - mWaterAbsorbtionFactor;

	public OrganTypes Organ => OrganTypes.Root;

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }

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
	public const float WaterAbsortionRatio = 1f;

	/// <summary>
	/// Water volume in m³ which can be absorbed from soil per hour
	/// </summary>
	public readonly float WaterAbsorbtionPerHour => Radius * 8f * Length * WaterAbsortionRatio;

	/// <summary>
	/// Water volume in m³ which can be absorbed from soil per timestep
	/// </summary>
	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public readonly float WaterAbsorbtionPerTick => WaterAbsorbtionPerHour / AgroWorld.TicksPerHour;


	//Let's assume (I might be fully wrong) that the plan can push the water 0.5mm in 1s, then in 1h it can push it 0.001 * 30 * 60 = 1.8m
	//also see - interestingly it states that while pholem is not photosensitive, xylem is
	//https://www.researchgate.net/publication/238417831_Simultaneous_measurement_of_water_flow_velocity_and_solute_transport_in_xylem_and_phloem_of_adult_plants_of_Ricinus_communis_during_day_time_course_by_nuclear_magnetic_resonance_NMR_spectrometry
	public const float WaterTransportRatio = 1.8f;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public readonly float WaterFlowToParentPerHour => 4f * Radius * Radius * WaterTransportRatio * (2f - mWaterAbsorbtionFactor);

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public readonly float WaterFlowToParentPerTick => WaterFlowToParentPerHour / AgroWorld.TicksPerHour;

	public const float EnergyTransportRatio = 2f;
	public readonly float EnergyFlowToParentPerHour => 4f * Radius * Radius * EnergyTransportRatio * (2f - mWaterAbsorbtionFactor);

	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public readonly float EnergyFlowToParentPerTick => EnergyFlowToParentPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public readonly float WaterStorageCapacity => 4f * Radius * Radius * Length * WaterCapacityRatio;

	/// <summary>
	/// Water volume in m³ which can flow through per hour, or can be stored in this agent
	/// </summary>
	public readonly float WaterTotalCapacityPerHour => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio);

	/// <summary>
	/// Water volume in m³ which can flow through per tick, or can be stored in this agent
	/// </summary>
	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#endif
	public readonly float WaterTotalCapacityPerTick => WaterTotalCapacityPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

	float EnergyCapacityFunc(float radius, float length) => 4f * radius * radius * length * (1f - WaterCapacityRatio) * EnergyStorageCoef * (2f - mWaterAbsorbtionFactor);

	public readonly float EnergyStorageCapacity => EnergyCapacityFunc(Radius, Length);
	float LifeSupportPerHour => Length * Radius * Radius * 4f * mWaterAbsorbtionFactor;
	public float LifeSupportPerTick => LifeSupportPerHour / AgroWorld.TicksPerHour;
	public float PhotosynthPerTick => 0f;

	public static Quaternion OrientationDown = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI * 0.5f);

	#endregion

	public UnderGroundAgent(int parent, Quaternion orientation, float initialEnergy, float initialWater = 0f, float initialWaterIntake = 1f, float radius = InitialRadius, float length = InitialLength)
	{
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Energy = initialEnergy;
		Water = initialWater;
		mWaterAbsorbtionFactor = initialWaterIntake;
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(UnderGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	public void CensusUpdateParent(int newParent) => Parent = newParent;

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep, byte stage)
	{
		//Console.WriteLine($"{timestep} x {formationID}: w={Water} e={Energy} waf={WaterAbsorbtionFactor}");
		var formation = (PlantSubFormation<UnderGroundAgent>)_formation;
		var plant = formation.Plant;

		//TODO perhaps it should somehow reflect temperature
		var diameter = 2f * Radius;
		var lr = Length * diameter; //area of the side face
		var volume = lr * diameter; //also this is the volume
		var lifeSupportPerHour = volume * mWaterAbsorbtionFactor;

		//life support
		Energy -= lifeSupportPerHour / AgroWorld.TicksPerHour;

		var children = formation.GetChildren(formationID);

		var waterFactor = Math.Clamp(Water / WaterStorageCapacity, 0f, 1f);
		///////////////////////////
		#region Growth
		///////////////////////////
		if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			var childrenCount = children.Count + 1;
			var lengthGrowth = waterFactor * 2e-4f / (AgroWorld.TicksPerHour * MathF.Pow(childrenCount, GrowthDeclineByExpChildren + 1) * MathF.Pow(lr * Length, 0.1f));
			var widthGrowth = waterFactor * 2e-5f / (AgroWorld.TicksPerHour * MathF.Pow(childrenCount, GrowthDeclineByExpChildren / 2) * MathF.Pow(volume, 0.1f)); //just optimized the number of multiplications

			Length += lengthGrowth;
			Radius += widthGrowth;

			mWaterAbsorbtionFactor -= widthGrowth * childrenCount;  //become wood faster with children
			if (mWaterAbsorbtionFactor < 0f)
				mWaterAbsorbtionFactor = 0f;

			const float yFactor = 0.5f;
			const float zFactor = 0.2f;
			//Chaining
			if (children.Count == 0 && Length > PlantFormation1.RootSegmentLength * 0.5f + plant.RNG.NextFloat(PlantFormation1.RootSegmentLength * 0.5f))
			{
				var ax = plant.RNG.NextFloat(-MathF.PI * yFactor, MathF.PI * yFactor);
				var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, ax);
				var az = plant.RNG.NextFloat(-MathF.PI * zFactor, MathF.PI * zFactor);
				var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, az);
				//var q = qz * qx * Orientation;
				var orientation = Orientation * qx * qz;
				var y = Vector3.Transform(Vector3.UnitX, orientation).Y;
				if (y > 0)
					orientation = Quaternion.Slerp(orientation, OrientationDown, plant.RNG.NextFloat(y));
				else
					orientation = Quaternion.Slerp(orientation, OrientationDown, plant.RNG.NextFloat(0.2f / AgroWorld.TicksPerHour));
				var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
				formation.Birth(new UnderGroundAgent(formationID, orientation, energy));
				Energy -= 2f * energy; //twice because some energy is needed for the birth itself
				//Console.WriteLine($"New root chained to {formationID} at time {timestep}");
			}

			//Branching
			if (children.Count > 0)
			{
				var pool = MathF.Pow(childrenCount, childrenCount << 2) * AgroWorld.TicksPerHour;
				if (pool < uint.MaxValue && plant.RNG.NextUInt((uint)pool) == 1 && waterFactor > plant.RNG.NextFloat())
				{
					var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, plant.RNG.NextFloat(-MathF.PI * yFactor, MathF.PI * yFactor));
					var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloat(MathF.PI * zFactor, MathF.PI * zFactor * 2f));
					//var q = qz * qx * Orientation;
					var orientation = Orientation * qx * qz;
					var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
					formation.Birth(new UnderGroundAgent(formationID, orientation, energy));
					Energy -= 2f * energy; //twice because some energy is needed for the birth itself
					//Console.WriteLine($"New root branched to {formationID} at time {timestep}");
				}
			}
		}
		else if (Energy > 0f) //if running out of energy, balance it by taking it away from children
		{
			//Console.WriteLine($"Root {formationID} starving at time {timestep}");
			foreach(var child in children)
			{
				var childEnergy = formation.GetEnergy(child);
				if (childEnergy > Energy)
				{
					var amount = Math.Min(formation.GetEnergyFlow_PerTick(child), Math.Min(formation.GetEnergyFlow_PerTick(child), childEnergy));
					plant.TransactionUG(child, formationID, PlantSubstances.Energy, amount);
				}
			}
		}
		else //Without energy the part dies
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
			var waterCapacity = WaterAbsorbtionPerTick;
			if (Water < waterCapacity)
			{
				var soil = plant.Soil;
				var baseCenter = formation.GetBaseCenter(formationID);
				//find all soild cells that the shpere intersects
				var sources = soil.IntersectSphere(baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f, Length * 0.25f); //TODO make a tube intersection

				var vegetativeTemp = plant.VegetativeLowTemperature;

				if (sources.Count > 0) //TODO this is a rough approximation taking only the first intersected soil cell
				{
					var amount = waterCapacity;
					var soilTemperature = soil.GetTemperature(sources[0]);
					if (soilTemperature > vegetativeTemp.X)
					{
						if (soilTemperature < vegetativeTemp.Y)
							amount *= (soilTemperature - vegetativeTemp.X) / (vegetativeTemp.Y - vegetativeTemp.X);
						soil.Send(sources[0], new SoilAgent.Water_UG_PullFrom_Soil(formation, Math.Min(waterCapacity - Water, amount), formationID)); //TODO change to tube surface!
					}
				}
			}
		}
		else
			mWaterAbsorbtionFactor = 0f;
		#endregion

		///////////////////////////
		#region Transport ENERGY
		///////////////////////////
		if (children.Count > 0 || Energy < EnergyStorageCapacity)
		{
			if (Parent >= 0)
			{
				var parentEnergy = formation.GetEnergy(Parent);
				if (parentEnergy > Energy)
					plant.TransactionUG(Parent, formationID, PlantSubstances.Energy, EnergyFlowToParentPerTick); //TODO make requests based on own need and the need of children
				else
					plant.TransactionUG(formationID, Parent, PlantSubstances.Energy, 0.5f * EnergyFlowToParentPerTick); //TODO make requests based on own need and the need of children
			}
			else
			{
				var roots = plant.AG.GetRoots();
				foreach(var root in roots)
				{
					var rootEnergy = plant.AG.GetEnergy(root);
					if (rootEnergy > Energy)
						plant.Send(root, new Energy_PullFrom_AG(formation, plant.AG.GetEnergyFlow_PerTick(root), formationID)); //TODO make requests based on own need and the need of children
				}
			}
		}
		#endregion

		///////////////////////////
		#region Transport WATER
		///////////////////////////
		if (Water > 0f)
		{
			if (Parent >= 0)
				plant.TransactionUG(formationID, Parent, PlantSubstances.Water, WaterFlowToParentPerTick);
			else
			{
				var roots = plant.AG.GetRoots();
				foreach(var root in roots)
					plant.Send(formationID, new Water_AG_PullFrom_UG(plant.AG, WaterFlowToParentPerTick, root));
			}
		}
		#endregion
	}

	void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water += amount;
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

	public bool ChangeAmount(PlantFormation1 plant, int index, int substanceIndex, float amount, bool inc) => substanceIndex switch {
		(byte)PlantSubstances.Water => plant.Send(index, inc ? new WaterInc(amount) : new WaterDec(amount)),
		(byte)PlantSubstances.Energy => plant.Send(index, inc ? new EnergyInc(amount) : new EnergyDec(amount)),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	public bool ChangeAmount(PlantFormation2 plant, int index, int substanceIndex, float amount, bool increase) => throw new Exception();

	public void Distribute(float water, float energy) => throw new Exception();

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG || TICK_LOG
	public readonly ulong ID { get; } = Utils.UID.Next();
	public Utils.QuatData OrienTaTion => new(Orientation);
	#endif
	#endregion
}
