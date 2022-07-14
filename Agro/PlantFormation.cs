using System;
using System.Collections.Generic;
using System.Numerics;
using AgentsSystem;
using Utils;

namespace Agro;

public partial class PlantFormation : IFormation
{
	Vector3 Position;
	bool ReadTMP = false;
	internal SoilFormation Soil;
	protected SeedAgent[] Seed = new SeedAgent[1]; //must be an array due to messaging compaatibility
	protected readonly SeedAgent[] SeedTMP = new SeedAgent[1];
	protected readonly PostBox<SeedAgent> PostboxSeed = new();
	protected UnderGroundAgent[] UnderGround = Array.Empty<UnderGroundAgent>();
	protected UnderGroundAgent[] UnderGroundTMP = Array.Empty<UnderGroundAgent>();
	protected readonly PostBox<UnderGroundAgent> PostboxUnderGround = new();
	
	//Once GODOT supports C# 6.0: Make it a List and then for processing send System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Stems);
	protected AboveGroundAgent[] AboveGround = Array.Empty<AboveGroundAgent>();
	protected AboveGroundAgent[] AboveGroundTMP = Array.Empty<AboveGroundAgent>();
	protected readonly PostBox<AboveGroundAgent> PostboxAboveGround = new();

	protected List<UnderGroundAgent> UnderGroundBirths = new();
	protected List<AboveGroundAgent> AboveGroundBirths = new();
	protected List<int> UnderGroundDeaths = new();
	protected List<int> AboveGroundDeaths = new();
	bool DeathSeed = false;

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
	}

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(SeedAgent[], SeedAgent[]) SrcDst_Seed() => ReadTMP ? (SeedTMP, Seed) : (Seed, SeedTMP);

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(UnderGroundAgent[], UnderGroundAgent[]) SrcDst_UG() => ReadTMP ? (UnderGroundTMP, UnderGround) : (UnderGround, UnderGroundTMP);

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(AboveGroundAgent[], AboveGroundAgent[]) SrcDst_AG() => ReadTMP ? (AboveGroundTMP, AboveGround) : (AboveGround, AboveGroundTMP);

	public int UnderGroundBirth(UnderGroundAgent agent)
	{
		UnderGroundBirths.Add(agent);
		// if (agent.Parent >= 0)
		// {
		// 	if (agent.Parent < UnderGround.Length)
		// 	{
		// 		var data = ReadTMP ? UnderGroundTMP : UnderGround;
		// 		data[agent.Parent] = data[agent.Parent].AddChild(UnderGround.Length + UnderGroundBirths.Count - 1);
		// 	}
		// 	else
		// 	{
		// 		var index = agent.Parent - UnderGround.Length;
		// 		UnderGroundBirths[index] = UnderGroundBirths[index].AddChild(UnderGround.Length + UnderGroundBirths.Count - 1);
		// 	}
		// }
		return UnderGround.Length + UnderGroundBirths.Count - 1;
	}

	public int AboveGroundBirth(AboveGroundAgent agent)
	{
		AboveGroundBirths.Add(agent);
		// if (agent.Parent >= 0)
		// {
		// 	if (agent.Parent < AboveGround.Length)
		// 	{
		// 		var data = ReadTMP ? AboveGroundTMP : AboveGround;
		// 		data[agent.Parent] = data[agent.Parent].AddChild(AboveGround.Length + AboveGroundBirths.Count - 1);
		// 	}
		// 	else
		// 	{
		// 		var index = agent.Parent - AboveGround.Length;
		// 		AboveGroundBirths[index] = AboveGroundBirths[index].AddChild(AboveGround.Length + AboveGroundBirths.Count - 1);
		// 	}
		// }
		return AboveGround.Length + AboveGroundBirths.Count - 1;
	}

	public void SeedDeath()
	{
		DeathSeed = true;
		Seed = Array.Empty<SeedAgent>();
	}

	public void UnderGroundDeath(int index)
	{
		UnderGroundDeaths.Add(index);
		var buffer = new Queue<int>();
		buffer.Enqueue(index);
		while (buffer.Count > 0)
		{
			var i = buffer.Dequeue();
			var children = GetChildren_UG(i);
			if (children != null)
				foreach(var child in children)
				{
					UnderGroundDeaths.Add(child);
					buffer.Enqueue(child);
				}
		}        
	}

	public void AboveGroundDeath(int index)
	{
		AboveGroundDeaths.Add(index);
		var buffer = new Queue<int>();
		buffer.Enqueue(index);
		while (buffer.Count > 0)
		{
			var i = buffer.Dequeue();
			//var children = ReadTMP ? AboveGroundTMP[i].Children : AboveGround[i].Children;
			var children = GetChildren_AG(i);
			if (children != null)
				foreach(var child in children)
				{
					AboveGroundDeaths.Add(child);
					buffer.Enqueue(child);
				}
		}
	}

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

	public bool Send(int dst, IMessage<SeedAgent> msg)
	{
		if (Seed.Length > 0 && Seed.Length > dst)
		{
			PostboxSeed.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	public bool Send(int dst, IMessage<UnderGroundAgent> msg)
	{
		if (UnderGround.Length > 0 && UnderGround.Length > dst)
		{
			PostboxUnderGround.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	public bool Send(int dst, IMessage<AboveGroundAgent> msg)
	{
		if (AboveGround.Length > 0 && AboveGround.Length > dst)
		{
			PostboxAboveGround.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	public void Tick(SimulationWorld world, uint timestep)
	{
		if (DeathSeed && (UnderGround.Length == 0 && AboveGround.Length == 0 && UnderGroundBirths.Count == 0 && AboveGroundBirths.Count == 0))
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
		var (srcUG, dstUG) = SrcDst_UG();
		var (srcAG, dstAG) = SrcDst_AG();

		if (UnderGroundBirths.Count > 0 || UnderGroundDeaths.Count > 0)
		{
			if (UnderGroundDeaths.Count > 0)
			{
				UnderGroundDeaths.Sort();
				//remove duplicates
				for(int i = UnderGroundDeaths.Count - 2; i >= 0; --i)
					if (UnderGroundDeaths[i] == UnderGroundDeaths[i + 1])
						UnderGroundDeaths.RemoveAt(i + 1);				
			}

			var diff = UnderGroundBirths.Count - UnderGroundDeaths.Count;
			UnderGroundAgent[] underGround;
			if (diff != 0)
				underGround = new UnderGroundAgent[srcUG.Length + diff];
			else
				underGround = srcUG;

			int a = 0;
			int[] indexMap = null;
			if (UnderGroundDeaths.Count > 0)
			{
				indexMap = new int[srcUG.Length + UnderGroundBirths.Count];
#if GODOT				
				for(var i = UnderGroundDeaths.Count - 1; i >= 0; --i)
					GodotRemoveUnderGroundSprite(UnderGroundDeaths[i]);
#endif

				// foreach(var index in UnderGroundDeaths)  //must run before copying to underGround
				// 	if (UnderGround[index].Parent >= 0)
				// 		UnderGround[UnderGround[index].Parent].RemoveChild(index);

				var dc = UnderGroundDeaths.Count;
				for(int i = 0, d = 0; i < srcUG.Length; ++i)
				{
					if (UnderGroundDeaths[d] == i)
					{
						if (++d == dc && i + 1 < srcUG.Length)
						{
							Array.Copy(srcUG, i + 1, underGround, a, srcUG.Length - i - 1);
							for(int j = i + 1; j < srcUG.Length; ++j)
								indexMap[j] = a++;
							break;
						}
					}
					else
					{
						indexMap[i] = a;
						underGround[a++] = srcUG[i];
					}
				}
				UnderGroundDeaths.Clear();
			}
			else
			{
				Array.Copy(srcUG, underGround, srcUG.Length);
				a = srcUG.Length;
			}

			for(int i = 0; i < UnderGroundBirths.Count; ++i, ++a)
			{				
				underGround[a] = UnderGroundBirths[i];
#if GODOT
				GodotAddUnderGroundSprite(a);
#endif				
			}

			UnderGroundBirths.Clear();

			if (indexMap != null)
				UnderGroundAgent.Reindex(underGround, indexMap);			

			if (ReadTMP)
			{
				UnderGround = new UnderGroundAgent[underGround.Length];
				UnderGroundTMP = underGround;
			}
			else
			{
				UnderGround = underGround;
				UnderGroundTMP = new UnderGroundAgent[underGround.Length];
			}
			(srcUG, dstUG) = SrcDst_UG();
		}

		if (AboveGroundBirths.Count > 0 || AboveGroundDeaths.Count > 0)
		{
			if (AboveGroundDeaths.Count > 0)
			{
				AboveGroundDeaths.Sort();
				//remove duplicates
				for(int i = AboveGroundDeaths.Count - 2; i >= 0; --i)
					if (AboveGroundDeaths[i] == AboveGroundDeaths[i + 1])
						AboveGroundDeaths.RemoveAt(i + 1);
			}

			var diff = AboveGroundBirths.Count - AboveGroundDeaths.Count;
			AboveGroundAgent[] aboveGround;
			if (diff != 0)
				aboveGround = new AboveGroundAgent[srcAG.Length + diff];
			else
				aboveGround = srcAG;

			int a = 0;
			int[] indexMap = null;
			if (AboveGroundDeaths.Count > 0)
			{
				indexMap = new int[srcAG.Length + AboveGroundBirths.Count];
#if GODOT
				for(var i = AboveGroundDeaths.Count - 1; i >=0; --i)
					GodotRemoveAboveGroundSprite(AboveGroundDeaths[i]);
#endif
				// foreach(var index in AboveGroundDeaths) //must run before copying to aboveGround
				// 	if (AboveGround[index].Parent >= 0)
				// 		AboveGround[AboveGround[index].Parent].RemoveChild(index);

				var dc = AboveGroundDeaths.Count;
				for(int i = 0, d = 0; i < srcAG.Length; ++i)
				{
					if (AboveGroundDeaths[d] == i)
					{
						if(++d == dc && i + 1 < srcAG.Length)
						{
							Array.Copy(srcAG, i + 1, aboveGround, a, srcAG.Length - i - 1);
							for(int j = i + 1; j < srcAG.Length; ++j)
								indexMap[j] = a++;
							break;
						}
					}
					else
					{
						indexMap[i] = a;
						aboveGround[a++] = srcAG[i];
					}
				}

				AboveGroundDeaths.Clear();

				for(int i = 0; i < AboveGroundBirths.Count; ++i)
					indexMap[srcAG.Length + i] = a + i;
			}
			else
			{
				Array.Copy(srcAG, aboveGround, srcAG.Length);
				a = srcAG.Length;
			}

			for(int i = 0; i < AboveGroundBirths.Count; ++i, ++a)
			{
				aboveGround[a] = AboveGroundBirths[i];
#if GODOT
				GodotAddAboveGroundSprite(a);
#endif	
			}

			AboveGroundBirths.Clear();

			if (indexMap != null)
				AboveGroundAgent.Reindex(aboveGround, indexMap);

			if (ReadTMP)
			{
				AboveGround = new AboveGroundAgent[aboveGround.Length];
				AboveGroundTMP = aboveGround;
			}
			else
			{
				AboveGround = aboveGround;
				AboveGroundTMP = new AboveGroundAgent[aboveGround.Length];
			}
			(srcAG, dstAG) = SrcDst_AG();
		}

		
		Array.Copy(srcAG, dstAG, srcAG.Length);
		Array.Copy(srcUG, dstUG, dstUG.Length);
		// RootsTMP.Clear();
		// RootsTMP.AddRange(Roots);
		// StemsTMP.Clear();
		// StemsTMP.AddRange(Stems);

		//We know there is just a single seed (if any)
		if (Seed.Length > 0)
		{
			Array.Copy(srcSeed, dstSeed, srcSeed.Length);
			dstSeed[0].Tick(world, this, 0, timestep);
		}
		
		for(int i = 0; i < dstUG.Length; ++i)
		//Parallel.For(0, RootsTMP.Length, i =>
			dstUG[i].Tick(world, this, i, timestep);
		//);
		for(int i = 0; i < dstAG.Length; ++i)
		//Parallel.For(0, StemsTMP.Length, i =>
			dstAG[i].Tick(world, this, i, timestep);
		//);
		ReadTMP = !ReadTMP;
	}

	public void DeliverPost()
	{
		if (Seed.Length > 0)
		{
			var (src, dst) = SrcDst_Seed();
			Array.Copy(src, dst, src.Length);
			PostboxSeed.Process(dst);
		}

		// Roots.Clear();
		// Roots.AddRange(RootsTMP);
		{
			var (src, dst) = SrcDst_UG();
			Array.Copy(src, dst, src.Length);
			PostboxUnderGround.Process(dst);
		}

		// Stems.Clear();
		// Stems.AddRange(StemsTMP);
		{
			var (src, dst) = SrcDst_AG();
			Array.Copy(src, dst, src.Length);
			PostboxAboveGround.Process(dst);
		}

		ReadTMP = !ReadTMP;

		// if (!DeathSeed)
		// 	Console.WriteLine("R: {0} E: {1}", Seed[0].Radius, Seed[0].StoredEnergy);
		// else if (UnderGround.Length > 0)
		// 	Console.WriteLine("R: {0}x{1} E: {2} W: {3}", UnderGround[0].Radius, UnderGround[0].Length, UnderGround[0].Energy, UnderGround[0].Water);
	}

	public bool HasUndeliveredPost => PostboxSeed.AnyMessages || PostboxUnderGround.AnyMessages || PostboxAboveGround.AnyMessages;

	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public List<int> GetChildren_UG(int index)
	{
		//TODO 1: precompute at the beginning of each step  O(n²) -> O(n)
		//TODO 2: keep between steps if not births or deaths happen
		var result = new List<int>();
		var src = ReadTMP ? UnderGroundTMP : UnderGround;
		for(int i = index + 1; i < src.Length; ++i)
			if (src[i].Parent == index)
				result.Add(i);
		return result;
	}

	public List<int> GetChildren_AG(int index)
	{
		//TODO 1: precompute at the beginning of each step  O(n²) -> O(n)
		//TODO 2: keep between steps if not births or deaths happen
		var result = new List<int>();
		var src = ReadTMP ? AboveGroundTMP : AboveGround;
		for(int i = index + 1; i < src.Length; ++i)
			if (src[i].Parent == index)
				result.Add(i);
		return result;
	}

	internal float GetEnergyCapacity_UG(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].EnergyCapacity : 0f)
		: (UnderGround.Length > index ? UnderGround[index].EnergyCapacity : 0f);

	internal float GetEnergyCapacity_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].EnergyCapacity : 0f)
		: (AboveGround.Length > index ? AboveGround[index].EnergyCapacity : 0f);

	internal float GetWaterStorageCapacity_UG(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].WaterStorageCapacity : 0f)
		: (UnderGround.Length > index ? UnderGround[index].WaterStorageCapacity : 0f);

	internal float GetWaterStorageCapacity_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].WaterStorageCapacity : 0f)
		: (AboveGround.Length > index ? AboveGround[index].WaterStorageCapacity : 0f);

	internal float GetWaterCapacityPerTick_UG(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].WaterCapacityPerTick : 0f)
		: (UnderGround.Length > index ? UnderGround[index].WaterCapacityPerTick : 0f);

	internal float GetWaterCapacityPerTick_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].WaterCapacityPerTick : 0f)
		: (AboveGround.Length > index ? AboveGround[index].WaterCapacityPerTick : 0f);

	public float GetEnergy_UG(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Energy : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Energy : 0f);

	public float GetEnergy_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Energy : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Energy : 0f);

	public float GetEnergyFlow_PerTick_UG(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].EnergyFlowToParentPerTick : 0f)
		: (UnderGround.Length > index ? UnderGround[index].EnergyFlowToParentPerTick : 0f);

	public float GetEnergyFlow_PerTick_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].EnergyFlowToParentPerTick : 0f)
		: (AboveGround.Length > index ? AboveGround[index].EnergyFlowToParentPerTick : 0f);
	
	public float GetWater_UG(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Water : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Water : 0f);
	
	public float GetWater_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Water : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Water : 0f);
	
	public float GetBaseRadius_UG(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Radius : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Radius : 0f);
	public float GetBaseRadius_AG(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Radius : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Radius : 0f);
	
	public float GetLength_UG(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Length : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Length : 0f);
	public float GetLength_AG(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Length : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Length : 0f);
	
	//TODO accumulate from root
	public Quaternion GetDirection_UG(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Orientation : Quaternion.Identity)
		: (UnderGround.Length > index ? UnderGround[index].Orientation : Quaternion.Identity);
	//TODO accumulate from root
	public Quaternion GetDirection_AG(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Orientation : Quaternion.Identity)
		: (AboveGround.Length > index ? AboveGround[index].Orientation : Quaternion.Identity);

	public OrganTypes GetOrgan_UG(int index) => OrganTypes.Root;

	public OrganTypes GetOrgan_AG(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Organ : OrganTypes.Stem)
		: (AboveGround.Length > index ? AboveGround[index].Organ : OrganTypes.Stem);

	public Vector3 GetBaseCenter_UG(int index)
	{
		if (ReadTMP ? UnderGroundTMP.Length <= index : UnderGround.Length <= index)
			return Vector3.Zero;

		var parents = new List<int>{index};
		if (ReadTMP)
			do parents.Add(UnderGroundTMP[parents[^1]].Parent);
			while(parents[^1] >= 0 && parents.Count <= UnderGroundTMP.Length);
		else
			do parents.Add(UnderGround[parents[^1]].Parent);
			while(parents[^1] >= 0 && parents.Count <= UnderGround.Length);

		var result = Position;
		if (ReadTMP)
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, UnderGroundTMP[parents[i]].Orientation) * UnderGroundTMP[parents[i]].Length;
		else
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, UnderGround[parents[i]].Orientation) * UnderGround[parents[i]].Length;

		return result;
	}

	public Vector3 GetBaseCenter_AG(int index)
	{
		if (ReadTMP ? AboveGroundTMP.Length <= index : AboveGround.Length <= index)
			return Vector3.Zero;

		var parents = new List<int>{index};
		if (ReadTMP)
			do parents.Add(AboveGroundTMP[parents[^1]].Parent);
			while(parents[^1] >= 0 && parents.Count <= AboveGroundTMP.Length);
		else
			do parents.Add(AboveGround[parents[^1]].Parent);
			while(parents[^1] >= 0 && parents.Count <= AboveGround.Length);

		var result = Position;
		if (ReadTMP)
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, AboveGroundTMP[parents[i]].Orientation) * AboveGroundTMP[parents[i]].Length;
		else
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, AboveGround[parents[i]].Orientation) * AboveGround[parents[i]].Length;

		return result;
	}
	#endregion

	///////////////////////////
	#region WRITE METHODS
	///////////////////////////

	//THERE ARE NO WRITE METHODS ALLOWED.

	#endregion
}
