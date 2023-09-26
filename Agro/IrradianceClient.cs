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

public enum Mode : byte { Constant, Mitsuba, Tamashii }

public class IrradianceClient
{
	readonly HttpClient Client;
	string RequestedMode;
	public bool IsOnline {get; private set; } = false;
	bool AddressFixed = false;

	bool GlobalIllumination = true;

	readonly List<float> Irradiances = new();
	readonly List<int> SkipFormations = new();
	readonly List<Vector3> IrradiancePoints = new();
	readonly Dictionary<IFormation, int[]> IrradianceFormationOffsets = new();

	bool IsNight = true;

	byte[] EnvMap = null; //just for debug output
	ushort EnvMapX = 0;

	public IrradianceClient(float latitude, float longitude, int _mode)
	{
		var mode = (Mode)_mode;
		Client = new() { BaseAddress = new Uri($"http://localhost:{9001 + Math.Clamp(_mode - 1, 0, 8)}"), Timeout = TimeSpan.FromHours(1) };
		Client.DefaultRequestHeaders.Add("La", latitude.ToString());
		Client.DefaultRequestHeaders.Add("Lo", longitude.ToString());
		GlobalIllumination = mode != Mode.Constant;
		RequestedMode = mode.ToString();
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

	public void SetAddress(string mitsubaAddr, string mitsubaPort, string tamashiiAddr, string tamashiiPort, int mode)
	{
		var addr = $"http://{(mode == 2 ? tamashiiAddr : mitsubaAddr)}:{(mode == 2 ? tamashiiPort : mitsubaPort)}";
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
		if (timestep == 0)
		{
			if (GlobalIllumination)
			{
				ProbeRenderer();
				world.RendererName = IsOnline ? RequestedMode : "none";
			}
			else
				world.RendererName = RequestedMode;
		}

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

					// meshBinaryStream.TryGetBuffer(out var meshByteBuffer);
					// tmp = new byte[meshByteBuffer.Count];
					// Array.Copy(meshByteBuffer.Array, tmp, meshByteBuffer.Count);
					// File.WriteAllBytes($"t{timestep}.mesh", tmp);
				}
				#endif
				if (offsetCounter > 0)
				{
					var reqEnvMap = false;
					var request = new HttpRequestMessage()
					{
						Method = HttpMethod.Post,
						Content = new ByteArrayContent(byteBuffer.Array, 0, byteBuffer.Count)
						//Content = new ByteArrayContent(new byte[]{3,1,0,0,0,55,0,0,0,2,106,12,206,61,81,173,6,60,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,10,215,35,188,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,109,2,39,61,121,71,160,58,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,136,145,185,61,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,140,111,157,188,71,93,254,60,171,144,137,54,74,165,241,62,52,66,143,60,50,149,133,177,154,9,74,56,6,55,12,62,75,14,117,61,127,106,35,60,59,32,86,183,159,146,12,62,1,2,109,2,39,61,121,71,160,58,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,136,145,185,61,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,153,111,157,60,72,93,254,188,163,144,137,182,92,45,7,63,52,66,143,60,50,149,5,48,154,9,74,56,6,55,12,62,74,14,117,189,140,106,35,188,62,32,86,55,224,176,24,189,1,2,106,12,206,61,247,82,236,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,136,145,185,61,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,165,135,18,61,57,158,140,58,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,248,206,67,62,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,78,60,135,188,168,42,223,60,137,109,170,54,55,150,243,62,183,182,155,60,164,127,146,177,17,207,69,56,254,44,113,62,18,128,82,61,130,95,15,60,155,163,132,183,172,124,0,62,1,2,165,135,18,61,57,158,140,58,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,248,206,67,62,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,87,60,135,60,168,42,223,188,131,109,170,182,229,52,6,63,184,182,155,60,0,0,0,0,16,207,69,56,254,44,113,62,18,128,82,189,137,95,15,188,157,163,132,55,41,178,208,188,1,2,106,12,206,61,47,75,203,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,248,206,67,62,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,180,25,252,60,12,234,113,58,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,150,106,149,62,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,179,195,97,188,8,248,191,60,241,179,206,54,151,137,245,62,190,116,162,60,68,161,73,177,0,242,63,56,19,182,170,62,205,180,47,61,9,169,246,59,247,222,160,183,234,174,232,61,1,2,180,25,252,60,12,234,113,58,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,150,106,149,62,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,195,195,97,60,8,248,191,188,238,179,206,182,53,59,5,63,191,116,162,60,68,161,201,47,0,242,63,56,19,182,170,62,205,180,47,189,22,169,246,187,248,222,160,55,232,16,95,188,1,2,106,12,206,61,103,67,170,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,150,106,149,62,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,145,17,206,61,23,209,3,59,220,187,115,191,86,114,93,190,83,114,93,62,0,0,0,63,0,0,0,0,243,4,53,63,244,4,53,63,176,237,200,62,30,150,156,190,138,88,44,63,137,88,44,191,205,204,76,61,0,2,220,35,211,60,212,151,74,58,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,176,237,200,62,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,221,186,71,60,104,197,160,188,114,8,131,182,52,140,4,63,248,126,44,60,180,220,168,47,245,194,74,56,111,166,215,62,176,113,27,189,24,147,206,187,98,245,75,55,136,187,173,187,1,2,106,12,206,61,159,59,137,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,176,237,200,62,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,13,128,189,61,39,196,147,58,220,187,115,191,86,114,93,190,83,114,93,62,0,0,0,63,0,0,0,0,243,4,53,63,244,4,53,63,202,112,252,62,30,150,156,190,138,88,44,63,137,88,44,191,205,204,76,61,0,4,0,0,0,63,202,112,252,62,205,204,76,61,103,224,217,58,0,2,106,12,206,61,175,103,80,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,202,112,252,62,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,44,56,129,60,192,230,247,57,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,242,249,23,63,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,117,45,243,187,75,192,68,60,212,81,140,54,45,116,250,62,137,16,226,59,93,253,154,176,135,184,73,56,13,153,28,63,43,66,189,60,17,206,124,59,215,105,90,183,141,118,171,61,1,2,44,56,129,60,192,230,247,57,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,242,249,23,63,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,149,45,243,59,76,192,68,188,204,81,140,182,233,197,2,63,137,16,226,59,93,253,26,48,135,184,73,56,13,153,28,63,43,66,189,188,46,206,124,187,219,105,90,55,1,89,133,60,1,2,12,35,70,60,116,26,191,57,90,165,76,191,3,51,5,63,36,206,153,190,237,219,244,62,0,0,128,50,254,255,255,62,216,179,93,63,74,91,237,62,38,206,25,63,130,58,49,63,88,165,204,190,122,33,241,61,0,8,216,117,61,60,126,68,253,59,103,129,67,182,90,0,254,62,103,208,245,58,0,0,0,0,9,186,80,56,166,105,241,62,57,22,124,60,6,89,190,187,170,16,130,182,217,228,16,62,1,2,12,35,70,60,116,26,191,57,89,150,153,190,15,126,83,191,221,53,244,62,237,219,244,62,0,0,0,0,255,255,255,62,216,179,93,63,74,91,237,62,221,53,116,191,177,2,133,62,89,150,25,190,122,33,241,61,0,8,94,105,150,188,248,19,62,59,254,53,155,54,211,87,230,62,114,208,245,58,252,157,237,173,8,186,80,56,166,105,241,62,29,49,189,59,186,29,23,60,130,58,195,181,216,177,1,62,1,2,106,12,206,61,31,88,14,59,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,242,249,23,63,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,237,132,48,60,8,66,169,57,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,127,187,49,63,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,124,145,166,187,13,91,6,60,89,234,134,54,87,52,252,62,34,110,148,59,7,30,141,176,245,85,74,56,161,215,52,63,176,162,129,60,27,162,44,59,101,0,82,183,222,169,149,61,1,2,237,132,48,60,8,66,169,57,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,127,187,49,63,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,137,145,166,59,14,91,6,188,82,234,134,182,213,229,1,63,35,110,148,59,10,173,211,47,245,85,74,56,161,215,52,63,177,162,129,188,40,162,44,187,105,0,82,55,187,139,220,60,1,2,230,58,214,59,136,113,79,57,90,165,76,191,3,51,5,63,36,206,153,190,61,193,245,62,0,0,128,50,254,255,255,62,216,179,93,63,75,248,14,63,38,206,25,63,130,58,49,63,88,165,204,190,250,249,229,61,0,8,51,140,203,59,204,216,136,59,12,17,146,182,75,173,250,62,73,121,198,58,0,0,0,0,105,128,79,56,195,49,16,63,86,106,7,60,10,179,77,187,101,89,194,182,32,22,0,62,1,2,230,58,214,59,136,113,79,57,89,150,153,190,15,126,83,191,221,53,244,62,61,193,245,62,0,0,0,0,255,255,255,62,216,179,93,63,75,248,14,63,221,53,116,191,177,2,133,62,89,150,25,190,250,249,229,61,0,8,112,152,33,188,107,104,205,58,82,236,231,54,138,240,237,62,77,121,198,58,250,99,0,174,105,128,79,56,195,49,16,63,91,66,75,59,191,77,163,59,16,220,17,182,132,206,239,61,1,2,106,12,206,61,87,145,152,58,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,127,187,49,63,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,237,50,189,59,160,58,53,57,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,12,125,75,63,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,78,74,49,187,99,235,143,59,198,15,146,54,194,249,253,62,229,35,44,59,96,190,226,175,237,9,73,56,193,52,77,63,234,250,9,60,246,235,184,58,225,89,99,183,124,55,127,61,1,2,237,50,189,59,160,58,53,57,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,12,125,75,63,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,92,74,49,59,101,235,143,187,190,15,146,182,31,3,1,63,231,35,44,59,149,41,23,47,236,9,73,56,193,52,77,63,236,250,9,188,2,236,184,186,229,89,99,55,29,98,26,61,1,2,138,83,144,61,61,126,57,58,220,187,115,191,86,114,93,190,83,114,93,62,237,219,244,62,0,0,0,0,243,4,53,63,244,4,53,63,74,91,237,62,30,150,156,190,138,88,44,63,137,88,44,191,122,33,241,61,0,2,244,42,164,59,246,50,29,57,90,165,76,191,3,51,5,63,36,206,153,190,100,14,237,62,0,0,128,50,254,255,255,62,216,179,93,63,96,111,3,63,38,206,25,63,130,58,49,63,88,165,204,190,194,37,41,62,0,8,187,214,156,59,127,169,81,59,97,63,69,182,107,215,240,62,252,81,77,58,203,180,196,174,125,181,80,56,223,70,4,63,163,174,208,59,68,147,29,187,97,57,131,182,113,56,51,62,1,2,244,42,164,59,246,50,29,57,89,150,153,190,15,126,83,191,221,53,244,62,100,14,237,62,0,0,0,0,255,255,255,62,216,179,93,63,96,111,3,63,221,53,116,191,177,2,133,62,89,150,25,190,194,37,41,62,0,8,234,6,249,187,26,90,157,58,15,152,156,54,199,11,231,62,4,82,77,58,50,35,131,172,125,181,80,56,224,70,4,63,213,157,28,59,69,50,122,59,223,247,196,181,105,237,44,62,1,2,36,104,124,60,239,147,35,57,216,187,115,191,0,0,128,51,46,150,156,62,0,0,0,63,0,0,0,0,255,255,127,63,0,0,128,51,12,125,75,63,44,150,156,190,0,0,0,0,216,187,115,191,205,204,76,61,0,2,204,222,74,58,55,138,191,55,216,187,115,191,80,114,93,190,98,114,93,62,0,0,0,63,0,0,192,51,242,4,53,63,244,4,53,63,173,110,79,63,32,150,156,190,136,88,44,63,134,88,44,191,205,204,76,61,0,8,130,254,194,185,182,5,25,58,189,223,9,54,80,185,255,62,217,227,44,57,47,185,160,174,86,208,79,56,88,157,79,63,51,194,151,58,48,158,68,57,54,155,214,182,46,173,83,61,1,2,204,222,74,58,55,138,191,55,218,187,115,63,105,114,93,62,102,114,93,190,0,0,0,63,0,0,0,179,242,4,53,63,244,4,53,63,173,110,79,63,44,150,156,62,136,88,44,191,134,88,44,63,205,204,76,61,0,8,146,254,194,57,183,5,25,186,166,223,9,182,88,35,0,63,219,227,44,57,198,21,113,45,86,208,79,56,88,157,79,63,52,194,151,186,63,158,68,185,61,155,214,54,108,236,69,61,1})
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

						if (offsetCounter != length)
						{
							Console.WriteLine($"The number of returned irradiances is wrong. Should be {offsetCounter} is {length}.");
							var req = new byte[byteBuffer.Count];
							Array.Copy(byteBuffer.Array, req, byteBuffer.Count);
							Console.WriteLine($"Request: {string.Join(",", req)}");
							var res = reader.ReadBytes((int)responseStream.Length);
							Console.WriteLine($"Response: {string.Join(",", res)}");
							throw new Exception("The number of returned irradiances is wrong.");
						}

						//var multiplier = world.HoursPerTick * 3600;
						for (var i = 0; i < length; ++i)
							Irradiances.Add(reader.ReadSingle() / 3600f);
					}
					catch (Exception e)
                    {
						IsOnline = false;
						SW.Stop();
						world.RendererName = "none (after failure)";
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
						case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem:
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
						case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem:
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
						case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem:
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
						case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem:
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

	internal void ExportAsBeautyPrimitives(IList<IFormation> formations, Stream binaryStream, bool extended = false, bool roots = false)
	{
		using var writer = new BinaryWriter(binaryStream);
		writer.WriteU8(extended ? (roots ? 6 : 5) : 4); //version 4 is for the beauty pass; using primitives, each surface is individually marked as obstacle or sensor}

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
				var world = plant.World;

				var sensorsCount = 0;
				for (int i = 0; i < count; ++i)
					if (ag.GetOrgan(i) == OrganTypes.Leaf)
						++sensorsCount;

				writer.WriteU32(count); //WRITE NUMBER OF ABOVE-GROUND SURFACES in this plant

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
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterEfficientCapacity(world, i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
								if (extended)
								{
									writer.Write(GetIrradiance(ag, i));
									writer.Write(ag.GetDailyResourcesInv(i));
									writer.Write(ag.GetDailyProductionInv(i));
								}
							}
							break;
						case OrganTypes.Stem: case OrganTypes.Petiole: case OrganTypes.Meristem:
							{
								writer.WriteU8(2); //ORGAN 2 stem
								writer.Write(scale.X); //length
								writer.Write(scale.Z * 0.5f); //radius
								writer.WriteM32(z, x, y, center);
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterEfficientCapacity(world, i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
								writer.Write(Math.Clamp(ag.GetWoodRatio(i), 0, 1));
							}
							break;
						case OrganTypes.Bud:
							{
								writer.WriteU8(3); //ORGAN 3 bud
								writer.WriteV32(center);
								writer.Write(scale.X); //radius
								writer.Write(Math.Clamp(ag.GetWater(i) / ag.GetWaterEfficientCapacity(world, i), 0, 1));
								writer.Write(Math.Clamp(ag.GetEnergy(i) / ag.GetEnergyCapacity(i), 0, 1));
							}
							break;
						default: throw new NotImplementedException();
					}
					if (extended)
					{
						var h = ag.GetHormones(i);
						writer.Write(h.X);
						writer.Write(h.Y);
					}
				}

				if (roots)
				{
					var ug = plant.UG;
					count = ug.Count;
					writer.WriteU32(count); //WRITE NUMBER OF UNDER-GROUND SURFACES in this plant
					for (int i = 0; i < count; ++i)
					{
						var organ = ug.GetOrgan(i);
						var center = ug.GetBaseCenter(i);
						var scale = ug.GetScale(i);
						var orientation = ug.GetDirection(i);

						var x = Vector3.Transform(Vector3.UnitX, orientation);
						var y = Vector3.Transform(Vector3.UnitY, orientation);
						var z = Vector3.Transform(Vector3.UnitZ, orientation);

						var parentIndex = ug.GetParent(i);
						writer.Write(parentIndex);

						switch (organ)
						{
							case OrganTypes.Root:
								{
									writer.WriteU8(1); //ORGAN 1 root
									writer.Write(scale.X); //length
									writer.Write(scale.Z * 0.5f); //radius
									writer.WriteM32(z, x, y, center);
									//writer.Write(Math.Clamp(ug.GetWater(i) / ug.GetWaterStorageCapacity(i), 0, 1));
									writer.Write(ug.GetWater(i));
									writer.Write(Math.Clamp(ug.GetEnergy(i) / ug.GetEnergyCapacity(i), 0, 1));
									writer.Write(Math.Clamp(ug.GetWoodRatio(i), 0, 1));
									if (extended)
									{
										writer.Write(ug.GetDailyResourcesInv(i));
										writer.Write(ug.GetDailyProductionInv(i));
									}
								}
								break;
						}
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
            case 5: ExportAsBeautyPrimitives(formations, target, extended: true); break;
			case 6: ExportAsBeautyPrimitives(formations, target, extended: true, roots: true); break;
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
					Irradiances.Add(world.HoursPerTick * 500f);
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
