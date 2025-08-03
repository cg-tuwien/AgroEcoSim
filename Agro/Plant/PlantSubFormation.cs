using AgentsSystem;
using glTFLoader.Schema;
using Innovative.Geometry;
using NumericHelpers;
using System;
using System.Buffers.Text;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Utils;

namespace Agro;

public partial class PlantSubFormation<T> : IFormation where T: struct, IPlantAgent
{
	readonly Action<T[], int[]> Reindex;

	public readonly PlantFormation2 Plant;
	//Once GODOT supports C# 6.0: Make it a List and then for processing send System.Runtime.InteropServices.CollectionsMarshal.AsSpan(Stems);
	bool ReadTMP = false;
	T[] Agents = Array.Empty<T>();
	T[] AgentsTMP = Array.Empty<T>();
	readonly PostBox<T> Post = new();
	readonly List<T> Births = new();
	readonly HashSet<int> Deaths = new();
	readonly List<int> DeathsHelper = new();

	readonly TreeCacheData TreeCache = new();

	public PlantSubFormation(PlantFormation2 plant, Action<T[], int[]> reindex, bool isAboveGround)
	{
		Plant = plant;
		Reindex = reindex;
		IsAboveGround = isAboveGround;
	}

	internal float DailyResourceMax { get; private set; }
	internal float DailyProductionMax { get; private set; }
	internal float DailyEfficiencyMax { get; private set; }
	internal float Height => TreeCache.Height;
	readonly bool IsAboveGround;

	public bool CheckIndex(int index) => index < Agents.Length;

	public bool Alive => Agents.Length > 0 || Births.Count > 0;
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

