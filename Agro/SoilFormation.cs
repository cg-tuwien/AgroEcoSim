using System.Runtime.InteropServices;
using System.Numerics;
using AgentsSystem;
using Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;
using System.Buffers;

namespace Agro;

public interface ISoilFormation : IFormation
{
	int FieldsCount { get; }

	/// <summary>Writes the soil's primary state for a checkpoint: what the simulation has changed,
	/// not the geometry, which the same request rebuilds identically.</summary>
	void WriteState(BinaryWriter writer);
	void ReadState(BinaryReader reader);

	float GetMetricGroundDepth(float x, float z, int soilIndex);
	void ProcessRequests();

	int IntersectPoint(Vector3 center, int soilIndex);
	float GetTemperature(int index, int soilIndex);
	void RequestWater(int index, float amount_g, PlantFormation2 plant, int soilIndex);
	void RequestWater(int index, float amount_g, PlantSubFormation<UnderGroundAgent> plant, int part, int soilIndex);

	float GetWater_g(int index, int soilIndex);

	Vector3 GetRandomSeedPosition(Pcg rnd, int soilIndex);
    void Write(BinaryWriter writer, int i);
    void SetWorld(AgroWorld world);
    byte[] Serialize();
    Vector3 GetFieldOrigin(int soilIndex);

    /// <summary>
    /// Appends the boundary surface of the soil as world space triangles, so that the internal
    /// light model can treat the terrain as an occluder and a reflector. Indices are relative to
    /// the start of <paramref name="vertices"/>.
    /// </summary>
    void AppendSurfaceTriangles(List<Vector3> vertices, List<int> indices);
}

