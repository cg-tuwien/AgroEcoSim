using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AgentsSystem;
using Utils;

namespace Agro;

[Flags]
public enum SeasonalPhase : byte { PreFlower = 1, Flowering = 2, PostFlower = 4, ResetPending = 8};

public partial class PlantFormation2 : IPlantFormation
{
	internal readonly AgroWorld World;

	public Vector3 Position { get; private init; }
	public int SoilIndex { get; private init; }
	bool ReadTMP = false;
	internal ISoilFormation Soil;
	protected SeedAgent[] Seed = new SeedAgent[1]; //must be an array due to messaging compaatibility
	protected readonly SeedAgent[] SeedTMP = new SeedAgent[1];
	protected readonly PostBox<SeedAgent> PostboxSeed = new();
	bool DeathSeed = false;

	//public readonly PlantSubFormation<UnderGroundAgent> UG;
	public readonly IPlantSubFormation<UnderGroundAgent> UG;
	public readonly PlantSubFormation<AboveGroundAgent> AG;

	//public readonly List<Quaternion> SegmentOrientations;

	internal const float RootSegmentLength = 0.05f;

	internal readonly Vector2 VegetativeLowTemperature = new(10, 15);
	internal readonly Vector2 VegetativeHighTemperature = new(35, 40);

	internal float WaterBalancePrev = 0f;
	/// <summary>
	/// The ratio of available water to the required water. If > 1, there is more production than need.
	/// </summary>
	internal float WaterBalance = 0f;
	/// <summary>
	/// The ratio of available water to the required water, adapted for the needs of underground agents.
	/// </summary>
	internal float WaterBalanceUG = 0f;
	/// <summary>
	/// Hydrated seed water and other water not yet held by organ tissue. The
	/// census releases it as anatomical capacity grows; roots do not draw more
	/// soil water while this reserve remains. This is counted exactly once in
	/// the plant water budget and is not an organ's storage capacity.
	/// </summary>
	internal double UnallocatedWater_g { get; private set; }
	// The previous census' biological water demand, retained for the next root
	// tick so tiny rounding remnants do not suppress genuine soil uptake.
	internal double WaterDemandPerTick_g { get; private set; }

	/// <summary>
	/// The ratio of available energy to the required energy. If > 1, there is more production than need.
	/// </summary>
	internal float EnergyBalance = 0f;

	// internal float WaterProductionMax = 0f;
	internal float EnergyProductionMax = 0f;

	/// <summary>
	/// Random numbers generator
	/// </summary>
	internal Pcg RNG;

	/// <summary>
	/// Species settings
	/// </summary>
	public SpeciesSettings Parameters { get; private set; }
	internal readonly PlantTickProbabilities TickProbabilities;

	public PlantFormation2(AgroWorld world, SpeciesSettings parameters, ISoilFormation soil, SeedAgent seed, Pcg parentRNG, int hoursPerTick)
	{
		World = world;
		Parameters = parameters ?? SpeciesSettings.Default;
        Console.WriteLine($"PlantFormation2 ctor: {Parameters.Name}, BudLength: {Parameters.FlowerSettings.BudLength}");
        Parameters.Init(hoursPerTick);
		TickProbabilities = new(Parameters, world.HoursPerTick);
        Console.WriteLine($"PlantFormation2 after Init: {Parameters.Name}, BudLength: {Parameters.FlowerSettings.BudLength}");
        Soil = soil;
		Seed[0] = seed;
		SoilIndex = seed.SoilIndex;
		Position = seed.Center;

		RNG = parentRNG.NextRNG();
		UG = world.VirtualRoots ? new VirtualRootsFormation(this) : new PlantSubFormation<UnderGroundAgent>(this, UnderGroundAgent.Reindex, false);
		AG = new(this, AboveGroundAgent.Reindex, true);
		//SegmentOrientations = new();
	}

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(SeedAgent[], SeedAgent[]) SrcDst_Seed() => ReadTMP ? (SeedTMP, Seed) : (Seed, SeedTMP);

