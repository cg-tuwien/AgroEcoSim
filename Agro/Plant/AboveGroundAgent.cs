using AgentsSystem;
using Microsoft.VisualBasic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;

namespace Agro;

/// <summary>
/// Position at which a rhizome bud originated on its supporting shoot.
/// </summary>
public enum RhizomeBudPosition : byte
{
	/// <summary>The agent is not a dormant rhizome bud.</summary>
	None,

	/// <summary>A primary rhizome bud formed at the seedling's basal crown.</summary>
	Basal,

	/// <summary>A lateral bud in the axil of a reduced rhizome leaf.</summary>
	Axillary
}

[StructLayout(LayoutKind.Auto)]
public partial struct AboveGroundAgent : IPlantAgent
{
	public ulong StableId { get; set; }
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	/// <summary>
	/// Simulation step when the agent was created
	/// </summary>
	public uint BirthTime { get; private set; }
	/// <summary>
	/// Orientation with respect to the parent. If there is no parent, this is the initial orientation.
	/// </summary>
	public Quaternion Orientation { get; private set; }

	/// <summary>
	/// Radius of the parent at birth of this agent. Used for sheding leaves and removing buds from older branches as they grow thicker
	/// </summary>
	public float ParentRadiusAtBirth { get; private set; } = 0f;

	//subdivions of this node (work in progress)
	public int FirstSegmentIndex { get; private set; }

	//number of segments (work in progress)
	//public byte SegmentsCount { get; private set; } = 2;

	/// <summary>
	/// Length of the agent in meters.
	/// </summary>
	public float Length { get;  set; }

	/// <summary>
	/// Radius of the bottom face in meters.
	/// </summary>
	public float Radius { get;  set; }

	/// <summary>
	/// Length ratio (0..1) where the leaf reaches its maximum radius
	/// </summary>
	public float LeafMaxRadiusRatio { get; private set; }

	/// <summary>
	/// Priority of the branch, depends on banching type e.g. monopodial, GetIrradiance(ag, i) etc.
	/// </summary>
	public byte DominanceLevel { get;  set; } = 1;

	/// <summary>
	/// Non-uniform scale vector along the local axes
	/// </summary>
	[M(AI)]public readonly Vector3 Scale() => Organ switch {
		// Petals are laminae, like leaves.  Their radius describes half of the
		// visible width, not half of a solid stem diameter.
		OrganTypes.Leaf or OrganTypes.FlowerPadel => new(Length, LeafThickness, 2f * Radius),
		_ => new(Length, 2f * Radius, 2f * Radius)
	};

	/// <summary>
	/// Volume of the agent in m³.
	/// </summary>
	[M(AI)]public readonly float Volume() => Organ switch {
		// Keep the physical model aligned with the flat-petal rendering.  Treating
		// a petal as a square solid (4 * length * radius²) makes a 14 mm-wide
		// Geranium petal more than a centimetre thick and loads its stem by grams.
		OrganTypes.Leaf or OrganTypes.FlowerPadel => Length * (2f * LeafThickness) * Radius,
		_ => Length * 4f * Radius* Radius
	};

	static Vector2 LimitPetioleGrowthToSupport(Vector2 currentSize, Vector2 sizeLimit, Vector2 growth, float parentRadius)
	{
		if (currentSize.Y + growth.Y > parentRadius)
			growth.Y = parentRadius - currentSize.Y;
		growth.Y = MathF.Max(0f, growth.Y);

		// Do not let longitudinal growth run ahead while radial growth is
		// temporarily limited by a young parent.  Otherwise a petiole can reach
		// full length with a tiny radius and immediately buckle under its leaf.
		if (sizeLimit.Y > 1e-8f)
		{
			var supportedLength = sizeLimit.X * Math.Clamp((currentSize.Y + growth.Y) / sizeLimit.Y, 0f, 1f);
			growth.X = MathF.Min(growth.X, MathF.Max(0f, supportedLength - currentSize.X));
		}

		return growth;
	}

	/// <summary>
	/// Births a petiole as a chain of segments and returns the LAST one, which is what a blade hangs
	/// off. A species that does not divide its petioles gets a single organ, exactly as before.
	/// </summary>
	/// <remarks>
	/// Size variance belongs to the petiole, not to each of its segments - the same lesson the blade
	/// chain taught. It is drawn only when there is a chain to share it with, so an undivided species
	/// consumes the RNG exactly as it did and no scene is reshuffled by a change meant for the
	/// long-petioled ones. Every segment starts along the same heading because orientations here are
	/// absolute.
	/// </remarks>
	static int BirthPetiole(PlantFormation2 plant, int parentIndex, Quaternion orientation, float energy,
		float water, byte dominance, float parentRadius, float initialResources, float initialProduction)
	{
		var species = plant.Parameters;
		var segments = species.PetioleSegmentCount;
		var shared = segments > 1;
		var lengthVar = shared ? plant.RNG.NextFloatVar(species.PetioleLengthVar) : 0f;
		var radiusVar = shared ? plant.RNG.NextFloatVar(species.PetioleRadiusVar) : 0f;
		var share = 1f / segments;

		for (int segment = 0; segment < segments; ++segment)
		{
			var born = new AboveGroundAgent(plant, parentIndex, OrganTypes.Petiole, orientation,
				energy * (shared ? share : 1f),
				initialResources: initialResources, initialProduction: initialProduction)
			{
				Water_g = water * (shared ? share : 1f),
				DominanceLevel = dominance,
				ParentRadiusAtBirth = parentRadius
			};
			if (shared)
			{
				born.LengthVar = lengthVar;
				born.RadiusVar = radiusVar;
			}
			parentIndex = plant.AG.Birth(born);
		}
		return parentIndex;
	}

	/// <summary>The first segment of a petiole, walking back through its own segments.</summary>
	internal static int PetioleChainRoot(PlantSubFormation<AboveGroundAgent> formation, int index)
	{
		for (var guard = 0; index >= 0; ++guard)
		{
			var parent = formation.GetParent(index);
			if (parent < 0 || formation.GetOrgan(parent) != OrganTypes.Petiole || guard > SpeciesSettings.MaximumPetioleSegments)
				return index;
			index = parent;
		}
		return index;
	}

	/// <summary>Total length of a petiole, summed over the segments it is built from.</summary>
	/// <remarks>
	/// The support a blade gets is a property of the whole petiole. Reading only the segment the
	/// blade hangs off would report a fraction of the length and throttle every leaf of a
	/// segmented species to that fraction forever.
	/// </remarks>
	internal static float PetioleChainLength(PlantSubFormation<AboveGroundAgent> formation, int index)
	{
		var total = 0f;
		for (var guard = 0; index >= 0 && formation.GetOrgan(index) == OrganTypes.Petiole; ++guard)
		{
			total += formation.GetLength(index);
			if (guard > SpeciesSettings.MaximumPetioleSegments)
				break;
			index = formation.GetParent(index);
		}
		return total;
	}

	/// <summary>
	/// The petiole a blade hangs from, walking back through the blade's own segments.
	/// </summary>
	/// <remarks>
	/// A segmented blade is a chain of leaf organs, so only its FIRST segment has the petiole as
	/// its parent. Asking each segment about its own parent therefore throttled segment 0 against a
	/// young petiole and let segments 1..n grow free, which put a systematic step at the joint to
	/// the sheath on every leaf of the plant.
	/// </remarks>
	internal static int BladePetiole(PlantSubFormation<AboveGroundAgent> formation, int parent)
	{
		//Bounded by SpeciesSettings.MaximumLeafSegments, so this cannot walk a long way.
		for (var guard = 0; parent >= 0 && formation.GetOrgan(parent) == OrganTypes.Leaf; ++guard)
		{
			if (guard > SpeciesSettings.MaximumLeafSegments)
				return -1;
			parent = formation.GetParent(parent);
		}
		return parent;
	}

