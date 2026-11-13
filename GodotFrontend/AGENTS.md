# GodotFrontend agent guide

A standalone desktop viewer for GreenFDT runs, built on **Godot 4.7.2 (.NET, C#)**. It is a client of
AgroServer exactly like `ThreeFrontend/`: SignalR hub `/SimSocket` plus REST under `/Simulation`. Nothing is
simulated in-process. CORS does not apply (a desktop client sends no `Origin`), so the server needs no change.

Pass 1 (this folder today) is the viewer core: connect, run, live playback, stored runs (list, view, delete),
session load/save (`.agro`, legacy `.json`), `.prim` export, the exact web camera, visual mappings, lighting
switches, hover picking with the organ readout, and checkerboard stereo for the LED wall. The side panel follows the
web's split: **Analysis** is what the view shows (mapping, shown classes, picking), **Rendering** is how it is drawn
(lighting effects, stereo). **Deferred** to pass 2: species editor, seed rows and the
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
| `--open-run ID` | view a stored run from the server (with `--frame N` / `--screenshot`: capture that frame) |
| `--frame-file PATH` | show one stored frame (`.g` zlib or raw) without a server |
| `--simulate HOURS` | start a run of the default request on startup |
| `--camera px,py,pz,tx,ty,tz` | position and target, as `still.mjs CAMERA=` |
| `--size WxH`, `--ssaa N`, `--screenshot OUT.png` | offscreen still of the 3D view (box-downsampled in sRGB), then quit |
| `--ui-screenshot OUT.png` | the whole window, side panel included, then quit |
| `--lighting a,b,..` / `--mapping NAME` | force lighting switches (`daylight,shadows,occlusion,indirect,bounce,translucency,sky`) or a data mapping for stills |
| `--replay SCRIPT` / `--replay-input SCRIPT` | a pointer script straight into the camera maths / as real input events; `--dump-pose` prints every change |
| `--stereo mono\|checkerboard`, `--eye-separation M`, `--convergence M`, `--stereo-swap`, `--stereo-pattern` | force the stereo setup (a fixed convergence replaces "converge on the orbit target") |
| `--transition-probe DIR` | press F10 twice and grow the window, then judge every frame drawn afterwards (see "Resizing") |
| `--settings PATH` | preferences file to use instead of `user://settings.json`; use it for every scripted run |
| `--no-ui`, `--wheel-accumulate`, `--debug-lighting`, `--debug-picking`, `--debug-stereo`, `--timeout S`, `--settle N`, `--quit` | diagnostics |

Anything forced on the command line (`--mapping`, `--lighting`, the stereo flags) applies to that run only and is
never written to `user://settings.json`. It used to be: a capture with `--eye-separation 0` left every later
interactive start with flat stereo.

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

## Stereo for the LED wall

The Rendering tab's Stereo mode offers Mono, Stereo Checkerboard and a disabled "Stereo OpenXR (future)". The
checkerboard follows the RadFoam LED-wall viewer (github.com/gramegnadominik/vr-radfoam, `LedWall.cs`,
`led_wall.gdshader`): every pixel shows one eye, counted from the top-left corner from zero - **even rows
L R L R ..., odd rows R L R L ...**, i.e. left when x + y is even (`Core/Stereo/Stereo.cs`, `StereoMath.EyeAt`; the
student's shader puts the right eye on (0, 0), so its convention is the swapped one).

- `App/Render/StereoRig.cs`: two eye SubViewports share the main viewport's `World3D` and render through off-axis
  frusta (`Camera3D.SetFrustum` with a horizontal offset, the eye moved half the separation along camera right; the
  frusta meet in the convergence plane, no toe-in). A full-rect `ColorRect` inside the main viewport interleaves them
  with `texelFetch` (`App/Shaders/stereo_checkerboard.gdshader`). The main viewport's own 3D pass is disabled while
  stereo is on, so the scene is drawn twice, not three times; in mono the eye viewports stop and shrink to 2 x 2.
- Convergence follows the orbit target by default (what the camera orbits sits on the wall, nearer things come out of
  it) or a fixed distance. Eye separation defaults to 6 cm, as the RadFoam viewer. Swap eyes corrects a window placed
  at an odd pixel offset; the test pattern paints the left eye red and the right eye blue.
- F11 toggles fullscreen and F10 the side panel. Settings live in `user://settings.json` (never in a session); the
  test pattern is never stored.
- **The interleave only survives a one-to-one pixel mapping.** `FRAGCOORD` in the SubViewport counts from its
  top-left pixel (measured: 256,000 of 256,000 test-pattern pixels as specified), and the container that shows it
  samples with nearest filtering while stereo is on. The side panel width is rounded to whole pixels: at 1366 px a
  quarter is 341.5, and a fractional viewport width would have the container resample the view. Measured with the
  panel visible at 1366 x 768: 0 of the viewport's pixels on the wrong eye.
- Eye cameras are recomputed every frame and written only when they changed; writing a camera marks the rendering
  server dirty and would defeat low-processor mode.
- **The eye viewports are children of the main viewport, not siblings.** Godot renders child viewports before their
  parent, and only that order guarantees the composite samples eyes rendered in the same frame; as siblings the order
  was unspecified. They also follow the main viewport's size in its `SizeChanged`, not one frame later.
- Measured on the fixture session at 640 x 400: stereo with zero separation against mono, 1 differing pixel unlit and
  15 with daylight, shadows, occlusion, sky and translucency (the eye viewports get the shared environment); with 6 cm
  the near soil edge sits 5-17 px further right for the left eye than for the right (crossed disparity), growing
  towards the camera.

## Resizing: hold the last image, keep drawing

Reported: F10 and F11 left a colourful pattern on screen for a while. Measured with `--transition-probe` before the
fix: low-processor mode drew exactly one frame per change, and in stereo that frame showed freshly allocated eye
textures nobody had rendered into - the Godot boot splash after F10, rainbow-striped noise after a window resize -
which then stayed until something else (a mouse move) caused a redraw.

- `InputRouter` draws the 3D viewport's texture itself (it is a `Control`, no longer a `SubViewportContainer`, which
  resizes its viewport the moment the layout changes). While the panel and the viewport disagree, the last image is
  drawn at the panel's height, centred (`InputRouter.HoldRect`), on the theme background. The camera keeps its
  vertical field of view, so that is exactly the middle of the image the new size will show; in stereo an unscaled
  image moves by whole pixel pairs, so the interleave holds. Once the panel has kept a size for 120 ms the viewport
  is reallocated, and it renders before the window does in the same frame.
- Any change of the panel or the window, and F10/F11 themselves, switch low-processor mode off for one second
  (`AppRoot.KeepDrawing`), so the held image, the render at the new size and the effects' history all reach the
  screen without waiting for the mouse.
- After the fix, at 1280 x 800 with the fixture session, mono, stereo and zero-separation stereo alike: the first frame
  2-9 ms after F10, 7-11 held frames, then only settled ones; where the old and the new image overlap they agree to the
  pixel (0 of 768,000). Growing the window: every judged frame held or settled.
- Two things the probe cannot change. A window resize first goes through the window system: on the development
  desktop the main loop sits 100-300 ms (swapchain and X11 resize handling, with and without readbacks) before any
  frame, so window steps report that delay instead of failing on it. And the window manager there refused to shrink
  a window it had just grown, so the probe's last step is usually skipped. Real fullscreen was not toggled in the
  probe, so as not to take over the screen; for the app it is the same resize.
- Probe artefacts: reading every drawn frame back stalled the main loop for over a second when the next change came
  within 0.6 s of the previous burst ending (steps are therefore 3 s), and a frame read back after a resize event has
  arrived is the new, undrawn window texture - such frames are counted and left out.
- **`:0` is the user's own desktop.** Scripted runs open windows there: pass `--settings` with a scratch file so
  the user's preferences are not overwritten, inject input inside the app (as the probe does), never through XTEST,
  and never capture the screen - other windows overlap ours.

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
- **Opening a stored run replaces the whole scene**, not only the plants: trays, imported obstacles, the field box and
  the seeds go before anything is fetched. A run built on a field model shows its stored trays, or no soil at all
  when the server has none - never the plain box of whatever scenario was on screen (`RendererBridge.ShowFieldBox`
  used to skip the box while trays were shown, and a failed terrain fetch left the previous soil standing). Every
  scene-replacing operation bumps a generation counter, and an open whose answer arrives after a later open, load or
  run start returns false without drawing anything.
- **`Array.Sort(keys, items, index, length)` applies the index to both arrays.** `MeshBvh` sorted a node-sized key
  array with the node's offset into the triangle list, which threw for every right-hand child below the root - the
  first stored facade run crashed in static picking. The small fixtures never reach that depth;
  `BvhAgreesWithBruteForceOnALargeMesh` does.

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
DISPLAY=:0 godot --path GodotFrontend -- --load Tests/fixtures/fixture-session.json --frame -1 --size 640x400 \
    --no-ui --stereo checkerboard --stereo-pattern --settle 40 --ui-screenshot $PWD/build/shots/pattern.png
DISPLAY=:0 godot --path GodotFrontend -- --server http://127.0.0.1:7215 --open-run RUN_ID --frame 300 \
    --size 960x600 --screenshot $PWD/build/shots/run.png
DISPLAY=:0 godot --path GodotFrontend -- --settings /tmp/probe-settings.json --load Tests/fixtures/fixture-session.json \
    --frame -1 --size 1280x800 --camera 0.6,0.5,1.0,0,0.15,0 [--stereo checkerboard] --transition-probe $PWD/build/probe
```

The harness also opens every stored model run over a plain-field run and checks what soil is left on screen, and
that a superseded open draws nothing.

The snap Chromium (156) runs headless again; it cannot read `/tmp`, so the profile goes under `$HOME` through
`TMPDIR`. The web build must be served on `http://localhost:8080` (the server's CORS origin); `still.mjs` and
`loadsession.mjs` now accept the renamed "Load run" button.
