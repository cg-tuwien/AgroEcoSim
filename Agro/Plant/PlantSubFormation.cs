using AgentsSystem;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Agro;

public interface IPlantSubFormation<T> : IFormation where T: struct, IPlantAgent
{
	/// <summary>Writes the live agents for a checkpoint.</summary>
	void WriteState(BinaryWriter writer);
	/// <summary>Restores agents into a formation freshly built from the same request.</summary>
	void ReadState(BinaryReader reader);

	bool Alive { get; }
	Vector3 Size { get; }
	float DailyProductionMax { get; }
	void FirstDay();
	void NewDay(uint timestep, byte ticksPerDay);
	void Distribute(PlantGlobalStats stats);
	PlantGlobalStats Gather();
	bool SendProtected(int part, IMessage<T> msg) ;
}

public partial class PlantSubFormation<T> : IPlantSubFormation<T> where T: struct, IPlantAgent
{
	public delegate void ReindexAgents(Span<T> agents, ReadOnlySpan<int> indexMap);
	readonly ReindexAgents Reindex;
	public Vector3 Size => throw new NotImplementedException();

	public readonly PlantFormation2 Plant;
	// Backing capacity is independent of the live prefix exposed by Src/SrcDst.
	int AgentCount;
	bool ReadTMP = false;
	T[] Agents = Array.Empty<T>();
	T[] AgentsTMP = Array.Empty<T>();
	readonly PostBox<T> Post = new();
	readonly List<T> Births = [];
	readonly HashSet<int> Deaths = [];
	readonly HashSet<int> RemovedBirths = [];
	int[] IndexMap = [];
	ulong NextOrganId = 1;

	readonly TreeCacheData TreeCache = new();
	// Derived cache generations; deliberately excluded from live checkpoints.
	internal long TopologyRevision { get; private set; }
	internal long CollisionGeometryRevision { get; private set; }
	bool HeightDirty;

    public Bvh CollisionBvh { get; set; } = new();

    /// <summary>Rhizome organs only. A creeping apex asks whether its next segment would run into
    /// an established runner, and that is the only query that needs them.</summary>
    public Bvh RhizomeBvh { get; set; } = new();

    public PlantSubFormation(PlantFormation2 plant, ReindexAgents reindex, bool isAboveGround)
	{
		Plant = plant;
		Reindex = reindex;
		IsAboveGround = isAboveGround;
		CollisionEligibility = IsCollisionEligible;
		PrunableCrown = TreeCache.PrunableCrown;
		InitialCollisionFilter = InitialPairFilter;
		RedirectComparison = CompareRedirectCandidates;
	}

	internal float DailyResourceMax { get; private set; }
	public float DailyProductionMax { get; private set; }
	internal float DailyEfficiencyMax { get; private set; }
	internal float Height
	{
		get
		{
			if (HeightDirty) { TreeCache.UpdateHeight(this); HeightDirty = false; }
			return TreeCache.Height;
		}
	}
	readonly bool IsAboveGround;
	const float HeightEpsilon = 1e-6f;

	internal float GetRelativeHeight(int index)
	{
		if (!IsAboveGround || Height <= HeightEpsilon)
			return 0f;

		return Math.Clamp((GetBaseCenter(index).Y - Plant.Position.Y) / Height, 0f, 1f);
	}

	public bool CheckIndex(int index) => index < AgentCount;

	public bool Alive => AgentCount > 0 || Births.Count > 0;
	public int Count => AgentCount;

	/// <summary>
	/// Live views of the double buffer. Capacity must never become a simulated organ.
	/// </summary>
	void SrcDst(out Span<T> src, out Span<T> dst)
	{
		src = Src();
		dst = (ReadTMP ? Agents : AgentsTMP).AsSpan(0, AgentCount);
	}
	Span<T> Src() => (ReadTMP ? AgentsTMP : Agents).AsSpan(0, AgentCount);
	internal ref readonly T GetAgent(int index) => ref Src()[index];

	void EnsureAgentCapacity(int count)
	{
		if (Agents.Length >= count)
			return;
		var capacity = Math.Max(count, Math.Max(8, Agents.Length + Agents.Length / 2));
		Array.Resize(ref Agents, capacity);
		Array.Resize(ref AgentsTMP, capacity);
	}

	public int Birth(T agent)
	{
		if (agent.StableId == 0) agent.StableId = NextOrganId++;
		Births.Add(agent);
		return AgentCount + Births.Count - 1;
	}

	public List<int>? Death(int index, bool force = false, OrganDeathReason reason = OrganDeathReason.Senescence)
	{
		if (index < 0 || index >= AgentCount) return null;
		if (RecordingDevelopment) { DevelopmentDeaths.Add((index, reason)); return null; }
		// Rhizomes are the persistent framework of the clonal Geraniums.  They
		// may lose their aerial descendants during a resource shortage, but
		// deleting one branch here recursively deletes every later crown and
		// produces an implausible bare, collapsed plant.
		if (!force && IsAboveGround && Plant.Parameters.RetainDormantRhizomes && GetIsRizome(index))
			return null;

		List<int> result = null;
		if (Deaths.Add(index))
		{
			if (reason == OrganDeathReason.Resources) ++ResourceDeaths; else ++NaturalDeaths;
			var buffer = new Queue<int>();
			buffer.Enqueue(index);
			while (buffer.Count > 0)
			{
				var i = buffer.Dequeue();
				var children = GetChildren(i);
				if (children != null)
					foreach(var child in children)
					{
						if (Deaths.Add(child))
						{
							if (reason == OrganDeathReason.Resources) ++ResourceDeaths; else ++NaturalDeaths;
							result ??= [];
							result.Add(child);
							buffer.Enqueue(child);
						}
					}
			}
		}
		return result;
	}

	public bool SendProtected(int dst, IMessage<T> msg)
	{
		if (AgentCount > dst)
		{
			lock(Post) Post.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	//--- checkpointing ------------------------------------------------------------------------
	//
	//Only the AGENTS are written. Everything else this class holds is derived and is rebuilt on the
	//next tick from the agents themselves: the tree cache, both BVHs, the index map. Births, deaths
	//and the postbox are empty at a step boundary - Census drains the first two and DeliverPost the
	//last - which is the only moment a checkpoint is ever taken.
	//
	//The agents go out as raw bytes. T is an unmanaged struct, so its memory IS its state and a
	//field-by-field writer would be a second description of the same layout, free to drift from it
	//the day someone adds a field. A blit cannot drift; it can only fail to compile.

	/// <summary>Writes the live agents. See the note above on what is deliberately not written.</summary>
	public void WriteState(BinaryWriter writer)
	{
		writer.Write(NextOrganId);
		var live = Src();
		writer.Write(live.Length);
		writer.Write(MemoryMarshal.AsBytes(live));

		//Beyond the organs: what this formation remembers ACROSS steps rather than within one.
		//The daily maxima are recomputed by NewDay, but a checkpoint taken part-way through a day
		//lands before that, and they normalise every organ's share until it runs.
		writer.Write(DailyResourceMax);
		writer.Write(DailyProductionMax);
		writer.Write(DailyEfficiencyMax);

		//Flowering is a history, not a state: which season's window this refers to and how far
		//through it each level has got. Restarting it would let a plant flower twice in one year.
		writer.Write(FloweringCycle);
		writer.Write(FloweringStarted.Count);
		foreach (var started in FloweringStarted) writer.Write(started);

		//Queued births and deaths, which DO cross the step boundary. PlantFormation2.Census calls
		//AG.Death for emergency energy pruning AFTER AG.Census has run, so those deaths are applied
		//by the NEXT step's Census - a checkpoint that dropped them would quietly reprieve organs
		//the plant had already decided it could not feed.
		writer.Write(Deaths.Count);
		foreach (var death in Deaths) writer.Write(death);
		writer.Write(Births.Count);
		if (Births.Count > 0)
		{
			var born = CollectionsMarshal.AsSpan(Births);
			writer.Write(MemoryMarshal.AsBytes(born));
		}
		WriteGrowthState(writer);
        WriteDeformationState(writer);
	}

	/// <summary>
	/// Restores the agents into a formation that has just been built from the same request.
	///
	/// Reads into the primary array and leaves ReadTMP false, which is the state a freshly
	/// constructed formation is in - so a restored one is indistinguishable from one that has
	/// simply not ticked yet, rather than one caught mid-swap.
	/// </summary>
	public void ReadState(BinaryReader reader)
	{
		NextOrganId = reader.ReadUInt64();
		var count = reader.ReadInt32();
		if (count < 0) throw new InvalidDataException($"Negative agent count {count} in the checkpoint.");

		EnsureAgentCapacity(count);
		var bytes = reader.ReadBytes(count * Unsafe.SizeOf<T>());
		if (bytes.Length != count * Unsafe.SizeOf<T>())
			throw new EndOfStreamException($"The checkpoint ended inside an agent array of {count} agents.");
		bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(Agents.AsSpan(0, count)));

		AgentCount = count;
		ReadTMP = false;
		//RemovedBirths is scratch that Census clears before every use; the other two are not.
		RemovedBirths.Clear();

		//And rebuild what the agents imply. The tree cache is derived, but it is NOT rebuilt on the
		//next tick - Census only touches it when something was born or died, and the first tick
		//after a restore asks it for an organ's children before that can happen. The same four
		//calls Census makes, in the same order, because this is the same situation: a complete set
		//of agents whose parent links have never been walked.
		InvalidateStaticClearance();
		var live = Agents.AsSpan(0, count);
		TreeCache.Clear(count);
		for (int i = 0; i < count; ++i)
			TreeCache.AddChild(live[i].Parent, i);
		TreeCache.FinishUpdate(this);
		TreeCache.UpdateBases(this);
		HeightDirty = false;
		++TopologyRevision;
		++CollisionGeometryRevision;
		InvalidateLaminaCache();

		DailyResourceMax = reader.ReadSingle();
		DailyProductionMax = reader.ReadSingle();
		DailyEfficiencyMax = reader.ReadSingle();

		FloweringCycle = reader.ReadInt32();
		var flowering = reader.ReadInt32();
		FloweringStarted.Clear();
		for (int i = 0; i < flowering; ++i) FloweringStarted.Add(reader.ReadUInt32());

		Deaths.Clear();
		var deaths = reader.ReadInt32();
		for (int i = 0; i < deaths; ++i) Deaths.Add(reader.ReadInt32());

		Births.Clear();
		var births = reader.ReadInt32();
		if (births > 0)
		{
			var bornBytes = reader.ReadBytes(births * Unsafe.SizeOf<T>());
			if (bornBytes.Length != births * Unsafe.SizeOf<T>())
				throw new EndOfStreamException("The checkpoint ended inside the queued births.");
			for (int i = 0; i < births; ++i) Births.Add(default);
			bornBytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(Births)));
		}

