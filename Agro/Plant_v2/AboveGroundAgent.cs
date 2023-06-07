using System;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;

namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
//TODO Create properties computing the respective storages for water and energy. Just for clarity.

[StructLayout(LayoutKind.Auto)]
public partial struct AboveGroundAgent2 : IPlantAgent
{
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

	public Vector3 Scale() => Organ switch {
		OrganTypes.Leaf => new(Length, 0.0001f, 2f * Radius),
		_ => new(Length, 2f * Radius, 2f * Radius)
	};

	public float Volume() => Organ switch {
		OrganTypes.Leaf => Length * 0.0002f * Radius,
		_ => Length * 4f * Radius* Radius
	};

	public float Energy { get; private set; }

	/// <summary>
	/// Water volume in m³
	/// </summary>
	public float Water { get; private set; }

	///<summary>
	///Accumulated energy output of this agent for the last 24 steps
	///</summary>
	public float PreviousDayProduction { get; private set; }
	///<summary>
	///Accumulated energy output of this agent for the next 24-steps batcg
	///</summary>
	float CurrentDayProduction { get; set; }

	///<summary>
	///Accumulated light exposure or water intake of this agent for the last 24 steps
	///</summary>
	public float PreviousDayEnvResources { get; private set; }
	///<summary>
	///Accumulated light exposure of this agent for the next 24-steps batch
	///</summary>
	float CurrentDayEnvResources { get; set; }

	/// <summary>
	/// Inverse woodyness ∈ [0, 1]. The more woody (towards 0) the less photosynthesis can be achieved.
	/// </summary>
	float mPhotoFactor;

