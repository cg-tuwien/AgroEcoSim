using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Security.AccessControl;
using System.Threading.Tasks;
using Utils;

namespace AgentsSystem;


internal class TreeCacheData
{
	public int Count { get; private set; }
	List<int>[] ChildrenNodes;
	ushort[] DepthNodes;
	readonly List<int> Roots = new();
	ushort MaxDepth = 0;

	public TreeCacheData()
	{
		Count = 0;
		ChildrenNodes = new List<int>[]{ new(), new() };
		DepthNodes = new ushort[]{ 0, 0 };
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
}

public abstract class FormationTree<T> : Formation<T> where T : struct, ITreeAgent
{
	readonly Action<T[], int[]> Reindex;

	readonly TransactionsBox Transactions = new();
	readonly List<T> Inserts = new();
	readonly List<int> InsertAncestors = new();

	readonly TreeCacheData TreeCache = new();

	public FormationTree(Action<T[], int[]> reindex)
	{
		Reindex = reindex;
	}

	public bool CheckIndex(int index) => index < Agents.Length;

	public bool Alive => Agents.Length > 0 || Births.Count > 0 || Inserts.Count > 0;
	public override int Count => Agents.Length;

	public void Insert(int ancestor, T agent)
	{
		Inserts.Add(agent);
		InsertAncestors.Add(ancestor);
	}

	public override void Death(int index)
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
			Postbox.Add(new (msg, dst));
			return true;
		}
		else
			return false;
	}

	public override void Census()
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
			Debug.WriteLine($"{typeof(T).Name} census event: B = {Births.Count}   I = {Inserts.Count}   D = {Deaths.Count}");
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

	protected virtual void PrepareDeaths() { }
	protected virtual void PostCensusPositive() { }
	protected virtual void PostCensusNegative() { }

	public override bool HasUnprocessedTransactions => Transactions.AnyTransactions;

	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public IList<int> GetChildren(int index) => TreeCache.GetChildren(index);
	public int GetAbsDepth(int index) => TreeCache.GetAbsDepth(index);
	public float GetRelDepth(int index) => TreeCache.GetRelDepth(index);
	public ICollection<int> GetRoots() => TreeCache.GetRoots();
	#endregion
}
