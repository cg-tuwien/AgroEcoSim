using System;
using System.Numerics;
using System.Collections.Generic;
using AgentsSystem;
using Utils;
using System.Timers;

namespace Agro;

public partial class SoilFormationNew : IFormation, IGrid3D
{
	bool ReadTMP = false;
	///<sumary>
	/// Water amount per cell
	///</sumary>
	readonly float[] Water, WaterTMP;
	///<sumary>
	/// Temperature per cell
	///</sumary>
	readonly float[] Temperature, TemperatureTMP;
	///<sumary>
	/// Steam amount per cell
	///</sumary>
	readonly float[] Steam, SteamTMP;
	///<sumary>
	/// Indexed by (y * width + x) returns the address of the above-ground cell in that column (all under-ground cells are before it)
	///</sumary>
	readonly int[] GroundAddr;
	readonly ushort[] GroundHeights;
	readonly Vector3i Size;
	readonly int SizeXY;
	readonly float CellSurface;
	readonly float CellVolume;
	readonly Vector3 Position;
	readonly Vector3 CellSize;
	readonly float CellCapacity;
	readonly int MaxZ;

	readonly int SubstepSeconds;
	readonly int Substeps;
	readonly static int[] Divisors3600 = { 1, 2, 3, 4, 5, 6, 8, 9, 10, 12, 15, 16, 18, 20, 24, 25, 30, 36, 40, 45, 48, 50, 60, 72, 75, 80, 90, 100, 120, 144, 150, 180, 200, 225, 240, 300, 360, 400, 450, 600, 720, 900, 1200, 1800, 3600 };

	(ushort, ushort, ushort)[] CoordsCache;

	public SoilFormationNew(Vector3i size, Vector3 metricSize, Vector3 position)
	{
		if (size.X >= ushort.MaxValue-1 || size.Y >= ushort.MaxValue-1 || size.Z >= ushort.MaxValue-1)
			throw new Exception($"Grid resolution in any direction may not exceed {ushort.MaxValue-1}");
		//Z is depth
		Size = size;
		SizeXY = size.X * size.Y;
		Position = position;

		CellSize = new Vector3(metricSize.X / size.X, metricSize.Y / size.Y, metricSize.Z / size.Z);
		CellSurface = CellSize.X * CellSize.Y;
		CellVolume = CellSurface * CellSize.Z;
		CellCapacity = CellVolume * 0.45e6f;

		//Just for fun a random heightfield
		var heightfield = new float[size.X, size.Y];
		for(int x = 0; x < size.X; ++x)
			for(int y = 0; y < size.X; ++y)
				heightfield[x, y] = metricSize.Z;

		var rnd = new Random(42);
		var maxRadius = Math.Min(size.X, size.Y) / 2;
		for(int i = 0; i < SizeXY; ++i)
		{
			var (px, py, r) = (rnd.Next(size.X), rnd.Next(size.Y), rnd.Next(maxRadius));
			var s = rnd.NextSingle();
			if (r > 0)
			{
				s -= 0.5f;
				s *= 8f;
				var rcpR = s / (r * size.Z);
				var xLimit = Math.Min(Size.X - 1, px + r);
				var yLimit = Math.Min(Size.Y - 1, py + r);
				for(int x = Math.Max(0, px - r); x <= xLimit; ++x)
					for(int y = Math.Max(0, py - r); y <= yLimit; ++y)
					{
						var d = Vector2.Distance(new(x, y), new(px, py));
						if (d <= r)
							heightfield[x, y] += (r - d) * rcpR;
					}
			}
		}

		var heights = new int[size.X, size.Y];
		for(int x = 0; x < size.X; ++x)
			for(int y = 0; y < size.X; ++y)
			{
				var h = Size.Z * (heightfield[x, y] / metricSize.Z);
				heights[x, y] = Math.Clamp((int)Math.Ceiling(h), 1, Size.Z - 1);
			}

		GroundAddr = new int[size.X * size.Y];
		GroundHeights = new ushort[GroundAddr.Length];
		var addr = 0;
		MaxZ = 0;
		for(int y = 0; y < size.X; ++y) //y must be outer so that neighboring x items stay adjacent
			for(int x = 0; x < size.X; ++x)
			{
				var h = heights[x, y];
				addr += h;
				var a = y * Size.X + x;
				GroundAddr[a] = addr++;
				GroundHeights[a] = (ushort)h;
				if (MaxZ < h)
					MaxZ = h;
			}

		CoordsCache = new (ushort, ushort, ushort)[addr];
		for(ushort x = 0; x < size.X; ++x)
			for(ushort y = 0; y < size.Y; ++y)
			{
				var height = Height(x, y);
				for(ushort z = 0; z <= height; ++z)
					CoordsCache[Index(x, y, z)] = (x, y, z);
			}

		Water = new float[addr];
		WaterTMP = new float[Water.Length];

		Temperature = new float[Water.Length];
		TemperatureTMP = new float[Water.Length];

		Steam = new float[Water.Length];
		SteamTMP = new float[Water.Length];

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

		//let's assume the water in soil flows down 0.5m per minute so 30m per hour
		var minDim = Math.Min(Math.Min(CellSize.X, CellSize.Y) * 2f, CellSize.Z); //*2 since lateralflow is slower
		var substeps = (int)Math.Ceiling(30 / minDim);
		var stepIndex = Array.BinarySearch(Divisors3600, 0, Divisors3600.Length, substeps);
		Substeps = (stepIndex >= 0 ? substeps : Divisors3600[~stepIndex]) * AgroWorld.HoursPerTick;
		SubstepSeconds = AgroWorld.HoursPerTick * 3600 / Substeps;
	}

