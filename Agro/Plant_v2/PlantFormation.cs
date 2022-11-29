using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using glTFLoader.Schema;
using NumericHelpers;
using Utils;

namespace Agro;

public class PlantGlobalStats
{
	public double Energy { get; set; }
	public double Water { get; set; }

	public double EnergyDiff { get; set; }
	public double WaterDiff { get; set; }

	public double EnergyCapacity { get; set; }
	public double WaterCapacity { get; set; }

	public double EnergyRequirement{ get; set; }
	public double WaterRequirement { get; set; }

	public double UsefulnessTotal { get; set; }

	public IList<float> Usefulness { get; set; }
	public IList<float> LifeSupportEnergy { get; set; }
	public IList<float> PhotosynthWater { get; set; }
	public IList<float> EnergyCapacities { get; set; }
	public IList<float> WaterCapacities { get; set; }

	public float[]? ReceivedEnergy;
	public float[]? ReceivedWater;

	internal void DistributeEnergyByRequirement(float factor)
	{
		//factor is energyAvailableTotal / energyRequirementTotal
		var weightsTotal = 0.0;
		ReceivedEnergy = new float[LifeSupportEnergy.Count];
		var positiveUsefulness = false;
		for(int i = 0; i < ReceivedEnergy.Length; ++i)
			if (Usefulness[i] > 0f)
			{
				positiveUsefulness = true;
				break;
			}

		if (positiveUsefulness && factor > 0f)
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
			{
				var w = LifeSupportEnergy[i] * Usefulness[i];
				ReceivedEnergy[i] = w * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
				weightsTotal += w;
			}

			var wtf = (float)weightsTotal;
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] /= wtf;
		}
		else
		{
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = LifeSupportEnergy[i] * factor; //in sum over all i: LifeSupportEnergy[i] / energyRequirementTotal yields 1
		}
	}

	internal void DistributeWaterByRequirement(float factor)
	{
		ReceivedWater = new float[PhotosynthWater.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = PhotosynthWater[i] * factor;
	}

	internal double Weights4EnergyDistributionByStorage(bool positiveEfficiency)
	{
		var weightsTotal = 0.0;
		if (positiveEfficiency)
			for(int i = 0; i < Usefulness.Count; ++i)
				weightsTotal += (EnergyCapacities[i] - LifeSupportEnergy[i]) * Usefulness[i];
		else
			for(int i = 0; i < Usefulness.Count; ++i)
				weightsTotal += EnergyCapacities[i] - LifeSupportEnergy[i];

		return weightsTotal;
	}

	internal void DistributeEnergyByStorage(float factor, bool positiveEfficiency)
	{
		ReceivedEnergy = new float[LifeSupportEnergy.Count];
		if (positiveEfficiency)
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
			{
				var w = (EnergyCapacities[i] - LifeSupportEnergy[i]) * Usefulness[i];
				ReceivedEnergy[i] = LifeSupportEnergy[i] + w * factor;
			}
		else
			for(int i = 0; i < ReceivedEnergy.Length; ++i)
				ReceivedEnergy[i] = LifeSupportEnergy[i] + (EnergyCapacities[i] - LifeSupportEnergy[i]) * factor;
	}

	internal void DistributeWaterByStorage(float factor)
	{
		ReceivedWater = new float[PhotosynthWater.Count];
		for(int i = 0; i < ReceivedWater.Length; ++i)
			ReceivedWater[i] = PhotosynthWater[i] + (WaterCapacities[i] - PhotosynthWater[i]) * factor;
	}
}

public partial class PlantFormation2 : IPlantFormation
{
	#if !GODOT
	[System.Text.Json.Serialization.JsonIgnore]
	#else
	[Newtonsoft.Json.JsonIgnore]
	#endif
	public byte Stages => 1;

	[Newtonsoft.Json.JsonIgnore]public Vector3 Position { get; private set; }
	[Newtonsoft.Json.JsonIgnore]bool ReadTMP = false;
	[Newtonsoft.Json.JsonIgnore] internal SoilFormation Soil;
	[Newtonsoft.Json.JsonIgnore]protected SeedAgent[] Seed = new SeedAgent[1]; //must be an array due to messaging compaatibility
	[Newtonsoft.Json.JsonIgnore]protected readonly SeedAgent[] SeedTMP = new SeedAgent[1];
	[Newtonsoft.Json.JsonIgnore]protected readonly PostBox<SeedAgent> PostboxSeed = new();
	[Newtonsoft.Json.JsonIgnore]bool DeathSeed = false;

	[Newtonsoft.Json.JsonIgnore]public readonly PlantSubFormation2<UnderGroundAgent2> UG;
	[Newtonsoft.Json.JsonIgnore]public readonly PlantSubFormation2<AboveGroundAgent2> AG;


