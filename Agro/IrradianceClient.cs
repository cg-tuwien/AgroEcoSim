//#define EXPORT_OBJ
//#define EXPORT_BIN
//#define USE_TRIANGLES
using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using AgentsSystem;
using Agro;
using System.Globalization;
using System.Collections.Generic;
using System;
using System.Net.Http;
using System.IO;
using System.Security.Cryptography;
using Innovative.SolarCalculator;
using System.Diagnostics;
using System.Linq;

/* Triangle Mesh Binary Serialization
uint8 version = 1
#INDEXED DATA FOR OBSTACLES
uint32 entitiesCount
foreach ENTITY
	uint32 surfacesCount
	foreach SURFACE
		uint8 trianglesCount
		foreach TRIANGLE
			uint32 index0
			uint32 index1
			uint32 index2
#INDEXED DATA FOR SENSORS
uint32 entitiesCount
foreach ENTITY
	uint32 surfacesCount
	foreach SURFACE
		uint8 trianglesCount
		foreach TRIANGLE
			uint32 index0
			uint32 index1
			uint32 index2
#POINTS DATA
uint32 pointsCount
	#foreach POINT
	float32 x
	float32 y
	float32 z
*/

/* Primitives Binary Serialization
uint8 version = 2
#OBSTACLES
uint32 entitiesCount
foreach ENTITY
	uint32 surfacesCount
	foreach SURFACE
		uint8 primitiveType    #1 = disk, 2 = cylinder(stem), 4 = sphere(bud), 8 = rectangle(leaf)
		#case disk
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		#case cylinder
		float32 length
		float32 radius
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		#case sphere
		3xfloat32 center
		float32 radius
		#case rectangle
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
#SENSORS
uint32 entitiesCount
foreach ENTITY
	uint32 surfacesCount
	foreach SURFACE
		uint8 primitiveType    #1 = disk, 2 = cylinder(stem), 4 = sphere(bud), 8 = rectangle(leaf)
		#case disk
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		#case cylinder
		float32 length
		float32 radius
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		#case sphere
		3xfloat32 center
		float32 radius
		#case rectangle
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
*/

public class IrradianceClient
{
	readonly HttpClient Client;
	readonly List<float> Irradiances = new();
	readonly List<int> SkipPlants = new();
	readonly List<Vector3> IrradiancePoints = new();
	readonly Dictionary<IFormation, int[]> IrradianceFormationOffsets = new ();

	readonly bool IsOnline = false;
	bool IsNight = true;

	private IrradianceClient()
	{
		Client = new() { BaseAddress = new Uri("http://localhost:9000"), Timeout = TimeSpan.FromHours(1) };
		Client.DefaultRequestHeaders.Add("La", AgroWorld.Latitude.ToString());
		Client.DefaultRequestHeaders.Add("Lo", AgroWorld.Longitude.ToString());

		//Probe if the client is online, else fallback to constant ambient light
		var request = new HttpRequestMessage() { Method = HttpMethod.Get };
		try
		{
			var result = Client.SendAsync(request).Result;
			IsOnline = result.IsSuccessStatusCode;
		}
		catch (Exception)
		{
			Console.WriteLine("WARNING: No irradiance client responded at http://localhost:9000. Falling back to ambient light pipeline.");
		}
	}

	public static void SetAddress(string addr)
	{
		if (Singleton.Client.BaseAddress == null)
			Singleton.Client.BaseAddress = new Uri(addr);
	}

	public static void Tick(uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (Singleton.IsOnline)
			Singleton.DoTick(timestep, formations, obstacles);
		else
			Singleton.DoFallbackTick(timestep, formations);
	}

