using System;
using System.Diagnostics;
using System.Numerics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;

namespace Agro;

[StructLayout(LayoutKind.Auto)]
public partial struct UnderGroundAgent : IAgent
{
	///////////////////////////
	#region DATA
	///////////////////////////

	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Length of the agent in m.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	public float Radius { get; private set; }

	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	public float Water { get; private set; }

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less water the root can absorb. 
	/// </summary>
	float WaterAbsorbtionFactor; //factor 0 .. 1

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
	public float WaterAbsorbtionPerHour => Radius * 8f * Length * WaterAbsortionRatio;

	/// <summary>
	/// Water volume in m³ which can be absorbed from soil per timestep
	/// </summary>
	public float WaterAbsorbtionPerTick => WaterAbsorbtionPerHour / AgroWorld.TicksPerHour;

	public const float EnergyTransportRatio = 4f;

	//Let's assume (I might be fully wrong) that the plan can push the water 0.5mm in 1s, then in 1h it can push it 0.001 * 30 * 60 = 1.8m
	//also see - interestingly it states that while pholem is not photosensitive, xylem is
	//https://www.researchgate.net/publication/238417831_Simultaneous_measurement_of_water_flow_velocity_and_solute_transport_in_xylem_and_phloem_of_adult_plants_of_Ricinus_communis_during_day_time_course_by_nuclear_magnetic_resonance_NMR_spectrometry
	public const float WaterTransportRatio = 1.8f;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public float WaterFlowToParentPerHour => 4f * Radius * Radius * WaterTransportRatio;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	public float WaterFlowToParentPerTick => WaterFlowToParentPerHour / AgroWorld.TicksPerHour;

	public float EnergyFlowToParentPerHour => 4f * Radius * Radius * WaterTransportRatio;

	public float EnergyFlowToParentPerTick => EnergyFlowToParentPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public float WaterStorageCapacity => 4f * Radius * Radius * Length * WaterCapacityRatio;

