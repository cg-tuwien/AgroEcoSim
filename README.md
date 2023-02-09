# Procedural Agro Simulator

The plants simulator can be used as a standalone CLI application, a HTTP server, as well as a Godot project. The simulation currently includes a procedural weather generator, simple water diffusion in soil as well as the plant growth itself. A light simulator that provides irradiance values for the plants important for photosynthesis is a [separate component](https://github.com/cfreude/agroeco-mts3/).

### Docker
If you want to try out the Docker app, have a look in the [Releases](https://github.com/cg-tuwien/AgroGodot/releases) section.

### Standalone CLI
This headless mode (subfolder `Agro`) is mainly indended for development and testing when you need to run a single simulation. On start it pings the light simulation server and then calls it in each timestep. Constant light is used as a fallback option.

### HTTP Server
The second headless mode (subfolder `AgroGodot`) is designed for running multiple simulations in a row. It reduces the overhead of starting new processes over again. The HTTP server runs inside of the Docker along with the light simulation server.

### Godot
The graphics mode allows to analyze the simulation in 3D usint the open source [Godot Engine](https://godotengine.org/). It integrates the simulation as a Godot component. Unfortunately Godot has many limitations regarding the use of C#. The simulation may behave differently than in the CLI mode. **See the first line in `AgroGodot.csproj` to determine the target Godot version.


## Setup
0. Make sure to use `--recursive` flag for checkout
1. Install [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
2. Install Python 3
3. Install requirements of `agroeco-mts3` either with Conda or from `requirements.txt`
4. Optionally install [VS Code](https://code.visualstudio.com/) (if you want to change the code)
5. Optionally download and extract [Godot 3.x](https://godotengine.org/download) with mono support (if you want to see the simulation in 3D)

Make sure to set telemetry opt-outs for both `.NET` and `VS Code` if you desire to protect your usage data.

Also make sure to select `dotnet CLI` for Godot builds under Editor > Mono > Builds > Build Tool.

## Usage
Outside of VS Code, you need to start the rendering server first:
* `cd whatever_path/AgroGodot/agroeco-mts3` then `python3 render-server.py --port 9000` and keep it running
* In VS code there are combined launchers provided, but they often fail at exceptions and keep the port reserved until reboot.
* There is an internal check for the presence of a rendering server that falls back to constant light if the server can't be reached.

The simulation can be started in three modes:
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

### Stand-alone CLI
* `cd whatever_path/AgroGodot` and either
* A) call `dotnet run -f net6.0 --project Agro/Agro.csproj` or
* B) open the `AgroGodot` folder in VS Code, select `Debug CLI` and hit F5 to launch it.

The Standalone version targets net6.0. Settings for the simulation can be passed using the `--import` flag pointing to a `json` file. See below in [Options](#options).

### Godot
* launch Godot (see `AgroGodot.csproj` for the respective version),
* select and load the project
* hit play or F5

Note that the first build will fail due to async deletion of extra files. Further builds should run fine. In case of any strange errors (like access denied, lots of declaration or symbol missing errors and similar) delete the whole `.mono` subfolder.

Godot only takes the hard-coded default simulation settings, it has so far no option for consuming `json` settings.

## Options
Most settings and their default values can be found in the following two files: `Agro/Initialize.cs` and `Agro/AgroWorld.cs`. They are shared for all modes. The WebAPI and CLI allow for passing a `json` file that overrides the defaults. This example covers all currently available options, position and sizes are given in metric units:
```json
{
    "TicksPerHour": 1,
    "TotalHours": 744,
    "FieldResolution": 0.5,
    "FieldSize": { "X": 10, "D": 4, "Z": 10 },
    "Seed": 42,

    "Plants": [
        { "P": {"X": 2.5, "Y": -0.05, "Z": 5}},
        { "P": {"X": 5, "Y": -0.05, "Z": 5}},
        { "P": {"X": 7.5, "Y": -0.05, "Z": 5}}
    ]
}
```

## Structure
The first set of projects contains the simulation core:
* **AgentsSystem** contains general and abstract data structures, interfaces etc.
* **Agro** inherits from AgentsSystem, overrides and adds much of functionality specific for the agricultural use-case. It also contains an executable stand-alone program.
* **AgroServer** a slim WebAPI server.
* **Utils** just a few useful utilities: `Pcg` a well controlable random numbers generator, `Vector3i` an integer vector struct, `NumericHelpers` extension methods for conversion of common .NET geometry types to Godot's own.

The following folders are related to the Godot rendering:
* **.mono** is the build folder for Godot. No need to touch it, but trashing it may help to solve strange Mono errors.
* **addons** contains the `trackball` plugin for Godot (3rd party) and `multiagent_system` which a thin wrapper for the agents-based simulation framework.
* **GodotBindings** defines how formations are rendered in Godot.

`AgroGodot.csproj` is an umbrella project that takes all files from all subfolders and compiles them for Godot. It defines the `GODOT` switch which activates or deactivates certain blcks of code. This is an ugly, but necessary workaround to cope with Godot's limited .NET support.

## v1 vs. v2
Some classes are denoted by v1/v2 or just 1/2 or stored in folders called v1/v2. This marks whether they are transaction-based (v1) or global-diffusion based (v2), respectively. The *supervised global diffusion* is a custom concept to mitigate the transport limitations of large time steps. Instead of trying to compute at a very dense time rate, the formation gathers all resources in each frame and redistributes them across all its agents at once.

# Renderer interface
The renderer is called via `http`. The primary renderer is Mitsuba 3 in the [agroeco-mts3](https://github.com/cfreude/agroeco-mts3/). Attention, Mitsuba requires AVX instructions, so it won't run on older CPUs. To plug-in a different renderer, it has to follow these guidelines:
* Listening at port `9000`
* Respond with a status code `200` (OK) to a `GET` request, this is a check for whether the server is up
* Respond to `POST` requests with scene data by returning accummulated irradiances in (W/m²)
* The energy should be accumulated around the `570 nm` wavelength

## POST headers
The body of the `POST` request contains the scene in a binary format as described below. Further more, it uses headers to store additional values:
* `La` and `Lo` contain latitude and longitude as floats (both mandatory)
* `Ti` is time string in ISO 8601 format with time zone information (mandatory)
* `Ra` is the number of rays (samples) per pixel (default=128, optional)
* `Cam` specifies the camera parameters to be used instead of irradiancemeters, great for debugging (default=None, optional)

`Cam` is an array of floats stored as a string with values separated by whitespaces. The values are: camera position (3 floats), target point to look at (3 floats), **vertical** FOV angle (1 float), viewport width and height (2 floats).

## Input scene data format
The renderer will receive the scene data in binary form as a set of triangle meshes. There are two variants a triangle-based and a primitive-based.
Both contain a section with sensors that measure irradiance and a section with obstacles that only block and reflect light, but do not measure it.

Plants correspond to entities. Their surfaces are typically light-sensitive plant organs like leafs. Each sensor surface must be associated with a sensor that measures the irradiance exposure (summed all over the surface) in W/m².

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