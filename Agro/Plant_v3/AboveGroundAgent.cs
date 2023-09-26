using System;
using System.Linq;
using System.Diagnostics;
using System.Numerics;
using AgentsSystem;
using System.Runtime.InteropServices;
using CommandLine;

namespace Agro;

//TODO IMPORTANT All resource transport should be request-confirm messages, i.e. pull-policy.
//  There should never be forced resource push since it may not be able to fit into the available storage.
//TODO Create properties computing the respective storages for water and energy. Just for clarity.

[StructLayout(LayoutKind.Auto)]
public partial struct AboveGroundAgent3 : IPlantAgent
{
	//TODO add local random seed that will be fixed for this node in order to compute the variances here again and again

	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	public Quaternion Orientation { get; private set; }

	//subdivions of this node
	public int FirstSegmentIndex { get; private set; }

	//number of segments
	public byte SegmentsCount { get; private set; } = 3;

	/// <summary>
	/// Length of the agent in m.
	/// </summary>
	public float Length { get; private set; }

	/// <summary>
	/// Radius of the bottom face in m.
	/// </summary>
	public float Radius { get; private set; }

	/// <summary>
	/// Priority of the branch, depends on banching type e.g. monopodial, GetIrradiance(ag, i) etc.
	/// </summary>
	public byte DominanceLevel { get; private set; } = 1;

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

	// /// <summary>
	// /// Hormones level (in custom units)
	// /// </summary>
	// public float AbscisicAcid { get; set; }
	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	public float Auxins { get; set; }
	/// <summary>
	/// Hormones level (in custom units)
	/// </summary>
	public float Cytokinins { get; set; }
	// /// <summary>
	// /// Hormones level (in custom units)
	// /// </summary>
	// public float Gibberellins { get; set; }

	///<summary>
	///Accumulated energy output of this agent for the last 24 steps, per m² i.e. invariant of size
	///</summary>
	public float PreviousDayProductionInv { get; private set; }
	///<summary>
	///Accumulated energy output of this agent for the next 24-steps batch, per m² i.e. invariant of size
	///</summary>
	float CurrentDayProductionInv { get; set; }

	///<summary>
	///Accumulated light exposure or water intake of this agent for the last 24 steps, per m² i.e. invariant of size
	///</summary>
	public float PreviousDayEnvResourcesInv { get; private set; }
	///<summary>
	///Accumulated light exposure of this agent for the next 24-steps batch, per m² i.e. invariant of size
	///</summary>
	float CurrentDayEnvResourcesInv { get; set; }

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