	public float WoodRatio() => 1f - mPhotoFactor;

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>
	public OrganTypes Organ { get; private set; }

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }

	/// <summary>
	/// Recommended initial length of the agent at birth in m.
	/// </summary>
	public const float InitialLength = 1e-5f;

	/// <summary>
	/// Recommended initial bottom face radius of the agent at birth in m.
	/// </summary>
	public const float InitialRadius = 0.2e-5f;

	public const float EnergyTransportRatio = 4f;
	//So far same as for undergrounds
	public const float WaterTransportRatio = 1.8f;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public float WaterFlowToParentPerHour() => 4f * Radius * Radius * WaterTransportRatio;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	public float WaterFlowToParentPerTick(AgroWorld world) => WaterFlowToParentPerHour() * world.HoursPerTick;

	public float EnergyFlowToParentPerHour() => 4f * Radius * Radius * WaterTransportRatio;

	public float EnergyFlowToParentPerTick(AgroWorld world) => EnergyFlowToParentPerHour() * world.HoursPerTick;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public static float WaterStorageCapacityfunction(float radius, float length) => 4f * radius * radius * length * WaterCapacityRatio;

	/// <summary>
	/// Water volume in m³ which can be stored in this agent
	/// </summary>
	public float WaterStorageCapacity() => WaterStorageCapacityfunction(Radius, Length);

	/// <summary>
	/// Water volume in m³ which can flow through per hour, or can be stored in this agent
	/// </summary>
	public float WaterTotalCapacityPerHour() => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio);

	/// <summary>
	/// Water volume in m³ which can flow through per tick, or can be stored in this agent
	/// </summary>
	public float WaterTotalCapacityPerTick(AgroWorld world) => WaterTotalCapacityPerHour() * world.HoursPerTick;

	/// <summary>
	/// Timespan for which 1 unit of energy can feed 1m³ of plant tissue
	/// </summary>
	//Assuming the energy units are chosen s.t. a plant need an amount of energy per hour
	//equal to its volume, then this coefficient expresses how long it can survive
	//without any energy gains if its storage is initially full
	const float EnergyStorageCoef = 24 * 31 * 3; //3 months

	static float EnergyCapacityFunc(float radius, float length, float woodRatio) => 4f * radius * radius * length * MathF.Pow(1f + woodRatio, 3) * EnergyStorageCoef;

	public float EnergyStorageCapacity() => EnergyCapacityFunc(Radius, Length, WoodRatio());

	public static Quaternion OrientationUp = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);

	public const float GrowthRatePerHour = 1 + 1/12; //Let us assume this plant can double volume in 12 hours
	public float GrowthRatePerTick(AgroWorld world) => GrowthRatePerHour * world.HoursPerTick; //Let us assume this plant can double volume in 12 hours

	float LifeSupportPerHour() => Length * Radius * (Organ == OrganTypes.Leaf ? LeafThickness : Radius * mPhotoFactor);
	public float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

	public const float mPhotoEfficiency = 0.025f;
	public const float ExpectedIrradiance = 400f; //in W/m² see https://en.wikipedia.org/wiki/Solar_irradiance
	public float PhotosynthPerTick() => Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPiTenth) * mPhotoFactor * mPhotoEfficiency * ExpectedIrradiance;

	float EnoughEnergy(float? lifeSupportPerHour = null) => (lifeSupportPerHour ?? LifeSupportPerHour()) * 320;

	public AboveGroundAgent2(int parent, OrganTypes organ, Quaternion orientation, float initialEnergy, float radius = InitialRadius, float length = InitialLength)
	{
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Organ = organ;

		Energy = initialEnergy;
		mPhotoFactor = 1f;

		Water = 0f;

		PreviousDayProduction = 0f;
		CurrentDayProduction = 0f;
		PreviousDayEnvResources = 0f;
		CurrentDayEnvResources = 0f;
		//Children = null;
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(AboveGroundAgent2[] src, int[] map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	public void CensusUpdateParent(int newParent) => Parent = newParent;

	const float TwoPi = MathF.PI * 2f;
	const float TwoPiTenth = MathF.PI * 0.2f;
	public const float LeafThickness = 0.0001f;

	public void Tick(SimulationWorld _world, IFormation _formation, int formationID, uint timestep, byte stage)
	{
		var world = _world as AgroWorld;
		var formation = (PlantSubFormation2<AboveGroundAgent2>)_formation;
		var plant = formation.Plant;

		if (plant.IsNewDay())
		{
			PreviousDayProduction = CurrentDayProduction;
			PreviousDayEnvResources = CurrentDayEnvResources;
			CurrentDayProduction = 0f;
			CurrentDayEnvResources = 0f;
		}

		//TODO perhaps growth should somehow reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour();
		var lifeSupportPerTick = LifeSupportPerTick(world);

		Energy -= lifeSupportPerTick; //life support

		var children = formation.GetChildren(formationID);

		var enoughEnergyState = EnoughEnergy(lifeSupportPerHour);

		//Photosynthesis
		if ((Organ == OrganTypes.Stem || Organ == OrganTypes.Leaf) && Water > 0f)
		{
			var approxLight = IrradianceClient.GetIrradiance(formation, formationID); //in Watt per Hour per m²
			if (approxLight > 0.01f)
			{
				var airTemp = world.GetTemperature(timestep);
				var surface = Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPi);
				var possibleAmountByLight = surface * approxLight * mPhotoFactor * (Organ == OrganTypes.Stem ? 0.1f : 1f);
				var possibleAmountByWater = Water;
				var possibleAmountByCO2 = airTemp >= plant.VegetativeHighTemperature.Y
					? 0f
					: (airTemp <= plant.VegetativeHighTemperature.X
						? float.MaxValue
						: surface * (airTemp - plant.VegetativeHighTemperature.X) / (plant.VegetativeHighTemperature.Y - plant.VegetativeHighTemperature.X)); //TODO respiratory cycle

				//simplified photosynthesis equation:
				//CO_2 + H2O + photons → [CH_2 O] + O_2
				var photosynthesizedEnergy = Math.Min(possibleAmountByLight * mPhotoEfficiency, Math.Min(possibleAmountByWater, possibleAmountByCO2));

				Water -= photosynthesizedEnergy;
				//Energy += AgroWorld.W2J(photosynthesizedEnergy, 3600 / AgroWorld.TicksPerHour); //TODO in th efuture convert energy to cal
				Energy += photosynthesizedEnergy;
				CurrentDayProduction += photosynthesizedEnergy;
				CurrentDayEnvResources += approxLight;
			}
		}

		//Growth
		if (Energy > enoughEnergyState) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			if (Organ != OrganTypes.Bud)
			{
				var isLeafStem = false;
				Vector2 growth;
 				var childrenCount = children.Count + 1;
				switch(Organ)
				{
					case OrganTypes.Leaf:
					{
						//TDMI take env res efficiency into account
						//TDMI limit by avg growth
						growth = new Vector2(2e-4f, 1e-4f) * world.HoursPerTick;
					}
					break;
					case OrganTypes.Stem:
					{
						isLeafStem = childrenCount == 2 && formation.GetOrgan(children[0]) == OrganTypes.Leaf;
						if (isLeafStem)
						{
							growth = new Vector2(1e-4f, 3e-6f) * world.HoursPerTick;
						}
						else
						{
							//var waterUsage = Math.Clamp(Water / WaterTotalCapacityPerTick, 0f, 1f);
							var energyUsage = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
							//TDMI take relative inverse depth
							var absDepth = formation.GetAbsInvDepth(formationID) + 1;
							growth = new Vector2(5e-3f / (MathF.Pow(absDepth, 1.5f) * childrenCount * mPhotoFactor), 2e-5f) * (energyUsage * world.HoursPerTick);
						}
					}
					break;
					default: growth = Vector2.Zero; break;
				};

				Length += growth.X;
				Radius += growth.Y;

				//TDMI maybe do it even if no growth
				if (Organ == OrganTypes.Stem)
				{
					mPhotoFactor -= 8f * growth.Y;
					mPhotoFactor = Math.Clamp(mPhotoFactor, 0f, 1f);

					// if (Parent == -1 || children.Count > 1)
					// 	Console.WriteLine($"{(Parent == -1 ? '⊥' : '⊤')} ID: {formationID} wood = {WoodRatio}");
				}

				//Chaining or branching
				if (Organ == OrganTypes.Stem && !isLeafStem)
				{
					var thresholdFactor = (2f - mPhotoFactor) * 0.5f;
					if (Length > PlantFormation2.RootSegmentLength * thresholdFactor + plant.RNG.NextFloat(PlantFormation2.RootSegmentLength * thresholdFactor))
					{
						//split this stem into two parts the new one will be the bottom part closer to the root
						formation.Insert(formationID, new(Parent, OrganTypes.Stem, Orientation, Energy * 0.5f, Radius, Length * 0.5f){ Water = Water * 0.5f, mPhotoFactor = mPhotoFactor});
						Length *= 0.5f;
						Energy *= 0.5f;
						Water *= 0.5f;
						if (children.Count > 0)
						{
							var childrenRadius = 0f;
							foreach(var child in children)
								childrenRadius = Math.Max(childrenRadius, formation.GetBaseRadius(child));
							Radius = (Radius + childrenRadius) * 0.5f;
						}
						var dir = Vector3.Transform(Vector3.UnitX, Orientation);
						if (dir.Y < 0.999f)
							Orientation = Quaternion.Slerp(Orientation, OrientationUp, mPhotoFactor * plant.RNG.NextFloat(0.01f)); //TODO keep the Yaw as is, right now it orients all the same!
					}
					else //branching
					{
						var waterFactor = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
						//var energyFactor = Math.Clamp(Energy / EnergyStorageCapacity, 0f, 1f);
						var stemChildrenCount = 0;
						foreach(var child in children)
							if (formation.GetOrgan(child) == OrganTypes.Stem)
								++stemChildrenCount;
						var pool = Math.Max(1, Math.Ceiling(MathF.Pow(childrenCount, childrenCount << 2) / world.HoursPerTick));
						var relativeDepth = formation.GetRelDepth(formationID);
						if (pool < uint.MaxValue && plant.RNG.NextUInt((uint)pool) == 1 && Math.Min(Math.Min(waterFactor, mPhotoFactor), 1f-relativeDepth) > plant.RNG.NextFloat())
						{
							const float xFactor = 0.5f;
							const float zFactor = 0.2f;
							var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, plant.RNG.NextFloat(-MathF.PI * xFactor, MathF.PI * xFactor));
							var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, plant.RNG.NextFloat(MathF.PI * zFactor, MathF.PI * zFactor * 2f));
							//var q = qz * qx * Orientation;
							var orientation = Orientation * qx * qz;
							var energy = EnergyCapacityFunc(InitialRadius, InitialLength, 0f);
							var water = WaterStorageCapacityfunction(InitialRadius, InitialLength);
							if (Water > 30f * water && Energy > 100f * energy)
							{
								var stem = formation.Birth(new(formationID, OrganTypes.Stem, orientation, energy) { Water = water } );
								var leafStem = formation.Birth(new(stem, OrganTypes.Stem, orientation, energy) { Water = water });
								formation.Birth(new(leafStem, OrganTypes.Leaf, orientation, energy) { Water = water } );
								Energy -= 100f * energy; //because some energy is needed for the birth itself
								Water -= 30f * water;
							}
							//Console.WriteLine($"New root branched to {formationID} at time {timestep}");
						}
					}
				}
				//Console.WriteLine($"{formationID}x{timestep}: l={Length} r={Radius} OK={Length > Radius}");
			}
		}
		else if (Energy <= 0f)
		{
			formation.Death(formationID);
			return;
		}
	}

	public void IncWater(float amount)
	{
		Debug.Assert(amount >= 0f);
		Water += amount;
	}
	public void IncEnergy(float amount)
	{
		Debug.Assert(amount >= 0f);
		Energy += amount;
	}

	internal float TryDecWater(float amount)
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

	internal float TryDecEnergy(float amount)
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

	public bool ChangeAmount(PlantFormation2 plant, int index, int substanceIndex, float amount, bool increase) => substanceIndex switch {
		(byte)PlantSubstances.Water => plant.Send(index, increase ? new WaterInc(amount) : new WaterDec(amount)),
		(byte)PlantSubstances.Energy => plant.Send(index, increase ? new EnergyInc(amount) : new EnergyDec(amount)),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	public void Distribute(float water, float energy)
	{
		Energy = energy;
		Water = water;
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
