# Procedural Agro Simulator

This is both a standalone CLI application as well as a Godot project. Very early development stage.

## Setup
0. Make sure to use `--recursive` flag for checkout
1. Install [.NET 6](https://dotnet.microsoft.com/en-us/download/dotnet/6.0)
2. Install Python 3
3. Install requirements of `agroeco-mts3` wither with Conda or from `requirements.txt`
4. Optionally install [VS Code](https://code.visualstudio.com/) (if you want to change the code)
5. Optionally download and extract [Godot](https://godotengine.org/download) with mono support (if you want to see the simulation in 3D)

Make sure to set telemetry opt-outs for both `.NET` and `VS Code` if you desire to protect your usage data.

Also make sure to select `dotnet CLI` for Godot builds under Editor > Mono > Builds > Build Tool.

## Usage
Outside of VS Code, you need to start the rendering server first:
* `cd whatever_path/AgroGodot/agroeco-mts3` then `python3 render-server.py --port 9000` and keep it running
* In VS code there are combined launchers provided, but they may fail (at least on my machine they do)

The simulation can be started in three modes:
* For running as a WebAPI server `cd whatever_path/AgroGodot` application either call `dotnet run -f net6.0 --project AgroServer/AgroServer.csproj` or open the `AgroGodot` folder in VS Code, select a `WebAPI` launch option and hit F5. WebAPI version targets net6.0. The server listens on port `7215`. To explore or try out the API, check `https://localhost:7215/swagger/index.html` (accept self-signed certificate).
* For running the stand-alone `cd whatever_path/AgroGodot` application either call `dotnet run -f net6.0 --project Agro/Agro.csproj` or open the `AgroGodot` folder in VS Code and hit F5 to launch it. Standalone version targets net6.0.
* For running inside of Godot: launch Godot, select and load the project, the hit play or F5. Note that the first build will fail due to async deletion of extra files. Further builds should run fine. In case of any strange errors (like access denied, lots of declaration or symbol missing errors and similar) delete the whole `.mono` subfolder. Godot 3 only supports .NET 4.7.2 or .NET Core 3.1 (partially), but at least some of the new language features are carried over using `<LangVersion>latest</LangVersion>` in the csproj. Reading JSON files is tricky, it needs explicit installation of `System.Text.JSON` from NUGet and net472 as target.

## Options
* The primary option for passing settings is choosing the WebAPI server and sending a `POST` request with setting formated as `json`. See above in *Usage*.
* The secondary option are command line argmuents of the CLI mode, basically you pass a path to a `json` file with the same syntax as in the WebAPI mode.

Most settings can be found in the following two files: `Agro/Initialize.cs` and `Agro/AgroWorld.cs`. They are shared for all modes. Godot only take the defaults, it has so far no option for consuming `json` settings. There is no GUI so far.

## Structure
The first set of projects contains the simulation core:
* **AgentsSystem** contains general and abstract data structures, interfaces etc.
* **Agro** inherits from AgentsSystem, overrides and adds much of functionality specific for the agricultural use-case. It also contains an executable stand-alone program.
* **AgroServer** a slim WebAPI server.
* **Utils** just a few useful utilities: `Pcg` a well controlable random numbers generator, `Vector3i` an integer vector struct, `NumericHelpers` extension methods for conversion of common .NET geometry types to Godot's own.

The following folders are related to the Godot rendering:
* **.mono** is the build folder for Godot. No need to touch it.
* **addons** contains the `trackball` plugin for Godot (3rd party) and `multiagent_system` which a thin wrapper for the agents-based simulation framework.
* **GodotBindings** defines how formations are rendered in Godot.