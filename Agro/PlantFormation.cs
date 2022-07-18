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

public partial class PlantFormation : IFormation
{
	public Vector3 Position { get; private set; }
	bool ReadTMP = false;
	internal SoilFormation Soil;
	protected SeedAgent[] Seed = new SeedAgent[1]; //must be an array due to messaging compaatibility
	protected readonly SeedAgent[] SeedTMP = new SeedAgent[1];
	protected readonly PostBox<SeedAgent> PostboxSeed = new();
	bool DeathSeed = false;

	public readonly PlantSubFormation<UnderGroundAgent> UG;
	public readonly PlantSubFormation<AboveGroundAgent> AG;


	internal const float RootSegmentLength = 0.1f;

	internal readonly Vector2 VegetativeLowTemperature = new(10, 15);
	internal readonly Vector2 VegetativeHighTemperature = new(35, 40);

	/// <summary>
	/// Random numbers generator
	/// </summary>
	internal Pcg RNG;

	public PlantFormation(SoilFormation soil, SeedAgent seed, Pcg parentRNG)
	{
		Soil = soil;
		Seed[0] = seed;
		Position = seed.Center;

		RNG = parentRNG.NextRNG();

		UG = new(this, UnderGroundAgent.Reindex
#if GODOT
		, i => UG_Godot.RemoveSprite(i), i => UG_Godot.AddSprite(i)
#endif
		);

		AG = new(this, AboveGroundAgent.Reindex
#if GODOT
		, i => AG_Godot.RemoveSprite(i), i => AG_Godot.AddSprite(i)
#endif
);
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

	public bool Send(int recipient, IMessage<UnderGroundAgent> msg) => UG.SendProtected(recipient, msg);

	public bool Send(int recipient, IMessage<AboveGroundAgent> msg) => AG.SendProtected(recipient, msg);

	public void SeedDeath()
	{
		DeathSeed = true;
		Seed = Array.Empty<SeedAgent>();
	}

	public void Census()
	{
		UG.Census();
		AG.Census();
	}

	public void Tick(SimulationWorld world, uint timestep)
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
			dstSeed[0].Tick(world, this, 0, timestep);
		}
		#if HISTORY_LOG
		if (dstSeed.Length > 0)
			StatesHistory.Add(dstSeed[0]);
		#endif
		ReadTMP = !ReadTMP;

		UG.Tick(world, timestep);
		AG.Tick(world, timestep);

		//Just testing
		var gltf = GlftHelper.Create(AG.ExportToGLTF());
		GlftHelper.Export(gltf, $"T{timestep}.gltf");
	}

	public void DeliverPost(uint timestep)
	{
		if (Seed.Length > 0)
		{
			var (src, dst) = SrcDst_Seed();
			Array.Copy(src, dst, src.Length);
			PostboxSeed.Process(timestep, dst);
		}
		ReadTMP = !ReadTMP;

		UG.DeliverPost(timestep);
		AG.DeliverPost(timestep);

		// if (!DeathSeed)
		// 	Console.WriteLine("R: {0} E: {1}", Seed[0].Radius, Seed[0].StoredEnergy);
		// else if (UnderGround.Length > 0)
		// 	Console.WriteLine("R: {0}x{1} E: {2} W: {3}", UnderGround[0].Radius, UnderGround[0].Length, UnderGround[0].Energy, UnderGround[0].Water);
	}

	public bool HasUndeliveredPost => PostboxSeed.AnyMessages || UG.AnyMessages || AG.AnyMessages;

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG
	List<SeedAgent> StatesHistory = new();
	public string HistoryToJSON() => $" \"Seeds\" : {Utils.Export.Json(StatesHistory)}, \"UnderGround\" : {UG.HistoryToJSON()}, AboveGround : {AG.HistoryToJSON()}";
	public ulong GetID() => Seed.Length > 0 ? Seed[0].ID : ulong.MaxValue;
	#endif
	#endregion

	///////////////////////////
	#region glTF EXPORT
	///////////////////////////
	public List<Node> ExportToGLTF() => AG.ExportToGLTF();
	#endregion
}
