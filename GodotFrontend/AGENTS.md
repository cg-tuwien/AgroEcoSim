# GodotFrontend agent guide

A standalone desktop viewer for GreenFDT runs, built on **Godot 4.7.2 (.NET, C#)**. It is a client of
AgroServer exactly like `ThreeFrontend/`: SignalR hub `/SimSocket` plus REST under `/Simulation`. Nothing is
simulated in-process. CORS does not apply (a desktop client sends no `Origin`), so the server needs no change.

Pass 1 (this folder today) is the viewer core: connect, run, live playback, stored runs (list, view, delete),
session load/save (`.agro`, legacy `.json`), `.prim` export, the exact web camera, visual mappings, lighting
switches, hover picking with the organ readout. **Deferred** to pass 2: species editor, seed rows and the
seed-distribution import, obstacle editor, terrain tab (site editor, OBJ upload), translate gizmo, fork / continue /
resume and the store log, the leaf area index panel and obstacle mapping (the painter design is in the plan), the
Export tab (stills with turntable, WebM through `ffmpeg`, image zips), glTF export, touch input, folded-leaf (V8
deformation) rendering.

## Layout

```
GodotFrontend/            the Godot project (res://): project.godot, GodotFrontend.csproj, Main.tscn, export_presets.cfg
  App/                    Godot nodes: AppRoot, CameraRig, InputRouter, RendererBridge, ScreenshotRunner
    Render/               MultiMesh batches, geometry upload, materials, terrain, seeds, lighting rig, decode worker
    Shaders/              plant_foliage, plant_opaque, terrain, sky (+ agro_common.gdshaderinc)
    UI/                   theme, widgets, the four side-panel tabs (Home, Simulation, Analysis, Runs), file dialogs
  Core/                   engine-free net8.0 library (.gdignore): camera, picking, decoder, packer, colours, sun/sky,
                          history, playback, run controller, REST + hub clients, .agro reader/writer
  Tests/                  xunit over Core (.gdignore); fixtures/ holds stored frames, the web session and dumped web values
  Harness/                console checks against a running AgroServer, and a decode benchmark (.gdignore)
```

Everything testable lives in `Core` and never references Godot; `Core` references `Agro/Agro.csproj` for the
request and species models (`SimulationRequest`, `SpeciesSettings`, `AgroWorld.SnapHoursPerTick`). Never construct
`AgroWorld` or call `SpeciesSettings.Init()` client-side.

## Toolchain and commands

The editor and templates live outside the repo:

```
~/.local/share/godot/bin/Godot_v4.7.2-stable_mono_linux_x86_64/   (symlinked as ~/.local/bin/godot)
~/.local/share/godot/export_templates/4.7.2.stable.mono/          (linux_release/debug.x86_64 only)
```

```
dotnet build GodotFrontend/GodotFrontend.csproj -c Debug -m:1        # Core + Agro (net8.0) + the app
dotnet test  GodotFrontend/Tests/GodotFrontend.Tests.csproj           # 95 engine-free tests, under a second
godot --headless --path GodotFrontend --import                        # after adding resources
DISPLAY=:0 godot --path GodotFrontend -- --server http://127.0.0.1:7215
godot --headless --path GodotFrontend --export-release "Linux/X11" ../build/GodotFrontend.x86_64
dotnet run --project GodotFrontend/Harness -- --server http://127.0.0.1:7215 --hours 240
dotnet run --project GodotFrontend/Harness -c Release -- --bench runstore/runs/<id>/frames/000423.g
```

A local server for the harness: `cd AgroServer && AGRO_RunStoreRoot=<scratch> dotnet bin/Release/net8.0/AgroServer.dll
--urls http://127.0.0.1:7215`. Develop with `dotnet build` + `godot --path`; the editor's play button hot-reloads
assemblies, which fails while a `HubConnection` or `HttpClient` is alive.

The **.NET 10 runtime crashes on this machine** ("Internal CLR error", also `dotnet restore` of a solution through
SDK 10 and `DOTNET_ROLL_FORWARD=LatestMajor`). The project is `net8.0`, which is also all GodotSharp 4.7.2 ships.

### Command line (after Godot's `--`)

| Flag | Effect |
| --- | --- |
| `--server URL` | AgroServer base URL; else `AGRO_SERVER`, else `user://settings.json`, else `http://localhost:7215` |
| `--load PATH` / `--frame N` | open a session (`.agro` or `.json`) and seek to index N (`-1` = last) |
| `--frame-file PATH` | show one stored frame (`.g` zlib or raw) without a server |
| `--simulate HOURS` | start a run of the default request on startup |
| `--camera px,py,pz,tx,ty,tz` | position and target, as `still.mjs CAMERA=` |
| `--size WxH`, `--ssaa N`, `--screenshot OUT.png` | offscreen still of the 3D view (box-downsampled in sRGB), then quit |
| `--ui-screenshot OUT.png` | the whole window, side panel included, then quit |
| `--lighting a,b,..` / `--mapping NAME` | force lighting switches (`daylight,shadows,occlusion,indirect,bounce,translucency,sky`) or a data mapping for stills |
| `--replay SCRIPT` / `--replay-input SCRIPT` | a pointer script straight into the camera maths / as real input events; `--dump-pose` prints every change |
| `--no-ui`, `--wheel-accumulate`, `--debug-lighting`, `--debug-picking`, `--timeout S`, `--settle N`, `--quit` | diagnostics |

## The camera is three.js r152 OrbitControls, ported line by line

`Core/Camera/OrbitControls.cs` is the reference implementation ThreeFrontend's WebGL backend uses with all defaults:
rotation divides BOTH axes by the viewport height; one 0.95 wheel step per event whatever its magnitude, ignored while
dragging; a middle-drag dolly takes one step per move event by the sign of dy; left + Ctrl/Meta/Shift pans and right +
those rotates; pan is screen-space and scales with distance; polar angle clamped to [1e-6, pi - 1e-6]; no damping, no
keys. The one configured deviation is the WebGPU backend's distance clamp [0.1, 250] (`tests/orbitCamera.test.ts`
asserts it; without it the scene vanishes past the far plane). The basis is built in double by a port of
`Matrix4.lookAt` (Godot's `LookingAt` degrades at the pole the clamp allows) and copied verbatim into the Camera3D:
both look down local -Z, right-handed, no flips.

Input: `InputRouter._GuiInput` on the SubViewportContainer; positions are control-local and the denominator is the
control's height, the analogue of `clientX/clientY` over `clientHeight`. Godot keeps delivering a drag to the control
that took the press, outside the window too, which is the browser's pointer capture.

**Parity, measured**: the shared script `Tests/fixtures/camera-script.json` (left, right, middle, Shift+left,
Ctrl+right drags, six wheel steps) replayed in the web app by `ThreeFrontend/tools/visual/cameraprobe.mjs` and in
Godot by `--replay` and by `--replay-input` ends at the same pose to 1e-15 in all three cases.
`CameraParityTests` pins the web's number.

## Rendering: what is exact, and the traps found on the way

- **Godot's front faces are clockwise; three's are counter-clockwise.** `GeometryUploader` reverses every
  triangle. Without it `FRONT_FACING` is inverted, the double-sided laminae turn their normal away from the viewer and
  get the hemisphere's ground colour: leaves measured (52,98,49) against the web's (88,179,82); after the fix
  (89,179,84).
- **Godot multiplies `DIFFUSE_LIGHT` by the albedo after `light()`.** Multiplying it inside as well made the sun
  about ten times too weak (sunlit soil 152 against the web's 176). Now 176 = 176.
- **The MultiMesh buffer layout is the wire's row-major 3x4** (row i = basis row i, then origin i), then colour, then
  custom data: 20 floats per instance, one `MultimeshSetBuffer` per bucket. Laminae copy through; cylinders get the
  column scale and the half-length recentring. Set `CustomAabb` *before* the upload or the server recomputes bounds
  over every instance. Per-instance non-uniform scale needs `skip_vertex_transform` and the cofactor normal.
- The data mappings ride in the custom data and are evaluated in the shader, so switching a mapping is a uniform,
  not a rebuild. Visibility toggles are a node flag per bucket category.
- The default look is the web's exact hemisphere term in `EMISSION`. Occlusion (SSAO) and indirect light (SDFGI)
  switch the hemisphere into Godot's sky ambient (the sky's radiance pass is the two-tone hemisphere) so those effects
  can act on it; they correspond to the web's GTAO and cone tracing in role, not pixel for pixel. Shadows are a
  shadow map with a caster-side offset in the foliage shader's shadow pass, so harder than the web's density-field
  shadows. The sky is a per-pixel port of `SkyTexture.skyColor` over the same dome model.
- **Parity, measured** on `ThreeFrontend/tools/visual/fixture-session.json`, default camera, 1280x800: against the
  live web viewport (`loadsession.mjs`, three backend) 0.354 % of pixels differ by more than 2 levels, all on edges;
  terrain (149,96,0) and background 250 identical; with the sun on, sunlit soil identical (176,112,0). Compare against
  the **live** viewport: the web's own offline still (`still.mjs`) renders tier 0 differently (soil (77,30,0),
  background 244) from its viewport.
