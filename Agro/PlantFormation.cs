using System;
using System.Collections.Generic;
using System.Numerics;
using AgentsSystem;

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

	internal readonly Vector2 VegetativeLowTemperature = new(10, 15);
	internal readonly Vector2 VegetativeHighTemperature = new(35, 40);

	public PlantFormation(SoilFormation soil, SeedAgent seed) : base()
	{
		Soil = soil;
		Seed[0] = seed;
		Position = seed.Center;
	}

	public int UnderGroundBirth(UnderGroundAgent agent)
	{
		UnderGroundBirths.Add(agent);
		if (agent.Parent >= 0)
		{
			if (agent.Parent < UnderGround.Length)
				UnderGround[agent.Parent].AddChild(UnderGround.Length + UnderGroundBirths.Count - 1);
			else
				UnderGroundBirths[agent.Parent - UnderGround.Length].AddChild(UnderGround.Length + UnderGroundBirths.Count - 1);
		}
		return UnderGround.Length + UnderGroundBirths.Count - 1;
	}

	public int AboveGroundBirth(AboveGroundAgent agent)
	{
		AboveGroundBirths.Add(agent);
		if (agent.Parent >= 0)
		{
			if (agent.Parent < AboveGround.Length)
				AboveGround[agent.Parent] = AboveGround[agent.Parent].AddChild(AboveGround.Length + AboveGroundBirths.Count - 1);
			else
			{
				var index = agent.Parent - AboveGround.Length;
				AboveGroundBirths[index] = AboveGroundBirths[index].AddChild(AboveGround.Length + AboveGroundBirths.Count - 1);
			}
		}
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
			var children = ReadTMP ? UnderGroundTMP[i].Children : UnderGround[i].Children;
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
			var children = ReadTMP ? AboveGroundTMP[i].Children : AboveGround[i].Children;
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
			UnderGroundAgent[] undergrounds;
			if (diff != 0)
				undergrounds = new UnderGroundAgent[UnderGround.Length + diff];
			else
				undergrounds = UnderGround;

			int a = 0;
			if (UnderGroundDeaths.Count > 0)
			{
#if GODOT				
				for(var i = UnderGroundDeaths.Count - 1; i >= 0; --i)
					GodotRemoveUnderGroundSprite(UnderGroundDeaths[i]);
#endif				
				for(int i = UnderGround.Length - 1, d = UnderGroundDeaths.Count - 1; i >= 0; --i)
				{
					if (d >= 0 && UnderGroundDeaths[d] == i)
						--d;
					else
						undergrounds[a++] = UnderGround[i];                    
				}
				UnderGroundDeaths.Clear();
			}
			else
				a = UnderGround.Length;

			for(int i = 0; i < UnderGroundBirths.Count; ++i, ++a)
			{				
				undergrounds[a] = UnderGroundBirths[i];
#if GODOT
				GodotAddUnderGroundSprite(a);
#endif				
			}
			
			foreach(var index in UnderGroundDeaths)
				if (UnderGround[index].Parent >= 0)
					UnderGround[UnderGround[index].Parent].RemoveChild(index);

			UnderGroundBirths.Clear();

			UnderGround = undergrounds;
			UnderGroundTMP = new UnderGroundAgent[UnderGround.Length];                
		}

		//TODO Reindex if RootsDeaths.Count!
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
				aboveGround = new AboveGroundAgent[AboveGround.Length + diff];
			else
				aboveGround = AboveGround;

			int a = 0;
			int[] indexMap = null;
			if (AboveGroundDeaths.Count > 0)
			{
				indexMap = new int[AboveGround.Length + AboveGroundBirths.Count];
#if GODOT
				for(var i = AboveGroundDeaths.Count - 1; i >=0; --i)
					GodotRemoveAboveGroundSprite(AboveGroundDeaths[i]);
#endif
				for(int i = AboveGround.Length - 1, d = AboveGroundDeaths.Count - 1; i >= 0; --i)
				{
					if (d >= 0 && AboveGroundDeaths[d] == i)
						--d;
					else
					{
						indexMap[i] = a;
						aboveGround[a++] = AboveGround[i];
					}
				}
				
				foreach(var index in AboveGroundDeaths)
					if (AboveGround[index].Parent >= 0)
						AboveGround[AboveGround[index].Parent].RemoveChild(index);

				AboveGroundDeaths.Clear();

				for(int i = 0; i < AboveGroundBirths.Count; ++i)
					indexMap[AboveGround.Length + i] = a + i;
			}
			else
				a = AboveGround.Length;

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

			AboveGround = aboveGround;
			AboveGroundTMP = new AboveGroundAgent[AboveGround.Length];
		}

		
		Array.Copy(AboveGround, AboveGroundTMP, AboveGround.Length);
		Array.Copy(UnderGround, UnderGroundTMP, UnderGround.Length);
		// RootsTMP.Clear();
		// RootsTMP.AddRange(Roots);
		// StemsTMP.Clear();
		// StemsTMP.AddRange(Stems);

		//We know there is just a single seed (if any)
		if (Seed.Length > 0)
		{
			Array.Copy(Seed, SeedTMP, Seed.Length);
			SeedTMP[0].Tick(world, this, 0, timestep);
		}
		
		for(int i = 0; i < UnderGroundTMP.Length; ++i)
		//Parallel.For(0, RootsTMP.Length, i =>
			UnderGroundTMP[i].Tick(world, this, i, timestep);
		//);
		for(int i = 0; i < AboveGroundTMP.Length; ++i)
		//Parallel.For(0, StemsTMP.Length, i =>
			AboveGroundTMP[i].Tick(world, this, i, timestep);
		//);
		ReadTMP = true;
	}

	public void DeliverPost()
	{
		if (Seed.Length > 0)
		{
			Array.Copy(SeedTMP, Seed, SeedTMP.Length);
			PostboxSeed.Process(Seed);
		}

		// Roots.Clear();
		// Roots.AddRange(RootsTMP);
		Array.Copy(UnderGroundTMP, UnderGround, UnderGroundTMP.Length);
		PostboxUnderGround.Process(UnderGround);

		// Stems.Clear();
		// Stems.AddRange(StemsTMP);
		Array.Copy(AboveGroundTMP, AboveGround, AboveGroundTMP.Length);
		PostboxAboveGround.Process(AboveGround);
		ReadTMP = false;

		// if (!DeathSeed)
		// 	Console.WriteLine("R: {0} E: {1}", Seed[0].Radius, Seed[0].StoredEnergy);
		// else if (UnderGround.Length > 0)
		// 	Console.WriteLine("R: {0}x{1} E: {2} W: {3}", UnderGround[0].Radius, UnderGround[0].Length, UnderGround[0].Energy, UnderGround[0].Water);
	}

	public float GetUnderGroundEnergy(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Energy : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Energy : 0f);

	public float GetAboveGroundEnergy(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Energy : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Energy : 0f);
	
	public float GetUnderGroundWater(int index) => ReadTMP
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Water : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Water : 0f);
	
	public float GetAboveGroundWater(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Water : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Water : 0f);
	
	public float GetUnderGroundBaseRadius(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Radius : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Radius : 0f);
	public float GetAboveGroundBaseRadius(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Radius : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Radius : 0f);
	
	public float GetUnderGroundLength(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Length : 0f)
		: (UnderGround.Length > index ? UnderGround[index].Length : 0f);
	public float GetAboveGroundLength(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Length : 0f)
		: (AboveGround.Length > index ? AboveGround[index].Length : 0f);
	
	public Quaternion GetUnderGroundDirection(int index) => ReadTMP 
		? (UnderGroundTMP.Length > index ? UnderGroundTMP[index].Direction : Quaternion.Identity)
		: (UnderGround.Length > index ? UnderGround[index].Direction : Quaternion.Identity);
	public Quaternion GetAboveGroundDirection(int index) => ReadTMP 
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Direction : Quaternion.Identity)
		: (AboveGround.Length > index ? AboveGround[index].Direction : Quaternion.Identity);

	public OrganTypes GetUnderGroundOrgan(int index) => OrganTypes.Root;

	public OrganTypes GetAboveGroundOrgan(int index) => ReadTMP
		? (AboveGroundTMP.Length > index ? AboveGroundTMP[index].Organ : OrganTypes.Stem)
		: (AboveGround.Length > index ? AboveGround[index].Organ : OrganTypes.Stem);

	public Vector3 GetUnderGroundBaseCenter(int index)
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
				result += Vector3.Transform(Vector3.UnitX, UnderGroundTMP[parents[i]].Direction) * UnderGroundTMP[parents[i]].Length;
		else
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, UnderGround[parents[i]].Direction) * UnderGround[parents[i]].Length;

		return result;
	}

	public Vector3 GetAboveGroundBaseCenter(int index)
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
				result += Vector3.Transform(Vector3.UnitX, AboveGroundTMP[parents[i]].Direction) * AboveGroundTMP[parents[i]].Length;
		else
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, AboveGround[parents[i]].Direction) * AboveGround[parents[i]].Length;

		return result;
	}
}
