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
public partial struct AboveGroundAgent3 : IPlantAgent
{
	readonly uint BirthTime;
	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Radius of the parent at birth of this agent. Used for sheding leaves and removing buds from older branches as they grow thicker
	/// </summary>
	public float ParentRadiusAtBirth { get; private set; } = 0f;

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

	public readonly Vector3 Scale() => Organ switch {
		OrganTypes.Leaf => new(Length, 0.0001f, 2f * Radius),
		_ => new(Length, 2f * Radius, 2f * Radius)
	};

	public readonly float Volume() => Organ switch {
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
	//public float Cytokinins { get; set; }
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

	float CurrentDayEnvResources { get; set; }
	public float PreviousDayEnvResources { get; private set; }

	/// <summary>
	/// Woodyness ∈ [0, 1].
	/// </summary>
	float WoodFactor;

	public float WoodRatio() => WoodFactor;

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

	#region Variances
	float LengthVar;
	float RadiusVar;
	float GrowthTimeVar;
	#endregion

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per hour
	/// </summary>
	public readonly float WaterFlowToParentPerHour() => 4f * Radius * Radius * WaterTransportRatio;

	/// <summary>
	/// Water volume in m³ which can be passed to the parent per timestep
	/// </summary>
	public readonly float WaterFlowToParentPerTick(AgroWorld world) => WaterFlowToParentPerHour() * world.HoursPerTick;

	public readonly float EnergyFlowToParentPerHour() => 4f * Radius * Radius * WaterTransportRatio;

	public readonly float EnergyFlowToParentPerTick(AgroWorld world) => EnergyFlowToParentPerHour() * world.HoursPerTick;

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
	public readonly float WaterStorageCapacity() => WaterStorageCapacityfunction(Radius, Length);

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

	static float EnergyCapacityFunc(float radius, float length, float woodRatio) => 4f * radius * radius * length * MathF.Pow(1f + woodRatio, 3) * EnergyStorageCoef;

	public readonly float EnergyStorageCapacity() => EnergyCapacityFunc(Radius, Length, WoodFactor);

	public readonly static Quaternion OrientationUp = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);

	public const float GrowthRatePerHour = 1 + 1/12; //Let us assume this plant can double volume in 12 hours
	public readonly float GrowthRatePerTick(AgroWorld world) => GrowthRatePerHour * world.HoursPerTick; //Let us assume this plant can double volume in 12 hours

    public readonly float LifeSupportPerHour() => Length * Radius * (Organ == OrganTypes.Leaf ? LeafThickness : Radius * WoodFactor);
	public readonly float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;

