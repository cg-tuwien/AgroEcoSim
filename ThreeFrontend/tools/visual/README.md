# Visual regression harness

Drives headless Chromium over the DevTools Protocol to screenshot the viewport and diff two
builds pixel for pixel. Node 22 supplies both `fetch` and `WebSocket`, so there are **no
dependencies** - no puppeteer, no image library (PNGs are decoded inside the browser).

It exists because the renderer refactor cannot be validated by type checks or unit tests:
the bug it caught (a minifier eliding bare signal reads, so plants never rendered) reproduced
only in a production build.

## Usage

    # serve a build, then:
    node loadsession.mjs http://127.0.0.1:8099/ ./fixture-session.json out.png
    MAPPING=water node loadsession.mjs http://127.0.0.1:8099/ ./fixture-session.json out.png
    node diff.mjs baseline.png candidate.png diff.png     # magenta = differing pixels
    node scan.mjs shot.png                                # colour census + content bbox
    node crop.mjs shot.png crop.png X Y W H SCALE         # nearest-neighbour zoom

    # offline renderer
    W=1280 H=800 SSAA=2 node still.mjs URL ./fixture-session.json still.png
    FORMAT=webm TIMING=simTime FPS=10 node animate.mjs URL ./fixture-session.json out.webm
    node playcheck.mjs out.webm video/webm                # actually decodes it in Chromium

`fixture-session.json` is a saved session (163 plant primitives, terrain, one seed). Loading
it feeds both builds byte-identical scene data, which live simulation cannot: the backend
carries state between runs, so two identical requests produce different plants.

## Gotchas discovered the hard way

- Chromium is a snap here, so it cannot read `/tmp`. Fixtures must live under `$HOME`.
- File chooser and download need a **trusted** click (`Input.dispatchMouseEvent`); a
  script-initiated `.click()` carries no user activation and is silently ignored.
- preact maps `onChange` to the **input** event. Dispatching only `change` updates the DOM
  value but never reaches the handler, so the signal keeps its old value.
- `ExactPreview` deadlocks the backend: the hub loop only advances once the client has called
  `Preview()`, and that call never arrives. Leave it off.
- Headless needs `--use-angle=swiftshader` or there is no WebGL2 context to render into.

## Fixture location

Chromium's snap confinement cannot read `/tmp`, so copy the fixture under `$HOME` first:

    mkdir -p ~/agro-fixture && cp fixture-session.json ~/agro-fixture/session.json

## voxelcheck.mjs

Analytic gate for the voxel density field (plan phase 14).

```
CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader" \
  node voxelcheck.mjs "http://127.0.0.1:8099/?gpu=1&debughook=1" ~/agro-fixture/session.json
```

Loads the fixture, ticks the density overlay (which is what builds the field), then diffs the
GPU field against the CPU reference in `core/VoxelGrid.ts` cell by cell. The reference itself
is covered by unit tests, so a disagreement here means the WGSL port drifted.

`?debughook=1` is required: it is what exposes the renderer on `window.__renderer`. Nothing is
exposed without it.

Expect `relError` around 3e-5 and identical occupied-cell counts; anything larger is a real
divergence, not float noise.

`still.mjs` takes `VOXELS=1` to capture the overlay itself, which is the only way to see the
field - a numerically perfect field can still be full of holes.

## Two ways to fool yourself with this harness

**Restart the server after every build.** `sirv` builds its file map at boot, and each build
emits new content-hashed bundle names, so the page 404s its own bundle and loads empty. It
presents as "the app suddenly has no buttons", which looks like a code regression and is not.

**Pass `?debughook=1` whenever `TIER` is set.** Setting the checkboxes and sleeping is not
proof they took effect: a run that captured before the lighting signals propagated once
produced a tier-0 image labelled tier 1, and the resulting 25 % "difference" between two
adjacent tiers looked exactly like a renderer regression. `still.mjs` now reads back the
lighting the renderer actually resolved and refuses to shoot if it disagrees, but it can only
do that through the debug hook. Shadows are the one exception it tolerates: they are sun
shadows, so with no sun above the horizon they resolve off regardless of the checkbox.

## The empty-field invariant (plan phase 15)

The strongest automated check on the GI: with nothing in the density field, Tier 3 must be
byte-identical to Tier 2.

```
for t in 2 3; do
  W=640 H=400 SSAA=1 TIER=$t SKIPLOAD=1 NOPLANTS=1 node still.mjs "http://localhost:8080/?gpu=1&debughook=1" x.json ./e-t$t.png
done
node diff.mjs ./e-t2.png ./e-t3.png ./e-d.png   # must be 0 differing
```

`SKIPLOAD=1` matters: hiding leaves and roots still leaves the stems, so the grid is not
actually empty. `NOPLANTS=1` additionally unticks the seed toggle. Terrain is excluded from
the field by design, so a scene with no plants and no seeds injects nothing at all.