	static Vector2 LimitLeafGrowthToPetioleSupport(
		PlantSubFormation<AboveGroundAgent> formation,
		int parent,
		Vector2 currentSize,
		Vector2 sizeLimit,
		Vector2 growth,
		Vector2 petioleSizeLimit)
	{
		parent = BladePetiole(formation, parent);
		if (parent < 0 || (formation.GetOrgan(parent) != OrganTypes.Petiole && formation.GetOrgan(parent) != OrganTypes.FlowerPedicel))
			return growth;

		// Maturity is relative to this petiole's sampled adult dimensions. Comparing
		// with the species mean permanently stunts blades on smaller adult petioles.
		// Floral bracts use fixed dimensions; their fields are not these variances.
		ref readonly var petiole = ref formation.GetAgent(parent);
		if (petiole.Organ == OrganTypes.Petiole && !petiole.FlowerAgent.flowerBase)
			petioleSizeLimit += new Vector2(petiole.LengthVar, petiole.RadiusVar);

		//The whole petiole, not the one segment the blade happens to hang off.
		var grown = formation.GetOrgan(parent) == OrganTypes.Petiole
			? PetioleChainLength(formation, parent)
			: formation.GetLength(parent);
		var lengthSupport = petioleSizeLimit.X > 1e-8f ? grown / petioleSizeLimit.X : 1f;
		var radiusSupport = petioleSizeLimit.Y > 1e-8f ? formation.GetBaseRadius(parent) / petioleSizeLimit.Y : 1f;
		var support = Math.Clamp(MathF.Min(lengthSupport, radiusSupport), 0f, 1f);
		var supportedSize = sizeLimit * support;
		var resultingSize = Vector2.Min(currentSize + growth, supportedSize);
		return Vector2.Max(resultingSize - currentSize, Vector2.Zero);
	}


	public Vector3 BaseOffset { get; set; } = new Vector3(0, 0, 0);
    /// <summary>
    /// Agent energy (umbrella for mainly sugars created by photosynthesis, in custom units)
    /// </summary>
    public float Energy { get;  set; }

	/// <summary>
	/// Water volume in gramms
	/// </summary>
	public float Water_g { get;  set; }
    /// <summary>
    /// Farbe
    /// </summary>
    public Vector3 Color { get; set; }

    // /// <summary>
    // /// Hormones level (in custom units)
    // /// </summary>
    // public float AbscisicAcid { get; set; }

    /// <summary>
    /// Hormones level (in custom units)
    /// </summary>
    /// <remarks>
    /// Auxins emerge in meristem and propagate away. Simplified in this simulation, they only keeps buds inactive in the vicinity of meristem.
    /// </remarks>
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
	public float PreviousDayProductionInvariant { get; private set; }
	///<summary>
	///Accumulated energy output of this agent for the next 24-steps batch, per m² i.e. invariant of size
	///</summary>
	float CurrentDayProductionInv { get; set; }

	///<summary>
	///Accumulated light exposure or water intake of this agent for the last 24 steps, per m² i.e. invariant of size
	///</summary>
	public float PreviousDayEnvResourcesInvariant { get; private set; }
	///<summary>
	///Accumulated light exposure of this agent for the next 24-steps batch, per m² i.e. invariant of size
	///</summary>
	float CurrentDayEnvResourcesInv { get; set; }

    [M(AI)] public void SetCurrentDayEnvResourcesInv(float value) => this.CurrentDayEnvResourcesInv = value;

    ///<summary>
    ///Accumulated light exposure of this agent during this day so far, in absolute units
    ///</summary>
    float CurrentDayEnvResources { get; set; }

	///<summary>
	///Accumulated light exposure of this agent during the previous day, in absolute units
	///</summary>
	public float PreviousDayEnvResources { get; private set; }


	/// <summary>
	/// Timesteps count from birth until adulcy
	/// </summary>
	//float AdulcyAge;
	[M(AI)]public readonly float Adulcy(uint timestep)
	{
		var result = (timestep - BirthTime) * GrowthTimeVar; //since gorwth time is 1 / adulcyAge we multiply instead of dividing
		return result < 1f ? result : 1f;
	}

	/// <summary>
	/// Whether this organ has lived twice its nominal growth duration. A floral segment or bud
	/// that a solid obstacle keeps from reaching its target length still differentiates: the
	/// inflorescence continues from the compressed internode instead of waiting for a length it
	/// can never reach. Vegetative organs have no nominal duration and are never overdue.
	/// </summary>
	[M(AI)]public readonly bool ElongationOverdue(uint timestep)
		=> GrowthTimeVar > 0f && (timestep - BirthTime) * GrowthTimeVar >= 2f;

	public float Stress { get; private set; } = 0f;

	/// <summary>
	/// Timesteps needed for the organ to die. If the process has not been started yet, the value is zero
	/// </summary>
	ushort SenescenceDuration = 0;

	/// <summary>
	/// Timestep when the organ started to die. If the process has not been started yet, the value is zero
	/// </summary>
	uint SenescenceStart = 0;

	[M(AI)]public readonly float Senescence(uint timestep)
	{
		if (SenescenceStart == 0 || SenescenceDuration == 0) return 0f;
		float t = timestep - SenescenceStart;
		var result = t / SenescenceDuration;
		return result < 1f ? result : 1f;
	}

	/// <summary>
	/// Woodyness ∈ [0, 1].
	/// </summary>
	float WoodFactor;

	/// <summary>
	/// Woodyness ∈ [0, 1].
	/// </summary>
	[M(AI)]public readonly float WoodRatio() => WoodFactor;

	/// <summary>
	/// Plant organ, e.g. stem, leaft, fruit
	/// </summary>
	public OrganTypes Organ { get;  set; }

	/// <summary>
	/// Index of the parent agent. -1 represents the root of the hierarchy.
	/// </summary>
	public int Parent { get; private set; }

	//TODO This is probably hte lateral pitch with respect to the parent, but since Roll is also assigned once this should be checked
	public float LateralAngle { get;  set; } = 0f;

	/// <summary>
	/// Recommended initial length of the agent at birth in m.
	/// </summary>
	public const float InitialLength = 1e-5f;

	/// <summary>
	/// Recommended initial bottom face radius of the agent at birth in m.
	/// </summary>
	public const float InitialRadius = 0.2e-5f;

	public const float InitialMaxRadiusRatio = 0.4f;

	public const float EnergyTransportRatio = 4f;
	//So far same as for undergrounds
	public const float WaterTransportRatio = 1.8f;

    public bool IsRizome { get; set; } = false;

	/// <summary>Developmental state used only by rhizome segments and buds.</summary>
	public struct RhizomeDevelopmentState
	{
		/// <summary>Graph depth of this segment or bud below the seedling's basal rhizome.</summary>
		public int SegmentDepth;

		/// <summary>
		/// Position of the bud from which this axis originates. The value remains
		/// available after activation so morphology probes can distinguish axes.
		/// </summary>
		public RhizomeBudPosition OriginatingBudPosition;

		/// <summary>Whether the seedling's basal rhizome has formed its configured primary buds.</summary>
		public bool BasalBudsFormed;

		/// <summary>
		/// Consecutive ticks on which this apex found an established rhizome in its
		/// path. A creeping apex is deflected around an obstruction rather than
		/// aborting on first contact; it only stops once it stays blocked.
		/// </summary>
		public byte BlockedAttempts;
	}

	/// <summary>Rhizome-specific developmental state for this agent.</summary>
	public RhizomeDevelopmentState RhizomeDevelopment;

	public Flower FlowerAgent { get; set; } = new Flower();
	//gravity
	public Quaternion baseOrientation { get; set; }
    public Quaternion restOrientation { get; set; }
    public Quaternion targetOrientation { get; set; }