public class SoilFormationRegularVoxels : IGrid3D, ISoilFormation
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	AgroWorld World;
	///<sumary>
	/// Water amount per cell (in gramms)
	///</sumary>
	readonly float[] Water_g;
	///<sumary>
	/// Temperature per cell (in degree Celsius)
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

	/// <summary>
    /// Precomputed coefficiens for vertical diffusion: [diffusionDepth][realDepth][cefficients]
    /// </summary>
	float[][][] DiffusionCoefs;

	public string ID { get; private set; }

	/// <summary>
	/// Cells count in all directions (x, depth, z)
	/// </summary>
	readonly Vector3i Size;
	readonly int SizeXZ;
	readonly float CellSurface;
	readonly float CellVolume;
	/// <summary>
	/// Metric cell size (x, depth, z)
	/// </summary>
	readonly Vector3 CellSize;
	readonly Vector3 CellSize4Intersect;
	readonly Vector3 Position;
	readonly int MaxLevel;
	readonly (ushort X, ushort D, ushort Z)[] CoordsCache;
	//readonly List<(int dst, float amount_g)>[] WaterTransactions;

	/// <summary>
	/// Water request in gramms
	/// </summary>
	//readonly List<(PlantFormation2 Plant, float Amount_g)>[] WaterRequestsSeeds;

	/// <summary>
	/// Water request in gramms
	/// </summary>
	//readonly List<(PlantSubFormation<UnderGroundAgent> Plant, int Part, float Amount_g)>[] WaterRequestsRoots;
	readonly HashSet<int> RequestsPresent;
	/// <summary>
    /// Water request in gramms. For seed requests Part < 0
    /// </summary>
	readonly List<(PlantFormation2 Plant, int Part, float Amount_g)>[] WaterRequests;

	public SoilFormationRegularVoxels(AgroWorld world, string id, Vector3i size, Vector3 metricSize, Vector3 position = default)
	{
		World = world;
		ID = id;
		if (size.X >= ushort.MaxValue - 1 || size.Y >= ushort.MaxValue - 1 || size.Z >= ushort.MaxValue - 1)
			throw new Exception($"Grid resolution in any direction may not exceed {ushort.MaxValue - 1}");
		//Z is depth
		Size = size;
		SizeXZ = size.X * size.Z;

		Position = position;
		CellSize = new(metricSize.X / size.X, metricSize.Y / size.Y, metricSize.Z / size.Z);
		CellSize4Intersect = new(CellSize.X, -CellSize.Y, CellSize.Z);
		CellSurface = CellSize.X * CellSize.Z;
		CellVolume = CellSurface * CellSize.Y;

		//Just for fun a not yet random heightfield
		// var heightfield = new float[size.X, size.Y];
		// for (int x = 0; x < size.X; ++x)
		// 	for (int y = 0; y < size.Y; ++y)
		// 		heightfield[x, y] = metricSize.Y;

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

		var heights = new int[size.X, size.Z];
		for (int x = 0; x < size.X; ++x)
			for (int z = 0; z < size.Z; ++z)
			{
				//float h = Size.Y * (heightField[x, y] / metricSize.Y);
				//var h = Size.Y;
				//heights[x, y] = Math.Clamp((int)Math.Ceiling(h), 1, Size.Y);

				heights[x, z] = Size.Y;
				//Console.WriteLine($"rH({x}, {y}) = {heights[x, y]}");
			}

		GroundAddr = new int[size.X * size.Z];
		GroundLevels = new ushort[GroundAddr.Length];
		var addr = 0;
		MaxLevel = 0;
		for (int z = 0; z < size.Z; ++z) //y must be outer so that neighboring x items stay adjacent
			for (int x = 0; x < size.X; ++x)
			{
				var h = heights[x, z];
				addr += h;
				var a = z * Size.X + x;
				GroundAddr[a] = addr++;
				GroundLevels[a] = (ushort)h;
				if (MaxLevel < h)
					MaxLevel = h;
			}

		if (World != null)
			ComputeDiffusionCoefs();

		//For a agiven x,z the ordering in Water and other compressed 1D arrays goes as: [floor, floor + 1, ... , ground -1, ground] and then goes the next (x,z) pair.

		CoordsCache = new (ushort X, ushort D, ushort Z)[addr];
		for (ushort x = 0; x < size.X; ++x)
			for (ushort z = 0; z < size.Z; ++z)
			{
				var height = GroundLevel(x, z);
				for (ushort h = 0; h <= height; ++h)
					CoordsCache[Index(x, h, z)] = (x, h, z);
			}

		Water_g = new float[addr];
		Temperature = new float[Water_g.Length];
		Steam = new float[Water_g.Length];

		// WaterRequestsSeeds = new List<(PlantFormation2, float)>[Water_g.Length];
		// WaterRequestsRoots = new List<(PlantSubFormation<UnderGroundAgent>, int, float)>[Water_g.Length];
		// for (int i = 0; i < Water_g.Length; ++i)
		// {
		// 	WaterRequestsSeeds[i] = [];
		// 	WaterRequestsRoots[i] = [];
		// }
		RequestsPresent = new HashSet<int>(Water_g.Length);
		WaterRequests = new List<(PlantFormation2, int, float)>[Water_g.Length];
		for (int i = 0; i < Water_g.Length; ++i)
			WaterRequests[i] = [];

		Array.Fill(Water_g, CellVolume * 100f * 1000); //some basic water (100 litres per m³ which is kind of normal soil saturation of loamy soils which retain the an average amount of water)
		//Array.Fill(Water, 1e3f * CellVolume); //some basic steam

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

		WaterCapacityPerCell = CellVolume * 200f * 1000;
		MinimumWaterToDiffuse = WaterCapacityPerCell * 0.001f;
		if (world != null)
			WaterCellsPerStep = WaterTravelDistPerTick() / CellSize.Y;

		// WaterTransactions = new List<(int dst, float amount_g)>[Water_g.Length];
		// for (int i = 0; i < Water_g.Length; ++i)
		// 	WaterTransactions[i] = [];
	}

	public int FieldsCount => 1;

	/// <summary>
	/// Address of the cell at the given coordinates (x, depth, z)
	/// </summary>
	/// <param name="coords"></param>
	/// <returns></returns>
	[M(AI)] public int Index(Vector3i coords) => Ground(coords) - coords.Y;

	/// <summary>
	/// Address of the cell at the given coordinates (x, depth, z)
	/// </summary>
	/// <param name="coords"></param>
	/// <returns></returns>
	[M(AI)] public int Index(int x, int depth, int z) => Ground(x, z) - depth;

	/// <summary>
	/// 3D Integer coordinates of the given index/address (x, depth, z)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public Vector3i Coords(int index) => new(CoordsCache[index].X, CoordsCache[index].D, CoordsCache[index].Z);
	[M(AI)] bool IsGround(int index) => CoordsCache[index].D == GroundLevel(CoordsCache[index].X, CoordsCache[index].Z);

	[M(AI)] public bool Contains(Vector3i coords) => coords.X >= 0 && coords.Y >= 0 && coords.Z >= 0 && coords.X < Size.X && coords.Z < Size.Z && coords.Y <= GroundLevel(coords);
	[M(AI)] public bool Contains(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Size.X && z < Size.Z && y <= GroundLevel(x, z);

	[M(AI)] int Ground(int x, int z) => GroundAddr[z * Size.X + x];
	[M(AI)] int Ground(Vector3i p) => GroundAddr[p.Z * Size.X + p.X];
	[M(AI)] ushort GroundLevel(int x, int z) => GroundLevels[z * Size.X + x];
	[M(AI)] ushort GroundLevel(Vector3i p) => GroundLevels[p.Z * Size.X + p.X];

	[M(AI)]
	(int GroundXY, int Level) GroundWithLevel(Vector3i p)
	{
		var addr = p.Z * Size.X + p.X;
		Debug.Assert(GroundLevels[addr] == (addr == 0 ? GroundAddr[0] : GroundAddr[addr] - GroundAddr[addr - 1] - 1));
		return (GroundAddr[addr], addr == 0 ? GroundAddr[0] : GroundAddr[addr] - GroundAddr[addr - 1] - 1);
	}

	/// <summary>
	/// Water amount currently stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWater_g(int index, int soilIndex = 0) => index >= 0 && index < Water_g.Length ? Water_g[index] : 0f;

	/// <summary>
	/// Water amount currently stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWater(Vector3i index) => GetWater_g(Index(index), 0);

	/// <summary>
	/// Maximum water amount that can be stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWaterCapacity_g(int index) => GetWaterCapacity_g(Coords(index));

	/// <summary>
	/// Maximum water amount that can be stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWaterCapacity_g(Vector3i index) => index.Y == 0 ? float.MaxValue : WaterCapacityPerCell;

	[M(AI)] public float GetTemperature(int index, int soilIndex) => 20f;

	[M(AI)] public int SoilCellIndex(Vector3i coords) => coords.X + (coords.Y + 1) * Size.X + coords.Z * SizeXZ;
	[M(AI)] public Vector3 GetFieldOrigin(int soilIndex) => Position;

	public int IntersectPoint(Vector3 center, int soilIndex = 0)
	{
		// Debug.WriteLine($"{center}");
		center = new Vector3(center.X, center.Y, center.Z) / CellSize4Intersect;

		var iCenter = new Vector3i(center);
		if (iCenter.X >= 0 && iCenter.Z >= 0 && iCenter.X < Size.X && iCenter.Z < Size.Z)
		{
			var (groundAddr, groundLevel) = GroundWithLevel(iCenter);
			if (iCenter.Y < groundLevel)
			{
				// var r = groundAddr - 1 - iCenter.Z;
				// Debug.WriteLine($"{center} -> [{iCenter}] -> ({groundAddr}, {groundLevel}) => {r}  +W {Water[r]}");
				//return new List<int>(){ groundAddr - (iCenter.Z <= MaxLevel - groundLevel ? 1 + iCenter.Z : 0) };
				return iCenter.Y < 0 ? groundAddr : groundAddr - 1 - iCenter.Y; //TODO this is a temporary fix for flat heightfields
			}
		}

		return -1;
	}

	[M(AI)]
	internal float GetMetricHeight(float x, float z, int soilIndex = 0)
	{
		var ixCenter = (int)(x / CellSize.X);
		var izCenter = (int)(z / CellSize.Z);
		return (GroundLevel(ixCenter, izCenter) + 1) * CellSize.Y;
	}

	[M(AI)]
	public float GetMetricGroundDepth(float x, float z, int soilIndex = 0)
	{
		var ixCenter = (int)(x / CellSize.X);
		var izCenter = (int)(z / CellSize.Z);
		return (MaxLevel - GroundLevel(ixCenter, izCenter)) * CellSize.Y;
	}

	/// <summary>
	/// 1g of water travel distance per simulation tick
	/// </summary>
	/// <returns></returns>
	[M(AI)] float WaterTravelDistPerTick() => WaterTravelDistPerTick(World.HoursPerTick); //1g of water can travel so far per tick

	/// <summary>
	/// 1g of water travel distance per hours
	/// </summary>
	/// <param name="hours"></param>
	/// <returns></returns>
	[M(AI)] static float WaterTravelDistPerTick(ushort hours) => hours * 0.012f; //1g of water can travel so far per tick
	/// <summary>
	/// Water amount that can be stored in the cell (in gramms)
	/// </summary>
	readonly float WaterCapacityPerCell;

	/// <summary>
	/// Minimum water amount to be retained in the cell (in gramms) before the water diffuses further down
	/// </summary>
	readonly float MinimumWaterToDiffuse;

	/// <summary>
	/// Number of cells water can pass in one time step. Since initial cell saturation is added at computation time, this is stored as a float.
	/// </summary>
	float WaterCellsPerStep;

	public void Tick(uint timestep)
	{
		//float[] waterSrc, waterTarget, steamSrc, steamTarget, temperatureSrc, temperatureTarget;

		var surfaceTemp = Math.Max(0f, World.GetTemperature(timestep));
		var evaporizationFactorPerHour = 0 * 1e-4f;
		var evaporizationSoilFactorPerStep = MathF.Pow(1f - evaporizationFactorPerHour, World.HoursPerTick);
		var evaporizationSurfaceFactorPerStep = MathF.Pow(1f - Math.Min(1f, evaporizationFactorPerHour * (surfaceTemp * surfaceTemp) / 10), World.HoursPerTick);

		//var sumBefore = Water.Sum();

		//1. Receive RAIN
		var rainPerCell_g = World.GetWater(timestep) * CellSurface; //in gramms, shadowing not taken into account
		if (rainPerCell_g > 0)
			foreach (var ground in GroundAddr)
				Water_g[ground] += rainPerCell_g;

		//3. Soak the water from bottom to the top
		for (int d = MaxLevel - 1; d > 0; --d)
		{
			int c = 0;
			for (int z = 0; z < Size.Z; ++z) //should be in this order to keep adjacency
				for (int x = 0; x < Size.X; ++x)
				{
					var depth = GroundLevels[c]; //GroundLevel(x, z);
					if (d < depth)
					{
						var srcIdx = GroundAddr[c] - d; //Index(x, d, z);
						Debug.Assert(Coords(srcIdx).Y == d);
						var distribute = Water_g[srcIdx] * evaporizationSoilFactorPerStep;

						if (distribute > MinimumWaterToDiffuse)
							GravityDiffusion(srcIdx, distribute - MinimumWaterToDiffuse, depth, d);
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

		for (int i = 0; i < GroundAddr.Length; ++i)
		{
			var srcIdx = GroundAddr[i];
			var distribute = Water_g[srcIdx] * evaporizationSurfaceFactorPerStep;
			if (distribute > 0)
				GravityDiffusion(srcIdx, distribute, GroundLevels[i], 0);
		}

		HasUndeliveredPost = true; //enforcing ProcessRequests() this way, since it must wait until all other agents have made requests, it needs to be part of the post delivery
	}

	private void GravityDiffusion(int srcIdx, float distribute, ushort groundLevel, int currentDepth)
	{
		var cellsPerStep = (int)(WaterCellsPerStep + Math.Min(1f, Water_g[srcIdx] / WaterCapacityPerCell));
		if (cellsPerStep > 0)
		{
			//simple scaling of what portion of water can reach what depth
			var length = Math.Min(cellsPerStep, groundLevel - currentDepth);

			var resolved = 0f;
			var coefs = DiffusionCoefs[cellsPerStep][length];
			for (int h = 0; h < length; ++h)
			{
				var target = srcIdx - h - 1;
				var occupied = Water_g[target];
				if (occupied < WaterCapacityPerCell)
				{
					var targetFreeCapacity = WaterCapacityPerCell - occupied;
					var requested = distribute * coefs[h];
					if (requested < targetFreeCapacity)
					{
						Water_g[target] = occupied + requested;
						resolved += requested;
					}
					else
					{
						Water_g[target] = WaterCapacityPerCell;
						resolved += targetFreeCapacity;
					}
				}
			}

			Water_g[srcIdx] -= resolved;
            var remaining = Water_g[srcIdx] - resolved;
            Debug.Assert(Water_g[srcIdx] >= -1e-5f);
			if (Water_g[srcIdx] < 0) Water_g[srcIdx] = 0;

        }
	}

	[M(AI)] void IFormation.Census() { }

	[M(AI)] public void DeliverPost(uint timestep)
    {
		ProcessRequests();
		HasUndeliveredPost = false;
    }

	public bool HasUndeliveredPost { get; private set; } = false;
	///<summary>
	///Number of agents in this formation
	///</summary>
	public int Count => Water_g.Length;

	[M(AI)]
	public void RequestWater(int index, float amount_g, PlantSubFormation<UnderGroundAgent> plant, int part, int soilIndex = 0)
	{
		lock(RequestsPresent) if (Water_g[index] > 0)
		{
			RequestsPresent.Add(index);
			WaterRequests[index].Add((plant.Plant, part, amount_g));
		}
	}

	[M(AI)]
	public void RequestWater(int index, float amount_g, PlantFormation2 plant, int soilIndex = 0)
	{
		lock(RequestsPresent) if (Water_g[index] > 0)
		{
			RequestsPresent.Add(index);
			WaterRequests[index].Add((plant, -1, amount_g));
		}
	}

	public void ProcessRequests()
	{
		foreach(var i in RequestsPresent)
		{
			var reqs = WaterRequests[i];
			var limit = reqs.Count;
			var sum = 0f;
			for (int j = 0; j < limit; ++j)
				sum += reqs[j].Amount_g;

			if (sum > 0)
			{
				if (sum <= Water_g[i])
				{
					for (int j = 0; j < limit; ++j)
						if (reqs[j].Part < 0)
							reqs[j].Plant.Send(0, new SeedAgent.WaterInc(reqs[j].Amount_g));
						else
							reqs[j].Plant?.UG?.SendProtected(reqs[j].Part, new UnderGroundAgent.WaterInc(reqs[j].Amount_g));
				}
				else
				{
					var factor = Water_g[i] / sum; //reduce all constributions so that the sum is exactly Water_g and not more
					for (int j = 0; j < limit; ++j)
						if (reqs[j].Part < 0)
							reqs[j].Plant.Send(0, new SeedAgent.WaterInc(reqs[j].Amount_g * factor));
						else
							reqs[j].Plant?.UG?.SendProtected(reqs[j].Part, new UnderGroundAgent.WaterInc(reqs[j].Amount_g * factor));
				}
				reqs.Clear();
			}
		}

		RequestsPresent.Clear();
	}

	public Vector3 GetRandomSeedPosition(Pcg rnd, int soilIndex = 0)
	{
		var metricSize = Size * CellSize;
		return new(metricSize.X * rnd.NextFloat(), -rnd.NextPositiveFloat(Math.Min(metricSize.Y, 0.04f)), metricSize.Z * rnd.NextFloat());
	}

	public void SetWorld(AgroWorld world)
	{
		World = world;
		if (World != null)
        {
            WaterCellsPerStep = WaterTravelDistPerTick() / CellSize.Y;
            ComputeDiffusionCoefs();
        }
    }

    private void ComputeDiffusionCoefs()
    {
        var maxGroundLevel = 0;
        for (int i = 0; i < GroundLevels.Length; ++i)
            if (GroundLevels[i] > maxGroundLevel) maxGroundLevel = GroundLevels[i];

        DiffusionCoefs = new float[maxGroundLevel + 1][][];
        DiffusionCoefs[0] = [[]];
        DiffusionCoefs[1] = [[], [1f]];

        for (int targetTravel = 2; targetTravel <= maxGroundLevel; ++targetTravel)
		{
			DiffusionCoefs[targetTravel] = new float[targetTravel + 1][];
			DiffusionCoefs[targetTravel][0] = [];
			DiffusionCoefs[targetTravel][1] = [1f];
			for (int allowedTravel = 2; allowedTravel <= targetTravel; ++allowedTravel)
			{
				var dc = new float[allowedTravel];
				DiffusionCoefs[targetTravel][allowedTravel] = dc;

				var factor = 0.5f;
				dc[0] = factor;
				var sum = factor;
				for (int j = 1; j < dc.Length; ++j)
				{
					factor *= 0.5f;
					dc[j] = factor;
					sum += factor;
				}

				if (targetTravel > allowedTravel)
				{
					var rest = 0f;
					for (int j = allowedTravel; j < targetTravel; ++j)
					{
						factor *= 0.5f;
						rest += factor;
					}
					sum += rest;

					//now assign the rest proportionally - the most to the deepest cell and the least to the top one
					var restWeightSum = 0.5f * allowedTravel * (allowedTravel + 1); // sum of 1 .. allowedTravel
					rest /= restWeightSum;
					for(int j = allowedTravel - 1; j >= 0; --j)
						dc[j] += rest * (j + 1);
				}

				for (int j = 0; j < dc.Length; ++j)
					dc[j] /= sum;
			}
		}
        //in case the ground was hit earlier distribute the rest among the cells

    }

	/// <summary>
	/// Water, temperature and steam per cell. Everything else this formation holds - cell volumes,
	/// capacities, ground addresses, the surface triangles - is a function of the field geometry,
	/// which the constructor derives from the request and derives the same way every time.
	/// </summary>
	public void WriteState(BinaryWriter writer)
	{
		writer.Write(Water_g.Length);
		writer.Write(MemoryMarshal.AsBytes<float>(Water_g));
		writer.Write(MemoryMarshal.AsBytes<float>(Temperature));
		writer.Write(MemoryMarshal.AsBytes<float>(Steam));
	}

	public void ReadState(BinaryReader reader)
	{
		var count = reader.ReadInt32();
		if (count != Water_g.Length)
			throw new InvalidDataException(
				$"The checkpoint holds {count} soil cells and this field has {Water_g.Length}. " +
				$"The field resolution or size has changed since the run was saved.");
		ReadInto(reader, Water_g);
		ReadInto(reader, Temperature);
		ReadInto(reader, Steam);
	}

	internal static void ReadInto(BinaryReader reader, float[] target)
	{
		var bytes = reader.ReadBytes(target.Length * sizeof(float));
		if (bytes.Length != target.Length * sizeof(float))
			throw new EndOfStreamException("The checkpoint ended inside a soil array.");
		bytes.AsSpan().CopyTo(MemoryMarshal.AsBytes(target.AsSpan()));
	}

    public byte[] Serialize()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);

		writer.Write((byte)1); //version 1: fields, then an obstacle section
		var count = FieldsCount;
		writer.Write(count);
		for (int i = 0; i < count; ++i)
			Write(writer, i);

		//The obstacle section, empty. It is not optional: the single reader of this format
		//(BinaryReader.readTerrain) always reads an obstacle count after the fields, so a stream
		//that stops here throws RangeError past its own last byte. Only SoilFormationsList ever
		//reached that reader before - a box field was never on this wire - and storing runs put it
		//there. Version 1 because the shape is now the one version 1 describes: an obstacle count
		//followed by a group count, both zero when there is nothing to send.
		writer.Write(0);
		writer.Write(0); //no groups

		return stream.ToArray();
    }

	/// <summary>The regular grid is an axis aligned box whose top face sits at
	/// <c>Position.Y</c>; only that face can shade or reflect anything above ground.</summary>
	public void AppendSurfaceTriangles(List<Vector3> vertices, List<int> indices)
	{
		var metricSize = Size * CellSize; //Y of the cell size is the depth
		var offset = vertices.Count;
		vertices.Add(new Vector3(Position.X, Position.Y, Position.Z));
		vertices.Add(new Vector3(Position.X + metricSize.X, Position.Y, Position.Z));
		vertices.Add(new Vector3(Position.X + metricSize.X, Position.Y, Position.Z + metricSize.Z));
		vertices.Add(new Vector3(Position.X, Position.Y, Position.Z + metricSize.Z));
		//Wound so that the face normal points up.
		indices.AddRange([offset, offset + 3, offset + 1, offset + 1, offset + 3, offset + 2]);
	}

	public void Write(BinaryWriter writer, int i)
	{
		writer.Write((byte)0); //type
		var metricSize = Size * CellSize;
		//write position
		writer.WriteV32(Position);
		//write size
		writer.WriteV32(metricSize);
		//write orientation
		writer.WriteQ32(Quaternion.Identity);
		//write ID
		writer.Write(ID);
	}
}