- The web's triangle counter counts V7 petals with the leaf mesh (8616 on the fixture); this app counts what it
  draws (7116).

## Performance, measured

Largest stored facade frame (4,134 plants, 1,191,644 primitives, 52.9M triangles, 86 MB zlib / 107 MB raw):

| step | first frame in the app | steady state (`--bench`) |
| --- | --- | --- |
| inflate (zlib, single stream) | 382 ms | 310-420 ms |
| decode | 325 ms | 95 ms |
| pack (parallel over plant chunks) | 94 ms | 15 ms |
| upload (`MultimeshSetBuffer`) | 187 ms | - |

Inflate dominates and cannot be split for one deflate stream; live frames arrive raw. Hover picking: under 20 ms
per pick at that scale in the test, sub-millisecond on a single plant.

## State and networking

`Core/Run/RunController.cs` is appstate.ts' run lifecycle: upload then hub `Start`, the lazy `Preview` pump (re-armed
when a frame was presented, which the renderer must report even for a frame it skipped), RunStarted/RunSite/Progress/
Result, timeline sync with the server's frame index (a union by step), the history tiers (raw, zlib, archive, evicted;
re-fetch from `/frame/{step}/packed` through an on-disk LRU under `user://framecache`), reattach after a restart, and
sessions. Every hub or HTTP continuation posts to `IMainThread`; `GodotMainThread` drains it in `_Process`.

