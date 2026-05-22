using System.Numerics;
using AgentsSystem;
using Utils;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using M = System.Runtime.CompilerServices.MethodImplAttribute;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using Agro;
using System.Collections;

namespace Agro.DelunayTerrain;

public class SoilFormationTetrahedral : ISoilFormation
{
	const MethodImplOptions AI = MethodImplOptions.AggressiveInlining;
	AgroWorld World;
	public string ID { get; private set; }

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

	/// <summary>
	/// Cells count in all directions (x, depth, z)
	/// </summary>
	RainCatcher[] RainCatchers;
	readonly float[] CellVolumes;

    readonly Vector3 Position;

	/// <summary>
	/// Contains all cells for which a requestexists
	/// </summary>
    readonly HashSet<int> RequestsPresent;
	/// <summary>
    /// Water request in gramms. For seed requests, Part < 0
    /// </summary>
	readonly List<(PlantFormation2 Plant, int Part, float Amount_g)>[] WaterRequests;

    Vector3[] Points;
    readonly List<Hyperface> Faces;
    readonly SimplexIndexes[] Tetrahedralization;
    static readonly FacesSharing[] PossibleFaces = Enum.GetValues<FacesSharing>();
    List<NeighborData>[] Neighs;
    float[,] DiffusionMatrix;

    readonly bool DoVirtual = true;
    float TotalRaincatch;
    float TotalWaterCapacity;
    float TotalCellVolume;
    float TotalWater_g;
    float TotalRequested;
    List<(PlantFormation2 Plant, int Part, float Amount_g)> TotalWaterRequests;