	/// <summary>
	/// Water volume in m³ which can flow through per hour, or can be stored in this agent
	/// </summary>
	public float WaterCapacityPerHour => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio);

	/// <summary>
	/// Water volume in m³ which can flow through per tick, or can be stored in this agent
	/// </summary>
	public float WaterCapacityPerTick => WaterCapacityPerHour / AgroWorld.TicksPerHour;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

	static float EnergyCapacityFunc(float radius, float length) => 4f * radius * radius * length * (1f - WaterCapacityRatio) * EnergyStorageCoef;

	public float EnergyCapacity => EnergyCapacityFunc(Radius, Length);

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
		WaterAbsorbtionFactor = initialWaterIntake;
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(UnderGroundAgent[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	public void Tick(SimulationWorld world, IFormation _formation, int formationID, uint timestep)
	{
		//Console.WriteLine($"{timestep} x {formationID}: w={Water} e={Energy} waf={WaterAbsorbtionFactor}");
		var formation = (PlantFormation)_formation;

		//TODO perhaps it should somehow reflect temperature
		var diameter = 2f * Radius;
		var lr = Length * diameter; //area of the side face
		var lifeSupportPerHour = lr * diameter; //also this is the volume
		var lifeSupportPerTick = lifeSupportPerHour / AgroWorld.TicksPerHour;

		//life support
		Energy -= lifeSupportPerTick;

		var children = formation.GetChildren_UG(formationID);

		var waterFactor = Math.Clamp(Water / WaterStorageCapacity, 0f, 1f);
		///////////////////////////
		#region Growth
		///////////////////////////
		if (Energy > lifeSupportPerHour * 36) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage        
		{
			var childrenCount = children.Count + 1;
			var lengthGrowth = waterFactor * 2e-4f / (AgroWorld.TicksPerHour * MathF.Pow(childrenCount, GrowthDeclineByExpChildren + 1) * MathF.Pow(lr * Length, 0.1f));
			var widthGrowth = waterFactor * 2e-5f / (AgroWorld.TicksPerHour * MathF.Pow(childrenCount, GrowthDeclineByExpChildren / 2) * MathF.Pow(lifeSupportPerHour, 0.1f)); //just optimized the number of multiplications
			
			Length += lengthGrowth;
			Radius += widthGrowth;

			WaterAbsorbtionFactor -= widthGrowth * childrenCount;  //become wood faster with children
			if (WaterAbsorbtionFactor < 0f)
				WaterAbsorbtionFactor = 0f;

			const float yFactor = 0.5f;
			const float zFactor = 0.2f;
			//Chaining
			if (children.Count == 0 && Length > PlantFormation.RootSegmentLength * 0.5f + formation.RNG.NextFloat(PlantFormation.RootSegmentLength * 0.5f))
			{
				var ax = formation.RNG.NextFloat(-MathF.PI * yFactor, MathF.PI * yFactor);
				var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, ax);
				var az = formation.RNG.NextFloat(-MathF.PI * zFactor, MathF.PI * zFactor);
				var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, az);
				//var q = qz * qx * Orientation;
				var orientation = Orientation * qx * qz;
				var y = Vector3.Transform(Vector3.UnitX, orientation).Y;
				if (y > 0)
					orientation = Quaternion.Slerp(orientation, OrientationDown, formation.RNG.NextFloat(y));
				else
					orientation = Quaternion.Slerp(orientation, OrientationDown, formation.RNG.NextFloat(0.2f / AgroWorld.TicksPerHour));
				var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
				formation.UnderGroundBirth(new UnderGroundAgent(formationID, orientation, energy));
				Energy -= 2f * energy; //twice because some energy is needed for the birth itself
				//Console.WriteLine($"New root chained to {formationID} at time {timestep}");
			}

			//Branching
			if(children.Count > 0 )
			{
				var pool = MathF.Pow(childrenCount, childrenCount << 2) * AgroWorld.TicksPerHour;
				if (pool < uint.MaxValue && formation.RNG.NextUInt((uint)pool) == 1 && waterFactor > formation.RNG.NextFloat())
				{
					var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, formation.RNG.NextFloat(-MathF.PI * yFactor, MathF.PI * yFactor));
					var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, formation.RNG.NextFloat(MathF.PI * zFactor, MathF.PI * zFactor * 2f));
					//var q = qz * qx * Orientation;
					var orientation = Orientation * qx * qz;
					var energy = EnergyCapacityFunc(InitialRadius, InitialLength);
					formation.UnderGroundBirth(new UnderGroundAgent(formationID, orientation, energy));
					Energy -= 2f * energy; //twice because some energy is needed for the birth itself
					//Console.WriteLine($"New root branched to {formationID} at time {timestep}");
				}
			}
		}
		else if (Energy > 0) //if running out of energy, balance it by taking it away from children
		{
			//Console.WriteLine($"Root {formationID} starving at time {timestep}");
			foreach(var child in children)
			{
				var childEnergy = formation.GetEnergy_UG(child);
				if (childEnergy > Energy)
				{
					var amount = Math.Min(formation.GetEnergyFlow_PerTick_UG(child), Math.Min(formation.GetEnergyFlow_PerTick_UG(child), (childEnergy - Energy) * 0.5f));
					formation.Send(child, new Energy_UG_PullFrom_UG(formation, amount, formationID));
				}
			}
		}
		else //Without energy the part dies
		{
			//Console.WriteLine($"Root {formationID} depleeted at time {timestep}");
			formation.UnderGroundDeath(formationID);
			return;
		}
		#endregion

		///////////////////////////
		#region Absorb WATER from soil
		///////////////////////////
		if (WaterAbsorbtionFactor > 0f)
		{
			var waterCapacity = WaterAbsorbtionPerTick;
			if (Water < waterCapacity)
			{
				var soil = formation.Soil;
				var baseCenter = formation.GetBaseCenter_UG(formationID);
				//find all soild cells that the shpere intersects
				var sources = soil.IntersectSphere(baseCenter + Vector3.Transform(Vector3.UnitX, Orientation) * Length * 0.75f, Length * 0.25f); //TODO make a tube intersection
		
				var vegetativeTemp = formation.VegetativeLowTemperature;
				
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
			WaterAbsorbtionFactor = 0f;
		#endregion

		///////////////////////////
		#region Transport ENERGY
		///////////////////////////
		if (Parent >= 0)
		{
			var parentEnergy = formation.GetEnergy_UG(Parent);
			if (parentEnergy > Energy)
				formation.Send(Parent, new Energy_UG_PullFrom_UG(formation, Math.Min(EnergyFlowToParentPerTick, (parentEnergy - Energy) * 0.5f), formationID)); //TODO make requests based on own need and the need of children
		}
		else
		{
			var parentEnergy = formation.GetEnergy_AG(0);
			if (parentEnergy > Energy)
				formation.Send(0, new Energy_UG_PullFrom_AG(formation, Math.Min(formation.GetEnergyFlow_PerTick_AG(0), (parentEnergy - Energy) * 0.5f), formationID)); //TODO make requests based on own need and the need of children
		}
		#endregion

		///////////////////////////
		#region Transport WATER
		///////////////////////////
		if (Water > 0f)
		{
			if (Parent >= 0)
				//formation.Send(Parent, new Water_UG_PushTo_UG(formation, WaterFlowToParentPerTick, formationID));
				formation.Send(formationID, new Water_UG_PullFrom_UG(formation, WaterFlowToParentPerTick, Parent));
			else
				formation.Send(0, new Water_AG_PullFrom_UG(formation, WaterFlowToParentPerTick, formationID));
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

	// internal UnderGroundAgent AddChild(int index)
	// {
	// 	var result = default(UnderGroundAgent);
	// 	result.Direction = Direction;
	// 	result.Radius = Radius;
	// 	result.Length = Length;
	// 	result.Energy = Energy;
	// 	result.Water = Water;
	//     result.mWaterIntake = mWaterIntake;
	// 	result.Parent = Parent;

	// 	if (Children == null)
	// 		result.Children = new int[]{index};
	// 	else
	// 	{
	// 		var children = new int[Children.Length];
	// 		Array.Copy(Children, children, Children.Length);
	// 		children[^1] = index;
	// 		result.Children = children;
	// 	}

	// 	return result;
	// }

	// internal void RemoveChild(int index)
	// {
	// 	Debug.Assert(Children.Contains(index));
	// 	if (Children.Length == 1)
	// 		Children = null;
	// 	else
	// 	{
	// 		var children = new int[Children.Length - 1];
	// 		for(int s = 0, t = 0; s < Children.Length; ++s)
	// 			if (Children[s] != index)
	// 				children[t++] = Children[s];
	// 		Children = children;
	// 	}
	// }
}