	void DoTick(uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
#if EXPORT_OBJ
		if (true)
#else
		if (AgroWorld.GetDaylight(timestep))
#endif
		{
			//if (IsNight) Debug.WriteLine("DAY");
			IsNight = false;

			SkipPlants.Clear();
			for(int i = 0; i < formations.Count; ++i)
				if (!(formations[i] is PlantFormation2 plant && plant.AG.Alive))
					SkipPlants.Add(i);

			int offsetCounter = 0;
			if (SkipPlants.Count < formations.Count)
			{
				Irradiances.Clear();
				IrradiancePoints.Clear();

#if EXPORT_OBJ
				var objFileName = $"t{timestep}.obj";
				using var objStream = File.Open(objFileName, FileMode.Create);
				using var objWriter = new StreamWriter(objStream, System.Text.Encoding.UTF8);
				var obji = new System.Text.StringBuilder();
				objWriter.WriteLine("o Field");
#endif

				var meshFileName = $"t{timestep}.mesh";
#if GODOT
				var meshFileFullPath = Path.Combine("agroeco-mts3", meshFileName);
#else
				var meshFileFullPath = Path.Combine("..", "agroeco-mts3", meshFileName);
#endif
				var ooc = offsetCounter;
				using var binaryStream = new MemoryStream();
#if USE_TRIANGLES
				offsetCounter = ExportAsTriangles(formations, obstacles, ooc, binaryStream
#if EXPORT_OBJ
					, objWriter, obji
#endif
				);
#else
				offsetCounter = ExportAsPrimitivesInterleaved(formations, obstacles, ooc, binaryStream);
#endif

				var startTime = SW.ElapsedMilliseconds;
				SW.Start();

				binaryStream.TryGetBuffer(out var byteBuffer);
#if EXPORT_BIN
				//if (timestep == 1999)
				{
					var tmp = new byte[byteBuffer.Count];
					Array.Copy(byteBuffer.Array, tmp, byteBuffer.Count);
					File.WriteAllBytes($"t{timestep}.prim", tmp);

					meshBinaryStream.TryGetBuffer(out var meshByteBuffer);
					tmp = new byte[meshByteBuffer.Count];
					Array.Copy(meshByteBuffer.Array, tmp, meshByteBuffer.Count);
					File.WriteAllBytes($"t{timestep}.mesh", tmp);
				}
#endif
				if (offsetCounter > 0)
				{
					var request = new HttpRequestMessage()
					{
						Method = HttpMethod.Post,
						Content = new ByteArrayContent(byteBuffer.Array, 0, byteBuffer.Count)
					};
					request.Headers.Add("Ti", AgroWorld.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture));
					//Debug.WriteLine(offsetCounter);
					//request.Headers.Add("C", offsetCounter.ToString()); //Only use for dummy debug
					request.Headers.Add("Ra", "4096");

					var result = Client.SendAsync(request).Result;
					using var responseStream = result.Content.ReadAsStreamAsync().Result;
					using var reader = new BinaryReader(responseStream);
					var length = responseStream.Length / sizeof(float);
					for (int i = 0; i < length; ++i)
						Irradiances.Add(reader.ReadSingle());
					Debug.WriteLine($"T: {AgroWorld.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture)} Sum: {Irradiances.Sum()}  Avg: {Irradiances.Average()} In: [{Irradiances.Min()} - {Irradiances.Max()}]");
				}

				SW.Stop();
				//Console.WriteLine($"R: {SW.ElapsedMilliseconds - startTime}ms S: {offsetCounter} RpS: {(SW.ElapsedMilliseconds - startTime) / offsetCounter}");
			}
		}
		else
		{
			//if (!IsNight) Debug.WriteLine("NIGHT");
			IsNight = true;
		}
	}

	public static byte[] DebugIrradiance(uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles, float[] cameraMatrix) => Singleton.DebugIrr(timestep, formations, obstacles, cameraMatrix);

	byte[] DebugIrr(uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles, float[] camera)
	{
		if (Singleton.IsOnline)
		{
			using var primBinaryStream = new MemoryStream();
#if USE_TRIANGLES
			var offsetCounter = ExportAsTriangles(formations, obstacles, 0, primBinaryStream);
#else
			var offsetCounter = ExportAsPrimitivesInterleaved(formations, obstacles, 0, primBinaryStream);
#endif
			primBinaryStream.TryGetBuffer(out var byteBuffer);
			if (offsetCounter > 0)
			{
				var request = new HttpRequestMessage()
				{
					Method = HttpMethod.Post,
					Content = new ByteArrayContent(byteBuffer.Array, 0, byteBuffer.Count)
				};
				request.Headers.Add("Ti", AgroWorld.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture));
				request.Headers.Add("Cam", string.Join(' ', camera));
				//request.Headers.Add("Ra", "256");
				var result = Client.SendAsync(request).Result;
				return result.Content.ReadAsByteArrayAsync().Result;
			}
		}
		return null;
	}

	private int ExportAsTriangles(IList<IFormation> formations, IList<IObstacle> obstacles, int offsetCounter, Stream binaryStream
#if EXPORT_OBJ
		, StreamWriter objWriter, System.Text.StringBuilder obji
#endif
	)
	{
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(1); //version 1 using triangular meshes

		//Obstacles
		writer.WriteU32(obstacles.Count);
		foreach(var obstacle in obstacles)
		{
			obstacle.ExportTriangles(IrradiancePoints, writer
#if EXPORT_OBJ
			,obji
#else
			, null
#endif
			);
		}

		//Formations
		writer.WriteU32(formations.Count - SkipPlants.Count); //WRITE NUMBER OF PLANTS in this system
		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == pi)
				++skipPointer;
			else
			{
				var plant = formations[pi] as PlantFormation2;
				var ag = plant.AG;
				var count = ag.Count;

				var offsets = new int[count];
				for(int i = 0; i < count; ++i)
					offsets[i] = offsetCounter + i;
				IrradianceFormationOffsets.Add(ag, offsets);
				offsetCounter += count;

				writer.WriteU32(count); //WRITE NUMBER OF SURFACES in this plant

				for (int i = 0; i < count; ++i)
				{
					var organ = ag.GetOrgan(i);
					var center = ag.GetBaseCenter(i);
					var scale = ag.GetScale(i);
					var halfRadiusX = new Vector3(0f, 0f, scale.Z * 0.5f);
					var orientation = ag.GetDirection(i);
					var lengthVector = new Vector3(scale.X, 0f, 0f);
					switch (organ)
					{
						case OrganTypes.Leaf:
							{
								writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface

								var p = IrradiancePoints.Count;
								IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX, orientation));

								//var t = IrradianceTriangles.Count;
								writer.WriteU32(p);
								writer.WriteU32(p + 1);
								writer.WriteU32(p + 2);
								writer.WriteU32(p);
								writer.WriteU32(p + 2);
								writer.WriteU32(p + 3);
#if EXPORT_OBJ
									obji.AppendLine(OF(p, p+1, p+2));
									obji.AppendLine(OF(p, p+2, p+3));
#endif
							}
							break;
						case OrganTypes.Stem:
							{
								var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
								writer.WriteU8(8); //WRITE NUMBER OF TRIANGLES in this surface
								var p = IrradiancePoints.Count;
								IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector + halfRadiusX + halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(lengthVector - halfRadiusX + halfRadiusY, orientation));

								//front face
								writer.WriteU32(p);
								writer.WriteU32(p + 1);
								writer.WriteU32(p + 5);
								writer.WriteU32(p);
								writer.WriteU32(p + 5);
								writer.WriteU32(p + 4);
								//right face
								writer.WriteU32(p + 1);
								writer.WriteU32(p + 2);
								writer.WriteU32(p + 6);
								writer.WriteU32(p + 1);
								writer.WriteU32(p + 6);
								writer.WriteU32(p + 5);
								//back face
								writer.WriteU32(p + 2);
								writer.WriteU32(p + 3);
								writer.WriteU32(p + 7);
								writer.WriteU32(p + 2);
								writer.WriteU32(p + 7);
								writer.WriteU32(p + 6);
								//left face
								writer.WriteU32(p + 3);
								writer.WriteU32(p);
								writer.WriteU32(p + 4);
								writer.WriteU32(p + 3);
								writer.WriteU32(p + 4);
								writer.WriteU32(p + 7);
#if EXPORT_OBJ
									obji.AppendLine(OF(p, p+1, p+5));
									obji.AppendLine(OF(p, p+5, p+4));

									obji.AppendLine(OF(p+1, p+2, p+6));
									obji.AppendLine(OF(p+1, p+6, p+5));

									obji.AppendLine(OF(p+2, p+3, p+7));
									obji.AppendLine(OF(p+2, p+7, p+6));

									obji.AppendLine(OF(p+3, p, p+4));
									obji.AppendLine(OF(p+3, p+4, p+7));
#endif
							}
							break;
						case OrganTypes.Bud:
							{
								var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
								writer.WriteU8(2); //WRITE NUMBER OF TRIANGLES in this surface
								var p = IrradiancePoints.Count;
								IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
								IrradiancePoints.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));

								writer.WriteU32(p);
								writer.WriteU32(p + 1);
								writer.WriteU32(p + 2);
								writer.WriteU32(p);
								writer.WriteU32(p + 2);
								writer.WriteU32(p + 3);
