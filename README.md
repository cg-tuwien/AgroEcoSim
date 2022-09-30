# Procedural Agro Simulator

This is both a standalone CLI application as well as a Godot project. Very early development stage.

If you want to try out the Docker app, have a look in the [Releases](https://github.com/cg-tuwien/AgroGodot/releases) section.

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

The WebAPI version targets net6.0. The server listens on port `7215`. To explore or try out the API, check `http://localhost:7215/swagger/index.html` (accept the self-signed certificate in browser). The `POST` request should contain settings for the simulation formated as `json`. See below in [Options](#options). The server responds with an array of plant volumes (in m³) with stable ordering (wrt. request):
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
* launch Godot (4.x not supported due to Godot bugs, see below),
* select and load the project
* hit play or F5

Note that the first build will fail due to async deletion of extra files. Further builds should run fine. In case of any strange errors (like access denied, lots of declaration or symbol missing errors and similar) delete the whole `.mono` subfolder. Godot 3 only supports .NET 4.7.2 or .NET Core 3.1 (partially), but at least some of the new language features are accessible through `<LangVersion>latest</LangVersion>` in the csproj. Reading JSON files is tricky, it needs explicit installation of `System.Text.JSON` from NUGet and net472 as target. Other assemblies can't be loaded at all. Hopefully, it gets easier once Godot 4.x is stable enough to work with.

Here are the Godot 4.0-beta1 issues so far reported:
* (Crash at material settings)[https://github.com/godotengine/godot/issues/66175]
* (Material rendered black)[https://github.com/godotengine/godot/issues/66214]

Godot only take the hard-coded default simulation settings, it has so far no option for consuming `json` settings. There is also no GUI so far.

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