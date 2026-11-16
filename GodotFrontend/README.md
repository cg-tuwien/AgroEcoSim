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
- **Runs**: view stored runs and their protection from automatic cleanup. Unlock writes with the shared password to protect/unprotect a run or permanently delete it; protection can change while a run computes.

The app starts read-only. The password entered through **Unlock writes** stays in memory for this session and is never saved in settings or session archives. Locking detaches live control; stored playback and downloads remain available. New runs start protected, while older runs remain unprotected until marked. A protected run can still be deliberately deleted after confirmation.

The console integration harness requires `AGRO_WRITE_PASSWORD` in its environment and must target an isolated test server. Use HTTPS/WSS when connecting to a production server.

Developer notes, commands and measurements are in [AGENTS.md](AGENTS.md).