	public float LateralAngle { get; private set; } = 0f;

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
	public const float ExpectedIrradiance = 500f; //in W/m² per hour see https://en.wikipedia.org/wiki/Solar_irradiance
	public float PhotosynthPerTick(AgroWorld world) => Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPiTenth) * mPhotoFactor * mPhotoEfficiency * ExpectedIrradiance * world.HoursPerTick;

	float EnoughEnergy(float? lifeSupportPerHour = null) => (lifeSupportPerHour ?? LifeSupportPerHour()) * 320;

	public AboveGroundAgent3(PlantFormation2 plant, int parent, OrganTypes organ, Quaternion orientation, float initialEnergy, float radius = InitialRadius, float length = InitialLength)
	{
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Organ = organ;

		Energy = initialEnergy;
		mPhotoFactor = 1f;

		Water = 0f;

		PreviousDayProductionInv = 0f;
		CurrentDayProductionInv = 0f;
		PreviousDayEnvResourcesInv = 0f;
		CurrentDayEnvResourcesInv = 0f;
		//Children = null;
		FirstSegmentIndex = plant.InsertSegments(SegmentsCount, orientation);

		Auxins = 0f;
		Cytokinins = 0f;
	}

	/// <summary>
	/// If the plant structure changes (agents added or removed), parent entries need to be reindexed according to the map
	/// </summary>
	public static void Reindex(AboveGroundAgent3[] src, int[] map)
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
	public void Tick(IFormation _formation, int formationID, uint timestep, byte stage)
	{
		var formation = (PlantSubFormation2<AboveGroundAgent3>)_formation;
		var plant = formation.Plant;
		var species = plant.Parameters;
		var world = plant.World;

		if (plant.IsNewDay())
		{
			PreviousDayProductionInv = CurrentDayProductionInv;
			PreviousDayEnvResourcesInv = CurrentDayEnvResourcesInv;
			CurrentDayProductionInv = 0f;
			CurrentDayEnvResourcesInv = 0f;
		}

		//TODO perhaps growth should somehow reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour();
		var lifeSupportPerTick = LifeSupportPerTick(world);

		Energy -= lifeSupportPerTick; //life support

		var children = formation.GetChildren(formationID);

		var enoughEnergyState = EnoughEnergy(lifeSupportPerHour);

		//Photosynthesis
		if (Organ == OrganTypes.Leaf && Water > 0f)
		{
			var approxLight = world.Irradiance.GetIrradiance(formation, formationID); //in Watt per Hour per m²
			if (approxLight > 0.01f && mPhotoFactor > 0f)
			{
				//var airTemp = world.GetTemperature(timestep);
				//var surface = Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPi);
				var surface = Length * Radius * 2f;
				//var possibleAmountByLight = surface * approxLight * mPhotoFactor * (Organ != OrganTypes.Leaf ? 0.1f : 1f);
				var possibleAmountByLight = surface * approxLight * mPhotoFactor;
				var possibleAmountByWater = Water;
				// var possibleAmountByCO2 = airTemp >= plant.VegetativeHighTemperature.Y
				// 	? 0f
				// 	: (airTemp <= plant.VegetativeHighTemperature.X
				// 		? float.MaxValue
				// 		: surface * (airTemp - plant.VegetativeHighTemperature.X) / (plant.VegetativeHighTemperature.Y - plant.VegetativeHighTemperature.X)); //TODO respiratory cycle

				//simplified photosynthesis equation:
				//CO_2 + H2O + photons → [CH_2 O] + O_2
				//var photosynthesizedEnergy = Math.Min(possibleAmountByLight * mPhotoEfficiency, Math.Min(possibleAmountByWater, possibleAmountByCO2));
				var photosynthesizedEnergy = Math.Min(possibleAmountByLight * mPhotoEfficiency, possibleAmountByWater);

				Water -= photosynthesizedEnergy;
				Energy += photosynthesizedEnergy;
				CurrentDayProductionInv += photosynthesizedEnergy / surface;
				CurrentDayEnvResourcesInv += approxLight;
			}
		}
		else if (Organ == OrganTypes.Meristem)
			Auxins += TwoPi * Radius * world.HoursPerTick;

		//just for DEBUG
		if (Organ == OrganTypes.Petiole && formation.GetOrgan(Parent) != OrganTypes.Meristem && plant.RNG.NextUInt((uint)8760 / world.HoursPerTick) <= 2)
			Energy = 0f;

		//Growth
		if (Energy > enoughEnergyState) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			if (Organ == OrganTypes.Bud) //Monopodial branching
			{
				if (Energy > EnergyStorageCapacity() && plant.RNG.NextUInt((uint)730 / world.HoursPerTick) <= 2)
				{
					Organ = OrganTypes.Meristem;
					LateralAngle = MathF.PI * 0.5f;
					++DominanceLevel;
					Orientation = TurnUpwards(Orientation);

					if (species.LateralsPerNode > 0)
						CreateLeaves(this, plant, LateralAngle + species.LateralRoll, formationID);
				}
			}
			else
			{
				var currentSize = new Vector2(Length, Radius);
				var growth = Vector2.Zero;
				var dominanceFactor = DominanceLevel < species.DominanceFactors.Length ? species.DominanceFactors[DominanceLevel] : species.DominanceFactors[species.DominanceFactors.Length];
				switch(Organ)
				{
					case OrganTypes.Leaf:
					{
						//TDMI take env res efficiency into account
						//TDMI thickness of the parent and parent-parent decides the max. leaf size,
						//  also the energy consumption of the siblings beyond the node should have effect
						var sizeLimit = new Vector2(species.LeafLength, species.LeafRadius);
						if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
						{
							//formation.GetDailyProduction(formationID) *
							growth = sizeLimit * world.HoursPerTick / species.LeafGrowthTime;
							var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
							growth = resultingSize - currentSize;
						}
					}
					break;
					case OrganTypes.Petiole:
					{
						var sizeLimit = new Vector2(species.PetioleLength, species.PetioleRadius);
						if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
						{
							growth = sizeLimit * world.HoursPerTick / species.LeafGrowthTime;
							var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
							growth = resultingSize - currentSize;

							var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
							if (currentSize.Y + growth.Y > parentRadius)
								growth.Y = parentRadius - currentSize.Y;
						}
					}
					break;
					case OrganTypes.Meristem:
					{
						var energyUsage = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
						growth = new Vector2(0.5f + mPhotoFactor * 0.5f, 0.01f) * (2e-3f * dominanceFactor * energyUsage * world.HoursPerTick);

						var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
						if (currentSize.Y + growth.Y > parentRadius)
							growth.Y = parentRadius - currentSize.Y;
					}
					break;
					case OrganTypes.Stem:
					{
						var waterAvailable = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
						var energyAvailable = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
						growth = new Vector2(0, 2e-5f * dominanceFactor * Math.Min(waterAvailable, energyAvailable) * world.HoursPerTick);

						var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
						if (currentSize.Y + growth.Y > parentRadius)
							growth.Y = parentRadius - currentSize.Y;
					}
					break;
				};


				Length += growth.X;
				Radius += growth.Y;

				//TDMI maybe do it even if no growth
				if (Organ == OrganTypes.Stem || Organ == OrganTypes.Meristem)
				{
					// or world.HoursPerTick / 8760f; means it takes 1 year
					mPhotoFactor -= 8f * growth.Y;
					mPhotoFactor = Math.Clamp(mPhotoFactor, 0f, 1f);
				}

				if (Organ == OrganTypes.Meristem)
				{
					var waterAvailable = Math.Clamp(Water / WaterStorageCapacity(), 0f, 1f);
					var energyAvailable = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
					const float lengthStochasticScale = 0.1f;
					if (
						Length > species.NodeDistance
						//plant.RNG.NextUInt() < PlantFormation2.TimeAccumulatedProbabilityUInt(Length * lengthStochasticScale * waterAvailable, world.HoursPerTick)
					){
						Organ = OrganTypes.Stem;
						if (species.MonopodialFactor < 1) //Dichotomous
						{
							if (DominanceLevel < 255)
							{
								var lateralPitch = 0.25f * MathF.PI * species.MonopodialFactor;

								var ou = TurnUpwards(Orientation);
								var orientation1 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -lateralPitch - 0.25f * MathF.PI);
								var meristem1 = formation.Birth(new(plant, formationID, OrganTypes.Meristem, orientation1, 0.1f * Energy) { Water = 0.1f * Water, LateralAngle = lateralPitch, DominanceLevel = (byte)(DominanceLevel + 1) } );
								var orientation2 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lateralPitch - 0.25f * MathF.PI);
								var meristem2 = formation.Birth(new(plant, formationID, OrganTypes.Meristem, orientation2, 0.1f * Energy) { Water = 0.1f * Water, LateralAngle = lateralPitch, DominanceLevel = (byte)(DominanceLevel + 1) } );

								Energy *= 0.8f;
								Water *= 0.8f;
								if (species.LateralsPerNode > 0)
								{
									CreateLeaves(this, plant, lateralPitch, meristem1);
									CreateLeaves(this, plant, lateralPitch, meristem2);
								}
							}
						}
						else
						{
							var la = LateralAngle + species.LateralRoll;
							var meristem = formation.Birth(new(plant, formationID, OrganTypes.Meristem, Orientation, 0.1f * Energy) { Water = 0.1f * Water, LateralAngle = la, DominanceLevel = DominanceLevel } );
							Energy *= 0.9f;
							Water *= 0.9f;

							if (species.LateralsPerNode > 0)
								CreateLeaves(this, plant, la, meristem);
						}
                    }
				}
			}
		}
		else if (Energy <= 0f)
		{
			switch (Organ)
			{
				case OrganTypes.Petiole:
				{
					Organ = OrganTypes.Bud;
					Length = 2.8f * Radius;
					if (children != null)
						foreach(var child in children)
							formation.Death(child);
				}
				break;
				case OrganTypes.Leaf:
				{
					formation.Death(formationID);
					formation.Death(Parent);
				}
				break;
				default: formation.Death(formationID); break;
			}

			return;
		}
	}

    internal static void CreateLeaves(AboveGroundAgent3 parent, PlantFormation2 plant, float la, int meristem)
    {
		var species = plant.Parameters;
        var angleStep = 2f * MathF.PI / species.LateralsPerNode;
        var leafPitchAngle = -(species.LeafPitch + plant.RNG.NextFloat(0.1f * MathF.PI));
        for (int l = 0; l < species.LateralsPerNode; ++l)
        {
            var orientation = parent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, l * angleStep + la) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -plant.Parameters.LateralPitch);
            orientation = TurnUpwards(orientation);

            var petioleIdx = plant.AG.Birth(new(plant, meristem, OrganTypes.Petiole, orientation, parent.Energy * 0.1f) { DominanceLevel = parent.DominanceLevel }); //leaf stem
            parent.Energy *= 0.9f;

            orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, leafPitchAngle);

            plant.AG.Birth(new(plant, petioleIdx, OrganTypes.Leaf, orientation, parent.Energy * 0.1f) { DominanceLevel = parent.DominanceLevel }); //leaf
            parent.Energy *= 0.9f;
        }
    }

    private static Quaternion TurnUpwards(Quaternion orientation)
    {
        var x = Vector3.Transform(Vector3.UnitX, orientation);
        if (Math.Abs(x.Y) < 0.999f)
        {
            var z = Vector3.Normalize(Vector3.Cross(x, Vector3.UnitY));
            var y = Vector3.Normalize(Vector3.Cross(z, x));

            orientation = Quaternion.CreateFromRotationMatrix(new()
            {
                M11 = x.X, M12 = x.Y, M13 = x.Z, M14 = 0,
                M21 = y.X, M22 = y.Y, M23 = y.Z, M24 = 0,
                M31 = z.X, M32 = z.Y, M33 = z.Z, M34 = 0,
                M41 = 0, M42 = 0, M43 = 0, M44 = 1
            });
        }

        return orientation;
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
