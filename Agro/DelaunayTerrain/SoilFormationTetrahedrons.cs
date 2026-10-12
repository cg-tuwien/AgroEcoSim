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

public class SoilFormationTetrahedral : ISoilFormation, ISoilRequestSink
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
    readonly List<int> RequestsPresent;
	/// <summary>
    /// Water request in grams. For seed requests, Part is less than zero.
    /// </summary>
	readonly List<(PlantFormation2 Plant, int Part, float Amount_g)>[] WaterRequests;

    Vector3[] Points;
    readonly List<Hyperface> Faces;
    /// <summary>The boundary as it was imported, triangulated and wound outward: index triples
    /// into <see cref="Points"/>. The tetrahedralisation's own boundary is not usable as a
    /// surface - it is numerically fragile and comes out non-manifold on real trays - and the
    /// source mesh is a closed manifold already, which is what an inside/outside test needs.</summary>
    readonly int[] SurfaceTriangles = [];
    readonly SimplexIndexes[] Tetrahedralization;
    static readonly FacesSharing[] PossibleFaces = Enum.GetValues<FacesSharing>();
    List<NeighborData>[] Neighs;
    float[,] DiffusionMatrix;

    readonly bool DoVirtual = true;
    float TotalRaincatch;
    double TotalWaterCapacity;
    float TotalCellVolume;
    double TotalWater_g;
    // Only non-virtual fields need per-cell precision. Keep the legacy float
    // initialization array without doubling the memory of the usual virtual trays.
    double[] FiniteWater_g;
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
        //Which of each tetrahedron's four faces is shared with some other tetrahedron.
        //Accumulated for BOTH members of a pair: the scan below only visits s > t, so a face
        //shared with a lower-indexed neighbour would otherwise never be marked, and every
        //interior face was emitted into Faces as though it were on the boundary. That left the
        //soil surface massively non-manifold - measured on an imported tray, 222 of 246 edges
        //carried more than two triangles - which makes any inside/outside test on it meaningless.
        var sharedFaces = new FacesSharing[Tetrahedralization.Length];
        for(int t = 0; t < Tetrahedralization.Length; ++t)
        {
            Neighs[t] = [];
            for(int s = t + 1; s < Tetrahedralization.Length; ++s)
            {
                var conn = Tetrahedralization[t].Shares(FacesSharing.None, Tetrahedralization[s]);
                if (conn != FacesSharing.None)
                {
                    sharedFaces[t] |= conn;
                    sharedFaces[s] |= Tetrahedralization[s].Shares(FacesSharing.None, Tetrahedralization[t]);
                    var (antigravityRatio, area) = Tetrahedralization[t].NeighInterface(Points, conn);
                    if (antigravityRatio < 0f)
                        Neighs[t].Add(new (s, -antigravityRatio * area));
                }
            }
        }

        for(int t = 0; t < Tetrahedralization.Length; ++t)
        {
            var neighFaces = sharedFaces[t];
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

		RequestsPresent = [];
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

    public SoilFormationTetrahedral(AgroWorld world, string name, Vector3[] vertices, IList<int[]> faces, List<SoilDelimeter> delimeters)
	{
		World = world;
        ID = name;

        //TODO The delimeters should subdivide the tray into sections => clipping the tetrahedral volume

        var vertexSet = new HashSet<int>(faces.Count * 3);
        foreach(var f in faces)
            vertexSet.UnionWith(f);

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

        SurfaceTriangles = BuildSurfaceTriangles(faces, vertexMap, Points);

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
        //Which of each tetrahedron's four faces is shared with some other tetrahedron.
        //Accumulated for BOTH members of a pair: the scan below only visits s > t, so a face
        //shared with a lower-indexed neighbour would otherwise never be marked, and every
        //interior face was emitted into Faces as though it were on the boundary. That left the
        //soil surface massively non-manifold - measured on an imported tray, 222 of 246 edges
        //carried more than two triangles - which makes any inside/outside test on it meaningless.
        var sharedFaces = new FacesSharing[Tetrahedralization.Length];
        for(int t = 0; t < Tetrahedralization.Length; ++t)
        {
            Neighs[t] = [];
            for(int s = t + 1; s < Tetrahedralization.Length; ++s)
            {
                var conn = Tetrahedralization[t].Shares(FacesSharing.None, Tetrahedralization[s]);
                if (conn != FacesSharing.None)
                {
                    sharedFaces[t] |= conn;
                    sharedFaces[s] |= Tetrahedralization[s].Shares(FacesSharing.None, Tetrahedralization[t]);
                    var (antigravityRatio, area) = Tetrahedralization[t].NeighInterface(Points, conn);
                    if (antigravityRatio < 0f)
                        Neighs[t].Add(new (s, -antigravityRatio * area));
                }
            }
        }

        for(int t = 0; t < Tetrahedralization.Length; ++t)
        {
            var neighFaces = sharedFaces[t];
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

		RequestsPresent = [];
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
	[M(AI)] public float GetWater_g(int index, int soilIndex = 0) =>
        (float)(DoVirtual ? TotalWater_g : (index >= 0 && index < Water_g.Length ? FiniteWater_g[index] : 0d));

	/// <summary>
	/// Maximum water amount that can be stored in this cell (in gramms)
	/// </summary>
	/// <param name="index"></param>
	/// <returns></returns>
	[M(AI)] public float GetWaterCapacity_g(int index) => DoVirtual ? (float)TotalWaterCapacity : WaterCapacityPerCell[index];

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
                TotalWater_g += (double)rain * TotalRaincatch;
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
                    FiniteWater_g[index] = Math.Min(WaterCapacityPerCell[index],
                        FiniteWater_g[index] + (double)rain * RainCatchers[i].ProjectedArea); // grams; shadowing not taken into account
                }

            //diffusion
            var count = Tetrahedralization.Length;

            // Snapshot the transport budget before moving anything. Each flux is
            // capped by both the source's remaining water and the destination's
            // free capacity. A saturated cell cannot silently destroy inflow.
            var availables = new double[count];
            for(int i = 0; i < count; ++i)
                availables[i] = Math.Max(0d, FiniteWater_g[i] - MinimumWaterToDiffuse[i]);

            for(int source = 0; source < count; ++source)
                if (availables[source] > 0d)
                    for(int destination = 0; destination < count; ++destination)
                    {
                        if (destination == source)
                            continue;
                        var headroom = WaterCapacityPerCell[destination] - FiniteWater_g[destination];
                        if (headroom <= 0d)
                            continue;
                        var sourceWater = FiniteWater_g[source] - MinimumWaterToDiffuse[source];
                        var transfer = Math.Min(DiffusionMatrix[destination, source] * availables[source],
                            Math.Min(sourceWater, headroom));
                        if (transfer <= 0d)
                            continue;
                        FiniteWater_g[source] -= transfer;
                        FiniteWater_g[destination] += transfer;
                    }
        }
        //Debug.WriteLine($"T: {timestep}\tID:{ID}\tR:{rain}\tA:{removed.Sum()}\tR:{received.Sum()}");

		HasUndeliveredPost = true; //enforcing ProcessRequests() this way, since it must wait until all other agents have made requests, it needs to be part of the post delivery
	}

	[M(AI)] void IFormation.Census() { }

	[M(AI)] public void DeliverPost(uint timestep) => DeliverPost(timestep, true);

	public void DeliverPost(uint timestep, bool gatherRequests)
    {
		if (gatherRequests) SoilRequestRouter.Gather(World, this);
		ProcessRequests();
		HasUndeliveredPost = false;
    }

	public ISoilRequestSink? RequestSink(int soilIndex) => soilIndex == 0 ? this : null;

	void ISoilRequestSink.Enqueue(PlantFormation2 plant, int cell, int part, float amount_g)
	{
		if (DoVirtual) TotalWaterRequests.Add((plant, part, amount_g));
		else
		{
			var requests = WaterRequests[cell];
			if (requests.Count == 0) RequestsPresent.Add(cell);
			requests.Add((plant, part, amount_g));
		}
	}

	public bool HasUndeliveredPost { get; private set; } = false;
	///<summary>
	///Number of agents in this formation
	///</summary>
	public int Count => Water_g.Length;

	[M(AI)]
	public void RequestWater(int index, float amount_g, PlantSubFormation<UnderGroundAgent> plant, int part, int soilIndex = 0)
	{
		if (!float.IsFinite(amount_g) || amount_g <= 0f || plant == null || part < 0)
			return;
		// Buffered on the requesting plant: no lock, and a deterministic order. See SoilRequestRouter.
		if (HasWater(index)) plant.Plant.WaterRequests.Add((index, part, amount_g));
	}

	[M(AI)]
	public void RequestWater(int index, float amount_g, PlantFormation2 plant, int soilIndex = 0)
	{
		if (!float.IsFinite(amount_g) || amount_g <= 0f || plant == null)
			return;
		if (HasWater(index)) plant.WaterRequests.Add((index, -1, amount_g));
	}

	bool HasWater(int index) => DoVirtual ? TotalWater_g > 0 : (uint)index < (uint)Water_g.Length && FiniteWater_g[index] > 0;

    static bool CanReceiveWater((PlantFormation2 Plant, int Part, float Amount_g) request)
    {
        var plant = request.Plant;
        if (plant == null || !float.IsFinite(request.Amount_g) || request.Amount_g <= 0f)
            return false;
        if (request.Part == -1)
            return plant.SeedAlive;
        return request.Part >= 0 && plant.UG is not VirtualRootsFormation &&
            request.Part < plant.UG.Count;
    }

    static void DeliverWater(List<(PlantFormation2 Plant, int Part, float Amount_g)> requests, ref double water_g)
    {
        if (water_g <= 0d || requests.Count == 0)
            return;

        double demand = 0d;
        for (int i = 0; i < requests.Count; ++i)
            if (CanReceiveWater(requests[i]))
                demand += requests[i].Amount_g;

        if (demand <= 0d)
            return;

        // Every valid plant sharing this pool receives the same fraction of its
        // demand. Accumulate in double precision: individual uptake can be much
        // smaller than one float ULP of a large imported tray's water store.
        var fraction = Math.Min(1d, water_g / demand);
        var remaining = water_g;
        for (int i = 0; i < requests.Count && remaining > 0d; ++i)
        {
            var request = requests[i];
            if (!CanReceiveWater(request))
                continue;

            var proposed = Math.Min((double)request.Amount_g * fraction, remaining);
            var amount = (float)proposed;
            if (amount > proposed)
                amount = MathF.BitDecrement(amount);
            if (amount <= 0f)
                continue;

            var sent = request.Part == -1
                ? request.Plant.Send(0, new SeedAgent.WaterInc(amount))
                : request.Plant.UG.SendProtected(request.Part, new UnderGroundAgent.WaterInc(amount));
            if (sent)
                remaining -= amount;
        }

        water_g = Math.Max(0d, remaining);
    }

	public void ProcessRequests()
	{
        if (DoVirtual)
        {
            DeliverWater(TotalWaterRequests, ref TotalWater_g);
            TotalWaterRequests.Clear();
        }
        else
        {
            RequestsPresent.Sort();
            foreach (var i in RequestsPresent)
            {
                var water = FiniteWater_g[i];
                DeliverWater(WaterRequests[i], ref water);
                FiniteWater_g[i] = water;
                WaterRequests[i].Clear();
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
            TotalWater_g = 0d;

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
            if (FiniteWater_g == null)
            {
                FiniteWater_g = new double[Water_g.Length];
                for (int i = 0; i < Water_g.Length; ++i)
                    FiniteWater_g[i] = Water_g[i];
            }

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

            // [destination, source] is the fraction leaving source. Normalize
            // each source column; normalizing rows can remove more water from a
            // cell than it holds and creates an artificial loss when clamped.
            for(int source = 0; source < Tetrahedralization.Length; ++source)
            {
                double sum = 0.0;
                for(int destination = 0; destination < Tetrahedralization.Length; ++destination)
                    sum += DiffusionMatrix[destination, source];
                if (sum > 0)
                    for(int destination = 0; destination < Tetrahedralization.Length; ++destination)
                        DiffusionMatrix[destination, source] =
                            (float)(DiffusionMatrix[destination, source] / sum);
            }
        }
    }

	/// <summary>Water, temperature and steam per tetrahedron; the mesh itself is rebuilt.</summary>
	public void WriteState(BinaryWriter writer)
	{
		writer.Write(Water_g.Length);
		writer.Write(MemoryMarshal.AsBytes<float>(Water_g));
		writer.Write(MemoryMarshal.AsBytes<float>(Temperature));
		writer.Write(MemoryMarshal.AsBytes<float>(Steam));
		writer.Write(DoVirtual);
		writer.Write(TotalWater_g);
		if (!DoVirtual)
			for (int i = 0; i < FiniteWater_g.Length; ++i)
				writer.Write(FiniteWater_g[i]);
	}

	public void ReadState(BinaryReader reader)
	{
		var count = reader.ReadInt32();
		if (count != Water_g.Length)
			throw new InvalidDataException(
				$"The checkpoint holds {count} soil cells and this field has {Water_g.Length}. " +
				$"The field model or resolution has changed since the run was saved.");
		SoilFormationRegularVoxels.ReadInto(reader, Water_g);
		SoilFormationRegularVoxels.ReadInto(reader, Temperature);
		SoilFormationRegularVoxels.ReadInto(reader, Steam);
		if (reader.ReadBoolean() != DoVirtual)
			throw new InvalidDataException("The checkpoint uses a different tetrahedral soil water model.");
		TotalWater_g = reader.ReadDouble();
		if (!DoVirtual)
			for (int i = 0; i < FiniteWater_g.Length; ++i)
				FiniteWater_g[i] = reader.ReadDouble();
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

	/// <summary>
	/// The boundary surface of the soil as world space triangles, wound so that every face normal
	/// points out of the soil.
	/// </summary>
	/// <remarks>
	/// <c>Faces</c> already holds exactly the hull, because a face shared by two tetrahedra cancels
	/// out in <see cref="SimplexIndexes.ToggleFaces"/>. Its index triples are sorted rather than
	/// wound, so the outward direction has to be recovered: every hull face belongs to exactly one
	/// tetrahedron, and pointing the normal away from that tetrahedron's fourth vertex is outward by
	/// construction. The internal light model needs this, because it only lets terrain shade what is
	/// outside it - a seedling starts below the soil surface and must not be shadowed by it.
	/// </remarks>
	/// <summary>
	/// Fan-triangulates the imported boundary polygons and orients them outward.
	/// </summary>
	/// <remarks>
	/// Orientation is settled by the divergence theorem rather than by trusting the source
	/// winding: on a closed surface the signed volume is positive exactly when the winding is
	/// outward, so one sum over the whole mesh decides whether to flip it.
	/// </remarks>
	static int[] BuildSurfaceTriangles(IList<int[]> faces, Dictionary<int, int> vertexMap, Vector3[] points)
	{
		var triangles = new List<int>(faces.Count * 3);
		foreach (var face in faces)
		{
			if (face.Length < 3)
				continue;
			var a = vertexMap[face[0]];
			for (int i = 1; i + 1 < face.Length; ++i)
			{
				triangles.Add(a);
				triangles.Add(vertexMap[face[i]]);
				triangles.Add(vertexMap[face[i + 1]]);
			}
		}

		double signedVolume = 0d;
		for (int t = 0; t + 2 < triangles.Count; t += 3)
		{
			var p0 = points[triangles[t]];
			var p1 = points[triangles[t + 1]];
			var p2 = points[triangles[t + 2]];
			signedVolume += Vector3.Dot(p0, Vector3.Cross(p1 - p0, p2 - p0)) / 6d;
		}

		if (signedVolume < 0d)
			for (int t = 0; t + 2 < triangles.Count; t += 3)
				(triangles[t + 1], triangles[t + 2]) = (triangles[t + 2], triangles[t + 1]);

		return [..triangles];
	}

	public void AppendSurfaceTriangles(List<Vector3> vertices, List<int> indices)
	{
		if (SurfaceTriangles.Length > 0)
		{
			var first = vertices.Count;
			for (int i = 0; i < Points.Length; ++i)
				vertices.Add(Points[i] + Position);
			for (int i = 0; i < SurfaceTriangles.Length; ++i)
				indices.Add(SurfaceTriangles[i] + first);
			return;
		}

		var offset = vertices.Count;
		for (int i = 0; i < Points.Length; ++i)
			vertices.Add(Points[i] + Position);

		//Interior faces appear twice and hull faces once, so any owner found is the right one for a
		//hull face.
		var owner = new Dictionary<Hyperface, int>(Tetrahedralization.Length * 4);
		for (int t = 0; t < Tetrahedralization.Length; ++t)
		{
			var s = Tetrahedralization[t];
			owner[new Hyperface(s.A, s.B, s.C)] = t;
			owner[new Hyperface(s.A, s.B, s.D)] = t;
			owner[new Hyperface(s.A, s.C, s.D)] = t;
			owner[new Hyperface(s.B, s.C, s.D)] = t;
		}

		for (int i = 0; i < Faces.Count; ++i)
		{
			var face = Faces[i];
			var a = face.IA;
			var b = face.IB;
			var c = face.IC;

			if (owner.TryGetValue(face, out var t))
			{
				var s = Tetrahedralization[t];
				var inner = s.A != a && s.A != b && s.A != c ? s.A
					: s.B != a && s.B != b && s.B != c ? s.B
					: s.C != a && s.C != b && s.C != c ? s.C : s.D;
				var normal = Vector3.Cross(Points[b] - Points[a], Points[c] - Points[a]);
				if (Vector3.Dot(normal, Points[a] - Points[inner]) < 0f)
					(b, c) = (c, b);
			}

			indices.Add(a + offset);
			indices.Add(b + offset);
			indices.Add(c + offset);
		}
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
