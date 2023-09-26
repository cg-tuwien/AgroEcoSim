using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;
using System.Timers;
using System.Diagnostics;
using System.Linq;

namespace Agro;

public partial class SoilFormationNew : IFormation, IGrid3D
{
	readonly AgroWorld World;
	///<sumary>
	/// Water amount per cell
	///</sumary>
	readonly float[] Water;
	///<sumary>
	/// Temperature per cell
	///</sumary>
	readonly float[] Temperature;
	///<sumary>
	/// Steam amount per cell
	///</sumary>
	readonly float[] Steam;
	///<sumary>
	/// Indexed by (y * width + x) returns the address of the above-ground cell in that column (all under-ground cells are before it)
	///</sumary>
	readonly int[] GroundAddr;
	readonly ushort[] GroundLevels;
	readonly Vector3i Size;
	readonly int SizeXY;
	readonly float CellSurface;
	readonly float CellVolume;
	readonly Vector3 CellSize;
	readonly Vector3 CellSize4Intersect;
	readonly float WaterCapacityPerCell;
	readonly int MaxLevel;
	readonly (ushort, ushort, ushort)[] CoordsCache;
	readonly List<(int dst, float amount)>[] WaterTransactions;

	readonly List<(PlantFormation2 Plant, float Amount)>[] WaterRequestsSeeds;
	readonly List<(PlantSubFormation2<UnderGroundAgent2> Plant, int Part, float Amount)>[] WaterRequestsRoots;


	public SoilFormationNew(AgroWorld world, Vector3i size, Vector3 metricSize)
	{
		World = world;
		if (size.X >= ushort.MaxValue-1 || size.Y >= ushort.MaxValue-1 || size.Z >= ushort.MaxValue-1)
			throw new Exception($"Grid resolution in any direction may not exceed {ushort.MaxValue-1}");
		//Z is depth
		Size = size;
		SizeXY = size.X * size.Y;

		CellSize = new (metricSize.X / size.X, metricSize.Y / size.Y, metricSize.Z / size.Z);
		CellSize4Intersect = new (CellSize.X, CellSize.Y, -CellSize.Z);
		CellSurface = CellSize.X * CellSize.Y;
		CellVolume = CellSurface * CellSize.Z;
		WaterCapacityPerCell = CellVolume * 0.45e6f;

		//Just for fun a random heightfield
		var heightfield = new float[size.X, size.Y];
		for(int x = 0; x < size.X; ++x)
			for(int y = 0; y < size.Y; ++y)
				heightfield[x, y] = metricSize.Z;

		// var rnd = new Random(42);
		// var maxRadius = Math.Min(size.X, size.Y) / 2;
		// for(int i = 0; i < SizeXY; ++i)
		// {
		// 	var (px, py, r) = (rnd.Next(size.X), rnd.Next(size.Y), rnd.Next(maxRadius));
		// 	var s = rnd.NextSingle();
		// 	if (r > 0)
		// 	{
		// 		s -= 0.5f;
		// 		s *= 16f;
		// 		var rcpR = s / (r * size.Z);
		// 		var xLimit = Math.Min(Size.X - 1, px + r);
		// 		var yLimit = Math.Min(Size.Y - 1, py + r);
		// 		for(int x = Math.Max(0, px - r); x <= xLimit; ++x)
		// 			for(int y = Math.Max(0, py - r); y <= yLimit; ++y)
		// 			{
		// 				var d = Vector2.Distance(new(x, y), new(px, py));
		// 				if (d <= r)
		// 					heightfield[x, y] += (r - d) * rcpR;
		// 			}
		// 	}
		// }

		var heights = new int[size.X, size.Y];
		for(int x = 0; x < size.X; ++x)
			for(int y = 0; y < size.Y; ++y)
			{
				var h = Size.Z * (heightfield[x, y] / metricSize.Z);
				heights[x, y] = Math.Clamp((int)Math.Ceiling(h), 1, Size.Z);
				//Console.WriteLine($"rH({x}, {y}) = {heights[x, y]}");
			}

		GroundAddr = new int[size.X * size.Y];
		GroundLevels = new ushort[GroundAddr.Length];
		var addr = 0;
		MaxLevel = 0;
		for(int y = 0; y < size.Y; ++y) //y must be outer so that neighboring x items stay adjacent
			for(int x = 0; x < size.X; ++x)
			{
				var h = heights[x, y];
				addr += h;
				var a = y * Size.X + x;
				GroundAddr[a] = addr++;
				GroundLevels[a] = (ushort)h;
				if (MaxLevel < h)
					MaxLevel = h;
			}

		//Fora agiven x,y the ordering in Water and other compressed 1D arrays goes as: [floor, floor + 1, ... , ground -1, ground] and then goes the next (x,y) pair.

		CoordsCache = new (ushort, ushort, ushort)[addr];
		for(ushort x = 0; x < size.X; ++x)
			for(ushort y = 0; y < size.Y; ++y)
			{
				var height = GroundLevel(x, y);
				for(ushort z = 0; z <= height; ++z)
					CoordsCache[Index(x, y, z)] = (x, y, z);
			}

		Water = new float[addr];
		Temperature = new float[Water.Length];
		Steam = new float[Water.Length];

		WaterRequestsSeeds = new List<(PlantFormation2, float)>[Water.Length];
		WaterRequestsRoots = new List<(PlantSubFormation2<UnderGroundAgent2>, int, float)>[Water.Length];
		for(int i = 0; i < Water.Length; ++i)
		{
			WaterRequestsSeeds[i] = new();
			WaterRequestsRoots[i] = new();
		}

		Array.Fill(Water, 1e3f * CellVolume); //some basic water
		Array.Fill(Water, 1e3f * CellVolume); //some basic steam

		// const float coldFactor = 0.75f; //earth gets 1 degree colder each x meters (where x is the value of this constant)
		// var airTemp = AgroWorld.GetTemperature(timestep);
		// var bottomTemp = airTemp > 4f ? Math.Max(4f, airTemp - fieldSize.Z * coldFactor) : Math.Min(4f, airTemp + fieldSize.Z * coldFactor);
		// for(var z = 0; z <= size.Z; ++z)
		// {
		// 	var temp = airTemp + (bottomTemp - airTemp) * z / Size.Z;
		// 	for(var x = 0; x < size.X; ++x)
		// 		for(var y = 0; y < size.Y; ++y)
		// 			Temp[Index(x, y, z)] = temp;
		// }

		WaterRetainedPerCell = CellSize.Z / WaterTravelDistPerTick();

		WaterTransactions = new List<(int dst, float amount)>[Water.Length];
		for(int i = 0; i < Water.Length; ++i)
			WaterTransactions[i] = new();
	}

