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
using System.Linq.Expressions;

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
	public bool IsOnline {get; private set; }= false;
	bool AddressFixed = false;

	bool GlobalIllumination = true;

	readonly List<float> Irradiances = new();
	readonly List<int> SkipFormations = new();
	readonly List<Vector3> IrradiancePoints = new();
	readonly Dictionary<IFormation, int[]> IrradianceFormationOffsets = new();

	bool IsNight = true;

	byte[] EnvMap = null; //just for debug output
	ushort EnvMapX = 0;

	public IrradianceClient(float latitude, float longitude, bool constantLights)
	{
		Client = new() { BaseAddress = new Uri("http://localhost:9000"), Timeout = TimeSpan.FromHours(1) };
		Client.DefaultRequestHeaders.Add("La", latitude.ToString());
		Client.DefaultRequestHeaders.Add("Lo", longitude.ToString());
		GlobalIllumination = !constantLights;
	}

	bool ProbeRenderer()
	{
		AddressFixed = true;
		//Probe if the client is online, else fallback to constant ambient light
		try
		{
			var result = Client.SendAsync(new() { Method = HttpMethod.Get }).Result; //HTTPRequests can not be reused, a new request needs to be created every time
			IsOnline = result.IsSuccessStatusCode;
		}
		catch (Exception)
		{
			Console.WriteLine($"WARNING: No irradiance client responded at {Client.BaseAddress}. Falling back to ambient light pipeline.");
			IsOnline = false;
		}
		return IsOnline;
	}

	public string Address => Client.BaseAddress?.ToString() ?? "null";

	public void SetAddress(string addr)
	{
		lock(Client)
		{
			if (!AddressFixed && Client.BaseAddress?.ToString() != addr)
			{
				Client.BaseAddress = new Uri(addr);
				AddressFixed = true;
			}
		}
	}

	public void Tick(SimulationWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (timestep == 0 && GlobalIllumination)
			ProbeRenderer();

		var agroWorld = world as AgroWorld;
		if (IsOnline && GlobalIllumination)
			DoTick(agroWorld, timestep, formations, obstacles);
		else
			DoFallbackTick(agroWorld, timestep, formations);
	}

	void DoTick(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		if (world.GetDaylight(timestep))
		{
			//if (IsNight) Debug.WriteLine("DAY");
			IsNight = false;

			SkipFormations.Clear();
			for(int i = 0; i < formations.Count; ++i)
				if (!(formations[i] is PlantFormation2 plant && plant.AG.Alive))
					SkipFormations.Add(i);

			int offsetCounter = 0;
			if (SkipFormations.Count < formations.Count)
			{
				Irradiances.Clear();
				IrradiancePoints.Clear(); // only necessary for triangular mesh export

				var meshFileName = $"t{timestep}.mesh";
				#if GODOT
				var meshFileFullPath = Path.Combine("agroeco-mts3", meshFileName);
				#else
				var meshFileFullPath = Path.Combine("..", "agroeco-mts3", meshFileName);
				#endif
				var ooc = offsetCounter;
				using var binaryStream = new MemoryStream();
				#if USE_TRIANGLES
				offsetCounter = ExportAsTriangles(formations, obstacles, ooc, binaryStream);
				#else
				offsetCounter = ExportAsPrimitivesInterleaved(formations, obstacles, binaryStream);
				#endif

				var startTime = SW.ElapsedMilliseconds;
				SW.Start();

				binaryStream.TryGetBuffer(out var byteBuffer);
				#if EXPORT_BIN
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
					var reqEnvMap = true;
					var request = new HttpRequestMessage()
					{
						Method = HttpMethod.Post,
						Content = new ByteArrayContent(byteBuffer.Array, 0, byteBuffer.Count)
					};
					request.Headers.Add("Ti", world.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture));
					request.Headers.Add("TiE", world.GetTime(timestep + 1).ToString("o", CultureInfo.InvariantCulture));
					//Debug.WriteLine(offsetCounter);
					//request.Headers.Add("C", offsetCounter.ToString()); //Only use for dummy debug
					request.Headers.Add("Ra", "2048");
					if (reqEnvMap)
						request.Headers.Add("Env", "true");

					try
					{
						var result = Client.SendAsync(request).Result;
						using var responseStream = result.Content.ReadAsStreamAsync().Result;
						using var reader = new BinaryReader(responseStream);

						long length;
						if (reqEnvMap)
						{
							length = reader.ReadUInt16() * sizeof(float);
							EnvMapX = reader.ReadUInt16();
							EnvMap = reader.ReadBytes((int)length);

							// Debug.WriteLine($"Total stream length: {responseStream.Length} env length: {length} irr length: {(responseStream.Length - sizeof(int) - length) / sizeof(float)}");
							length = (responseStream.Length - 2*sizeof(ushort) - length) / sizeof(float);
						}
						else
							length = responseStream.Length / sizeof(float);

						for (var i = 0; i < length; ++i)
							Irradiances.Add(reader.ReadSingle() * 1e3f * world.HoursPerTick);
					}
					catch (Exception)
                    {
						IsOnline = false;
						SW.Stop();
						DoFallbackTick(world, timestep, formations);
					}
					// Debug.WriteLine($"Irradiances length: {length} count: {Irradiances.Count}");

					//Debug.WriteLine($"T: {AgroWorld.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture)} Sum: {Irradiances.Sum()}  Avg: {Irradiances.Average()} In: [{Irradiances.Min()} - {Irradiances.Max()}]");
					//Debug.WriteLine($"IR: {String.Join(", ", Irradiances)}");
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

	public byte[] DebugIrradiance(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles, float[] cameraMatrix) => DebugIrr(world, timestep, formations, obstacles, cameraMatrix);
	public byte[] DebugEnvironment() => EnvMap;
	public ushort DebugEnvironmentX() => EnvMapX;

	byte[] DebugIrr(AgroWorld world, uint timestep, IList<IFormation> formations, IList<IObstacle> obstacles, float[] camera)
	{
		if (IsOnline)
		{
			using var primBinaryStream = new MemoryStream();
#if USE_TRIANGLES
			var offsetCounter = ExportAsTriangles(formations, obstacles, 0, primBinaryStream);
#else
			var offsetCounter = ExportAsPrimitivesInterleaved(formations, obstacles, primBinaryStream);
#endif
			primBinaryStream.TryGetBuffer(out var byteBuffer);
			if (offsetCounter > 0)
			{
				var request = new HttpRequestMessage()
				{
					Method = HttpMethod.Post,
					Content = new ByteArrayContent(byteBuffer.Array, 0, byteBuffer.Count)
				};
				request.Headers.Add("Ti", world.GetTime(timestep).ToString("o", CultureInfo.InvariantCulture));
				request.Headers.Add("TiE", world.GetTime(timestep + 1).ToString("o", CultureInfo.InvariantCulture));
				request.Headers.Add("Cam", string.Join(' ', camera));
				//request.Headers.Add("Ra", "256");
				var result = Client.SendAsync(request).Result;
				return result.Content.ReadAsByteArrayAsync().Result;
			}
		}
		return null;
	}

	private void ExportAsObj(IList<IFormation> formations, IList<IObstacle> obstacles, StreamWriter writer)
	{
		var obji = new System.Text.StringBuilder();
		var points = new List<Vector3>();
		foreach(var obstacle in obstacles)
			obstacle.ExportObj(points, obji);

		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == pi)
				++skipPointer;
			else
			{
				var plant = formations[pi] as PlantFormation2;
				var ag = plant.AG;
				var count = ag.Count;

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
								var p = points.Count;
								points.Add(center + Vector3.Transform(-halfRadiusX, orientation));
								points.Add(center + Vector3.Transform(halfRadiusX, orientation));
								points.Add(center + Vector3.Transform(lengthVector + halfRadiusX, orientation));
								points.Add(center + Vector3.Transform(lengthVector - halfRadiusX, orientation));
								obji.AppendLine(OF(p, p+1, p+2));
								obji.AppendLine(OF(p, p+2, p+3));
							}
							break;
						case OrganTypes.Stem: case OrganTypes.Petiole:
							{
								var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
								var p = points.Count;
								points.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(lengthVector - halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(lengthVector + halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(lengthVector + halfRadiusX + halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(lengthVector - halfRadiusX + halfRadiusY, orientation));

								obji.AppendLine(OF(p, p+1, p+5));
								obji.AppendLine(OF(p, p+5, p+4));

								obji.AppendLine(OF(p+1, p+2, p+6));
								obji.AppendLine(OF(p+1, p+6, p+5));

								obji.AppendLine(OF(p+2, p+3, p+7));
								obji.AppendLine(OF(p+2, p+7, p+6));

								obji.AppendLine(OF(p+3, p, p+4));
								obji.AppendLine(OF(p+3, p+4, p+7));
							}
							break;
						case OrganTypes.Bud:
							{
								var halfRadiusY = new Vector3(0f, scale.Y * 0.5f, 0f);
								var p = points.Count;
								points.Add(center + Vector3.Transform(-halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(halfRadiusX - halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(halfRadiusX + halfRadiusY, orientation));
								points.Add(center + Vector3.Transform(-halfRadiusX + halfRadiusY, orientation));

								obji.AppendLine(OF(p, p+1, p+2));
								obji.AppendLine(OF(p, p+2, p+3));
							}
							break;
						default: throw new NotImplementedException();
					}
				}
			}
		}

		for (int i = 0; i < points.Count; ++i)
			writer.WriteLine($"v {points[i].X} {points[i].Y} {points[i].Z}");

		writer.WriteLine(obji.ToString());
	}

	private int ExportAsTriangles(IList<IFormation> formations, IList<IObstacle> obstacles, int offsetCounter, Stream binaryStream)
	{
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(1); //version 1 using triangular meshes

		//Obstacles
		writer.WriteU32(obstacles.Count);
		foreach(var obstacle in obstacles)
			obstacle.ExportTriangles(IrradiancePoints, writer);

		//Formations
		writer.WriteU32(formations.Count - SkipFormations.Count); //WRITE NUMBER OF PLANTS in this system
		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == pi)
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
							}
							break;
						case OrganTypes.Stem: case OrganTypes.Petiole:
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
		}
		return offsetCounter;
	}

	private int ExportAsPrimitivesClustered(IList<IFormation> formations, IList<IObstacle> obstacles, int offsetCounter, Stream binaryStream)
	{
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(2); //version 2 using primitives clustered into two groups: obstacles and sensors

		//Obstacles
		writer.WriteU32(obstacles.Count);
		foreach(var obstacle in obstacles)
			obstacle.ExportAsPrimitivesClustered(writer);

		//Formations
		writer.WriteU32(formations.Count - SkipFormations.Count); //WRITE NUMBER OF PLANTS in this system
		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == pi)
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
						case OrganTypes.Stem: case OrganTypes.Petiole:
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

	private int ExportAsPrimitivesInterleaved(IList<IFormation> formations, IList<IObstacle> obstacles, Stream binaryStream)
	{
		int offsetCounter = 0;
		IrradianceFormationOffsets.Clear();
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(3); //version 3 using primitives, each surface is individually marked as obstacle or sensor

		//Formations
		writer.WriteU32(formations.Count - SkipFormations.Count + obstacles.Count); //WRITE NUMBER OF PLANTS in this system
		foreach(var obstacle in obstacles)
			obstacle.ExportAsPrimitivesInterleaved(writer);

		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == pi)
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
				for (int i = 0; i < count; ++i)
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
						case OrganTypes.Stem: case OrganTypes.Petiole:
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

	internal void ExportAsBeautyPrimitives(string fileName, IList<IFormation> formations)
	{
		using var file = File.OpenWrite(fileName);
		ExportAsBeautyPrimitives(formations, file);
	}

	internal void ExportAsBeautyPrimitives(IList<IFormation> formations, Stream binaryStream, bool extended = false)
	{
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(extended ? 5 : 4); //version 4 is for the beauty pass; using primitives, each surface is individually marked as obstacle or sensor}

		//Formations
		writer.WriteU32(formations.Count - SkipFormations.Count); //WRITE NUMBER OF PLANTS in this system

		var skipPointer = 0;
		for (int pi = 0; pi < formations.Count; ++pi)
		{
			if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == pi)
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

					var parentIndex = ag.GetParent(i);
					writer.Write(parentIndex);
					switch (organ)
					{
						case OrganTypes.Leaf:
							{
								writer.WriteU8(1); //ORGAN 1 leaf
								var ax = x * scale.X * 0.5f;
								var ay = -z * scale.Z * 0.5f;
								var az = y * scale.Y * 0.5f;
								var c = center + ax;
								writer.WriteM32(ax, ay, az, c);
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterStorageCapacity(i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
								if (extended)
								{
									writer.Write(GetIrradiance(ag, i));
									writer.Write(ag.GetDailyResources(i));
									writer.Write(ag.GetDailyProduction(i));
								}
							}
							break;
						case OrganTypes.Stem: case OrganTypes.Petiole:
							{
								writer.WriteU8(2); //ORGAN 2 stem
								writer.Write(scale.X); //length
								writer.Write(scale.Z * 0.5f); //radius
								writer.WriteM32(z, x, y, center);
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterStorageCapacity(i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
								writer.Write(Math.Clamp(ag.GetWoodRatio(i), 0, 1));
							}
							break;
						case OrganTypes.Bud:
							{
								writer.WriteU8(3); //ORGAN 3 bud
								writer.WriteV32(center);
								writer.Write(scale.X); //radius
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterStorageCapacity(i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
							}
							break;
						default: throw new NotImplementedException();
					}
				}
			}
		}
	}

	public void ExportToFile(string fileName, byte version, IList<IFormation> formations, IList<IObstacle> obstacles = null)
    {
        using var file = File.OpenWrite(fileName);
        ExportToStream(version, formations, obstacles, file);
    }

    void ExportToStream(byte version, IList<IFormation> formations, IList<IObstacle> obstacles, Stream target)
    {
		if (SkipFormations.Count == 0)
		{
			for(int i = 0; i < formations.Count; ++i)
				if (!(formations[i] is PlantFormation2 plant && plant.AG.Alive))
					SkipFormations.Add(i);
		}

        switch (version)
        {
            default: ExportAsTriangles(formations, obstacles, 0, target); break;
            case 2: ExportAsPrimitivesClustered(formations, obstacles, 0, target); break;
            case 3: ExportAsPrimitivesInterleaved(formations, obstacles, target); break;
            case 4: ExportAsBeautyPrimitives(formations, target); break;
            case 5: ExportAsBeautyPrimitives(formations, target, true); break;
        }
    }

    public byte[] ExportToStream(byte version, IList<IFormation> formations, IList<IObstacle> obstacles = null)
	{
		using var stream = new MemoryStream();
		ExportToStream(version, formations, obstacles, stream);
		return stream.ToArray();
	}

	public void ExportToObjFile(string fileName, IList<IFormation> formations, IList<IObstacle> obstacles)
	{
		using var objStream = File.Open(fileName, FileMode.Create);
		using var objWriter = new StreamWriter(objStream, System.Text.Encoding.UTF8);
		objWriter.WriteLine("o Field");
		ExportAsObj(formations, obstacles, objWriter);
	}


	void DoFallbackTick(AgroWorld world, uint timestep, IList<IFormation> formations)
	{
		if (world.GetDaylight(timestep))
		{
			SkipFormations.Clear();
			for(int i = 0; i < formations.Count; ++i)
				if (!(formations[i] is PlantFormation2 plant && plant.AG.Alive))
					SkipFormations.Add(i);

			int offsetCounter = 0;

			if (SkipFormations.Count < formations.Count)
			{
				Irradiances.Clear();
				IrradianceFormationOffsets.Clear();
				IrradiancePoints.Clear();

				var skipPointer = 0;
				for(int i = 0; i < formations.Count; ++i)
				{
					if (skipPointer < SkipFormations.Count && SkipFormations[skipPointer] == i)
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
					Irradiances.Add(world.HoursPerTick * 50f);
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

	public float GetIrradiance(IFormation formation, int agentIndex)
	{
		if (!IsNight && IrradianceFormationOffsets.TryGetValue(formation, out var offset))
		{
			var position = offset[agentIndex];
			if (position >= 0)
				return Irradiances[position];
		}
		return 0f;
	}

	//TODO make this a ReadOnlySpan
	public (int[], IList<float>) GetIrradiance(IFormation formation) =>
		(!IsNight && formation.Count > 0 && IrradianceFormationOffsets.TryGetValue(formation, out var offset) ? offset : null, Irradiances);


	readonly Stopwatch SW = new();

	public long ElapsedMilliseconds => SW.ElapsedMilliseconds;

	~IrradianceClient()
	{
		Client.Dispose();
	}
}