	public SoilFormationTetrahedral(AgroWorld world, string name, Vector3[] vertices, IList<int> faces, List<List<int>> soilFaces)
	{
		World = world;
        ID = name;

        var vertexSet = new HashSet<int>(faces.Count * 3);
        foreach(var f in faces)
            vertexSet.UnionWith(soilFaces[f]);

        var vertexArray = vertexSet.ToArray();
        Array.Sort(vertexArray);
        var vertexMap = new Dictionary<int, int>();

        var regularCount = vertexArray.Length;
        Points = new Vector3[regularCount];
        Faces = new List<Hyperface>(faces.Count);

        for(int i = 0; i < vertexArray.Length; ++i)
        {
            var v = vertexArray[i];
            vertexMap[v] = i;
            Points[i] = vertices[v];
        }

        Position = Points[0];
        for(int i = 1; i < regularCount; ++i)
            Position = Vector3.Min(Position, Points[i]);
        //find the closest point
        var minDist = Vector3.DistanceSquared(Position, Points[0]);
        var closestToMin = 0;
        for(int i = 1; i < regularCount; ++i)
        {
            var d = Vector3.DistanceSquared(Position, Points[i]);
            if (d < minDist)
            {
                minDist = d;
                closestToMin = i;
            }
        }
        Position = Points[closestToMin];

        var pointsInt = new Vector3int[regularCount + 8];
        var pointsInclude = new BitArray(regularCount);
        var mergedMapping = new HashSet<Vector3int>(regularCount);
        for(int i = 0; i < regularCount; ++i)
        {
            Points[i] -= Position;
            var pi = new Vector3int(Points[i]);
            pointsInt[i] = pi;
            pointsInclude.Set(i, mergedMapping.Add(pi));
        }

        List<int> badTetrahedrons = new(128);
        HashSet<Hyperface> polyhedronSet = [];

        var tetrahedralization = new TetrahedronsSoA(regularCount);

        SuperTetrahedron(pointsInt);

        var sizeHint = pointsInt.Length / 3;
        if (tetrahedralization.Capacity < sizeHint)
            tetrahedralization.Capacity = sizeHint;

        tetrahedralization.CreateInitial(pointsInt);
        var currentQueue = new Queue<int>();
        for (int p = 0; p < regularCount; ++p) //find all the tetrahedrons that are no longer valid due to the insertion
            if (pointsInclude.Get(p))
                currentQueue.Enqueue(p);

        var nextQueue = new Queue<int>();
        while (currentQueue.Count > 0)
        {
            foreach(var p in currentQueue)
            {
                //1. Indentify indices of bad tetrahedrons (breaking the Delaunay criterion)
                badTetrahedrons.Clear();
                tetrahedralization.CheckBad(pointsInt[p], badTetrahedrons);

                //2. Get the polyhedron spanning through all bad tetrahedrons
                polyhedronSet.Clear();
                polyhedronSet.EnsureCapacity(badTetrahedrons.Count << 2);
                tetrahedralization.ToggleFaces(badTetrahedrons, polyhedronSet);

                //Re-tetrahedralize the star-shaped polyhedral hole:
                //3. Preparation of new tetrahedra
                tetrahedralization.TryCreate(polyhedronSet, pointsInt, p, badTetrahedrons, nextQueue);
            }

            if (nextQueue.Count > 0 && nextQueue.Count < currentQueue.Count)
            {
                currentQueue.Clear();
                (nextQueue, currentQueue) = (currentQueue, nextQueue);
            }
            else
                currentQueue.Clear();
        }

        //done inserting points, now clean up
        //remove vertices from the original super-tetrahedron and all incident tetrahedra
        tetrahedralization.RemoveSupertetras();
        Tetrahedralization = tetrahedralization.Indices;
        Array.Resize(ref Tetrahedralization, tetrahedralization.Count);

        CellVolumes = new float[Tetrahedralization.Length];
        for(int t = 0; t < Tetrahedralization.Length; ++t)
            CellVolumes[t] = Tetrahedralization[t].Volume(Points);

        var raincatchers = new List<RainCatcher>(Tetrahedralization.Length);
        Neighs = new List<NeighborData>[Tetrahedralization.Length];
        for(int t = 0; t < Tetrahedralization.Length; ++t)
        {
            var neighFaces = FacesSharing.None;
            Neighs[t] = [];
            for(int s = t + 1; s < Tetrahedralization.Length; ++s)
            {
                var conn = Tetrahedralization[t].Shares(FacesSharing.None, Tetrahedralization[s]);
                if (conn != FacesSharing.None)
                {
                    neighFaces |= conn;
                    var (antigravityRatio, area) = Tetrahedralization[t].NeighInterface(Points, conn);
                    if (antigravityRatio < 0f)
                        Neighs[t].Add(new (s, -antigravityRatio * area));
                }
            }

            var raincatch = 0f;
            //for faces without neighbors check whether it is able to catch rain
            foreach(var face in PossibleFaces)
                if (!neighFaces.HasFlag(face))
                {
                    Faces.Add(Tetrahedralization[t].GetFace(face));
                    var (upwardsRatio, area) = Tetrahedralization[t].NeighInterface(Points, face);
                    if (upwardsRatio > 0f)
                        raincatch += area * upwardsRatio;
                }

            if (raincatch > 0f)
                raincatchers.Add(new(t, raincatch));
        }

        RainCatchers = [..raincatchers];

        //replace the faces by the originals (workaround as long as numeric issues have not been fixes)
        // Faces.Clear();
        // foreach(var f in faces)
        // {
        //     var face = soilFaces[f];
        //     Span<int> abc = [vertexMap[face[0]], vertexMap[face[1]], vertexMap[face[2]]];
        //     MemoryExtensions.Sort(abc);
        //     Faces.Add(new(abc[0], abc[1], abc[2]));
        // }

		Water_g = new float[Tetrahedralization.Length];
		Temperature = new float[Water_g.Length];
		Steam = new float[Water_g.Length];
        WaterCapacityPerCell = new float[Water_g.Length];
        MinimumWaterToDiffuse = new float[Water_g.Length];

		RequestsPresent = new HashSet<int>(Water_g.Length);
		WaterRequests = new List<(PlantFormation2, int, float)>[Water_g.Length];
		for (int i = 0; i < Water_g.Length; ++i)
			WaterRequests[i] = [];

        for(int i = 0; i < Tetrahedralization.Length; ++i)
        {
            Water_g[i] = CellVolumes[i] * 100f * 1000; //some basic water (100 litres per m³ which is kind of normal soil saturation of loamy soils which retain the an average amount of water)
		    WaterCapacityPerCell[i] = CellVolumes[i] * 200f * 1000;
            Debug.Assert(WaterCapacityPerCell[i] >= 0f);
            MinimumWaterToDiffuse[i] = WaterCapacityPerCell[i] * 0.001f;
        }

		if (World != null)
			ComputeDiffusionCoefs();
	}