	public int Index(Vector3i coords) => Ground(coords) - coords.Z;
	public int Index(int x, int y, int depth) => Ground(x, y) - depth;

	public Vector3i Coords(int index) => new(CoordsCache[index].Item1, CoordsCache[index].Item2, CoordsCache[index].Item3);
	bool IsGround(int index) => CoordsCache[index].Item3 == Height(CoordsCache[index].Item1, CoordsCache[index].Item2);

	public bool Contains(Vector3i coords) => coords.X >= 0 && coords.Y >= 0 && coords.Z >= 0 && coords.X < Size.X && coords.Y < Size.Y && coords.Z <= Height(coords);
	public bool Contains(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Size.X && y < Size.Y && z <= Height(x, y);

	int Ground(int x, int y) => GroundAddr[y * Size.X + x];
	int Ground(Vector3i p) => GroundAddr[p.Y * Size.X + p.X];
	ushort Height(int x, int y)
	{
		var addr = y * Size.X + x;
		return (ushort)(addr == 0 ? GroundAddr[0] : GroundAddr[addr] - GroundAddr[addr - 1] - 1);
	}

	ushort Height(Vector3i p)
	{
		var addr = p.Y * Size.X + p.X;
		return (ushort)(addr == 0 ? GroundAddr[0] : GroundAddr[addr] - GroundAddr[addr - 1] - 1);
	}

	public float GetWater(int index) => index >= 0 && index < Water.Length
		? (ReadTMP ? WaterTMP[index] : Water[index])
		: 0f;

	public float GetWater(Vector3i index) => GetWater(Index(index));

	public float GetWaterCapacity(int index) => GetWaterCapacity(Coords(index));

	public float GetWaterCapacity(Vector3i index) => Height(index) == index.Z ? float.MaxValue : CellVolume * 0.45e6f;

	public float GetTemperature(int index) => 20f;

	public int SoilIndex(Vector3i coords) => coords.X + coords.Y * Size.X + (coords.Z + 1) * SizeXY;

	public List<int> IntersectPoint(Vector3 center)
	{
		center = new Vector3(center.X, center.Z, center.Y) - Position;

		center *= CellSize;

		var iCenter = new Vector3i(center);
		var iGroundLevel = Height(iCenter);

		if (iCenter.X >= 0 && iCenter.Y >= 0 && iCenter.Z >= 0 && iCenter.X < Size.X && iCenter.Y < Size.Y)
		{
			return iCenter.Z < iGroundLevel
				? new(){ SoilIndex(iCenter) }
				: new(){ SoilIndex(new Vector3i(iCenter.X, iCenter.Y, iGroundLevel)) };
		}
		else
			return new();
	}

	internal float GetHeight(float x, float z)
    {
		var center = new Vector3(x, z, 0) - Position;

		center *= CellSize;

		var iCenter = new Vector3i(center);
		return (Height(iCenter) + 1) * CellSize.Y;
    }

	static readonly float WaterTravelDist = AgroWorld.HoursPerTick * 0.000008858f; //1g of water can travel so far an hour

	public void Tick(SimulationWorld world, uint timestep, byte stage)
	{
		//float[] waterSrc, waterTarget, steamSrc, steamTarget, temperatureSrc, temperatureTarget;

		var evaporizationFactor = 0.00001f;
		var evaporizationSoilFactorPerStep = MathF.Pow(1f - evaporizationFactor, SubstepSeconds);
		var evaporizationSurfaceFactorPerStep = MathF.Pow(1f - evaporizationFactor * 10f, SubstepSeconds);
		var waterVerticalFlowPerStep = 0.01f * SubstepSeconds;
		var steamVerticalFlowPerStep = 0.2f * SubstepSeconds;

		//1. Receive RAIN
		var rainPerCell = AgroWorld.GetWater(timestep) * CellSurface; //shadowing not taken into account
		if (rainPerCell > 0)
			foreach(var ground in GroundAddr)
				Water[ground] += rainPerCell;

		//2. Evaporate
		foreach(var ground in GroundAddr)



		//3. Soak the water from bottom to the top
		for(int d = MaxZ - 1; d > 0; --d)
		{
			for(int y = 0; y < Size.Y; ++y) //should be in this order to keep adjacency
				for(int x = 0; x < Size.X; ++x)
				{
					var height = Height(x, y);
					if (d < height)
					{
						var srcIdx = Index(x, y, d);
						var distribute = Water[srcIdx] * evaporizationSoilFactorPerStep;

						if (distribute > 0)
							GravityDiffusion(srcIdx, distribute, height, d);
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
                GravityDiffusion(srcIdx, distribute, GroundHeights[i], 0);
        }




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
	}

    private void GravityDiffusion(int srcIdx, float distribute, ushort height, int d)
    {
		var diffuse = true;
        var distPerTimestep_in_m = WaterTravelDist * distribute;
        var cellsPerStep = (int)Math.Ceiling(distPerTimestep_in_m * CellSize.Z);
        var limit = Math.Min(d + cellsPerStep, height);
        var factors = new float[limit - d];
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

        var resolved = 0f;

        for (int h = d + 1; diffuse && h <= limit; ++h)
        {
            var target = srcIdx - h;
            var occupied = Water[target];
            if (occupied < CellCapacity)
            {
                var available = CellCapacity - occupied;
                var requested = distribute * factors[h - d - 1];
                if (requested < available)
                {
                    Water[target] = occupied + requested;
                    resolved += requested;
                }
                else
                {
                    Water[target] = available;
                    resolved += available;
                }
            }
        }
        Water[srcIdx] = distribute - resolved;
    }

    void IFormation.Census() {}

    void IFormation.ProcessTransactions(uint timestep, byte stage) {}
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

	public float RequestWater(int index, float amount)
	{
		var current = Water[index];
		if (amount <= current)
		{
			Water[index] = current - amount;
			return amount;
		}
		else
		{
			Water[index] = 0f;
			return current;
		}
	}
}