**Running `NOPLANTS=1` without `SKIPLOAD=1` does not test this invariant.** The fixture's stems
survive all three visibility toggles and they are injected into the density field, so Tier 3
legitimately differs from Tier 2 by a hundred-odd pixels - every one of them brighter, none
darker, which is the additivity property behaving correctly. It reads exactly like a broken
invariant. Check the flags before believing the failure.

## bouncecheck.mjs / densecheck.mjs

`bouncecheck.mjs` measures the GI second bounce in the density field rather than on screen.
Screen pixels only see the fraction of the frame the canopy covers; the coarsest mip of the
radiance texture is one texel that, because the mip chain is an arithmetic mean of a
sigma-premultiplied quantity, is exactly the volume average. It reports the ratio between one
bounce and two, and the compacted occupied-cell count.

```
node bouncecheck.mjs "http://localhost:8080/?gpu=1&debughook=1" ~/agro-fixture/session.json
```

`densecheck.mjs` builds a denser scene (small field, seeds on a raster) to see how the bounce
scales with occupancy. NOTE: its simulation step does not currently start a run - it measures
the seed geometry only, which is still a useful density sweep, but it is not a grown canopy.

`diff.mjs` reports `brighter`/`darker` counts relative to the FIRST image, which is what makes
"this pass may only ever brighten" checkable.

## still.mjs environment

| Var | Effect |
|---|---|
| `W`, `H`, `SSAA` | Offline still size and supersampling |
| `TIER` | Lighting, as the legacy 0..4 ladder; translated to the checkboxes by `lightingScript` |
| `DAYLIGHT` | `1` ticks the daylight switch as well |
| `CAMERA` | `px,py,pz,tx,ty,tz` - overrides the camera. Needs `?debughook=1`. Merges into the live pose, because CameraPose also carries up/fovY/near/far and a partial one renders an empty frame |
| `SKIPLOAD` | Do not load the fixture (for the empty-scene invariant) |
| `SIMULATE` | Run a simulation of N hours instead of loading the fixture |
| `FRAME` | Seek to a specific history index instead of the end |
| `NOPLANTS` | Untick showLeaves/showRoots/showSeeds |
| `VOXELS` | Enable the density-field overlay |
| `ALLLOGS` | Include warnings in the output, not just errors |

`CAMERA` matters more than it looks: the saved pose frames the whole field, and the fixture's
plant sits at the terrain corner where the sun throws its shadow off-field. Any shadow change
measured from the default pose is measuring nothing.

`animate.mjs` also takes `TIER`, which is how the "does the video respect the lighting
switches?" check is run: export at `TIER=0` and `TIER=1`, extract a frame from each zip
(`FORMAT=png-zip`) and diff them. They must differ by roughly what the viewport differs by.
An export resolves lighting per frame from the frame's own timestep, NOT from the playhead.

`still.mjs` reports `cfg.resolved` when `?debughook=1` is set: the lighting the RENDERER
actually resolved, not what the DOM says. A checkbox that was found is not a signal that
updated, and the two have disagreed before - several hours went into probing a shader that was
never the problem because only the DOM had been checked.

## pickprobe.mjs

Checks that a hover does not leave the pick pass's 1x1 skewed matrix in the shared frame
uniform. The depth+normal prepass draws through that buffer, so a leftover made shadows and
occlusion follow the cursor. Offscreen stills cannot catch this - `renderToPixels` writes its
own uniform first - which is why the bug survived every measurement in this harness.

## makefixture.mjs

Runs a simulation and captures the app's own Save output, so the same multi-frame history can
be replayed identically across builds. Live runs cannot be compared - the backend keeps state
between runs, so two identical requests grow different plants.

    SIMULATE=1440 TICK=4 START_DATE=2026-05-01 node makefixture.mjs "http://localhost:8080/?gpu=0" out.json

`START_DATE` matters more than it looks. Without one the backend defaults to 1 February, and a
February sun at 48 degrees north grows a plant of about 2e-6 cubic metres over 960 simulated
hours - the fixture ends up being a picture of bare soil.

## playbench.mjs

Measures the CPU cost of advancing the playhead, on a fixture so two builds can be compared.

    FIXTURE=out.json LAPS=2 node playbench.mjs "http://localhost:8080/?gpu=0"

Reports three clocks, and only one of them moves when the decoder changes:

- `syncMs` - the click itself: the playback FSM, the base64 decode and `readAgroScene`.
- `settleMs` - plus the signal effects it schedules, i.e. the instance-list rebuild.
- `stepMs` - plus two animation frames. This is pinned to the display cadence and reports
  about 33 ms no matter what the CPU did, so do not read it as a throughput number.

