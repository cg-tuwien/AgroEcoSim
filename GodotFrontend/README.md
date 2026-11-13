# GodotFrontend

A desktop viewer for GreenFDT plant simulations, built on Godot 4.7 (.NET). It talks to an AgroServer the same way
the web frontend does: start runs, watch them grow, scrub through stored runs, load and save sessions.

## Run

1. Start AgroServer (`cd AgroServer && dotnet run -c Release`), by default on `http://localhost:7215`.
2. `godot --path GodotFrontend` (Godot 4.7.2 .NET), or the exported `build/GodotFrontend.x86_64`.
   Another server: `-- --server http://host:port`, or the `AGRO_SERVER` environment variable.

## Use

- **Camera**: left-drag orbits, right-drag pans, middle-drag or the wheel zooms; Shift/Ctrl + left pans and
  Shift/Ctrl + right orbits. Exactly as in the web viewport.
- **Home**: Simulate/Stop, the timeline and transport buttons, memory and progress, load/save `.agro` sessions
  (compatible with the web app), download the current frame as `.prim`.
- **Simulation**: time step, horizon, start date, seed, render mode, contact engine, field size, seeds.
- **Analysis**: data mappings (water, energy, irradiance, ...), visibility toggles, lighting (daylight, sun and
  shadows, occlusion, indirect light, leaf translucency, sky), and the readout of the organ under the cursor.
- **Runs**: the server's stored runs - view or delete them.

Developer notes, commands and measurements are in [AGENTS.md](AGENTS.md).