    public int CrownIndex { get; set; } = 0;

	/// <summary>
	/// March-to-February seasonal cycle in which this crown bud last attempted activation.
	/// </summary>
	public int LastCrownActivationCycle { get; set; } = int.MinValue;

	public bool flowerSupport { get; set; } = true;

	/// <summary>
	/// A committed floral axis needs a vegetative successor. The successor is a separate
	/// collision-checked birth, so crowded leaves cannot veto placement of the scape.
	/// Cleared only when that birth is accepted or another apex makes it unnecessary.
	/// </summary>
	public bool FloralHandoverPending { get; set; }

	/// <summary>
	/// This organ's inadmissible increment or pending development is waiting for its next
	/// geometry retry. The flag is local and is not inherited by a whole branch.
	/// </summary>
	/// <remarks>
	/// Photosynthesis, maintenance, normal senescence and independently admissible radial
	/// growth continue. Contact may still deflect this organ through its support chain.
	/// </remarks>
	public bool GrowthBlocked { get; set; }

	/// <summary>
	/// Set by the collision phase while this organ touches an upward-facing support surface.
	/// </summary>
	/// <remarks>
	/// A contact is unilateral: it does not anchor a rhizome or carry every descendant's
	/// weight. Loads remain in the moment sum and contact reactions constrain the geometry.
	/// </remarks>
	public bool GroundSupported { get; set; }
	/// <summary>Embedded substrate anchors a rhizome; mere tray contact does not.</summary>
	public bool SoilAnchored { get; set; }
	/// <summary>Unconstrained rod bending exceeds the numerical elastic limit.</summary>
	public bool LoadLimited { get; set; }
	public float ObstructionHours { get; set; }
	public void SetGeometry(float length, float radius) { Length = length; Radius = radius; }

	/// <summary>
	/// Legacy diagnostic retained for callers; the elastic contact solver does not accumulate
	/// plastic rotations or modify the organ's own rest frame.
	/// </summary>
	public float PlasticAngle { get; set; }

	/// <summary>Legacy counter retained for callers. Above-ground obstruction never cuts tissue.</summary>
	public ushort ObstructionSteps { get; set; }

    #region Variances
    /// <summary>
    /// Precomputed random variance of maximm length for this agent
    /// </summary>
    float LengthVar;
    /// <summary>
    /// Precomputed random variance of maximm radius for this agent
    /// </summary>
	float RadiusVar;
	float GrowthTimeVar;
    internal readonly float GetGrowthTimeVar() => GrowthTimeVar;
	#endregion

	public readonly float GetLengthVar() => LengthVar;
    public readonly float GetRadiusVar() => RadiusVar;

    /// <summary>
    /// Replaces this organ's target dimensions, and contracts it immediately if it has
    /// already outgrown the new target.
    /// </summary>
    /// <remarks>
    /// A herbaceous organ otherwise only ever grows, so a target alone cannot shrink one.
    /// The flower centre needs it: the closed bud is taller than the receptacle it turns
    /// into once the petals unfurl. A non-positive argument leaves that dimension alone.
    /// </remarks>
    [M(AI)] internal void RetargetSize(float length, float radius)
    {
        if (length > 0f)
        {
            LengthVar = length;
            if (Length > length) Length = length;
        }
        if (radius > 0f)
        {
            RadiusVar = radius;
            if (Radius > radius) Radius = radius;
        }
    }

    /// <summary>
    /// Water amount in gramms which can be passed to the parent per hour
    /// </summary>
    [M(AI)]public readonly float WaterFlowToParentPerHour_g() => 4f * Radius * Radius * WaterTransportRatio * UnderGroundAgent.CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water amount in gramms which can be passed to the parent per timestep
	/// </summary>
	[M(AI)]public readonly float WaterFlowToParentPerTick_g(AgroWorld world) => WaterFlowToParentPerHour_g() * world.HoursPerTick;

	[M(AI)]public readonly float EnergyFlowToParentPerHour() => 4f * Radius * Radius * EnergyTransportRatio;

	[M(AI)]public readonly float EnergyFlowToParentPerTick(AgroWorld world) => EnergyFlowToParentPerHour() * world.HoursPerTick;

	/// <summary>
	/// Volume ratio ∈ [0, 1] of the agent that can used for storing water
	/// </summary>
	const float WaterCapacityRatio = 0.75f;

	/// <summary>
	/// Water amount in gramms which can be stored in this agent
	/// </summary>
	[M(AI)]public static float WaterStorageCapacityFunction_g(float radius, float length) => 4f * radius * radius * length * WaterCapacityRatio * UnderGroundAgent.CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water amount in gramms which can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterStorageCapacity_g() => Volume() * WaterCapacityRatio * UnderGroundAgent.CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water amount in gramms which can flow through per hour, or can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterTotalCapacityPerHour_g() => 4f * Radius * Radius * (Length * WaterCapacityRatio + WaterTransportRatio) * UnderGroundAgent.CubicMetersToGrammsOfWater;

	/// <summary>
	/// Water amount in gramms which can flow through per tick, or can be stored in this agent
	/// </summary>
	[M(AI)]public readonly float WaterTotalCapacityPerTick_g(AgroWorld world) => WaterTotalCapacityPerHour_g() * world.HoursPerTick;

	/// <summary>
	/// Historical reserve-allocation coefficient in model energy units per m³.
	/// </summary>
	// This is not a physical carbohydrate capacity or a survival duration: the
	// maintenance model has organ-specific coefficients and reserves are not capped.
	const float EnergyStorageCoef = 24 * 31 * 3;

	// Preserve the historical allocation weight for zero-cost requests. It is
	// not a storage cap; the cylindrical proxy overweights a thin lamina.
	[M(AI)]public readonly float EnergyStorageCapacity() => 4f * Radius * Radius * Length * MathF.Pow(1f + WoodFactor, 3) * EnergyStorageCoef;
	// Positive-cost accounting instead weights the blade by physiological tissue
	// volume, consistent with construction mass and maintenance.
	[M(AI)]public readonly float EnergyStorageCapacity(SpeciesSettings species) =>
		ResourceUnits.Enabled(species) ? Volume() * MathF.Pow(1f + WoodFactor, 3) * EnergyStorageCoef : EnergyStorageCapacity();
	[M(AI)]public readonly float EnergyAllocationWeight(SpeciesSettings species)
		=> MathF.Max(EnergyStorageCapacity(species), ResourceUnits.ConstructionSinkPerHour(this, species));

