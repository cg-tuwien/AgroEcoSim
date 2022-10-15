using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using AgentsSystem;
using glTFLoader.Schema;
using Utils;
using NumericHelpers;
using System.Collections;
using System.Runtime.InteropServices;

namespace Agro;

public enum PlantSubstances : byte { Water, Energy}

internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	ushort[] DepthNodes;
	Vector3[] PointNodes;
	readonly List<int> Roots = new();
	ushort MaxDepth = 0;

	public TreeCacheData()
	{
		Count = 0;
		ChildrenNodes = new List<int>[]{ new(), new() };
		DepthNodes = new ushort[]{ 0, 0 };
		PointNodes = new Vector3[] {default, default};
	}

	public void Clear(int newSize)
	{
		Roots.Clear();
		if (newSize > ChildrenNodes.Length)
		{
			var l = ChildrenNodes.Length;
			Array.Resize(ref ChildrenNodes, newSize);
			for(int i = l; i < newSize; ++i)
				ChildrenNodes[i] = new();
			Array.Resize(ref DepthNodes, newSize);
			Array.Resize(ref PointNodes, newSize);
		}
		Count = newSize;
		for(int i = 0; i < newSize; ++i)
			ChildrenNodes[i].Clear();
	}

	public void AddChild(int parentIndex, int childIndex)
	{
		if (parentIndex >= 0)
			ChildrenNodes[parentIndex].Add(childIndex);
		else
			Roots.Add(childIndex);
	}

	public void FinishUpdate()
	{
		var buffer = new Stack<(int, ushort)>();
		foreach(var item in Roots)
			buffer.Push((item, 0));

		MaxDepth = 0;

		while(buffer.Count > 0)
		{
			var (index, depth) = buffer.Pop();
			DepthNodes[index] = depth;
			if (depth > MaxDepth)
				MaxDepth = depth;
			var nextDepth = (ushort)(depth + 1);
			foreach(var child in ChildrenNodes[index])
				buffer.Push((child, nextDepth));
		}

		++MaxDepth;
	}

	internal IList<int> GetChildren(int index) => ChildrenNodes[index];
	internal ICollection<int> GetRoots() => Roots;
	internal ushort GetAbsDepth(int index) => DepthNodes[index];
	internal float GetRelDepth(int index) => MaxDepth > 0 ? (DepthNodes[index] + 1) / (float)MaxDepth : 1f;
	internal Vector3 GetBaseCenter(int index) => PointNodes[index];

	internal void UpdateBases<T>(PlantSubFormation<T> formation) where T : struct, IPlantAgent
	{
		var buffer = new Stack<int>();
		foreach(var root in Roots)
		{
			PointNodes[root] = formation.Plant.Position;
			var point = formation.Plant.Position + Vector3.Transform(Vector3.UnitX, formation.GetDirection(root)) * formation.GetLength(root);
			foreach(var child in GetChildren(root))
			{
				PointNodes[child] = point;
				buffer.Push(child);
			}
		}

		while (buffer.Count > 0)
		{
			var next = buffer.Pop();
			var children = GetChildren(next);
			if (children.Count > 0)
			{
				var point = PointNodes[next] + Vector3.Transform(Vector3.UnitX, formation.GetDirection(next)) * formation.GetLength(next);
				foreach(var child in children)
				{
					PointNodes[child] = point;
					buffer.Push(child);
				}
			}
		}
	}
}

public partial class PlantSubFormation<T> : IFormation where T: struct, IPlantAgent
{
	public byte Stages => 1;
	readonly Action<T[], int[]> Reindex;
	public readonly PlantFormation1 Plant;
	//Once GODOT supports C# 6.0: Make it a List and then for processing send System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Stems);
	bool ReadTMP = false;
	T[] Agents = Array.Empty<T>();
	T[] AgentsTMP = Array.Empty<T>();
	readonly PostBox<T> Post = new();
	readonly TransactionsBox Transactions = new();
	readonly List<T> Births = new();
	readonly List<T> Inserts = new();
	readonly List<int> InsertAncestors = new();
	readonly HashSet<int> Deaths = new();
	readonly List<int> DeathsHelper = new();

	readonly TreeCacheData TreeCache = new();

	public PlantSubFormation(PlantFormation1 plant, Action<T[], int[]> reindex)
	{
		Plant = plant;
		Reindex = reindex;
	}

	public bool CheckIndex(int index) => index < Agents.Length;