    void SuperTetrahedron(Vector3int[] points)
    {
        //Rough approximation by a bounding box
        //https://computergraphics.stackexchange.com/questions/10533/how-to-compute-a-bounding-tetrahedron
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue, maxZ = int.MinValue;
        var regularCount = points.Length - 8;
        for (int i = 0; i < regularCount; ++i)
        {
            if (points[i].X < minX) minX = points[i].X;
            if (points[i].Y < minY) minY = points[i].Y;
            if (points[i].Z < minZ) minZ = points[i].Z;

            if (points[i].X > maxX) maxX = points[i].X;
            if (points[i].Y > maxY) maxY = points[i].Y;
            if (points[i].Z > maxZ) maxZ = points[i].Z;
        }

        var center = new Vector3int(minX + maxX, minY + maxY, minZ + maxZ) / 2;
        var diameter = maxX - minX;
        var tmp = maxY - minY;
        if (tmp > diameter)
            diameter = tmp;
        tmp = maxZ - minZ;
        if (tmp > diameter)
            diameter = tmp;

        var radius = (6 * diameter + 10) / 11; //with a bit of a reserve
        var minRequired = (diameter + 1) / 2;
        if (radius < minRequired)
            radius = minRequired;

        //add a super tetrahedron
        points[^8] = center + new Vector3int(-radius, -radius, -radius);
        points[^7] = center + new Vector3int( radius, -radius, -radius);
        points[^6] = center + new Vector3int(-radius,  radius, -radius);
        points[^5] = center + new Vector3int( radius,  radius, -radius);
        points[^4] = center + new Vector3int(-radius, -radius,  radius);
        points[^3] = center + new Vector3int( radius, -radius,  radius);
        points[^2] = center + new Vector3int(-radius,  radius,  radius);
        points[^1] = center + new Vector3int( radius,  radius,  radius);

#if DEBUG
        for (int i = 0; i < regularCount; i++)
        {
            var p = points[i];
            Debug.Assert(p.X >= center.X - radius && p.X <= center.X + radius);
            Debug.Assert(p.Y >= center.Y - radius && p.Y <= center.Y + radius);
            Debug.Assert(p.Z >= center.Z - radius && p.Z <= center.Z + radius);
        }
#endif
    }

    void DebugExport(int step, bool onlyInner)
    {
        var ts = DateTime.Now.Ticks;
        var writer = new StringBuilder();

        for(int i = 0; i < Points.Length; ++i)
            writer.AppendLine($"v {Points[i].X:F8} {Points[i].Y:F8} {Points[i].Z:F8}");

        var regularCount = Points.Length;
        for(int i = 0; i < Tetrahedralization.Length; ++i)
        {
            var ind = Tetrahedralization[i];
            if (!onlyInner || (ind.A < regularCount && ind.B < regularCount && ind.C < regularCount && ind.D < regularCount))
            {
                writer.AppendLine($"g Tetra_{step}__{i:D2}");

                writer.AppendLine($"\t f {ind.A + 1} {ind.B + 1} {ind.C + 1}");
                writer.AppendLine($"\t f {ind.A + 1} {ind.B + 1} {ind.D + 1}");
                writer.AppendLine($"\t f {ind.A + 1} {ind.C + 1} {ind.D + 1}");
                writer.AppendLine($"\t f {ind.B + 1} {ind.C + 1} {ind.D + 1}");
            }
        }
        File.WriteAllText($"debug_{ts}_{step}.obj", writer.ToString());
    }

	public int FieldsCount => 1;

	/// <summary>
	/// Water amount currently stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWater_g(int index, int soilIndex = 0) => DoVirtual ? TotalWater_g : (index >= 0 && index < Water_g.Length ? Water_g[index] : 0f);

	/// <summary>
	/// Maximum water amount that can be stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWaterCapacity_g(int index) => DoVirtual ? TotalWaterCapacity : WaterCapacityPerCell[index];

	[M(AI)] public float GetTemperature(int index, int soilIndex) => 20f;

	[M(AI)] public Vector3 GetFieldOrigin(int soilIndex) => Position;