	public readonly static Quaternion OrientationUp = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f);

	public const float GrowthRatePerHour = 1 + 1/12; //Let us assume this plant can double volume in 12 hours
	[M(AI)]public readonly float GrowthRatePerTick(AgroWorld world) => GrowthRatePerHour * world.HoursPerTick; //Let us assume this plant can double volume in 12 hours

    [M(AI)]public readonly float LifeSupportPerHour() => Length * Radius * (Organ == OrganTypes.Leaf ? LeafThickness : Radius * WoodFactor);
	[M(AI)]public readonly float LifeSupportPerTick(AgroWorld world) => LifeSupportPerHour() * world.HoursPerTick;
	[M(AI)]internal readonly float LifeSupportPerHour(SpeciesSettings species) => ResourceUnits.Enabled(species)
		? ResourceUnits.MaintenancePerHour(Volume(), WoodRatio(), species) : LifeSupportPerHour();
	[M(AI)]public readonly float LifeSupportPerTick(AgroWorld world, SpeciesSettings species)
		=> LifeSupportPerHour(species) * world.HoursPerTick;

	public const float mPhotoEfficiency = 0.005f;
	public const float ExpectedIrradiance = 500f; // W/m², an empirical water-demand forecast, not measured incident light
	// Both active Tick paths photosynthesize only in leaves. Nonphotosynthetic
	// axes and floral organs still receive their tissue-water storage share; they
	// must not also demand water for assimilation that never takes place.
	[M(AI)]public readonly float PhotosynthPerTick(AgroWorld world) => Organ == OrganTypes.Leaf
		? Length * Radius * 2f * mPhotoEfficiency * ExpectedIrradiance * world.HoursPerTick
		: 0f;
	[M(AI)]public readonly float PhotosynthPerTick(AgroWorld world, SpeciesSettings species)
		=> PhotosynthPerTick(world) * (ResourceUnits.Enabled(species) ? ResourceUnits.WaterPerGlucose : 1f);

    [M(AI)]readonly float EnoughEnergy(float? lifeSupportPerHour = null) => (lifeSupportPerHour ?? LifeSupportPerHour()) * 320;
	[M(AI)]readonly float EnoughEnergy(SpeciesSettings species) => EnoughEnergy(LifeSupportPerHour(species));

	void Photosynthesize(PlantSubFormation<AboveGroundAgent> formation, int agentID,
		SpeciesSettings species, AgroWorld world)
	{
		if (Organ != OrganTypes.Leaf || Water_g <= 0f) return;
		var irradiance = world.Irradiance.GetIrradiance(formation, agentID); // Wh/m² over this tick
		if (irradiance <= 0.01f) return;
		var area = Length * Radius * 2f;
		if (area <= 0f) return;
		var glucoseAccounting = ResourceUnits.Enabled(species);
		var production = ResourceUnits.PhotosynthesizedGlucose(irradiance, area, Water_g, glucoseAccounting);
		Water_g -= ResourceUnits.WaterDebit(production, glucoseAccounting);
		Energy += production;
		CurrentDayEnvResources += irradiance * area;
		CurrentDayEnvResourcesInv += irradiance;
		CurrentDayProductionInv += production / area;
	}


	public AboveGroundAgent(PlantFormation2 plant, int parent, OrganTypes organ, Quaternion orientation, float initialEnergy, float radius = InitialRadius, float length = InitialLength, float initialResources = 0f, float initialProduction = 0f, bool rizome = false, Flower flowerAgent = default)
	{
		BirthTime = plant.World.Timestep;
		Parent = parent;
		Radius = radius;
		LeafMaxRadiusRatio = InitialMaxRadiusRatio; //TDMI For initial testing, just a constant
		Length = length;
		Orientation = orientation;
		baseOrientation = orientation;
		restOrientation = orientation;
		targetOrientation = orientation;
		IsRizome = rizome;
		Organ = organ;

		Energy = initialEnergy;
		WoodFactor = 0f;

		Water_g = 0f;

		PreviousDayProductionInvariant = initialProduction;
		CurrentDayProductionInv = 0f;
		PreviousDayEnvResourcesInvariant = initialResources;
		CurrentDayEnvResourcesInv = 0f;
		PreviousDayEnvResources = initialResources * Length * Radius * 2f;
		CurrentDayEnvResources = 0f;

		FlowerAgent = flowerAgent;
		RhizomeDevelopment = default;

		//FirstSegmentIndex = plant.InsertSegments(SegmentsCount, orientation);

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
					Color = species.YoungLeafColor;
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
					if (IsRizome)
					{
						LengthVar = plant.Parameters.RhizomeSegmentLength + plant.RNG.NextFloatVar(species.RhizomeSegmentLengthVar);
						RadiusVar = plant.Parameters.RhizomeSegmentRadius;
					}
					else
					{
						//A rosette has compressed but non-zero internodes. With a zero
						//target the "Length <= LengthVar" growth gate is false from birth,
						//so the meristem never elongates and every leaf of the crown
						//attaches at the same point instead of stacking.
						LengthVar = MathF.Max(species.MinimumInternodeLength, species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar));
						RadiusVar = 0f;
						GrowthTimeVar = 0f;
					}
				}
			break;

			case OrganTypes.Stem:
				{


						LengthVar = 0f;
						RadiusVar = 0f;
						GrowthTimeVar = plant.World.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));


				}
			break;
            case OrganTypes.FlowerMeristem:
                {
                    LengthVar = FlowerAgent.flowerBase? species.FlowerSettings.FlowerBaseSegmentLength + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBaseSegmentLengthVariance) :  species.FlowerSettings.FlowerBranchSegmentLength + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBranchSegmentLengthVariance);
					RadiusVar = FlowerAgent.flowerBase ? species.FlowerSettings.FlowerBaseSegmentRadius + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBaseSegmentRadiusVariance) : species.FlowerSettings.FlowerBranchSegmentRadius + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBranchSegmentRadiusVariance);
					GrowthTimeVar = plant.World.HoursPerTick / (species.FlowerSettings.FlowerStemSegmentGrowthDurationHours + plant.RNG.NextFloatVar(species.FlowerStemSegmentGrowthDurationVarianceHours));
                }
                break;
            case OrganTypes.FlowerPedicel: {
					LengthVar = species.FlowerSettings.FlowerPedicelLength + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerPedicelLengthVariance);
					RadiusVar = species.FlowerSettings.FlowerPedicelRadius + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerPedicelRadiusVariance);
                    GrowthTimeVar = plant.World.HoursPerTick / (species.FlowerSettings.PetalAndPedicelGrowthDurationHours + plant.RNG.NextFloatVar(species.FlowerSettings.PetalAndPedicelGrowthDurationVarianceHours));

                }
                break;
            case OrganTypes.FlowerPadel: {
					LengthVar = species.FlowerSettings.PetalLength + plant.RNG.NextFloatVar(species.FlowerSettings.PetalLengthVariance);
					RadiusVar = species.FlowerSettings.PetalRadius;
                    GrowthTimeVar = plant.World.HoursPerTick / (species.FlowerSettings.PetalAndPedicelGrowthDurationHours + plant.RNG.NextFloatVar(species.FlowerSettings.PetalAndPedicelGrowthDurationVarianceHours));
                } break;
            case OrganTypes.FlowerBud: {
                    LengthVar = species.FlowerSettings.BudLength;
                    RadiusVar = species.FlowerSettings.BudRadius;
                    GrowthTimeVar = plant.World.HoursPerTick / (species.FlowerSettings.BudBloomDurationHours + plant.RNG.NextFloatVar(species.BudBloomDurationVarianceHours));
                } break;
            case OrganTypes.FlowerStem: {
                    // The UseDeterministicBranching internode path can create a FlowerStem
                    // directly.  A converted FlowerMeristem already carries its
                    // targets, but a direct stem does not, which otherwise leaves
                    // it at its tiny construction radius beneath a bud or petals.
                    LengthVar = FlowerAgent.flowerBase
                        ? species.FlowerSettings.FlowerBaseSegmentLength + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBaseSegmentLengthVariance)
                        : species.FlowerSettings.FlowerBranchSegmentLength + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBranchSegmentLengthVariance);
                    RadiusVar = FlowerAgent.flowerBase
                        ? species.FlowerSettings.FlowerBaseSegmentRadius + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBaseSegmentRadiusVariance)
                        : species.FlowerSettings.FlowerBranchSegmentRadius + plant.RNG.NextFloatVar(species.FlowerSettings.FlowerBranchSegmentRadiusVariance);
                    GrowthTimeVar = plant.World.HoursPerTick /
                        (species.FlowerSettings.FlowerStemSegmentGrowthDurationHours + plant.RNG.NextFloatVar(species.FlowerStemSegmentGrowthDurationVarianceHours));
                } break;
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
	[M(AI)]public static void Reindex(Span<AboveGroundAgent> src, ReadOnlySpan<int> map)
	{
		for(int i = 0; i < src.Length; ++i)
			src[i].Parent = src[i].Parent == -1 ? -1 : map[src[i].Parent];
	}

	///<summary>
	/// Use with caution, call only from census! Updates the Parent value after splitting an agent.
	///</summary>
	[M(AI)]public void CensusUpdateParent(int newParent) => Parent = newParent;

	const float TwoPi = MathF.PI * 2f;
	const float TwoPiTenth = MathF.PI * 0.2f;
	public const float LeafThickness = 0.0001f;
	public void Tick(IFormation _formation, int agentID, uint timestep)
	{
        var formation = (PlantSubFormation<AboveGroundAgent>)_formation;
        switch (formation.Plant.Parameters.Behavior)
        {
            //case Behavior.Geranium_Sanguineum: GeraniumSanguineum.TickGeraniumSanguineum(ref this, formation, agentID, timestep); break;
            case Behavior.Herbaceous: Herbaceous.Tick(ref this, formation, agentID, timestep); break;
            default: TickDefault(_formation, agentID, timestep); break;

        };
    }

    public void TickDefault(IFormation _formation, int agentID, uint timestep)
	{
        var formation = (PlantSubFormation<AboveGroundAgent>)_formation;
		//Console.WriteLine("default");
        var plant = formation.Plant;
		var species = plant.Parameters;
		var world = plant.World;
		var ageHours = (timestep - BirthTime) * world.HoursPerTick; //age in hours

		//TODO growth should reflect temperature
		var lifeSupportPerHour = LifeSupportPerHour(species);
		var lifeSupportPerTick = lifeSupportPerHour * world.HoursPerTick;

		Energy -= lifeSupportPerTick; //life support

		var children = formation.GetChildren(agentID);

		var enoughEnergyState = EnoughEnergy(lifeSupportPerHour);
		var wasMeristem = false;

		//Senescence
		if (SenescenceStart > 0)
		{
			if (SenescenceDuration == 0) //was added over a message
				InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, false);
			else if (Senescence(timestep) >= 1f)
				switch (Organ)
				{
					case OrganTypes.Petiole: MakeBud(formation, children); break; //keep an option for a new leaf
					case OrganTypes.Leaf:
					{
						formation.Death(agentID);
						formation.Death(Parent); //remove the petiole as well
					}
					break;
					default: formation.Death(agentID); break; //remove the item
				}

			return;
		}
		else if (world.Season > species.SeasonalSenescence && Organ == OrganTypes.Petiole)
			InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: false);

		Photosynthesize(formation, agentID, species, world);

		formation.MarkDevelopmentPhysiology(this);
		switch(Organ)
		{
			//leafs fall off with increasing age
			case OrganTypes.Petiole:
			{
				if (ageHours > 36 && formation.GetOrgan(Parent) != OrganTypes.Meristem)
				{
					var p = ageHours / 4032; //6 months in hours
					if (plant.RNG.NextFloatAccum(p * p, world.HoursPerTick))
						InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: false);
				}
			}
			break;

			//height-based termination
			case OrganTypes.Stem:
				if (DominanceLevel > 1 && formation.GetDominance(Parent) < DominanceLevel)
				{
					var h = 5f * formation.GetRelativeHeight(agentID);
					var e = 4f * PreviousDayEnvResources / formation.DailyEfficiencyMax;
					var q = 1f + h*h + 20f * Radius + e * e + WoodFactor;
					var p = 0.004f / (q * q);
					//Debug.WriteLine($"{formationID}: h {formation.GetBaseCenter(formationID).Y / formation.Height}  r {Radius}  e {PreviousDayEnvResources / formation.DailyEfficiencyMax}  =  {q}  % {p}");
					if (plant.RNG.NextFloatAccum(p, world.HoursPerTick))
						InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: false);
				}
				break;

			//a marker to later indicate transformation
			case OrganTypes.Meristem: wasMeristem = true; break;
		}

		//Growth
		if (Energy > enoughEnergyState && !GrowthBlocked)
		{
			//Monopodial branching, swaps a leaf to a twig
			//TODO sympodial branching (one of the laterals becomes the main)
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
						if (Organ == OrganTypes.Petiole) //activate the implicit bud at the petiole
						{
							if (formation.GetOrgan(Parent) != OrganTypes.Meristem)
							{
								if (children != null) //drop all children (i.e. the leaf)
									foreach(var child in children)
										formation.Death(child);
								makeTwig = true;
							}
						}
						else if (Organ == OrganTypes.Bud) //activate the explicit bud turning it into a new twig
							makeTwig = true;

						if (makeTwig)
						{
							Organ = OrganTypes.Meristem;
							LateralAngle = MathF.PI * 0.5f;
							++DominanceLevel;
							Orientation = TurnUpwards(Orientation);
							LengthVar = species.NodeDistance + plant.RNG.NextFloatVar(species.NodeDistanceVar);

							if (species.LateralsPerNode > 0)
								CreateLeaves(this, plant, LateralAngle + species.LateralRoll, agentID);
						}
					}
				}
			}

			//Growth and branching
			if (Organ != OrganTypes.Bud) //for simplicity dormant buds do not grow
			{
				var currentSize = new Vector2(Length, Radius);
				//dominance factor decreases the growth of structures further away from the dominant branch (based on the tree structure)
				var dominanceFactor = species.GetDominanceFactor(DominanceLevel);
				switch(Organ)
				{
					case OrganTypes.Leaf:
					{
						//TDMI take env res efficiency into account
						//TDMI thickness of the parent and parent-parent decides the max. leaf size,
						//  also the energy consumption of the siblings beyond the node should have effect
						var sizeLimit = new Vector2(species.LeafLength + LengthVar, species.LeafRadius + RadiusVar);
						if (currentSize.X < sizeLimit.X || currentSize.Y < sizeLimit.Y)
						{
							//HoursPerTick are included in GrowthTimeVar
							var growth = formation.DailyProductionMax > 1e-6f ? Math.Min(1f, plant.WaterBalance) * sizeLimit * GrowthTimeVar * (PreviousDayProductionInvariant / formation.DailyProductionMax) : Vector2.Zero;
							growth = LimitLeafGrowthToPetioleSupport(formation, Parent, currentSize, sizeLimit, growth, new(species.PetioleLength, species.PetioleRadius));
							var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
							growth = resultingSize - currentSize;
							Length += growth.X;
							Radius += growth.Y;
						}
					}
					break;
					case OrganTypes.Petiole:
					{
						var sizeLimit = new Vector2(species.PetioleLength + LengthVar, species.PetioleRadius + RadiusVar);
						if (currentSize.X < sizeLimit.X || currentSize.Y < sizeLimit.Y)
						{
							//HoursPerTick are included in GrowthTimeVar
							var growth = formation.DailyProductionMax > 1e-6f ? Math.Min(1f, plant.WaterBalance) * sizeLimit * GrowthTimeVar * (PreviousDayProductionInvariant / formation.DailyProductionMax) : Vector2.Zero;
							var resultingSize = Vector2.Min(currentSize + growth, sizeLimit);
							growth = resultingSize - currentSize;

							// Assure the petiole does not outgrow its parent or become
							// disproportionately long while the parent is still thin.
							var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
							growth = LimitPetioleGrowthToSupport(currentSize, sizeLimit, growth, parentRadius);
							Length += growth.X;
							Radius += growth.Y;
						}
					}
					break;
					case OrganTypes.Meristem:
					{
						var energyReserve = Math.Clamp(Energy / EnergyStorageCapacity(species), 0f, 1f);
						var waterReserve = Math.Min(1f, plant.WaterBalance);
						var growth = formation.DailyProductionMax > 1e-6f ? new Vector2(1e-3f, 2e-5f) * (dominanceFactor * energyReserve * waterReserve * world.HoursPerTick * (PreviousDayProductionInvariant / formation.DailyProductionMax)) : Vector2.Zero;

						//assure not to outgrow the parent
						var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
						if (currentSize.Y + growth.Y > parentRadius)
							growth.Y = parentRadius - currentSize.Y;
						Length += growth.X;
						Radius += growth.Y;
					}
					break;
					case OrganTypes.Stem:
					{
						var energyReserve = Math.Clamp(Energy / EnergyStorageCapacity(species), 0f, 1f);
						var growth = 2e-5f * dominanceFactor * energyReserve * Math.Min(plant.WaterBalance, energyReserve) * world.HoursPerTick;

						//assure not to outgrow the parent
						var parentRadius = Parent >= 0 ? formation.GetBaseRadius(Parent) : float.MaxValue;
						if (currentSize.Y + growth > parentRadius)
							growth = parentRadius - currentSize.Y;
						Radius += growth;
					}
					break;
				}

				//TDMI maybe do it even if no growth
				if (Organ == OrganTypes.Stem || Organ == OrganTypes.Meristem)
				{
					if (Organ == OrganTypes.Stem && WoodFactor < 1f)
					{
						var pw = Parent >= 0 ? formation.GetWoodRatio(Parent) : WoodFactor; //assure that no child has a higher factor than its parent
						WoodFactor = Math.Min((WoodFactor <= pw ? WoodFactor : pw) + GrowthTimeVar, 1f);
					}

					//chaining (meristem then continues in a new segment)
					if (Organ == OrganTypes.Meristem && Length > LengthVar)
					{
						Organ = OrganTypes.Stem;
						GrowthTimeVar = world.HoursPerTick / (species.WoodGrowthTime + plant.RNG.NextFloatVar(species.WoodGrowthTimeVar));
						wasMeristem = true;
						float prevResources, prevProduction;
						if (timestep - BirthTime > world.HoursPerTick)
						{
							prevResources = PreviousDayEnvResourcesInvariant;
							prevProduction = PreviousDayProductionInvariant;
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
								var meristem1 = formation.Birth(new(plant, agentID, OrganTypes.Meristem, RandomOrientation(plant, species, orientation1), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * Water_g, LateralAngle = lateralPitch, DominanceLevel = DominanceLevel } );
								var orientation2 = ou * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -0.5f * MathF.PI) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, lateralPitch - 0.25f * MathF.PI);
								var meristem2 = formation.Birth(new(plant, agentID, OrganTypes.Meristem, RandomOrientation(plant, species, orientation2), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * Water_g, LateralAngle = lateralPitch, DominanceLevel = DominanceLevel } );

								Energy *= 0.8f;
								Water_g *= 0.8f;
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
							var meristem = formation.Birth(new(plant, agentID, OrganTypes.Meristem, TurnUpwards(RandomOrientation(plant, species, Orientation)), 0.1f * Energy, initialResources: prevResources, initialProduction: prevProduction) { Water_g = 0.1f * Water_g, LateralAngle = lateralPitch, DominanceLevel = DominanceLevel } );
							Energy *= 0.9f;
							Water_g *= 0.9f;

							if (species.LateralsPerNode > 0)
								CreateLeaves(this, plant, lateralPitch, meristem);
						}
					}
				}
				else
				{
					//if the stem grows too thick so that it already covers a large portion of the petiole, it gets removed and a new bud emerges.
					if (Organ == OrganTypes.Petiole && ParentRadiusAtBirth + species.PetioleCoverThreshold < formation.GetBaseRadius(Parent))
						InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: false);

					//termination of unproductive branches
					if (Organ == OrganTypes.Petiole && ageHours > 48 && formation.GetOrgan(Parent) != OrganTypes.Meristem && children != null)
					{
						var production = 0f;
						for(int c = 0; c < children.Count; ++c)
							production += formation.GetDailyProductionInv(children[c]);
						//Debug.WriteLine($"T{world.Timestep} Prod: {production}");

						production /= plant.EnergyProductionMax;
						if (production < 0.5) //the lower half of production efficiency
						{
							production = 1f - 2f * production;
							production *= production;
							if (plant.RNG.NextFloatAccum(production, world.HoursPerTick))
							{
								Debug.WriteLine($"DEL LEAF {agentID} % {production} @ {timestep}");
								InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: true);
								//formation.Death(agentID);
							}
						}
					}
				}
			}
		}
		else if (Energy <= 0f) //remove organs that drained all their energy
        {
            InitSenescence(timestep, plant, species, formation, children, world.HoursPerTick, stressReason: true);
        }

        //update auxins for meristem and stems that were meristem in the previous step
        Auxins = wasMeristem || Organ == OrganTypes.Meristem ? species.AuxinsProduction : 0;
	}

    [M(AI)]private void InitSenescence(uint timestep, PlantFormation2 plant, SpeciesSettings species, PlantSubFormation<AboveGroundAgent> formation, IList<int>? children, ushort hoursPerTick, bool stressReason)
    {
		if (SenescenceStart > 0 && SenescenceDuration == 0)
			SenescenceDuration = (ushort)(SenescenceStart - timestep - 1);
		else
			SenescenceDuration = (ushort)((species.LeafSenescenceSeasonalDuration + plant.RNG.NextFloatVar(species.LeafSenescenceSeasonalDurationVar)) / hoursPerTick);

		SenescenceStart = timestep;
		if (children != null)
			for(int i = 0; i < children.Count; ++i)
				formation.SendProtected(children[i], new PropagateSenescence(SenescenceStart + SenescenceDuration));
    }

    [M(AI)]private void MakeBud(PlantSubFormation<AboveGroundAgent> formation, IList<int>? children)
    {
        Organ = OrganTypes.Bud;
        ParentRadiusAtBirth = !formation.GetIsRizome(Parent) ? formation.GetBaseRadius(Parent) : float.MaxValue; ;
        Length = 2.8f * Radius;
        if (children != null)
            foreach (var child in children)
                formation.Death(child);
    }
    [M(AI)]
    private void MakeFlowerBud(PlantSubFormation<AboveGroundAgent> formation)
    {
        Organ = OrganTypes.FlowerBud;
        ParentRadiusAtBirth = formation.GetBaseRadius(Parent);
        Length = 2.8f * Radius;
    }
    [M(AI)]internal static void CreateFirstLeaves(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateLeavesBase(parent, plant, lateralAngle, meristem, 1f, 1f);
	[M(AI)]internal readonly void CreateLeaves(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateLeavesBase(parent, plant, lateralAngle, meristem, PreviousDayEnvResourcesInvariant, PreviousDayProductionInvariant);
	[M(AI)] internal static void CreateLeavesFromReserve(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem,
		float initialResources, float initialProduction, float energyPerOrgan, float waterPerOrgan) =>
		CreateLeavesBase(parent, plant, lateralAngle, meristem, initialResources, initialProduction, energyPerOrgan, waterPerOrgan);

	static void CreateLeavesBase(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem,
		float initialResources, float initialProduction, float? transferredEnergyPerOrgan = null, float transferredWaterPerOrgan = 0f)
	{
		var species = plant.Parameters;
		if (species.LateralsPerNode <= 0)
			return;

		var angleStep = 2f * MathF.PI / species.LateralsPerNode;
		for (var lateralIndex = 0; lateralIndex < species.LateralsPerNode; ++lateralIndex)
		{
			var roll = plant.RNG.NextFloatVar(species.LateralRollVar);
			var pitch = plant.RNG.NextFloatVar(species.LateralPitchVar);
			var orientation = parent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, lateralIndex * angleStep + lateralAngle) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -species.LateralPitch);
			orientation = TurnUpwards(orientation) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, pitch);
			var petioleEnergy = transferredEnergyPerOrgan ?? parent.Energy * 0.1f;
			var petioleID = BirthPetiole(plant, meristem, orientation, petioleEnergy, transferredWaterPerOrgan,
				parent.DominanceLevel, parent.Radius, initialResources, initialProduction);
			if (!transferredEnergyPerOrgan.HasValue)
				parent.Energy *= 0.9f;

			CreateLeafBase(orientation, parent, plant, petioleID, initialResources, initialProduction,
				transferredEnergyPerOrgan ?? parent.Energy * 0.1f, transferredWaterPerOrgan);
			if (!transferredEnergyPerOrgan.HasValue)
				parent.Energy *= 0.9f;
		}
	}

    /// <summary>
    /// Add a blade to an already-created petiole.  This is shared by normal
    /// leaf construction and perennial leaf renewal at an existing node.
    /// </summary>
	internal static void CreateLeaf(Quaternion petioleOrientation, AboveGroundAgent energySource, PlantFormation2 plant, int petioleID, float initialResources, float initialProduction)
		=> CreateLeafBase(petioleOrientation, energySource, plant, petioleID, initialResources, initialProduction, energySource.Energy * 0.1f, 0f);

	static void CreateLeafBase(Quaternion petioleOrientation, AboveGroundAgent energySource, PlantFormation2 plant, int petioleID,
		float initialResources, float initialProduction, float initialEnergy, float initialWater)
	{
		var species = plant.Parameters;
		var leafPitchVariance = plant.RNG.NextFloatVar(species.LateralPitchVar);
		var orientation = petioleOrientation * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, leafPitchVariance - species.LeafPitch);

		//A long blade is built as a chain of segments so it can arch instead of staying a rigid
		//strip - see SpeciesSettings.LeafSegmentLength. Each is an ordinary leaf organ, and the
		//species' leaf length is divided among them, so the blade's area is unchanged.
		var segments = species.LeafSegmentCount;
		var parent = petioleID;
		//Every segment starts along the same heading - orientations here are absolute, so a segment
		//left at identity would point along world +X rather than continuing the blade - and each
		//takes an equal share of the blade's energy and water, or it would be culled on its first
		//tick for having none.
		var share = 1f / segments;
		//Size variance belongs to the BLADE, not to each of its segments. The constructor draws
		//LengthVar, RadiusVar and GrowthTimeVar per organ, and a segment is an ordinary organ, so
		//without this every segment of one leaf targeted its own width and its own length: for
		//Carex, LeafRadius 3 mm +/- 0.8 mm gave neighbouring segments of the SAME blade half widths
		//anywhere in 2.2-3.8 mm, which is the staircase the blade was drawn as. It also mis-spent
		//the variance the species author wrote down - averaging eight independent draws collapsed
		//the intended difference BETWEEN leaves by about sqrt(8) and re-expressed it as raggedness
		//INSIDE each leaf.
		//Drawn only when there is a chain to share it with: an unsegmented species must keep
		//consuming the RNG exactly as it did, or every plant in the scene is reshuffled by a change
		//that was meant to affect strap-leaved species alone.
		var shared = segments > 1;
		var lengthVar = shared ? plant.RNG.NextFloatVar(species.LeafLengthVar) : 0f;
		var radiusVar = shared ? plant.RNG.NextFloatVar(species.LeafRadiusVar) : 0f;
		var growthTimeVar = shared
			? plant.World.HoursPerTick / (species.LeafGrowthTime + plant.RNG.NextFloatVar(species.LeafGrowthTimeVar))
			: 0f;
		for (int segment = 0; segment < segments; ++segment)
		{
			var born = new AboveGroundAgent(plant, parent, OrganTypes.Leaf, orientation,
				initialEnergy * share,
				initialResources: initialResources, initialProduction: initialProduction)
			{
				Water_g = initialWater * share,
				DominanceLevel = energySource.DominanceLevel,
				ParentRadiusAtBirth = float.MaxValue
			};
			if (shared)
			{
				born.LengthVar = lengthVar;
				born.RadiusVar = radiusVar;
				born.GrowthTimeVar = growthTimeVar;
			}
			parent = plant.AG.Birth(born);
		}
	}
    [M(AI)] internal static void CreateFirstFlowerBaseLeaves(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateFlowerBaseLeavesBase(parent, plant, lateralAngle, meristem, 1f, 1f);
    [M(AI)] internal readonly void CreateFlowerBaseLeaves(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem) => CreateFlowerBaseLeavesBase(parent, plant, lateralAngle, meristem, PreviousDayEnvResourcesInvariant, PreviousDayProductionInvariant);
    static void CreateFlowerBaseLeavesBase(AboveGroundAgent parent, PlantFormation2 plant, float lateralAngle, int meristem, float initialResources, float initialProduction)
    {
        var species = plant.Parameters;
        var angleStep = 2f * MathF.PI / species.LateralsPerNode;
        for (int l = 0; l < species.LateralsPerNode; ++l)
        {
            var roll = plant.RNG.NextFloatVar(species.LateralRollVar);
            var pitch = plant.RNG.NextFloatVar(species.LateralPitchVar);
            var orientation = parent.Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, l * angleStep + lateralAngle) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -plant.Parameters.LateralPitch);
            orientation = TurnUpwards(orientation) * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll) * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, pitch);
            var petioleIdx = plant.AG.Birth(new(plant, meristem, OrganTypes.Petiole, orientation, parent.Energy * 0.1f, initialResources: initialResources, initialProduction: initialProduction) { DominanceLevel = parent.DominanceLevel, ParentRadiusAtBirth = parent.Radius,FlowerAgent=new Flower() { flowerBase = true }, LengthVar = species.FlowerSettings.FlowerBaseLeafPetioleLength, RadiusVar = species.FlowerSettings.FlowerBaseLeafPetioleRadius }); //leaf stem
            parent.Energy *= 0.9f;

            var leafPitchVar = plant.RNG.NextFloatVar(species.LateralPitchVar);
            orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitZ, leafPitchVar - species.LeafPitch);

            plant.AG.Birth(new(plant, petioleIdx, OrganTypes.Leaf, orientation, parent.Energy * 0.1f, initialResources: initialResources, initialProduction: initialProduction) { DominanceLevel = parent.DominanceLevel, ParentRadiusAtBirth = float.MaxValue, FlowerAgent = new Flower() { flowerBase = true }, LengthVar = species.FlowerSettings.FlowerBaseLeafLength,RadiusVar = species.FlowerSettings.FlowerBaseLeafRadius }); //leaf
            parent.Energy *= 0.9f;

        }
    }
    public static  Quaternion TurnUpwards(Quaternion orientation)
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

	public readonly Quaternion RandomOrientation(PlantFormation2 plant, SpeciesSettings species, Quaternion orientation, bool gravitropism = true)
	{
		var range = 0.2f * MathF.PI * (species.TwigsBendingByLevel * DominanceLevel - species.EffectiveTwigsBendingApical);
		var factor = species.TwigsBending * range;
		var a = plant.RNG.NextFloatVar(factor);
		orientation *= Quaternion.CreateFromAxisAngle(Vector3.UnitY, a);
		if (!gravitropism || species.EffectiveShootsGravitaxis <= 0f) return orientation;
		var y = Vector3.Transform(Vector3.UnitX, orientation).Y;

		if (species.ShootsGravitropicSetPointAngle > 0f)
			//A plagiotropic shoot holds a set-point angle away from the vertical instead
			//of reaching for it. Steering it to vertical whenever its axis dipped is what
			//made a trailing bellflower climb rather than sprawl.
			orientation = Quaternion.Slerp(orientation,
				GravitropicSetPointOrientation(orientation, species.ShootsGravitropicSetPointAngle),
				Math.Clamp(plant.RNG.NextPositiveFloat(species.EffectiveShootsGravitaxis), 0f, 1f));
		else if (y < 0)
			orientation = Quaternion.Slerp(orientation, PlantFormation2.AdjustUpBase(orientation, up: true), plant.RNG.NextPositiveFloat(-y));
		else //if (species.EffectiveShootsGravitaxis > 0)
			orientation = Quaternion.Slerp(orientation, PlantFormation2.AdjustUpBase(orientation, up: true), plant.RNG.NextPositiveFloat(species.EffectiveShootsGravitaxis));

		return orientation;
	}

	/// <summary>
	/// The orientation this shoot's gravitropism aims at: the same compass direction,
	/// inclined <paramref name="setPointAngle"/> away from the vertical.
	/// </summary>
	/// <remarks>
	/// Rotating the current axis onto the target and applying that rotation to the whole
	/// frame preserves the roll, so leaves keep the phyllotaxis they were built with.
	/// A shoot pointing straight up or straight down has no azimuth to preserve and
	/// borrows one from its own cross-section.
	/// </remarks>
	public static Quaternion GravitropicSetPointOrientation(Quaternion orientation, float setPointAngle)
	{
		var axis = Vector3.Transform(Vector3.UnitX, orientation);
		var horizontal = new Vector3(axis.X, 0f, axis.Z);
		if (horizontal.LengthSquared() < 1e-8f)
		{
			var side = Vector3.Transform(Vector3.UnitZ, orientation);
			horizontal = new Vector3(side.X, 0f, side.Z);
			if (horizontal.LengthSquared() < 1e-8f)
				horizontal = Vector3.UnitX;
		}
		horizontal = Vector3.Normalize(horizontal);

		var target = horizontal * MathF.Sin(setPointAngle) + Vector3.UnitY * MathF.Cos(setPointAngle);
		var cos = Math.Clamp(Vector3.Dot(axis, target), -1f, 1f);
		if (cos > 0.9999f)
			return orientation;

		var rotationAxis = Vector3.Cross(axis, target);
		if (rotationAxis.LengthSquared() < 1e-8f)
			return orientation;

		return Quaternion.Normalize(
			Quaternion.CreateFromAxisAngle(Vector3.Normalize(rotationAxis), MathF.Acos(cos)) * orientation);
	}

	/// <summary>
	/// The orientation a growing organ's negative gravitropism steers its rest frame toward,
	/// tick after tick: inclined <paramref name="setPointAngle"/> from the vertical in an azimuth
	/// that belongs to the organ.
	/// </summary>
	/// <remarks>
	/// <see cref="GravitropicSetPointOrientation"/> takes the azimuth from the organ's current
	/// lean, which is right for a one-off decision at birth and wrong for a restoring force: a
	/// lean whose azimuth is read back from itself is restored in tilt only, so anything that
	/// nudges the organ sideways - a neighbour's leaf, a pressed flower head - turns the lean
	/// around the vertical with nothing to turn it back, and a crowded Geranium peduncle
	/// precessed in circles at a constant 20 degrees. Here the azimuth is the organ's own
	/// across axis projected to the horizontal: fixed in the organ, carried with it when a
	/// parent moves it, and restored along with the tilt. The sign follows the side the organ
	/// already leans to, so an arch is never steered back through the vertical onto the other
	/// side; one pushed over to the far side settles there instead of circling.
	/// </remarks>
	public static Quaternion GravitropicRestoringOrientation(Quaternion orientation, float setPointAngle)
	{
		var axis = Vector3.Transform(Vector3.UnitX, orientation);
		Vector3 target;
		if (setPointAngle <= 0f) target = Vector3.UnitY;
		else
		{
			var across = Vector3.Transform(Vector3.UnitZ, orientation);
			var horizontal = new Vector3(across.X, 0f, across.Z);
			if (horizontal.LengthSquared() < 1e-8f)
			{
				var up = Vector3.Transform(Vector3.UnitY, orientation);
				horizontal = new Vector3(up.X, 0f, up.Z);
				if (horizontal.LengthSquared() < 1e-8f) horizontal = Vector3.UnitX;
			}
			horizontal = Vector3.Normalize(horizontal);
			var lean = new Vector3(axis.X, 0f, axis.Z);
			if (lean.LengthSquared() > 1e-4f && Vector3.Dot(lean, horizontal) < 0f) horizontal = -horizontal;
			target = horizontal * MathF.Sin(setPointAngle) + Vector3.UnitY * MathF.Cos(setPointAngle);
		}
		var cos = Math.Clamp(Vector3.Dot(axis, target), -1f, 1f);
		if (cos > 0.9999f) return orientation;
		var rotationAxis = Vector3.Cross(axis, target);
		if (rotationAxis.LengthSquared() < 1e-8f) return orientation;
		return Quaternion.Normalize(
			Quaternion.CreateFromAxisAngle(Vector3.Normalize(rotationAxis), MathF.Acos(cos)) * orientation);
	}

	public bool CompleteDay(uint timestep, byte ticksPerDay)
	{
		var complete = timestep - BirthTime >= ticksPerDay;

		if (complete || Organ != OrganTypes.Leaf) //the or avoids max value to wrongly stay until the next day for non-leaves
		{
			PreviousDayProductionInvariant = CurrentDayProductionInv;
			PreviousDayEnvResourcesInvariant = CurrentDayEnvResourcesInv;
			PreviousDayEnvResources = CurrentDayEnvResources;
		}

		CurrentDayProductionInv = 0f;
		CurrentDayEnvResourcesInv = 0f;
		CurrentDayEnvResources = 0f;
		return complete;
	}

	[M(AI)]public void Distribute(float water, float energy)
	{
		Energy = energy;
		Water_g = water;
	}

	[M(AI)]public void IncAuxins(float amount) => Auxins += amount;
	//public void IncCytokinins(float amount) => Cytokinins += amount;

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
		PreviousDayEnvResources = efficiency;
	}

	[M(AI)]public void DailyDiv(uint count)
	{
		PreviousDayEnvResourcesInvariant /= count;
		PreviousDayProductionInvariant /= count;
	}
	[M(AI)]
    public void SetOffset(Vector3 offset)
    {
        BaseOffset = offset;
    }
	[M(AI)] public void SetOrientation(Quaternion orientation)
	{
		Orientation = orientation;
	}

	/// <summary>
	/// Sets both the current orientation and the material rest state used by gravity.
	/// Use this for a developmental reorientation such as bud break, not elastic motion.
	/// </summary>
	[M(AI)] internal void SetStructuralOrientation(Quaternion orientation)
	{
		orientation = Quaternion.Normalize(orientation);
		Orientation = orientation;
		baseOrientation = orientation;
		restOrientation = orientation;
		targetOrientation = orientation;
	}

    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct OrientationSet : IMessage<AboveGroundAgent>
    {
        public readonly Quaternion Orientation;
        public OrientationSet(Quaternion orientation)
        {
            Orientation = orientation;

        }
        bool IMessage<AboveGroundAgent>.Valid => true;

        Transaction IMessage<AboveGroundAgent>.Type => Transaction.Unknown;

        void IMessage<AboveGroundAgent>.Receive(ref AboveGroundAgent agent, uint timestep)
        {
            agent.SetOrientation(Orientation);
        }
    }


    [StructLayout(LayoutKind.Auto)]
    [Message]
    public readonly struct PropagateSenescence : IMessage<AboveGroundAgent>
    {
        /// <summary>
		/// Senescence mut be completed before this timestep
		/// </summary>
		public readonly uint BeforeTimestep;
        public PropagateSenescence(uint beforeTimestep)
        {
            BeforeTimestep = beforeTimestep;
        }

        public bool Valid => true;
        public Transaction Type => Transaction.Unknown;
        [M(AI)]public void Receive(ref AboveGroundAgent dstAgent, uint timestep) => dstAgent.SenescenceStart = BeforeTimestep;
    }
}