	public bool Alive => Agents.Length > 0 || Births.Count > 0 || Inserts.Count > 0;
	public int Count => Agents.Length;

	/// <summary>
	/// An ordered tuple of the double data-buffer entries ready for swap.
	/// </summary>
	(T[], T[]) SrcDst() => ReadTMP ? (AgentsTMP, Agents) : (Agents, AgentsTMP);
	T[] Src() => ReadTMP ? AgentsTMP : Agents;

	public int Birth(T agent)
	{
		Births.Add(agent);
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

	public bool SendProtected(int srcIndex, int dstIndex, PlantSubstances substance, float amount)
	{
		if (Agents.Length > srcIndex && Agents.Length > dstIndex)
		{
			Transactions.Add(srcIndex, dstIndex, (byte)substance, amount);
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
			//Debug.WriteLine($"{typeof(T).Name} census event: B = {Births.Count}   I = {Inserts.Count}   D = {Deaths.Count}");

			// #if GODOT
			// MultiagentSystem.TriggerPause();
			// #endif
			var (src, dst) = SrcDst();
			// #if DEBUG
			// Console.WriteLine(DebugTreePrint(src));
			// #endif
			if (Deaths.Count > 0)
			{
				DeathsHelper.Clear();
				DeathsHelper.AddRange(Deaths);
				DeathsHelper.Sort();
			}

			var diff = Births.Count + Inserts.Count - Deaths.Count;

			//filter out addidions to death parts
			BitArray? birthsHelper = null, insertsHelper = null;
			if (Deaths.Count > 0)
			{
				bool anyRemoved;
				var localRemoved = new HashSet<int>();
				if (Births.Count > 0)
				{
					do
					{
						anyRemoved = false;
						for(int i = Births.Count - 1; i >= 0; --i)
						{
							var index = src.Length + i;
							if (!localRemoved.Contains(index))
							{
								var p = Births[i].Parent;
								if (Deaths.Contains(p) || localRemoved.Contains(p))
								{
									localRemoved.Add(index);
									anyRemoved = true;
								}
							}
						}
					}
					while (anyRemoved);
				}

				if (Inserts.Count > 0)
				{
					do
					{
						anyRemoved = false;
						for(int i = Inserts.Count - 1; i >= 0; --i)
						{
							var index = src.Length + Births.Count + i;
							if (!localRemoved.Contains(index))
							{
								var p = Inserts[i].Parent;
								if (Deaths.Contains(p) || localRemoved.Contains(p))
								{
									localRemoved.Add(index);
									anyRemoved = true;
								}
							}
						}
					}
					while (anyRemoved);
				}

				if (localRemoved.Count > 0)
				{
					diff -= localRemoved.Count;
					if (Births.Count > 0)
					{
						birthsHelper = new BitArray(Births.Count, true);
						for(int i = 0; i < Births.Count; ++i)
							if (localRemoved.Contains(src.Length + i))
								birthsHelper.Set(i, false);
					}

					if (Inserts.Count > 0)
					{
						insertsHelper = new BitArray(Inserts.Count, true);
						for(int i = 0; i < Inserts.Count; ++i)
							if (localRemoved.Contains(src.Length + Births.Count + i))
								insertsHelper.Set(i, false);
					}
				}
			}

			var tmp = diff != 0 ? new T[src.Length + diff] : dst;

			if (Deaths.Count > 0)
			{
				var indexMap = new int[src.Length + Births.Count + Inserts.Count];
				Array.Fill(indexMap, -1);

				PrepareDeaths();

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
				for(int i = 0; i < birthsCount; ++i)
					if (birthsHelper?.Get(i) ?? true)
					{
						indexMap[src.Length + i] = a;
						tmp[a++] = Births[i];
					}

				var insertsCount = Inserts.Count;
				var insertsUpdateMap = Inserts.Count > 0 ? new int[Inserts.Count] : Array.Empty<int>();
				for(int i = 0; i < insertsCount; ++i)
					if (insertsHelper?.Get(i) ?? true)
					{
						indexMap[src.Length + i] = a;
						insertsUpdateMap[i] = a;
						tmp[a++] = Inserts[i];
					}

				if (indexMap != null)
					Reindex(tmp, indexMap);

				for(int i = 0; i < insertsCount; ++i)
					if (insertsHelper?.Get(i) ?? true)
						tmp[indexMap?[InsertAncestors[i]] ?? InsertAncestors[i]].CensusUpdateParent(insertsUpdateMap[i]);

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
				{
					tmp[a] = Inserts[i];
					tmp[InsertAncestors[i]].CensusUpdateParent(a);
				}
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
			// #if DEBUG
			// Console.WriteLine(DebugTreePrint(Src()));
			// #endif

			src = Src();
			TreeCache.Clear(src.Length);
			for(int i = 0; i < src.Length; ++i)
				TreeCache.AddChild(src[i].Parent, i);

			TreeCache.FinishUpdate();
			PostCensusPositive();
			Births.Clear();
			Inserts.Clear();
			InsertAncestors.Clear();
		}
		else
			PostCensusNegative();
	}

	void PrepareDeaths()
	{
		#if GODOT
		for(var i = DeathsHelper.Count - 1; i >= 0; --i)
			GodotRemoveSprite(DeathsHelper[i]);
		#endif
	}

	void PostCensusPositive()
	{
		TreeCache.UpdateBases(this);

		#if GODOT
		GodotAddSprites(Agents.Length);
		#endif
	}

	void PostCensusNegative() => TreeCache.UpdateBases(this);

	public void Tick(SimulationWorld world, uint timestep, byte stage)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		// StemsTMP.Clear();
		// StemsTMP.AddRange(Stems);

		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(world, this, i, timestep, stage);

		#if TICK_LOG
		StatesHistory.Clear();
		#endif
		#if HISTORY_LOG || TICK_LOG
		var state = new T[dst.Length];
		Array.Copy(dst, state, dst.Length);
		StatesHistory.Add(state);
		#endif
		ReadTMP = !ReadTMP;
	}

	public void DeliverPost(uint timestep, byte stage)
	{
		// Roots.Clear();
		// Roots.AddRange(RootsTMP);
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		Post.Process(timestep, stage, dst);

		ReadTMP = !ReadTMP;
	}

	public void ProcessTransactions(uint timestep, byte stage)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		for(int substanceIndex = 0; substanceIndex < Transactions.Buffer.Count; ++substanceIndex)
			if (Transactions.Buffer[substanceIndex].Count > 0)
			{
				var buffer = Transactions.Buffer[substanceIndex];

				//accumulate all transactions on per-agent basis
				//so positive sum means the agent will in sum receive some substance
				//negative sum means it will donate some
				var sumPerAgent = new float[src.Length];
				for(int j = 0; j < buffer.Count; ++j)
				{
					var amount = buffer[j].Amount;
					sumPerAgent[buffer[j].SrcIndex] -= amount;
					sumPerAgent[buffer[j].DstIndex] += amount;
				}

				//decrease the requested amount if the capacity is not sufficient
				var scale = new float[src.Length];
				Array.Fill(scale, 1f);
				var anyScale = false;
				for(int d = 0; d < sumPerAgent.Length; ++d)
				{
					var dstCapacity = GetCapacity(d, substanceIndex);
					if (sumPerAgent[d] > dstCapacity)
					{
						scale[d] = dstCapacity / sumPerAgent[d];
						anyScale = true;
					}
				}

				//apply the decrease and recompute the sums
				var updated = new float[buffer.Count];
				if (anyScale)
				{
					Array.Fill(sumPerAgent, 0f);
					for(int j = 0; j < buffer.Count; ++j)
					{
						var d = buffer[j].DstIndex;
						var amount =  buffer[j].Amount * scale[d];
						updated[j] = amount;
						sumPerAgent[buffer[j].SrcIndex] -= amount;
						sumPerAgent[d] += amount;
					}
					Array.Fill(scale, 1f);
				}
				for(int j = 0; j < buffer.Count; ++j)
					updated[j] =  buffer[j].Amount;

				//decrease the donated amount if the requests are higher as the available amount
				for(int s = 0; s < sumPerAgent.Length; ++s)
				{
					var srcAmount = GetAmount(s, substanceIndex);
					if (-sumPerAgent[s] > srcAmount)
						scale[s] = srcAmount / -sumPerAgent[s];
				}

				//apply the decrease and fire respective messages
				for(int j = 0; j < buffer.Count; ++j)
				{
					var s = buffer[j].SrcIndex;
					var amount = updated[j] * scale[s];
					if (amount > 0f)
					{
						var d = buffer[j].DstIndex;
						dst[s].ChangeAmount(Plant, s, substanceIndex, amount, increase: false);
						dst[d].ChangeAmount(Plant, d, substanceIndex, amount, increase: true);
					}
				}
			}

		ReadTMP = !ReadTMP;
		Transactions.Clear();
	}