	public int IntersectPoint(Vector3 center, int soilIndex = 0)
	{
        if (DoVirtual)
            return 0;
        else
        {
            for(int i = 0; i < Tetrahedralization.Length; ++i)
                if (Tetrahedralization[i].PointInside(Points, center))
                    return i;

            return -1;
        }
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
	readonly float[] WaterCapacityPerCell;

	/// <summary>
	/// Minimum water amount to be retained in the cell (in gramms) before the water diffuses further down
	/// </summary>
	readonly float[] MinimumWaterToDiffuse;

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

        var rain = World.GetWater(timestep);
		if (DoVirtual)
        {
            if (rain > 0)
            {
                TotalWater_g += rain * TotalRaincatch;
                if (TotalWaterCapacity < TotalWater_g)
                    TotalWater_g = TotalWaterCapacity;
            }
        }
        else
        {
            //1. Receive RAIN
            if (rain > 0)
                for(int i = 0; i < RainCatchers.Length; ++i)
                {
                    var index = RainCatchers[i].Index;
                    Water_g[index] += rain * RainCatchers[i].ProjectedArea; //in gramms, shadowing not taken into account
                    if (Water_g[index] > WaterCapacityPerCell[index])
                        Water_g[index] = WaterCapacityPerCell[index];
                }

            //diffusion
            var count = Tetrahedralization.Length;

            var availables = new float[count];
            var received = new float[count];
            var removed = new float[count];
            for(int i = 0; i < count; ++i)
                availables[i] = Water_g[i] - MinimumWaterToDiffuse[i];

            for(int j = 0; j < count; ++j)
                if (availables[j] > 0)
                    for(int i = 0; i < count; ++i)
                    {
                        var w = DiffusionMatrix[i, j] * availables[j];
                        received[i] += w;
                        removed[j] += w;
                    }

            for(int i = 0; i < count; ++i)
            {
                var w = Water_g[i] + received[i] - removed[i];
                if (w <= 0f)
                    Water_g[i] = 0f;
                if (WaterCapacityPerCell[i] >= w)
                    Water_g[i] = w;
                else
                    Water_g[i] = WaterCapacityPerCell[i];
                //TODO redistribute the remaining water
                Water_g[i] = WaterCapacityPerCell[i];
            }
        }
        //Debug.WriteLine($"T: {timestep}\tID:{ID}\tR:{rain}\tA:{removed.Sum()}\tR:{received.Sum()}");

		HasUndeliveredPost = true; //enforcing ProcessRequests() this way, since it must wait until all other agents have made requests, it needs to be part of the post delivery
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
		lock(RequestsPresent)
        {
            if (DoVirtual)
            {
                if (TotalWater_g > 0)
                {
                    TotalRequested += amount_g;
                    TotalWaterRequests.Add((plant.Plant, part, amount_g));
                }
            }
            else
            {
                if (Water_g[index] > 0)
                {
                    RequestsPresent.Add(index);
                    WaterRequests[index].Add((plant.Plant, part, amount_g));
                }
            }
        }
	}

	[M(AI)]
	public void RequestWater(int index, float amount_g, PlantFormation2 plant, int soilIndex = 0)
	{
		lock(RequestsPresent)
        {
            if (DoVirtual)
            {
                TotalRequested += amount_g;
                TotalWaterRequests.Add((plant, -1, amount_g));
            }
            else
            {
                if (Water_g[index] > 0)
                {
                    RequestsPresent.Add(index);
                    WaterRequests[index].Add((plant, -1, amount_g));
                }
            }
        }
	}

	public void ProcessRequests()
	{
        if (DoVirtual)
        {
            if (TotalWater_g > 0)
            {
                var limit = TotalWaterRequests.Count;
                if (TotalRequested < TotalWater_g)
                {
                    for (int j = 0; j < limit; ++j)
                    {
                        if (TotalWaterRequests[j].Part < 0)
                            TotalWaterRequests[j].Plant.Send(0, new SeedAgent.WaterInc(TotalWaterRequests[j].Amount_g));
                        else
                            TotalWaterRequests[j].Plant?.UG?.SendProtected(TotalWaterRequests[j].Part, new UnderGroundAgent.WaterInc(TotalWaterRequests[j].Amount_g));
                    }
                }
                else
                {
                    var factor = TotalWater_g / TotalRequested; //reduce all constributions so that the sum is exactly Water_g and not more
                    for (int j = 0; j < limit; ++j)
                        if (TotalWaterRequests[j].Part < 0)
                            TotalWaterRequests[j].Plant.Send(0, new SeedAgent.WaterInc(TotalWaterRequests[j].Amount_g * factor));
                        else
                            TotalWaterRequests[j].Plant?.UG?.SendProtected(TotalWaterRequests[j].Part, new UnderGroundAgent.WaterInc(TotalWaterRequests[j].Amount_g * factor));
                }
            }

            TotalRequested = 0f;
            TotalWaterRequests.Clear();
        }
        else
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


	}

	public Vector3 GetRandomSeedPosition(Pcg rnd, int soilIndex = 0)
	{
        var index = rnd.NextUInt((uint)RainCatchers.Length);
        return Tetrahedralization[RainCatchers[index].Index].GetRandomPoint(Points, rnd, 0f, 0.04f);
	}

	public void SetWorld(AgroWorld world)
	{
		World = world;
		if (World != null)
            ComputeDiffusionCoefs();
    }