	public const float mPhotoEfficiency = 0.025f;
	public const float ExpectedIrradiance = 500f; //in W/m² per hour see https://en.wikipedia.org/wiki/Solar_irradiance
	public readonly float PhotosynthPerTick(AgroWorld world) => Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPiTenth) * mPhotoEfficiency * ExpectedIrradiance * world.HoursPerTick;

    readonly float EnoughEnergy(float? lifeSupportPerHour = null) => (lifeSupportPerHour ?? LifeSupportPerHour()) * 320;

	public AboveGroundAgent3(PlantFormation2 plant, int parent, OrganTypes organ, Quaternion orientation, float initialEnergy, float radius = InitialRadius, float length = InitialLength, float initialResources = 0f, float initialProduction = 0f)
	{
		BirthTime = plant.World.Timestep;
		Parent = parent;
		Radius = radius;
		Length = length;
		Orientation = orientation;

		Organ = organ;

		Energy = initialEnergy;
		WoodFactor = 0f;

		Water = 0f;

		PreviousDayProductionInv = initialProduction;
		CurrentDayProductionInv = 0f;
		PreviousDayEnvResourcesInv = initialResources;
		CurrentDayEnvResourcesInv = 0f;
		PreviousDayEnvResources = initialResources * Length * Radius * 2f;
		CurrentDayEnvResources = 0f;

		FirstSegmentIndex = plant.InsertSegments(SegmentsCount, orientation);

		Auxins = 0f;
		//Cytokinins = 0f;

		var species = plant.Parameters;
		switch (Organ)
		{
			case OrganTypes.Leaf:
			{
				LengthVar = plant.RNG.NextFloatVar(species.LeafLengthVar);
				RadiusVar = plant.RNG.NextFloatVar(species.LeafRadiusVar);
				GrowthTimeVar = plant.World.HoursPerTick / (species.LeafGrowthTime + plant.RNG.NextFloatVar(species.LeafGrowthTimeVar));
			}
			break;

			case OrganTypes.Petiole:
			{
				LengthVar = plant.RNG.NextFloatVar(species.PetioleLengthVar);
				RadiusVar = plant.RNG.NextFloatVar(species.PetioleRadiusVar);
				GrowthTimeVar = plant.World.HoursPerTick / (species.LeafGrowthTime + plant.RNG.NextFloatVar(species.LeafGrowthTimeVar));
			}
			break;

			case OrganTypes.Meristem:
			{
				LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);
				RadiusVar = 0f;
				GrowthTimeVar = 0f;
			}
			break;

			case OrganTypes.Stem:
			{
				LengthVar = 0f;
				RadiusVar = 0f;
				GrowthTimeVar = plant.World.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
			}
			break;

			default:
			{
				LengthVar = 0f;
				RadiusVar = 0f;
				GrowthTimeVar = 0f;
			}
			break;
		}

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
	public void Tick(IFormation _formation, int formationID, uint timestep)
	{
		var formation = (PlantSubFormation2<AboveGroundAgent3>)_formation;
		var plant = formation.Plant;
		var species = plant.Parameters;
		var world = plant.World;
		var age = timestep - BirthTime;

		//TODO perhaps growth should somehow reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour();
		var lifeSupportPerTick = LifeSupportPerTick(world);

		Energy -= lifeSupportPerTick; //life support

		var children = formation.GetChildren(formationID);

		var enoughEnergyState = EnoughEnergy(lifeSupportPerHour);
		var wasMeristem = false;
		//Photosynthesis
		if (Organ == OrganTypes.Leaf && Water > 0f)
		{
			var approxLight = world.Irradiance.GetIrradiance(formation, formationID); //in Watt per Hour per m²
			if (approxLight > 0.01f)
			{
				//var airTemp = world.GetTemperature(timestep);
				//var surface = Length * Radius * (Organ == OrganTypes.Leaf ? 2f : TwoPi);
				var surface = Length * Radius * 2f;
				//var possibleAmountByLight = surface * approxLight * mPhotoFactor * (Organ != OrganTypes.Leaf ? 0.1f : 1f);
				var possibleAmountByLight = surface * approxLight;
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
				CurrentDayEnvResources += approxLight * surface;
				CurrentDayEnvResourcesInv += approxLight;
				CurrentDayProductionInv += photosynthesizedEnergy / surface;
			}
		}
		else if (Organ == OrganTypes.Meristem)
			wasMeristem = true;

		// just for DEBUG, replace by auxin determined branching in combination with age and strength based dying
		switch(Organ)
		{
			case OrganTypes.Petiole:
			{
				if (age > 36 && formation.GetOrgan(Parent) != OrganTypes.Meristem)
				{
					var p = age / (24 * 356f * 4);
					if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
						MakeBud(formation, children);
				}
			}
			break;
			case OrganTypes.Stem:
				if (DominanceLevel > 1 && formation.GetDominance(Parent) < DominanceLevel)
				{
					var h = 5f * formation.GetBaseCenter(formationID).Y / formation.Height;
					var e = 4f * PreviousDayEnvResources / formation.DailyEfficiencyMax;
					var q = 1f + h*h + 20f * Radius + e * e;
					var p = 0.004f / (q * q);
					Debug.WriteLine($"{formationID}: h {formation.GetBaseCenter(formationID).Y / formation.Height}  r {Radius}  e {PreviousDayEnvResources / formation.DailyEfficiencyMax}  =  {q}  % {p}");
					if (plant.RNG.NextFloatAccum(p, world.HoursPerTick))
					{
						Energy = 0f;
						Debug.WriteLine($"DEL STEM {formationID} % {p} @ {timestep}");
					}
				}
				break;
		}

		//Swap a leaf to a twig
		if (Organ == OrganTypes.Petiole || Organ == OrganTypes.Bud)
		{
			var parentAuxins = formation.GetAuxins(Parent);
			if (parentAuxins < species.AuxinsThreshold)
			{
				//check down towards the roots
				var ascendantIndex = formation.GetParent(Parent);
				var localMinimum = ascendantIndex < 0 || formation.GetAuxins(ascendantIndex) >= parentAuxins;

				//check up towards the leaves
				if (localMinimum)
				{
					foreach(var child in formation.GetChildren(Parent))
						if (formation.GetOrgan(child) == OrganTypes.Stem && formation.GetAuxins(child) <= parentAuxins)
						{
							localMinimum = false;
							break;
						}
				}

				//if this is a local minimum (in case of equal values the top-most is considered the local minimum)
				if (localMinimum)
				{
					var makeTwig = false;
					if (Organ == OrganTypes.Petiole)
					{
						if(formation.GetOrgan(Parent) != OrganTypes.Meristem)
						{
							if (children != null)
								foreach(var child in children)
									formation.Death(child);
							makeTwig = true;
						}
					}
					else if (Organ == OrganTypes.Bud)
						makeTwig = true;

					if (makeTwig)
					{
						Organ = OrganTypes.Meristem;
						LateralAngle = MathF.PI * 0.5f;
						++DominanceLevel;
						Orientation = TurnUpwards(Orientation);
						LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);

						if (species.LateralsPerNode > 0)
							CreateLeaves(this, plant, LateralAngle + species.LateralRoll, formationID);
					}
				}
			}
		}

		//Growth
		if (Energy > enoughEnergyState) //maybe make it a factor storedEnergy/lifeSupport so that it grows fast when it has full storage
		{
			if (Organ == OrganTypes.Bud) //Monopodial branching
			{
				if (Energy > EnergyStorageCapacity() && plant.RNG.NextUInt((uint)730 / world.HoursPerTick) < 0)
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
				var dominanceFactor = DominanceLevel < species.DominanceFactors.Length ? species.DominanceFactors[DominanceLevel] : species.DominanceFactors[species.DominanceFactors.Length - 1];
				switch(Organ)
				{
					case OrganTypes.Leaf:
					{
						//TDMI take env res efficiency into account
						//TDMI thickness of the parent and parent-parent decides the max. leaf size,
						//  also the energy consumption of the siblings beyond the node should have effect
						var sizeLimit = new Vector2(species.LeafLength + LengthVar, species.LeafRadius + RadiusVar);
						if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
						{
							//formation.GetDailyProduction(formationID) *
							growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * GrowthTimeVar;
							var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
							growth = resultingSize - currentSize;
						}
					}
					break;
					case OrganTypes.Petiole:
					{
						var sizeLimit = new Vector2(species.PetioleLength + LengthVar, species.PetioleRadius + RadiusVar);
						if (currentSize.X < sizeLimit.X && currentSize.Y < sizeLimit.Y)
						{
							growth = Math.Min(1f, plant.WaterBalance) * sizeLimit * GrowthTimeVar;
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
						var energyReserve = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
						var waterReserve = Math.Min(1f, plant.WaterBalance);
						growth = new Vector2(1f, 0.01f) * (1e-3f * dominanceFactor * energyReserve * waterReserve * world.HoursPerTick);

						var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
						if (currentSize.Y + growth.Y > parentRadius)
							growth.Y = parentRadius - currentSize.Y;
					}
					break;
					case OrganTypes.Stem:
					{
						var energyReserve = Math.Clamp(Energy / EnergyStorageCapacity(), 0f, 1f);
						growth = new Vector2(0, 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick);

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
					if (Organ == OrganTypes.Stem && WoodFactor < 1f && (Parent < 0 || WoodFactor <= formation.GetWoodRatio(Parent)))
						WoodFactor = Math.Min(WoodFactor + GrowthTimeVar, 1f);

					//chaining (meristem continues in a new segment)
					if (Organ == OrganTypes.Meristem && Length > LengthVar)
					{
						Organ = OrganTypes.Stem;
						GrowthTimeVar = plant.World.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
						wasMeristem = true;
						float prevResources, prevProduction;
						if (timestep - BirthTime > world.HoursPerTick)
						{
							prevResources = PreviousDayEnvResourcesInv;
							prevProduction = PreviousDayProductionInv;
						}
						else
						{
							prevResources = formation.DailyResourceMax;
							prevProduction = formation.DailyProductionMax;
							//Debug.WriteLine($"PREV res {prevResources} prod {prevProduction}");
						}

						if (species.MonopodialFactor < 1) //Dichotomous
						{
							if (DominanceLevel < 255)
							{
								var lateralPitch = 0.3f * MathF.PI * species.MonopodialFactor;

								var ou = TurnUpwards(Orientation);
								var orientation1 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -lateralPitch - 0.25f * MathF.PI);
								var meristem1 = formation.Birth(new(plant, formationID, OrganTypes.Meristem, RandomOrientation(plant, species, orientation1), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water = 0.1f * Water, LateralAngle = lateralPitch, DominanceLevel = (byte)(DominanceLevel + 1) } );
								var orientation2 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lateralPitch - 0.25f * MathF.PI);
								var meristem2 = formation.Birth(new(plant, formationID, OrganTypes.Meristem, RandomOrientation(plant, species, orientation2), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water = 0.1f * Water, LateralAngle = lateralPitch, DominanceLevel = (byte)(DominanceLevel + 1) } );

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
							var lateralPitch = LateralAngle + species.LateralRoll;
							var meristem = formation.Birth(new(plant, formationID, OrganTypes.Meristem, TurnUpwards(RandomOrientation(plant, species, Orientation)), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water = 0.1f * Water, LateralAngle = lateralPitch, DominanceLevel = DominanceLevel } );
							Energy *= 0.9f;
							Water *= 0.9f;

							if (species.LateralsPerNode > 0)
								CreateLeaves(this, plant, lateralPitch, meristem);
						}
					}
				}
				else
				{
					//if the stem grows too thick so that it already covers the whole bud or a portion of a petiole, they are removed. This way
					/*if (Organ == OrganTypes.Bud && (ParentRadiusAtBirth + Radius < formation.GetBaseRadius(Parent)))
						formation.Death(formationID);
					else */if (Organ == OrganTypes.Petiole && ParentRadiusAtBirth + species.PetioleCoverThreshold < formation.GetBaseRadius(Parent))
						MakeBud(formation, children);


					if (Organ == OrganTypes.Petiole && age > 36 && formation.GetOrgan(Parent) != OrganTypes.Meristem)
					{
						var production = 0f;
						for(int c = 0; c < children.Count; ++c)
							production += formation.GetDailyProductionInv(children[c]);
						//Debug.WriteLine($"T{world.Timestep} Prod: {production}");

						production /= plant.EnergyProductionMax;
						if (production < 0.5)
						{
							production = 1f - 2f * production;
							production += production;
							if (plant.RNG.NextFloatAccum(production, world.HoursPerTick))
							{
								Debug.WriteLine($"DEL LEAF {formationID} % {production} @ {timestep}");
								formation.Death(formationID);
							}
						}
					}
				}
			}
		}
		else if (Energy <= 0f)
		{
			switch (Organ)
			{
				case OrganTypes.Petiole: MakeBud(formation, children); break;
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

		Auxins = wasMeristem || Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
	}

    private void MakeBud(PlantSubFormation2<AboveGroundAgent3> formation, IList<int>? children)
    {
        Organ = OrganTypes.Bud;
        ParentRadiusAtBirth = formation.GetBaseRadius(Parent);
        Length = 2.8f * Radius;
        if (children != null)
            foreach (var child in children)
                formation.Death(child);
    }

    internal static void CreateFirstLeaves(AboveGroundAgent3 parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateLeavesBase(parent, plant, lateralAngle, meristem, 1f, 1f);
	internal readonly void CreateLeaves(AboveGroundAgent3 parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateLeavesBase(parent, plant, lateralAngle, meristem, PreviousDayEnvResourcesInv, PreviousDayProductionInv);
    static void CreateLeavesBase(AboveGroundAgent3 parent, PlantFormation2 plant, float lateralAngle, int meristem, float initialResources, float initialProduction)
    {
		var species = plant.Parameters;
        var angleStep = 2f * MathF.PI / species.LateralsPerNode;
        for (int l = 0; l < species.LateralsPerNode; ++l)
        {
			var roll = plant.RNG.NextFloatVar(species.LateralRollVar);
			var pitch = plant.RNG.NextFloatVar(species.LateralPitchVar);
            var orientation = parent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, l * angleStep + lateralAngle) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -plant.Parameters.LateralPitch);
            orientation = TurnUpwards(orientation) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, pitch);
            var petioleIdx = plant.AG.Birth(new(plant, meristem, OrganTypes.Petiole, orientation, parent.Energy * 0.1f, initialResources: initialResources, initialProduction: initialProduction) { DominanceLevel = parent.DominanceLevel, ParentRadiusAtBirth = parent.Radius }); //leaf stem
            parent.Energy *= 0.9f;

			var leafPitchVar = plant.RNG.NextFloatVar(species.LateralPitchVar);
            orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, leafPitchVar - species.LeafPitch);

            plant.AG.Birth(new(plant, petioleIdx, OrganTypes.Leaf, orientation, parent.Energy * 0.1f, initialResources: initialResources, initialProduction: initialProduction) { DominanceLevel = parent.DominanceLevel, ParentRadiusAtBirth = float.MaxValue }); //leaf
            parent.Energy *= 0.9f;
        }
    }

    private static Quaternion TurnUpwards(Quaternion orientation)
    {
		//determines a rotation that maintains the main direction (x-axis of the matrix)
		//while rotating the secondary direction (y-axis) as much upwards as possible
		//this way the twigs and leaves maintain a natural orientation
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

    private static Quaternion AdjustUpBase(Quaternion orientation)
    {
		var y = Vector3.Transform(Vector3.UnitY, orientation);
		Vector3 z;
		if (Vector3.Dot(Vector3.UnitY, y) < 0.999f)
		{
			z = Vector3.Cross(Vector3.UnitY, y);
			y = Vector3.Cross(z, Vector3.UnitY);
		}
		else
		{
			z = Vector3.Transform(Vector3.UnitZ, orientation);
			y = Vector3.Cross(z, Vector3.UnitY);
		}

		return Quaternion.CreateFromRotationMatrix(new()
		{
			M11 = 0, M12 = 1, M13 = 0, M14 = 0,
			M21 = y.X, M22 = y.Y, M23 = y.Z, M24 = 0,
			M31 = z.X, M32 = z.Y, M33 = z.Z, M34 = 0,
			M41 = 0, M42 = 0, M43 = 0, M44 = 1
		});
    }


	private readonly Quaternion RandomOrientation(PlantFormation2 plant, SpeciesSettings species, Quaternion orientation)
	{
		var range = 0.2f * MathF.PI * (species.TwigsBendingLevel * DominanceLevel - species.TwigsBendingApical);
		var factor = species.TwigsBending * range;
		var a = plant.RNG.NextFloatVar(factor);
		orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, a);
		var y = Vector3.Transform(Vector3.UnitX, orientation).Y;

		if (y < 0)
			orientation = Quaternion.Slerp(orientation, AdjustUpBase(orientation), plant.RNG.NextPositiveFloat(-y));
		else //if (species.ShootsGravitaxis > 0)
		  	orientation = Quaternion.Slerp(orientation, AdjustUpBase(orientation), plant.RNG.NextPositiveFloat(species.ShootsGravitaxis));

		return orientation;
	}

	public bool NewDay(uint timestep, byte ticksPerDay)
	{
		var complete = timestep - BirthTime >= ticksPerDay;

		if (complete || Organ != OrganTypes.Leaf) //the or avoids max value to wrongly stay until the next day for non-leaves
		{
			PreviousDayProductionInv = CurrentDayProductionInv;
			PreviousDayEnvResourcesInv = CurrentDayEnvResourcesInv;
			PreviousDayEnvResources = CurrentDayEnvResources;
		}

		CurrentDayProductionInv = 0f;
		CurrentDayEnvResourcesInv = 0f;
		CurrentDayEnvResources = 0f;
		return complete;
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

	public readonly bool ChangeAmount(PlantFormation2 plant, int index, int substanceIndex, float amount, bool increase) => substanceIndex switch {
		(byte)PlantSubstances.Water => plant.Send(index, increase ? new WaterInc(amount) : new WaterDec(amount)),
		(byte)PlantSubstances.Energy => plant.Send(index, increase ? new EnergyInc(amount) : new EnergyDec(amount)),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	public void Distribute(float water, float energy)
	{
		Energy = energy;
		Water = water;
	}

	public void IncAuxins(float amount) => Auxins += amount;
	//public void IncCytokinins(float amount) => Cytokinins += amount;

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
		PreviousDayEnvResources = efficiency;
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