	public bool Send(int recipient, IMessage<SeedAgent> msg)
	{
		if (Seed.Length > 0 && Seed.Length > recipient)
		{
			lock(PostboxSeed) PostboxSeed.Add(new (msg, recipient));
			return true;
		}
		else
			return false;
	}

	// public bool Send(int recipient, IMessage<UnderGroundAgent> msg) => UG.SendProtected(recipient, msg);
	// public bool Send(int recipient, IMessage<AboveGroundAgent> msg) => AG.SendProtected(recipient, msg);

	public void SeedDeath()
	{
		DeathSeed = true;
		Seed = [];

		//initial setup of default efficiencies
		//Do not call this.Census() as it would also start the gathering phase
		UG.Census();
		AG.Census();
		UG.FirstDay();
		AG.FirstDay();
	}

	public bool SeedAlive => Seed.Length == 1;

	//--- checkpointing ------------------------------------------------------------------------

	/// <summary>
	/// Writes everything about this plant that the next step depends on.
	///
	/// Position, soil index and species are NOT here: they come from the request, and a resumed
	/// world is rebuilt from that same request before this is read back. What is here is what the
	/// simulation has changed since - the balances it carries between steps, the generator that
	/// decides its next branch, its seed, and its organs.
	/// </summary>
	public void WriteState(BinaryWriter writer)
	{
		writer.Write(SeedAlive);
		if (SeedAlive) writer.Write(MemoryMarshal.AsBytes(SrcDst_Seed().Item1.AsSpan(0, 1)));
		writer.Write(DeathSeed);

		writer.Write(WaterBalancePrev);
		writer.Write(WaterBalance);
		writer.Write(WaterBalanceUG);
		writer.Write(EnergyBalance);
		writer.Write(EnergyProductionMax);
		writer.Write(UnallocatedWater_g);
		writer.Write(WaterDemandPerTick_g);

		//The per-plant generator. Reseeding instead would carry the same plants on with different
		//random decisions - a different experiment that looks like a continuation.
		var (state, increment) = RNG.GetState();
		writer.Write(state);
		writer.Write(increment);

		AG.WriteState(writer);
		UG.WriteState(writer);
	}

	public void ReadState(BinaryReader reader)
	{
		ReadTMP = false;
		var seedAlive = reader.ReadBoolean();
		if (seedAlive)
		{
			var bytes = reader.ReadBytes(Unsafe.SizeOf<SeedAgent>());
			if (bytes.Length != Unsafe.SizeOf<SeedAgent>())
				throw new EndOfStreamException("The checkpoint ended inside a seed.");
			// A caller may rewind a world that has already germinated.
			if (Seed.Length != 1) Seed = new SeedAgent[1];
			bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(Seed.AsSpan(0, 1)));
		}
		else if (Seed.Length == 1)
			//Germinated before the checkpoint: the seed is gone and the organs carry the plant.
			Seed = [];
		DeathSeed = reader.ReadBoolean();

		WaterBalancePrev = reader.ReadSingle();
		WaterBalance = reader.ReadSingle();
		WaterBalanceUG = reader.ReadSingle();
		EnergyBalance = reader.ReadSingle();
		EnergyProductionMax = reader.ReadSingle();
		UnallocatedWater_g = reader.ReadDouble();
		WaterDemandPerTick_g = reader.ReadDouble();
		if (!double.IsFinite(UnallocatedWater_g) || UnallocatedWater_g < 0d ||
			!double.IsFinite(WaterDemandPerTick_g) || WaterDemandPerTick_g < 0d)
			throw new InvalidDataException("The checkpoint contains an invalid plant water budget.");

		RNG.SetState(reader.ReadUInt64(), reader.ReadUInt64());

