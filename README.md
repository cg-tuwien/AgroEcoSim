# Procedural Agro Simulator

The plants simulator can be used as a standalone CLI application, or a HTTP server. There is the new web graphic interface (powered by Three.js) as well as a Godot branch (not maintained anymore). The simulation currently includes a procedural weather generator, simple water diffusion in soil and the plant growth itself. A light simulator that provides irradiance values for the plants important for photosynthesis is a [separate component](https://github.com/cfreude/agroeco-mts3/).

### Docker
If you want to try out the Docker app, have a look in the [Releases](https://github.com/cg-tuwien/AgroGodot/releases) section or download the source and run `buildDocker.sh` (on Linux). Then call `docker compose up` in the directory to start the app. The web GUI will be then available at `localhost:8080`.

### Standalone CLI
This headless mode (subfolder `Agro`) is mainly indended for development and testing when you need to run a single simulation. On start it pings the light simulation server and then calls it in each timestep. Constant light is used as a fallback option.

### HTTP Server
The second headless mode (subfolder `AgroServer`) is designed for running multiple simulations in a row. It reduces the overhead of starting new processes over again. The HTTP server runs inside of the Docker along with the light simulation server.

### Web GUI Server
A webserver (subfolder `ThreeFrontend`) based on [Node.js](https://nodejs.org) is the primary GUI. It uses [Three.js](https://threejs.org) on top of [preact](https://preactjs.com).

### Godot (legacy)
Godot used to be our first GUI. Since it was replaced by the web interface it is not maintained anymore and has been removed from the main branch. The legacy version can be found in the `godot4` branch. The graphics mode allows to analyze the simulation in 3D using the open source [Godot Engine](https://godotengine.org/). It integrates the simulation as a Godot component. Before launching set the first line in `AgroGodot.csproj` to determine the target Godot version.


## Setup
0. Make sure to use `--recursive` flag for checkout
1. Install [.NET 6 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/6.0) or higher
2. Install Python 3
3. Install requirements of `agroeco-mts3` either with Conda or from `requirements.txt`
4. Optionally install [VS Code](https://code.visualstudio.com/) or any other code editor of your choice (if you want to change the code)

Make sure to set telemetry opt-outs for both `.NET` and `VS Code` if you desire to protect your usage data.

## Usage
The simulation can be started in two modes:
### WebAPI server
* `cd whatever_path/AgroGodot` and either
* A) call `dotnet run -f net6.0 --project AgroServer/AgroServer.csproj` or
* B) open the `AgroGodot` folder in VS Code, select a `WebAPI` launch option and hit F5.

The WebAPI version targets net6.0. The server listens on port `7215`. To explore or try out the API, check `https://localhost:7215/swagger/index.html` (accept the self-signed certificate in browser). The `POST` request should contain settings for the simulation formated as `json`. See below in [Options](#options). The server responds with an array of plant volumes (in m³) with stable ordering (wrt. request):
```json
{
  "plants": [
    { "V": 0.0017850252  },
    { "V": 0.0015580055  },
    { "V": 0.0016916988  }
  ]
}
```

The server can be access directly over its API or in combination with the web app. Starting the frontend on your local development machine requires to:
* `cd ThreeFrontend`
* `npm install` if running the first time
* `npm run dev` and keep it running
* you can then open the app in browser under `localhost:8080`

### Stand-alone CLI
* `cd whatever_path/AgroGodot` and either
* A) call `dotnet run -f net6.0 --project Agro/Agro.csproj` or
* B) open the `AgroGodot` folder in VS Code, select `Debug CLI` and hit F5 to launch it.

The Standalone version targets net6.0. Settings for the simulation can be passed using the `--import` flag pointing to a `json` file. See below in [Options](#options).

## Global Illumation Backend
The simulation probes several ports to find if a global illumination solver is running. If none is found it will use the ambient mode that assumes constant light everywhere and any time.

### Mitsuba erndering backend
Before starting the simulation, perform the following steps:
* `cd whatever_path/AgroGodot/agroeco-mts3` then `python3 render-server.py --port 9001` and keep it running
* Mistuba must be always associated with port `9001`
* There is an internal check for the presence of a rendering server that falls back to constant light if the server can't be reached.

## Options
Most settings and their default values can be found in the following two files: `Agro/Initialize.cs` and `Agro/AgroWorld.cs`. They are shared for all modes. The WebAPI and CLI allow for passing a `json` file that overrides the defaults. The following example covers the basic available options. Position and sizes are given in metric units. Do not be worried about the short property names for now.
```json
{
  "HoursPerTick": 4,
  "TotalHours": 1440,
  "FieldResolution": 0.5,
  "FieldSize": {
    "X": 10,
    "D": 4,
    "Z": 10
  },
  "Seed": 42,
  "Species": [{
      "N": "default",
      "H": 12,
      "ND": 0.04, "NDv": 0.01,
      "BMF": 1,
      "BDF": 0.7,
      "AP": 40,
      "AR": 1,
      "BLN": 2,
      "BR": 0, "BRv": 0.09,
      "BP": 0.7, "BPv": 0.09,
      "TB": 0.5,
      "TBL": 1,
      "TBA": 0.98,
      "SG": 0.2,
      "WGT": 2400, "WGTv": 240,
      "LV": 2,
      "LL": 0.12, "LLv": 0.02,
      "LR": 0.04, "LRv": 0.01,
      "LGT": 480, "LGTv": 120,
      "LP": 0.35, "LPv": 0.087,
      "PL": 0.05, "PLv": 0.01,
      "PR": 0.0015, "PRv": 0.0005,
      "RS": 50.0005,
      "RG": 0.2
  }],
  "Plants": [
    { "S": "default", "P": { "X": 5, "Y": -0.01, "Z": 5 } }
  ],
  "Obstacles": [
	{ "T": "wall", "O": 0, "L": 5, "H": 3.2, "P": {"X": 2.5, "Y": 0, "Z": 0}},
	{ "T": "umbrella", "R": 1.5, "H": 2.2, "D": 0.1, "P": {"X": 2.5, "Y": 0, "Z": 2.5}}
  ],
  "RequestGeometry": true,
  "RenderMode": 1,
  "SamplesPerPixel": 2048,
  "ExactPreview": false,
  "DownloadRoots": false
}
```
For a complete reference see the files in `Agro/RequestModels`. It also contains explanations for all the short names used. When running the `AgroServer` there is also the option to browse the API using Swashbuckle at `http://localhost:7215/swagger/index.html`.

## Structure
The first set of projects contains the simulation core:
* **AgentsSystem** contains general and abstract data structures, interfaces etc.
* **Agro** inherits from AgentsSystem, overrides and adds much of functionality specific for the agricultural use-case. It also contains an executable stand-alone program.
* **AgroServer** a slim WebAPI server.
* **Utils** just a few useful utilities: `Pcg` a well controlable random numbers generator, `Vector3i` an integer vector struct, `NumericHelpers` extension methods for conversion of common .NET geometry types to Godot's own.

# Renderer interface
The renderer is called via `http`. The primary renderer is Mitsuba 3 in the [agroeco-mts3](https://github.com/cfreude/agroeco-mts3/). Attention, Mitsuba requires AVX instructions, so it won't run on older CPUs. To plug-in a different renderer, it has to follow these guidelines:
* Listening at port `9000`
* Respond with a status code `200` (OK) to a `GET` request, this is a check for whether the server is up
* Respond to `POST` requests with scene data by returning accummulated irradiances in (W/m²)
* The energy should be accumulated around the `570 nm` wavelength

## POST headers
The body of the `POST` request contains the scene in a binary format as described below. Further more, it uses headers to store additional values:
* `La` and `Lo` contain latitude and longitude as floats (both mandatory)
* `Ti` is time string in ISO 8601 format with time zone information (mandatory), it marks the initial time of the rendering (the skydome gets accumulated between start and end)
* `TiE` is time string in ISO 8601 format with time zone information (mandatory), it marks the end time of the rendering (the skydome gets accumulated between start and end)
* `Ra` is the number of rays (samples) per pixel (default=128, optional)
* `Cam` specifies the camera parameters to be used instead of irradiancemeters, great for debugging (default=None, optional)

`Cam` is an array of floats stored as a string with values separated by whitespaces. The values are: camera position (3 floats), target point to look at (3 floats), **vertical** FOV angle (1 float), viewport width and height (2 floats).

## Input scene data format
The renderer will receive the scene data in binary form as a set of triangle meshes. There are two variants a triangle-based and a primitive-based.
Both contain a section with sensors that measure irradiance and a section with obstacles that only block and reflect light, but do not measure it.

Plants correspond to entities. Their surfaces are typically light-sensitive plant organs like leaves. Each sensor surface must be associated with a sensor that measures the irradiance exposure (summed all over the surface) in W/m².

* The coordinate system is right-handed with `x: right`, `y: up`, `z: front`
* For introducing the sun movement, north is oriented back `N = [0, 0, -1]` and east is right `E = [1, 0, 0]`

### Triangle Mesh Binary Serialization
```
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
```
Each surface is represented as a set of triangles which are given by vertex indices. After the section with entities, a list of vertices with 3D coordinates is provided.

### Primitive Binary Serialization (separated Obstacles and sensors)
```
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

#disk: anchored in the center, normal facing +Z, default radius <-1, +1>
#cylinder: anchored in the center of the bottom face; main axis +Y
#sphere: anchored in the center
#rectangle: anchored in the center, normal facing +Z
#matrix vector ordering: [ x.X, y.X, z.X, t.X, x.Y, y.Y, z.Y, t.Y, x.Z, y.Z, z.Z, t.Z ]
```
### Primitive Binary Serialization (interleaved Obstacles and sensors)
```
uint8 version = 3
#ENTITIES
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
		#end switch
		bool isSensor

#primitives and matries same as for version 2
```
### Primitive Binary Serialization (Beauty target)
```
uint8 version = 4
#ENTITIES
uint32 entitiesCount
foreach ENTITY
	uint32 surfacesCount
	foreach SURFACE
		int32 parentIndex   #local index within the entity; -1 = root, int.MinValue = invalid
		uint8 organType     #1 = leaf, 2 = stem, 3 = bud
		#case leaf
		#the primitive is a disk (i.e. circle)
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		float32 waterRatio  #[0..1] wrt. to its capacity given by the volume and structure
		float32 energyRatio #ditto
		#case stem
		#the primitive is a cylinder
		float32 length
		float32 radius
		float32 matrix 4x3 (the bottom row is always 0 0 0 1)
		float32 waterRatio  #[0..1] wrt. to its capacity given by the volume and structure
		float32 energyRatio #ditto
		float32 woodRatio   #[0..1]
		#case bud
		#the primitive is a sphere
		3xfloat32 center
		float32 radius
		float32 waterRatio  #[0..1] wrt. to its capacity given by the volume and structure
		float32 energyRatio #ditto
		#end switch

#primitives and matries same as for version 2
```

Note that the matrix defines a local coordinate system (right handed with Y up) for each primitive. Assume there is the local right axis vector `x` (already scaled), local up axis `y` and local front axis `z`. These vectors specify the orientation and scale of the respective local axes given in world coordinates. At last, there is also the translation vector `t` that specifies the center of the local coordinate system in world coordinates. The matrix is serialized as an array of `float32` elements in the following order:
```
[ x.X, y.X, z.X, t.X,
  x.Y, y.Y, z.Y, t.Y,
  x.Z, y.Z, z.Z, t.Z ]

Both `disk` and `rectangle` are 2D bodies anchored in their centers with the normal facing +Z.
```

Since shearing is not supported, the bootom row is just `[0 0 0 1]`, hence full matrix is:
```
[ x.X, y.X, z.X, t.X,
  x.Y, y.Y, z.Y, t.Y,
  x.Z, y.Z, z.Z, t.Z,
    0,   0,   0,   1 ]
```

## Result irradiance data format
The resulting irradiances per surface need to be sent back as a simple array of 32-bit floats preserving the order of the surfaces in the request.

# Cite as
```
@Misc{agroecosim24,
	author =   {Martin Il\v{c}\'{i}k and Christian Freude and Pierre Ecormier-Nocca and Michael Wimmer and Barath Raghavan},
	title =    {AgroEcoSim},
	howpublished = {\url{https://github.com/cg-tuwien/AgroEcoSim}},
	year = {2022--2024}
}
```