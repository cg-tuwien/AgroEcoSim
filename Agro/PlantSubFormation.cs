using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using Utils;

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
	protected List<int> Deaths = new();

	public PlantSubFormation(PlantFormation plant, Action<T[], int[]> reindex)
	{
		Plant = plant;
		Reindex = reindex;
	}

	public bool Alive => Agents.Length > 0 || Births.Count > 0;
	public bool AnyMessages => Post.AnyMessages;

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(T[], T[]) SrcDst() => ReadTMP ? (AgentsTMP, Agents) : (Agents, AgentsTMP);

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

	public void Death(int index)
	{
		Deaths.Add(index);
		var buffer = new Queue<int>();
		buffer.Enqueue(index);
		while (buffer.Count > 0)
		{
			var i = buffer.Dequeue();
			var children = GetChildren(i);
			if (children != null)
				foreach(var child in children)
				{
					Deaths.Add(child);
					buffer.Enqueue(child);
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

		if (Births.Count > 0 || Deaths.Count > 0)
		{
			var (src, dst) = SrcDst();
			if (Deaths.Count > 0)
			{
				Deaths.Sort();
				//remove duplicates
				for(int i = Deaths.Count - 2; i >= 0; --i)
					if (Deaths[i] == Deaths[i + 1])
						Deaths.RemoveAt(i + 1);
			}

			var diff = Births.Count - Deaths.Count;
			T[] underGround;
			if (diff != 0)
				underGround = new T[src.Length + diff];
			else
				underGround = src;

			int a = 0;
			int[] indexMap = null;
			if (Deaths.Count > 0)
			{
				indexMap = new int[src.Length + Births.Count];
				Array.Fill(indexMap, -1);
#if GODOT
				for(var i = Deaths.Count - 1; i >= 0; --i)
					GodotRemoveSprite(Deaths[i]);
#endif

				// foreach(var index in Deaths)  //must run before copying to underGround
				// 	if (Agents[index].Parent >= 0)
				// 		Agents[Agents[index].Parent].RemoveChild(index);

				var dc = Deaths.Count;
				for(int i = 0, d = 0; i < src.Length; ++i)
				{
					if (Deaths[d] == i)
					{
						if (++d == dc && i + 1 < src.Length)
						{
							Array.Copy(src, i + 1, underGround, a, src.Length - i - 1);
							for(int j = i + 1; j < src.Length; ++j)
								indexMap[j] = a++;
							break;
						}
					}
					else
					{
						indexMap[i] = a;
						underGround[a++] = src[i];
					}
				}
				Deaths.Clear();
			}
			else
			{
				Array.Copy(src, underGround, src.Length);
				a = src.Length;
			}

			for(int i = 0; i < Births.Count; ++i, ++a)
				underGround[a] = Births[i];

			if (indexMap != null)
				Reindex(underGround, indexMap);

			Debug.Assert(Enumerable.Range(0, underGround.Length).All(i => underGround[i].Parent < i));

			if (ReadTMP)
			{
				Agents = new T[underGround.Length];
				AgentsTMP = underGround;
			}
			else
			{
				Agents = underGround;
				AgentsTMP = new T[underGround.Length];
			}

#if GODOT
			for(int i = Agents.Length - Births.Count; i < Agents.Length; ++i)
				GodotAddSprite(i);
#endif
			Births.Clear();
		}
	}

	public void Tick(SimulationWorld world, uint timestep)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		// StemsTMP.Clear();
		// StemsTMP.AddRange(Stems);

		for(int i = 0; i < dst.Length; ++i)
		//Parallel.For(0, RootsTMP.Length, i =>
			dst[i].Tick(world, this, i, timestep);
		//);

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
		var src = ReadTMP ? AgentsTMP : Agents;
		for(int i = index + 1; i < src.Length; ++i)
			if (src[i].Parent == index)
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

	public Vector3 GetBaseCenter(int index)
	{
		if (ReadTMP ? AgentsTMP.Length <= index : Agents.Length <= index)
			return Vector3.Zero;

		var parents = new List<int>{index};
		if (ReadTMP)
			do parents.Add(AgentsTMP[parents[^1]].Parent);
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
}