		AG.ReadState(reader);
		UG.ReadState(reader);
	}

	// A seven-day reserve (or one full tick for unusually long intervals), in
	// the same model energy units as the pool. Also used when removing demand.
	internal static double MaintenanceReserve(double requirementPerTick, ushort hoursPerTick)
		=> requirementPerTick * Math.Max(1d, 168d / hoursPerTick);

	public void Census()
	{
		UG.Census();
		AG.Census();

		if (Seed.Length == 0 && (UG.Alive || AG.Alive))
		{
			var timestep = World.Timestep;

			if (timestep % World.StatsBlockLength == 0) //A New Day Just Begun
			{
				UG.NewDay(World.Timestep, World.TicksPerDay);
				AG.NewDay(World.Timestep, World.TicksPerDay);
			}

			var ugGathered = UG.Gather();
			var agGathered = AG.Gather();

			var energy = agGathered.Energy + ugGathered.Energy;
			var water = agGathered.Water + ugGathered.Water + UnallocatedWater_g;

			var energyRequirement = agGathered.EnergyRequirementPerTick + ugGathered.EnergyRequirementPerTick;
			var waterRequirement = agGathered.WaterRequirementPerTick + ugGathered.WaterRequirementPerTick;
			WaterDemandPerTick_g = Math.Max(0d, waterRequirement);

			var energyStorage = agGathered.EnergyCapacity + ugGathered.EnergyCapacity;
			var waterStorage = Math.Max(0d, agGathered.WaterCapacity + ugGathered.WaterCapacity);
			var waterPriority = Math.Clamp(
				agGathered.WaterPriorityPerTick + ugGathered.WaterPriorityPerTick, 0d, waterStorage);

			// Seven days of maintenance, independent of the biological tick duration.
			// energyRequirement is already integrated over this tick; divide only once.
			var energyEmergencyThrehold = MaintenanceReserve(energyRequirement, World.HoursPerTick);
			var energAlertThreshold = Math.Max(energyEmergencyThrehold * 4, 0.01 * energyStorage);

			const double zero = 1e-12;

			//Debug.WriteLine($"W: {water} / {waterRequirement}   {globalUG.WaterDiff} + {globalAG.WaterDiff}");
			//Debug.WriteLine($"E: {energy} / {energyRequirement}   {globalUG.EnergyDiff} + {globalAG.EnergyDiff}");
			if (energy < energyEmergencyThrehold)
			{
				//Debug.WriteLine("E DIST: req!!!");
				var count = agGathered.Count;
				var ordering = new List<(float energyRequired, int index)>(count);
				int i = 0;
				for(; i < count; ++i)
					ordering.Add((agGathered[i].ResourcesEfficiency, i));
				ordering.Sort((x, y) => x.energyRequired.CompareTo(y.energyRequired));
				var target = energyEmergencyThrehold - energy;
				i = 0;
				var removed = new HashSet<int>();
				while (target > 0 && i < ordering.Count)
				{
					var index = ordering[i++].index;
					if (Parameters.RetainDormantRhizomes && AG.GetIsRizome(index))
						continue;
					if (!removed.Contains(index))
					{
						target -= MaintenanceReserve(agGathered[index].LifesupportEnergy, World.HoursPerTick);
						removed.Add(index);
						var deaths = AG.Death(index, reason: OrganDeathReason.Resources);
						if (deaths != null)
							foreach(var item in deaths)
								if (!removed.Contains(item))
								{
									target -= MaintenanceReserve(agGathered[item].LifesupportEnergy, World.HoursPerTick);
									removed.Add(item);
								}
					}
				}
				Debug.Write($"Emergency removal of {removed.Count} plant agents.");

				var weights = agGathered.Weights4EnergyDistributionByRequirement() +ugGathered.Weights4EnergyDistributionByRequirement();
				var factor = (float)(energy / (weights >= zero ? weights : 1));
				agGathered.DistributeEnergyByRequirement(factor);
				ugGathered.DistributeEnergyByRequirement(factor);
			}
			else if (energy < energAlertThreshold || energyStorage < energyRequirement) //the plant is short on energy, it must be distributed
			{
				//Debug.WriteLine("E DIST: req");
				// In the construction-accounted mode this is only an early warning:
				// the pool can still cover many ticks of maintenance. Killing a low-
				// efficiency support here recursively removes an otherwise lit crown.
				// Prioritize maintenance with requirement-weighted distribution;
				// construction still pays from the conserved pool. Retain shedding
				// for the genuine emergency below.
				if (Parameters.ConstructionEnergyPerDryGram <= 0f)
				{
					var min = float.MaxValue;
					var minList = new List<int>();
					var count = agGathered.Count;
					for(int i = 0; i < count; ++i)
					{
						// A clonal perennial may shed leaves and aerial shoots under
						// stress, but pruning a rhizome deletes every descendant crown.
						if (Parameters.RetainDormantRhizomes && AG.GetIsRizome(i))
							continue;
						switch (agGathered[i].ResourcesEfficiency.CompareTo(min))
						{
							case -1:
								min = agGathered[i].ResourcesEfficiency;
								minList.Clear();
								minList.Add(i);
								break;
							case 0: minList.Add(i); break;
						}
					}
					foreach(var item in minList)
						AG.Death(item, reason: OrganDeathReason.Resources);
				}

				var weights = agGathered.Weights4EnergyDistributionByRequirement() + ugGathered.Weights4EnergyDistributionByRequirement();
				var factor = (float)(energy / (weights >= zero ? weights : 1));
				agGathered.DistributeEnergyByRequirement(factor);
				ugGathered.DistributeEnergyByRequirement(factor);
			}
			else //there is enough energy
			{
				//Debug.WriteLine("E DIST: storage");
				var energyOverhead = energy - energyRequirement;
				var weights = agGathered.Weights4EnergyDistributionByStorage() + ugGathered.Weights4EnergyDistributionByStorage();

				var factor = (float)(energyOverhead / (weights >= zero ? weights : 1));
				agGathered.DistributeEnergyByStorage(factor);
				ugGathered.DistributeEnergyByStorage(factor);
			}
			#if DEBUG
			var check = Math.Abs(energy - (agGathered.ReceivedEnergySum() + ugGathered.ReceivedEnergySum()));
			Debug.Assert(check <= energy * 1e-3);
			#endif

			// The forecast is a priority for distributing water *inside* tissue,
			// not additional capacity. If the hydrated seed supplied more water
			// than the newborn organs can hold, keep the excess in a separate
			// plant reserve and admit it as the organs expand.
			var allocatableWater = Math.Clamp(water, 0d, waterStorage);
			if (allocatableWater < waterPriority)
			{
				var factor = waterPriority > zero ? allocatableWater / waterPriority : 0d;
				agGathered.DistributeWaterByRequirement(factor);
				ugGathered.DistributeWaterByRequirement(factor);
			}
			else
			{
				var remainingCapacity = waterStorage - waterPriority;
				var factor = remainingCapacity > zero
					? (allocatableWater - waterPriority) / remainingCapacity : 1d;
				agGathered.DistributeWaterByStorage(factor);
				ugGathered.DistributeWaterByStorage(factor);
			}
			UnallocatedWater_g = Math.Max(0d, water -
				(agGathered.ReceivedWaterSum() + ugGathered.ReceivedWaterSum()));

			UG.Distribute(ugGathered);
			AG.Distribute(agGathered);

			// The unallocated reserve cannot be consumed by a leaf this tick;
			// count only water that fits in tissue when reporting water sufficiency.
			WaterBalance = waterRequirement > 1e-6
				? (float)(allocatableWater / waterRequirement) : 1;
			//WaterBalanceUG = WaterBalance * WaterBalance;
			//WaterBalanceUG = MathF.Pow(WaterBalance, 4f);
			if (WaterBalance < WaterBalancePrev && WaterBalance < 1f / (1.00001f - Parameters.RootsDensity)) //The constant actually steers the roots sparsity, maybe take over from the species parameter
			{
 				var trend = WaterBalancePrev / WaterBalance;
				WaterBalanceUG = Math.Min(trend, WaterBalance);
			}
			else
				WaterBalanceUG = WaterBalance;

			WaterBalancePrev = WaterBalance;
			WaterBalanceUG *= WaterBalanceUG;

			EnergyBalance = energyRequirement > 1e-6 ? (float)(energy / energyRequirement) : 1;
			//Debug.WriteLine($"{timestep}\twater: {WaterBalance}\tP_roots: {1f / WaterBalanceUG}\tenergy: {EnergyBalance}");

			//WaterProductionMax = UG.DailyProductionMax;
			EnergyProductionMax = AG.DailyProductionMax;

			//take care to avoid division by zero
			//if (WaterProductionMax < 1e-6f) WaterProductionMax = 1f;
			if (EnergyProductionMax < 1e-6f) EnergyProductionMax = 1f;
		}
	}

	public void Tick(uint timestep)
	{
		if (DeathSeed && !UG.Alive && !AG.Alive)
			return;

		//Ready for List and Span combination

		// if (RootsDeaths.Count > 0)
		// {
		//     RootsDeaths.Sort();
		//     for(int i = 1; i < RootsDeaths.Count; ++i)
		//         for(int r = RootsDeaths[i - 1]; r < RootsDeaths[i]; ++r)
		//             Roots[r - i] = Roots[r];
		//     for(int r = RootsDeaths[^1]; r < Roots.Count; ++r)
		//         Roots[r - RootsDeaths.Count] = Roots[r];
		//     Roots.RemoveRange(Roots.Count - RootsDeaths.Count, RootsDeaths.Count); //todo can be done instead of the last for cycle
		//     //TODO reindex children if any deaths
		// }

		// if (RootsBirths.Count > 0)
		//     Roots.AddRange(RootsBirths);

		// if (StemsDeaths.Count > 0)
		// {
		//     StemsDeaths.Sort();
		//     for(int i = 1; i < StemsDeaths.Count; ++i)
		//         for(int r = StemsDeaths[i - 1]; r < StemsDeaths[i]; ++r)
		//             Stems[r - i] = Stems[r];
		//     for(int r = StemsDeaths[^1]; r < Stems.Count; ++r)
		//         Stems[r - StemsDeaths.Count] = Stems[r];
		//     Stems.RemoveRange(Stems.Count - StemsDeaths.Count, StemsDeaths.Count); //todo can be done instead of the last for cycle
		//     //TODO reindex children if any deaths
		// }

		// if (StemsBirths.Count > 0)
		//     Stems.AddRange(StemsBirths);

		var (srcSeed, dstSeed) = SrcDst_Seed();
		//We know there is just a single seed (if any)
		if (Seed.Length > 0)
		{
			Array.Copy(srcSeed, dstSeed, srcSeed.Length);
			dstSeed[0].Tick(this, 0, timestep);
		}
		else
		{
			UG.Tick(timestep);
			// for(int i = World.HoursPerTick - 1; i >= 0; --i)
			// {
			// 	UG.Tick(timestep);
			// 	if (i > 0) //attention the loop decrements so this will not run for the last item
			// 	{
			// 		Soil.ProcessRequests();
			// 		UG.DeliverPost(timestep);
			// 		UG.Census();
			// 	}
			// }

			AG.Tick(timestep);
			AG.Hormones(true);

			//Physics (work in progress)
			AG.Gravity();
		}

		ReadTMP = !ReadTMP;
	}

	public void DeliverPost(uint timestep)
	{
		if (Seed.Length > 0)
		{
			var (src, dst) = SrcDst_Seed();
			Array.Copy(src, dst, src.Length);
			PostboxSeed.Process(timestep, dst);
		}
		else if (PostboxSeed.AnyMessages)
			PostboxSeed.Clear();

		ReadTMP = !ReadTMP;

		if (UG.HasUndeliveredPost)
			UG.DeliverPost(timestep);
		if (AG.HasUndeliveredPost)
			AG.DeliverPost(timestep);

		// if (!DeathSeed)
		// 	Console.WriteLine("R: {0} E: {1}", Seed[0].Radius, Seed[0].StoredEnergy);
		// else if (UnderGround.Length > 0)
		// 	Console.WriteLine("R: {0}x{1} E: {2} W: {3}", UnderGround[0].Radius, UnderGround[0].Length, UnderGround[0].Energy, UnderGround[0].Water);
	}

	public (uint, uint, uint) GeometryStats()
	{
		uint triangles = 0, sensors = 0;
		for(int i = 0; i < AG.Count; ++i)
			switch (AG.GetOrgan(i))
			{
				case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem: triangles += 8; break;
				case OrganTypes.Bud: triangles += 4; break;
				case OrganTypes.Leaf: ++sensors; triangles += 2; break;
			}

		return ((uint)AG.Count, triangles, sensors);
	}

	static Quaternion UpsideDown = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);

	internal static Quaternion AdjustUpBase(Quaternion orientation, bool up)
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

		// return Quaternion.CreateFromRotationMatrix(new()
		// {
		// 	M11 = 0, M12 = 1, M13 = 0, M14 = 0,
		// 	M21 = y.X, M22 = y.Y, M23 = y.Z, M24 = 0,
		// 	M31 = z.X, M32 = z.Y, M33 = z.Z, M34 = 0,
		// 	M41 = 0, M42 = 0, M43 = 0, M44 = 1
		// });

		//slightly faster version given that the first row are constant values
		var trace = y.Y + z.Z;
		Quaternion result = new();

		if (trace > 0.0f)
		{
			var s = MathF.Sqrt(trace + 1.0f);
			result.W = s * 0.5f;
			s = 0.5f / s;
			result.X = (y.Z - z.Y) * s;
			result.Y = z.X * s;
			result.Z = (1f - y.X) * s;
		}
		else
		{
			if (0f >= y.Y && 0f >= z.Z)
			{
				var s = MathF.Sqrt(1.0f - y.Y - z.Z);
				var invS = 0.5f / s;
				result.X = 0.5f * s;
				result.Y = (1f + y.X) * invS;
				result.Z = z.X * invS;
				result.W = (y.Z - z.Y) * invS;
			}
			else if (y.Y > z.Z)
			{
				var s = MathF.Sqrt(1.0f + y.Y - z.Z);
				var invS = 0.5f / s;
				result.X = (y.X + 1f) * invS;
				result.Y = 0.5f * s;
				result.Z = (z.Y + y.Z) * invS;
				result.W = z.X * invS;
			}
			else
			{
				var s = MathF.Sqrt(1.0f + z.Z - y.Y);
				var invS = 0.5f / s;
				result.X = z.X * invS;
				result.Y = (z.Y + y.Z) * invS;
				result.Z = 0.5f * s;
				result.W = (1f - y.X) * invS;
			}
		}

		return up ? result : UpsideDown * result;
    }

	public bool HasUndeliveredPost => PostboxSeed.AnyMessages || UG.HasUndeliveredPost || AG.HasUndeliveredPost;// || NeedsGathering;
	public int Count => SeedAlive ? 1 : UG.Count + AG.Count;

	public void UGBirth(UnderGroundAgent agent)
	{
		if (World.VirtualRoots)
			(UG as VirtualRootsFormation).Birth(agent.Water_g, agent.Energy);
		else
			(UG as PlantSubFormation<UnderGroundAgent>).Birth(agent);
	}

	//public List<Node> ExportToGLTF() => AG.ExportToGLTF();

    internal uint GetAge_h()
    {
        if (AG != null && AG.Count > 0)
			return AG.GetAge_ticks(0, World.Timestep) * World.HoursPerTick;
		else
			return 0;
    }

    // internal int InsertSegments(byte segmentsCount, Quaternion orientation)
    // {
    //     var result = SegmentOrientations.Count;
    // 	for(int i = 0; i < segmentsCount; ++i)
    // 		SegmentOrientations.Add(orientation);

    // 	return result;
    // }
}