	internal const float RootSegmentLength = 0.1f;

	[Newtonsoft.Json.JsonIgnore]internal readonly Vector2 VegetativeLowTemperature = new(10, 15);
	[Newtonsoft.Json.JsonIgnore]internal readonly Vector2 VegetativeHighTemperature = new(35, 40);

	/// <summary>
	/// Random numbers generator
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]internal Pcg RNG;

	/// <summary>
	/// Species settings
	/// </summary>
	[Newtonsoft.Json.JsonIgnore]public PlantSettings Parameters { get; private set; }

	public PlantFormation2(PlantSettings parameters, SoilFormation soil, SeedAgent seed, Pcg parentRNG)
	{
		Parameters = parameters;
		Soil = soil;
		Seed[0] = seed;
		Position = seed.Center;

		RNG = parentRNG.NextRNG();

		UG = new(this, UnderGroundAgent2.Reindex
#if GODOT
		, i => UG_Godot.RemoveSprite(i), i => UG_Godot.AddSprites(i)
#endif
		);

		AG = new(this, AboveGroundAgent2.Reindex
#if GODOT
		, i => AG_Godot.RemoveSprite(i), i => AG_Godot.AddSprites(i)
#endif
);
		Parameters = PlantSettings.Avocado;
	}

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(SeedAgent[], SeedAgent[]) SrcDst_Seed() => ReadTMP ? (SeedTMP, Seed) : (Seed, SeedTMP);

	// public bool Send(int dst, IMessage<SoilAgent> msg)
	// {
	//     if (dst < Roots.Length)
	//     {
	//         PostboxRoots.Add(new (msg, dst));
	//         return true;
	//     }
	//     else
	//         return false;
	// }

	public bool Send(int recipient, IMessage<SeedAgent> msg)
	{
		if (Seed.Length > 0 && Seed.Length > recipient)
		{
			PostboxSeed.Add(new (msg, recipient));
			return true;
		}
		else
			return false;
	}

	public bool Send(int recipient, IMessage<UnderGroundAgent2> msg) => UG.SendProtected(recipient, msg);

	public bool Send(int recipient, IMessage<AboveGroundAgent2> msg) => AG.SendProtected(recipient, msg);

	public bool TransactionAG(int srcIndex, int dstIndex, PlantSubstances substance, float amount) => AG.SendProtected(srcIndex, dstIndex, substance, amount);
	public bool TransactionUG(int srcIndex, int dstIndex, PlantSubstances substance, float amount) => UG.SendProtected(srcIndex, dstIndex, substance, amount);

	public bool Transaction(bool ag, int srcIndex, int dstIndex, PlantSubstances substance, float amount) =>
		ag ? AG.SendProtected(srcIndex, dstIndex, substance, amount) : UG.SendProtected(srcIndex, dstIndex, substance, amount);

	public void SeedDeath()
	{
		DeathSeed = true;
		Seed = Array.Empty<SeedAgent>();
	}

	[Newtonsoft.Json.JsonIgnore] public bool SeedAlive => Seed.Length == 1;

	public void Census()
	{
		UG.Census();
		AG.Census();
	}

	public void Tick(SimulationWorld world, uint timestep, byte stage)
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
			dstSeed[0].Tick(world, this, 0, timestep, stage);
		}
		else
		{
			UG.Tick(world, timestep, stage);
			AG.Tick(world, timestep, stage);

			var globalUG = UG.Gather();
			var globalAG = AG.Gather();

			var energy = globalAG.Energy + globalUG.Energy;
			var water = globalAG.Water + globalUG.Energy;

			var energyRequirement = globalAG.EnergyRequirement + globalUG.EnergyRequirement;
			var waterRequirement = globalAG.WaterRequirement + globalUG.WaterRequirement;

			var energyStorage = globalAG.EnergyCapacity + globalUG.EnergyCapacity;
			var waterStorage = globalAG.WaterCapacity + globalUG.WaterCapacity;

			var positiveEfficiencyAG = globalAG.UsefulnessTotal > 0.0;
			var positiveEfficiencyUG = globalUG.UsefulnessTotal > 0.0;
			#if GODOT
			UG.Efficiency = globalUG.Usefulness;
			AG.Efficiency = globalAG.Usefulness;
			#endif

			//Debug.WriteLine($"W: {water} / {waterRequirement}   {globalUG.WaterDiff} + {globalAG.WaterDiff}");
			//Debug.WriteLine($"E: {energy} / {energyRequirement}   {globalUG.EnergyDiff} + {globalAG.EnergyDiff}");

			if (energy < energyRequirement || energyStorage < energyRequirement) //the plant is short on energy, it must be distributed
			{
				var factor = (float)(energy / energyRequirement);
				globalAG.DistributeEnergyByRequirement(factor);
				globalUG.DistributeEnergyByRequirement(factor);
			}
			else //there is enough energy
			{
				var energyOverhead = energy - energyRequirement;
				var weights = globalAG.Weights4EnergyDistributionByStorage(positiveEfficiencyAG) + globalUG.Weights4EnergyDistributionByStorage(positiveEfficiencyUG);

				var factor = (float)(energyOverhead / weights);
				globalAG.DistributeEnergyByStorage(factor, positiveEfficiencyAG);
				globalUG.DistributeEnergyByStorage(factor, positiveEfficiencyUG);

				//check
				var check = Math.Abs(energy - (globalAG.ReceivedEnergy.Sum() + globalUG.ReceivedEnergy.Sum()));
				Debug.Assert(check < energy * 1e-4);
			}

			if (water < waterRequirement || waterStorage < waterRequirement)
			{
				var factor = (float)(water / waterRequirement);
				globalAG.DistributeWaterByRequirement(factor);
				globalUG.DistributeWaterByRequirement(factor);
			}
			else
			{
				var waterOverhead = water - waterRequirement;

				var factor = (float)(waterOverhead / (waterStorage - waterRequirement));
				globalAG.DistributeWaterByStorage(factor);
				globalUG.DistributeWaterByStorage(factor);
			}

			UG.Distribute(globalUG);
			AG.Distribute(globalAG);
		}
		#if TICK_LOG
		StatesHistory.Clear();
		#endif
		#if HISTORY_LOG || TICK_LOG
		if (dstSeed.Length > 0)
			StatesHistory.Add(dstSeed[0]);
		else
			StatesHistory.Add(null);
		#endif
		ReadTMP = !ReadTMP;

		//Just testing
		// var gltf = GlftHelper.Create(AG.ExportToGLTF());
		// GlftHelper.Export(gltf, $"T{timestep}.gltf");
	}

	public void DeliverPost(uint timestep, byte stage)
	{
		if (Seed.Length > 0)
		{
			var (src, dst) = SrcDst_Seed();
			Array.Copy(src, dst, src.Length);
			PostboxSeed.Process(timestep, stage, dst);
		}
		ReadTMP = !ReadTMP;

		if (UG.HasUndeliveredPost)
			UG.DeliverPost(timestep, stage);
		if (AG.HasUndeliveredPost)
			AG.DeliverPost(timestep, stage);

		// if (!DeathSeed)
		// 	Console.WriteLine("R: {0} E: {1}", Seed[0].Radius, Seed[0].StoredEnergy);
		// else if (UnderGround.Length > 0)
		// 	Console.WriteLine("R: {0}x{1} E: {2} W: {3}", UnderGround[0].Radius, UnderGround[0].Length, UnderGround[0].Energy, UnderGround[0].Water);
	}

	public void ProcessTransactions(uint timestep, byte stage)
	{
		if (UG.HasUnprocessedTransactions)
			UG.ProcessTransactions(timestep, stage);
		if (AG.HasUnprocessedTransactions)
			AG.ProcessTransactions(timestep, stage);
	}

	[Newtonsoft.Json.JsonIgnore] public bool HasUndeliveredPost => PostboxSeed.AnyMessages || UG.HasUndeliveredPost || AG.HasUndeliveredPost;

	[Newtonsoft.Json.JsonIgnore] public bool HasUnprocessedTransactions => UG.HasUnprocessedTransactions || AG.HasUnprocessedTransactions;

	[Newtonsoft.Json.JsonIgnore]public int Count => SeedAlive ? 1 : UG.Count + AG.Count;

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG || TICK_LOG
	[Newtonsoft.Json.JsonIgnore] readonly List<SeedAgent?> StatesHistory = new();
	public string HistoryToJSON(int timestep = -1, byte stage = 0) => timestep >= 0
		? $"{{ \"Seeds\" : {Export.Json(StatesHistory[timestep])}, \"UnderGround\" : {UG.HistoryToJSON(timestep)}, \"AboveGround\" : {AG.HistoryToJSON(timestep)} }}"
		: $"{{ \"Seeds\" : {Export.Json(StatesHistory)}, \"UnderGround\" : {UG.HistoryToJSON()}, \"AboveGround\" : {AG.HistoryToJSON()} }}";
	public ulong GetID() => Seed.Length > 0 ? Seed[0].ID : ulong.MaxValue;
	#endif
	#endregion

	///////////////////////////
	#region glTF EXPORT
	///////////////////////////
	public List<Node> ExportToGLTF() => AG.ExportToGLTF();
	#endregion
}