## multiply.mjs and scalecheck.mjs — production plant counts

The test site renders **7,700 living plants**. Every other fixture here holds one plant, and a
single plant hides every capacity limit in the renderer.

`multiply.mjs` replicates a saved session's plants N times, spread across the field, by parsing
the V7 scene stream and offsetting each copy's affine translation. Stacking them at one point
instead would understate draw-call and buffer pressure and wildly overstate atomic contention in
the density field.

    node multiply.mjs ~/agro-fixture/session.json ~/agro-fixture/x7700.json 7700

`scalecheck.mjs` loads such a fixture and reports whether the renderer survives it: console and
GPU-validation errors, JS heap, load time, and whether anything was actually drawn — via
`renderToPixels`, since the headless canvas never composites.

    LOAD_TRIES=150 TIER=3 node scalecheck.mjs "http://localhost:8080/?gpu=1&debughook=1" ~/agro-fixture/x7700.json

Read `nonClearPixels` carefully. When it equals the total pixel count the frame is **garbage**,
not full — that is what a rejected bind group looks like. A real image sits well below the total.

**All GPU timings from this harness are SwiftShader**, a software rasterizer. They establish
correctness and the absence of validation errors. They are not frame times and must not be
quoted as such.

## toggle.mjs — the interactive path, not the offscreen one

Screenshots the LIVE canvas either side of a lighting checkbox. The offscreen still
(`renderToPixels`) and the interactive viewport are different code paths, and a bug reachable
only from the second will not show up in the first. WebGL composites in headless Chromium so a
real screenshot works here; WebGPU does not.

    TOGGLE=lightOcclusion PRESET=lightShadows node toggle.mjs "http://localhost:8080/?gpu=0&debughook=1" ~/agro-fixture/session.json ./ao

`PRESET` is a comma-separated list of boxes to tick first, so you can test "occlusion on top of
shadows" rather than only "occlusion from nothing".

Read the max delta, not just the pixel count. An effect can touch half a per cent of the frame
and still be invisible if it moves each pixel by 10/255 — that is what the first volumetric AO
did, and the count alone made it look like it was working.

## recovercheck.mjs - a reload must not lose the run

    node recovercheck.mjs "http://localhost:8080/?debughook=1" http://localhost:7215

Starts a run, reloads the tab **while it is still computing**, and then asks the reloaded page what
it has. Asserts the run id survived, the timeline came back from `GET /run/{id}/index`, a frame in
the middle can be fetched and decoded, streaming resumed, and the finished timeline holds exactly
as many frames as the server stored.

`TOTAL_HOURS` (default 1200) sets the run length. It must be long enough that the reload really
lands mid-run - the harness asserts `computing` was still true at that moment, so a run that
finished early cannot pass as a successful mid-run reattach.

Two ways this harness lied before it was right, both worth remembering:

- it waited for the client's `computing` flag to go false, which after a reload is false from the
  first instant, so it sampled a run that was still going. Wait on the **server's** status.
- it asserted no transport control was disabled. At the end of a run the forward ones correctly
  are, and `Previous frame` is too, because a finished run is left in `ForwardWaiting` with the
  playhead one past the last frame. That is pre-existing - see `playstate.mjs`.

## playstate.mjs - what the playback machine looks like when a run ends

    node playstate.mjs "http://localhost:8080/?debughook=1" http://localhost:7215

No reload, no persistence: just runs to completion and reports the playhead. It exists to settle
whether something the recovery harness saw belongs to the reattach path or was already there.
Measured on a plain run: 100 frames, `pointer` 100 (out of range), `playing` 3, `Previous frame`
disabled. All pre-existing, all in the playback FSM rather than in Stage 2.

## timelinecheck.mjs - the slider must span the run, not the download

    PLANTS=300 TOTAL_HOURS=2400 node timelinecheck.mjs "http://localhost:8080/?debughook=1" http://localhost:7215

The server stores every step; the wire sends a frame only when the browser asks for one. At a large
stand the browser asks rarely, so the slider used to span the handful of frames that had arrived
while the progress line under it counted on correctly.

**It truncates the history mid-run on purpose.** At 300 small plants the browser keeps up, every
frame arrives on the wire and the sync has nothing to do - so simply watching the gap passes while
proving nothing, which is what the first version of this harness did. Cutting the history to five
frames reproduces what a large stand does naturally, and the verdict requires frames to come back
**with no payload**, because only the run index can produce those. A run that was never behind
reports INCONCLUSIVE rather than a pass.

## playstate.mjs - what the playback machine looks like when a run ends

    node playstate.mjs "http://localhost:8080/?debughook=1" http://localhost:7215

No reload, no persistence: runs to completion and reports the playhead. Written to settle whether
something the recovery harness saw belonged to the reattach path or was already there - it was
already there, twice over. Expected now: 100 frames, pointer 99, playing 0, step controls live.