		ReadGrowthState(reader);
        ReadDeformationState(reader);
        TreeCache.UpdateBases(this);
		if (IsAboveGround)
			BuildBvh();
	}

	public void Census()
	{
		if (Births.Count > 0 || Deaths.Count > 0)
		{
			var oldCount = AgentCount;
			var geometryReindexed = Deaths.Count > 0;
			RemovedBirths.Clear();
			if (Deaths.Count > 0 && Births.Count > 0)
			{
				// A queued birth can itself parent another queued birth. Keep removing
				// descendants until the set is closed, including forward references.
				bool anyRemoved;
				do
				{
					anyRemoved = false;
					for (int i = Births.Count - 1; i >= 0; --i)
					{
						var index = oldCount + i;
						var parent = Births[i].Parent;
						if (!RemovedBirths.Contains(index) &&
							(Deaths.Contains(parent) || RemovedBirths.Contains(parent)))
							anyRemoved |= RemovedBirths.Add(index);
					}
				}
				while (anyRemoved);
			}

			var newCount = oldCount + Births.Count - Deaths.Count - RemovedBirths.Count;
			Debug.Assert(newCount >= 0);
			EnsureAgentCapacity(newCount);
			var src = Src();
			var dst = (ReadTMP ? Agents : AgentsTMP).AsSpan(0, newCount);
			if (Deaths.Count > 0)
			{
				var mapCount = oldCount + Births.Count;
				if (IndexMap.Length < mapCount)
					Array.Resize(ref IndexMap, Math.Max(mapCount, Math.Max(8, IndexMap.Length + IndexMap.Length / 2)));
				var map = IndexMap.AsSpan(0, mapCount);
				map.Fill(-1);
				var target = 0;
				for (int i = 0; i < oldCount; ++i)
					if (!Deaths.Contains(i))
					{
						map[i] = target;
						dst[target++] = src[i];
					}
				for (int i = 0; i < Births.Count; ++i)
					if (!RemovedBirths.Contains(oldCount + i))
					{
						map[oldCount + i] = target;
						dst[target++] = Births[i];
					}
				Reindex(dst, map);
				Deaths.Clear();
			}
			else
			{
				src.CopyTo(dst);
				CollectionsMarshal.AsSpan(Births).CopyTo(dst[oldCount..]);
			}

			// Census historically reset the previous-state buffer to zero. Gather
			// reads it, so retaining the old values here would change resource deltas.
			(ReadTMP ? AgentsTMP : Agents).AsSpan(0, Math.Max(oldCount, newCount)).Clear();
			ReadTMP = !ReadTMP;
			AgentCount = newCount;
			ReindexGeometry(oldCount, newCount, geometryReindexed);
			if (newCount < oldCount)
				(ReadTMP ? AgentsTMP : Agents).AsSpan(newCount, oldCount - newCount).Clear();

			InvalidateStaticClearance();
			TreeCache.Clear(newCount);
			for (int i = 0; i < newCount; ++i)
				TreeCache.AddChild(dst[i].Parent, i);
			TreeCache.FinishUpdate(this);
			TreeCache.UpdateBases(this);
			++TopologyRevision;
			InvalidateLaminaCache();
			RefreshOrganIndices();
			Births.Clear();
		}
		else
			TreeCache.UpdateBases(this);

		HeightDirty = false;
		if (IsAboveGround)
			BuildBvh();
		// The underground collision tree has no consumer at present. Reactivate
		// this call when root collision queries are enabled again.
		// else BuildBvh();
	}

	public void Tick(uint timestep)
	{
		CaptureGeometry();
		SrcDst(out var src, out var dst);
		src.CopyTo(dst);
        PrepareGrowthAttempts(dst);
		FloweringPhaseCached = false;
		PhaseCacheTimestep = timestep;
		CacheFloweringPhase = true;
		try
		{
			for(int i = 0; i < dst.Length; ++i)
				TickWithDevelopment(ref dst[i], i, timestep);
		}
		finally
		{
			CacheFloweringPhase = false;
		}
		ReadTMP = !ReadTMP;
	}


    readonly List<GatherDataBase> Gathering = [];
	public readonly PlantGlobalStats Gathered = new();

	public PlantGlobalStats Gather()
	{
		var world = Plant.World;
		var species = Plant.Parameters;
		var energy = 0.0;
		var water = 0.0;
		var energyDiff = 0.0;
		var waterDiff = 0.0;
		var energyCapacity = 0.0;
		var waterCapacity = 0.0;
		var energyRequirement = 0.0;
		var waterRequirement = 0.0;
		var waterPriority = 0.0;
		SrcDst(out var dst, out var src); //since Tick already swapped them
		var prevLength = Gathering.Count;

		if (prevLength > dst.Length)
			Gathering.RemoveRange(dst.Length, Gathering.Count - dst.Length);

		for(int i = prevLength; i < dst.Length; ++i)
			Gathering.Add(default);

		for(int i = 0; i < dst.Length; ++i)
		{
			//Debug.WriteLine($"{(IsAboveGround ? "⊤" : "⊥")}-{i} Energy {dst[i].Energy} Water {dst[i].Water} Res {dst[i].PreviousDayEnvResourcesInv} / {DailyResourceMax} Prod {dst[i].PreviousDayProductionInv} / {DailyProductionMax} Life {dst[i].LifeSupportPerTick(world)} wStore {dst[i].WaterStorageCapacity()} wStore {dst[i].EnergyStorageCapacity()}");
			energy += Math.Max(0f, dst[i].Energy);
			water += dst[i].Water_g;

			var previousEnergy = src[i].Energy;
			energyDiff -= previousEnergy;
			waterDiff -= src[i].Water_g;

			var lifeSupport = dst[i].LifeSupportPerTick(world, species);
			energyRequirement += lifeSupport;

			float photosynthSupport;
			if (IsAboveGround)
			{
				photosynthSupport = dst[i].PhotosynthPerTick(world, species);
				waterRequirement += photosynthSupport;
			}
			else
				photosynthSupport = 0;

			var energyStorageCapacity = dst[i].EnergyAllocationWeight(species);
			energyCapacity += energyStorageCapacity;

			var waterStorageCapacity = dst[i].WaterStorageCapacity_g();
			waterCapacity += waterStorageCapacity;

            var resEff = DailyResourceMax > 0f ? dst[i].PreviousDayEnvResourcesInvariant / DailyResourceMax : 1f;
            var prodEff = DailyProductionMax > 0f ? dst[i].PreviousDayProductionInvariant / DailyProductionMax : 1f;

            if (IsAboveGround)
            {
                // New tissue is a carbon sink even before it receives direct light.
                // Relative irradiance still governs production, while the plant's
                // absolute pool funds construction and remains conserved.
                if (ResourceUnits.Enabled(species) && energyStorageCapacity > dst[i].EnergyStorageCapacity(species))
                    resEff = 1f;
                var parent = dst[i].Parent;
                var isRhizomeSupportedCrownBase = !dst[i].IsRizome &&
                    parent >= 0 && parent < dst.Length && dst[parent].IsRizome;
                var isFlowerOrgan = dst[i].Organ is OrganTypes.FlowerStem or OrganTypes.FlowerPadel or
                    OrganTypes.FlowerPedicel or OrganTypes.FlowerMeristem or OrganTypes.FlowerBud;
                if (dst[i].IsRizome || isRhizomeSupportedCrownBase || isFlowerOrgan)
                {
                    // Rhizomes translocate stored carbohydrate to attached renewal buds and
                    // establishing crown bases. Allocation cannot depend on downstream leaves
                    // because physiological transport through the axis is bidirectional.
                    resEff = 1f;
                }
            }
            Gathering[i] = new(lifeSupport, photosynthSupport, energyStorageCapacity, waterStorageCapacity, resEff,prodEff);
			waterPriority += PlantGlobalStats.WaterPriority(Gathering[i]);
		}

		//this is faster than probing .Contains() for each i
		foreach(var i in Deaths)
		{
			waterPriority -= PlantGlobalStats.WaterPriority(Gathering[i]);
			Gathering[i] = default;
			energyRequirement -= dst[i].LifeSupportPerTick(world, species);
			waterRequirement -= dst[i].PhotosynthPerTick(world, species);
			energyCapacity -= dst[i].EnergyAllocationWeight(species);
			waterCapacity -= dst[i].WaterStorageCapacity_g();
		}

		energyDiff += energy; //optimal variant of sum(dst[i] - src[i])
		waterDiff += water;

		//return new PlantGlobalStats() {
			Gathered.Energy = energy;
			Gathered.Water = water;
			Gathered.EnergyDiff = energyDiff;
			Gathered.WaterDiff = waterDiff;
			Gathered.EnergyCapacity = energyCapacity;
			Gathered.WaterCapacity = waterCapacity;
			Gathered.EnergyRequirementPerTick = energyRequirement;
			Gathered.WaterRequirementPerTick = waterRequirement;
			Gathered.WaterPriorityPerTick = waterPriority;
			Gathered.Gathering = Gathering;
		//};

		return Gathered;
	}

	//work in progress
	List<float> Weights = [];
	List<int> WeightReoslveChildren = [];
	/// <summary>Weight-times-position of each organ's subtree, so a node knows where the load it
	/// carries actually acts. Dividing by the subtree weight gives its centre of mass.</summary>
	readonly List<Vector3> WeightedCentroids = [];

	//Per-formation counters, summed by a probe. Instance fields rather than shared atomics so the
	//parallel tick does not contend on them.
	/// <summary>Stopwatch ticks spent in the collision pass.</summary>
	public long CollisionTicks;
	/// <summary>Stopwatch ticks spent in gravity, for phase-level performance probes.</summary>
	public long GravityTicks;
	/// <summary>Candidate pairs the broad phase produced.</summary>
	public long CollisionCandidatePairs;
	/// <summary>Pairs that survived the narrow phase and were actually resolved.</summary>
	public long CollisionContacts;
	/// <summary>Sub-phase timings, so the pass can be attributed rather than guessed at.</summary>
	public long TicksEligibility, TicksBroad, TicksNarrow, TicksRebase;

	/// <summary>Bodies in the collision hierarchy after the last census, and how many petals were
	/// folded into their receptacle instead of getting one each.</summary>
	public int CollisionBodies, AggregatedPetals;

	//One plant ticks on one thread, so a single scratch buffer is enough for the growth query
	//a meristem issues against its own collision tree every step.
	internal readonly List<int> GrowthOverlapBuffer = [];

	//Gravity runs for every plant on every step; these buffers keep it allocation free.
	readonly List<int> WeightFrontA = [], WeightFrontB = [];
	readonly Queue<int> GravityQueue = new();

	/// <summary>The floral axes whose stiffness follows the flower senescence curve.
	/// Deliberately excludes FlowerBaseBud, so this is not the whole FlowerBud..FlowerBaseBud range.</summary>
	static bool IsFloralAxis(OrganTypes organ) => organ is OrganTypes.FlowerStem or OrganTypes.FlowerPadel
		or OrganTypes.FlowerPedicel or OrganTypes.FlowerMeristem or OrganTypes.FlowerBud;

	/// <summary>
	/// Effective keel thickness for one blade segment, tapering from base to tip.
	/// </summary>
	/// <remarks>
	/// The position is found by counting leaf ancestors, which is at most
	/// <see cref="SpeciesSettings.MaximumLeafSegments"/> steps and needs no extra per-agent state.
	/// An unsegmented blade is all base, and keeps the full thickness.
	/// </remarks>
	float BladeKeelAt(int index)
	{
		var keel = Plant.Parameters.LeafKeelThickness;
		var tipFraction = Plant.Parameters.LeafTipStiffnessFraction;
		var segments = Plant.Parameters.LeafSegmentCount;
		if (segments <= 1 || tipFraction >= 1f)
			return keel;

		var position = 0;
		for (var p = GetParent(index); p >= 0 && GetOrgan(p) == OrganTypes.Leaf; p = GetParent(p))
			if (++position >= segments)
				break;

		var t = position / (float)(segments - 1);
		return keel * (1f - t * (1f - tipFraction));
	}

	/// <summary>Second moment of area of a blade about its MINOR axis - the width axis, which is
	/// what a leaf droops about: a rectangle of the blade's full width and an effective thickness
	/// representing its keel.</summary>
	static float BladeInertia(float halfWidth, float keelThickness)
	{
		var width = 2f * MathF.Max(halfWidth, 1e-6f);
		var thickness = MathF.Max(keelThickness, AboveGroundAgent.LeafThickness);
		return width * thickness * thickness * thickness / 12f;
	}

	/// <summary>Second moment of area of a blade about its MAJOR axis - the blade normal, which is
	/// what it would have to bend about to curve sideways in its own plane.</summary>
	/// <remarks>
	/// A lamina is not a rod, and using one figure for both directions is what let a Carex blade
	/// fold sideways as readily as it droops. The two swap width and thickness, so the ratio is
	/// (w/t)^2 - for a 6 mm blade on a 2.8 mm effective keel that is only 4.6, but for the 0.1 mm
	/// a real strip is thick it is over three thousand.
	/// </remarks>
	static float BladeInertiaMajor(float halfWidth, float keelThickness)
	{
		var width = 2f * MathF.Max(halfWidth, 1e-6f);
		var thickness = MathF.Max(keelThickness, AboveGroundAgent.LeafThickness);
		return thickness * width * width * width / 12f;
	}

    internal void RefreshMechanicalLoads()
    {
        var dst = Src();
        var count = dst.Length;
        if (Weights.Count < count)
            Weights.AddRange(new float[count - Weights.Count]);
        if (WeightReoslveChildren.Count < count)
            WeightReoslveChildren.AddRange(new int[count - WeightReoslveChildren.Count]);
        if (WeightedCentroids.Count < count)
            WeightedCentroids.AddRange(new Vector3[count - WeightedCentroids.Count]);

        //initialize weights with self weight converted to Newtons
        for (int i = 0; i < count; ++i)
        {
            var weight = GetWeight(i, Plant.Parameters) * 0.001f * 9.81f;
            Weights[i] = weight;
            //Where that weight acts: the midpoint of the organ. Accumulated up the tree alongside
            //the weight, this gives every node the centre of mass of everything it carries, which
            //is the only thing that can supply a correct lever arm.
            var selfDir = Vector3.Transform(Vector3.UnitX, dst[i].Orientation);
            WeightedCentroids[i] = weight * (GetBaseCenter(i) + (GetDeformation(i) != Vector4.Zero && dst[i].Organ == OrganTypes.Petiole
                ? Vector3.Transform(ContactDeformation.RodPoint(.5f,dst[i].Length,GetDeformation(i)),dst[i].Orientation)
                : selfDir * (dst[i].Length * 0.5f)));
            WeightReoslveChildren[i] = GetChildren(i).Count;
        }
        //accumulate weights from leaves up
        var leavesToProcess = WeightFrontA;
        var internalNodesToProcess = WeightFrontB;
        leavesToProcess.Clear();
        leavesToProcess.AddRange(GetLeaves());
        internalNodesToProcess.Clear();
        while (leavesToProcess.Count > 0)
        {
            internalNodesToProcess.Clear();
            foreach (var idx in leavesToProcess)
            {
                var parent = GetParent(idx);
                if (parent >= 0)
                {
                    // A point contact cannot remove the whole descendant load. Keep it in
                    // the moment sum; geometric reactions are applied by the contact solve.
                    Weights[parent] += Weights[idx];
                    WeightedCentroids[parent] += WeightedCentroids[idx];
                    if (--WeightReoslveChildren[parent] == 0)
                        internalNodesToProcess.Add(parent);
                }
            }
            (leavesToProcess, internalNodesToProcess) = (internalNodesToProcess, leavesToProcess);
        }
    }

	internal void Gravity()
	{
		if (!IsAboveGround)
            return;
		var started = Stopwatch.GetTimestamp();
		try
		{

        var dst = Src(); //since Tick already swapped them
        var count = dst.Length;
        RefreshAnchors(Plant.World.Collisions.StaticScene);
        if (count == 0)
            return;
        // The certified engine rebuilds every elastic fold from the unloaded shape each tick
        // and relies on its correction rounds to find it again. The soft engine applies one
        // capped increment per tick, so its folds must persist and relax like contact turns.
        var persistentDeformation = HasContactDeformation && Plant.World.ContactEngine == ContactEngineKind.Soft;
        if (!persistentDeformation) ResetElasticDeformation();
        RefreshMechanicalLoads();

		// Process nodes from roots to leaves, propagating rotations.  The response
		// is specified per hour so changing the simulation timestep does not alter
		// how quickly an organ approaches its elastic equilibrium.
		float tickSpeed = MathF.Max(0f, Plant.World.HoursPerTick);
		const float responsePerHour = 0.05f;
		float response = HasContactDeformation && !persistentDeformation ? 1f : 1f - MathF.Pow(1f - responsePerHour, tickSpeed);
		//Bending used to be clamped here so no tip could drop below the flat plane
		//Plant.Position.Y - which is the SEED, a few centimetres under the surface. It kept a long
		//chain of segments from pointing straight down and burying its own leaves, and it also made
		//it impossible for anything to hang below its own trough, so a Geranium could not cascade
		//over a trough edge. The hard soil pass in Agro/Collisions/CollisionWorld.cs replaces it
		//with containment in the actual soil hull, which forbids the first without forbidding the
		//second.
        var nodesToVisit = GravityQueue;
        nodesToVisit.Clear();
        foreach (var root in GetRoots())
            nodesToVisit.Enqueue(root);
		while (nodesToVisit.Count > 0)
		{


			var i = nodesToVisit.Dequeue();
			ref var agent = ref dst[i];
            agent.LoadLimited = false;
			if ((GetIsRizome(i) && agent.SoilAnchored) || GetOrgan(i).Equals(OrganTypes.FlowerPadel))
			{
				foreach (var child in GetChildren(i))
					nodesToVisit.Enqueue(child);

				continue;
			}
			var length = agent.Length;
			var radius = agent.Radius;
			var totalWeight = Weights[i];



			float woodiness = agent.WoodRatio();
			var basalAttenuation = MathF.Exp(-GetAbsDepth(i) *
				Plant.Parameters.BasalStiffnessAttenuationPerSegment);
			var greenElasticity = MathF.Max(Plant.Parameters.GreenElasticModulus,
				Plant.Parameters.BasalGreenElasticModulus * basalAttenuation);
			float elasticity = greenElasticity * (1 - woodiness) +
				Plant.Parameters.WoodElasticModulus * woodiness;

			if (agent.Organ.Equals(OrganTypes.Petiole))
			{
				elasticity = Plant.Parameters.PetioleElasticModulus;
			}
			if (agent.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel)
			{
				//A blade's stiffness belongs to the blade, not to how deep it sits in the plant. The
				//basal ramp above is keyed on topological depth to stiffen the base of a shoot, and
				//because it is applied with a max() it was overriding the species' own figure for
				//every leaf.
				elasticity = Plant.Parameters.LeafElasticModulus > 0f
					? Plant.Parameters.LeafElasticModulus
					: Plant.Parameters.GreenElasticModulus;
			}
			if (IsFloralAxis(agent.Organ))
			{
				// Supporting floral axes dry and lignify during senescence; they do
				// not lose essentially all bending stiffness. Interpolate toward the
				// species' mature structural-tissue modulus instead of toward zero.
				//The axis, the pedicel and the flower itself are three different tissues. A
				//Bergenia scape has to stay rigid and a Geranium peduncle has to bend, and one
				//shared modulus cannot express both.
				var scapeElasticity = Plant.Parameters.FlowerScapeElasticModulus > 0f
					? Plant.Parameters.FlowerScapeElasticModulus
					: Plant.Parameters.FlowerOrganElasticModulus;
				var greenFlowerElasticity = agent.Organ switch
				{
					OrganTypes.FlowerPedicel => Plant.Parameters.FlowerPedicelElasticModulus,
					OrganTypes.FlowerStem or OrganTypes.FlowerMeristem => scapeElasticity,
					_ => Plant.Parameters.FlowerOrganElasticModulus,
				};
				elasticity = greenFlowerElasticity * (1f - woodiness) +
					Plant.Parameters.WoodElasticModulus * woodiness;
			}
			Quaternion rotation = Quaternion.Identity;
			Quaternion appliedDelta = Quaternion.Identity;

			// Surface reactions are applied by the contact solve; all loads remain in the moment sum.

			if (length > 1e-4f && radius > 0f && totalWeight > 0f)
			{
				var baseDir = Vector3.Transform(Vector3.UnitX, agent.baseOrientation);
				var isBlade = agent.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel;
				//A petiole is the end-loaded cantilever the old scalar law gets most wrong: it holds a
				//whole blade out at its tip, and putting that load at the petiole's own midpoint
				//understates the moment by roughly the ratio of the blade's reach to half the
				//petiole - so a long one (Heuchera's, Geranium macrorrhizum's 18 cm) came out rigid
				//when it should visibly arch.
				var isPetiole = agent.Organ is OrganTypes.Petiole;

				//These get the real thing: the torque of the load they carry about their own base,
				//resolved in their own frame. Everything below is the old scalar law, kept for the
				//structural axes - see the remark on the two formulations further down.
				if ((isBlade || isPetiole || agent.IsRizome || IsFloralAxis(agent.Organ)) && Collisions.CollisionSettings.VectorBending)
				{
					//tau = r x F with F = (0, -W, 0), so |tau| = W * |horizontal offset to the load|
					//and the axis falls out of the same cross product. This replaces BOTH the
					//length*0.5 lever and the transverseLoadFactor heuristic: the old form put the
					//WHOLE subtree's weight at the midpoint of the current segment, which is right
					//only for the tip. For segment k of n the true arm is the distance to the centre
					//of mass of everything beyond it, so the base of an 8-segment blade was short by
					//a factor of 8 and the error grew with the segment count - the arch got weaker
					//the finer the blade was divided, which is the opposite of converging.
					var centroid = WeightedCentroids[i] / totalWeight;
					var r = centroid - GetBaseCenter(i);
					var torque = new Vector3(r.Z, 0f, -r.X) * totalWeight;

					//Resolved in the organ's own frame: X along it, and for a blade Y is its normal
					//and Z runs across the width.
					var ex = Vector3.Transform(Vector3.UnitX, agent.baseOrientation);
					var ey = Vector3.Transform(Vector3.UnitY, agent.baseOrientation);
					var ez = Vector3.Transform(Vector3.UnitZ, agent.baseOrientation);
					Vector3 kappa;
					if (isBlade)
					{
						var keel = BladeKeelAt(i);
						//Drooping is rotation about the WIDTH axis and is easy; curving sideways is
						//rotation about the blade normal and is (w/t)^2 harder. One isotropic figure
						//for both let a blade fold sideways as readily as it drooped. The component
						//about the blade's own long axis is torsion, which this model has no degree of
						//freedom for and which gravity cannot produce in a symmetric blade anyway - it
						//is dropped rather than silently bent, because feeding it back as bending is
						//what crumpled the lamina.
						kappa = ez * (Vector3.Dot(torque, ez) / (elasticity * BladeInertia(radius, keel)))
							+ ey * (Vector3.Dot(torque, ey) / (elasticity * BladeInertiaMajor(radius, keel)));
					}
					else
					{
						//A petiole is a rod: the same second moment about every transverse axis, so
						//only the axial component has to come out - that is torsion, and there is no
						//degree of freedom for it here either.
						var rodInertia = MathF.PI * MathF.Pow(radius, 4) / 4f;
						kappa = (torque - ex * Vector3.Dot(torque, ex)) / (elasticity * rodInertia);
					}

					var deltaThetaBlade = kappa.Length() * length / (Deformable(i) ? 3f : 1f);
                    if (agent.IsRizome)
                        agent.LoadLimited = !float.IsFinite(deltaThetaBlade) || deltaThetaBlade > Collisions.CollisionSettings.MaxElasticBendRadians;
					if (deltaThetaBlade > 1e-7f && float.IsFinite(deltaThetaBlade))
					{
						var bladeAxis = kappa / kappa.Length();
						//The equilibrium offset from the rest pose, not a per-step increment - the
						//slerp below does the rate limiting. Capped well short of the old bound,
						//which was "anything up to straight down": that turned a saturated segment's
						//TARGET into vertical, so an over-driven blade did not arc, it folded.
						var maxBlade = Collisions.CollisionSettings.MaxElasticBendRadians;
						var angleFromDown = MathF.Acos(Math.Clamp(Vector3.Dot(baseDir, -Vector3.UnitY), -1f, 1f));
						var safeBlade = Math.Clamp(deltaThetaBlade, 0f, MathF.Min(maxBlade, angleFromDown));
						if (safeBlade > 1e-7f)
						{
							rotation = Quaternion.CreateFromAxisAngle(bladeAxis, safeBlade);
							agent.targetOrientation = Quaternion.Normalize(rotation * agent.baseOrientation);
							var bladeOr = Quaternion.Slerp(agent.Orientation, agent.targetOrientation, response);
							appliedDelta = bladeOr * Quaternion.Inverse(agent.Orientation);
							agent.SetOrientation(bladeOr);
						}
					}
					else
					{
						// A released joint must relax even when its remaining load is axial or zero.
						agent.targetOrientation = agent.baseOrientation;
						var relaxed = Quaternion.Slerp(agent.Orientation, agent.baseOrientation, response);
						appliedDelta = relaxed * Quaternion.Inverse(agent.Orientation);
						agent.SetOrientation(relaxed);
					}

					PropagateToSubtree(dst, i, appliedDelta);
					foreach (var child in GetChildren(i))
						nodesToVisit.Enqueue(child);
					continue;
				}

				// Only the component perpendicular to the organ bends a cantilever.
				// The previous implementation applied maximum bending even to a
				// vertical stem, where gravity is an axial load.
				//
				//This is the OLD scalar law and it has the same lever-arm defect the lamina branch
				//above describes: the subtree's whole weight is applied at this organ's own
				//midpoint. It is left in place for rods deliberately. For an organ whose subtree is
				//itself the two agree on the MAGNITUDE of the moment - which is why the lamina
				//change is about the arch of a segmented blade rather than about how hard a single
				//leaf droops - but for a stem carrying a whole shoot they differ by a large factor,
				//and correcting that would rescale the bending of every species at once against
				//elastic moduli calibrated to this form. That is a retune, not a fix, and it wants
				//its own pass.
				var transverseLoadFactor = MathF.Sqrt(MathF.Max(0f, 1f - baseDir.Y * baseDir.Y));
				var bendingMoment = totalWeight * transverseLoadFactor * length * 0.5f;
				//Second moment of area. A stem is a rod; a blade is not, and treating one as the
				//other is not a small error: the circular form with the blade's HALF WIDTH as the
				//radius gives a Carex leaf 6.4e-11 m^4, where a rectangle of its real 0.1 mm
				//thickness gives 5e-16 - a factor of 128,000, which is why a 30 cm blade never bends.
				//
				//The rectangle is not taken at face value either. A real sedge leaf is keeled, and a
				//flat strip 0.1 mm thick would simply collapse; the keel is what holds it up, so the
				//blade bends as a rectangle of an EFFECTIVE thickness that stands in for that
				//structure. See SpeciesSettings.LeafKeelThickness.
				var inertia = agent.Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel
					? BladeInertia(radius, BladeKeelAt(i))
					: MathF.PI * MathF.Pow(radius, 4) / 4f;
				var curvature = bendingMoment / (elasticity * inertia);
				var deltaTheta = curvature * length;

				float angleToDown = MathF.Acos(Math.Clamp(Vector3.Dot(baseDir, Vector3.UnitY), -1f, 1f));
				var axis = Vector3.Cross(baseDir, Vector3.UnitY);
                if (axis.LengthSquared() < 1e-8f)
                    axis = Vector3.Cross(baseDir, Vector3.UnitZ);

				if (axis.LengthSquared() < 1e-8f)
					axis = Vector3.Cross(baseDir, Vector3.UnitX);
				float maxAllowedBend = MathF.Min(Collisions.CollisionSettings.MaxElasticBendRadians, MathF.PI - angleToDown);
				float safeDelta = Math.Clamp(deltaTheta, 0f, maxAllowedBend);

				var len = axis.Length();
				if (len > 1e-4f)
				{
					axis /= len;
					rotation = Quaternion.CreateFromAxisAngle(axis, -safeDelta);
					agent.targetOrientation = Quaternion.Normalize(rotation * agent.baseOrientation);
					var newOr = Quaternion.Slerp(agent.Orientation, agent.targetOrientation, response);

					appliedDelta = newOr * Quaternion.Inverse(agent.Orientation);

					agent.SetOrientation(newOr);
                }

                PropagateToSubtree(dst, i, appliedDelta);
            }
            else if (HasContactDeformation && agent.Orientation != agent.baseOrientation)
            {
                // A tiny or unloaded joint has no gravitational moment, but must
                // still release a previous elastic contact turn. Keep its unloaded
                // frame exactly, without repeatedly normalizing an unchanged frame.
                var released=persistentDeformation
                    ? Quaternion.Normalize(Quaternion.Slerp(agent.Orientation,agent.baseOrientation,response))
                    : agent.baseOrientation;
                appliedDelta=Quaternion.Normalize(released*Quaternion.Conjugate(agent.Orientation));
                agent.targetOrientation=agent.baseOrientation;
                agent.SetOrientation(released);
                PropagateToSubtree(dst,i,appliedDelta);
            }
			foreach (var child in GetChildren(i))
			{
                nodesToVisit.Enqueue(child);
            }
        }
        GravityDeformation(persistentDeformation ? response : 1f);
		TreeCache.UpdateBases(this);
		HeightDirty = false;
		refitAllBounds();
        //Foliage against foliage within one plant. Obstacles and soil are hard constraints and are
        //handled by the world collision phase regardless of this switch.
        // Dynamic contacts are resolved with static constraints by CollisionWorld, after Census.
		}
		finally { GravityTicks += Stopwatch.GetTimestamp() - started; }
    }
    /// <summary>
    /// Carries a rotation of one organ rigidly through everything beyond it.
    /// </summary>
    /// <remarks>
    /// Orientations here are absolute, so turning an organ does not move its subtree - and that
    /// applies to the rest poses too, not only the current ones. All three are transported by the
    /// same delta, which is what keeps the subtree's RELATIVE rest configuration unchanged; a
    /// transport is not a deformation.
    /// </remarks>
    void PropagateToSubtree(Span<T> dst, int index, Quaternion appliedDelta)
    {
        var children = CollisionChildren;
        children.Clear();
        children.Enqueue(index);
        while (children.Count > 0)
        {
            var node = children.Dequeue();
            foreach (var child in GetChildren(node))
            {
                children.Enqueue(child);
                ref var c = ref dst[child];
                c.SetOrientation(Quaternion.Normalize(appliedDelta * c.Orientation));
                c.baseOrientation = Quaternion.Normalize(appliedDelta * c.baseOrientation);
                c.restOrientation = Quaternion.Normalize(appliedDelta * c.restOrientation);
                c.targetOrientation = Quaternion.Normalize(appliedDelta * c.targetOrientation);
            }
        }
    }

    static bool IsNearlyIdentity(Quaternion q)
    {
        // identity and -identity are the same rotation; use dot as a robust check
        float d = MathF.Abs(Quaternion.Dot(Quaternion.Normalize(q), Quaternion.Identity));
        return d > 0.999999f;
    }
    readonly Func<int, int> PrunableCrown;
    readonly Func<int, bool> CollisionEligibility;
    readonly Func<int, int, bool> InitialCollisionFilter;
    readonly List<(int first, int second)> OverlappingPairs = [];
    readonly Queue<int> CollisionChildren = new();
    readonly List<(int agentIndex, Bvh.BoundingBox bounds)> CollisionLeaves = [];
    readonly List<(int agentIndex, Bvh.BoundingBox bounds)> RhizomeLeaves = [];

    /// <summary>The soft-pass mask, for the cross-plant pass which lives outside this class.</summary>
    internal bool IsCollisionEligiblePublic(int index) => IsCollisionEligible(index);

    bool IsCollisionEligible(int index) =>
        GetOrgan(index) != OrganTypes.Seed
        && (GetLength(index) > 0f || GetBaseRadius(index) > 0f);

    /// <summary>
    /// Accepts a broad-phase pair for the narrow phase.
    /// </summary>
    /// <remarks>
    /// This used to claim each pair in a hash set first, so that a pair re-offered by the
    /// incremental re-query could not bypass the crown rejection recorded for its first
    /// occurrence. That re-query is gone, and <see cref="Bvh.GetOverlappingPairs"/> emits every
    /// leaf pair exactly once - each belongs to exactly one lowest common ancestor - so the claim
    /// could never fail and the hashing was pure overhead on the hottest path in the pass.
    /// The pair arrives already ordered, which keeps the directional crown test deterministic.
    /// </remarks>
    bool InitialPairFilter(int a, int b) =>
        GetParent(a) != b && GetParent(b) != a && GetParent(a) != GetParent(b) && !shareCrown(a, b);


    internal void collisionHandling()
    {
        var dst = Src();
        if (dst.Length == 0 || CollisionBvh.IsEmpty)
            return;

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        var budget = Collisions.CollisionSettings.ContactsPerPlantPerRound;
        for (int round = 0; round < Collisions.CollisionSettings.SoftRounds && budget > 0; ++round)
        {
        // Tick can change an organ's type or grow it past the length threshold
        // after the previous census built the hierarchy.
        var tEligibility = System.Diagnostics.Stopwatch.GetTimestamp();
        CollisionBvh.RefreshCollisionEligibility(CollisionEligibility);
        var tBroad = System.Diagnostics.Stopwatch.GetTimestamp();
        TicksEligibility += tBroad - tEligibility;
        OverlappingPairs.Clear();
        CollisionBvh.GetOverlappingPairs(OverlappingPairs, InitialCollisionFilter, collisionsOnly: true,
            maxPairs: Collisions.CollisionSettings.SoftCandidateBudget, traversalPhase: (uint)Plant.World.Timestep);
        var tNarrow = System.Diagnostics.Stopwatch.GetTimestamp();
        TicksBroad += tNarrow - tBroad;
        CollisionCandidatePairs += OverlappingPairs.Count;
        if (OverlappingPairs.Count == 0)
            break;

        var resolved = 0;
        BeginContactSweep();
        foreach (var overlap in OverlappingPairs)
		{
            if (resolved >= budget)
                break;

            int index1 = overlap.first;
			int index2 = overlap.second;

            if (!BranchIntersect(index1, index2, out var closestPoints))
				continue;

            ++CollisionContacts;
            ++resolved;

            //Push the pair apart by the depth they overlap, each about its own base, sharing the
            //correction so the heavier organ moves less.
            //
            //The previous form aimed both organs at a weighted midpoint BETWEEN them, which is a
            //direction that would drive them together, and then rotated with an axis of
            //Cross(target, current) - the reverse of what turns one onto the other. The two sign
            //errors cancelled into a separation, which is why it worked at all, and neither could
            //be corrected without the other. Stating the depth and the direction explicitly avoids
            //the question.
            var separation = closestPoints.Point2 - closestPoints.point1;
            var distance = closestPoints.distance;
            if (!float.IsFinite(distance) || separation.LengthSquared() <= 1e-12f)
                continue;

            var pushDirection = separation / MathF.Sqrt(separation.LengthSquared());
            var penetration = dst[index1].Radius + dst[index2].Radius - distance;
            if (penetration <= 0f)
                continue;

            //A contact in the basal quarter of an organ is answered by its parent, which has the
            //leverage to clear it. A root has no parent, so the escalation stops there.
            if (Vector3.Distance(closestPoints.point1, GetBaseCenter(index1)) < dst[index1].Length * 0.25f)
            {
                var escalated = GetParent(index1);
                if (escalated >= 0) index1 = escalated;
            }
            if (Vector3.Distance(closestPoints.Point2, GetBaseCenter(index2)) < dst[index2].Length * 0.25f)
            {
                var escalated = GetParent(index2);
                if (escalated >= 0) index2 = escalated;
            }

            if (index1 == index2 || isDescendant(index1, index2) || isDescendant(index2, index1))
            {
                //One organ carries the other. Rotating the ancestor moves the descendant with it,
                //so only the descendant is worth turning, and it takes the whole correction.
                var mover = isDescendant(index1, index2) ? index2 : index1;
                if (mover == index1 && !isDescendant(index2, index1))
                    continue;
                SeparateOrgan(mover, mover == index2 ? closestPoints.Point2 : closestPoints.point1,
                    mover == index2 ? pushDirection : -pushDirection, penetration);
                continue;
            }

            var w1 = Weights.Count > index1 ? Weights[index1] : 0f;
            var w2 = Weights.Count > index2 ? Weights[index2] : 0f;
            var total = w1 + w2;
            //Without usable weights, split it evenly.
            var share1 = float.IsFinite(total) && total > 1e-9f ? w2 / total : 0.5f;

            SeparateOrgan(index1, closestPoints.point1, -pushDirection, penetration * share1);
            SeparateOrgan(index2, closestPoints.Point2, pushDirection, penetration * (1f - share1));
        }

        TicksNarrow += System.Diagnostics.Stopwatch.GetTimestamp() - tNarrow;

        var tRebase = System.Diagnostics.Stopwatch.GetTimestamp();
        EndContactSweep(Collisions.CollisionSettings.MaxSoftCorrectionRadians);
        TicksRebase += System.Diagnostics.Stopwatch.GetTimestamp() - tRebase;
        if (resolved == 0)
            break;
        }
        }
        finally
        {
            CollisionTicks += System.Diagnostics.Stopwatch.GetTimestamp() - started;
        }
    }
	/// <summary>Refits <paramref name="i"/> and its whole subtree. The organ itself has to be
	/// included: a resolved contact rotates it, so its own bounds are stale too.</summary>
	private void refitChildrens(int i)
	{
		++CollisionGeometryRevision;
        var children = CollisionChildren;
        children.Clear();
		children.Enqueue(i);
        CollisionBvh.UpdateLeafBounds(i, CollisionBounds(i));
		if (GetIsRizome(i)) RhizomeBvh.UpdateLeafBounds(i, ComputeBounds(i));
        while (children.Count > 0)
        {
			var index = children.Dequeue();
            foreach (var child in GetChildren(index))
            {
                children.Enqueue(child);
                CollisionBvh.UpdateLeafBounds(child, CollisionBounds(child));
				if (GetIsRizome(child)) RhizomeBvh.UpdateLeafBounds(child, ComputeBounds(child));
            }

        }
        CollisionBvh.RefitUpdatedLeaves();
		RhizomeBvh.RefitUpdatedLeaves();
    }

	/// <summary>Refits every organ. The previous whole-tree refit passed index 0, which skipped
	/// agent 0's own bounds and any subtree under a second root.</summary>
	private void refitAllBounds()
	{
		++CollisionGeometryRevision;
        var children = CollisionChildren;
        children.Clear();
        foreach (var root in GetRoots())
        {
            children.Enqueue(root);
            CollisionBvh.UpdateLeafBounds(root, CollisionBounds(root));
            if (GetIsRizome(root)) RhizomeBvh.UpdateLeafBounds(root, ComputeBounds(root));
        }
        while (children.Count > 0)
        {
            var index = children.Dequeue();
            foreach (var child in GetChildren(index))
            {
                children.Enqueue(child);
                CollisionBvh.UpdateLeafBounds(child, CollisionBounds(child));
                if (GetIsRizome(child)) RhizomeBvh.UpdateLeafBounds(child, ComputeBounds(child));
            }
        }
        CollisionBvh.RefitUpdatedLeaves();
        RhizomeBvh.RefitUpdatedLeaves();
    }
    /// <summary>
    /// Turns one organ about its base so that <paramref name="contact"/> moves
    /// <paramref name="distance"/> along <paramref name="direction"/>.
    /// </summary>
    /// <remarks>
    /// The turn is carried into the subtree, because orientations here are absolute: rotating an
    /// organ alone leaves its descendants pointing where they were and shears the branch instead of
    /// swinging it. Soft contacts are transient - they do not touch the rest pose - so a foliage
    /// contact relaxes out again once the neighbour moves, which is what interleaved growth needs.
    /// </remarks>
    private void SeparateOrgan(int index, Vector3 contact, Vector3 direction, float distance)
    {
        if (index < 0 || distance <= 0f)
            return;

        var lever = contact - GetBaseCenter(index);
        var leverLength = lever.Length();
        if (leverLength < 1e-4f)
            return;

        var turn = Vector3.Cross(lever, direction);
        var turnLength = turn.Length();
        if (turnLength < 1e-9f)
            return;

        //Small angle. Rotating by theta about turn/turnLength advances the contact point along
        //`direction` by theta * turnLength, not theta * leverLength - the two differ by the sine of
        //the angle between the organ and the push, so dividing by the lever delivered only part of
        //the separation asked for, and none at all as the push approached the organ's own axis.
        var angle = MathF.Min(distance / turnLength, Collisions.CollisionSettings.MaxSoftCorrectionRadians);
        if (angle <= 0f)
            return;

        ApplyRotationDelta(index, Quaternion.CreateFromAxisAngle(turn / turnLength, angle), plastic: false);
    }

    private bool shareCrown(int index1, int index2) => TreeCache.SharesCrown(index1, index2);
    private bool isDescendant(int index, int descendant) => TreeCache.IsDescendant(index, descendant);

    private (Vector3 point1, Vector3 Point2, float distance) ClosestPointBetweenBranches(int index1, int index2)
    {
        var dst = Src();
        return ClosestPointBetweenBranches(
            GetBaseCenter(index1), Vector3.Transform(Vector3.UnitX, GetDirection(index1)), dst[index1].Length,
            GetBaseCenter(index2), Vector3.Transform(Vector3.UnitX, GetDirection(index2)), dst[index2].Length);
    }

    /// <summary>Closest points of two organ axes. Transforming a unit vector by a unit quaternion
    /// already yields a unit vector, so the directions are not renormalised.</summary>
    private static (Vector3 point1, Vector3 Point2, float distance) ClosestPointBetweenBranches(
        Vector3 base1, Vector3 dir1, float len1, Vector3 base2, Vector3 dir2, float len2)
    {
        Collisions.StaticCollider.ClosestPointsBetweenSegments(base1, base1 + dir1 * len1,
            base2, base2 + dir2 * len2, out var cp1, out var cp2, out var squared);
        return (cp1, cp2, MathF.Sqrt(squared));
    }
    public Vector3 GetTipPosition(int index)
    {
        var dst = Src();
        var direction = Vector3.Transform(Vector3.UnitX, dst[index].Orientation); // Local X axis
        return GetBaseCenter(index) + GetAttachmentOffset(index);
    }
    public bool BranchIntersect(int index1, int index2)
        => BranchIntersect(index1, index2, out _);

    /// <summary>
    /// Capsule against capsule, returning the closest points so the caller does not recompute them.
    /// </summary>
    /// <remarks>
    /// Same acceptance rule as before, reordered and with the transcendentals removed. This runs
    /// once per candidate pair and the broad phase produces tens of thousands of them per plant per
    /// step, so the order of the tests is the whole cost: the enum and index rejections are free
    /// and now come first, the closest points are computed once instead of three times (here, again
    /// inside baseCheck, and again in the caller), and
    /// <c>|acos(d) - pi/2| &gt; 1e-3</c> is written as <c>|d| &gt; sin(1e-3)</c>, which is the same
    /// test without two inverse cosines.
    /// </remarks>
    public bool BranchIntersect(int index1, int index2, out (Vector3 point1, Vector3 Point2, float distance) closest)
    {
        closest = default;
        if (index1 == index2)
            return false;

        var dst = Src();
        //Cheapest first: organ type and topology need no geometry at all.
        if (dst[index1].Organ == OrganTypes.Leaf || dst[index2].Organ == OrganTypes.Leaf)
            return false;
        if (GetParent(index1) == index2 || GetParent(index2) == index1)
            return false;

        var base1 = GetBaseCenter(index1);
        var base2 = GetBaseCenter(index2);
        //Siblings share one base point exactly - TreeCacheData assigns every child of a node from
        //the same expression - so this equality is meaningful rather than a float accident.
        if (base1 == base2)
            return false;

        var dir1 = Vector3.Transform(Vector3.UnitX, GetDirection(index1));
        var dir2 = Vector3.Transform(Vector3.UnitX, GetDirection(index2));
        var len1 = dst[index1].Length;
        var len2 = dst[index2].Length;

        var center1 = base1 + dir1 * (len1 * 0.5f);
        var center2 = base2 + dir2 * (len2 * 0.5f);
        var r1 = len1 * 0.5f + dst[index1].Radius;
        var r2 = len2 * 0.5f + dst[index2].Radius;
        if (Vector3.DistanceSquared(center1, center2) > (r1 + r2) * (r1 + r2))
            return false;

        var tip1 = base1 + dir1 * len1;
        var tip2 = base2 + dir2 * len2;
        // Exclude if base of one is tip of the other
        if (base1 == tip2 || base2 == tip1)
            return false;

        //The capsule test itself, hoisted ahead of everything that follows. Every remaining test
        //can only reject, and this one rejects about 99 of every 100 pairs the broad phase emits,
        //so running it first is what keeps the expensive predicates off the hot path. The sphere
        //reject above is kept because it is cheaper still.
        var (p1, p2, distance) = ClosestPointBetweenBranches(base1, dir1, len1, base2, dir2, len2);
        closest = (p1, p2, distance);
        if (distance - (dst[index1].Radius + dst[index2].Radius) >= 1e-3f)
            return false;

        if (!baseCheck(index1, index2) || !baseCheck(index2, index1))
            return false;

        var delta = p2 - p1;
        var deltaLenSq = delta.LengthSquared();
        if (deltaLenSq > 1e-24f)
        {
            var deltaDir = delta * (1f / MathF.Sqrt(deltaLenSq));
            //sin(1e-3) - the angular tolerance the inverse cosines used to express.
            const float PerpendicularTolerance = 0.0009999998f;

            if (MathF.Abs(Vector3.Dot(deltaDir, dir1)) > PerpendicularTolerance && distance > dst[index2].Radius)
                return false;
            if (MathF.Abs(Vector3.Dot(deltaDir, dir2)) > PerpendicularTolerance && distance > dst[index1].Radius)
                return false;

            if (Vector3.Dot(dir1, deltaDir) < 0f)
                return false;
            if (Vector3.Dot(dir2, -deltaDir) < 0f)
                return false;
        }

        return true;
    }

    private bool baseCheck(int index1, int index2)
    {
        var dst = Src();
        if (!isDescendant(index1, index2))
            return true;

        var topmost = -1;
        for (var i = GetParent(index2); i >= 0; i = GetParent(i))
        {
            var parent = GetParent(i);
            if (parent < 0 || !GetChildren(parent).Contains(index1))
                break;
            topmost = i;
        }

        if (topmost < 0)
            return true;

        var (_, b2, distanceBaseToBranch) = ClosestPointBetweenBranches(index1, index2);
        //Exact equality is deliberate: sibling base points are assigned from one shared
        //expression in TreeCacheData.UpdateBases, so they are bitwise identical when they coincide.
        return !(GetBaseCenter(topmost) == b2 && distanceBaseToBranch < dst[index1].Radius);
    }

    private void TryResolveGroundByRotation(int index, float groundHeight)
    {
        var dst = Src();
        Vector3 tip = GetTipPosition(index);
        if (tip.Y >= groundHeight)
            return;

        float depth = groundHeight - tip.Y;
        ref var agent = ref dst[index];

        Vector3 dir = Vector3.Transform(Vector3.UnitX, agent.Orientation);
        Vector3 correctionAxis = Vector3.Cross(dir, Vector3.UnitY);

        if (correctionAxis.LengthSquared() < 1e-6f)
            return;

        float angle = MathF.Asin(Math.Clamp(depth / agent.Length, -1f, 1f));

        Quaternion correction = Quaternion.CreateFromAxisAngle(Vector3.Normalize(correctionAxis), angle);
        Quaternion original = agent.Orientation;

        agent.SetOrientation(Quaternion.Normalize(correction * agent.Orientation));

    }

	/// <summary>Recomputes every base point and refits the collision hierarchy. The collision phase
	/// calls this once per plant after it has finished moving organs, not once per contact.</summary>
	internal void RebaseAndRefit()
	{
		TreeCache.UpdateBases(this);
		HeightDirty = false;
		refitAllBounds();
	}

	// A trial changes one support component. Recompute its descendants using the
	// same arithmetic as a whole-plant rebase; defer the unrelated height scan.
	void RebaseAndRefit(int root)
	{
		TreeCache.UpdateBases(this, root);
		HeightDirty = true;
		refitChildrens(root);
	}

	/// <summary>
	/// Rotates an organ and its whole subtree rigidly about the organ's base.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Orientations in this model are absolute, not relative to the parent, so a subtree does not
	/// follow its root on its own - <see cref="Gravity"/> has to propagate its bend explicitly, and
	/// anything that turns an organ must do the same or it shears the subtree instead of swinging
	/// it. Descendant base points are fixed up by the caller's rebase.
	/// </para>
	/// <para>
	/// <paramref name="plastic"/> also carries the turn into <c>baseOrientation</c>, the pose
	/// gravity bends away from, so the organ keeps the new heading instead of springing back next
	/// step. It is applied as a delta rather than through
	/// <see cref="AboveGroundAgent.SetStructuralOrientation"/>, which would overwrite the rest pose
	/// and discard the bend accumulated so far.
	/// </para>
	/// <para>
	/// The plastic share applies to the contacted organ ALONE. Descendants are rigidly carried, so
	/// they take the full delta in every pose they hold - see the remark at the propagation loop.
	/// </para>
	/// </remarks>
	internal void ApplyRotationDelta(int index, Quaternion delta, bool plastic)
	{
		if (BatchingContacts)
		{
			if (delta.W < 0f) delta = new(-delta.X, -delta.Y, -delta.Z, -delta.W);
			var vector = new Vector3(delta.X, delta.Y, delta.Z);
			var length = vector.Length();
			if (length > 1e-8f) ContactTurns[index] += vector * (2f * MathF.Atan2(length, delta.W) / length);
			return;
		}
		var dst = Src();
		if (index < 0 || index >= dst.Length)
			return;

		delta = Quaternion.Normalize(delta);
		//Only part of the turn is kept in the rest pose - see CollisionSettings.PlasticFraction -
		//and only up to a budget. An organ in sustained contact was reshaping its rest pose every
		//step with nothing to stop it, so a blade lying on the soil walked its own rest orientation
		//through hundreds of degrees over a couple of seasons. Past the budget a contact still
		//deflects the organ elastically; it simply no longer remodels it.
		var kept = Quaternion.Identity;
		if (plastic && dst[index].PlasticAngle < Collisions.CollisionSettings.MaxPlasticRadians)
		{
			kept = Quaternion.Normalize(Quaternion.Slerp(Quaternion.Identity, delta, Collisions.CollisionSettings.PlasticFraction));
			dst[index].PlasticAngle += 2f * MathF.Acos(Math.Clamp(MathF.Abs(kept.W), 0f, 1f));
		}

		dst[index].SetOrientation(Quaternion.Normalize(delta * dst[index].Orientation));
		if (plastic)
			dst[index].baseOrientation = Quaternion.Normalize(kept * dst[index].baseOrientation);

		var queue = CollisionChildren;
		queue.Clear();
		queue.Enqueue(index);
		while (queue.Count > 0)
		{
			var node = queue.Dequeue();
			foreach (var child in GetChildren(node))
			{
				queue.Enqueue(child);
				ref var c = ref dst[child];
				//The FULL delta, not the plastic share. Turning an organ is a rigid transport of
				//everything beyond it, and baseOrientation is an ABSOLUTE pose, so a descendant's
				//rest orientation has to be carried by exactly the same rotation as its actual one
				//or the relative rest configuration of the subtree silently changes. Only the
				//contacted organ's OWN rest pose is a deformation, and only that one takes
				//PlasticFraction.
				//
				//This was briefly the other way round, on the reasoning that a transient contact
				//should not reach a descendant's rest pose at all. That reasoning confuses a
				//deformation with a change of frame: writing `kept` here left every descendant
				//(delta - kept) away from its own rest pose after every single contact, with
				//nothing to write it back, and the residue is about whatever axis the contact
				//happened to use. Hundreds of steps of that is what turned a Carex blade from an
				//arc into a zig-zag. Gravity's own subtree propagation has always used the full
				//delta for exactly this reason.
				c.SetOrientation(Quaternion.Normalize(delta * c.Orientation));
				c.baseOrientation = Quaternion.Normalize(delta * c.baseOrientation);
				c.restOrientation = Quaternion.Normalize(delta * c.restOrientation);
			}
		}
	}

	/// <summary>Offset from this plant's field-local frame to world space. Plants in different
	/// soil fields do not share an origin, and on the facade those origins span tens of metres.</summary>
	internal Vector3 FieldOrigin => Plant.Soil.GetFieldOrigin(Plant.SoilIndex);

	/// <summary>The plant's canopy box in world space, for a probe that measures canopy overlap.</summary>
	public bool TryGetWorldBoundsPublic(out Vector3 min, out Vector3 max) => TryGetWorldBounds(out min, out max);

	/// <summary>The plant's canopy box in world space, for the top level of the inter-plant
	/// broad phase.</summary>
	internal bool TryGetWorldBounds(out Vector3 min, out Vector3 max)
	{
		if (!CollisionBvh.TryGetRootBounds(out var local))
		{
			min = max = Vector3.Zero;
			return false;
		}
		var origin = FieldOrigin;
		min = local.Min + origin;
		max = local.Max + origin;
		return true;
	}

	/// <summary>Organs whose bounds meet <paramref name="worldBox"/>. The hierarchy is field-local,
	/// so the query box is brought into that frame first.</summary>
	internal void QueryWorldBounds(Vector3 worldMin, Vector3 worldMax, List<int> result)
	{
		var origin = FieldOrigin;
		CollisionBvh.QueryOverlaps(new Bvh.BoundingBox(worldMin - origin, worldMax - origin), result);
	}

	/// <summary>An organ's axis in world space.</summary>
	internal void GetWorldSegment(int index, out Vector3 basePoint, out Vector3 tip, out float radius)
	{
		var dst = Src();
		basePoint = GetBaseCenter(index) + dst[index].BaseOffset + FieldOrigin;
		tip = basePoint + Vector3.Transform(Vector3.UnitX, GetDirection(index)) * dst[index].Length;
		radius = dst[index].Radius;
	}

	/// <summary>Separates one organ from a contact found by the inter-plant pass.</summary>
	internal void SeparateFromNeighbour(int index, Vector3 contact, Vector3 direction, float distance)
		=> SeparateOrgan(index, contact - FieldOrigin, direction, distance);

	internal bool GetGrowthBlocked(int index) => ReadTMP
		? index < AgentCount && AgentsTMP[index].GrowthBlocked
		: index < AgentCount && Agents[index].GrowthBlocked;

	internal void SetGrowthBlocked(int index, bool blocked)
	{
		var dst = Src();
		if (index >= 0 && index < dst.Length)
			dst[index].GrowthBlocked = blocked;
	}

	/// <summary>
	/// Answers a hard contact on a blade by bending the WHOLE blade, the way a beam under a point
	/// load actually responds, instead of hinging the one segment that happens to touch.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A contact used to rotate exactly one organ about its own base and carry everything beyond it
	/// rigidly. For a blade that is the wrong body: the load at the contact is reacted by every
	/// segment between it and the sheath, each turning in proportion to its own compliance, and a
	/// blade whose tip meets the ground lifts along its length rather than folding at one joint.
	/// Hinging one segment also puts the whole correction into one place, so clearing a tip by a
	/// few millimetres swung it through the full remaining lever and overshot.
	/// </para>
	/// <para>
	/// The distribution is Euler-Bernoulli and needs no tuning constant. A turn of theta_k about
	/// axis a_k at node k moves the contact point by theta_k * |r_k x u| along the escape direction
	/// u, and the moment a point load there exerts on node k is proportional to the same quantity,
	/// so theta_k = lambda * c_k * g_k with c_k the node's angular compliance L/(EI) and
	/// g_k = |r_k x u|. Requiring the displacements to sum to the correction fixes lambda at
	/// distance / sum(c_k * g_k^2). Segments BEYOND the contact are carried rigidly and that is
	/// correct: a free end past a point load carries no moment.
	/// </para>
	/// </remarks>
	internal bool DistributeBladeCorrection(int index, Vector3 contactPoint, Vector3 escape, float distance, bool plastic)
	{
		if (!Collisions.CollisionSettings.DistributeBladeContacts)
			return false;

		var dst = Src();
		if (index < 0 || index >= dst.Length || distance <= 0f)
			return false;
		if (dst[index].Organ is not (OrganTypes.Leaf or OrganTypes.FlowerPadel))
			return false;

		var direction = escape.LengthSquared() > 1e-12f ? Vector3.Normalize(escape) : Vector3.Zero;
		if (direction == Vector3.Zero)
			return false;

		//The blade, from the contacted segment back to the sheath.
		var chain = BladeChainBuffer;
		chain.Clear();
		for (var node = index; node >= 0 && GetOrgan(node) is OrganTypes.Leaf or OrganTypes.FlowerPadel; node = GetParent(node))
		{
			chain.Add(node);
			if (chain.Count > SpeciesSettings.MaximumLeafSegments)
				break;
		}
		if (chain.Count < 2)
			return false;

		Span<float> gains = stackalloc float[SpeciesSettings.MaximumLeafSegments + 1];
		Span<float> weights = stackalloc float[SpeciesSettings.MaximumLeafSegments + 1];
		var total = 0f;
		for (int k = 0; k < chain.Count; ++k)
		{
			var node = chain[k];
			var lever = contactPoint - GetBaseCenter(node);
			var cross = Vector3.Cross(lever, direction);
			gains[k] = cross.Length();
			//Angular compliance of one segment: how far it turns per unit moment.
			var keel = BladeKeelAt(node);
			var inertia = BladeInertia(dst[node].Radius, keel);
			var elasticity = Plant.Parameters.LeafElasticModulus > 0f
				? Plant.Parameters.LeafElasticModulus
				: Plant.Parameters.GreenElasticModulus;
			var compliance = inertia > 1e-20f && elasticity > 1f ? dst[node].Length / (elasticity * inertia) : 0f;
			weights[k] = compliance * gains[k];
			total += weights[k] * gains[k];
		}
		if (total <= 1e-20f)
			return false;

		var lambda = distance / total;

		//If any node's share overruns the per-step cap, scale the WHOLE distribution rather than
		//clipping that one node: clipping keeps the total down but distorts the shape of the
		//response, leaving the blade bent in a way no load produces. A scaled response is simply a
		//partial one, and the next step continues it.
		var largest = 0f;
		for (int k = 0; k < chain.Count; ++k)
			largest = MathF.Max(largest, lambda * weights[k]);
		if (largest > Collisions.CollisionSettings.MaxCorrectionRadians)
			lambda *= Collisions.CollisionSettings.MaxCorrectionRadians / largest;

		var moved = false;
		//Base first, so each turn is applied to a configuration its ancestors have already settled.
		for (int k = chain.Count - 1; k >= 0; --k)
		{
			var angle = lambda * weights[k];
			if (angle <= 1e-7f || gains[k] <= 1e-9f)
				continue;

			var node = chain[k];
			var lever = contactPoint - GetBaseCenter(node);
			var turn = Vector3.Cross(lever, direction);
			var turnLength = turn.Length();
			if (turnLength < 1e-9f)
				continue;
			ApplyRotationDelta(node, Quaternion.CreateFromAxisAngle(turn / turnLength, angle), plastic);
			moved = true;
		}
		return moved;
	}

	readonly List<int> BladeChainBuffer = [];

	/// <summary>Records that a surface is carrying this organ's load. See
	/// <see cref="AboveGroundAgent.GroundSupported"/>.</summary>
	internal void SetGroundSupported(int index, bool supported)
	{
		var dst = Src();
		if (index >= 0 && index < dst.Length)
			dst[index].GroundSupported = supported;
	}

	internal ushort GetObstructionSteps(int index) => ReadTMP
		? index < AgentCount ? AgentsTMP[index].ObstructionSteps : (ushort)0
		: index < AgentCount ? Agents[index].ObstructionSteps : (ushort)0;

	internal void SetObstructionSteps(int index, ushort steps)
	{
		var dst = Src();
		if (index >= 0 && index < dst.Length)
			dst[index].ObstructionSteps = steps;
	}

	public void setAgentOrientation(int id, Quaternion orientation)
	{
        var dst = Src();
		dst[id].SetOrientation(orientation);
    }

    [StructLayout(LayoutKind.Auto)]
	readonly struct PathData
	{
		public readonly int Index;
		public readonly int SegDist;
		public readonly float MetricDist;
		public readonly float BranchDist;
		//public readonly bool Dir;

		public PathData(int index, int segDist, float metricDist, float branchDist)
		{
			Index = index;
			SegDist = segDist;
			MetricDist = metricDist;
			BranchDist = branchDist;
		}
	}
	readonly List<PathData> AuxinPath = [];
	readonly List<(int, float)> AuxinFrontA = [], AuxinFrontB = [];

	internal void Hormones(bool isAG)
	{
		//Auxin
		if (isAG)
		{
			var dst = Src();
			var path = AuxinPath;
			for(int i = 0; i < dst.Length; ++i)
			{
				var isMeristem = dst[i].Organ == OrganTypes.Meristem;
				if (isMeristem || (Births.Count > 0 && dst[i].Organ == OrganTypes.Stem && dst[i].Auxins >= Plant.Parameters.AuxinsProduction))
					DistributeAuxin(dst, i, path);

				if (isMeristem)
					foreach(var child in GetChildren(i))
						dst[child].IncAuxins(Plant.Parameters.AuxinsProduction);
			}
		}
	}

	void DistributeAuxin(Span<T> dst, int initNode, List<PathData> path)
	{
		path.Clear();

		var auxinsReach = Plant.Parameters.AuxinsReach;
		var bufferSrc = AuxinFrontA;
		var bufferDst = AuxinFrontB;
		bufferSrc.Clear();
		bufferDst.Clear();

		{
			var p = GetParent(initNode);
			if (p >= 0)
			{
				foreach(var child in GetChildren(p))
					if (child != initNode && dst[child].Organ == OrganTypes.Stem)
						bufferSrc.Add((-(child+1), 0));

				bufferSrc.Add((++p, 0));
			}
		}

		var segSum = 0;
		var c = 1; //seg Sums count from 1
		while (bufferSrc.Count > 0)
		{
			bufferDst.Clear();
			for(int i = 0; i < bufferSrc.Count; ++i)
			{
				segSum += c;
				var (prev, distSum) = bufferSrc[i];
				if (prev > 0) //hence we follow roots
				{
					--prev;
					var d = GetLength(prev);
					distSum += d;
					if (distSum < auxinsReach)
					{
						path.Add(new(prev, c, d, distSum));

						var next = GetParent(prev);
						if (next >= 0)
						{
							foreach(var child in GetChildren(next))
								if (child != prev && dst[child].Organ == OrganTypes.Stem)
									bufferDst.Add((-(child+1), distSum));

							bufferDst.Add((++next, distSum));
						}
					}
				}
				else // p < 0, hence we follow children
				{
					prev = -++prev;
					var d = GetLength(prev);
					distSum += d;
					if (distSum < auxinsReach)
					{
						path.Add(new(prev, c, d, distSum));

						foreach(var child in GetChildren(prev))
							if (child != prev && dst[child].Organ == OrganTypes.Stem)
								bufferDst.Add((-(child+1), distSum));
					}
				}
			}
			++c;
			(bufferDst, bufferSrc) = (bufferSrc, bufferDst);
		}

		var auxin = Plant.Parameters.AuxinsProduction;

		var factor = auxin / segSum;

		var maxSegDist = c;
		for(int i = 0; i < path.Count; ++i)
			dst[path[i].Index].IncAuxins(path[i].MetricDist * (maxSegDist - path[i].SegDist) * factor / path[i].BranchDist);
	}

	public void Distribute(PlantGlobalStats stats)
	{
		var dst = Src();
		for (int i = 0; i < dst.Length; ++i)
		{

            dst[i].Distribute(stats.ReceivedWater[i], stats.ReceivedEnergy[i]);
		}
	}

	// List<Vector2> HormonesData = new();
	// internal void Hormones(AgroWorld world)
	// {
	// 	var degradation = new Vector2(Plant.Parameters.AuxinsDegradation, Plant.Parameters.CytokininsDegradation);
	// 	HormonesData.Clear();
	// 	var dst = Src();
	// 	for(int i = 0; i < dst.Length; ++i)
	// 	{
	// 		HormonesData.Add(GetHormones(i));
	// 	}

	// }

	public void DeliverPost(uint timestep)
	{
		// Roots.Clear();
		// Roots.AddRange(RootsTMP);
		if (Post.AnyMessages)
		{
			SrcDst(out var src, out var dst);
			src.CopyTo(dst);
			Post.Process(timestep, dst);

			ReadTMP = !ReadTMP;
		}
	}

	public bool HasUndeliveredPost => Post.AnyMessages;
	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public int GetParent(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Parent : int.MinValue)
		: (AgentCount > index ? Agents[index].Parent : int.MinValue);
	public List<int> GetChildren(int index) => TreeCache.GetChildren(index);
	public int GetAbsDepth(int index) => TreeCache.GetAbsDepth(index);
	public int GetAbsInvDepth(int index) => TreeCache.GetAbsInvDepth(index);
	public float GetRelDepth(int index) => TreeCache.GetRelDepth(index);
	public List<int> GetRoots() => TreeCache.GetRoots();
	public List<int> GetLeaves() => TreeCache.GetLeaves();

	public Vector3 GetBoundingBoxSize()
	{
		float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
		return default;
	}

	internal float GetEnergyCapacity(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].EnergyStorageCapacity(Plant.Parameters) : 0f)
		: (AgentCount > index ? Agents[index].EnergyStorageCapacity(Plant.Parameters) : 0f);

	internal float GetWaterStorageCapacity(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].WaterStorageCapacity_g() : 0f)
		: (AgentCount > index ? Agents[index].WaterStorageCapacity_g() : 0f);

	internal float GetWaterTotalCapacity(AgroWorld world, int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].WaterTotalCapacityPerTick_g(world) : 0f)
		: (AgentCount > index ? Agents[index].WaterTotalCapacityPerTick_g(world) : 0f);

	internal float GetWaterEfficientCapacity(AgroWorld world, int index)
	{
		var src = Src();
		if (AgentCount > index)
		{
			var agent = src[index];
			return agent.WaterStorageCapacity_g() + agent.PhotosynthPerTick(world, Plant.Parameters);
		}
		else
			return 0f;
	}

	public float GetEnergy(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Energy : 0f)
		: (AgentCount > index ? Agents[index].Energy : 0f);

	public float GetEnergyFlow_PerTick(AgroWorld world, int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].EnergyFlowToParentPerTick(world) : 0f)
		: (AgentCount > index ? Agents[index].EnergyFlowToParentPerTick(world) : 0f);

	public float GetWater(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Water_g : 0f)
		: (AgentCount > index ? Agents[index].Water_g : 0f);

	public float GetBaseRadius(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Radius : 0f)
		: (AgentCount > index ? Agents[index].Radius : 0f);

	public float GetLength(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Length : 0f)
		: (AgentCount > index ? Agents[index].Length : 0f);

	public Quaternion GetDirection(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Orientation : Quaternion.Identity)
		: (AgentCount > index ? Agents[index].Orientation : Quaternion.Identity);

	public OrganTypes GetOrgan(int index) => ReadTMP
		? (index >= 0 && AgentCount > index ? AgentsTMP[index].Organ : OrganTypes.Stem)
		: (index >= 0 && AgentCount > index ? Agents[index].Organ : OrganTypes.Stem);

	public float GetWoodRatio(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].WoodRatio() : 0f)
		: (AgentCount > index ? Agents[index].WoodRatio() : 0f);

    public bool GetIsRizome(int index) => ReadTMP
           ? (index >= 0 && AgentCount > index && AgentsTMP[index].IsRizome)
           : (index >= 0 && AgentCount > index && Agents[index].IsRizome);
    public bool GetHasFlowerSupport(int index) => ReadTMP
       ? (AgentCount > index ? AgentsTMP[index].flowerSupport : false)
       : (AgentCount > index ? Agents[index].flowerSupport : false);
    public Vector3 GetBaseOffset(int index) => ReadTMP
    ? (AgentCount > index ? AgentsTMP[index].BaseOffset : Vector3.Zero)
    : (AgentCount > index ? Agents[index].BaseOffset : Vector3.Zero);
	public Vector3 GetColor(int index) => ReadTMP
	? (AgentCount > index ? AgentsTMP[index].Color : Vector3.Zero)
	: (AgentCount > index ? Agents[index].Color : Vector3.Zero);

    public Vector3 GetBaseCenter(int index) => TreeCache.GetBaseCenter(index);
	public Vector3 GetBaseCenterWorld(int index) => TreeCache.GetBaseCenter(index) + Plant.Soil.GetFieldOrigin(Plant.SoilIndex);

	public byte GetDominance(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].DominanceLevel : (byte)0)
		: (AgentCount > index ? Agents[index].DominanceLevel : (byte)0);

	/// <summary>
	/// Production during the previous day, per m² i.e. invariant of size
	/// </summary>
	public float GetDailyProductionInv(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].PreviousDayProductionInvariant : 0f)
		: (AgentCount > index ? Agents[index].PreviousDayProductionInvariant : 0f);

	/// <summary>
	/// Resources allocated during the previous day, per m² i.e. invariant of size
	/// </summary>
	public float GetDailyResourcesInv(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].PreviousDayEnvResourcesInvariant : 0f)
		: (AgentCount > index ? Agents[index].PreviousDayEnvResourcesInvariant : 0f);

	public float GetDailyEfficiency(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].PreviousDayEnvResources : 0f)
		: (AgentCount > index ? Agents[index].PreviousDayEnvResources : 0f);

	public Vector3 GetScale(int index) => ReadTMP
		? (AgentCount > index ? AgentsTMP[index].Scale() : Vector3.Zero)
		: (AgentCount > index ? Agents[index].Scale() : Vector3.Zero);

	///<summary>
	///Volume in m³
	///</summary>
	public float GetVolume()
	{
		var volume = 0f;
		foreach (ref readonly var agent in Src())
			volume += agent.Volume();
		return volume;
	}

	///<summary>
	///Weight in g
	///</summary>
	public float GetWeight(int index, SpeciesSettings species)
	{
		var src = Src();
		if (index >= src.Length)
			return 0f;
		else
		{
			var woodRatio = src[index].WoodRatio();
			return (woodRatio * species.DensityDryWood + ((1f - woodRatio) * species.DensityDryStem)) * src[index].Volume() + src[index].Water_g;
		}
	}

	/// <summary>
	/// Track the timesteps when flowering of this plant started. While flowering is ongoing the most recent start is tha last value in the list, uint.MaxValue otherwise.
	/// </summary>
	readonly List<uint> FloweringStarted = [PreFloweringState];
	const uint PreFloweringState = uint.MaxValue;
	const uint PostFloweringState = uint.MaxValue - 1;

	/// <summary>
	/// Season year whose flowering window the state in <see cref="FloweringStarted"/> refers to.
	/// A season year runs from 1 March to the end of the following February, matching
	/// <see cref="AgroWorld.Season"/> and the crown-bud activation cycle.
	/// </summary>
	int FloweringCycle = int.MinValue;

	static int SeasonCycle(DateTime date) => date.Month >= 3 ? date.Year : date.Year - 1;

    bool CacheFloweringPhase, FloweringPhaseCached;
    uint PhaseCacheTimestep;
    SeasonalPhase CachedFloweringPhase;

    public SeasonalPhase GetFloweringPhase(SpeciesSettings species, uint timestep)
    {
        // Cache only within the organ tick: age/calendar and the source buffer
        // stay fixed there. Calls outside Tick retain their original state updates.
        if (!CacheFloweringPhase || timestep != PhaseCacheTimestep || !ReferenceEquals(species, Plant.Parameters))
            return CalculateFloweringPhase(species, timestep);
        if (!FloweringPhaseCached)
        {
            CachedFloweringPhase = CalculateFloweringPhase(species, timestep);
            // Closing a window after senescence returns PostFlower once, then
            // ResetPending. Do not turn this existing transition into a whole tick.
            FloweringPhaseCached = CachedFloweringPhase != SeasonalPhase.PostFlower ||
                Plant.World.Season <= species.SeasonalSenescence;
        }
        return CachedFloweringPhase;
    }

    SeasonalPhase CalculateFloweringPhase(SpeciesSettings species, uint timestep)
    {
		var season = Plant.World.Season;
		var age_h = Plant.GetAge_h();

		//Re-arm the window once per season year. The old code re-armed only inside the
		//"season < FloweringSeasonStart" branch, which a species with
		//FloweringSeasonStart == 0 can never reach: Carex flowered once and then stayed
		//in ResetPending - and with its Dormant* rates at zero that froze the whole plant
		//for nine months. The season year is the same cycle TryActivateCrownBud uses.
		var cycle = SeasonCycle(Plant.World.GetTime(timestep));
		if (cycle != FloweringCycle)
		{
			FloweringCycle = cycle;
			if (FloweringStarted[^1] != PreFloweringState)
				FloweringStarted.Add(PreFloweringState);
		}

        if (age_h < species.FloweringStartAgeHours || season < species.FloweringSeasonStart)
		{
			switch (FloweringStarted[^1])
			{
				case PreFloweringState: break;
				case PostFloweringState: FloweringStarted[^1] = uint.MaxValue; break;
				default: FloweringStarted.Add(uint.MaxValue); break;
			}

			//Dormancy is driven by the season, not by flowering history. A plant too
			//young to flower reaches this branch all year round and would otherwise
			//keep growing through the winter.
			return season > species.SeasonalSenescence
				? SeasonalPhase.ResetPending
				: SeasonalPhase.PreFlower;
		}
        else if (age_h >= species.FloweringStartAgeHours && season >= species.FloweringSeasonStart && season <= species.FloweringSeasonEnd && (FloweringStarted[^1] == PreFloweringState || (FloweringStarted[^1] < PostFloweringState && (Plant.World.Timestep - FloweringStarted[^1]) * Plant.World.HoursPerTick <= species.FloweringPhaseDurationHours)))
		{
			if (FloweringStarted[^1] == PreFloweringState)
				FloweringStarted[^1] = Plant.World.Timestep;
			return SeasonalPhase.Flowering;
		}
		//Only a recorded start timestep may be closed off. PostFloweringState is
		//uint.MaxValue - 1, so a "< uint.MaxValue" test also matched an already
		//closed cycle: the branch re-entered every tick, appended a list entry each
		//time, and evaluated the elapsed time against a wrapped-around sentinel.
		//ResetPending then became unreachable, so winter dormancy never happened.
		if (FloweringStarted[^1] < PostFloweringState && ((Plant.World.Timestep - FloweringStarted[^1]) * Plant.World.HoursPerTick > species.FloweringPhaseDurationHours || season > species.FloweringSeasonEnd))
		{
			FloweringStarted[^1] = PostFloweringState;
			return SeasonalPhase.PostFlower;
		}
        else if (FloweringStarted[^1] == PostFloweringState)
		{
			//Fruiting and reserve refilling continue after anthesis until seasonal
			//senescence sets in; from then on the plant is dormant and the Dormant*
			//probabilities apply. Branch one returns it to PreFlower next spring.
			return season > species.SeasonalSenescence
				? SeasonalPhase.ResetPending
				: SeasonalPhase.PostFlower;
		}
        else
		{
			//The window closed without this plant ever entering it - it was still too
			//young when the season passed FloweringSeasonEnd. That is not dormancy:
			//a perennial keeps growing vegetatively until seasonal senescence. Returning
			//ResetPending here is what stalled a first-year Carex from May to March.
			return season > species.SeasonalSenescence
				? SeasonalPhase.ResetPending
				: SeasonalPhase.PostFlower;
		}
    }

    public float GetAuxins(int index) => ReadTMP
		? (index < AgentCount ? AgentsTMP[index].Auxins : 0f)
		: (index < AgentCount ? Agents[index].Auxins : 0f);

    public uint GetAge_ticks(int index, uint timestep) => ReadTMP
		? index < AgentCount ? timestep - AgentsTMP[index].BirthTime : 0
		: index < AgentCount ? timestep - Agents[index].BirthTime : 0;

	public float GetAdulcy(int index, uint timestep) => ReadTMP
		? index < AgentCount ? AgentsTMP[index].Adulcy(timestep) : 0f
		: index < AgentCount ? Agents[index].Adulcy(timestep) : 0f;

	public float GetStress(int index) => ReadTMP
		? index < AgentCount ? AgentsTMP[index].Stress : 0f
		: index < AgentCount ? Agents[index].Stress : 0f;

	public float GetSenescence(int index, uint timestep) => ReadTMP
		? index < AgentCount ? AgentsTMP[index].Senescence(timestep) : 0f
		: index < AgentCount ? Agents[index].Senescence(timestep) : 0f;
	#endregion

	///////////////////////////
	#region WRITE METHODS
	///////////////////////////
	//THERE ARE NO WRITE METHODS ALLOWED except for these via messages.
	#endregion

	readonly List<byte> ChildrenToWaitFor = [];
	readonly List<uint> LeavesCount = [];
	readonly List<float> Volumes = [];
	readonly List<float> Resources = [];
	List<int> ReadyNodes0 = [], ReadyNodes1 = [];
	readonly List<int> NewDayIncomplete = [];

	public void BuildBvh()
	{
		++CollisionGeometryRevision;
        var dst = Src();
        if (dst.Length == 0)
        {
            CollisionBvh.Clear();
			RhizomeBvh.Clear();
            return;
        }
        // All physical organs participate in foliage contacts. The separate rhizome hierarchy
        // also serves the existing developmental branching query; it is not an exclusion mask.
        var leaves = CollisionLeaves;
        var rhizomes = RhizomeLeaves;
        leaves.Clear();
        rhizomes.Clear();

        AggregatedPetals = 0;
        for (int i = 0; i < dst.Length; ++i)
        {
            if (IsCollisionEligible(i))
            {
                // A broad receptacle box does not make its narrow-phase sphere represent
                // the petals. Keep their actual sheets so aggregation cannot hide crossings.
                leaves.Add((i, CollisionBounds(i)));
            }
            if (GetIsRizome(i))
                rhizomes.Add((i, ComputeBounds(i)));
        }

        CollisionBodies = leaves.Count;
        if (!CollisionBvh.TryRefit(CollectionsMarshal.AsSpan(leaves)))
            CollisionBvh.Build(CollectionsMarshal.AsSpan(leaves));
        // Different leaves of the same crown can touch; crown-wide pruning is not valid.
        if (!RhizomeBvh.TryRefit(CollectionsMarshal.AsSpan(rhizomes)))
            RhizomeBvh.Build(CollectionsMarshal.AsSpan(rhizomes));
    }
    //Temporal coherence for the static passes: an organ that was measured well clear of every
    //wall does not have to be measured again until it has moved far enough to matter. Indexed by
    //agent, so Census invalidates the lot whenever it reindexes.
    internal float[] StaticClearance = [];
    internal Collisions.OrganPose[] ClearancePose = [];

    internal void EnsureClearanceCapacity(int count)
    {
        if (StaticClearance.Length >= count)
            return;
        var capacity = Math.Max(count, Math.Max(16, StaticClearance.Length * 2));
        Array.Resize(ref StaticClearance, capacity);
        Array.Resize(ref ClearancePose, capacity);
    }

    /// <summary>Drops every cached clearance. Census moves agents between indices, so a cache keyed
    /// by index cannot survive it.</summary>
    internal void InvalidateStaticClearance() => Array.Clear(StaticClearance);

    /// <summary>
    /// The current and swept bounds an organ occupies in the collision hierarchy.
    /// </summary>
    Bvh.BoundingBox CollisionBounds(int index)
    {
        var bounds = ComputeBounds(index);
        if (index < PreviousGeometry.Length && PreviousGeometry[index].Exists)
        {
            var template = GetLaminaTemplate(index);
            var previous = new Collisions.ContactShape(PreviousGeometry[index].Pose, template);
            var current = new Collisions.ContactShape(GetContactPose(index), template);
            var swept = previous.SweptBounds(current);
            bounds = Bvh.BoundingBox.Merge(bounds, new(swept.Min - FieldOrigin, swept.Max - FieldOrigin));
        }
        return bounds;
    }

    public Bvh.BoundingBox ComputeBoundsFromParameters(Vector3 baseCenter, Quaternion orientation, float length, float radius)
    {
        var direction = Vector3.Transform(Vector3.UnitX, orientation);
        if (direction.LengthSquared() < 1e-6f)
            direction = Vector3.UnitX;

        var tip = baseCenter + Vector3.Normalize(direction) * length;
        var radiusVec = new Vector3(radius);
        var min = Vector3.Min(baseCenter, tip) - radiusVec;
        var max = Vector3.Max(baseCenter, tip) + radiusVec;
        return new Bvh.BoundingBox(min, max);
    }

    /// <summary>
    /// Bounds of an organ's collision body, in the same shape the light model and the primitive
    /// export give it.
    /// </summary>
    /// <remarks>
    /// This used to treat every organ as a capsule inflated by <c>Radius</c> in all three axes.
    /// For a lamina that is wrong twice over: <c>Radius</c> is its half WIDTH, not a stem radius,
    /// so the box was inflated by centimetres along the blade normal where the blade is 0.1 mm
    /// thick. It also ignored <see cref="GetBaseOffset"/>, which every other consumer applies -
    /// so a petal was bounded at its receptacle rather than out on the rim where it actually sits.
    /// </remarks>
    public Bvh.BoundingBox ComputeBounds(int index, bool usePendingOrientation = false)
    {
        var dst = Src();
        if (index < 0 || index >= dst.Length)
            return new Bvh.BoundingBox(Vector3.Zero, Vector3.Zero);

        var orientation = GetDirection(index);
        var axis = Vector3.Transform(Vector3.UnitX, orientation);
        if (axis.LengthSquared() < 1e-6f)
            axis = Vector3.UnitX;

        var baseCenter = GetBaseCenter(index) + dst[index].BaseOffset;
        var length = dst[index].Length;
        var radius = dst[index].Radius;

        switch (dst[index].Organ)
        {
            case OrganTypes.Leaf:
            case OrganTypes.FlowerPadel:
            {
                if (GetLaminaTemplate(index) is { } template)
                {
                    var pose = GetContactPose(index) with { Base = baseCenter };
                    return template.Bounds(pose);
                }
                //A flat rectangle: length along the organ axis, half width across it, and a
                //thickness that is almost nothing along the blade normal.
                var across = Vector3.Transform(Vector3.UnitZ, orientation) * radius;
                var normal = Vector3.Transform(Vector3.UnitY, orientation) * (AboveGroundAgent.LeafThickness * 0.5f);
                var tip = baseCenter + axis * length;
                var half = Vector3.Abs(across) + Vector3.Abs(normal);
                return new Bvh.BoundingBox(Vector3.Min(baseCenter, tip) - half, Vector3.Max(baseCenter, tip) + half);
            }

            case OrganTypes.Bud:
            case OrganTypes.FlowerBud:
            case OrganTypes.FlowerBaseBud:
            case OrganTypes.Fruit:
            case OrganTypes.Seed:
            {
                //A sphere whose radius is the organ length, matching CpuLightSolver.FillPlant and
                //the primitive export.
                var r = new Vector3(MathF.Max(length, radius));
                return new Bvh.BoundingBox(baseCenter - r, baseCenter + r);
            }

            default:
            {
                var tip = baseCenter + axis * length;
                var radiusVec = new Vector3(radius);
                return new Bvh.BoundingBox(Vector3.Min(baseCenter, tip) - radiusVec, Vector3.Max(baseCenter, tip) + radiusVec);
            }
        }
    }
    public void NewDay(uint timestep, byte ticksPerDay)
	{
		var src = Src();

		DailyResourceMax = float.MinValue;
		DailyProductionMax = float.MinValue;
		DailyEfficiencyMax = float.MinValue;
		NewDayIncomplete.Clear();

		if (IsAboveGround)
		{
			ReadyNodes0.Clear();
			ChildrenToWaitFor.Clear();
			LeavesCount.Clear();
			Volumes.Clear();
			Resources.Clear();

			for(int i = 0; i < src.Length; ++i)
			{
				var complete = src[i].CompleteDay(timestep, ticksPerDay); //for all organs as non-leaves also need to be reset to zero
				//assuming only leaves photosynthesize
				if (src[i].Organ == OrganTypes.Leaf)
				{
					if (complete)
					{
						if (DailyResourceMax < src[i].PreviousDayEnvResourcesInvariant) DailyResourceMax = src[i].PreviousDayEnvResourcesInvariant;
						if (DailyProductionMax < src[i].PreviousDayProductionInvariant) DailyProductionMax = src[i].PreviousDayProductionInvariant;
					}
					else
						NewDayIncomplete.Add(i);

					//A leaf is not necessarily a tip any more: a long blade is a chain of leaf
					//segments, so a segment may have a leaf child to wait for. Seeding those with
					//zero put them in the ready set immediately, and the child's later
					//--ChildrenToWaitFor[parent] then underflowed a byte from 0 to 255 - the
					//bubbling stopped there and every ancestor lost its share of the tree's
					//resources and volume. The assertion below is what catches it in a debug build;
					//in release it corrupts the distribution silently.
					Debug.Assert(GetChildren(i).Count < 255);
					var leafChildren = (byte)GetChildren(i).Count;
					ChildrenToWaitFor.Add(leafChildren);
					if (leafChildren == 0)
						ReadyNodes0.Add(i);
					LeavesCount.Add(1);
					Resources.Add(src[i].PreviousDayEnvResources);
				}
				else
				{
					Debug.Assert(GetChildren(i).Count < 255);
					var children = (byte)GetChildren(i).Count;
					ChildrenToWaitFor.Add(children);
					LeavesCount.Add(0);
					Resources.Add(0);
					if (children == 0) //for those tips of twigs that have no leaves
						ReadyNodes0.Add(i);
				}
				Volumes.Add(src[i].Volume());
			}

			if (DailyResourceMax == float.MinValue) //means that all leaves are in incomplete list
			{
				DailyResourceMax = 1;
				DailyProductionMax = 1;
				DailyEfficiencyMax = 1;
			}
			//Debug.WriteLineIf(NewDayIncomplete.Count > 0, $"Setting maximum of {DailyResourceMax} : {DailyProductionMax} for {NewDayIncomplete.Count} leaves.");

			foreach(var i in NewDayIncomplete)
			{
				var pdr = DailyProductionMax * src[i].Radius * src[i].Length;
				src[i].DailySet(DailyResourceMax, DailyProductionMax, pdr);
				Resources[i] = pdr;
			}

			// Debug.Assert(DailyProductionMax > 0f);
			// Debug.Assert(DailyResourceMax > 0);

			//now bubble up the tree to the root(s) and propagate the maximum
			while (ReadyNodes0.Count > 0)
			{
				ReadyNodes1.Clear();
				foreach(var i in ReadyNodes0)
				{
					var parent = src[i].Parent;
					if (parent >= 0)
					{
						var lc = LeavesCount[i];
						//src[parent].DailyMax(src[i].PreviousDayEnvResourcesInv, src[i].PreviousDayProductionInv);
						if (lc > 0)
						{
							src[parent].DailyAdd(src[i].PreviousDayEnvResourcesInvariant, src[i].PreviousDayProductionInvariant);
							LeavesCount[parent] += LeavesCount[i];
							Resources[parent] += Resources[i];
						}

						Volumes[parent] += Volumes[i];

						--ChildrenToWaitFor[parent];
						if (ChildrenToWaitFor[parent] == 0)
							ReadyNodes1.Add(parent);
					}
				}
				(ReadyNodes1, ReadyNodes0) = (ReadyNodes0, ReadyNodes1);
			}

			Debug.Assert(ChildrenToWaitFor.All(x => x == 0));
			const float rcpScale = 1e-5f;

			for(int i = 0; i < src.Length; ++i)
				if (src[i].Organ == OrganTypes.Bud)
				{
					var parent = src[i].Parent;
					src[i].DailySet(src[parent].PreviousDayEnvResourcesInvariant, src[parent].PreviousDayProductionInvariant, Resources[parent] * rcpScale / Volumes[parent]);
					//src[i].DailySet(Resources[parent], Volumes[parent]);
				}
				else
				{
					var efficiency = Resources[i] * rcpScale / Volumes[i];

					if (efficiency > DailyEfficiencyMax)
						DailyEfficiencyMax = efficiency;

					if (LeavesCount[i] > 1)
						//src[i].DailyDiv(LeavesCount[i]);
						src[i].DailySet(src[i].PreviousDayEnvResourcesInvariant / LeavesCount[i], src[i].PreviousDayProductionInvariant / LeavesCount[i], efficiency);
					else
						src[i].DailySet(src[i].PreviousDayEnvResourcesInvariant, src[i].PreviousDayProductionInvariant, efficiency);
				}
		}
		else
		{
			for(int i = 0; i < src.Length; ++i)
			{
				if (src[i].CompleteDay(timestep, ticksPerDay))
				{
					if (DailyResourceMax < src[i].PreviousDayEnvResourcesInvariant) DailyResourceMax = src[i].PreviousDayEnvResourcesInvariant;
					if (DailyProductionMax < src[i].PreviousDayProductionInvariant) DailyProductionMax = src[i].PreviousDayProductionInvariant;
				}
				else
					NewDayIncomplete.Add(i);
			}

			if (DailyResourceMax == float.MinValue) //means that all roots are in incmplete list
			{
				DailyResourceMax = 1;
				DailyProductionMax = 1;
			}

			//This happens during a dry period
			// Debug.Assert(DailyProductionMax > 0f);
			// Debug.Assert(DailyResourceMax > 0);

			foreach(var i in NewDayIncomplete)
				src[i].DailySet(DailyResourceMax, DailyProductionMax, 0f);
		}

		// switch (Efficiencies.Count.CompareTo(dst.Length))
		// {
		// 	case -1: Efficiencies.AddRange(new GatherEfficiency[dst.Length - Efficiencies.Count]); break;
		// 	case 1: Efficiencies.RemoveRange(dst.Length, Efficiencies.Count - dst.Length); break;
		// }

		// for(int i = 0; i < dst.Length; ++i)
		// 	Efficiencies[i] = new(dst[i].PreviousDayEnvResourcesInv / DailyResourceMax, dst[i].PreviousDayProductionInv / DailyProductionMax);
	}

	public void FirstDay()
	{
		var src = Src();

		for(int i = 0; i < src.Length; ++i)
		{
			if (DailyResourceMax < src[i].PreviousDayEnvResourcesInvariant) DailyResourceMax = src[i].PreviousDayEnvResourcesInvariant;
			if (DailyProductionMax < src[i].PreviousDayProductionInvariant) DailyProductionMax = src[i].PreviousDayProductionInvariant;
		}

		// switch (Efficiencies.Count.CompareTo(src.Length))
		// {
		// 	case -1: Efficiencies.AddRange(new GatherEfficiency[src.Length - Efficiencies.Count]); break;
		// 	case 1: Efficiencies.RemoveRange(src.Length, Efficiencies.Count - src.Length); break;
		// }

		// for(int i = 0; i < src.Length; ++i)
		// 	Efficiencies[i] = new(src[i].PreviousDayEnvResourcesInv / DailyResourceMax, src[i].PreviousDayProductionInv / DailyProductionMax);
	}

	///////////////////////////
	#region glTF EXPORT
	///////////////////////////
	/*
	public List<Node> ExportToGLTF()
	{
		var src = Src();
		var nodes = new List<Node>(src.Length);
		for(int i = 0; i < src.Length; ++i)
		{
			var baseCenter = GetBaseCenterWorld(i);
			nodes[i] = new()
			{
				Name = $"{GetOrgan(i)}_{i}",
				Mesh = 0,
				Rotation = GetDirection(i).ToArray(),
				Translation = baseCenter.ToArray(),
				Scale = null
			};
		}
		return nodes;
	}
	*/
	#endregion

	#if DEBUG
	readonly struct DebugTreeData
	{
		public readonly int Index;
		public readonly int Offset;
		public readonly bool NewLine;
		public DebugTreeData(int index, int offset, bool newLine)
		{
			Index = index;
			Offset = offset;
			NewLine = newLine;
		}
		public static int NumStrLength(int number) => number < 10 ? 1 : (int)Math.Ceiling(MathF.Log10(number + 0.1f));
	}

	/// <summary>
	///
	/// </summary>
	/*
	Usage:
	#if DEBUG
		Console.WriteLine(DebugTreePrint(src));
	#endif
	*/
	public static string DebugTreePrint(T[] tree)
	{
		var sb = new System.Text.StringBuilder();
		var stack = new Stack<DebugTreeData>();


		var children = new List<DebugTreeData>();
		var firstChild = true;
		for(int i = 0; i < tree.Length; ++i)
			if (tree[i].Parent == -1)
			{
				children.Add(new(i, 0, !firstChild));
				firstChild = false;
			}

		for(int i = children.Count - 1; i >= 0; --i) //reverse push
			stack.Push(children[i]);

		const string rootStr = "(: > ";

		while (stack.Count > 0)
		{
			var data = stack.Pop();
			var offset = data.Offset;
			if (data.NewLine)
			{
				sb.AppendLine();
				for(int i = 0; i < data.Offset; ++i)
					sb.Append(' ');
			}

			if (data.Offset == 0)
			{
				sb.Append($"{rootStr}{data.Index}");
				offset += rootStr.Length + DebugTreeData.NumStrLength(data.Index);
			}
			else
			{
				sb.Append($" > {data.Index}");
				offset += 3 + DebugTreeData.NumStrLength(data.Index);
			}

			children.Clear();
			firstChild = true;
			for(int i = 0; i < tree.Length; ++i)
				if (tree[i].Parent == data.Index)
				{
					children.Add(new(i, offset, !firstChild));
					firstChild = false;
				}

			for(int i = children.Count - 1; i >= 0; --i) //reverse push
				stack.Push(children[i]);
		}
		return sb.ToString();
	}
#endif
}