- **At the live edge, draw the arrived frame directly.** Relying on the playback machine's "pointer moved" test never
  drew frame 0 (pointer 0, arrived index 0), nothing was presented, and the pump never re-armed: one frame per run.
- **Console code that awaits on the main pump deadlocks** if it awaits at top level: the continuation is posted to the
  pump the same thread should be draining. The harness waits by draining (`Run(task)`).
- A session's own view settings replace the scripted `--lighting`/`--mapping`; they are re-applied after a load.

## Verification

```
dotnet test GodotFrontend/Tests/GodotFrontend.Tests.csproj
dotnet run --project GodotFrontend/Harness -- --server http://127.0.0.1:7215 --hours 720     # ALL PASSED
DISPLAY=:0 godot --path GodotFrontend -- --load Tests/fixtures/fixture-session.json --frame -1 \
    --camera 0,3,5,0,0,0 --size 1280x800 --ssaa 2 --no-ui --screenshot ../build/shots/godot.png
CHROME=/snap/bin/chromium TMPDIR=$HOME/agro-tmp node ThreeFrontend/tools/visual/loadsession.mjs \
    "http://localhost:8080/?debughook=1&gpu=0" ~/agro-fixture/session.json web.png
CHROME=/snap/bin/chromium TMPDIR=$HOME/agro-tmp node ThreeFrontend/tools/visual/cameraprobe.mjs \
    "http://localhost:8080/?debughook=1&gpu=0" GodotFrontend/Tests/fixtures/camera-script.json
DISPLAY=:0 godot --path GodotFrontend -- --size 1280x800 --no-ui --replay-input Tests/fixtures/camera-script.json
```

The snap Chromium (156) runs headless again; it cannot read `/tmp`, so the profile goes under `$HOME` through
`TMPDIR`. The web build must be served on `http://localhost:8080` (the server's CORS origin); `still.mjs` and
`loadsession.mjs` now accept the renamed "Load run" button.