## determinism.mjs - is the same request the same run?

    PLANTS=100 TOTAL_HOURS=1600 SEED=424242 node determinism.mjs "http://localhost:8080/?debughook=1" http://localhost:7215

Three runs, not two: a process-global mutated once by the FIRST run looks exactly like per-run
randomness if you only compare a pair. Frames are compared byte for byte through the run store,
after checking the two runs were the same request at all - which is where two earlier attempts
went wrong.

Set the plants and positions explicitly, as this does. `pushSeedRaster` picks a random species per
position, so any two runs seeded with it differ by construction and comparing them measures the
helper. `DETERMINISTIC=1` additionally sets the sequential-tick flag.

## heapcheck.mjs - where the heap actually goes

    CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader" \
      node heapcheck.mjs "http://localhost:8080/?gpu=1&debughook=1" ~/agro-fixture/x7700.json

Loads one 7,700-plant frame and reports the heap at each stage, collecting garbage before every
reading so what is reported is retained rather than merely allocated. It releases the history
payloads, then the decoded-scene cache, then the scene itself, **separately** - one number saying
"a gigabyte is resident" points at nothing, while knowing how much each stage gives back is the
difference between a budget that is too generous and something that never reclaims at all.

Measured: 1,148 MB for one frame, of which 108 MB is the payload the memory budget governs and
579 MB never comes back.

## exportfromstore.mjs - a video from a run that is on disk

    node exportfromstore.mjs "http://localhost:8080/?gpu=1&debughook=1" http://localhost:7215

Runs a real simulation, then exports the animation and checks the export gave the frames back
first. Asserts on **producedBytes**, not on the panel's "Saved N MB" text - `animate.mjs` reports
`done: false` on exports that demonstrably produced a file, so that text is not a completion
signal and treating it as one turns a working export into a phantom failure.

## laiexport.mjs - is the leaf area index export complete?

    OBSTACLES=3  node laiexport.mjs "http://localhost:8080/?debughook=1" http://localhost:7215 small.json
    OBSTACLES=60 node laiexport.mjs "http://localhost:8080/?debughook=1" http://localhost:7215 big.json

Runs a real measurement - walls with plants in front, render mode 3, since modes 1 and 2 light
plants but measure nothing on a facade - and asserts on the export DOCUMENT rather than on a
downloaded blob's size. Empty `days` arrays are both a legitimate outcome of the payload budget and
the symptom of a broken measurement, and only the document distinguishes them.

Run it at both scales. Under the budget every level must carry days; over it the per-obstacle
series go but `selection.days` must survive with real numbers in it, and the full series must be
retrievable from the run store - a `fullSeriesUrl` written into an exported file that 404s is worse
than no URL at all, so the harness fetches and parses it.

## obstaclepaint.mjs - do the obstacle mapping colours hold still?

    cp "<a facade>.obj" build/facade.obj
    CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader" \
      node obstaclepaint.mjs "http://localhost:8080/?gpu=1&debughook=1" "" /tmp/out

Imports a facade through the app's own upload, fabricates a measurement over its named groups, and
separates the two causes of a flickering mapping: the same pose rendered twice (an unstable DRAW)
and the same pose before and after a burst of mouse moves (a REBUILD that corrupts the colours -
picking flips a hover state on every mouse move, so "while the camera moves" may have nothing to do
with the camera). It also reads `__rendererStats()`: orbiting must not change `obstacleRepaints` or
`atlasUploads`.

**It frames the model first, and refuses to continue on an empty render.** The first version did
neither and reported a confident `0 differing` on two blank images - the imported model sits
nowhere near the default camera. Needs an imported mesh: procedural walls carry no colour array and
are never painted.

## obstacledepth.mjs

Analytic regression for the obstacle mapping depth tie. A measured panel overlaps a differently
triangulated, unmeasured panel at facade-scale coordinates; a third panel sits 2 cm in front.
Five camera poses check the measured interior, real occlusion, changed measurement membership,
and exact pixel restoration after switching through None. A wrong renderer or any failed check
exits nonzero. No OBJ or simulation run is needed.

```
node tools/visual/obstacledepth.mjs 'http://localhost:8080/?gpu=0&debughook=1'
CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader" \
  node tools/visual/obstacledepth.mjs 'http://localhost:8080/?gpu=1&debughook=1'
```

For the FAV11 facade, use the real measurement selection (`_T`) when fabricating readings:
the large `P001`–`P006` panels are unmeasured context and share a plane with the tray backs.
Counting distinct Z coordinates across the model does not measure separation of overlapping
surfaces. Nor does the number of pixels changed by moving the camera isolate flicker: ordinary
silhouette motion changes pixels too. Check interior colours against a geometry reference.
