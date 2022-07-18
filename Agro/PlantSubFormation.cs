using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using glTFLoader.Schema;
using Utils;
using NumericHelpers;


namespace Agro;

public partial class PlantSubFormation<T> : IFormation where T: struct, IPlantAgent
{
	readonly Action<T[], int[]> Reindex;
	public readonly PlantFormation Plant;
	//Once GODOT supports C# 6.0: Make it a List and then for processing send System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Stems);
	bool ReadTMP = false;
	protected T[] Agents = Array.Empty<T>();
	protected T[] AgentsTMP = Array.Empty<T>();
	protected readonly PostBox<T> Post = new();

	protected List<T> Births = new();
	protected List<T> Inserts = new();
	protected List<int> InsertAncestors = new();
	//protected bool ParentUpdates = false;
	protected HashSet<int> Deaths = new();
	protected List<int> DeathsHelper = new();

	public PlantSubFormation(PlantFormation plant, Action<T[], int[]> reindex)
	{
		Plant = plant;
		Reindex = reindex;
	}

	public bool Alive => Agents.Length > 0 || Births.Count > 0 || Inserts.Count > 0;
	public bool AnyMessages => Post.AnyMessages;

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(T[], T[]) SrcDst() => ReadTMP ? (AgentsTMP, Agents) : (Agents, AgentsTMP);
	T[] Src() => ReadTMP ? AgentsTMP : Agents;

	public int Birth(T agent)
	{
		Births.Add(agent);
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
		return Agents.Length + Births.Count - 1;
	}

	public void Insert(int ancestor, T agent)
	{
		Inserts.Add(agent);
		InsertAncestors.Add(ancestor);
	}

	// public void Update(int index) => ParentUpdates = true;