	public int Index(Vector3i coords) => Ground(coords) - coords.Z;
	public int Index(int x, int y, int depth) => Ground(x, y) - depth;

	public Vector3i Coords(int index) => new(CoordsCache[index].Item1, CoordsCache[index].Item2, CoordsCache[index].Item3);
	bool IsGround(int index) => CoordsCache[index].Item3 == GroundLevel(CoordsCache[index].Item1, CoordsCache[index].Item2);

	public bool Contains(Vector3i coords) => coords.X >= 0 && coords.Y >= 0 && coords.Z >= 0 && coords.X < Size.X && coords.Y < Size.Y && coords.Z <= GroundLevel(coords);
	public bool Contains(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Size.X && y < Size.Y && z <= GroundLevel(x, y);

	int Ground(int x, int y) => GroundAddr[y * Size.X + x];
	int Ground(Vector3i p) => GroundAddr[p.Y * Size.X + p.X];
	ushort GroundLevel(int x, int y) => GroundLevels[y * Size.X + x];
	ushort GroundLevel(Vector3i p) => GroundLevels[p.Y * Size.X + p.X];

	(int, int) GroundWithLevel(Vector3i p)
	{
		var addr = p.Y * Size.X + p.X;
		return (GroundAddr[addr], addr == 0 ? GroundAddr[0] : GroundAddr[addr] - GroundAddr[addr - 1] - 1);
	}

	public float GetWater(int index) => index >= 0 && index < Water.Length ? Water[index] : 0f;

	public float GetWater(Vector3i index) => GetWater(Index(index));

	public float GetWaterCapacity(int index) => GetWaterCapacity(Coords(index));

	public float GetWaterCapacity(Vector3i index) => index.Z == 0 ? float.MaxValue : WaterCapacityPerCell;

	public float GetTemperature(int index) => 20f;

	public int SoilIndex(Vector3i coords) => coords.X + coords.Y * Size.X + (coords.Z + 1) * SizeXY;
	static readonly List<int> Empty = new();

	public IReadOnlyList<int> IntersectPoint(Vector3 center)
	{
		// Debug.WriteLine($"{center}");
		center = new Vector3(center.X, center.Z, center.Y);
		center /= CellSize4Intersect;

		var iCenter = new Vector3i(center);
		if (iCenter.X >= 0 && iCenter.Y >= 0 && iCenter.X < Size.X && iCenter.Y < Size.Y)
		{
			var (groundAddr, groundLevel) = GroundWithLevel(iCenter);
			if (iCenter.Z < groundLevel)
			{
				// var r = groundAddr - 1 - iCenter.Z;
				// Debug.WriteLine($"{center} -> [{iCenter}] -> ({groundAddr}, {groundLevel}) => {r}  +W {Water[r]}");
				//return new List<int>(){ groundAddr - (iCenter.Z <= MaxLevel - groundLevel ? 1 + iCenter.Z : 0) };
				return new List<int>(){ groundAddr - 1 - iCenter.Z}; //TODO this is a temporary fix for flat heightfields
			}
		}

		return Empty;
	}

	internal float GetMetricHeight(float x, float z)
	{
		var center = new Vector3(x, z, 0);

		center /= CellSize;

		var iCenter = new Vector3i(center);
		return (GroundLevel(iCenter) + 1) * CellSize.Y;
	}

	internal float GetMetricGroundDepth(float x, float z)
	{
		var center = new Vector3(x, z, 0);

		center /= CellSize;

		var iCenter = new Vector3i(center);
		return (MaxLevel - GroundLevel(iCenter)) * CellSize.Y;
	}

	float WaterTravelDistPerTick() => World.HoursPerTick * 0.000012f; //1g of water can travel so far an hour
	readonly float WaterRetainedPerCell;

	public void Tick(uint timestep, byte stage)
	{
		//float[] waterSrc, waterTarget, steamSrc, steamTarget, temperatureSrc, temperatureTarget;

		var surfaceTemp = Math.Max(0f, World.GetTemperature(timestep));
		var evaporizationFactorPerHour = 0*1e-4f;
		var evaporizationSoilFactorPerStep = MathF.Pow(1f - evaporizationFactorPerHour, World.HoursPerTick);
		var evaporizationSurfaceFactorPerStep = MathF.Pow(1f - Math.Min(1f, evaporizationFactorPerHour * (surfaceTemp * surfaceTemp) / 10), World.HoursPerTick);

		var sumBefore = Water.Sum();

		//1. Receive RAIN
		var rainPerCell = World.GetWater(timestep) * CellSurface; //shadowing not taken into account
		if (rainPerCell > 0)
			foreach(var ground in GroundAddr)
				Water[ground] += rainPerCell;

		//3. Soak the water from bottom to the top
		for(int d = MaxLevel - 1; d > 0; --d)
		{
			for(int y = 0; y < Size.Y; ++y) //should be in this order to keep adjacency
				for(int x = 0; x < Size.X; ++x)
				{
					var depth = GroundLevel(x, y);
					if (d < depth)
					{
						var srcIdx = Index(x, y, d);
						Debug.Assert(Coords(srcIdx).Z == d);
						var distribute = Water[srcIdx] * evaporizationSoilFactorPerStep;

						if (distribute > WaterRetainedPerCell)
							GravityDiffusion(srcIdx, distribute, depth, d, WaterRetainedPerCell);
					}
				}
		}

		//4. Soak in the rain
		//1 inch of rain can penetate 6-12 inches deep
		//soil can take up 0.2-6.0 inches of water per hour

		//25400 g of rain can penetrate 0.1524 - 0.3048 m deep
		//soil can take up 5080 - 152.400 g of water per hour
		//1 m3 of water weights 1.000.000 g
		//by the type of soil, saturation is 30-60% so 300.000 - 600.000 g/m³

		for(int i = 0; i < GroundAddr.Length; ++i)
		{
			var srcIdx = GroundAddr[i];
			var distribute = Water[srcIdx] * evaporizationSurfaceFactorPerStep;
			if (distribute > 0)
				GravityDiffusion(srcIdx, distribute, GroundLevels[i], 0, 0);
		}

		var sumAfter = Water.Sum();
		//Console.WriteLine($"Water in system {sumAfter} {(sumAfter < sumBefore ? "---------" : "")}");

		// //for(int t = 0; t < Substeps; ++t)
		// {
		// 	// if (ReadTMP)
		// 	// {
		// 	// 	waterSrc = WaterTMP;
		// 	// 	steamSrc = SteamTMP;
		// 	// 	temperatureSrc = TemperatureTMP;
		// 	// 	waterTarget = Water;
		// 	// 	steamTarget = Steam;
		// 	// 	temperatureTarget = Temperature;
		// 	// }
		// 	// else
		// 	// {
		// 	// 	waterSrc = Water;
		// 	// 	steamSrc = Steam;
		// 	// 	temperatureSrc = Temperature;
		// 	// 	waterTarget = WaterTMP;
		// 	// 	steamTarget = SteamTMP;
		// 	// 	temperatureTarget = TemperatureTMP;
		// 	// }

		// 	// Array.Fill(waterTarget, 0f);
		// 	// Array.Fill(steamTarget, 0f);
		// 	// Array.Fill(temperatureTarget, 0f);



		// 	for(int i = 0; i < Steam.Length; ++i)
		// 	{
		// 		var vapor = Water[i] * (IsGround(i) ? evaporizationSurfaceFactorPerStep : evaporizationSoilFactorPerStep);
		// 		Water[i] -= vapor;
		// 		Steam[i] += vapor;
		// 	}

		// 	for (int d = 0; d < MaxZ; ++d)
		// 		for(int y = 0; y < Size.Y; ++y) //should be in this order to keep adjacency
		// 			for(int x = 0; x < Size.X; ++x)
		// 			{
		// 				var height = Height(x, y);
		// 				if (d <= height)
		// 				{
		// 					var srcIdx = Index(x, y, d);
		// 					var dstIdx = srcIdx - 1;
		// 					var current = Water[srcIdx];
		// 					var flow = Math.Min(waterVerticalFlowPerStep / (d + 1), current);
		// 					var freeAtTarget = CellVolume - Water[dstIdx];
		// 					flow = Math.Min(flow, freeAtTarget);
		// 					if (flow > 0f)
		// 					{
		// 						Water[dstIdx] += flow;
		// 						Water[srcIdx] = current - flow;
		// 					}
		// 				}
		// 			}

		// 	for (int d = MaxZ; d > 0; --d)
		// 		for(int y = 0; y < Size.Y; ++y) //should be in this order to keep adjacency
		// 			for(int x = 0; x < Size.X; ++x)
		// 			{
		// 				var height = Height(x, y);
		// 				if (d <= height)
		// 				{
		// 					var srcIdx = Index(x, y, d);
		// 					var dstIdx = srcIdx + 1;
		// 					var current = Steam[srcIdx];
		// 					var flow = Math.Min(steamVerticalFlowPerStep / (d + 1), current);
		// 					var freeAtTarget = CellVolume - Steam[dstIdx];
		// 					flow = Math.Min(flow, freeAtTarget);
		// 					if (flow > 0f)
		// 					{
		// 						Steam[dstIdx] += flow;
		// 						Steam[srcIdx] = current - flow;
		// 					}
		// 				}
		// 			}
		// }

		ProcessRequests();
	}

	private void GravityDiffusion(int srcIdx, float distribute, ushort groundLevel, int currentDepth, float retain)
	{
		var distPerTimestep_in_m = WaterTravelDistPerTick() * distribute;
		var cellsPerStep = (int)Math.Ceiling(distPerTimestep_in_m * CellSize.Z);
		if (cellsPerStep > 0)
		{
			var factors = new float[Math.Min(cellsPerStep, groundLevel - currentDepth)];
			if (factors.Length > 1)
			{
				factors[0] = 0.5f;
				var factorsSum = 0.5f;
				for (int f = 1; f < factors.Length; ++f)
				{
					var factor = factors[f - 1] * 0.5f;
					factors[f] = factor;
					factorsSum += factor;
				}
				for (int f = 0; f < factors.Length; ++f)
					factors[f] /= factorsSum;
			}
			else
				//factors[0] = distPerTimestep_in_m / CellSize.Z;
				factors[0] = 1f;

			var resolved = 0f;
			if (distribute > retain)
			{
				distribute -= retain;
				Debug.Assert(Math.Abs(factors.Sum() - 1) < 1e-5f);
				for (int h = 0; h < factors.Length; ++h)
				{
					var target = srcIdx - h - 1;
					var occupied = Water[target];
					if (occupied < WaterCapacityPerCell)
					{
						var available = WaterCapacityPerCell - occupied;
						var requested = distribute * factors[h];
						if (requested < available)
						{
							Water[target] = occupied + requested;
							resolved += requested;
						}
						else
						{
							Water[target] = WaterCapacityPerCell;
							resolved += available;
						}
					}
				}
				Debug.Assert(retain == 0 || Water[srcIdx] - resolved + 1e-5f >= WaterRetainedPerCell);
				Water[srcIdx] -= resolved;
				Debug.Assert(Water[srcIdx] >= 0f);
			}
		}
	}

	void IFormation.Census() {}

	void IFormation.ProcessTransactions(uint timestep, byte stage) { }

	void IFormation.DeliverPost(uint timestep, byte stage) {}
	bool IFormation.HasUndeliveredPost => false;
	bool IFormation.HasUnprocessedTransactions => false;
	public byte Stages => 1;
	#if HISTORY_LOG || TICK_LOG
	string IFormation.HistoryToJSON(int timestep = -1, byte stage = 0) => "";
	#endif
	///<summary>
	///Number of agents in this formation
	///</summary>
	public int Count => Water.Length;

	// public float RequestWater(int index, float amount)
	// {
	// 	var current = Water[index];
	// 	if (amount <= current)
	// 	{
	// 		Water[index] = current - amount;
	// 		return amount;
	// 	}
	// 	else
	// 	{
	// 		Water[index] = 0f;
	// 		return current;
	// 	}
	// 	//WaterTransactions[index].Add((dst, amount));
	// }

	public void RequestWater(int index, float amount, PlantSubFormation2<UnderGroundAgent2> plant, int part)
	{
		if (Water[index] > 0)
			WaterRequestsRoots[index].Add((plant, part, amount));
	}

	public void RequestWater(int index, float amount, PlantFormation2 plant)
	{
		if (Water[index] > 0)
			WaterRequestsSeeds[index].Add((plant, amount));
	}

	void ProcessRequests()
	{
		for(int i = 0; i < WaterRequestsRoots.Length; ++i)
		{
			double sum = 0;

			if (WaterRequestsSeeds[i].Count > 0)
			{
				var reqs = WaterRequestsSeeds[i];
				var limit = reqs.Count;
				for(int j = 0; j < limit; ++j)
					sum += reqs[j].Amount;
			}

			if (WaterRequestsRoots[i].Count > 0)
			{
				var reqs = WaterRequestsRoots[i];
				var limit = reqs.Count;
				for(int j = 0; j < limit; ++j)
					sum += reqs[j].Amount;
			}

			if (sum <= Water[i])
			{
				if (WaterRequestsSeeds[i].Count > 0)
				{
					var reqs = WaterRequestsSeeds[i];
					var limit = reqs.Count;
					for(int j = 0; j < limit; ++j)
						reqs[j].Plant.Send(0, new SeedAgent.WaterInc(reqs[j].Amount));
					reqs.Clear();
				}

				if (WaterRequestsRoots[i].Count > 0)
				{
					var reqs = WaterRequestsRoots[i];
					var limit = reqs.Count;
					for(int j = 0; j < limit; ++j)
						reqs[j].Plant.SendProtected(reqs[j].Part, new UnderGroundAgent2.WaterInc(reqs[j].Amount));
					reqs.Clear();
				}
			}
			else
			{
				var factor = Water[i] / sum;
				if (WaterRequestsSeeds[i].Count > 0)
				{
					var reqs = WaterRequestsSeeds[i];
					var limit = reqs.Count;
					for(int j = 0; j < limit; ++j)
						reqs[j].Plant.Send(0, new SeedAgent.WaterInc((float)(reqs[j].Amount * factor)));
					reqs.Clear();
				}

				if (WaterRequestsRoots[i].Count > 0)
				{
					var reqs = WaterRequestsRoots[i];
					var limit = reqs.Count;
					for(int j = 0; j < limit; ++j)
						reqs[j].Plant.SendProtected(reqs[j].Part, new UnderGroundAgent2.WaterInc((float)(reqs[j].Amount * factor)));
					reqs.Clear();
				}
			}
		}
	}
}