	public bool HasUndeliveredPost => Post.AnyMessages;

	public bool HasUnprocessedTransactions => Transactions.AnyTransactions;

	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public IList<int> GetChildren(int index) => TreeCache.GetChildren(index);
	public int GetAbsDepth(int index) => TreeCache.GetAbsDepth(index);
	public float GetRelDepth(int index) => TreeCache.GetRelDepth(index);
	public ICollection<int> GetRoots() => TreeCache.GetRoots();

	internal float GetEnergyCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].EnergyStorageCapacity : 0f)
		: (Agents.Length > index ? Agents[index].EnergyStorageCapacity : 0f);

	internal float GetWaterStorageCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WaterStorageCapacity : 0f)
		: (Agents.Length > index ? Agents[index].WaterStorageCapacity : 0f);

	internal float GetWaterTotalCapacity(int index) => ReadTMP
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

	public Quaternion GetDirection(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Orientation : Quaternion.Identity)
		: (Agents.Length > index ? Agents[index].Orientation : Quaternion.Identity);

	public OrganTypes GetOrgan(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Organ : OrganTypes.Stem)
		: (Agents.Length > index ? Agents[index].Organ : OrganTypes.Stem);

	public float GetWoodRatio(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WoodRatio : 0f)
		: (Agents.Length > index ? Agents[index].WoodRatio : 0f);

	public Vector3 GetBaseCenter(int index) => TreeCache.GetBaseCenter(index);
	// {
	// 	if (ReadTMP ? AgentsTMP.Length <= index : Agents.Length <= index)
	// 		return Vector3.Zero;

	// 	var parents = new List<int>(TreeCache.GetAbsDepth(index)){index};
	// 	if (ReadTMP)
	// 		do {parents.Add(AgentsTMP[parents[^1]].Parent); }
	// 		while(parents[^1] >= 0 && parents.Count <= AgentsTMP.Length);
	// 	else
	// 		do parents.Add(Agents[parents[^1]].Parent);
	// 		while(parents[^1] >= 0 && parents.Count <= Agents.Length);

	// 	var result = Plant.Position;
	// 	if (ReadTMP)
	// 		for(int i = parents.Count - 2; i > 0; --i)
	// 			result += Vector3.Transform(Vector3.UnitX, AgentsTMP[parents[i]].Orientation) * AgentsTMP[parents[i]].Length;
	// 	else
	// 		for(int i = parents.Count - 2; i > 0; --i)
	// 			result += Vector3.Transform(Vector3.UnitX, Agents[parents[i]].Orientation) * Agents[parents[i]].Length;

	// 	// var better = TreeCache.GetBaseCenter(index);
	// 	// Debug.Assert(Math.Abs(better.X - result.X) < 1e-6 && Math.Abs(better.Y - result.Y) < 1e-6 && Math.Abs(better.Z - result.Z) < 1e-6);

	// 	return result;
	// }

	public Vector3 GetScale(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Scale : Vector3.Zero)
		: (Agents.Length > index ? Agents[index].Scale : Vector3.Zero);

	float GetCapacity(int index, int substanceIndex) => substanceIndex switch {
		(byte)PlantSubstances.Water => GetWaterTotalCapacity(index),
		(byte)PlantSubstances.Energy => GetEnergyCapacity(index),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	float GetAmount(int index, int substanceIndex) => substanceIndex switch {
		(byte)PlantSubstances.Water => GetWater(index),
		(byte)PlantSubstances.Energy => GetEnergy(index),
		_ => throw new IndexOutOfRangeException($"SubstanceIndex out of range: {substanceIndex}")
	};

	public float GetVolume() => (ReadTMP ? AgentsTMP : Agents).Aggregate(0f, (sum, current) => sum + current.Scale.X * current.Scale.Y * current.Scale.Z);

		public float GetIrradiance(int index) => IrradianceClient.GetIrradiance(this, index);

	#endregion

	///////////////////////////
	#region WRITE METHODS
	///////////////////////////
	//THERE ARE NO WRITE METHODS ALLOWED except for these via messages.
	#endregion

	///////////////////////////
	#region LOG
	///////////////////////////
	#if HISTORY_LOG || TICK_LOG
	List<T[]> StatesHistory = new();
	public string HistoryToJSON(int timestep = -1, byte stage = 0) => timestep >= 0 ? Utils.Export.Json(StatesHistory[timestep]) : Utils.Export.Json(StatesHistory);

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