	public List<int>? Death(int index)
	{
		List<int> result = null;
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
						{
							result ??= new();
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
		if (Births.Count > 0 || Deaths.Count > 0)
		{
			//Debug.WriteLine($"{typeof(T).Name} census event: B = {Births.Count}   I = {Inserts.Count}   D = {Deaths.Count}");
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

			var diff = Births.Count - Deaths.Count;
			Debug.Assert(src.Length + diff >= 0);

			//filter out addidions to death parts
			BitArray? birthsHelper = null;
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
				}
			}

			var tmp = diff != 0 ? new T[src.Length + diff] : dst;

			if (Deaths.Count > 0)
			{
				var indexMap = new int[src.Length + Births.Count];
				Array.Fill(indexMap, -1);

				int a = 0;
				{
					if (DeathsHelper[0] > 0)
					{
						var stop = DeathsHelper[0];
						Array.Copy(src, 0, tmp, 0, stop);
						for(int s = 0; s < stop; ++s)
							indexMap[s] = a++;
					}

					var deathsCount = DeathsHelper.Count;
					for(int d = 1; d < deathsCount; ++d)
					{
						var range = DeathsHelper[d] - DeathsHelper[d - 1];
						if (range > 1) //there were some alive inbewteen
						{
							var s = DeathsHelper[d - 1] + 1;
							Array.Copy(src, s, tmp, a, range - 1);
							var stop = DeathsHelper[d];
							while(s < stop)
								indexMap[s++] = a++;
						}
					}

					if (DeathsHelper[^1] < src.Length)
					{
						Array.Copy(src, DeathsHelper[^1] + 1, tmp, a, src.Length - 1 - DeathsHelper[^1]);
						for(int s = DeathsHelper[^1] + 1; s < src.Length; ++s)
							indexMap[s] = a++;
					}
				}

				var birthsCount = Births.Count;
				for(int i = 0; i < birthsCount; ++i)
					if (birthsHelper?.Get(i) ?? true)
					{
						indexMap[src.Length + i] = a;
						tmp[a++] = Births[i];
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
				Births.CopyTo(tmp, a);
				a += Births.Count;
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
			TreeCache.UpdateBases(this);

			Births.Clear();
		}
		else
			TreeCache.UpdateBases(this);
	}

	public void Tick(uint timestep)
	{
		var (src, dst) = SrcDst();
		Array.Copy(src, dst, src.Length);
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Tick(this, i, timestep);

		ReadTMP = !ReadTMP;
	}


	List<GatherDataBase> Gathering = new();

	public PlantGlobalStats Gather()
	{
		var world = Plant.World;
		var energy = 0.0;
		var water = 0.0;
		var energyDiff = 0.0;
		var waterDiff = 0.0;
		var energyCapacity = 0.0;
		var waterCapacity = 0.0;
		var energyRequirement = 0.0;
		var waterRequirement = 0.0;
		var (dst, src) = SrcDst(); //since Tick already swapped them
		var prevLength = Gathering.Count;

		if (prevLength > dst.Length)
			Gathering.RemoveRange(dst.Length, Gathering.Count - dst.Length);

		for(int i = prevLength; i < dst.Length; ++i)
			Gathering.Add(default);

		for(int i = 0; i < dst.Length; ++i)
		{
			//Debug.WriteLine($"{(IsAboveGround ? "⊤" : "⊥")}-{i} Energy {dst[i].Energy} Water {dst[i].Water} Res {dst[i].PreviousDayEnvResourcesInv} / {DailyResourceMax} Prod {dst[i].PreviousDayProductionInv} / {DailyProductionMax} Life {dst[i].LifeSupportPerTick(world)} wStore {dst[i].WaterStorageCapacity()} wStore {dst[i].EnergyStorageCapacity()}");
			energy += Math.Max(0f, dst[i].Energy);
			water += dst[i].Water;

			var previousEnergy = src[i].Energy;
			energyDiff -= previousEnergy;
			waterDiff -= src[i].Water;

			var lifeSupport = dst[i].LifeSupportPerTick(world);
			energyRequirement += lifeSupport;

			float photosynthSupport;
			if (IsAboveGround)
			{
				photosynthSupport = dst[i].PhotosynthPerTick(world);
				waterRequirement += photosynthSupport;
			}
			else
				photosynthSupport = 0;

			var energyStorageCapacity = dst[i].EnergyStorageCapacity();
			energyCapacity += energyStorageCapacity;

			var waterStorageCapacity = dst[i].WaterStorageCapacity();
			waterCapacity += waterStorageCapacity;

			Gathering[i] = new(lifeSupport, photosynthSupport, energyStorageCapacity, waterStorageCapacity, dst[i].PreviousDayEnvResourcesInv / DailyResourceMax, dst[i].PreviousDayProductionInv / DailyProductionMax);
		}

		//this is faster than probing .Contains() for each i
		foreach(var i in Deaths)
		{
			Gathering[i] = default;
			energyRequirement -= dst[i].LifeSupportPerTick(world);
			waterRequirement -= dst[i].PhotosynthPerTick(world);
			energyCapacity -= dst[i].EnergyStorageCapacity();
			waterCapacity -= dst[i].WaterStorageCapacity();
		}

		energyDiff += energy; //optimal variant of sum(dst[i] - src[i])
		waterDiff += water;

		return new PlantGlobalStats() {
			Energy = energy,
			Water = water,
			EnergyDiff = energyDiff,
			WaterDiff = waterDiff,
			EnergyCapacity = energyCapacity,
			WaterCapacity = waterCapacity,
			EnergyRequirementPerTick = energyRequirement,
			WaterRequirementPerTick = waterRequirement,
			Gathering = Gathering,
		};
	}

	//work in progress
	List<float> Weights = new();
	List<int> WeightReoslveChildren = new();

	internal void Gravity()
	{
        if (!IsAboveGround)
            return;

        var dst = Src(); //since Tick already swapped them
        var count = dst.Length;

        Dictionary<Vector3, List<int>> grid = new();
        Dictionary<int, Vector3> agentCellMap = new();
        float cellSize = dst.Max(a => Math.Max(a.Radius, a.Length));

        if (count == 0)
            return;

        if (Weights.Count < count)
            Weights.AddRange(new float[count - Weights.Count]);
        if (WeightReoslveChildren.Count < count)
            WeightReoslveChildren.AddRange(new int[count - WeightReoslveChildren.Count]);
		
        //initialize weights with self weight converted to Newtons
        for (int i = 0; i < count; ++i)
        {
            var weight = GetWeight(i, Plant.Parameters) * 0.001f * 9.81f;
            Weights[i] = weight;
            WeightReoslveChildren[i] = GetChildren(i).Count;
        }
        //accumulate weights from leaves up
        var leavesToProcess = new List<int>(GetLeaves());
        var internalNodesToProcess = new List<int>();
        while (leavesToProcess.Count > 0)
        {
            internalNodesToProcess.Clear();
            foreach (var idx in leavesToProcess)
            {
                var parent = GetParent(idx);
                if (parent >= 0)
                {
                    Weights[parent] += Weights[idx];
                    if (--WeightReoslveChildren[parent] == 0)
                        internalNodesToProcess.Add(parent);
                }
            }
            (leavesToProcess, internalNodesToProcess) = (internalNodesToProcess, leavesToProcess);
        }



        //process nodes from roots to leaves, propagating rotations
		float tickSpeed= Plant.World.HoursPerTick;

        var nodesToVisit = new Queue<int>(GetRoots());
        while (nodesToVisit.Count > 0)
        {
            float depthFactor = Plant.Parameters.DepthFactor;

            var i = nodesToVisit.Dequeue();
            ref var agent = ref dst[i];

            var length = agent.Length;
            var radius = agent.Radius;
            var totalWeight = Weights[i];

            float woodiness = agent.WoodRatio();
            float depthAttenuation = MathF.Exp(-GetAbsDepth(i) * depthFactor);

			float baseDebthstiffness = 1e9f;

            float elasticity = (MathF.Max(Plant.Parameters.GreenElasticModulus, baseDebthstiffness * depthAttenuation)) * (1 - woodiness) + Plant.Parameters.WoodElasticModulus * woodiness;
            Quaternion rotation = Quaternion.Identity;
            if (length > 0f && radius > 0f && totalWeight > 0f)
            {
                var bendingMoment = totalWeight * length * 0.5f;
                var inertia = MathF.PI * MathF.Pow(radius, 4) / 4f;
                var curvature = bendingMoment / (elasticity * inertia);
                var deltaTheta = curvature * length * tickSpeed;

               

                var dir = Vector3.Transform(Vector3.UnitX, agent.Orientation);

                float angleToDown = MathF.Acos(Math.Clamp(Vector3.Dot(dir, Vector3.UnitY), -1f, 1f));
                float maxAllowedBend = MathF.PI - angleToDown;
                float safeDelta = Math.Clamp(deltaTheta,-maxAllowedBend, maxAllowedBend);
                var axis = Vector3.Cross(dir, Vector3.UnitY);
                var len = axis.Length();
                if (len > 1e-6f)
                {
                    axis /= len;
                    rotation = Quaternion.CreateFromAxisAngle(axis, -safeDelta);
                    agent.SetOrientation(Quaternion.Normalize(rotation * agent.Orientation));
					Vector3 start = GetBaseCenter(i);
                    Vector3 center = start + Vector3.Transform(Vector3.UnitX, GetDirection(i)) * (agent.Length / 2);
                    Vector3 key = GridKey(center, cellSize);
                    if (!grid.TryGetValue(key, out var list))
                        grid[key] = list = new();
                    grid[key].Add(i);
					agentCellMap[i] = key;
                }
                
            }

            foreach (var child in GetChildren(i))
            {
                ref var c = ref dst[child];
                if (rotation != Quaternion.Identity)
                    c.SetOrientation(Quaternion.Normalize(rotation * c.Orientation));

				if (agentCellMap.TryGetValue(child, out var oldKey))
				{
					if (grid.TryGetValue(oldKey, out var oldList))
					{
                        oldList.Remove(child);
					}
				}

                Vector3 newStart = GetBaseCenter(child);
                Vector3 newCenter = newStart + Vector3.Transform(Vector3.UnitX, GetDirection(child)) * (c.Length / 2f);
                Vector3 newKey = GridKey(newCenter, cellSize);
                if (!grid.TryGetValue(newKey, out var list))
                {
                    list = new List<int>();
                    grid[newKey] = list;
                }
                list.Add(child);
                grid[newKey]= list;
                agentCellMap[child] = newKey;
                nodesToVisit.Enqueue(child);
            }
        }
        collisionHandling(grid);
    }
    internal Vector3 GridKey(Vector3 pos, float cellSize) => new((int)(pos.X / cellSize), (int)(pos.Y / cellSize), (int)(pos.Z / cellSize));

    [StructLayout(LayoutKind.Auto)]
    readonly struct DirtyCell
    {
        public readonly Vector3 Key;
        public readonly HashSet<int> BranchesToCheck;

        //public readonly bool Dir;

        public DirtyCell(Vector3 key, HashSet<int> branchesToCheck)
        {
            Key = key;
			BranchesToCheck = branchesToCheck;
        }
    }
    internal void collisionHandling(Dictionary<Vector3, List<int>> grid)
	{
        var dst = Src();
        float cellSize = dst.Max(a => Math.Max(a.Radius, a.Length))*1.5f;
        Queue<DirtyCell> dirtyCells = new();
        foreach (var key in grid.Keys)
        {
            dirtyCells.Enqueue(new DirtyCell(key, new HashSet<int>())); 
        }
        while (dirtyCells.Count > 0)
        {
            var cell = dirtyCells.Dequeue();
            if (!grid.TryGetValue(cell.Key, out var cellCollisions) || cellCollisions.Count < 2)
                continue;
            for (int i = 0; i < cellCollisions.Count; ++i)
			{
                
                //Console.WriteLine($"{cell.Key} - {cell.BranchesToCheck.Count()} - {cellCollisions.Count()}");
                if (cell.BranchesToCheck.Count() > 0)
				{
                    if (!cell.BranchesToCheck.Contains(i))
						continue;
				}
                for (int j = i + 1; j < cellCollisions.Count; ++j)
				{
                    //Console.WriteLine($"{i}-{j}");
                    int index1 = cellCollisions[i];
                    int index2 = cellCollisions[j];

					if (BranchIntersect(index1, index2))
					{
						bool isParentChild = isDescendant(index1,index2) || isDescendant(index2,index1);
                        var closestPoints = ClosestPointBetweenBranches(index1, index2);
                        if (isParentChild)
						{
							Vector3 Point1;

							Vector3 Point2;
							int index;
                            if (isDescendant(index1, index2))
							{
								Point1 = closestPoints.point1 + Vector3.Normalize(closestPoints.Point2 - closestPoints.point1) * dst[index1].Radius;
								Point2 = closestPoints.Point2 + Vector3.Normalize(closestPoints.point1 - closestPoints.Point2) * dst[index2].Radius;
								index = index2;
							}
							else 
                            {
                                Point1 = closestPoints.point1 + Vector3.Normalize(closestPoints.point1 - closestPoints.Point2) * dst[index2].Radius;
                                Point2 = closestPoints.Point2 + Vector3.Normalize(closestPoints.Point2 - closestPoints.point1) * dst[index1].Radius;
								index = index1;
                            }
                            Vector3 abAlt = Vector3.Normalize(Point2 - GetBaseCenter(index));
                            Vector3 cbAlt = Vector3.Normalize(Point1 - GetBaseCenter(index));
                            float dotAlt = Vector3.Dot(abAlt, cbAlt);

                            float angleAlt = (float)(MathF.Acos(Math.Clamp(dotAlt, -1f, 1f)));

                            var axisAlt = Vector3.Cross(abAlt, cbAlt);
                            if (angleAlt > 0f && axisAlt.LengthSquared() > 0)
                            {
                                var rot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axisAlt), angleAlt);
                                dst[index].SetOrientation(Quaternion.Normalize(rot * dst[index].Orientation));
                                markChildCellsDirty(ref dirtyCells, index, cellSize);
                            }
							continue;
                        }


						Vector3 adjustedPoint1 = closestPoints.point1 + Vector3.Normalize(closestPoints.Point2 - closestPoints.point1) * dst[index1].Radius;
						Vector3 adjustedPoint2 = closestPoints.Point2 + Vector3.Normalize(closestPoints.point1 - closestPoints.Point2) * dst[index2].Radius;

						Vector3 weightedMidPoint = ((adjustedPoint1 * Weights[index1] + adjustedPoint2 * Weights[index2])
							/ (Weights[index1] + Weights[index2]));
						Vector3 ab = Vector3.Normalize(weightedMidPoint - GetBaseCenter(index1));
						Vector3 cb = Vector3.Normalize(adjustedPoint1 - GetBaseCenter(index1));
						float dot = Vector3.Dot(ab, cb);

						float angle = (float)(MathF.Acos(Math.Clamp(dot, -1f, 1f)));

						var axis = Vector3.Cross(cb, ab);
						if (angle > 0f && axis.LengthSquared() > 0)
						{
							var rot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
							dst[index1].SetOrientation(Quaternion.Normalize(rot * dst[index1].Orientation));
                            markChildCellsDirty(ref dirtyCells, index1, cellSize);
                        }

						ab = Vector3.Normalize(weightedMidPoint - GetBaseCenter(index2));
						cb = Vector3.Normalize(adjustedPoint2 - GetBaseCenter(index2));
						dot = Vector3.Dot(ab, cb);

						angle = (float)(MathF.Acos(Math.Clamp(dot, -1f, 1f)));

						axis = Vector3.Cross(cb, ab);
						if (angle > 0f && axis.LengthSquared() > 0)
						{
                            var rot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
							dst[index2].SetOrientation(Quaternion.Normalize(rot * dst[index2].Orientation));
							markChildCellsDirty(ref dirtyCells, index2, cellSize);
						}

					}
                }

            }
		}
	}
    HashSet<Vector3> seenCells = new();
    private void markChildCellsDirty (ref Queue<DirtyCell> dirtyCells,int index, float size)
	{
        var dst = Src();
        Vector3 start = GetBaseCenter(index);
        Vector3 center = start + Vector3.Transform(Vector3.UnitX, GetDirection(index)) * (dst[index].Length / 2);
        Vector3 key = GridKey(center, size);
        if (seenCells.Add(key))
		{
			var cell = new DirtyCell(key, new HashSet<int>());
			cell.BranchesToCheck.Add(index);
			dirtyCells.Enqueue(cell);
		}
		foreach (var child in GetChildren(index))
		{
            if (dst[child].Organ == OrganTypes.Leaf || dst[child].Organ == OrganTypes.Petiole)
                continue;
            markChildCellsDirty(ref dirtyCells, child, size);
		}
	}
	private bool isDescendant(int index, int indexDescendant)
	{
		int i = indexDescendant;
		while (index != i && GetAbsDepth(i) > 0)
		{
			if (index == GetParent(i))
				return true;
			i = GetParent(i);
		}
		return false;
	}
	private (Vector3 point1, Vector3 Point2, float distance) ClosestPointBetweenBranches(int index1, int index2)
	{
        var dst = Src();

        Vector3 u = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, GetDirection(index1))) * dst[index1].Length;
        Vector3 v = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, GetDirection(index2))) * dst[index2].Length;

        Vector3 w = GetBaseCenter(index1) - GetBaseCenter(index2);

        float a = Vector3.Dot(u, u);
        float b = Vector3.Dot(u, v);
        float c = Vector3.Dot(v, v);
        float d = Vector3.Dot(u, w);
        float e = Vector3.Dot(v, w);
        float denom = a * c - b * b;

        float s = 0f, t = 0f;

        if (denom > 1e-6f)
        {
            s = (b * e - c * d) / denom;
            t = (a * e - b * d) / denom;
        }
        s = Math.Clamp(s, 0f, 1f);
        t = Math.Clamp(t, 0f, 1f);
        Vector3 cp1 = GetBaseCenter(index1) + s * u;
        Vector3 cp2 = GetBaseCenter(index2) + t * v;
        float distance = Vector3.Distance(cp1, cp2);

		return (cp1, cp2, distance);
    }
    private Vector3 GetTipPosition(int index)
    {
        var dst = Src();
        var direction = Vector3.Transform(Vector3.UnitX, dst[index].Orientation); // Local X axis
        return GetBaseCenter(index) + direction * dst[index].Length;
    }
    private bool BranchIntersect(int index1, int index2)
	{
		if (index1==index2)
			return false;
        if (GetParent(index1) == index2 || GetParent(index2) == index1)
            return false;

        var dst = Src();
        Vector3 center1 = GetBaseCenter(index1) + Vector3.Transform(Vector3.UnitX, GetDirection(index1)) * (dst[index1].Length * 0.5f);
        Vector3 center2 = GetBaseCenter(index2) + Vector3.Transform(Vector3.UnitX, GetDirection(index2)) * (dst[index2].Length * 0.5f);
        float r1 = dst[index1].Length * 0.5f + dst[index1].Radius;
        float r2 = dst[index2].Length * 0.5f + dst[index2].Radius;

        if (Vector3.DistanceSquared(center1, center2) > (r1 + r2) * (r1 + r2))
            return false;

        if (!baseCheck(index1,index2)|| !baseCheck(index2,index1)) return false;

        if (GetBaseCenter(index1)==GetBaseCenter(index2))
			return false;
        if (dst[index1].Organ == OrganTypes.Leaf)
            return false;
        if (dst[index2].Organ == OrganTypes.Leaf)
            return false;
        var base1 = GetBaseCenter(index1);
        var base2 = GetBaseCenter(index2);
        var tip1 = GetTipPosition(index1);
        var tip2 = GetTipPosition(index2);

        // Exclude if base of one is tip of the other
        if (base1 == tip2 || base2 == tip1 || base1 == base2)
            return false;
        var (p1, p2, distance) = ClosestPointBetweenBranches(index1, index2);


        if (Math.Abs(Math.Acos(Vector3.Dot(Vector3.Normalize(p2-p1), Vector3.Normalize(Vector3.Transform(Vector3.UnitX, GetDirection(index1))))) - (Math.PI / 2f)) > 1e-3f)
		{
			if (distance > dst[index2].Radius)return false;
		}
        if (Math.Abs(Math.Acos(Vector3.Dot(Vector3.Normalize(p1 - p2), Vector3.Normalize(Vector3.Transform(Vector3.UnitX, GetDirection(index2))))) - (Math.PI / 2f)) > 1e-3f)
        {
            if (distance > dst[index1].Radius) return false;
        }

        var dir1 = Vector3.Transform(Vector3.UnitX, dst[index1].Orientation);
        var dir2 = Vector3.Transform(Vector3.UnitX, dst[index2].Orientation);
        if (Vector3.Dot(Vector3.Normalize(dir1), Vector3.Normalize((p2-p1))) < 0f) return false;
        if (Vector3.Dot(Vector3.Normalize(dir2), Vector3.Normalize((p1 - p2))) < 0f) return false;

        return (distance - (dst[index1].Radius + dst[index2].Radius)) < 1e-3;
    }

	private bool baseCheck(int index1, int index2)
	{
        var dst = Src();
        if (isDescendant(index1,index2))
        {
            List<int> tocheck = new List<int>();
            int i = GetParent(index2);
            while (GetChildren(GetParent(i)).Contains(index1))
            {
                tocheck.Add(i);
                i = GetParent(i);
            }
            tocheck.Reverse();
            foreach (int child in tocheck)
            {
                var (b1, b2, distanceBaseToBranch) = ClosestPointBetweenBranches(index1, index2);
                if (GetBaseCenter(child) == b2 && distanceBaseToBranch < dst[index1].Radius)
                    return false;
                else
                {
					return true;
                }
            }
        }
		return true;
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

	internal void Hormones(bool isAG)
	{
		//Auxin
		if (isAG)
		{
			var dst = Src();
			var path = new List<PathData>();
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

	void DistributeAuxin(T[] dst, int initNode, List<PathData> path)
	{
		path.Clear();

		var auxinsReach = Plant.Parameters.AuxinsReach;
		var bufferSrc = new List<(int, float)>();
		var bufferDst = new List<(int, float)>();

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

	internal void Distribute(PlantGlobalStats stats)
	{
		var dst = Src();
		for(int i = 0; i < dst.Length; ++i)
			dst[i].Distribute(stats.ReceivedWater[i], stats.ReceivedEnergy[i]);
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
			var (src, dst) = SrcDst();
			Array.Copy(src, dst, src.Length);
			Post.Process(timestep, dst);

			ReadTMP = !ReadTMP;
		}
	}

	public bool HasUndeliveredPost => Post.AnyMessages;
	///////////////////////////
	#region READ METHODS
	///////////////////////////

	public int GetParent(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Parent : int.MinValue)
		: (Agents.Length > index ? Agents[index].Parent : int.MinValue);
	public IList<int> GetChildren(int index) => TreeCache.GetChildren(index);
	public int GetAbsDepth(int index) => TreeCache.GetAbsDepth(index);
	public int GetAbsInvDepth(int index) => TreeCache.GetAbsInvDepth(index);
	public float GetRelDepth(int index) => TreeCache.GetRelDepth(index);
	public ICollection<int> GetRoots() => TreeCache.GetRoots();
	public ICollection<int> GetLeaves() => TreeCache.GetLeaves();

	internal float GetEnergyCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].EnergyStorageCapacity() : 0f)
		: (Agents.Length > index ? Agents[index].EnergyStorageCapacity() : 0f);

	internal float GetWaterStorageCapacity(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WaterStorageCapacity() : 0f)
		: (Agents.Length > index ? Agents[index].WaterStorageCapacity() : 0f);

	internal float GetWaterTotalCapacity(AgroWorld world, int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].WaterTotalCapacityPerTick(world) : 0f)
		: (Agents.Length > index ? Agents[index].WaterTotalCapacityPerTick(world) : 0f);

	internal float GetWaterEfficientCapacity(AgroWorld world, int index)
	{
		var src = ReadTMP ? AgentsTMP : Agents;
		if (AgentsTMP.Length > index)
		{
			var agent = src[index];
			return agent.WaterStorageCapacity() + agent.PhotosynthPerTick(world);
		}
		else
			return 0f;
	}

	public float GetEnergy(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Energy : 0f)
		: (Agents.Length > index ? Agents[index].Energy : 0f);

	public float GetEnergyFlow_PerTick(AgroWorld world, int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].EnergyFlowToParentPerTick(world) : 0f)
		: (Agents.Length > index ? Agents[index].EnergyFlowToParentPerTick(world) : 0f);

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
		? (AgentsTMP.Length > index ? AgentsTMP[index].WoodRatio() : 0f)
		: (Agents.Length > index ? Agents[index].WoodRatio() : 0f);

	public Vector3 GetBaseCenter(int index) => TreeCache.GetBaseCenter(index);

	public byte GetDominance(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].DominanceLevel : (byte)0)
		: (Agents.Length > index ? Agents[index].DominanceLevel : (byte)0);

	/// <summary>
	/// Production during the previous day, per m² i.e. invariant of size
	/// </summary>
	public float GetDailyProductionInv(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].PreviousDayProductionInv : 0f)
		: (Agents.Length > index ? Agents[index].PreviousDayProductionInv : 0f);

	/// <summary>
	/// Resources allocated during the previous day, per m² i.e. invariant of size
	/// </summary>
	public float GetDailyResourcesInv(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].PreviousDayEnvResourcesInv : 0f)
		: (Agents.Length > index ? Agents[index].PreviousDayEnvResourcesInv : 0f);

	public float GetDailyEfficiency(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].PreviousDayEnvResources : 0f)
		: (Agents.Length > index ? Agents[index].PreviousDayEnvResources : 0f);

	public Vector3 GetScale(int index) => ReadTMP
		? (AgentsTMP.Length > index ? AgentsTMP[index].Scale() : Vector3.Zero)
		: (Agents.Length > index ? Agents[index].Scale() : Vector3.Zero);

	///<summary>
	///Volume in m³
	///</summary>
	public float GetVolume() => (ReadTMP ? AgentsTMP : Agents).Aggregate(0f, (sum, current) => sum + current.Volume());

	///<summary>
	///Weight in g
	///</summary>
	public float GetWeight(int index, SpeciesSettings species)
	{
		var src = ReadTMP ? AgentsTMP : Agents;
		if (index >= src.Length)
			return 0f;
		else
		{
			var woodRatio = src[index].WoodRatio();
			return (woodRatio * species.DensityDryWood + (1f - woodRatio * species.DensityDryStem)) * 1e3f * src[index].Volume() + src[index].Water;
		}
	}

	public float GetAuxins(int index) => ReadTMP
		? (index < AgentsTMP.Length ? AgentsTMP[index].Auxins : 0f)
		: (index < Agents.Length ? Agents[index].Auxins : 0f);

	#endregion

	///////////////////////////
	#region WRITE METHODS
	///////////////////////////
	//THERE ARE NO WRITE METHODS ALLOWED except for these via messages.
	#endregion

	List<byte> ChildrenToWaitFor = new();
	List<uint> LeavesCount = new();
	List<float> Volumes = new();
	List<float> Resources = new();
	List<int> ReadyNodes0 = new(), ReadyNodes1 = new();
	List<int> NewDayIncomplete = new();
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
				var complete = src[i].NewDay(timestep, ticksPerDay); //for all organs as non-leaves also need to be reset to zero
				//assuming only leaves photosynthesize
				if (src[i].Organ == OrganTypes.Leaf)
				{
					if (complete)
					{
						if (DailyResourceMax < src[i].PreviousDayEnvResourcesInv) DailyResourceMax = src[i].PreviousDayEnvResourcesInv;
						if (DailyProductionMax < src[i].PreviousDayProductionInv) DailyProductionMax = src[i].PreviousDayProductionInv;
					}
					else
						NewDayIncomplete.Add(i);

					ReadyNodes0.Add(i);
					ChildrenToWaitFor.Add(0);
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

			if (DailyResourceMax == float.MinValue) //means that all leaves are in incmplete list
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

			Debug.Assert(DailyProductionMax > 0f);

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
							src[parent].DailyAdd(src[i].PreviousDayEnvResourcesInv, src[i].PreviousDayProductionInv);
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
					src[i].DailySet(src[parent].PreviousDayEnvResourcesInv, src[parent].PreviousDayProductionInv, Resources[parent] * rcpScale / Volumes[parent]);
					//src[i].DailySet(Resources[parent], Volumes[parent]);
				}
				else
				{
					var efficiency = Resources[i] * rcpScale / Volumes[i];

					if (efficiency > DailyEfficiencyMax)
						DailyEfficiencyMax = efficiency;

					if (LeavesCount[i] > 1)
						//src[i].DailyDiv(LeavesCount[i]);
						src[i].DailySet(src[i].PreviousDayEnvResourcesInv / LeavesCount[i], src[i].PreviousDayProductionInv / LeavesCount[i], efficiency);
					else
						src[i].DailySet(src[i].PreviousDayEnvResourcesInv, src[i].PreviousDayProductionInv, efficiency);
				}
		}
		else
		{
			for(int i = 0; i < src.Length; ++i)
			{
				if (src[i].NewDay(timestep, ticksPerDay))
				{
					if (DailyResourceMax < src[i].PreviousDayEnvResourcesInv) DailyResourceMax = src[i].PreviousDayEnvResourcesInv;
					if (DailyProductionMax < src[i].PreviousDayProductionInv) DailyProductionMax = src[i].PreviousDayProductionInv;
				}
				else
					NewDayIncomplete.Add(i);
			}

			if (DailyResourceMax == float.MinValue) //means that all roots are in incmplete list
			{
				DailyResourceMax = 1;
				DailyProductionMax = 1;
			}

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
			if (DailyResourceMax < src[i].PreviousDayEnvResourcesInv) DailyResourceMax = src[i].PreviousDayEnvResourcesInv;
			if (DailyProductionMax < src[i].PreviousDayProductionInv) DailyProductionMax = src[i].PreviousDayProductionInv;
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