    private void ComputeDiffusionCoefs()
    {
        if (DoVirtual)
        {
            TotalWaterRequests = [];
            TotalRaincatch = 0f;
            TotalWaterCapacity = 0f;
            TotalCellVolume = 0f;
            TotalWater_g = 0f;

            for(int i = 0; i < RainCatchers.Length; ++i)
                TotalRaincatch += RainCatchers[i].ProjectedArea;

            for(int i = 0; i < Tetrahedralization.Length; ++i)
            {
                TotalWaterCapacity += WaterCapacityPerCell[i];
                TotalCellVolume += CellVolumes[i];
                TotalWater_g += Water_g[i];
            }
        }
        else
        {
            var heights = new float[Tetrahedralization.Length];
            for(int i = 0; i < Tetrahedralization.Length; ++i)
                heights[i] = SimplexIndexes.Height(Points);

            DiffusionMatrix = new float[Tetrahedralization.Length, Tetrahedralization.Length];
            var travelPerTick = WaterTravelDistPerTick();
            for(int t = 0; t < Tetrahedralization.Length; ++t)
            {
                var buffer = new Queue<NeighborQueueItem>();
                for(int i = 0; i < Neighs[t].Count; ++i)
                {
                    DiffusionMatrix[Neighs[t][i].Index, t] += Neighs[t][i].Ratio * 0.5f;
                    var remainingDistance = travelPerTick - heights[t] - heights[i];
                    if (remainingDistance > 0)
                        buffer.Enqueue(new(Neighs[t][i].Index, 1, remainingDistance));
                }

                while (buffer.Count > 0)
                {
                    var item = buffer.Dequeue();
                    for(int i = 0; i < Neighs[item.Index].Count; ++i)
                    {
                        DiffusionMatrix[Neighs[item.Index][i].Index, t] += Neighs[item.Index][i].Ratio / (2 << item.Level);
                        var remainingDistance = item.RemainingDistance - heights[item.Index];
                        if (remainingDistance > 0)
                            buffer.Enqueue(new(Neighs[item.Index][i].Index, (ushort)(item.Level + 1), remainingDistance));
                    }
                }
            }

            //Normalize; [x,y] means flow from y to x so we need to sum and normalize over the y dimension
            for(int i = 0; i < Tetrahedralization.Length; ++i)
            {
                double sum = 0.0;
                for(int j = 0; j < Tetrahedralization.Length; ++j)
                    sum += DiffusionMatrix[i, j];
                if (sum > 0)
                    for(int j = 0; j < Tetrahedralization.Length; ++j)
                        DiffusionMatrix[i, j] = (float)(DiffusionMatrix[i, j] / sum);
            }
        }
    }

    public byte[] Serialize()
	{
		using var stream = new MemoryStream();
		using var writer = new BinaryWriter(stream);

		writer.Write((byte)0); //version
		var count = FieldsCount;
		writer.Write(count);
		for (int i = 0; i < count; ++i)
			Write(writer, i);

		return stream.ToArray();
    }

	public void Write(BinaryWriter writer, int fieldIndex)
	{
        writer.Write((byte)1); //type
		writer.Write(ID);
        writer.WriteV32(Position);
        writer.Write(Points.Length);
        for(int i = 0; i < Points.Length; ++i)
            writer.WriteV32(Points[i]);

        writer.Write(Faces.Count);
        for(int i = 0; i < Faces.Count; ++i)
        {
            writer.Write(Faces[i].IA);
            writer.Write(Faces[i].IB);
            writer.Write(Faces[i].IC);
        }
	}


	[M(AI)]
	public float GetMetricGroundDepth(float x, float z, int soilIndex = 0)
	{
		return 0f;
	}

    public readonly struct RainCatcher
    {
        public readonly int Index;
        public readonly float ProjectedArea; //check if this is computed by a valid formula (triangle area multiplied by the cosine of its roll angle)

        public RainCatcher(int index, float projectedArea)
        {
            Index = index;
            ProjectedArea = projectedArea;
        }
    }

    public readonly struct NeighborData
    {
        public readonly int Index;
        public readonly float Ratio;
        public NeighborData(int index, float ratio)
        {
            Index = index;
            Ratio = ratio;
        }
    }

    public readonly struct NeighborQueueItem
    {
        public readonly int Index;
        public readonly float RemainingDistance;
        public readonly ushort Level;
        public NeighborQueueItem(int index, ushort level, float remainingDist)
        {
            Index = index;
            Level = level;
            RemainingDistance = remainingDist;
        }
    }
}