	public void Death(int index)
	{
		if (Deaths.Add(index))
		{
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
							buffer.Enqueue(child);
					}
			}
		}
	}

	public bool SendProtected(int dst, IMessage<T> msg)
	{
		if (Agents.Length > dst)
		{
			Post.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	public void Census()
	{
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
		if (Births.Count > 0 || Inserts.Count > 0 || Deaths.Count > 0)
		{
			var (src, dst) = SrcDst();

			if (Deaths.Count > 0)
			{
				DeathsHelper.Clear();
				DeathsHelper.AddRange(Deaths);
				DeathsHelper.Sort();
			}

			//filter out addidions to death parts
			bool anyRemoved;
			var localRemoved = new HashSet<int>();
			do
			{
				anyRemoved = false;
				for(int i = Births.Count - 1; i >= 0; --i)
				{
					var p = Births[i].Parent;
					if (Deaths.Contains(p) || localRemoved.Contains(p))
					{
						Births.RemoveAt(i);
						localRemoved.Add(src.Length + i);
						anyRemoved = true;
					}
				}
			}
			while (anyRemoved);

			do
			{
				anyRemoved = false;
				for(int i = Inserts.Count - 1; i >= 0; --i)
				{
					var p = Inserts[i].Parent;
					if (Deaths.Contains(p) || localRemoved.Contains(p))
					{
						Inserts.RemoveAt(i);
						InsertAncestors.RemoveAt(i);
						localRemoved.Add(src.Length + Births.Count + i);
						anyRemoved = true;
					}
				}
			}
			while (anyRemoved);

			if (Inserts.Count > 0)
			{
				for(int i = 0; i < Inserts.Count; ++i)
				{
					var index = src.Length + Births.Count - Deaths.Count + i;
					src[InsertAncestors[i]].CensusUpdateParent(index);
				}
			}

			var diff = Births.Count + Inserts.Count - Deaths.Count;
			var tmp = diff != 0 ? new T[src.Length + diff] : dst;

			if (Deaths.Count > 0)
			{
				var indexMap = new int[src.Length + Births.Count +  Inserts.Count];
				Array.Fill(indexMap, -1);
#if GODOT
				for(var i = DeathsHelper.Count - 1; i >= 0; --i)
					GodotRemoveSprite(DeathsHelper[i]);
#endif

				// foreach(var index in Deaths)  //must run before copying to underGround
				// 	if (Agents[index].Parent >= 0)
				// 		Agents[Agents[index].Parent].RemoveChild(index);

				int a = 0;
				var deathsCount = DeathsHelper.Count;
				for(int s = 0, d = 0; s < src.Length;) //iterate all existing agents
				{
					if (DeathsHelper[d] == s) //if this one is dead skip it ...
					{
						++s;
						if (++d == deathsCount && s < src.Length) // ... but if it is the last one, copy the rest of src and done
						{
							Array.Copy(src, s, tmp, a, src.Length - s);
							for(int j = s; j < src.Length; ++j)
								indexMap[j] = a++;
							break;
						}
					}
					else //if this one is alive
					{
						indexMap[s] = a;
						//TODO this could be more efficient using Array.Copy for continuous blocks
						tmp[a++] = src[s++];
					}
				}

				var birthsCount = Births.Count;
				for(int i = 0; i < birthsCount; ++i, ++a)
				{
					indexMap[src.Length + i] = a;
					tmp[a] = Births[i];
				}

				var insertsCount = Inserts.Count;
				for(int i = 0; i < insertsCount; ++i, ++a)
				{
					indexMap[src.Length + i] = a;
					tmp[a] = Inserts[i];
				}

				if (indexMap != null)
					Reindex(tmp, indexMap);

				Deaths.Clear();
			}
			else
			{
				Array.Copy(src, tmp, src.Length);
				var a = src.Length;

				var birthsCount = Births.Count;
				for(int b = 0; b < birthsCount; ++a, ++b)
					tmp[a] = Births[b];

				var insertsCount = Inserts.Count;
				for(int i = 0; i < insertsCount; ++i, ++a)
					tmp[a] = Inserts[i];
			}

			if (ReadTMP)
			{
				Agents = new T[tmp.Length];
				AgentsTMP = tmp;
			}
			else
			{
				Agents = tmp;
				AgentsTMP = new T[tmp.Length];
			}

#if GODOT
			for(int i = Agents.Length - Births.Count - Inserts.Count; i < Agents.Length; ++i)
				GodotAddSprite(i);
#endif
			Births.Clear();
			Inserts.Clear();
			InsertAncestors.Clear();
		}
	}

	public void Tick(SimulationWorld world, uint timestep)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		// StemsTMP.Clear();
		// StemsTMP.AddRange(Stems);

		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(world, this, i, timestep);

		#if HISTORY_LOG
		var state = new T[dst.Length];
		Array.Copy(dst, state, dst.Length);
		StatesHistory.Add(state);
		#endif
		ReadTMP = !ReadTMP;
	}

	public void DeliverPost(uint timestep)
	{
		// Roots.Clear();
		// Roots.AddRange(RootsTMP);
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		Post.Process(timestep, dst);

		ReadTMP = !ReadTMP;
#if DEBUG
		for(int i = 0; i < Agents.Length; ++i)
			GetBaseCenter(i);
#endif
	}

	public bool HasUndeliveredPost => Post.AnyMessages;

	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public List<int> GetChildren(int index)
	{
		//TODO 1: precompute at the beginning of each step  O(n²) -> O(n)
		//TODO 2: keep between steps if not births or deaths happen
		var result = new List<int>();
		var src = Src();
		//for(int i = index + 1; i < src.Length; ++i) //this had a lot of issues for AG when splitting branches
		for(int i = 0; i < src.Length; ++i)
			if (src[i].Parent == index)
				result.Add(i);
		return result;
	}

	public List<int> GetRoots()
	{
		var result = new List<int>();
		var src = Src();
		for(int i = 0; i < src.Length; ++i)
			if (src[i].Parent < 0)
				result.Add(i);
		return result;
	}

	internal float GetEnergyCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].EnergyStorageCapacity : 0f)
		: (Agents.Length > index ? Agents[index].EnergyStorageCapacity : 0f);

	internal float GetWaterStorageCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WaterStorageCapacity : 0f)
		: (Agents.Length > index ? Agents[index].WaterStorageCapacity : 0f);

	internal float GetWaterCapacityPerTick(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WaterTotalCapacityPerTick : 0f)
		: (Agents.Length > index ? Agents[index].WaterTotalCapacityPerTick : 0f);

	public float GetEnergy(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Energy : 0f)
		: (Agents.Length > index ? Agents[index].Energy : 0f);

	public float GetEnergyFlow_PerTick(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].EnergyFlowToParentPerTick : 0f)
		: (Agents.Length > index ? Agents[index].EnergyFlowToParentPerTick : 0f);

	public float GetWater(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Water : 0f)
		: (Agents.Length > index ? Agents[index].Water : 0f);

	public float GetBaseRadius(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Radius : 0f)
		: (Agents.Length > index ? Agents[index].Radius : 0f);

	public float GetLength(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Length : 0f)
		: (Agents.Length > index ? Agents[index].Length : 0f);

	//TODO accumulate from root
	public Quaternion GetDirection(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Orientation : Quaternion.Identity)
		: (Agents.Length > index ? Agents[index].Orientation : Quaternion.Identity);

	public OrganTypes GetOrgan(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Organ : OrganTypes.Stem)
		: (Agents.Length > index ? Agents[index].Organ : OrganTypes.Stem);

	public float GetWoodRatio(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WoodRatio : 0f)
		: (Agents.Length > index ? Agents[index].WoodRatio : 0f);

	public Vector3 GetBaseCenter(int index)
	{
		if (ReadTMP ? AgentsTMP.Length <= index : Agents.Length <= index)
			return Vector3.Zero;

		var parents = new List<int>{index};
		if (ReadTMP)
			do {parents.Add(AgentsTMP[parents[^1]].Parent); }
			while(parents[^1] >= 0 && parents.Count <= AgentsTMP.Length);
		else
			do parents.Add(Agents[parents[^1]].Parent);
			while(parents[^1] >= 0 && parents.Count <= Agents.Length);

		var result = Plant.Position;
		if (ReadTMP)
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, AgentsTMP[parents[i]].Orientation) * AgentsTMP[parents[i]].Length;
		else
			for(int i = parents.Count - 2; i > 0; --i)
				result += Vector3.Transform(Vector3.UnitX, Agents[parents[i]].Orientation) * Agents[parents[i]].Length;

		return result;
	}

	public Vector3 GetScale(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Scale : Vector3.Zero)
		: (Agents.Length > index ? Agents[index].Scale : Vector3.Zero);
	#endregion

	///////////////////////////
	#region WRITE METHODS
	///////////////////////////

	//THERE ARE NO WRITE METHODS ALLOWED.

	#endregion

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG
	List<T[]> StatesHistory = new();
	public string HistoryToJSON() => Utils.Export.Json(StatesHistory);

	public ulong GetID(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].ID : ulong.MaxValue)
		: (Agents.Length > index ? Agents[index].ID : ulong.MaxValue);
	#endif
	#endregion

	///////////////////////////
	#region glTF EXPORT
	///////////////////////////
	public List<Node> ExportToGLTF()
	{
		var src = Src();
		var nodes = new List<Node>(src.Length);
		for(int i = 0; i < src.Length; ++i)
		{
			var baseCenter = GetBaseCenter(i);

			nodes[i] = new(){
				Name = $"{GetOrgan(i)}_{i}",
				Mesh = 0,
				Rotation = GetDirection(i).ToArray(),
				Translation = baseCenter.ToArray(),
				Scale = null
			};
		}
		return nodes;
	}
	#endregion
}