#if EXPORT_OBJ
									obji.AppendLine(OF(p, p+1, p+2));
									obji.AppendLine(OF(p, p+2, p+3));
#endif
							}
							break;
						default: throw new NotImplementedException();
					}
				}
			}
		}

		writer.Write((uint)IrradiancePoints.Count);
		for (int i = 0; i < IrradiancePoints.Count; ++i)
		{
			var p = IrradiancePoints[i];
			writer.Write(p.X);
			writer.Write(p.Y);
			writer.Write(p.Z);
#if EXPORT_OBJ
			objWriter.WriteLine($"v {p.X} {p.Y} {p.Z}");
#endif
		}
#if EXPORT_OBJ
				objWriter.WriteLine(obji.ToString());
#endif
		return offsetCounter;
	}

	private int ExportAsPrimitivesClustered(IList<IFormation> formations, IList<IObstacle> obstacles, int offsetCounter, Stream binaryStream)
	{
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(2); //version 2 using triangular meshes

		//Obstacles
		writer.WriteU32(obstacles.Count);
		foreach(var obstacle in obstacles)
			obstacle.ExportAsPrimitivesClustered(writer);

		//Formations
		writer.WriteU32(formations.Count - SkipPlants.Count); //WRITE NUMBER OF PLANTS in this system
		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == pi)
				++skipPointer;
			else
			{
				var plant = formations[pi] as PlantFormation2;
				var ag = plant.AG;
				var count = ag.Count;

				var offsets = new int[count];
				for(int i = 0; i < count; ++i)
					offsets[i] = offsetCounter + i;
				IrradianceFormationOffsets.Add(ag, offsets);
				offsetCounter += count;

				writer.WriteU32(count); //WRITE NUMBER OF SURFACES in this plant

				for (int i = 0; i < count; ++i)
				{
					var organ = ag.GetOrgan(i);
					var center = ag.GetBaseCenter(i);
					var scale = ag.GetScale(i);
					var orientation = ag.GetDirection(i);

					var x = Vector3.Transform(Vector3.UnitX, orientation);
					var y = Vector3.Transform(Vector3.UnitY, orientation);
					var z = Vector3.Transform(Vector3.UnitZ, orientation);
					switch (organ)
					{
						case OrganTypes.Leaf:
							{
								writer.WriteU8(8); //PRIMITIVE TYPE 1 disk, 2 cylinder, 4 sphere, 8 >RECTANGLE<
								var ax = x * scale.X * 0.5f;
								var ay = -z * scale.Z * 0.5f;
								var az = y * scale.Y * 0.5f;
								var c = center + ax;
								writer.WriteM32(ax, ay, az, c);
							}
							break;
						case OrganTypes.Stem:
							{
								writer.WriteU8(2); //PRIMITIVE TYPE 1 disk, 2 >CYLINDER<, 4 sphere, 8 rectangle
								writer.Write(scale.X); //length
								writer.Write(scale.Z * 0.5f); //radius
								writer.WriteM32(z, x, y, center);
							}
							break;
						case OrganTypes.Bud:
							{
								writer.WriteU8(4); //PRIMITIVE TYPE 1 disk, 2 cylinder, 4 >SPHERE<, 8 rectangle
								writer.WriteV32(center);
								writer.Write(scale.X); //radius
							}
							break;
						default: throw new NotImplementedException();
					}
				}
			}
		}

		return offsetCounter;
	}

	private int ExportAsPrimitivesInterleaved(IList<IFormation> formations, IList<IObstacle> obstacles, int offsetCounter, Stream binaryStream)
	{
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(3); //version 2 using triangular meshes

		//Formations
		writer.WriteU32(formations.Count - SkipPlants.Count + obstacles.Count); //WRITE NUMBER OF PLANTS in this system
		foreach(var obstacle in obstacles)
			obstacle.ExportAsPrimitivesInterleaved(writer);

		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == pi)
				++skipPointer;
			else
			{
				var plant = formations[pi] as PlantFormation2;
				var ag = plant.AG;
				var count = ag.Count;

				var sensorsCount = 0;
				for (int i = 0; i < count; ++i)
					if (ag.GetOrgan(i) == OrganTypes.Leaf)
						++sensorsCount;

				var offsets = new int[count];
				for(int i = 0; i < count; ++i)
					offsets[i] = ag.GetOrgan(i) == OrganTypes.Leaf ? offsetCounter++ : -1;

				IrradianceFormationOffsets.Add(ag, offsets);

				writer.WriteU32(count); //WRITE NUMBER OF SURFACES in this plant

				for (int i = 0; i < count; ++i)
				{
					var organ = ag.GetOrgan(i);
					var center = ag.GetBaseCenter(i);
					var scale = ag.GetScale(i);
					var orientation = ag.GetDirection(i);

					var x = Vector3.Transform(Vector3.UnitX, orientation);
					var y = Vector3.Transform(Vector3.UnitY, orientation);
					var z = Vector3.Transform(Vector3.UnitZ, orientation);
					switch (organ)
					{
						case OrganTypes.Leaf:
							{
								writer.WriteU8(8); //PRIMITIVE TYPE 1 disk, 2 cylinder, 4 sphere, 8 >RECTANGLE<
								var ax = x * scale.X * 0.5f;
								var ay = -z * scale.Z * 0.5f;
								var az = y * scale.Y * 0.5f;
								var c = center + ax;
								writer.WriteM32(ax, ay, az, c);
								writer.Write(true);
							}
							break;
						case OrganTypes.Stem:
							{
								writer.WriteU8(2); //PRIMITIVE TYPE 1 disk, 2 >CYLINDER<, 4 sphere, 8 rectangle
								writer.Write(scale.X); //length
								writer.Write(scale.Z * 0.5f); //radius
								writer.WriteM32(z, x, y, center);
								writer.Write(false);
							}
							break;
						case OrganTypes.Bud:
							{
								writer.WriteU8(4); //PRIMITIVE TYPE 1 disk, 2 cylinder, 4 >SPHERE<, 8 rectangle
								writer.WriteV32(center);
								writer.Write(scale.X); //radius
								writer.Write(false);
							}
							break;
						default: throw new NotImplementedException();
					}
				}
			}
		}

		return offsetCounter;
	}

	public static void ExportToFile(string fileName, byte version, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		using var file = File.OpenWrite(fileName);
		switch (version)
		{
			default: Singleton.ExportAsTriangles(formations, obstacles, 0, file); break;
			case 2: Singleton.ExportAsPrimitivesClustered(formations, obstacles, 0, file); break;
			case 3: Singleton.ExportAsPrimitivesInterleaved(formations, obstacles, 0, file); break;
		}
	}

	void DoFallbackTick(uint timestep, IList<IFormation> formations)
	{
		if (AgroWorld.GetDaylight(timestep))
		{
			SkipPlants.Clear();
			for(int i = 0; i < formations.Count; ++i)
				if (!(formations[i] is PlantFormation2 plant && plant.AG.Alive))
					SkipPlants.Add(i);

			int offsetCounter = 0;
			if (SkipPlants.Count < formations.Count)
			{
				Irradiances.Clear();
				IrradianceFormationOffsets.Clear();
				IrradiancePoints.Clear();

				var skipPointer = 0;
				for(int i = 0; i < formations.Count; ++i)
				{
					if (skipPointer < SkipPlants.Count && SkipPlants[skipPointer] == i)
						++skipPointer;
					else
					{
						var plant = formations[i] as PlantFormation2;
						var ag = plant!.AG;
						var count = ag.Count;


						var offsets = new int[count];
						for(int j = 0; j < count; ++j)
							offsets[j] = offsetCounter + j;
						IrradianceFormationOffsets.Add(ag, offsets);
						offsetCounter += count;
					}
				}

				for(int i = 0; i < offsetCounter; ++i)
					Irradiances.Add(1f);
			}
			//if (IsNight) Debug.WriteLine("DAY");
			IsNight = false;
		}
		else
		{
			//if (!IsNight) Debug.WriteLine("NIGHT");
			IsNight = true;
		}
	}

	static string OF(int a, int b, int c) => $"f {a+1} {b+1} {c+1}";
	public static float GetIrradiance(IFormation formation, int agentIndex) => Singleton.GetIrr(formation, agentIndex);
	float GetIrr(IFormation formation, int agentIndex)
	{
		if (!IsNight && IrradianceFormationOffsets.TryGetValue(formation, out var offset) && agentIndex < offset.Length)
		{
			var position = offset[agentIndex];
			if (position >= 0 && position < Irradiances.Count)
				return Irradiances[position];
		}
		return 0f;
	}

	//TODO make this a ReadOnlySpan
	public static float[]  GetIrradiance(IFormation formation) => Singleton.GetIrr(formation);
	float[] GetIrr(IFormation formation)
	{
		var result = new float[formation.Count];
		if (!IsNight && IrradianceFormationOffsets.TryGetValue(formation, out var offset))
			for(int i = 0; i < offset.Length; ++i)
			{
				var position = offset[i];
				result[i] = position >= 0 && position < Irradiances.Count ? Irradiances[position] : 0f;
			}

		return result;
	}


	readonly Stopwatch SW = new();

	public static long ElapsedMilliseconds => Singleton.SW.ElapsedMilliseconds;

	~IrradianceClient()
	{
		Client.Dispose();
	}

	static readonly IrradianceClient Singleton = new();
}
