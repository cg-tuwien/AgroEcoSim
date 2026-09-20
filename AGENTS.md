# GreenFDT agent guide

This is a plant growth simulation. Always check physical concepts and botanical
knowledge and lookup data for particular species to obtain correct results.

## Scope and working tree

- The actively maintained simulator projects target **.NET 8**. Treat the
  `TargetFramework(s)` in the `.csproj` files as authoritative; some root README
  examples still say `net6.0`.
- `Agro/Agro.csproj` is the primary build target for plant-simulation changes.
  Its dependencies include `AgentsSystem` and `Utils`.
- This checkout is normally a **dirty worktree** containing generated archives,
  simulation exports, editor settings, and concurrent work. Begin with
  `git status --short`, preserve unrelated changes, and never reset, clean, or
  delete broad paths to obtain a clean state.
- Prefer `rg`/`rg --files` for exploration. Use `apply_patch` for source edits.

## Build and acceptance

From the repository root, compile the core simulator with:

```bash
dotnet build Agro/Agro.csproj --no-restore -m:1 -v:minimal
```

- Keep `-m:1`: it is the known reliable setting in this checkout.
- `--no-restore` assumes the existing restore artifacts are usable. Restore only
  when dependencies changed or a restore error demonstrates it is required.
- A successful build currently has zero errors but many pre-existing warnings.
  Do not obscure a functional fix by attempting a warning-cleanup sweep unless
  that is the requested task.
- Run `git diff --check` after edits. For a source change, compilation alone is
  necessary but not sufficient: run a deterministic simulation probe relevant
  to the changed behavior.

## Code efficiency guidelines

Expect the simulation to run with thousands of plants. Always watch hot paths
and try o design efficient code. Avoid O(N²) and worse algorithms if possible.
Also look at low level code optimization. 

If caching may help, propose it. What can be precomputed should be given at compile time or by reading a file.

Keep the code readable and comprehensive.

## Plant-behavior probe workflow

Use a temporary console probe outside the repository (for example under
`/tmp`), targeting `net8.0` and referencing `Agro/Agro.csproj`. Do not commit
diagnostic harnesses unless explicitly asked.

1. Construct a `SimulationRequest` with one plant, `Seed = 42`, explicit
   `HoursPerTick`, `TotalHours`, and `RenderMode = 0`.
2. Create the world with `Initialize.World(request)` and run
   `world.Run((uint)world.TimestepsTotal())`.
3. Inspect the resulting `PlantFormation2.AG` for:
   - organ counts by `OrganTypes`;
   - water and mass staying finite and biologically plausible;
   - orientation from `Vector3.Transform(Vector3.UnitX, AG.GetDirection(i))`;
   - downward (`Y < 0`) and inverted (`Y < -0.8`) organ counts;
   - target versus actual length/radius of flower organs.
4. Compare a one-hour timestep with the regular four-hour timestep whenever
   changing growth, probabilities, or mechanics. `GrowthTimeVar` already
   includes `HoursPerTick`; do not multiply by it a second time.

Useful regression scenarios:

| Scenario | Purpose | Expected signal |
| --- | --- | --- |
| Carex morrowii 'Variegata', 100 days, 1-hour ticks | Foliage and mass stability | No downward/inverted organs |
| Carex morrowii 'Variegata', 100 days, 4-hour ticks | Timestep regression | No downward/inverted organs |
| Carex morrowii 'Variegata', 450 days, 4-hour ticks | Active flowering culm | Culm remains upright; no inversions |
| Geranium Macrorrhizum, daily through 162 days, 4-hour ticks | Flower chain | Buds precede petals; follow the interval, since a final snapshot can miss a completed bloom |

Longer probes can be costly. Start with one species and one deterministic seed;
expand only after the targeted scenario behaves correctly.

After dealing with a mismatch of simulated output versus botanical ground truth, add a new regression scenario (if not already present) to assure that the issue does not reappear.

Collision and saved-run regression scenarios are documented in
[`Agro/COLLISIONS.md`](Agro/COLLISIONS.md). In particular, preserve the translated
tray/blade-edge sweep test, thin-lamina wall crossing, Bergenia scape under a
ceiling, embedded versus loaded airborne rhizome, and nonlethal obstruction with
daily retries (72 hours after three failed attempts) at both 1-hour and 4-hour ticks.
Also test same-crown leaf contacts, leaf-edge escape, widening and swept crossings,
retained developmental transactions, obstacle removal, and Geranium tangent inheritance.
Check tiny curved leaves as well: mesh relief must scale with actual blade width in
both collision templates and exported matrices, or seedling leaves are millimetres
deep before they have expanded. Relative sibling sweeps must remove their common
parent's elongation and rotation. Exercise four plants with independent contacting
pairs to check deterministic parallel contact scheduling.
Rollback of a support must preserve its descendants' independent growth and bends;
ancestor thickening does not move a child's attachment and must not expand rollback.
Final restoration must copy accepted frames exactly, including tiny support-frame
changes: repeated quaternion normalization can push a tip outside the attachment
exemption boundary and invalidate an otherwise admissible fallback.
At coincident contacts, remembered sides take precedence over the organ-base fallback.
Checkpoint contact tests must compare actual exported frames after resume, including
organ removal/reindexing, warm versus rebuilt clearance caches, and directional-guidance refresh. Live checkpoint version 3
stores stable organ IDs, pending development, retries, sparse contacts and guidance;
older live checkpoints are rejected, while their saved playback remains readable.

## Key simulator invariants and traps

### Water and mass

- In `PlantFormation.Census`, storage distribution sends each organ its
  photosynthetic requirement plus `CapacityWater * factor`. Therefore divide
  excess water by the **full** aggregate `waterStorage`, not by
  `waterStorage - waterRequirement`. The latter is non-conservative and caused
  runaway Carex water/mass and apparent gravity failure.
- Watch for very large water or mass values before changing stiffness values.
  Resource errors can masquerade as bending errors.

### Gravity and support

- Calculate bending from the load component perpendicular to an organ's base
  axis. A vertical stem bears gravity axially and must not receive the maximum
  bending moment.
- Keep the mechanics response timestep-normalized:
  `1 - (1 - responsePerHour)^HoursPerTick`.
- Leaves and petioles must not attain full longitudinal size while their support
  remains too thin. Constrain their growth independently by available radius.
- `TurnUpwards` **preserves the main axis** and adjusts only cross-section
  orientation. Use `PlantFormation2.AdjustUpBase(orientation, up: true)` when
  creating a truly vertical culm or shoot.

### Flower state and growth

- `FlowerMeristem` and direct `FlowerStem` construction choose their target
  dimensions in the constructor. Pass a fully initialized `Flower` (especially
  `flowerBase`) as the constructor argument; setting it only in an object
  initializer is too late.
- Directly-created `FlowerStem` objects need `LengthVar`, `RadiusVar`, and
  `GrowthTimeVar`. Converted meristems already have them.
- Keep floral length and radius growth independent when parent radius limits
  thickness. Coupling them can leave a meristem permanently short and prevent
  buds/petals from appearing.
- `flowerBaseDebth` counts the current basal segment. A value of one means one
  culm, so additional basal segments use
  `depth + 1 < flowerBaseDebth`.

### Flowering phases and species settings

- Trace the generic phase logic in
  `Agro/Plant/PlantSubFormation.cs:GetFloweringPhase` before changing species
  presets. It uses `FloweringStartAgeHours`, seasonal start/end, and
  `FloweringDurationAgeHours`.
- `FloweringEndAgeHours` is currently not used by that generic phase logic;
  do not assume changing it changes runtime behavior.
- Prefer user-authored species phase probabilities and botanical values unless
  they contradict botanical ground truth. Fix general mechanics/state
  bugs before compensating with arbitrary species constants.

### Light and the internal CPU tracer

`RenderMode` selects the light model: 0 constant ambient, 1 Mitsuba3, 2 Tamashii,
3 the internal CPU tracer in `Agro/Irradiance/`. Modes 1 and 2 fall back to 3
when their renderer does not answer, so mode 0 is now the only way to get flat
ambient light. **Switching a scenario from mode 0 to mode 3 changes growth**: a
sunlit horizontal leaf receives about 1.2x the global horizontal irradiance
(its own hemisphere plus what the ground reflects onto its underside) while a
shaded one receives a fifth of that, where mode 0 gave every leaf 500 W/m2.
Re-calibrate species constants against mode 3 before comparing runs.

Units are unchanged: `GetIrradiance` returns Wh/m2 accumulated over the tick.

- `Irradiance/SkyDome.cs` - sun position and sky radiance. Every step of the
  chain is citable (Meinel beam transmittance, Liu & Jordan diffuse,
  Kasten & Czeplak cloud attenuation, Erbs beam/diffuse split, CIE general sky
  for the angular distribution) and the CIE shape is normalised against the
  diffuse horizontal irradiance, so shape constants only redistribute a known
  total. Cloud cover comes from the `SkyCoverage` that `AgroWorld.Init` already
  simulates. North is -Z and east is +X, matching the Mitsuba adapter.
- `Irradiance/RayScene.cs`, `RayBvh.cs` - one hierarchy over the static scene,
  built once, and one over all vegetation, rebuilt every daylight step by
  `BvhBuilder.BuildParallel`. Two traps are recorded in the comments there and
  both were measured, not guessed: a hierarchy *per plant* traces 2.4x slower
  than one flat hierarchy because plants in a stand interpenetrate, and the
  terrain must occlude **one-sided**, or every seedling - seeded a centimetre
  below the soil surface, hence inside the soil hull - sits in the dark.
- `Irradiance/CpuLightSolver.cs` - the gather. One shadow ray per sun sub-step
  plus `LightSamples` hemisphere rays per leaf, both faces. Indirect light is
  not traced recursively: a ray landing on a surface reads that surface's
  exitant radiance from `SurfaceRadianceModel`, built from the previous step, so
  the effective path length grows as the simulation runs.
- `Irradiance/ObstacleLightMeter.cs` + `ObstacleLightSeries.cs` - leaf area
  index and solar load for the obstacles whose name contains
  `LeafAreaIndexObstacles`. Three things about its data model are load-bearing
  and each was forced by a measurement, not by taste:
  - **Per day, never per step.** Each step folds into the running day and the day
    is closed when the local date turns over; nothing per-step is retained. A
    season of per-step records over a few hundred facade objects is hundreds of
    megabytes and answers nothing a daily total does not.
  - **Resolved per orientation and per height band.** An imported wall is a
    solid, so one number per object averages the planted street face against the
    bare courtyard face: measured on a test wall, 14% shading against the 27% its
    planted face actually sees. Height matters for the same reason - foliage grows
    from the planting level, and the bottom band of that wall reached 58% while
    the top read 0.6%. Orientation classes are assigned from each sensor's
    outward normal *after* the scene corrects it, because mesh winding cannot be
    trusted (`Wall` is wound inwards throughout).
  - **Transmittances are ratios of summed energies, never means of per-step
    ratios.** A plain mean lets a dim step just after dawn, where the foliage
    blocks nothing, count as much as noon.

  Two traps recorded in that file, both found by probing: the outward side of a
  surface is the side that sees **more sky**, not the side without geometry within
  a fixed probe distance - the fixed probe left a six metre wall's top and reveals
  pointing inwards and silently dropped them from every reading. And surfaces are
  ordered by the **solar load** they carry, not by area: a slab has two faces of
  equal area, and ordering by area picked the shaded north side of a planted south
  wall as "the dominant facade".

  **The measurement does not depend on which model lights the plants.** Mitsuba
  and Tamashii light plants but measure nothing on a facade, so when a filter is
  set `CpuLightSolver.MeasureObstacles` runs beside them - it needs the same
  traced scene either way, and the only thing it gives up is that the canopy's own
  reflected contribution falls back to the analytic seed. Before that, a leaf area
  index study silently produced nothing the moment Mitsuba happened to be
  reachable, which is exactly how it was reported.

  **A filter that matches nothing says so.** It logs the count and a sample of the
  actual names, and `ObstacleLightMeta` is emitted even with zero matches, so the
  HUD can distinguish "your filter is wrong" from "the measurement never ran".
  Those two were indistinguishable and it cost a debugging session. Note the
  naming rule that decides what is even available: an imported object becomes soil
  when its last underscore-separated part is `S<digit>`, and an obstacle
  otherwise. In the FAV11 facade model the trays are `P001_Y026_T001`, their soil
  is `P001_Y026_T001_S001`, so `_T` matches 1645 of 1651 obstacles.

  **Payload is budgeted.** Sixteen hundred trays with a daily series each is
  ~28 MB of JSON at twelve days and over a hundred at a season, which no socket
  should carry. Above `DailyRecordBudget` the per-obstacle series are dropped and
  only summaries are sent; the selection-wide daily series is always kept, because
  the seasonal curve of the facade as a whole is what the study is about. The
  viewport mapping falls back to the summary when a series is absent, or it would
  go blank on exactly the large scenes it is most useful on.

  Call `Finish()` before reading the results, or the last day stays open.
  `AgroWorld.ObstacleLight` exposes the meter; `ObstacleLightResponse.From`
  snapshots it for the API and calls `Finish` itself.

  **Repainting is gated on a signature, and that is load-bearing.** Picking runs
  on every mouse move and flips an obstacle's hover state, which the renderer host
  digests into the obstacle frame revision - so orbiting the camera re-runs
  `rebuildObstacles` once per mouse-move event. Nothing about a hover changes a
  measured value, but an unguarded repaint rewrote 2.9M floats twice and had the
  WebGPU atlas re-upload its whole 42 MB vertex buffer, every event, which read as
  violent flicker while moving the camera. `writeObstacleColors` therefore returns
  `unpaintable` / `unchanged` / `repainted`, and only `repainted` may trigger an
  upload. If stutter remains on a large scene, the next suspect is the same
  trigger: `rebuildStatics` rebuilds the entire instance list per mouse move, which
  predates this work.

  The frontend reads that series in `helpers/ObstacleLight.ts`, exports it as a
  self-describing JSON document (daily records, whole-period summaries and a
  definitions block - the three leaf area index variants differ by a factor of two
  on the same canopy, so a file that does not say which it carries invites the
  wrong reading), and colours the measured obstacles from it. Colouring needs the
  obstacle group table that terrain blob version 1 appends: imported obstacles are
  merged into one mesh to keep a single draw call, and the table says which
  vertices each named object owns. `core/ObstacleColors.ts` paints them in place,
  because the atlas never reclaims space and re-registering a million-vertex
  facade on every step of playback would grow it without bound.

Cost, measured on 32 cores: about 2.1 us per leaf per step, flat in scale up to
230k leaves (800 plants, 100 days, 4 h ticks: 508 ms per step at 231k leaves and
668k organs, half of total wall time). Verify any change to the tracer against
an analytic case before trusting a simulation result - an unoccluded horizontal
lamina must gather exactly `DHI + groundAlbedo * GHI` of diffuse irradiance.

Obstacles carry a `Name`. Imported OBJ scenes now produce one `MeshObstacle` per
named object instead of one merged mesh, because the leaf area index is reported
per obstacle; the terrain serialization still hands the frontend a single merged
mesh, so nothing changed there.

`IrradianceClient.SkipFormations` lists the formations that carry no live
above-ground plant, and **every export walks the formation list against it**. It
is refreshed once per tick in `Tick`, for every light model. Do not move that
refresh into a light-model branch or behind a daylight test: a plant that has not
germinated is on the list, `ExportToStream` only fills the list when it is empty,
and a light model that never refreshed it left the plant hidden from the viewport
for the whole run while it grew normally in memory. That failure looks exactly
like "it never germinates", and it is invisible to a probe that reads
`PlantFormation2.AG` directly instead of the exported scene. Probe the export.

## Useful code map

- `Agro/Plant/PlantFormation.cs` — plant-level resource distribution and age.
- `Agro/Plant/PlantSubFormation.cs` — gathering, gravity, orientations, and
  flowering phases.
- `Agro/Plant/AboveGroundAgent.cs` and `.Herbaceous.cs` — organ construction,
  growth, branching, and flower commitment.
- `Agro/Plant/Flower/FlowerHelper.cs` — floral growth and state transitions.
- `Agro/Species/*.cs` and `Agro/SpeciesSettings.cs` — species presets and
  configurable biological parameters.

## Herbaceous habit: what a two-year probe showed, and the six mechanisms behind it

Every predefined species was run for **730 days at 4 h ticks, render mode 3, seed 42**
and measured organ by organ. All seven were wrong, and the same handful of mechanisms
explained almost all of it. Read this before touching a species preset - most apparent
"species" problems are one of these.

### 1. A crown axis had no length bound, so every rosette became a column

`ContinueCrown` adds one node per successful continuation roll and nothing ever stopped
it. Measured at day 730: Fragaria had **108 stacked crown nodes and 31 cm of stem**,
carrying 432 leaves at every height along it - the "leaf ball on a stick". Campanula
reached **9.3 m of total stem in a single strand with exactly one branch point**, 2.37 m
tall. That is also why crowns sank: see mechanism 2.

`SpeciesSettings.CrownMaximumNodes` bounds it (0 = unbounded, the old behaviour). Set it
to roughly *live leaves / `LateralsPerNode`*. A capped apex rests; `RenewPetiole` keeps
replacing leaves at the existing nodes, so the rosette stays alive and bounded, and the
plant widens through rhizome/offset crowns instead of stacking.

**`RenewPetiole` means leaves are replaced, never lost.** `LeafMaximumAgeHours` does not
reduce the leaf count - it swaps the blade at the same node. So the live leaf count is
bounded *only* by the node count, and `CrownMaximumNodes` is the only thing that bounds
it at all. A two-year Geranium macrorrhizum carried 2,808 leaves before the cap.

**Cap both continuation paths.** `ContinueCrown` is reached from two places: the
vegetative continuation roll, and - when `ContinueCrownAfterFlowerCommitment` is set -
straight after a flower commitment. Guarding only the vegetative one changed nothing
measurable on Fragaria, because an old flower dying frees a slot under
`MaximumFlowersPerCrown`, the same axis commits again a month later, and grows a node
each time. All 106 of its nodes came through the floral path.

### 2. Bending has no restoring term, so long chains curl underground

`PlantSubFormation.Gravity` always rotates an organ *downward* from its unloaded
`baseOrientation`, and the parent's increment is baked into every descendant's
`baseOrientation`. Per organ that is bounded; along a chain it accumulates, and a long
enough chain always ends up pointing straight down. There is no gravitropic recovery
after construction - `RandomOrientation` runs once, at birth.

Measured on Heuchera at day 730: stem `dirY` median **-0.29**, and the **median leaf sat
4.2 cm below the soil surface** with the 10th percentile at -14.4 cm. Half the foliage
was buried. Geranium macrorrhizum reached -12.8 cm at the 10th percentile.

`LimitBendToSoilSurface` now shrinks a bend that would push an organ's tip below
`Plant.Position.Y` (eight bisection steps, and only for organs that would break the
surface). It is a floor, not a fix for the missing restoring term - bounding the chain
with `CrownMaximumNodes` is what actually keeps a crown upright.

### 3. Every shoot was steered upright, whatever the species wanted

`RandomOrientation` pulled any axis with `y < 0` back toward vertical, in proportion to
how far it had dipped and **regardless of `ShootsGravitaxis`**. A decumbent species could
not trail: Serbian bellflower climbed instead of sprawling.

`SpeciesSettings.ShootsGravitropicSetPointAngle` is the gravitropic set-point angle.
Zero is orthotropic and takes the historical code path unchanged, so nothing moves for
the other six species. Values near pi/2 are plagiotropic - a trailing or creeping stem
that holds itself near the horizontal.

`ShootsGravitaxis` is applied **once per node**, as a single slerp step toward the
target, so the set point is reached over tens of nodes, not immediately. Measured: from
vertical, at the default strength, an axis is still 40 degrees above the horizontal after
sixty steps. A species whose axis is only a dozen nodes long needs a strength near 1 -
and its `CrownPitch` should already start the axis near the set point.

### 3b. A dormant rhizome bud was deleted, so five of seven species never spread

`RetainDormantRhizomes` defaults to **false**, and a rhizome organ whose local energy
reserve reaches zero is then killed outright. Dormant buds run their reserve down every
winter, so they were deleted before they could ever break. Only Geranium macrorrhizum and
Geranium x cantabrigiense set the flag - and they were the only two species that built a
real clonal clump (100 and 84 shoot clusters at two years). The other five sat at 1 to 4
clusters with a rhizome reach of 0.1 to 15 cm.

Set it for every perennial with a persistent rhizome, caudex or crown. It is not a
tuning knob; a perennial rhizome does not die because its reserve is momentarily empty.

The counterpart: with the flag set, dormant buds now accumulate instead of being culled,
and `RhizomeAxillaryBudsPerSegment > 1` makes the bud count grow as
`2^RhizomeMaximumSegmentDepth`. Campanula at depth 8 with two buds per segment reached
**3,631 rhizome organs and 2,580 dormant buds against 362 leaves**. Keep the branching
factor at one unless the depth is small.

### 4. The seasonal phase machine could not re-arm, and stalled a plant for nine months

`GetFloweringPhase` re-armed `FloweringStarted` only inside the branch guarded by
`season < FloweringSeasonStart`. **A species with `FloweringSeasonStart == 0` can never
reach it** - season is never negative. Carex set exactly that, flowered once, and then
returned `ResetPending` for the rest of the run; with its `Dormant*` rates at zero the
plant froze completely. Measured: organ count pinned at 786 and leaf count at 294 from
day 91 to day 364. That is what "grows too late and looks empty" is.

Two changes, both in `PlantSubFormation`:

- The window re-arms once per **season year** (1 March to end of February), the same
  cycle `TryActivateCrownBud` uses, independent of `FloweringSeasonStart`.
- A plant whose window closed without it ever flowering returns **`PostFlower`**, not
  `ResetPending`. It was too young, not dormant; a perennial keeps growing until
  `SeasonalSenescence`.

Still keep `FloweringSeasonStart > 0`. Zero is not a meaningful "flowers from 1 March".

### 5. Flowers were committed in every phase, including late autumn

Flower creation is driven by the **per-phase commitment probabilities**, not by the
`Flowering` phase. Two consequences that produced the reported autumn and winter bloom:

- `PostFlower` runs from `FloweringSeasonEnd` all the way to `SeasonalSenescence`
  (default 7.8/11 = mid-November). At `PostFloweringCommitmentProbabilityPerHour =
  0.0002` a meristem still has a **36 % chance over that window**. Geranium macrorrhizum
  opened petals 20 Sep - 22 Nov and again 27 Dec - 7 Feb.
- `FloweringStartAgeHours` gates the **phase**, not the commitment, so `PreFlower`'s own
  rate flowered a first-year seedling that should not bloom until its second summer.

Both are now gated at the commitment site in `AboveGroundAgent.Herbaceous.cs`:
maturity (`Plant.GetAge_h() >= FloweringStartAgeHours`) and a lead-time test using
`FlowerSettings.CommitmentToBloomLeadHours` - scape growth plus bud swell plus petal
expansion. A commitment whose bloom would land past `SeasonalSenescence` is refused.

### 6. Scapes were shorter than the petioles that hold the leaves

Inflorescence height is `FlowerBaseSegmentCount * FlowerBaseSegmentLength` plus the
branch segments and the pedicel. Compare it against `PetioleLength * cos(LateralPitch)`
plus the blade **before** believing a preset. Measured against ground truth at day 730:

| species | scape was | foliage top | verdict |
| --- | --- | --- | --- |
| Heuchera micrantha | 6.4 cm | 20-27 cm | flowers buried; real scape 38-61 cm |
| Geranium macrorrhizum | 11.7 cm | 29-33 cm | flowers buried; must clear the mound |
| Geranium x cantabrigiense | 7.0 cm | 19-22 cm | level with the leaves |
| Bergenia cordifolia | 11.1 cm | 26-32 cm | no visible scape at all; real 40 cm |

A long scape also needs a radius that can carry it - 0.6 mm will not hold 35 cm upright,
and the petiole support limit truncates growth that its support cannot bear.

**`LateralPitch` is measured from the crown axis, not from the horizontal.** With a
vertical `CrownPitch`, Heuchera's 15 degrees put every petiole almost parallel to the
axis and the blades piled up on each other: mean leaf distance from the axis 10 cm
against a 25 cm maximum. A basal rosette needs 45-60 degrees. This is the direct cause
of a "ball-like crown with no spread".

### 7. `LeafPitch` and `LateralPitch` must be complementary, or the blade is edge-on

`LateralPitch` is the angle of the petiole **from the crown axis**; `LeafPitch` is the
angle of the blade **from the petiole**. A blade only ends up horizontal when
`LeafPitch ~ 90 degrees - LateralPitch`. Both Geraniums happened to satisfy it (45/45)
and were the only two species whose foliage read correctly. Heuchera ran 85/55 and
Fragaria 85/85, which points the lamina at the ground - Fragaria's median leaf sat below
the soil surface even after its crown was capped, purely from this.

### 8. Scape growth is sequential, so a long scape delays the bloom by months

`FlowerBaseSegmentCount` segments grow one after another, each taking
`FlowerStemSegmentGrowthDurationHours`. Bergenia's 25 segments at 72 h are **1,800 hours**
of scape before the bud even starts: a flower committed on 1 April opened in mid-August.
The species' flowering window governs **commitment**, not anthesis. Budget the lead time
(`FlowerSettings.CommitmentToBloomLeadHours`) at two to four weeks and set the segment
duration from the segment count, not the other way round.

### Inflorescence combinatorics are exponential - check them by hand

Branching in `FlowerHelper.chaning` is binary: each node creates a continuation meristem
**and** `FlowerBranchLateralsPerNode` laterals, so tips go as `2^FlowerBranchMaximumDepth`
per branch origin. `CreateFlowerBaseLaterals` adds another origin for every base segment
in the top `FlowerBaseLateralActiveTopFraction` of the scape, and each of those restarts
the count at depth zero. Bergenia at depth 6 over a 25-segment scape produced **1,414
floral organs per crown, 45,565 organs and 27,440 open petals on one plant**, 7,049 of
them hanging inverted. A real panicle holds 20-40 flowers. Multiply it out before
raising a depth.

## The flower model, and the six things it could not express

Everything about an inflorescence used to come from one set of numbers and one colour
curve, and several parts of it were hardcoded. Six consequences, all visible:

### 1. Petals were drawn with the species' LEAF mesh

A petal is a lamina and travels in the **same wire record as a leaf**
(`OrganTypes.FlowerPadel` shares the `Primitives.Rectangle` branch), and the viewport
resolved that record's geometry with `leafGeom(species)`. So a three-millimetre Heuchera
petal was a maple blade and a Bergenia petal was a heart.

`FlowerSettings.PetalMorphology` is a `LeafMorphology` of its own, served by
`GET /Simulation/mesh/petal/{species}` alongside the leaf endpoint, registered by the
frontend as `petal:${name}` and selected in `BuildScene` on the organ. `LeafDimensions`
carries `Curvature01`, so petal cupping and reflexing are a property of the outline -
which is right, because a petal's curve is its shape and not a response to load. Petals
are deliberately still excluded from the bending solver.

### 2. Petal pitch was one hardcoded constant for every species

`FlowerHelper` used `basePetalPitch = 0.95f` rad - 54 degrees, a shallow bowl, correct for
nothing in particular. `PetalPitchDegrees`, `PetalPitchVarianceDegrees`,
`PetalRollVarianceDegrees` and `PetalRimFraction` replace it. Near 90 degrees is rotate
(Fragaria, Geranium); 20-40 is campanulate (Heuchera, Bergenia, Campanula's lobes flare
to 86); past 90 the petals reflex.

### 3. There was no flower centre

The terminal bud does not vanish at anthesis - it *is* the receptacle the petals stand
on. Nothing retargeted it, so an open flower had an elongated bud at its middle in the
petals' own colour. `FlowerCentreLength` / `FlowerCentreRadius` retarget it to a disc when
the petals unfurl (via `AboveGroundAgent.RetargetSize`, which is also the only way to
*shrink* a herbaceous organ - they otherwise only grow), and `FlowerCentreColor` gives it
its own colour. This is what puts the yellow eye in a strawberry flower.

### 4. Every floral organ was handed the LEAF phenology on the wire

`ExportV7` wrote `LeafPhenology.WriteColor` for every cylinder organ, so the scape went
out leaf-green - and the viewport then ignored it and greyed the scape towards wood by
`woodRatio`, because only `Organs.Petiole` was allowed a backend colour.
`FlowerSettings.ScapeColor` (null = the old behaviour) now travels for
`FlowerStem`/`FlowerMeristem`/`FlowerPedicel`, `FlowerCentreColor` for `FlowerBud`, and
`PlantColors.backendColoredCylinders` is the set the frontend honours. Stem green is
`#009900`, so a scape that should match the stems uses `(0, 0.6, 0)`.

### 5. Every scape was forced straight up

Each base segment was built with `AdjustUpBase(orientation, up: true)`, which **discards
the current axis** and points the segment at the vertical, whatever the species. Right for
a Bergenia scape - a thick fleshy column that holds its panicle up - and wrong for a
Geranium peduncle, which ascends and then curves so its flowers finish a little above the
surrounding leaves. `FlowerSettings.ScapeGravitropicSetPointAngle` (0 = the old behaviour
exactly, because the set-point target *is* vertical) and `ScapeGravitropism` give the
scape the same gravitropic set point shoots have.

### 6. The scape and the flower shared one stiffness

`FlowerOrganElasticModulus` covered the axis, the bud and the petals, so no species could
have a rigid scape and a nodding flower, or the reverse.
`SpeciesSettings.FlowerScapeElasticModulus` (0 = fall back to the old value) separates the
axis. The seven presets now span 4.5e7 for an arching Geranium peduncle to 8e8 for a
trigonous sedge culm, with Bergenia at 2.5e8 as the deliberately rigid reference.

### Two traps in tuning a scape

- **`FlowerBaseSegmentCount` costs time, not just length.** Segments grow *sequentially*,
  one `FlowerStemSegmentGrowthDurationHours` each, **and** the growth is resource-limited,
  so the wall-clock is longer than the product. A twelve-segment Heuchera scape gained
  about 10 cm a fortnight and topped out near 27 cm inside its flowering window; eight
  longer segments reached the measured 47 cm. Prefer fewer, longer segments.
- **A mean over `FlowerStem` organs is not the scape's posture.** Branch and pedicel
  segments are `FlowerStem` too, and they *should* point outward and down, so the mean
  goes negative as soon as the panicle opens. It looks exactly like a collapsing scape and
  is not one. Measure the height the topmost `FlowerStem` reaches instead.

### What is still missing from the flower model

- **No anther or stamen organ.** For Heuchera the exserted orange anthers are the only
  saturated colour in the flower and the thing that makes a render read as Heuchera; the
  model can only approximate them with the centre colour. The same organ would carry
  Carex's cream filaments properly instead of borrowing the petal slot.
- **Carex borrows the petal slot for glumes and anthers.** A sedge has no perianth at all,
  so its 14 narrow pale "petals" per spike are a stand-in for the exserted anthers and
  scales. It renders correctly and is botanically a lie; an anther organ would fix it.
- **One petal morphology per species**, so sepals, petals and a differing inner whorl
  cannot coexist.
- **The radial-petal inversion count is meaningless.** `InvertedOrgans` counts any organ
  with `dirY < -0.8`, and half a radial petal ring legitimately points downward. Exclude
  `FlowerPadel` when reading that number.

## Species ground truth, and the metrics a probe should assert

Sources: MOBOT Plant Finder, RHS, naturadb.de, Gaissmayer, Walters Gardens, Flora of
North America. Site is Vienna, 48.208 N - use Central European bloom months, not the
St. Louis or UK ones where they differ (noted below).

| species | foliage H x W | inflorescence | bloom (48.2 N) | habit |
| --- | --- | --- | --- | --- |
| Heuchera micrantha 'Palace Purple' | 20-30 x 30-45 cm | scape 38-61 cm, **well above** the mound | Jun-Jul | clump of crowns, no runners |
| Bergenia cordifolia | 30 x 30-46 cm, blade 25 x 20 cm | stout scape to 40 cm, within or above foliage | Apr-May | slowly spreading rhizome clump |
| Campanula poscharskyana | 10-15 (to 30) x 30-46 cm | flowers in loose panicles **along** the stems | Jun-Sep (MOBOT says May-Jun for St. Louis) | prostrate, many trailing stems, short underground runners |
| Carex morrowii 'Variegata' | 30-40 x 30-50 cm | culms at or just above the foliage | Apr-May | evergreen tussock, tillers |
| Fragaria vesca 'Rugen' | 15-25 x 25-30 cm | stalk 3-15 cm, usually above the leaves | May-Oct, everbearing | sold as runnerless (see below); widens by **branch crowns**, seed lines segregate runners |
| Geranium macrorrhizum | 15-30 x 50-100 cm | cyme held above the foliage on upright stems | May-Jul | rhizomatous mat |
| Geranium x cantabrigiense | 15-25 x 20-40 cm | clusters above the foliage | May-Jul, sporadic to autumn | low mat, short runners |

**Fragaria 'Rugen' is runnerless in the trade descriptions, and the trade descriptions
disagree.** Wikipedia calls it "the first modern cultivar, i.e., runnerless, everbearing
and large fruited"; German nurseries say "auslaeuferlose, buschige Sorte" and "bildet
keine Auslaeufer"; Annie's Annuals says 'Improved Rugen' makes "very few, very short
runners"; naturadb calls the same plant "auslaeuferbildende, kriechende wintergruene
Staude". They can all be right, and the genetics say why: the runnerless phenotype is a
**single recessive 9 bp deletion in FveGA20ox4** (the R locus), confirmed by 3:1
segregation in the Ilaria F2 - and 'Rugen' is normally raised from **seed**, so any
open-pollinated line throws runnering offspring. Do not treat "runnerless" as a property
of the taxon. Make runnering a parameter with a small non-zero value for 'Rugen', and
expect a user's balcony plant to crawl.

Even a genuinely runnerless plant is not a ball. Hytonen et al. on the *r* mutant: "the
fate of an AXM that does not produce stolon is to remain dormant or to generate a BC
terminated by an inflorescence" - the axillary meristems that would have been runners
become **branch crowns**, giving a bushier plant with more flowers. That is how it
reaches a 25-30 cm clump, and the rhizome segments in the preset stand for those branch
crowns - 2 cm, beside the mother crown.

### The three fates of an axillary meristem, and the one the model lacks

Strawberry architecture is a fate decision taken at every leaf axil. An AXM becomes a
**stolon**, a **branch crown**, or it **stays dormant**, and the switch is GA-mediated
and environmental: long days and warmth give stolons, short days and cool give branch
crowns. The herbaceous path already has the second and third - `TryActivateCrownBud` and
the dormant `Bud` on a rhizome. It has no stolon, and the rhizome cannot stand in for
one, because a runner is not a shortened rhizome but a different topology:

- a stolon unit is **two long internodes with the daughter plant at the second node**;
  node one carries a bract and a bud that goes straight to dormancy;
- the daughter forms **adventitious roots** from the base of its own compressed crown;
- **"upon completion of the rooting process, the lateral bud on the second node begins to
  elongate to form a new stolon"** - the chain is sympodial and restarts at the daughter,
  so its reach is a function of *time*, not of a depth budget;
- the connector is aerial, filiform, **crimson**, arches above the litter rather than
  lying in it, and abscises, leaving independent ramets.

**Measured: no value of `RhizomeMaximumSegmentDepth` gives both reach and a sane ramet
count.** Reach is `depth x RhizomeSegmentLength`, and `SegmentDepth` is inherited
monotonically and retires the apex at the cap
(`AboveGroundAgent.Herbaceous.cs`, `FormRhizomeContinuationAndAxillaryBuds`), while the
bud count grows as `(1 + RhizomeAxillaryBudsPerSegment)^depth`. Both come off the same
counter. Fragaria at depth 3, seed 42, 4 h ticks:

| preset | rhizome reach | crowns | leaves | 2 x leafRadial / height |
| --- | --- | --- | --- | --- |
| shipped (depth 3 x 1.5 cm), day 56 to day 730 | **5.2 cm, frozen** | 6 | 124 | 3.2 |
| depth 12 x 9 cm, year 1 | 31 cm | 1 | 24 | 2.8 |
| depth 12 x 9 cm, year 2 | 61 cm | 10 | 204 | 6.2 |
| depth 12 x 9 cm, year 3 | 92 cm | **76** | **1524** | 9.5 |
| depth 12 x 9 cm, year 4 | 103 cm, at the cap | **265** | **5304** | 12.3 |
| depth 12 x 9 cm, no axillary buds | 43 cm, frozen at day 546 | 5 | 104 | 4.2 |

The spread *rate* of the tuned variant is right, 31 cm in each of the first three years.
The ramet count goes 1, 10, 76, 265 and the leaf count 24, 204, 1524, 5304, because a
real patch adds ramets linearly along an advancing front while a depth-bounded tree adds
them geometrically. Year four is where the curves separate: reach adds only 11 cm as it
converges on its `12 x 9 cm` ceiling, while the count still multiplies by 3.5 - the plant
stops spreading and keeps thickening. The shipped preset is the other failure: nine of its
fifteen crown attachment points never activate, because the network is complete at day 56
and nothing can extend it. Target is 10-15 cm tall by 30-40 cm per plant (patch to 90 cm),
aspect 6 to 8.

One more gap sits underneath this: **there are no adventitious roots anywhere in the
model**. Roots are born only at the seed (`SeedAgent.cs`), so a mat of any width draws
water through one root crown over a single soil column. `UGBirth` already exists
(`PlantFormation.cs`) if a ramet should ever root.

## Morphology probe

The plant-behavior probe above reads organ counts; habit needs geometry. Build a console
probe under `/tmp` referencing `Agro/Agro.csproj` and measure, per snapshot:

- leaf **radial distance from the seed** and leaf **height**, as percentiles - a mean far
  below the maximum means the foliage is bunched on the axis rather than spread;
- `MaxHeight / LeafMaxRadial` - a rosette should be wider than tall (< 1), a column is > 1;
- flower organ height **minus** the 90th percentile leaf height - negative means the
  flowers are inside the crown;
- count of crown axes (stems whose parent is a rhizome) and of branch points;
- longest crown axis in nodes, and total stem length;
- organs with `dirY < -0.8`, and any organ tip below `Plant.Position.Y`;
- the calendar dates on which `PetalCount` is non-zero.

Dump one CSV row per organ at a few fixed days as well; several of these defects were
only obvious from the parent/depth/direction columns.

Use **render mode 3**, not 0: the frontend defaults to render mode 1, which falls back to
3, so mode 3 is what a user actually sees. A 730-day single-plant run costs 3-10 minutes
of wall clock for most species and about 90 minutes for a flowering Bergenia.

### Regression scenarios

Add these to the table above. All at seed 42, 4 h ticks, `InitialDate` 2026-03-01,
render mode 3, one plant in a 1x1 m field.

| Scenario | Purpose | Expected signal |
| --- | --- | --- |
| Every species, 730 days | Crown axis bound | Longest crown axis <= `CrownMaximumNodes`; total stem length per axis under 40 cm |
| Every species, 730 days | Soil floor | No organ tip below `Plant.Position.Y`; 10th-percentile leaf height >= 0 |
| Every species, 730 days | Out-of-season bloom | `PetalCount == 0` on every snapshot outside the species' documented bloom months |
| Every species, 730 days | Habit aspect | `MaxHeight / LeafMaxRadial` inside the ground-truth band for that species |
| Heuchera, Bergenia, both Geraniums, 730 days | Scape clears the foliage | `FlowerMaxHeight - LeafP90Height > 0` at peak bloom |
| Carex, 365 days | No dormancy stall | Organ and leaf counts strictly increase from day 91 to day 273 |
| Carex, 730 days | Annual re-arm | A `Flowering` phase occurs in **both** season years |
| Campanula, 730 days | Trailing, not climbing | `MaxHeight < 0.35 m`; more than 8 crown axes; more than 5 branch points |
| Fragaria, 730 days | Rosette, not column | `MaxHeight / LeafMaxRadial < 1.3`; more than 3 crown axes by year two |
| Bergenia, 730 days | Inflorescence size | Fewer than 400 floral organs per crown; fewer than 100 inverted organs |
| Any species, 1 h vs 4 h ticks, 100 days | Timestep sensitivity | See the note below - treat this as a drift check, not an equality check |

**The model is not timestep invariant, and it was not before these changes either.**
Measured over 100 days at seed 42, render mode 3:

| metric | Carex 1 h vs 4 h | Geranium macrorrhizum 1 h vs 4 h |
| --- | --- | --- |
| organs | 445 vs 582 (24 %) | 863 vs 827 (4 %) |
| leaves | 150 vs 194 (23 %) | 116 vs 104 (10 %) |
| max height | 0.343 vs 0.311 m (9 %) | 0.400 vs 0.418 m (4 %) |
| shoot clusters | 19 vs 27 | 7 vs 5 |
| inverted organs | 0 vs 0 | 19 vs 15 |

Growth is a chain of per-tick Bernoulli events, and `NextFloatAccum` normalises the
rate but not the variance or the ordering, so branch counts diverge. Use the comparison
to catch a change that makes the divergence *worse*, and never to assert equality.

## Where the seven species stand now

Measured at day 730, seed 42, 4 h ticks, render mode 3, one plant in a 1x1 m field.
Lengths in centimetres. "Tallest flower" is the highest floral organ reached during the
second season's bloom; "above foliage" is that height minus the 90th percentile of leaf
tip height.

| species | height | target | width | target | crowns | leaves | tallest flower | above foliage | bloom (year 2) | target |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Heuchera micrantha | 30.6 | 20-30 | 52.4 | 30-45 | 9 | 110 | 47.8 | +33.7 | 06-20-07-18 | Jun-Jul |
| Bergenia cordifolia | 27.2 | 25-40 | 40.2 | 30-46 | 6 | 42 | 42.6 | +32.1 | 04-11-05-09 | Apr-May |
| Campanula poscharskyana | 19.7 | 10-30 | 56.8 | 30-46 | 61 | 1588 | 14.3 | +7.7 | 07-04-09-12 | Jun-Sep |
| Carex morrowii 'Variegata' | 39.3 | 30-40 | 43.0 | 30-50 | 52 | 418 | 46.0 | +27.8 | 04-25-05-23 | Apr-May |
| Fragaria vesca 'Rugen' | 11.5 | 15-25 | 31.7 | 25-30 | 6 | 124 | 14.1 | +4.8 | 06-06-06-20 | May-Oct |
| Geranium macrorrhizum | 31.7 | 15-30 | 109.4 | 50-100 | 127 | 1270 | 41.0 | +10.2 | 05-09-08-01 | May-Jul |
| Geranium x cantabrigiense | 20.9 | 15-25 | 61.3 | 20-50 | 64 | 636 | 26.0 | +8.3 | 06-06-08-29 | May-Jul |

Scape posture, which is what separates a Geranium peduncle from a Bergenia one:
`FlowerScapeElasticModulus` runs 4.5e7 (Geranium, arching) - 8e7 (Campanula) - 1.5e8
(Heuchera and Carex, wiry or trigonous but self-supporting) - 2.5e8 (Bergenia, rigid),
and only the Geraniums set `ScapeGravitropicSetPointAngle` (20 degrees, approached at
0.35 per segment).

## Remaining calibration gaps

Honest list of what still does not match:

- **Three species spread wider than the books say**: Campanula 90 cm and
  G. x cantabrigiense 62 cm against 30-46 and 20-50; G. macrorrhizum 112 cm against
  50-100. Width is leaf reach plus rhizome reach, so the levers are
  `RhizomeMaximumSegmentDepth` and `RhizomeSegmentLength`, not the crown - but those two
  also set the ramet count, and the coupling is exponential. See "The three fates of an
  axillary meristem" above before reaching for them.
- **No species can crawl.** Nothing in the model expresses a stolon: an aerial, arching,
  leafless, two-internode connector that plants a rooted daughter at its tip and then
  restarts from that daughter. The rhizome is a depth-bounded tree from the mother, so
  its reach is fixed at compile time while a runner's reach is a function of time. This
  blocks Fragaria vesca proper, F. x ananassa, and the whole stoloniferous ground-cover
  set (Ajuga reptans, Glechoma, Duchesnea, Potentilla reptans, Trifolium repens).
- **Everbearing Fragaria still flowers once, in June, not May to October.** The cause is
  structural: a crown that flowers is spent, and with `CrownMaximumNodes` binding it
  cannot hand over, so the plant waits for a new rhizome crown. Raising the node cap buys
  more flushes but also widens the plant - Campanula was fixed that way and grew 10 cm
  wider for it. The real fix is basal node abscission, below.
- **Density and width are coupled**, because both come from stem length. Campanula's mat
  had to be built from many short stems rather than a few long ones to get 2.6x the leaf
  density at the same width; the same trade-off will appear for any mat-former.
- **No leaf and no node ever dies.** `RenewPetiole` replaces the blade at the same node,
  so foliage is bounded only by the node count, and `CrownMaximumNodes` is a hard
  ceiling rather than a turnover. A real rosette sheds its oldest leaves and its oldest
  basal nodes each season, which is what lets it flower repeatedly without growing.
  Implementing that is the single most valuable thing left here.
- **Clonal proliferation is still fast**: Carex reaches 65 tillers and G. macrorrhizum
  114 crowns in the first year. Sizes stay right because the crowns are capped, but the
  density is high. The feedback loop runs through `MakeBud`: every shaded-out petiole
  leaves a bud that can raise a new crown.
- **`FlowerAboveFoliage` is misleading for Carex**, whose arching leaves put their tips
  low; compare culm height against `MaxHeight`, not against the leaf-tip percentile.

## Troubleshooting

- If a tool or build fails with `No space left on device`, inspect space before
  changing source. Do not remove user archives or exports without explicit
  approval; use a temporary probe location under `/tmp`.
- In static helpers, use the object path actually in scope (for example,
  `formation.Plant.World`, not a nonexistent `plant.World`) and rebuild
  immediately after the correction.

---

# ThreeFrontend agent guide

The web viewport in `ThreeFrontend/`. Preact 10 + `@preact/signals`, built by
preact-cli 3 (webpack 4), three.js r152. There is no Vite and
`preact.config.js` is entirely commented out, so **there are no custom
loaders**.

## Architecture: the renderer-neutral core

The viewport was refactored so a second backend (WebGPU) can exist beside the
Three.js one without duplicating logic. The layering is load-bearing:

```
src/render/
  IViewportRenderer.ts   the contract + the `activeRenderer` registry
  RendererHost.tsx       mounts a backend; owns EVERY signal subscription
  core/                  colour, geometry, matrices, rays, draw list, picking
  three/                 the WebGL backend, a thin adapter over core/
  offline/               offscreen stills and animation export
```

- **`src/render/core/**` and `src/render/webgpu/**` must never import three.**
  Enforced by an eslint `no-restricted-imports` override in `package.json`.
  The build transpiles with babel and never type-checks, so lint is the only
  automated guard.
- `appstate` owns simulation data only. It holds no `THREE.Object3D`, no
  `TransformControls`, no exporter. The renderer reaches into appstate, never
  the reverse.
- Renderers receive frame data as **arguments** (`PlantFrameInputs` and
  friends), not by reading signals themselves. This is not stylistic — see the
  minifier trap below.

## Commands

```bash
cd ThreeFrontend
npm run dev            # preact watch, :8080
npm run typecheck      # tsc --noEmit  (the build NEVER type-checks; run this)
npm test               # jest
NODE_OPTIONS=--openssl-legacy-provider npx preact build --no-prerender
```

The backend is a separate .NET service:

```bash
dotnet build AgroServer/AgroServer.csproj -c Release -f net8.0
cd AgroServer && AGRO_HOSTNAME=http://127.0.0.1:8099 dotnet run -c Release -f net8.0 --no-build
```

`AGRO_HOSTNAME` accepts **one** origin (`Program.cs` passes it straight to
`WithOrigins`). Serving two builds on two ports for comparison will fail CORS
on one of them; serve them on the same port in turn instead.

## Visual regression harness — `ThreeFrontend/tools/visual/`

Drives headless Chromium over the DevTools Protocol. **No dependencies**: Node
22 supplies `fetch` and `WebSocket`, and PNGs are decoded inside the browser,
so there is no puppeteer and no image library.

```bash
node loadsession.mjs URL ./fixture-session.json out.png   # MAPPING=water to switch ramp
node diff.mjs baseline.png candidate.png diff.png         # magenta = differing pixels
node scan.mjs shot.png                                    # colour census + content bbox
node crop.mjs shot.png crop.png X Y W H SCALE             # nearest-neighbour zoom
W=1280 H=800 SSAA=2 node still.mjs URL ./fixture.json still.png
FORMAT=webm TIMING=simTime node animate.mjs URL ./fixture.json out.webm
node playcheck.mjs out.webm video/webm                    # really decodes it
```

**Use it.** Every serious bug in this refactor was invisible to `tsc`, eslint
and 100+ unit tests, and appeared only in a real browser.

Fixture note: `fixture-session.json` is a saved session (163 plant primitives,
terrain, one seed). Loading it feeds two builds byte-identical scene data.
**Live simulation cannot be compared** — the backend carries state between
runs, so two identical requests produce different plants (188 vs 167 organs
was observed for the same request).

To compare against pre-refactor code, use a git worktree:
`git worktree add --detach /tmp/base HEAD`, symlink `node_modules`, and copy
the untracked `Species.gen.ts` / `Species.gen.tsx` in — they are required to
build but are not tracked.

## Traps that cost real time

### The build silently deletes signal subscriptions

```js
useSignalEffect(() => {
    appstate.scene.value;              // WRONG: an unused expression
    rendererRef.current?.rebuildPlants();
});
```

Terser removes the bare property read. On the effect's first run the renderer
is not mounted, so the call is a no-op — and after minification the effect
tracks **nothing** and never fires again. Works in dev, dead in production.
Reads must be consumed (assigned, returned, or passed as an argument).
`no-unused-expressions` is enforced repo-wide to prevent recurrence.

### JSX factory shadowing

```jsx
presets.map(([label, w, h]) => <option/>)   // `h` shadows preact's h()
```

Every element in that callback calls a number. Surfaces only as
`TypeError: n is not a function` from a minified bundle. Resolve minified
stacks with the shipped source maps:

```js
new (require('source-map').SourceMapConsumer)(map).originalPositionFor({line:1, column: N})
```

### Type-only imports close runtime cycles

babel cannot tell a type from a value, so `import { SomeType }` is emitted as
a real runtime edge. That closed a cycle
(`appstate → IViewportRenderer → core/Picking → helpers/Plant → appstate`) and
crashed at module init in the bundle. Use `import type` for type-only symbols
in `src/render/**`.

### preact maps `onChange` to the `input` event

Dispatching only `change` from a test harness updates the DOM value but never
reaches the handler, so the signal keeps its old value — silently. Fire both.

## three.js r152 parity facts (verified against `node_modules/three/src`)

Needed to reproduce the viewport exactly in WGSL:

- `useLegacyLights` multiplies light intensity by `π` (`WebGLLights.js:227`);
  `BRDF_Lambert` multiplies by `1/π` (`common.glsl.js:121`). **They cancel.**
- With no env map the specular term is provably zero, so
  `MeshLambertMaterial` and `MeshStandardMaterial{metalness:0}` are identical
  here. One shading function covers the whole app:
  `color * mix(ground, sky, 0.5*dot(N,L)+0.5) + emissive`.
- `LinearTosRGB` uses the **truncated** exponent `0.41666`, not `1/2.4`. The
  sRGB round-trip is therefore inexact by ~4e-6 *by design*; do not "fix" it.
- `Color.getHSL`/`setHSL` default to the working space, which is linear — so
  `lerpHSL` interpolates HSL of **linear** values, and hue is lerped plainly,
  not along the shortest arc.
- `LinearToneMapping` at exposure 1 is the identity in r152. Omit it.
- The clear colour is the raw sRGB byte value, not a linear one.

## WebGPU backend (`src/render/webgpu/`)

Hand-written WGSL, no three. `auto` tries WebGPU first and falls back to WebGL
if the mount fails; an explicit choice via the "Viewport GPU API" control or
`?gpu=1` / `?gpu=0` is honoured on every tab. On `auto` only, editing tabs
(Plants / Obstacles / Terrain) switch to WebGL for its axis gizmo — WebGPU can
still drag, by projecting the cursor onto the object's own height.

**Do not add a capability probe.** One existed and had to be removed: asking for
an adapter merely to throw it away is a second code path that can disagree with
the real one — an early `requestAdapter` returns null simply because the GPU
process is still starting — and it made `auto` pick WebGL on machines where
WebGPU works. Attempting the real mount *is* the probe; `RendererHost` tries
each backend in turn and only falls back on a genuine failure. For the same
reason, never leave the viewport unmounted because the first choice failed:
a browser can have WebGPU but blocklisted WebGL, or the reverse.

Conventions that differ from WebGL and bite silently — all three were real bugs:

- **Clip space is `z ∈ [0,1]`**, not `[-1,1]`. Reusing three's projection clips
  half the scene. Use `core/Mat4.perspectiveZeroToOne`, unit-tested against
  three's convention.
- **The framebuffer origin is top-left**, so `dpdy` has the opposite sign to
  GLSL. A flat normal computed as `cross(dpdx, dpdy)` points backwards, which
  shows up as flat-shaded surfaces lit by the ground colour instead of the sky
  (terrain came out at 66,45,5 instead of 149,96,0). Use
  `cross(dpdy, dpdx)`. The same mirroring flips apparent triangle winding, so
  check `frontFace` if geometry disappears under back-face culling.
- **The clear colour is written verbatim** to a non-sRGB surface, so it must be
  the sRGB byte value, not the linear one — otherwise the background is a few
  units dark (243 where WebGL shows 250).
- `requestAdapter` can return null simply because the GPU process is still
  starting. `Device.ts` retries; a single attempt is not reliable.
- **On Linux Chromium, enabling WebGPU disables WebGL.** The two are mutually
  exclusive there, so "fall back to WebGL" is not a safety net on such a
  machine - it is a second failure. `RendererHost` records backends that failed
  to mount and stops retrying them, which is what keeps a tab change from
  tearing the viewport down and rebuilding it every time.
- Camera input is hand-written, so its directions are not checked by anything
  the compiler sees. Conventions, all matching OrbitControls: `deltaY > 0`
  (scroll down) dollies OUT; dragging right decreases the azimuth; dragging
  down raises the camera. Left orbits, middle dollies, right pans - and
  `contextmenu` must be suppressed or right-drag opens the browser menu
  instead. See `tests/orbitCamera.test.ts`.
- WebGPU reports validation failures asynchronously and otherwise **silently** —
  a bad draw just does not appear. `Device.ts` installs an `uncapturederror`
  listener; keep it.
- **A storage buffer's binding size must be a multiple of 4**, and a growth
  factor does not preserve that on its own: `floor(4860 * 1.5) = 7290`, which
  Dawn rejects. Align every buffer capacity to the struct size. This only bites
  once a scene outgrows its first allocation, so it presents as a crash "after
  a few simulated months" rather than an obvious sizing bug — see
  `tests/webgpuBuffers.test.ts`.

**Canvas presentation cannot be verified in headless SwiftShader.** Devices,
buffers, draws and texture readback all work, but the canvas never composites,
so a blank viewport there means nothing. Verify the renderer through
`renderToPixels` (see `tools/visual/still.mjs`), which bypasses the canvas
entirely. Measured parity that way: WebGPU vs WebGL offscreen stills differ in
**665 of 1,024,000 pixels (0.065%)** at the time, all single-pixel edges — rasterisation
fill rules and depth encoding, the irreducible part.

## Offscreen rendering

- `renderer.outputColorSpace` applies **only to the canvas**. A render target
  needs `rt.texture.colorSpace = THREE.SRGBColorSpace` explicitly, or offline
  frames come out darker than the viewport. This is the first thing to check
  when an exported image does not match.
- `PixelReadback` is a normative contract (RGBA8, top-left rows, straight
  alpha, already sRGB-encoded, box-averaged in encoded sRGB). WebGL
  `readPixels` is bottom-up and must be flipped; WebGPU is not. Keep that
  asymmetry inside each backend.
- Unbind the target (`setRenderTarget(null)`) **before** reading, to force the
  MSAA resolve blit.
- VRAM is `w·ssaa · h·ssaa · 7 bytes · msaa`. 4K at ssaa 2 is ~230 MB; the
  figure is shown in the UI because exceeding it loses the GL context.

## Video export

WebCodecs emits raw chunks with **no container**. `encode/WebMWriter.ts` is a
hand-rolled single-track EBML muxer (no dependency, per project constraint).

- Write a real `Duration`, or players report an infinite duration and cannot
  scrub. It is the last child of `Info` so it can be patched in place at
  finalisation.
- A cluster must start on a keyframe, and `SimpleBlock` stores a **signed
  16-bit** timestamp relative to its cluster, so clusters cannot span ~32 s.
- `frame.close()` after every `encode()` — a `VideoFrame` holds a GPU buffer
  that is not garbage collected.
- Gate on `encodeQueueSize`; without backpressure a 4K export OOMs.
- `pickCodec` returning `null` is a first-class outcome (Safari has no VP9
  encoder). The PNG/JPEG-in-ZIP path is the always-available fallback.

## Simulated time and the sun

`src/helpers/Sun.ts` mirrors the server's clock so a shadow falls where the
simulation thinks the sun is. Latitude 48.208333 and longitude 16.3725 are
compile-time constants in `Agro/AgroWorld.cs`, mirrored as constants here.

Two traps, both encoded in `tests/sun.test.ts`:

- **`HoursPerTick` is snapped by the server**, and the request's value is not
  what runs: `5 -> 4`, `7 -> 6`, `9|10 -> 8`, `11 -> 12`, `13..18 -> 12`,
  `19..23 -> 24`, and `>= 24` rounds down to whole days. Advancing the clock by
  the *requested* value drifts the sun by up to an hour every tick, which reads
  as a bug in the solar maths rather than a units mismatch. The HUD shows the
  effective value when it differs.
- **`InitialTime` is a LOCAL wall-clock midnight**, converted to UTC through the
  site's zone (`AgroWorld.GetTime`). The frontend historically never sent
  `InitialDate` at all, so every simulation silently ran from 2026-02-01 - a
  midday sun only ~23 degrees above the horizon at this latitude. It is sent
  now, and the control reports the midday altitude so the consequence is visible.

## The daylight switch, and why the effects were invisible

The historical hemisphere runs at intensity 3.175 with a white sky. A lit surface therefore
gets about 3.2 from the ambient and 1.6 from the sun: **the ambient is the scene**. Anything
that modulates it - occlusion, indirect light - is adjusting a term that already dominates,
and the change does not survive sRGB quantisation. That is not a bug in those effects; it is
what the light model was.

`daylight` moves the energy where it physically belongs: sky at 0.85 (`DAY_SKY`), sun at 4.0.
A lit surface keeps roughly its exposure (~4.8 against ~4.775) while a shadowed one drops from
4.775 to 0.8. That ratio is the point - a shadow reads as a shadow, and occlusion and bounce
have something to act on.

- The hemisphere is now carried in `FrameLighting` per frame, not read from constants, so the
  switch can change it. With daylight off it is exactly the historical value, which is what
  keeps the default byte-identical.
- three's `HemisphereLight` intensity is pinned at 1 and the colours carry the energy, exactly
  as the sun already did, so both backends read their hemisphere from the same numbers.
- A non-Natural visual mapping falls back to the exact historical ambient, not merely "sun
  off": the data ramps are calibrated against it.

Measured: daylight changes 25.1 % of pixels with maxDelta 146, and the two backends still
agree to 0.207 %.

## Viewport lighting: independent switches

`LightingOptions` is five independent switches - shadows, occlusion, indirect, second bounce,
leaf translucency - not a ladder. The ladder was a convenient way to build the features up, but the terms are
genuinely independent: occlusion and indirect light attenuate the AMBIENT, so both are
meaningful with no sun at all. Requiring shadows first described the implementation order, not
the lighting. `lightingFromLegacyTier` migrates sessions saved under the old numbering.

The harness still speaks in tier numbers; `lightingScript(tier, daylight, translucency)` in
`cdp.mjs` translates one to the checkboxes, so every recorded measurement stays comparable.
Translucency is NOT part of the ladder and defaults off there for exactly that reason;
`still.mjs` drives it with `TRANSLUCENCY=1`.

## Viewport lighting tiers

`src/render/core/Lighting.ts` resolves a frame's lighting for both backends.

- **Tier 0 (default) must stay byte-identical** to the historical WebGL look. It
  is kept that way by construction rather than by a separate code path: the sun
  colour is exactly black and `params.x` is 0, so every added term multiplies
  out. A new lighting feature should vanish the same way.
- **A non-Natural visual mapping forces Tier 0**, enforced in `resolveLighting`
  rather than in the UI. Those mappings paint quantitative values into
  `emissive`; lighting them would make one number read as different colours
  depending on which way a leaf faces, quietly corrupting a readout.
- The sun's position comes from the simulated clock, so a shadow shows where the
  simulation thinks the sun is. **Drive it with `appstate.currentTimestep()`,
  never `playPointer`** - the pointer indexes `history`, previews can be skipped,
  and using it puts the sun at the wrong time of day.
- The shadow frustum is fitted to the FIELD, not the plants. Fitting it to the
  geometry would resize it as they grow and the shadow map would visibly swim
  between timesteps.
- **A WebGPU pass cannot sample a texture it renders into.** The pass that fills
  the shadow map uses `frameLayoutBasic`, which omits the shadow bindings
  entirely; binding them anyway makes Dawn reject the whole command buffer.
- Leaves are single double-sided quads, so shadow casting needs `cullMode: none`
  (WebGPU) and `shadowSide = DoubleSide` (three), or half the canopy casts
  nothing.
- **Occlusion stays on at night, the sun does not.** AO attenuates the
  hemisphere term, which is the only light left after dark; switching it off
  with the sun would make surfaces jump brighter as the sun set.

## GTAO (Tier 2, WebGPU only)

Half-resolution horizon-search AO over a depth+normal prepass, then a
depth-aware blur, then a bilinear upsample in the shading pass. Four things
about it are load-bearing and none are obvious:

- **The gbuffer must be `rgba32float`, not `rgba16float`.** It stores linear
  view depth in metres, and the horizon search reconstructs a view position per
  sample. A 10-bit mantissa puts the depth ulp at ~16 mm at 20 m, which is
  coarser than the search resolves - and because the search takes a `max` over
  ~48 samples, that quantisation noise is *rectified* into a systematically
  positive false horizon. It rendered as hard horizontal banding across flat
  terrain, which reads like a shader bug rather than a precision one. 32-bit
  float is renderable in core WebGPU (non-blendable, non-filterable); the pass
  only ever `textureLoad`s it, so neither restriction applies.
- **`r8unorm` is not a storage-texture format** in core WebGPU. Of the formats
  that are storage-capable, `rgba8unorm` is the only one that is also
  filterable, and filtering is what makes the half-res upsample cheap. Three of
  its four channels go unused.
- **The rotation pattern is a 4x4 tile and the blur is a 4x4 box**, deliberately
  matched: the blur then averages all 16 rotations exactly, turning 8 directions
  into an effective 128. Rotate over one inter-direction gap (`2PI/DIRECTIONS`),
  not a full turn, or the 16 rotations just repeat the same eight azimuths.
- **`renderToPixels` must build its own occlusion set.** Occlusion is a function
  of resolution *and* camera pose, so the canvas-sized textures are wrong for an
  offline still at any other size - and worse, they are *silently* right in the
  one case you would test first, where the still happens to match the canvas
  exactly. `OcclusionTargets` exists to be allocated per view. The same applies
  to `writeFrameUniform`'s `projParams.zw`, which the shading pass divides
  `gl_FragCoord` by; passing the canvas size while rendering 4K misplaces every
  sample. `renderShadowMap` also has to run there, and it writes the *light's*
  view into the frame uniform - so the camera's has to be written after it.

## The voxel density field (Tier 2 GI groundwork)

`src/render/core/VoxelGrid.ts` holds the grid maths and a CPU reference splat; the GPU passes
live in `src/render/webgpu/gi/`. The field stores **leaf area**, resolved to
`LAD = area / cellVolume` and `sigma = 0.5 * LAD`, not opacity - opacity is not scale
invariant, so a sub-voxel leaf either binarises to fully opaque or vanishes. Area also makes
mip generation a plain arithmetic mean and makes double-sidedness free.

- **A point splat needs roughly one sample per cell the triangle crosses.** The terrain is a
  100 m2 quad over ~8 cm cells - about 15000 of them - and at 64 samples per triangle it
  deposited its entire area into **128 isolated dense blobs with nothing between them**: a
  ground plane full of holes. The field was *numerically correct* throughout (total area
  matched the reference to 6 decimal places), so no numeric check could have caught it. Only
  the debug overlay showed it. That is the reason the overlay exists.
- Samples are batched across the dispatch's z dimension rather than looped in one invocation.
  Statics get 256 batches, plants 1, because an organ is smaller than a cell and plants are
  re-injected every timestep while terrain is not.
- **The grid is 4 % larger than the field** (`GRID_MARGIN`). Without it the field edge sits
  exactly on the grid edge, a sample at `x = fieldSizeX` floors to cell `dim` and is dropped,
  and *which* samples get dropped then depends on f32 versus f64 rounding - which is what made
  the GPU and the CPU reference disagree by 0.5 % of total area.
- Fixed-point atomics **round, not truncate**. Truncation is one-sided, and tens of thousands
  of deposits per frame turn that bias into a measurable shortfall.
- Verify with `tools/visual/voxelcheck.mjs`, which diffs the GPU field against the CPU
  reference cell by cell. Current agreement: 18333 occupied cells identical on both,
  3e-5 relative error on total area.

## Voxel cone tracing (Tier 3, WebGPU only)

Five cones per pixel at half resolution, off the same depth+normal prepass GTAO uses, lit from
a density field whose cells carry radiance rather than just extinction.

- **The composite attenuates, it does not replace.** `irradiance = hemi(n) * visibility +
  bounce`, where an empty grid gives visibility 1 and bounce 0. That is what makes the
  invariant EXACT: with nothing injected, Tier 3 is byte-identical to Tier 2 - verified at
  0 differing pixels of 256,000, identical PNG bytes. An estimator that replaced the ambient
  with its own hemisphere integral could only ever converge approximately.
- **Cone visibility and GTAO are combined with `min`, not multiplication.** They measure the
  same occlusion at different scales, so multiplying counts a contact shadow twice.
- **Terrain is excluded from the field** (`IFlags.ExcludeFromGI`). A leaf-area density is the
  right model for foliage and the wrong one for a large flat surface: injected, the ground
  becomes a slab that smears vertically at coarse mips, and every cone from a point ON that
  ground samples it. Open field darkened by ~24 %, uniformly, which is pure artifact. Properly
  solved with 6-directional anisotropic voxels at 6x the memory. The cost of excluding it is
  that the ground no longer bounces onto leaf undersides - deferred, not denied.
- **Side cones are tilted 50 degrees, not the textbook 60.** With a 30-degree half-angle a
  60-degree cone has its lower edge exactly horizontal, so on any flat surface it grazes
  forever.
- **Cones start one cell along the normal.** A surface injects area into the cells around it,
  so tracing from the exact surface point makes everything self-occlude. It was two cells while
  terrain was still injected; a whole cell is a large fraction of a plant only a few cells
  tall, and the extra clearance threw away most of the near-field shading.
- **A point splat needs several samples per cell, not one.** The sample budget was capped so
  low that whether a cell got 0, 1 or 2 samples was down to where the stratified sequence
  landed - density mottled 100 % cell to cell, and a grazing cone integrating along a surface
  turned that into visible bands. `MAX_SAMPLE_BATCHES` exists to bound a pathological request,
  not to truncate the ordinary one.

## WebGL abandoned shadow maps too, for the same reason WebGPU did

Two complaints drove this: occlusion did nothing at all on the WebGL backend, and the shadow map
projected a leaf's shadow onto the leaf casting it, at any bias.

**Occlusion did nothing because it was never implemented.** `ThreeRenderer.setLighting` read
`lighting.shadows` and never looked at `lighting.ambientOcclusion`. The checkbox was live, the
signal propagated, and the renderer ignored it.

**The shadow map could not be fixed by tuning.** A leaf is a zero-thickness double-sided quad.
Rendered into the shadow map it records its OWN depth, so the comparison that follows is a tie,
and which side of that tie a fragment lands on comes down to rasterisation and interpolation
rounding - which is exactly what the torn, triangle-shaped self-shadowing looked like. Depth
bias cannot settle it: the offset required is proportional to the depth slope across a texel,
which grows without bound as the leaf turns edge-on to the sun. `shadowSide` does not help
either, because for zero thickness the front and back faces are at the same depth. That is what
"bias does not help" means, and it is a property of the geometry, not of the settings.

So the WebGL backend now does what the WebGPU one does: **integrate extinction through a
leaf-area-density field and apply Beer-Lambert**, instead of comparing depths.

- `core/VoxelGrid` is renderer-neutral, so `three/ThreeDensityField.ts` builds the same field on
  the CPU that the GPU path builds in compute - same grid fitting, same `scatterTriangleArea`,
  same `extinctionFromArea`. The two backends cannot disagree about what is dense.
- It uploads as a `Data3DTexture`, **half float, not float**: linear filtering of half-float
  textures is core WebGL2, while filtering a 32-bit float texture needs
  `OES_texture_float_linear`, which is not guaranteed. Unfiltered sampling makes the marches
  step visibly cell to cell.
- `three/ThreeDensityShader.ts` injects the lookups via `onBeforeCompile`, applied immediately
  after `lights_fragment_begin` - the one point where `reflectedLight.directDiffuse` already
  holds the sun and `irradiance` still holds the hemisphere term that `lights_fragment_end` will
  consume. Scaling those two leaves every other material feature working.
- The march starts **two cells clear of the surface**, which is why a surface can no longer
  occlude itself. Same offset the WebGPU tracer uses.

Two decisions worth keeping:

**Gated by uniforms, not by a `#define`.** A define would recompile every material in the scene
each time the user ticks a box - which is the same failure that once made the shadow toggle
appear to do nothing until the next timestep. A uniform at zero costs one branch and multiplies
by exactly 1.0, so the default tier stays bit-identical.

**sampler3D is WebGL2 only.** On a WebGL1 context the field is left undefined and the backend
renders as it did before; injecting the code there would fail to compile and take the whole
backend down.

Measured on the fixture: occlusion changes 0.52 % of pixels on a single plant and 12.27 % on a
60-plant canopy, every changed pixel darker. Cast shadows are clearly visible on the ground.
Tier-0 parity against WebGPU stayed at exactly 665 pixels. (It is **21** since the depth range was
shortened - see "Obstacle mapping flicker" below. Compare against 21, not 665.)

**The cost is a CPU voxelisation on every plant rebuild** - about 7.5 ms per timestep on a
231-frame session (settle 1.1 ms to 8.6 ms). `DENSITY_TRIANGLE_BUDGET` refuses scenes past
400,000 triangles and says so, because scattering runs on the main thread and a canopy large
enough to matter would stall the tab between timesteps. WebGL is already the wrong backend at
that size - it draws one call per primitive.

The bias spinner is gone. Neither backend compares depths any more, so there was nothing left
to bias.

### The first AO attempt was measurable and invisible

Worth recording, because "the measurement says it works" nearly closed this out wrongly. The
first volumetric AO changed 0.52 % of pixels on a one-plant scene, every one darker - a real,
reproducible effect. The user still saw nothing. **Max delta was 10/255, about 4 %.**

The interactive path was not at fault: screenshotting the live canvas either side of the
checkbox (`tools/visual/toggle.mjs`) gave 5,398 pixels darker, bbox exactly on the plant,
identical to the offscreen still. The term was applied. It was just too faint to perceive.

Three causes compounded, all of them scale mistakes rather than logic errors:

1. **The AO march reused the sun's two-cell start offset.** At a 16 cm cell that is 0.33 m, and
   the plant being shaded is 0.30 m across - so every ray began beyond the whole plant and
   sampled empty air. The sun does not care, because it marches metres and meets the canopy
   above; occlusion is a contact effect and cares about the leaf next to this one. **This is the
   trap: the two terms read the same field through the same uniforms, so it is natural to give
   them the same march, and that is exactly wrong.**
2. **`DENSITY_DIM` was 64**, a 16 cm cell, so a whole plant spanned under two cells. A field
   cannot resolve occlusion finer than its cells.
3. **The fifth cone was a duplicate of the first.** `vec3(0.0, 0.258819, 0.0)` normalises
   straight back to (0,1,0), so it was four cones pretending to be five, with a narrower spread
   than intended.

Fixed together: `DENSITY_DIM` 128 (8 cm cells), AO start offset 0.75 of a cell, six steps at
1.15x reaching about 0.6 m, and a proper symmetric set of one cone along the normal plus four at
45 degrees with cosine weights. Max delta went **10 -> 44** live, **10 -> 66** on the offscreen
still, and 70 on a 60-plant canopy - now comparable to the WebGPU GTAO's 36, which the user
could see. The character changed too: fewer pixels affected (0.53 % -> 0.30 %) but each far more
strongly, which is the difference between a broad dim and contact darkening.

**It got cheaper, not dearer, despite eight times the cells.** The area-to-extinction sweep now
skips empty cells, and a canopy fills a few per cent of the grid at most, so the per-timestep
cost fell from 8.6 ms to 5.5 ms.

One structural limit worth knowing: the AO multiplies `irradiance`, which is the ambient and
hemisphere share only - it deliberately does not darken direct sun. With a strong sun the effect
is therefore inherently subtle, and that is correct: occlusion is about how much sky a point can
see. The WebGPU path makes the same choice.

## History retention: what killed the green-facade run, and Stage 1 of the fix

A 4,134-plant run from `Nooshin_Export/2026-August` killed the tab around simulated month 2.
**Not a leak — an absence of any ceiling.** `appstate.history` accumulates one entry per timestep
and is emptied only when a new run starts. Nothing evicted, nothing warned, nothing pushed back.

Measured rather than assumed: V8's ceiling here is **4,192 MB**; retained history grows as
**0.54 x nSteps x finalFrameSize** (integrated over a real 231-step growth curve); and base64 is
ASCII so V8 stores it **one byte per char**. Back-solving from the reported crash point, at
3-4 h/tick the tab dies when a frame reaches 16-22 MB, i.e. 40-55 primitives per plant - which is
what tray perennials look like two months from sowing, and a third of a mature plant.

Three things made it worse than the arithmetic suggests:

- **Every incoming frame briefly costs 3.5x its own size** - the websocket string, `JSON.parse`'s
  fresh copy, `atob`'s binary string and the `Uint8Array`, all live at once, all in large-object
  space. Near the ceiling that spike is what actually trips the OOM, not the steady state.
- **`historySize` under-reported by 33%.** It counted decoded bytes while what was retained was
  the base64 string. Someone watching it pass 1500 Mb was holding about 2.0 GB.
- **`save()` was already broken.** The whole history went through one `JSON.stringify`, which
  exceeds V8's ~537 M character cap above roughly 512 MB - `RangeError: Invalid string length`.
  The user could not save the very session that was about to die.

### Stage 1, landed

- **History holds bytes, not base64.** The declared type always said `Uint8Array`; the code did
  not. Converting at receipt and dropping the string is **-25%** retained and lets the string be
  collected immediately. It also makes `historySize` honest for free, since `byteLength` is now
  what is actually held.
- **A budget with a defined failure.** `historyBudget` is 45 % of `jsHeapSizeLimit` where the
  browser exposes it. On crossing it, `stopForMemory()` aborts the run **once** (latched) and
  says so. Refusing the next frame beats losing every frame already computed - especially now
  that the session can be saved.
- **`save()` assembles Blob parts** instead of one string, so the length cap cannot be reached.
  The on-disk format is unchanged (base64), so old sessions still load.
- **`SCENE_CACHE_PRIMITIVES` 200,000 -> 1,500,000.** At 200,000 the LRU was **inert at production
  scale**: one 4,134-plant frame is ~674,000 primitives, so `size > budget` refused every frame
  and every scrub paid a full decode.

**A trap worth remembering:** `load()` assigned `history` twice, once before a `batch()` and once
inside it. Harmless while it was a reference copy - and an immediate `TypeError` once the
rehydration consumed its source as it converted. Duplicated work is not free once the work stops
being idempotent.

`?debughook=1` now exposes `window.__appstate` alongside `window.__renderer`, because these
invariants (the shape of `history`, what `save()` wrote, the ceiling) are exactly the kind that
break silently and are invisible from the DOM. `tools/visual/saveload.mjs` round-trips a session
through save and load and compares the bytes.

### Stage 1b, landed: compression and the asynchrony it forces

Frames are deflated in the background once they fall out of a hot window of the 8 most recent.
`src/helpers/deflate.ts` wraps `CompressionStream`/`DecompressionStream` and is shared with the
PNG encoder, which had its own copy.

**The hot window is the whole design.** Compression makes retrieval asynchronous, and asynchrony
is what the playback machine cannot absorb - a preact effect cannot be async and `batch()` cannot
span an await. Keeping the recent frames raw means sequential playback and single-stepping, which
is what the viewport does almost all of the time, never leave the synchronous path. Only scrubbing
back into old history pays for a decompress.

So retrieval is two functions, not one:

- `sceneAtSync(i)` returns the scene or **undefined** - a cache hit, or a frame still in the hot
  window. Undefined rather than `[]`, so a caller knows to go asynchronous instead of thinking the
  frame is empty.
- `sceneAt(i)` inflates and decodes, and is what the offline renderer uses.
- `showSceneAt(i)` writes `scene` immediately on the synchronous path and later on the cold one,
  guarded by a token: dragging the timeline fast issues several requests, and without the guard
  the slowest could land last and leave the wrong frame showing.

`FrameSource.at()` became async. The await is deliberately inside it and not around the
`appstate.scene` write that follows - that write runs the rebuild effects synchronously, and the
export loop depends on the renderer being current by the time it calls `renderToPixels`.

Two things worth keeping:

- **Compression can lose.** The six-byte zlib wrapper outweighs the gain on a tiny frame, so the
  compressor keeps whichever is smaller and only sets `plain: false` when it actually swapped in
  packed bytes. Flagging a frame compressed while storing the raw copy would corrupt it on the way
  back.
- **A loaded session is a large history too.** The compressor is normally driven by frames arriving
  from a run, so loading a session left it raw forever - the harness caught that, not a test.

Measured on the 231-frame reference session: **222 of 231 frames packed, stored bytes 947,494 ->
694,537**. That is 1.36x on a fixture whose frames average 4 KB; a single mature frame measured
1.69x, and the zlib overhead is proportionally smaller the bigger the frame.

`tools/visual/compresscheck.mjs` loads a session, waits for the compressor to drain, and proves a
packed frame still decodes to the same scene.

## Four defects the audit caught, and one measurement that kept lying

### The eviction that protected nothing

`evictOldestPayloads` kept a fixed window of the newest **64 frames** while the budget was in
**bytes**. At the ~108 MB a mature green-facade frame occupies, the budget is crossed around
frame 12, so `history.length - 64` clamped to zero, the loop body never executed, and nothing was
dropped or reported. Worse, because a `runId` was present the `stopForMemory` fallback was skipped
too - **the presence of the thing that makes dropping safe disabled the one path that could have
saved the tab.** The OOM protection was inert at exactly the scale it was written for.

Now byte-driven, with a small count-based window only around the playhead and the newest frame,
and it **escalates**: if it cannot get under budget it calls `stopForMemory()` rather than
returning quietly. The incoming frame is also counted before eviction runs, so the loop weighs the
real total rather than one missing the frame that triggered it.

### A best-effort store that could kill the server

`RunStore`'s constructor let its exceptions escape, and it is called from a top-level statement
before the host is built - so an unwritable root took the whole backend down before Kestrel ever
listened. That is the deployed case: the image copies a root-owned `/app` then drops to an
unprivileged user, which cannot create `/app/runstore`. The class's own contract said "never
throws; storage is best-effort" and it did neither. It now catches, sets `Disabled`, and every
operation becomes a no-op. Verified: with an unwritable root the server answers **HTTP 200** and
logs why persistence is off.

Related, and just as invisible: `AddEnvironmentVariables(prefix: "AGRO_")` was registered *after*
the first `Configuration[...]` read, so `AGRO_RunStoreRoot` - the operator's only escape hatch -
was silently ignored. Registered first now.

### Orphaned runs were unkillable

Moving the loop off the hub introduced this. `OnDisconnectedAsync` dropped the connection
association, but `Abort` is reachable **only** through that association - so after a reload nothing
on the server or the wire could stop the run. It went on computing, exporting and deflating every
timestep at full speed, pushing frames into an empty group, and each reload added another. Runs
now carry `UnwatchedSince` and stop themselves after a grace period, and the client keeps its run
id in `sessionStorage` so a reload can reattach instead of losing it.

### A run stuck at "running" made the retention budget unreachable

Status only ever changed in-process, so a crash or redeploy left records at "running" forever.
`Evict` skips those while still counting their bytes, so a few interrupted long runs meant every
later `BeginRun` deleted **all** the completed runs and still never got under budget. The store
now sweeps stale "running" records to "aborted" at startup - nothing can be running in a fresh
process - and refuses to evict at all when the un-evictable portion alone exceeds the budget.

### Determinism: what is actually known

Two measurements of mine were wrong before one was right, both times because the harness was not
sending the same request twice:

1. The `HoursPerTick` control snaps against its **current** value, so driving it through the DOM
   is stateful - one run came out at 3 h/tick and the other at 4. Set the signals, not the inputs.
2. `pushSeedRaster` picks a **random species per position**
   (`species[Math.floor(Math.random() * species.length)]`), so any two runs seeded with it differ
   by construction. Place seeds explicitly.

What survives: **a single-plant run is byte-identical across runs** - 60 frames, zero differing
bytes, verified through the run store. Multi-plant determinism is **unmeasured**, because every
attempt so far compared two different requests.

A `Deterministic` flag now exists (`SimulationWorld.Deterministic`, `Deterministic` on the
request, a HUD checkbox) forcing the sequential tick. It is plumbed end to end and recorded in the
run metadata, but **it has not been shown to deliver reproducibility** - the one run where it was
enabled still diverged, and that test was itself invalid for reason (2). Treat the flag as
untested until a fixed-species, fixed-position multi-plant comparison says otherwise.

## Stage 2: the frames live on the server now

The data existed exactly once, in the one place that could not afford it. The server produced
each frame and discarded it immediately; the browser kept every frame forever. Stage 2 swaps
those round.

### Server side

`AgroServer/Services/RunStore.cs`. **Bulk to files, metadata to LiteDB** - one two-month run at
production plant count is ~17 GB compressed, one frame already exceeds LiteDB's 16 MB document
limit, and a LiteDB file that size is outside where it behaves well. Keeping frames as opaque
files also sidesteps LiteDB's reflection-based mapper being at odds with `CreateSlimBuilder` plus
the trim analyzer.

    <root>/index.litedb                 run metadata only
    <root>/models/<sha256>.obj.gz       field model sources, SHARED between runs
    <root>/runs/<id>/request.json       so a resumed run can rebuild itself
    <root>/runs/<id>/index.bin          8 bytes per frame, APPENDED
    <root>/runs/<id>/terrain.bin.gz     the serialized soil, so a stored run can be VIEWED
    <root>/runs/<id>/frames/<step>.g

The index is an appended file rather than a LiteDB document because at a frame per timestep a
growing document would be rewritten thousands of times to add eight bytes.

The frame extension is **`.g`**. Runs written before that read back unchanged - the reader falls
back to `.z`, which is the same zlib stream under the older name. Nothing renames them: a rename
pass over the 42,000 frame files of an existing store is a large destructive startup operation in
exchange for nothing the fallback does not already give.

**The stored run is complete where the streamed one has holes.** The export used to happen only
on the steps a `Preview` request happened to land on, so what the client held depended on how
fast the browser was keeping up. Now every step is exported and stored, and the wire stays
exactly as lazy as it was.

Frames are served from `GET /Simulation/run/{id}/frame/{step}` with **`Content-Encoding:
deflate`**. They are stored deflated, so the browser inflates them natively and neither side
spends anything on it - measured: 443 bytes stored, 716 received, decoded to a valid scene by the
app's own reader. `Cache-Control: immutable` lets the browser cache hold the working set, since a
frame never changes once written. REST rather than a hub method, because the SignalR JSON
protocol base64-encodes every `byte[]` - a third more bytes for nothing.

### Making a stored run re-runnable, and viewable

`request.json` alone is **not** enough to rebuild a run that used an imported facade, and the two
missing pieces are not recoverable after the fact:

- **The model.** It reaches the simulation through `POST /terrain` and a per-connection cache that
  is single-use and expires in five minutes, and `FieldModelKey` has never been set by anything -
  it is `null` in every request ever stored. So the OBJ source is filed at UPLOAD time, which is
  the last moment it exists, content-addressed under `<root>/models` and shared between the runs
  that use it. A run then names its model by hash, and continuing it months later re-uploads
  nothing. Unreferenced models are collected, but only once they are older than `ModelGrace` - a
  model is stored before any run refers to it, so collecting immediately would delete the one
  about to be used, every time.
- **The parameters the trays were built with.** The resolution used is the one that was on screen
  when the OBJ was uploaded, which is not necessarily the one in the request by the time the run
  started. Rebuilding with the wrong one gives a different set of trays, so the upload-time values
  travel with the run.

All three soil implementations now write the SAME stream - version 1, the fields, then an obstacle
section that is a pair of zeros when there is nothing to send. They did not before:
`SoilFormationsList` wrote version 1 with the section while the box and tetrahedral fields wrote
version 0 and stopped after the fields. That was invisible for as long as the only terrain on this
wire came from an imported model, and storing every run's terrain put a box field there - where
`readTerrain`, which always reads an obstacle count next, ran off the end of a 54-byte buffer with
a RangeError. `ThreeFrontend/tests/terrainFormat.test.ts` pins both directions, because a truncated
stream that half-decodes looks exactly like a correct one.

A terrain is stored **only for an imported field**. A plain box field is already drawn by the
viewport from the configured field size when it has no terrain of its own - that is what a live run
shows - so storing one would hand the same box back through a second path whose position and extent
are derived differently, and a reopened run would sit in a subtly different box from the one it ran
in. It is not data; it is a shape the client can already draw from numbers it has.

When the cached terrain is absent but the model is not, `GET /run/{id}/terrain` **rebuilds it and
writes it back**, so a run stored before terrains were cached is still viewable and the rebuild is
paid for once. `hasTerrain` on the wire therefore means "can be drawn with its trays" - a cached
terrain, or a model to rebuild one from - because that is the question the panel is asking.

`terrain.bin.gz` is separate from the model and serves the other half: it is the soil exactly as
the socket sends it to a viewport. Rebuilding it from the OBJ costs seconds and a few hundred
megabytes; serving the bytes costs neither, and **viewing** a past run is the common case. Without
it a stored run opens as plants floating in nothing, because trays and facade never travel in a
frame.

**Counts are taken off the built world, not off the request.** For an imported facade the two are
different measurements: the request carries a single `mesh` obstacle standing for the whole model -
`SoilFormationsList` merges them before they reach the browser - while the world holds one per
named OBJ group. Trays are not in the request at all. Runs stored before these were recorded report
**0, meaning "not measured"**, and the panel prints `?` rather than a zero that would read as a
count. What CAN be recovered from an old run is backfilled once, by `RunStore.Reconcile`: the
request gives plants, species, seed and dates, the directory gives the sizes, and the first frame's
six-byte header gives the export flags.

### The client evicts the comfort window before it stops a run

Eviction spares the newest frame and four either side of the playhead - they are about to be drawn,
and re-fetching one mid-playback stalls it. When sparing them means the budget cannot be met, they
go too, and only the frame actually on screen survives. That rule used to be absolute and it stopped
a ten month run at simulated month 4: a mature stand's frames are hundreds of megabytes, so the ten
protected frames alone exceeded a budget under two gigabytes while every frame sat safely on disk.
The window is a comfort, not a correctness requirement - with a store each frame is about three
milliseconds away. Without a store the old trade still holds and the run stops, because a dropped
frame is then gone.

Note which limit is which: "full - run stopped" is the BROWSER's heap budget
(`jsHeapSizeLimit * 0.15` with a store), not the disk. `AGRO_RunStoreRetentionBytes` is the disk
budget and it never stops a run - it evicts old ones.

### The store maintains itself on a timer

Eviction used to happen only when a simulation STARTED, and model collection only from eviction or
an explicit delete. On a host that uploads and browses but does not simulate - most of a working day
- nothing swept at all: a store already over budget stayed over it, and uploaded models accumulated
with nothing to collect them, at up to 256 MB per upload and *before* any run refers to them. A
one-shot timer re-armed at the end of each pass (`AGRO_RunStoreSweepMinutes`, default 30, 0 to
disable) collects abandoned partials, collects dead models, then evicts.

**Field models have their own budget** (`AGRO_RunStoreModelBytes`, default 8 GiB) rather than a share
of `RetentionBytes`. The two cannot be spent against each other: evicting a run does not release its
model - it outlives the run and may be shared - so a single budget has the eviction loop deleting
every completed run in the store chasing a total that only model collection can bring down. A
*referenced* model is never deleted at any size, for the same reason a branched-from run is pinned:
it is the only copy of the OBJ that makes its run re-runnable and drawable. Over cap with everything
referenced, the store says so and deletes nothing.

Collection is two-stage: unreferenced and older than `ModelGrace`, then - only over the cap -
unreferenced regardless of age, oldest-touched first. Both stages re-check the timestamp **under the
metadata lock immediately before deleting**, because between building the reference set and reaching
the file, an upload of the same OBJ can have claimed it: `StoreFieldModel` returns the hash of an
existing file without rewriting it, and the touch is the only evidence. A 15 minute floor
(`ModelMinGrace`) is never crossed whatever the budget says - it has to exceed the upload buffer's
own five-minute lifetime, since a model whose buffer entry is still live is about to be used.

A store whose `index.litedb` was lost, replaced, or assembled by copying run directories between
machines is adopted at startup rather than ignored - the frames are the run, and the database is a
description of them. Which is why the model hash is written to `runs/<id>/terrain.json` as well as
the database: an adopted run that cannot say which model it used contributes nothing to the
reference set, and the next eviction would delete every model in the store. `CollectModels` refuses
to run at all while any run names a model without naming which - an incomplete reference set does
not produce a partial clean-up, it deletes precisely the models that are still in use.

### Resume carries a run on; continue and fork recompute it

`runs/<id>/checkpoint.bin.gz` is the live simulation state, written every
`AGRO_RunStoreCheckpointStride` steps (default 32) and always at the end however the run finished.
Restoring it and carrying on is **byte-identical to never having stopped** - `Agro --checkpoint-selftest`
proves it across three scenarios and four resume points each, and is the only test this can have:
correctness is not a property of any one field but of the whole set, and a miss shows up as a run
that continues plausibly while diverging from the one it claims to continue.

What the checkpoint holds is what the request cannot rebuild. The structure comes back for free -
Initialize is deterministic, so the same request builds the same soil, the same plants in the same
order, and formations are append-only and never removed, which is what makes a positional restore
safe. What it cannot rebuild is everything the simulation has done since: the clock, the water in
each cell, every organ, and what each plant's generator will say next.

Three pieces of that are easy to mistake for derived state and are not:

- **The previous step's irradiance** (`IrradianceClient.Irradiances` and its per-formation offsets).
  It is recomputed every step - but by a callback that runs AFTER the agents tick, so the agents
  consume the previous step's values. A world restored without it spends its first step in the dark.
- **Queued deaths.** `PlantFormation2.Census` calls `AG.Death` for emergency energy pruning *after*
  `AG.Census` has run, so those deaths are applied by the NEXT step. Dropping them reprieves organs
  the plant had already decided it could not feed.
- **Flowering history and the daily maxima.** The first would let a plant flower twice in one year;
  the second normalises every organ's share until the next `NewDay` runs.

The tree cache and the BVHs genuinely are derived - but they are NOT rebuilt by the next tick, only
by a `Census` that has something to do, so `ReadState` rebuilds them the same four ways `Census`
does. Agents are blitted rather than written field by field: they are unmanaged structs, so their
memory is their state, and a hand-written serializer is a second description of the same layout that
is free to drift from it.

A resumed run **appends to the same run**. It is the same simulation, and splitting it across two
ids would leave neither of them the whole thing; the frames keep their step numbers, the index
extends, and `TotalSteps` moves to the new horizon. Extending past the original total is the same
operation with a larger one - safe because the weather table forks its own RNG stream and plans
months sequentially, so a longer table has a byte-identical prefix.

### Continue and fork are re-runs, not resumes

Nothing in this project serializes live world state. The store holds exported geometry, which is a
picture of the plants and not the plants, so a child run is **computed again from its parent's
recorded request**, from step zero. For a deterministic parent the first stretch reproduces it
exactly and the comparison means something; for any other it is a second sample of the same
scenario, which is why `Deterministic` is recorded per run and shown in the panel. `ParentRunId`
is lineage only and changes nothing about how the simulation runs.

The two differ only in what happens next: **continue** raises `TotalHours` and starts immediately;
**fork** loads the scenario into the editor and stops, because a fork exists to be changed.

### Client side

**Keep `t`, stream `s`.** Only four places ever read the payload; every other consumer - the
clock, `timestepAt`, all three frame mappers, the transport bar, the export panel - reads `t` or
`length`. So `history[].s` became optional, and under memory pressure the oldest payloads are
dropped while `t` stays.

`sceneAt` gained a fetch fallback, and `rawFrameBytes` collapses the three states a frame can be
in - held raw, held compressed, evicted to the server - into one call, because `save()` and the
`.prim` download both need raw bytes and neither should have to know which.

Eviction only happens **when a run id exists**. Without one the frames are not recoverable, so
the old behaviour stands: stop the run, which at least leaves a saveable session.

Measured on a 160-frame run with the budget forced low: **96 frames evicted, 64 resident, and a
frame fetched back in 3 ms** and decoded correctly while its memory stayed freed.

### The run no longer belongs to the connection

`AgroServer/Services/SimulationRunner.cs`. The loop used to live inside the `Run` hub invocation,
and three things followed from that: it held one of the three parallel invocation slots for the
entire run while `Preview` and `Abort` competed for the other two; a client that reloaded
orphaned the run, which kept ticking with nowhere to send frames while its connection id stayed
marked "already running" forever; and nothing could outlive the connection, which is what
crash-resume needs.

Runs are keyed by run id and frames go to a SignalR **group** named by it. A reconnecting client
gets a new connection id, so it calls `subscribe(runId)` to rejoin - without that the run would
carry on server-side while the tab heard nothing more.

**`Create` and `Begin` are separate on purpose.** Starting the loop inside `Create` races the
group join: anything pushed before the connection is in the group goes nowhere, and the only
symptom is a missing first progress tick. So the hub creates, joins, announces, then begins.

`OnDisconnectedAsync` now exists at all. It drops the connection's `SoilFormationsList` - 150-250
MB for the green-facade model, previously kept per connection id forever while the frontend
reconnects with infinite retry - and detaches the connection from its run **without stopping the
run**.

Measured: the socket was forced offline mid-run for four seconds. 19 frames before the drop, 167
after reconnecting, server at 168 and still running. `tools/visual/reconnect.mjs`.

### Reloading the tab used to lose the run

The frames were on disk and `GET /run/{id}/index` served the timeline, but **nothing in the client
ever called it**, and the socket is not opened on page load either. So a reload during a long run
left the simulation computing for an audience that had gone - until the unwatched grace period
stopped it - while the tab showed an empty transport bar and no hint that a run had existed.

`recoverRun()` runs once at module load. It reads the run id from `sessionStorage`, asks the server
what the run is, rebuilds the timeline from the index, and only then decides what else to do:

- **finished** - stop there. The timeline is final and its frames come over REST; a completed run
  should not open a websocket to do nothing.
- **still running** - reopen the socket, `subscribe(runId)`, and put the tab back into
  `ForwardWaiting` with `computing` set. Rejoining the group is **not** enough to resume streaming:
  the wire is deliberately lazy, so frames arrive only while the client is asking for them, and a
  freshly loaded page is asking for nothing. Without that last step the reattached tab sits on a
  timeline frozen at the moment of the reload while the run computes on, which looks exactly like
  a failed reattach.

Measured (`tools/visual/recovercheck.mjs`): reloaded at 25 frames with the run still computing,
recovered the timeline, resumed streaming, and finished holding **300 frames against the server's
300** - `RECOVERED`.

### The stored run is complete; now the visible one is too

The server exports and stores **every** step while the wire sends a frame only when asked, so the
history a run built up was whatever the browser managed to keep up with. A run that stored 1,440
frames and streamed 60 left the other 1,380 on disk and unreachable from the page that asked for
them. `syncTimelineToIndex` merges the stored index into the history when a run ends, so those
become simply frames whose payload has not been fetched yet, like any evicted one.

It merges **by step, not by position** - the two sequences differ exactly in the positions, so
index-for-index would pair unrelated frames and mislabel every payload after the first gap.

### `historySize` was answering two questions

`historySize == 0` was the transport bar's test for "there is nothing to play", and the export
panel read it only to subscribe. That held while every frame in the history had its payload. It
stops being true the moment frames are evicted or a reload rebuilds the timeline from the index: a
history of 600 frames then legitimately occupies **zero bytes**, and every transport control would
be disabled over a run that is entirely intact on disk. `historyCount` answers "how many frames";
`historySize` answers "how many bytes".

### A saved session recorded a byte count for a different history

`save()` writes every frame **raw** - it inflates the compressed ones and re-fetches the evicted
ones - but recorded the live `historySize`, which counts those same frames compressed or not at
all. So a session saved with 96 of 160 frames evicted claimed a few megabytes and loaded hundreds.
The HUD then under-reported and, far worse, eviction measured against a total too small to ever
cross the budget - the exact failure Stage 1 existed to end. `load()` now **measures** what it
loaded (`measureHistoryBytes`) rather than believing the file.

Verified on the 231-frame session: `694,537` stored + `231 x 136` overhead = **725,953**, which is
what the HUD now shows.

### A loaded session inherited the previous run's id

`load()` left `runId` pointing at whatever run the tab had last computed. Eviction is only safe
because the server is assumed to hold what it drops, so that turned "the server has it" into
silent data loss - and re-fetching returned the **same step of a different simulation**, which
decodes perfectly and is simply the wrong plants. `load()` and `run()` now both clear the id and
the `sessionStorage` copy.

### One number, two names, on the server

`RunStore.WriteFrame` took both a `step` and a "simulated timestep `t`", and its only caller passed
the same value for each. The client fetches by the one it kept. Two names for one number is an
invitation: the day they diverge, the client silently gets the wrong frame or a 404 and nothing on
either side says so. It is now one parameter, documented as the frame's **address**.

### Stage 2 did nothing at all in the deployed image

`RunStoreRoot` defaulted to `AppContext.BaseDirectory/runstore` = `/app/runstore`. `/app` is copied
in as root and `Dockerfile-sim` then drops to uid 61000, which cannot create it - so the store
caught, disabled itself, and the whole feature was inert in production while working perfectly in
development. The store now lives in `/data`, owned by that user, declared `VOLUME`, and mounted
from a named volume in `docker-compose.yml`. `AGRO_RunStoreRetentionBytes` sets the budget, because
the host is the only thing that knows whether 64 GiB is generous or fatal.

### Two lifecycle holes the grace period did not cover

**The abandonment check was inside the computing branch.** With lazy previews that is every
iteration, so it looked right - but in `ExactPreview` mode the loop parks in the `else` branch
waiting for a `Preview` call a departed client will never make. The abandoned run the grace period
exists to stop was precisely the one that never reached the check. Hoisted to the top of the loop.

**A run created but never begun leaked.** `Create` and `Begin` are separate so the caller can join
the group in between, which means the caller can also fail in between - a client that drops during
the join left a run that exists everywhere and runs nowhere, with a store record stuck at
"running" where nothing can evict it. `Cancel(runId)` handles that, from a `catch` in the hub.

### What was verified, and how

| Behaviour | Result |
| --- | --- |
| Reload mid-run, reattach, finish | `RECOVERED`, 300 client frames = 300 stored (`recovercheck.mjs`) |
| Eviction and re-fetch | 130 of 160 evicted, frame back in 3 ms (`evictioncheck.mjs`) |
| `Content-Encoding: deflate` | 389 stored -> 554 received, browser inflated, V7 decoded (`fetchframe.mjs`) |
| Compression | 222 of 231 packed, 694,537 stored (`compresscheck.mjs`) |
| Save/load round trip | byte-identical at 2 and at 231 frames (`saveload.mjs`) |
| Server hard-killed mid-run | record swept to `aborted` on restart, its 13 frames still readable |
| Unwritable store root | HTTP 200, store disabled with a diagnostic, frames 404 |
| Retention eviction | 8 runs / 451 KB -> 2 runs / 24.7 KB, oldest first, no orphaned directories |
| Evicted run, traversal, bad step | 404 throughout, never a 500 |
| Cross-backend parity | **665** of 1,024,000 at that time; **21** since the depth range changed |
| Unit tests | 318 in 21 suites, `tsc` clean |

### Stage 2 made the simulation two to three times slower, and why

Reported from a real green-facade run: two months in several minutes before, an hour to reach week
four after. The cause is not subtle once seen. The old loop exported a frame **only inside**
`if (requests.Preview)` - that is, only when the browser had asked for one, which at several
thousand plants is rarely. The new loop exported **every** step and then deflated it, wrote it,
appended to an index file and updated a LiteDB record, all on the simulation thread.

Measured with a probe against the real `Agro` assembly (`ExportToStream` plus the store's own
work, per step, at four-hour ticks):

| plants | simulation | export | deflate | write+index | storage total |
| --- | --- | --- | --- | --- | --- |
| 400 | 6.1 ms | 3.5 | 3.9 | 0.1 | **1.24x the simulation** |
| 1600 | 8.4 ms | 6.3 | 9.5 | 0.2 | **1.90x the simulation** |

Two things to read off that. **Deflate is the largest single term and it grows fastest** - it
overtakes the simulation step itself between those two sizes. And **the ratio worsens with scale**,
which is why the complaint came from the biggest scene rather than from the fixtures.

What was NOT the cause, both measured rather than assumed: LiteDB in `Shared` mode costs 0.73 ms
per frame (Direct is 0.22, so the connection mode is worth about half a millisecond, not a run);
and `SkipFormations` is walked with a sorted pointer, not `Contains`, so the export is O(n) in
plant count and not O(n^2).

**The fix is that only the export has to happen on that thread.** It reads live world state, so it
cannot overlap the next step. Everything after it can:

- `RunStore` now has one background writer per run, fed through a **bounded** queue. Bounded
  deliberately: each slot holds a whole exported frame, so an unbounded queue is just a slower way
  to run the server out of memory - moving the Stage 2 failure from the browser to the server
  would not be a fix. Measured handoff cost on the simulation thread: **0.1 ms**, and the writer
  never fell behind.
- The index file is opened **once per run** instead of being opened, appended to and closed for
  eight bytes every frame.
- The metadata record is updated on a **two-second timer** rather than per frame. It is a progress
  figure; a thousand mutex round trips to keep it exact are wasted.
- `ExportToStream`'s `MemoryStream` now carries a **capacity hint from the previous frame**. An
  unsized one starts at 256 bytes and doubles, so a 20 MB frame meant about seventeen
  reallocations and ~40 MB of copying every step, all on the large object heap. The stream itself
  cannot be reused - `ExportV7` wraps it in a `BinaryWriter` that closes it - but the capacity can.
  Measured at 1,600 plants: export 6.7 -> 5.8 ms, allocation 4,220 -> 3,932 MB, gen2 8 -> 6.

Result at 1,600 plants: storage went from **1.90x** the simulation to **0.64x**, i.e. the whole
run from 2.90x to 1.64x of its unstored cost.

**The remaining 0.64x is the export, and the only way to avoid it is not to do it.**
`AGRO_RunStoreFrameStride` stores every Nth step; the last step is always stored. At 1 - the
default - the stored run is complete and every timestep can be scrubbed to. At 4 the storage cost
is a quarter and a frame is kept every 16 simulated hours. That is a real trade and it belongs to
whoever is running the thing, not to this code.

### The slider showed the frames that had arrived, not the run that had been computed

Reported as "at 550/1080 steps it keeps showing 1 month" - and it was right to be suspicious. The
clock reads `simHoursPerTick * history[playPointer].t`, which is the time of the frame **being
looked at**. That frame is whatever last arrived on the wire, and the wire is lazy: the server
stores every step but sends one only when the browser asks, so at several thousand plants the
timeline crawled while the progress line underneath it counted on correctly. Two numbers for one
run, and the honest one was the small print.

`syncTimelineToIndex` already existed for reloads; it now also runs **during** the run, driven by
`Progress`, throttled to one request every two seconds and only when the run is actually ahead.
The index is eight bytes a frame, so catching up on a 1,080-step run costs about 8 KB - and the
slider then spans everything computed so far, each payload fetched only if scrubbed onto.

Two things this needed:

- The merge became a **union**, not "take the server's list". With a frame stride the client holds
  previews of steps that were never stored, so neither side is a superset of the other.
- It redraws **only when the visible frame actually moved**. It now runs repeatedly while a run
  streams, and redrawing the same frame every two seconds would fight the playback machine for the
  renderer at exactly the scale where that is expensive.

`tools/visual/timelinecheck.mjs`. Note how it has to work: at 300 small plants the browser keeps up
and the sync has nothing to do, so watching the gap "passes" while proving nothing - the first
version did exactly that. It now truncates the history mid-run and requires frames to come back
**with no payload**, which only the index can produce. Measured: cut to 5 frames at step 39,
back to 169 frames at step 169, gap 0, 34 of them recovered from the index.

### The animation exporter waited for the network on every frame

Each frame the client no longer holds is an HTTP round trip, and it sat in series with the render:
fetch, decode, render, fetch, decode, render. The render is the long pole, so the fetch is nearly
free if it overlaps. The exporter now starts pulling the **next** frame before rendering the
current one.

Only the bytes, into the browser's own cache - frames are served `immutable`, so a plain `fetch`
warms it and the real read is a local hit. That needs no cache, no eviction and no memory budget
here, and it does not resurrect the decoded-frame buffering `FrameSource` is right to refuse: a
decoded scene is thousands of objects and tens of megabytes, the compressed bytes are a few.

### The playback machine could not notice that a run had ended

Two defects, one visible symptom. A finished run left the step controls disabled over a history
that was entirely present.

**`PlayState.ForwardWaiting` advanced the playhead without checking where it was going.** It did
`playPointer + 1` unconditionally - the only branch in that switch that does not bound-check - so
at the end of a run the playhead came to rest ONE PAST the last frame. Nothing ever threw, because
every reader clamps, which is exactly why it survived: the pointer was simply wrong, and the clock
reported a step that did not exist.

**And nothing woke the machine after the last frame.** The state loop only advances when
`playRequest` flips, and `playRequest` is driven by an effect on `playing` - so a transition that
sets `playing` to the value it already holds notifies nobody. The thing that woke it during a run
was a frame arriving. After the final frame it stayed in `ForwardWaiting` for the rest of the
session. The run ending now says so, from the `result` handler.

`tools/visual/playstate.mjs` measures it on a plain run: **100 frames, pointer 99, playing 0,
step controls live**. Before: pointer 100, playing 3, disabled.

### A live timeline and an arriving frame are two ways to learn about the same step

Syncing the timeline during a run introduced this, and it is worth spelling out because it looks
harmless. The preview handler did `history.push(...)`. Once the timeline is also being filled in
from the run index, a preview for step 57 arrives at a history that may **already** carry step 57
with no payload - so the push left a duplicate `t` and a non-monotonic history.

That breaks more than it appears to. `FrameSource.isMonotonic` gives up on a non-monotonic series
and the whole animation silently falls back to uniform timing, and the slider grows a step that is
not a step. `upsertHistoryFrame` files a received frame **by step**: binary search, fill the
payload if the entry is already known, insert in sorted position if not, and count the bytes
exactly once however many times the same step arrives - a reconnect can deliver one twice.

The playhead then follows the newest frame that has actually ARRIVED, not the end of the history,
because the history now runs ahead of the wire. The transport behaves like a player buffering: the
slider spans what exists, the playhead sits on what has been received.

Covered by `tests/runIndex.test.ts`, including the case that matters - an index announcing every
step while previews arrive for a scattered few, asserting the result is still monotonic.

### Determinism: measured at last, and the old claim does not hold

The repo has long recorded that "two identical requests produce different plants (188 vs 167
organs)". With every frame stored server-side that is finally measurable rather than recalled, and
`tools/visual/determinism.mjs` measures it: same request three times - three, not two, so that a
process-global mutated once by the FIRST run is distinguishable from per-run randomness - compared
frame by frame, byte for byte, through the run store.

| plants | run length | verdict |
| --- | --- | --- |
| 1 | 240 h | byte-identical |
| 9 | 240 h | byte-identical |
| 100 | 240 h | byte-identical |
| 100 | 1600 h (400 steps) | byte-identical |

All with `Deterministic: false`, i.e. the **parallel** tick, which is the path the divergence was
attributed to. Whatever produced 188-vs-167 is either fixed or was never request-identical in the
first place - and two earlier attempts of mine to measure it were themselves not request-identical
(the `HoursPerTick` control snaps statefully; `pushSeedRaster` randomises species per position).

**What is still NOT established**: production scale. A hundred plants over four hundred steps does
exercise real branching, but it is not 4,134 plants over a thousand steps, and soil water
contention scales with the stand rather than with the run length. Treat determinism as measured
where measured and open above it.

### The memory ceiling was governing nine per cent of the problem

The tab still ran out of memory after Stage 2, so the heap was measured instead of reasoned about
(`tools/visual/heapcheck.mjs`, one 7,700-plant frame, 4,192 MB limit):

| resident | MB |
| --- | --- |
| one frame loaded and drawn | **1,148** |
| of that, the history payload the budget governs | 108 |
| the decoded scene | ~461 |
| **never comes back, even with an empty scene and no payloads** | **579** |

So `historyBudget` - 45 % of the heap, 1,886 MB - was policing the 9 % while the other 91 % was
nobody's business. 1,886 of payloads plus 1,148 for one frame on screen is 3,034 MB before a
single transient, against a 4,192 MB ceiling.

**The 45 % was correct for a client that was the only copy.** Dropping a frame lost it, so the only
alternative to holding on was stopping the run. With a run store neither is true: a dropped frame
comes back in about three milliseconds. `HISTORY_BUDGET_FRACTION_WITH_STORE` is **0.15**, applied
whenever a run id exists and reverted whenever one does not - a loaded session has no server behind
it and must still hoard.

The 579 MB that never returns is the renderer's own structures (`InstanceList.items` pools one
`Instance` per primitive and its `trim()` still has no callers; the WebGPU geometry atlas is
append-only). Not fixed here, and it is the next thing worth attacking - it is the largest single
resident item at production scale.

### Exporting a video should not need the run in memory

The tab is most likely to die at exactly the moment an export starts: a long run has just finished,
the history is at its budget, and one frame on screen already costs a gigabyte. But every frame is
in the run store and the export reads them one at a time, so the copy in memory buys nothing there.

`releaseHeldFrames()` hands them all back before the export begins. Measured on a 200-frame run:
200 payloads released, history 3.2 MB -> 0, heap peak **102 MB**, and a complete 48,600-byte
archive produced. Without a run id it does nothing - then the payloads really are the only copy.

Combined with the prefetch already added to the frame loop, an export after a crash and reload
works from the timeline that `recoverRun` rebuilt, with nothing resident at all.

### The forward transport controls flickered, and the old fix was worse than the bug

Reported as "the >>| button now blinks all the time", and correctly identified as the consequence
of removing a deliberate hack: the playback machine used to leave the playhead one index PAST the
last frame, which pinned `playPointer >= history.length - 1` permanently true and held the button
still.

The comparison is unstable while a run streams, and syncing the timeline during the run made it
much more so: the playhead follows the frames that have ARRIVED while the history spans the frames
the server has STORED, and the two chase each other. Hence the flicker.

The hack is still not the fix - an out-of-range playhead disabled the step-back controls over a
perfectly good history and made the clock report a step that did not exist. The stable statement is
the honest one: **while the tab is following the live edge, "seek to end" and "step forward" have
nothing to do**. `atLiveEdge()` in `Start.tsx` says it once, and no amount of racing between the
two counters can make it flicker.

`tools/visual/timelinecheck.mjs` counts state changes of that button while a run streams. Measured:
**0 flickers**, one state throughout.

### `null !== undefined`

`ObstacleMapping.tsx` and `LeafAreaIndex.tsx` both did
`const measured = light !== undefined && light.length > 0;`. The server sends `obstacleLight: null`
whenever a run measured no obstacles - `ObstacleLightResponse.From` returns null rather than an
empty array - so the guard passed and the next term read `.length` off null, throwing inside the
component's render. A run with no obstacles is the ordinary case, so this threw after almost every
simulation. `!= null` catches both.

Worth noting how it was found: the export harness reported it, and it looked like the export was
broken. It was not - the export had produced a valid archive. The lesson is the older one: a
console error during a render is not evidence about the thing you were testing, and `producedBytes`
is. Minified stacks map cleanly through `build/*.map`, which is faster than guessing at a
component from `c.i [as constructor]`.

### The leaf area index export was complete, and unreadable

Reported as "empty `days: []` all over the place ... shouldn't there be some data?". The file was
behaving exactly as designed - and the report was still right, because nothing in the file said so.

What was verified (`tools/visual/laiexport.mjs`, walls with plants in front, render mode 3):

| obstacles | budget | result |
| --- | --- | --- |
| 3 | under | every obstacle and every surface carries days; selection 30 days |
| 60 | over | per-obstacle and per-surface days empty, **selection series intact, 18 non-zero fields** |

So the measurement runs, the summaries are exact, and the seasonal curve of the whole selection
survives. `DailyRecordBudget` (8,000 records) drops the rest, which at 1,645 trays it always will:
one series per tray plus one per orientation is ~180 records each, so the budget is gone at 45
objects.

**Three things were wrong about how that was communicated, and one about the design.**

`meta.dailySeriesOmitted` was the only evidence, a bare boolean in a metadata block, with its
explanation living in a C# doc comment the reader never sees. The export now carries a top-level
`dailySeries` block saying in words that the arrays were dropped, why, what is still complete, and
where the rest is - and `definitions.days` says the same beside every other field definition. The
export promises to be self-describing; on the one point that confused a reader, it was not.

The HUD said "narrow the filter for per-obstacle curves", which for a 1,645-tray facade is not
advice, it is a refusal.

**And the budget was solving the wrong problem.** It exists because a browser cannot hold a hundred
megabytes of JSON arriving as one socket message - which is a fact about the WIRE, not about how
much a study is allowed to know. Stage 2 already put a run store and REST behind the app, and this
is the same shape as streaming frames: the complete measurement is now written to
`<run>/obstaclelight.json.gz` and served from `GET /Simulation/run/{id}/obstaclelight`. The export
saves the small readable document and, when it was truncated, downloads the full one beside it.

Two files rather than one merged document, deliberately: merging means parsing a hundred megabytes
of JSON in the tab that has just finished a long run - the exact allocation the budget exists to
avoid - whereas a second download streams to disk and the browser never looks inside it.

Measured at 60 obstacles over 30 days: wire copy truncated, stored file **5.8 MB, 60 obstacles all
carrying 30 days, 300 surfaces carrying days**, meta and selection present.

**One trap worth keeping.** The stored document came out **PascalCase** and parsed in the browser
to an object with no obstacles, no meta and no selection - silently, erroring nowhere. ASP.NET and
SignalR camelCase their payloads through their own serializer options; a direct
`JsonSerializer.SerializeToUtf8Bytes` with the source-generated context does not, and the context
has no naming policy. It now has a context of its own with `JsonKnownNamingPolicy.CamelCase`,
rather than a policy on the shared one, which would have renamed every wire payload the frontend
already reads. The two files must agree because the same reader opens both.

### Obstacle mapping flicker: corrected diagnosis and rendering fix (10 September 2026)

The earlier diagnosis below is superseded. Counting distinct Z coordinates across a mesh does
not measure distances between overlapping surfaces; different triangulations of the same plane
need not share any vertices. On FAV11 the measured `_T` tray backs and unmeasured `P001`–`P006`
panels occupy the same planes. Double-precision rays through the remaining artifact pixels put
the two surfaces only 3–7 nanometres apart. They are effectively coplanar, not a dense stack of
micrometre-thick objects.

`core/ObstacleMappingGeometry.ts` partitions indices into context and measured ranges, sharing
all vertex arrays and preserving source geometry and group ranges. Each face is drawn once.
Only the measured range gets a small depth preference; the previous common offset on both
ranges could not resolve their tie. Index ordering is cached by measurement membership, so
changing values or hovering does not repartition the mesh. WebGL shares BufferAttributes;
WebGPU draws two ranges from the same atlas slice rather than allocating another vertex buffer.

`tools/visual/obstacledepth.mjs` is the regression: overlapping panels with different
triangulations, five camera poses, a real occluder 2 cm nearer, changed membership, and exact
restoration after None. Run it for both backends. Full-facade verification uses the real `_T`
selection and compares interior pixels with measured-face-only reference renders.

One more correction: r152's offscreen WebGL render target defaulted to **16-bit depth**, so the
old screenshot measurements did not test the assumed 24-bit buffer. The still renderer now
explicitly requests 24-bit depth. Old cross-backend pixel counts, including 21, are historical
measurements rather than acceptance invariants for the new offscreen format.

Historical investigation (the geometric conclusions and precision claims are superseded):

Reproduced and diagnosed. The screenshots settled it: diagonal hatching and horizontal scanlines
across the trays that REORGANISE with the camera. Wrong vertex colours do not do that; a depth tie
does, because a tie is decided by whichever triangle last won the comparison.

Measured on the FAV11 facade (`tools/visual/coplanarcheck.mjs`):

| | |
| --- | --- |
| depth extent of the whole facade | **220 mm** |
| distinct Z planes within it | **36,665** |
| median spacing between adjacent planes | **1 micrometre** |
| what a 24-bit buffer resolved at 20 m with near 0.025 / far 5000 | **0.95 mm** |

Roughly a thousand times finer than the buffer could separate. `near = 0.025` against `far = 5000`
is a ratio of **200,000:1**, and depth precision is spent on the RATIO, not the distance - so the
range was throwing away about five bits for a scene that never needed to see past fifty metres.

That accounts for every part of the report: invisible while everything is white, because both
winners are the same colour; appearing the instant a mapping gives neighbouring trays different
values; "grey and blue", being two tray colours or a tray against the dark; and enormous change
from tiny camera movements. It is also why it appears in BOTH backends - reversed-Z on
`depth32float` gives about 2.4 um of relative precision at that distance, which is the same order
as the spacing.

Ruled out on the way, each by measurement rather than reading: the trays are not duplicated between
the terrain and the obstacle lists (0 of 948,409 vertices shared), and near-coincident vertices
belonging to DIFFERENT named groups are only 0.16 % of positions - so it is not duplicate geometry,
it is a dense stack of genuinely distinct surfaces.

**`CAMERA_NEAR` / `CAMERA_FAR` now live in `core/Mat4.ts`**, at 0.05 and 500, read by both backends
so they cannot drift apart. 500 m still clears a 38 m facade by an order of magnitude and 5 cm is
closer than anything is worth inspecting.

**It does not fix the flicker, and that is measured, not assumed.** Under a 0.2 % camera nudge:

| depth range | pixels changed |
| --- | --- |
| 0.05 / 500 (ratio 10,000) | **18,638** |
| 0.0377 / 754 (ratio 20,000) | 18,361 |
| `logarithmicDepthBuffer: true` | **24,790 - worse** |

Ten times the range makes no difference and a logarithmic buffer makes it a third worse, most
likely because three writes `gl_FragDepth` itself under that flag and the hardware `polygonOffset`
the obstacles rely on stops applying. It was tried, measured and **reverted**; do not reach for it
again without re-reading this table. The problem is a factor of a thousand and no depth encoding
spans that, so the remaining fix is geometric: weld or separate the surfaces at import.

**The cross-backend parity invariant moved from 665 to 21**, and this is why: shortening the range
gave both backends enough precision to resolve the fixture's coplanar surfaces the same way, so the
disagreements it used to count are gone. Stable across repeats, `maxDelta` unchanged at 188. **21
is the new figure to compare against.**

### How a harness can be blind to a violent fault

Worth recording on its own, because the first three runs of `obstaclepaint.mjs` all reported
STABLE on a scene that flickers unmistakably in front of a person.

1. **It rendered nothing.** The imported model sits nowhere near the default camera, so two blank
   images diffed to a confident `0 differing`. It now frames the model and refuses to run on a
   render with fewer than a thousand non-background pixels.
2. **It framed the whole model**, which left it a 3 % sliver of the frame - too small for a speckle
   artifact to register. It now fills the frame.
3. **It fabricated a smooth ramp of values**, so neighbouring trays got nearly identical colours
   and a flipped depth tie repainted a pixel to almost the same colour. The fault was present in
   every one of those runs and the test could not see it by construction. Fabricated data must
   make neighbours CONTRAST - alternating extremes - or it hides exactly the class of fault that
   only shows where two surfaces meet.
4. **Its input was missing and it never checked.** `npm run build` recreates the build directory,
   which deleted the model the harness fetched over HTTP - so it uploaded a 404 page as a File,
   parsed it to nothing, and reported "import produced no colour groups". From that one root cause
   came two confident and wrong diagnoses: that the server's 3.24 GB RSS made a second import fail,
   and that `logarithmicDepthBuffer` broke the WebGL context. Neither was true. The harness now
   copies the model in itself and sniffs the first bytes before uploading - and note that copying
   is not enough on its own, because **sirv reads the directory once at startup** and serves a
   later addition as 404.
5. **It launched the browser with the WebGPU flags for a WebGL run.**
   `--use-webgpu-adapter=swiftshader --enable-features=Vulkan` intermittently leaves the GL context
   reporting `maxTextureSize: 0` here. That is what actually broke the runs blamed on log depth.
   Set those flags only for WebGPU.
6. **It held the camera still.** The reported symptom is a response to camera movement, and no
   single frame contains it. A 0.2 % nudge of the camera position now runs as its own comparison,
   and that is the check that finally caught it: **18,361 pixels changed, a tenth of the painted
   facade, worst-case swing 250 of 255.**

### Obstacle mapping flicker: not reproduced, one hazard closed

Reported on real hardware in the WebGPU viewport: with an obstacle mapping on, vertices go randomly
grey and blue and flicker while orbiting; with the mapping off every obstacle is a stable white.

**It has not been reproduced here, and nothing below claims to have fixed it.**
`tools/visual/obstaclepaint.mjs` imports a real facade, fabricates a measurement over its named
groups, frames the model and then separates the two candidate causes, because they call for
opposite fixes:

- the same pose rendered twice - any difference is the DRAW being unstable;
- the same pose before and after 24 mouse moves - which is what orbiting actually does to the
  obstacle rebuild, through picking and the hover state.

Both come back byte-identical, with `obstacleRepaints` at 1 and `atlasUploads` unchanged across the
mouse moves. So the paint signature and the upload guard hold. The caveat is real, though: this is
SwiftShader with a fixed camera, and z-fighting in particular behaves differently on real hardware.

**Re-run on the real facade, filling the frame.** 1,651 named groups, 948,409 vertices, the model
covering 167,246 pixels of a 256,000-pixel render: still 0 differing on both checks, repaints 1,
uploads unchanged. And with a value fabricated for EVERY group, **zero pixels of the unmeasured
background grey** - which is the sharper test, because it does not need the fault to be unstable:
any grey at all would mean a second surface fighting for the depth, or colours written where they
do not belong.

**Leads checked and closed by reading, so they are not checked again.** The `IFlags` values do not
collide (`VertexColorsEmissive = 64`, clear of `Hidden`/`DepthBias`/`ExcludeFromGI`). The depth
bias has the right sign for reversed Z - `DEPTH_BIAS_TOWARDS_CAMERA` is +1 and `Targets.ts` derives
it from `REVERSED_Z`. And `variantFor` tests `flags & IFlags.DepthBias` as a bitmask, so the two
flags a painted obstacle gains do not knock it out of the biased pipeline variant, which would have
brought back exactly the terrain z-fighting the bias exists to cure.

**What that leaves.** The facade is ONE merged obstacle mesh, so obstacle-against-obstacle fighting
is impossible; the only coplanarity available is obstacle against TERRAIN, and the imported soils
sit inside their trays (`P001_Y026_T001` beside `P001_Y026_T001_S001`). Grey is what an unmapped
terrain surface would read as. Toggling terrain visibility off is therefore a one-checkbox test
that would settle it, and it is the first thing to try on hardware that reproduces this.

**The one hazard that does produce that exact symptom, now closed.** `GeometrySlice` recorded
`firstIndex`, `indexCount` and `baseVertex` but **not** the vertex count, while
`GeometryRegistry.register` REPLACES the mesh stored under an existing id and the atlas keeps the
slice it appended from the old one. `refreshVertexColors` then wrote
`mesh.positions.length / 3` vertices from `slice.baseVertex` with no bound - so a mesh that grew
would repaint whatever geometry the atlas appended next, in the obstacle ramp's colours. Unrelated
objects wearing grey and blue is precisely what that looks like, and it would change as the camera
brings different objects into view.

No path was found that actually re-registers an obstacle mesh at a different size - obstacle keys
carry a per-instance uid - so this is a latent hazard rather than a demonstrated cause. The slice
now carries its vertex count, the write is clamped to it, and a mismatch logs loudly instead of
corrupting silently.

**Diagnostics, so the next look is not another guess.** `window.__rendererStats()` reports
`obstacleRepaints`, `atlasUploads` and `atlasUploadedMb`. Orbiting must not move any of them:
nothing about a camera move changes a measured value, and a full atlas re-upload per mouse-move
event - 42 MB on an imported facade - is the documented cause of this exact flicker. If those
counters climb while only the camera moves, the guard has been defeated again and the numbers say
which one; if they do not, the cause is in the draw and not in the upload.

### Two traps worth remembering

**Two sources of truth for one number.** The HUD measured against the `historyBudget` signal
while the eviction check called `historyBudgetBytes()`. Nothing failed - eviction simply never
fired, because the function returns the real heap limit no matter what the signal says. It only
showed up because a test set the signal low and nothing happened.

**A stale server can silently answer for the new one.** An older `AgroServer` still held port
7215, so the replacement failed to bind, exited, and two "verified" simulation runs had actually
gone to the previous build. Kestrel says `Address already in use` and then the process dies - but
if the tests are running against something that answers, everything looks fine. Check what owns
the port, not whether the port answers. Related: `pkill -f AgroServer` matches its own command
line when the path contains that string, killing the shell that issued it.

**A parity check that reports a perfect 0 has probably not run.** `still.mjs` with `?gpu=1`
silently rendered through **three**, because headless WebGPU needs
`CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader"`
and without them the fallback chain quietly serves WebGL. The diff was `0 differing` and both PNGs
were byte-identical in size - which reads like the best possible result and is actually the
measurement not happening. **Check `cfg.resolved.kind` in the `still.mjs` output before believing
any cross-backend number.** The invariant was 665 for the whole of that work and is **21** since
the depth range was shortened.

**And do NOT set those flags for a WebGL run.** They intermittently leave the GL context reporting
`maxTextureSize: 0`, which surfaces as an `OfflineSizeError` from `renderToPixels` and reads
exactly like a renderer regression. Two measurements were wasted on that before it was noticed.

**A flag the client sets to false on load cannot be waited on.** The recovery harness waited for
`computing` to go false before sampling; after a reload it is false from the first instant, so the
loop returned immediately and sampled a run that was still going. It reported a failure that was
entirely its own. Wait on the **server's** `status`, which is the only authority on whether a run
has ended. That is the third time in this project a harness has failed to wait for the thing it
claimed to measure.

**`pkill -f "sirv build"` does not match `sirv /abs/path/build`.** Two servers ended up alive, the
older one holding the port with a stale file map, so every rebuild 404'd its own bundle and the
app loaded to a blank page. That reads exactly like a code regression and is not one.

### What Stage 1 does NOT fix

2.3x with compression is not a fix, it is a delay: it moves the crash from month 2 into month 3,
and the same run taken to maturity overruns again. Retention has to stop being unbounded, which
means the frames must live on the server. See the plan file for Stages 2 and 3.

## Scale: the production site renders 7,700 plants, and that changes the answers

Every fixture used during development held ONE plant of 80-500 primitives. The real test site
has **7,700 living plants**. Replicating the fixture's plant across the field
(`tools/visual/multiply.mjs`) gives 1,255,100 primitives, and that scene found things a single
plant never could.

One trap in that generator, since it produced a convincing wrong answer: the affine transform is
row-major 3x4, so translation lives at float indices 3 and 11 - byte 12 and byte 44 from the
start of the transform. Patching byte 0 and byte 32 instead hits `m00` and `m20`, which does not
move a plant, it **shears** it. 7,700 sheared plants still fill the frame with green and still
report exactly the right primitive count, so every capacity number came out correct while the
picture was meaningless. Only zooming in far enough to see individual plants revealed it.

### requestDevice() with no requiredLimits throws away most of the GPU

`adapter.requestDevice()` does NOT give you the adapter's capabilities. It gives you the spec's
conservative defaults. Measured on this machine:

| limit | adapter offers | default device got |
|---|---|---|
| maxStorageBufferBindingSize | 1024 MiB | **128 MiB** |
| maxBufferSize | 1024 MiB | **256 MiB** |

The instance buffer is 160 bytes per primitive, so 128 MiB caps the scene at 838,860
primitives - about **5,146 plants** at the ~163 primitives a mature plant carries. Past that,
Dawn rejects the bind group and every submit after it:

    Binding size (200816000) of [Buffer "instances"] is larger than the maximum storage buffer
    binding size (134217728). This adapter supports a higher maxStorageBufferBindingSize of
    1073741824, which can be specified in requestDevice()

The viewport fills with garbage - every pixel differed from the clear colour - and the only
trace is that async message. Confirmed empirically: 5,000 plants rendered fine at 130.4 MB,
7,700 needed 200.8 MB and failed. `Device.ts` now asks for the adapter's own maxima, which can
never be refused, and 7,700 plants renders clean.

`InstanceBuffers.upload` additionally clamps to the device limit and logs **once**, so a scene
that genuinely exceeds the hardware degrades visibly-but-explicably instead of turning to noise.
Buckets are trimmed alongside the clamp, or one would draw past the end of the buffer.

### What holds at scale, and what does not

Verified by loading real fixtures at each size, WebGPU backend:

| plants | primitives | triangles | heap | result |
|---|---|---|---|---|
| 1,000 | 163,000 | 3.9M | 209 MB | clean |
| 2,500 | 407,500 | 9.6M | 409 MB | clean, tier 3 GI correct |
| 5,000 | 815,000 | 19.3M | 811 MB | clean |
| 7,700 | 1,255,100 | 29.7M | 1,246 MB | clean *after* the limits fix |

The voxel GI grid is a fixed power-of-two dimension per tier, so the field buffer does not grow
with plant count - only injection time does. The per-bucket dispatch loop already splits over
`maxComputeWorkgroupsPerDimension`, so the 65,535 cap is not reachable. Neither is a bottleneck
that a single plant would have hidden.

**Voxel GI injection is the cost that scales, and it is the one number this hardware cannot
give.** Injection walks every triangle in the scene, up to 64 samples each, every time the field
is rebuilt - 29.7M triangles at 7,700 plants. On SwiftShader a tier-3 frame took 286 s at 2,500
plants and a tier-1 still never finished at 7,700, while the same 7,700-plant scene rendered in
32 s with GI off. Nothing failed and no limit was hit; it is purely throughput. Whether real
hardware absorbs it is the open question for this site, and answering it needs a real GPU.

**All other GPU timings here are SwiftShader, a software rasterizer.** A 320x200 frame took 29 s at
7,700 plants and a tier-3 GI frame took 286 s at 2,500. Those numbers say nothing about real
hardware and must not be quoted as frame times - they establish correctness and absence of
validation errors, nothing more.

### The retained history is the real ceiling, and no fix here touches it

`appstate.history` keeps every timestep's base64 scene string forever, with no eviction. One
timestep at 7,700 plants is 113 MB binary, **151 MB as base64**:

| timesteps | retained |
|---|---|
| 1 | 0.15 GB |
| 10 | 1.5 GB |
| 60 (ten days at 4 h/tick) | **9.1 GB** |
| 240 | 36 GB |

So a *simulation run* at this scale dies after roughly ten to twenty timesteps regardless of
how fast the renderer is. `historySize` is tracked and shown in the HUD, so the user sees a
number climbing, but nothing prevents the tab from dying. Fixing it means streaming, eviction,
or server-side retention - an architecture change, not a patch, and it is the single most
valuable thing left to do for this site.

Related: the decoded-scene cache is bounded at 200,000 primitives and refuses to cache any
single frame larger than the whole budget, so at 7,700 plants (1.26M primitives) it is inert by
design. That is the right behaviour - caching one such frame would cost roughly half a gigabyte
of JS objects - but it means the cache helps small scenes and does nothing for large ones.

### The HUD counters were O(primitives) per render, and wrong

`Start.tsx` computed "Objects" and "Triangles" with two nested reduces over the whole scene,
inline in a component subscribed to `scene` and `playPointer`. At 7,700 plants that is roughly
2.5 million iterations every time anything in the HUD redrew, to display two numbers. They are
memoised in `core/SceneStats.ts` now, keyed on the scene array's identity - the decoder hands
out a fresh array per timestep, so identity is exactly right, and a WeakMap lets superseded
scenes be collected.

The triangle numbers were also simply wrong, which the plan had flagged and nobody had acted on.
The table `[0, 12, 32, 0, 128, 0, 0, 0, 2]` was indexed by `Primitives`
(Disk, Rectangle, Cylinder, Sphere, Box) but its values were authored for the legacy V3 wire
codes (1/2/4/8). Measured against the real geometry:

| primitive | table said | actually |
|---|---|---|
| Disk | 0 | 12 |
| Rectangle (leaf) | 12 | 2 |
| Cylinder | 32 | 32 |
| Box (root) | 128 | 12 |

So leaves were overstated six-fold and root boxes ten-fold. Counts now come from
`getGeometry(id).indices.length / 3`, so they cannot drift from what is drawn again, and a
species' leaf mesh is resolved through `geometryIdFor('leaf:<name>')` - a read-only lookup, so
asking what is drawn cannot itself register a geometry. Spheres are decoded but never emitted by
BuildScene, so they count zero.

### Still unaddressed at scale

Neither of these is a small change, and both are real:

- **`Seeds.tsx` renders one `<li>` per seed, each with a `<select>` of every species, and calls
  `appstate.seeds.value.sort(...)` during render** - an in-place sort of the signal's own array.
  With a seed per plant that is 7,700 rows and a mutation of signal state from a render pass.
  Not measured: the synthetic fixture has 7,700 scene entities but one seed, so this is a
  code-reading finding, not an observed failure.
- **`InstanceBuffers.slotRefs` is rebuilt per frame**, one array push per primitive, so ~1.26M
  entries and their allocation churn every timestep. It works; it is simply on the list of things
  that would repay attention before the retained-history problem is solved.

### The three.js fallback does not scale and is not meant to

`ThreeRenderer.syncInstanceMeshes` pools meshes rather than reallocating them, but it is still
**one `THREE.Mesh`, and therefore one draw call, per primitive**. At 7,700 plants that is 1.25M
draw calls per frame against a realistic WebGL budget of five to twenty thousand. The fallback
is for machines without WebGPU and for parity diffing, and at this site it is neither usable nor
intended to be. Making it survive would mean `InstancedMesh` per (geometry, variant) bucket -
the same bucketing the WebGPU path already computes - which is real work, not a tweak.

## The static geometry is hand-written now, and three is only the oracle

`core/StaticGeometry.ts` generates all ten static geometries; `three/ThreeGeometrySource.ts`
survives solely so `tests/staticGeometry.test.ts` can compare the two **element-wise and
exactly**. Not approximately: a geometry that is merely close changes z-fighting on the
coplanar terrain/umbrella pairs and shifts flat-shaded facets, both of which surface later as a
WebGPU-vs-WebGL parity regression with no obvious cause.

Demanding exact equality paid for itself immediately - it caught two things no tolerance would
have:

1. **three truncates to f32 between the generator and the transform.**
   `new CircleGeometry(...).rotateX(-PI/2)` builds the circle in f64, stores it as f32 in
   `setAttribute`, and only then rotates. Transforming the f64 accumulator instead gives a
   different f32 answer. So `applyMatrix4` here takes a finished MeshData, never a Raw.
2. **`PolyhedronGeometry` emits each face as (b, c, a), not (a, b, c).** `subdivideFace` at
   detail 0 pushes `v[0][1], v[1][0], v[0][0]`, and the two non-`a` corners come out of
   `Vector3.lerp` with alpha 1 - that is `p + (q - p)`, which is not bit-identical to `q`.

Also worth knowing: `rotateX(-PI/2)` uses `cos(-PI/2) = 6.1e-17`, not 0, so the rotated circles
carry a tiny non-zero component a "clean" rotation would not produce; and three derives the
normal matrix by inverting and transposing the upper 3x3 even for a pure translation, then
re-normalizes. All of it is reproduced literally.

**A latent bug this exposed:** the geometry table used to be filled only by the Three adapter's
side-effect import, so the WebGPU renderer resolved geometry ids correctly only because three
happened to be loaded first. Both renderers now import the generators themselves, and
`tests/moduleGraph.test.ts` asserts a WebGPU-only load fills the table.

## Reversed-Z, and why it needs the float format to mean anything

Depth is reversed: near maps to 1, far to 0, `depthCompare: 'greater'`, clear 0. The frustum is
near 0.025 m against far 5000 m - a ratio of 200,000 - and under a standard mapping nearly every
representable depth is spent just in front of the near plane.

**The reversal alone buys nothing.** On a normalised integer format the quantisation is uniform
across [0,1], so flipping the range is a pure relabelling; the win comes from pairing it with a
float buffer, whose finest steps sit near 0, which is now the far plane where 1/z is coarsest.
That is why `DEPTH_FORMAT` moved to `depth32float` in the same change. Measured on the app's
own frustum, the smallest depth difference resolvable at 10 m improves by more than 100x.

`REVERSED_Z`, `DEPTH_FORMAT`, `DEPTH_CLEAR_VALUE`, `DEPTH_COMPARE` and
`DEPTH_BIAS_TOWARDS_CAMERA` are declared as one group in `Targets.ts` and never inlined,
because a mismatched pair does not fail loudly - it renders the scene inside out, or silently
stops fixing the z-fighting it was there to fix. The bias sign is the subtle one: three's
`polygonOffsetFactor: -2` pulls obstacles towards the camera, and "towards the camera" is
+1 under reversed Z.

Nothing else had to change. The GI and GTAO passes read **linear view depth from an r32float
gbuffer**, not the hardware depth buffer, so they are indifferent to the mapping. Parity stayed
at exactly 665 pixels and every tier-to-tier difference was unchanged to the pixel.

## The decoder was allocating two objects per float

`readFloat32` was `new Float32Array(source.slice(pos, pos + 4).buffer)` - a four-byte copy plus
a typed array to reinterpret it, on every single float. An affine transform is twelve of them
and the analytics block another eight, so a primitive cost roughly fifty allocations, and the
whole scene was redecoded on every frame of playback.

A `DataView` over the same bytes removes all of it. **16.4x faster** in isolation. In the app,
decoding a 231-frame session: median 0.8 ms -> 0.3 ms per frame, and the whole CPU step
(decode plus instance rebuild) 1.6 ms -> 1.1 ms.

Pass `byteOffset` and `byteLength` when constructing the DataView. `src` is often a view into a
larger buffer, and a DataView over `src.buffer` alone reads from the wrong place - it is the one
way to get this wrong that still passes every other test, so `tests/decodeParity.test.ts` checks
it explicitly against a copy of the old decoder kept as an oracle.

**The plan's suggested base64 micro-optimisation is wrong.** `Uint8Array.from(atob(s), c =>
c.charCodeAt(0))` is not 3x faster than the per-char loop, it is **32x slower** - the per-byte
callback defeats it. Measured: loop 0.024 ms, `Uint8Array.from` 0.771 ms, `Buffer.from` 0.008 ms
on the same payload. `Buffer.from` wins but is Node-only. The loop stays.

## Playback is not decode-bound at these scene sizes

With the CPU step at ~1 ms, the renderer can produce far more steps per second than the display
consumes; stepping the playhead is pinned to the animation-frame cadence, not to the decode. The
decode work matters for the large canopies the plan anticipated (tens of thousands of
primitives), not for the plants these fixtures actually grow.

Which is worth stating plainly: **the fixtures grow almost nothing by default.** The frontend
sends no `InitialDate` unless one is set, so the backend starts on 1 February, and a February
sun at 48 degrees north produces a plant of about 2e-6 cubic metres over 960 simulated hours.
`tools/visual/makefixture.mjs` sets a May start date for exactly this reason - without it, any
performance fixture is a picture of bare soil.

## Two harness lessons from this round

**`sirv` caches its file listing at boot.** Every rebuild produces new content-hashed bundle
names, so the server 404s them and the page loads empty - which presents as "the app has no
buttons any more" and looks exactly like a code regression. Restart the server after every
build.

**A still whose lighting silently did not apply is worse than no still.** A run that captured
before the lighting signals propagated produced a tier-0 image labelled tier 1, and the
resulting 25 % "difference" between two adjacent tiers looked precisely like a renderer
regression. `still.mjs` now reads back what the renderer actually resolved and refuses to shoot
otherwise, which needs `?debughook=1` on the URL. One carve-out: shadows are sun shadows, so
with no sun above the horizon they resolve off whatever the checkbox says.

**`NOPLANTS=1` is not the empty-field flag.** It hides leaves, roots and seeds - the stems stay,
and stems are injected into the density field, so tier 3 legitimately differs from tier 2 by a
handful of pixels there. `SKIPLOAD=1` is the flag that gives a genuinely empty grid.


## PNG is encoded by hand, not by canvas.toBlob

`canvas.toBlob` was the original PNG path and it produced **blank images on a real machine**
with no error anywhere: the readback was verified good by `warnIfFlat`, the WebM export of the
very same pixels was correct, and the PNG still came out empty. A 2D canvas in Chromium is a
GPU-backed surface, and on a machine already under GPU pressure it can hand back a blank
without failing - there is nothing to catch.

`PngEncoder.ts` writes the file directly: IHDR/IDAT/IEND, filter type 0 per scanline, deflate
via `CompressionStream` (which is the zlib wrapper PNG wants), CRC-32 borrowed from the ZIP
writer. **Colour type 2, so no alpha channel** - these exports are opaque and an alpha channel
is only somewhere for a bug to hide. It is also smaller: the fixture's frame sequence went from
15 kB to 6 kB.

JPEG still goes through the canvas, being the only lossy encoder available without a
dependency - and a blank JPEG would at least be reproducible here.

`encodePngBytes` is split from `encodePng` so the encoding can be tested without a Blob at all;
jsdom's Blob has neither `.stream()` nor `.arrayBuffer()`.

## Offline readbacks are forced opaque

`PixelReadback` promises straight alpha at 255 and both backends already render opaque, but it
is enforced rather than trusted (`offline/opaque.ts`). The failure it guards is silent and
total: a PNG whose RGB is right but whose alpha is zero opens as a blank transparent image and
nothing in the pipeline flags it. `reportOpaque` warns once if the correction ever fires, so a
"my export is blank" report can be answered rather than guessed at. There is no use for alpha
in these exports.

## The prepass belongs to everything that reads the gbuffer, not just occlusion

`renderOcclusion` renders the depth+normal gbuffer AND runs the horizon search, and it was
gated entirely on `ambientOcclusion`. But the cone trace reconstructs a world position per
pixel from that gbuffer, and the sun is traced from that position - so **shadows need it too**.

With occlusion off, the gbuffer therefore held whatever camera last wrote it. Shadows belonged
to a previous view: absent at one zoom level, correct after zooming in one step, still correct
after zooming back out, and "fixed" by toggling occlusion, which happened to write the buffer
once. `needsPrepass()` is now `occlusion || indirect || shadows`, and only the horizon search
is still gated on occlusion.

`renderToPixels` had the identical gate on allocating its `OcclusionTargets`, so a
shadows-only export silently fell back to the CANVAS set - a different resolution, filled for
a different camera. Same one-line cause, two places; whenever a gate like this is fixed, grep
for the condition rather than assuming the interactive path was the only copy.

## Everything the prepass reads must be written BEFORE the prepass

The depth+normal prepass draws through `frameBindBasic`, which is the shared frame uniform. It
used to run before `writeFrameUniform`, `atlas.flush()` and `rebindInstances()`, so it rendered
from whatever was last written:

- right after a hover, the **1x1 skewed gluPickMatrix** the pick pass leaves behind - so
  shadows and occlusion appeared to follow the cursor, and disabling picking "fixed" it;
- otherwise the **previous frame's camera** - so they lagged one frame, looked frozen while
  orbiting or zooming, and snapped right when the animation advanced;
- with stale instance buffers, which is where phantom terrain-coloured leaves came from.

`prepareFrame` now does flush, rebind, write-uniform and field-build in that order, and **both
the on-screen and the offline paths call it**. The offline one happened to be correct already,
which is exactly why every offscreen measurement in this harness looked right while the
viewport was visibly wrong - `renderToPixels` writes its own uniform before the prepass, so no
still could ever reproduce it. `pick` also restores the camera's matrices after recording its
pass, as defence in depth.

**A bug only reachable from the interactive path will not show up in an offscreen still.** When
a report is about moving the mouse or the camera, the harness is not evidence.

## renderOnce is coalesced to one render per animation frame

Several effects each ask for a render in the same tick. During a simulation every timestep
does: preview arrives -> plants rebuild -> `renderOnce` -> a setTimeout advances the playhead
-> the lighting effect re-runs (the sun follows the clock) -> `setLighting` -> `renderOnce`
again. **Two full scene passes per timestep**, each of which also redraws the 4096 shadow map
over the whole canopy - and the cost grows with the plant.

Chromium on Vulkan reported the result as `THREE.WebGLProgram: Shader Error 1282 -
VALIDATE_STATUS false` with an **empty program info log**, immediately followed by
`CONTEXT_LOST_WEBGL`. An empty log is the tell: nothing is wrong with the shader source, the
driver simply could not link any more. Firefox on the same scene was fine.

Both renderers now queue the render on `requestAnimationFrame` and drop duplicate requests.
Nothing needed a synchronous render: picking uses a raycaster (Three) or its own 1x1 pass
(WebGPU), and both offline paths render into their own targets. `dispose` cancels the pending
frame.

The same episode had `setLighting` traversing every mesh in the scene on every timestep to
reapply shadow flags. Those flags live on the Mesh and every rebuild makes new ones, so they
are now set inside the pooling loop that creates them, and the traverse only runs when shadows
actually turn on or off.

**When a per-frame cost is added, check what else already runs per frame.** Making the sun
follow the clock was correct; it also silently doubled the frame's work, because the thing it
newly triggered each frame ended in a render.

## Diagnosing "WebGPU fails immediately"

A lost device reports almost nothing, and what it does report is easy to throw away. Two
things now make such a report actionable:

- `Device.ts` logs `info.reason` and `info.message` at the point the promise settles. The
  message is not recoverable afterwards, so a fallback handler that only says "falling back"
  destroys the only evidence there was.
- `WebGPURenderer` tracks a `mountStage` label and names it if the device dies during mount.
  "Immediately" is otherwise indistinguishable between an unsupported format, a shader the
  driver rejects, and a context that never configured.

**Pipelines for tiers above the default are built on first use, not at mount.** Creating a
pipeline compiles its shader in the driver, and mount had grown from three shader modules to
seven - so a driver that chokes on any one of the new ones took down the whole viewport at
startup, including the parity tier that never runs them. Built lazily, a bad shader can only
break the tier that asks for it, and which tier that is identifies the shader. Bind group
LAYOUTS stay eager: they compile nothing, and the resize path needs them before a tier is
chosen.

## An export resolves its own lighting, per frame

The offline paths must not inherit the renderer's current `FrameLighting`. It was resolved for
wherever the playhead happens to sit, and an export deliberately never moves the playhead - so
leaving the playhead at the start of a run, the usual place, put the sun at midnight of the
start date, below the horizon, and **every frame of an exported video came out ambient-only
however the lighting switches were set**. The still export was unaffected, which is what made
it look like a video-specific problem.

It also has to be resolved per FRAME, not once per export: a video spanning months of
simulated time would otherwise be lit by a single instant of it. `lightingFor(historyIndex)`
in `OfflineRenderer.ts` does this, and the run restores the viewport's own lighting at the end
alongside its scene.

Check it with `TIER=0` and `TIER=1` runs of `animate.mjs` and a diff of the extracted frames:
they must differ by roughly what the viewport differs by (~25 % of pixels for the sun).

## The leaf shading constants, and what they trade against each other

Three constants in `mesh.wgsl.ts` control how foliage reads, and they are not independent:

- `LEAF_TRANSMISSION` - how much sun passes through to the shaded face.
- `LEAF_WRAP` - how far past the terminator the lit face keeps receiving light. This is the
  subsurface-scattering stand-in.
- `LEAF_ROUGHNESS` - Oren-Nayar sigma. A leaf is not smooth at the microscopic scale: Geranium
  and Heuchera carry fine hairs that scatter light back towards the viewer and flatten the
  falloff further, which is what reads as "matte" rather than "moulded plastic".

**Raising wrap and transmission makes leaves smoother and less normal-dependent, and in the
same motion makes indirect light harder to see** - a bounce is only visible against something
darker. On the fixture, going from wrap 0.5 / transmission 0.25 to 0.7 / 0.4 took the
indirect-light difference from 2.94 % of pixels down to 0.18 %. That is the trade, not a
regression to fix by tuning one of them alone.

They are per-renderer constants, not per-species. Real foliage varies - Bergenia and Campanula
are glossy where Geranium is not - and doing that properly means a specular term and material
parameters travelling with the species from the server, which is a data-model change rather
than a shader one.

**They live in `core/Lighting.ts` now**, not in `mesh.wgsl.ts`, and both backends interpolate
them into their own shader source (`${f32lit(...)}`, four decimals, because `String(1)` is an
int literal in both WGSL and GLSL). The WebGL path grew the same `leafSun()` the WGSL one has,
so a single edit can no longer move one backend without the other.

## Leaf translucency: one term, evaluated on both faces

**A canopy seen from below was nearly black, and the sun term was never going to fix it.**
Measured on the fixture, looking up into the crown: leaf undersides rendered at RGB (23,74,21)
at the default lighting and (1,3,0) with daylight and a sun. That is the complaint, quantified.

The reason is not the sun. Both backends turn a double-sided surface's normal towards the
VIEWER before shading it, so the hemisphere term - `mix(ground, sky, 0.5*dot(n, up) + 0.5)` -
hands an underside the GROUND colour with none of the sky: `0.514` against `3.175`, a sixth of
what its own topside receives. And the default lighting has **no sun at all**, so
`LEAF_TRANSMISSION`, which only ever multiplied the direct sun and only on WebGPU, had nothing
to act on. The term that needed to learn about translucency was the ambient one.

The model is one function, applied per light, on both backends:

```
E = here + t * max(there - here, 0)
```

`here` is the light as the face being shaded receives it, `there` the same expression with the
normal reversed. **The brighter face is never touched**, so a sunlit crown renders exactly as
it did and only the shaded side lifts - verified, not assumed: at the default lighting not one
pixel of either backend gets darker, on any camera.

- Hemisphere: `getHemisphereLightIrradiance` evaluated at `n` and at `-n`. On WebGL it is
  evaluated from the light rather than mirrored out of `irradiance`, which also carries the
  ambient light and the light probe.
- Sun: `leafSun(raw)` and `leafSun(-raw)`, the wrap + Oren-Nayar + transmission response.
- Bounce: the cone trace is a screen-space lookup resolved from the single viewer-facing
  prepass normal, so there is no far-side value to bleed from. It is added once, outside the
  term. A two-sided bounce means a second set of cones for a contribution this small.

`t` is zero unless the surface is foliage AND the switch is on, and every term it adds is
multiplied by it, so off is bit-identical rather than merely close.

### It is a switch, and it is off by default

Unlike shadows, occlusion and indirect light, this term is **live with no sun**, because it
modulates the hemisphere ambient - which is the only light the viewport has until something
else is ticked. It therefore cannot be "a term that vanishes at Tier 0"; the only way to keep
the historical look byte-identical was to make it opt-in. `resolveLighting` counts it in
`anything` (or ticking it alone would do nothing) and forces it to zero under a data mapping,
like every other term.

It rides in `frame.params.x` on WebGPU. That slot and `params.y` described the shadow map,
which neither backend has any more, and nothing read them - so the frame uniform did not have
to grow, and neither packing trap applies.

### `DoubleSided` is not `Foliage`

`IFlags.DoubleSided` means "leaves, legacy disks, **terrain, obstacles**", and the leaf BSDF
was selected on it - so a wall and the ground were already being shaded as foliage. Invisible
while the leaf model only touched the sun, since the default has none; not invisible once the
ambient learns to bleed, because a wall would glow from its shaded side.

`IFlags.Foliage` (bit 256) is set in `BuildScene` on the `Primitives.Rectangle` case alone,
which is where leaves and petals both arrive. Terrain and obstacles build their instances in
the backends and never get it.

**This changes existing output in exactly one place**, and it is the only such change:
WebGPU at a sun tier, where terrain moves off the wrapped leaf response onto `max(dot, 0)` and
goes from RGB (156,98,0) to (137,86,0). Measured: 63968 differing pixels, of which the terrain
block alone is 63875. Foliage pixels are unchanged in count and in value.

### What was measured

Before/after builds of the same tree, `still.mjs`, 640x400 SSAA 2, the standard fixture.

| | |
| --- | --- |
| Switch off, no sun, both backends, default and close-up cameras | **byte-identical PNGs** |
| Switch off, sun, WebGL | byte-identical |
| Switch off, sun, WebGPU | terrain only, see above |
| Switch on, default lighting, looking up | WebGPU (23,74,21) -> (45,126,42); WebGL lifts the same 59.6k pixels |
| Switch on, daylight + sun, looking up | WebGL (1,3,0) -> (10,74,7) |
| Pixels darkened by the bleed, any camera, any backend | **0** |

Roughly 23 % of the frame changes on a close-up from below, mean delta 31-44/255. Compare the
4/255 that an earlier AO attempt moved and that nobody could see.

**One caveat, and it is not the bleed.** On WebGL the switch also swaps `saturate(dot(n, L))`
for `leafSun`, because that path never had a leaf sun response - which is the whole reason its
undersides were worse than WebGPU's. The wrap compresses a fully sunlit face, so leaf TOPSIDES
darken by up to 24/255 (mean 4.6) when the switch goes on there. That is the documented
leaf-versus-sheet-metal trade arriving on WebGL, not a fault in the term, and it is what makes
the two backends agree. WebGPU, which already had `leafSun`, darkens nothing.

### The occlusion normal was never flipped

Found while checking this. `vDensityNormal` is the un-flipped object normal, so on WebGL a
leaf's UNDERSIDE was occlusion-tested against the hemisphere ABOVE it: it inherited its own
topside's occlusion and went dark a second time, for a reason unrelated to where it actually
sits in the canopy. It is flipped by `faceDirection` under `#ifdef DOUBLE_SIDED` now, the same
condition three itself uses. Only visible with occlusion on.

## A bare cosine makes a leaf look like sheet metal

Two successive versions of the leaf sun term were wrong in instructive ways.

`abs(dot(n, L))` handed the shaded face the full sun as though it were reflecting, so every
leaf in a crown read the same brightness whichever way it faced and indirect light had nothing
to contribute - nothing was dark enough for a bounce to matter.

Splitting it into `max(raw, 0)` reflection plus `max(-raw, 0) * T` transmission fixed the
brightness but introduced a **crease**: both terms reach zero at `dot(n, L) == 0`, so a leaf
whose triangles straddle the terminator gets a dark band straight through it and its faceting
becomes glaring. A bare cosine also changes fastest exactly there.

The model is now **wrap-around diffuse on both faces**: `(raw + w) / (1 + w)` clamped, with
`w = LEAF_WRAP`. Light entering a thin translucent surface scatters inside before leaving, so
the real falloff towards the terminator is much softer than a cosine; wrapping is the standard
cheap stand-in. Nothing reaches zero at the terminator, and the response to a normal is
compressed - which is most of the visual difference between a leaf and a sheet of metal.

Measured effect on the fixture: indirect light went from 0.18 % of pixels to **2.94 %**, purely
because leaves stopped being uniformly bright.

It is not a full subsurface model - no thickness, no lateral bleed within a leaf - but for
geometry one voxel thick that is most of the effect.

## Leaves reflect AND transmit; abs(dot(n, L)) is not a lighting model

The sun term used to be `abs(dot(n, L))` for double-sided surfaces, so a leaf's shaded face
received the full sun as though it were reflecting. Every leaf in a crown then read the same
brightness whichever way it faced, the canopy looked flat, and **indirect light had nothing to
contribute because nothing was dark enough for a bounce to matter**.

It is now `max(raw, 0) + max(-raw, 0) * LEAF_TRANSMISSION`: reflection off the sunlit face,
transmission through to the shaded one. Foliage passes roughly a fifth to a quarter of what
lands on it. The shaded side is still never zero, so the reason `abs()` was there - a canopy
going black in patches - does not come back.

Tier 0 is unaffected: `sunColor` is black there, so the whole sun term vanishes regardless.

This used to widen the gap between the backends at the sun tiers, because three's double-sided
lighting had no transmission term at all. **The translucency switch closes it** - see below -
but only when it is on, because off has to stay byte-identical to what each backend rendered
before, and what they rendered before was different.

## Never offset by a normal flipped with a sign test

`traceSun` offset its start along the surface normal "turned towards the sun":
`n * select(-1, 1, dot(n, dir) >= 0)`. That is a **discontinuity at dot(n, dir) == 0**, and
with a low sun that is exactly where leaf triangles sit. Neighbouring triangles of one leaf
started their march two cells apart on OPPOSITE sides, so one came back shadowed and its
neighbour lit - a faceted, creepy look on young leaves - and as the sun moved an hour, whole
groups crossed the zero together and the shadow pattern jumped instead of sweeping.

Marching along the sun direction alone has no such discontinuity and still clears the origin
cell, which is all the offset was for. **Any `select` on the sign of a dot product is a
discontinuity waiting for the geometry to line up with it.**

## WebGPU has no shadow map: the sun is traced through the density field

Shadow maps are the wrong instrument for foliage, and no amount of bias tuning fixes it. A leaf
is a single **zero-thickness quad**, so the caster and the receiver are the SAME triangle: the
depth comparison is a tie, only a bias can break it, and the leaf ends up wearing a leaf-shaped
shadow of itself. Any bias large enough to stop that also leaks light between leaves that
genuinely touch. It is made worse by the double-sided rule - a back-lit leaf is deliberately
lit by `abs(dot(n, L))` while the map correctly reports its own front face occluding it.

`traceSun` in `coneTrace.wgsl.ts` replaces it. There is no comparison at all: the field says how
much leaf area lies between the point and the sun, and Beer-Lambert turns that into a fraction.
**Self-shadowing cannot happen by construction**, the softness is the canopy's rather than an
artifact, and it is the same law the simulation's own light transport uses - so a shadow means
what a shadow means to the model underneath.

- The cone trace's alpha channel carries **sun transmittance**, not sky visibility. Sky
  occlusion comes from the screen-space AO pass, which resolves finer than an 8 cm voxel can,
  so the channel is better spent on what the voxels are uniquely good at.
- The five diffuse cones are gated on the indirect switch; shadows alone pay only for the one
  sun cone.
- Enabling shadows therefore builds the density field. Terrain is excluded from it
  (`IFlags.ExcludeFromGI`), so the ground neither self-shadows nor casts - correct for a ground
  plane, and it is plants shadowing the ground that matters.
- The 4096 shadow map, its pass and its pipeline are **gone** on WebGPU: 67 MB and one full
  scene pass per frame recovered, and one less shader compiled at mount.
- Voxel cells are lit as if unoccluded, because a cell cannot sample the field it is being
  resolved into. That slightly overestimates the bounce deep in a dense canopy; the cone trace
  still attenuates whatever reaches the eye.

**The Three/WebGL path keeps its shadow map** - there is no density field there - so it keeps
the bias control and the artifact it implies. WebGPU is the definitive fix.

## Shadow bias on double-sided leaves (WebGL only)

A leaf is a single thin quad, so a leaf that shadows itself is comparing a surface against
**itself** in the shadow map. No depth bias can fix that: there is no separation between
caster and receiver to bias across. The symptom is a leaf-shaped shadow on the very leaf that
cast it, with nothing above it.

It is made worse by the double-sided lighting rule. A back-lit leaf is lit by `abs(dot(n, L))`
- deliberately, so a canopy does not go black in patches - but the shadow map correctly reports
that the leaf's own front face occludes its back. Lit and shadowed at once.

The fix is a **normal-offset bias**: move the shadow lookup off the surface along its own
normal, flipped to face the sun so a back-lit leaf samples from its lit side.

**three's `normalBias` does NOT flip the normal towards the light.** For a back-lit leaf it
therefore offsets the lookup DEEPER into shadow and makes the artifact worse - so on the Three
path the same texel count also drives `shadow.bias`, a plain depth bias that has no direction
to get wrong. The WebGPU path flips the normal itself and needs no equivalent. Expressed in
shadow TEXELS, not metres, so it stays correct if the field size or map resolution changes,
and scaled up at grazing incidence where a texel covers more surface. `shadow.normalBias` is
three's name for the same thing, and the Three path derives it from the same constants.

The amount is **tunable at runtime** (`shadowBias` in the lighting panel) because the right
value is not predictable from the scene: too little and a leaf shadows itself, too much and
light leaks between leaves that genuinely touch. Both failures are obvious on screen.

**The fixture does not exhibit the artifact**, which cost a lot of time to establish. Verifying
a shadow change needs a view where a shadow actually lands: in the saved session the plant sits
at the terrain's corner and the sun throws its shadow off-field entirely, so changing the bias
from 0 to 400 texels produced a byte-identical image. Use `CAMERA=` on `still.mjs` for a
close-up, and confirm the shadow path is live before concluding anything about it - forcing
`sunVisibility` to return 0 should darken the image, and over-biasing the comparison by +0.01
should darken it slightly. Both are one-line edits and both were needed here.

## The frame uniform's member ORDER must match the CPU's packing

Worse than the size mismatch below, because it is completely silent: no validation error, no
warning, just members reading each other's floats.

`sunDirWorld` and `shadowParams` were added to `struct Frame` **in front of** `projParams`,
while `writeFrameUniform` appended them **after** it. The ambient-occlusion lookup then divided
the fragment coordinate by `projParams.zw`, which was actually
`(SHADOW_GRAZING_OFFSET_TEXELS, depthBias)` = `(4, 0.0005)` - so every geometry pixel produced
a UV far past 1, sampled the AO texture's clamped corner, and got 1.0.

**Occlusion, indirect light and the shadow bias all went dead at once, and each looked like an
unrelated bug in its own pass.** Days went into probing the GTAO compute shader, the prepass,
the normal encoding and the horizon search - all of which were correct the whole time. The
tell, when it finally came, was rendering `projParams.z` straight to the screen and decoding
the pixel: 4.2, which is a shadow constant, not a resolution.

`FRAME_MEMBERS` in `WebGPURenderer.ts` is the canonical order and
`tests/webgpuBuffers.test.ts` compares it against the parsed WGSL struct. Verified to fail on
the real bug.

**When several effects stop working simultaneously, suspect what they SHARE** - here, one
uniform - before investigating each of them separately.

## The frame uniform must grow with the WGSL struct

Adding a field to `struct Frame` without growing `FRAME_FLOATS` is caught only at draw time,
as `[Buffer "frame-uniform"] bound with size N ... is too small`, naming the PIPELINE rather
than the field that was added - and the viewport just stops drawing.
`tests/webgpuBuffers.test.ts` counts the struct's declared members and compares.

## The second bounce and occupied-cell compaction (Tier 4)

- **Compaction turns the second bounce from impossible into trivial.** A 128 grid is 2,097,152
  cells and a canopy occupies a few thousand; the resolve pass records occupied cells into a
  list with an atomic counter, a one-invocation pass turns the count into indirect dispatch
  arguments, and the bounce dispatches over the list. Measured on the fixture: 59 cells, i.e.
  a 35,000x reduction in dispatched work. The count is only known on the GPU, so reading it
  back to size a dispatch would stall the frame it belongs to - hence
  `dispatchWorkgroupsIndirect`.
- **A pass cannot sample the storage texture it writes**, so the bounce reads the first-bounce
  texture and writes a second. The destination is seeded with `copyTextureToTexture` first, or
  the cells the pass skips - the empty majority - read as uninitialised. Both textures need
  `COPY_SRC`/`COPY_DST` accordingly, and the debug readback needs `COPY_SRC` on whichever is
  final.
- **The bounce is strictly additive**, which is the property worth asserting: a cell can only
  gain radiance, so the tier can never darken anything and turning it off returns exactly to
  the tier below. Verified as 48 pixels changed, all brighter, none darker.
- **Measure a bounce in the FIELD, not on screen.** Screen pixels only see the fraction of the
  frame the canopy covers. The coarsest mip is one texel and, because the chain is an
  arithmetic mean of a sigma-premultiplied quantity, that texel is exactly the volume average.
  `tools/visual/bouncecheck.mjs` reads it at both tiers and reports the ratio.
- **The magnitude tracks canopy density, and the fixture has almost none.** Measured +2.1 % on
  the saved fixture (59 occupied cells), +1.8 % on 3,466, +3.1 % on 6,616 - doubling occupancy
  nearly doubles the gain. The plan's 15-20 % figure assumes a closed canopy where a leaf's
  neighbours are most of what it sees; at LAD around 5 a cone is only ~11 % absorbed per cell,
  so a few percent is the arithmetically correct answer for an open canopy, not a weak
  implementation.

## Duplicated builders: check which one is live

`BuildScene.buildTerrain` was the neutral terrain builder the refactor left behind. **Nothing
called it** - both backends build terrain themselves (`ThreeRenderer.rebuildTerrain`,
`WebGPURenderer.appendTerrain`) - and it had passing unit tests the whole time, which is
exactly what made it invisible. A change to terrain flags landed there and silently did
nothing for a full build-and-measure cycle. It and its tests are gone; the comment left in its
place says so. Before editing a builder, grep for its callers.

## WGSL delivery traps

- **A backtick anywhere in a shader ends the TypeScript template literal.** WGSL itself
  contains neither a backtick nor `${`, which is what makes this delivery mechanism work - but
  a backtick in a *comment* inside the shader breaks it, and the parse errors point at the
  wrong place entirely. `tests/shaders.test.ts` guards this.
- **`target` is a reserved keyword in WGSL** and Tint rejects the whole module for it, with an
  error naming a line number in a string that has no line numbers in the editor. Same test
  covers a list of the reserved words that read like natural variable names.
- **A pipeline must declare a depth state matching the pass it joins.** An overlay that neither
  tests nor writes depth still needs `depthStencil` with `depthCompare: 'always'`, or Dawn
  rejects it as attachment-incompatible.

## The minifier trap, part two: guarded calls

Already documented for bare `appstate.x.value;` statements. There is a second mechanism, and
`no-unused-expressions` does not catch it:

```js
// source
useSignalEffect(() => {
    rendererRef.current?.setVoxelDebug?.({ show: appstate.showVoxels.value, ... });
});
// terser output - the reads are now INSIDE the guarded call
null===(e=n.current)||void 0===e||null===(t=e.setVoxelDebug)||void 0===t||
  t.call(e, {show: X.c.showVoxels.value, ...})
```

The renderer mounts asynchronously, so on the effect's **first** run the guard short-circuits
before any `.value` is evaluated - the effect subscribes to nothing and never fires again. The
control silently stops working, in production builds only.

Wrapping the reads in a helper function is **not** sufficient: terser inlines a small enough
helper, as it did to `visibilityInputs`. What it cannot move is an observable property write,
so the effects assign into a `latest` ref first and pass that value in. `refreshVisibility`
was already broken this way before anyone noticed - the showTerrain/showSeeds/showObstacles
toggles were not reacting in production builds.

**This is now checked automatically.** `tests/bundleSubscriptions.test.ts` reads the
production bundle and fails if a `.value` read sits inside a `rendererRef.current?.…` guard.
It skips when there is no `build/`, so run `npm run build` before `npm test` to get the check.
Verified by reintroducing the bug and watching it fail.

Two spellings that look fine and are not:

- `(appstate.playPointer.value, appstate.currentTimestep())` - reading a signal purely to
  subscribe, as the discarded operand of a comma expression. Terser deletes it as dead code.
  This froze the sun at whatever time the tier was last changed. Pass the value into the call
  instead (`appstate.timestepAt(appstate.playPointer.value)`), so it is genuinely used.
- Wrapping the reads in a helper function. Terser inlines a small enough helper; it did
  exactly that to `visibilityInputs`. Only an observable property write cannot be moved.

## Lost wakeups on non-signal state

`hubConnection.state` is a plain field, so an effect that consults it cannot be woken when it
changes. The preview loop did:

```js
if (st.previewRequest.value && hubConnection.state == Connected) { invoke("preview"); … }
```

A request made before the socket came up was dropped - and because `previewRequest` was
already `true`, setting it `true` again was not a change and did not re-run the effect
either. The loop then stalled for the rest of the session: the simulation ran to completion
server-side while the viewport only updated when the final `result` arrived. Whether the race
was lost came down to how fast the browser opened the WebSocket, which is why it showed up in
Firefox and not in Chromium.

`hubConnected` is now a signal, mirrored from `start()`, `onclose`, `onreconnecting` and
`onreconnected`. **Any non-signal state an effect branches on is a lost wakeup waiting to
happen**, and a level-triggered boolean request makes it permanent rather than transient.

## three.js: shadows need a material recompile

three compiles shadow support INTO each material's program. Flipping
`renderer.shadowMap.enabled`, or a mesh's `receiveShadow`, after a program is cached does
nothing until that program is rebuilt - so switching lighting tiers appeared to do nothing
until the next timestep, when `buildPlants` happened to create new materials. Set
`material.needsUpdate = true` on a real change (not on every reapply - recompiling every
material is not free).

`castShadow`/`receiveShadow` also live on the **Mesh**, and every rebuild makes new ones, so
the flags have to be reapplied after each rebuild or a plant that grew after shadows were
enabled silently stops casting.

## WebGPU render-target memory

The two biggest allocations - the 4096 shadow map (67 MB) and the full-resolution occlusion
gbuffer - used to be created unconditionally, on the **default tier**, which uses neither.
With the MSAA colour and depth buffers that came to roughly half a gigabyte at 4K. When that
does not fit, the GPU process dies: there is **no JavaScript error at all**, and the canvas is
left showing whatever was in the memory it lost, often other windows' pixels.

- `Targets.setLightingNeeds(shadows, occlusion)` sizes both to the tier and returns whether
  anything moved, so the caller can rebuild its bind groups. Unused ones drop to 1x1 rather
  than being freed, because the bind groups still need a view.
- The prepass writes `rg16float` (octahedral normal) + `r32float` (linear depth), 8 bytes per
  pixel, rather than one `rgba32float` at 16. Depth must stay 32-bit; the normal never needed
  to be.
- The renderer logs its render-target total on every change. That number is the only thing
  that makes a "it just crashed" report diagnosable after the fact.
- Every `dispatchWorkgroups` dimension is capped at `maxComputeWorkgroupsPerDimension`
  (65535). A mature canopy can put more instances than that in one bucket, and exceeding it
  invalidates the whole command buffer.


## Backend behaviours worth knowing

- **The backend runs without docker**: `cd AgroServer && dotnet build -c Release && dotnet run
  -c Release --no-build --urls "http://127.0.0.1:7215"`. It needs .NET 8.
- **CORS is pinned to `http://localhost:8080`** (`AGRO_HOSTNAME`, defaulting in
  `AgroServer/Program.cs`). Serving the frontend from `127.0.0.1` or any other port fails every
  fetch and the SignalR negotiate, which looks like the backend being down.

- **`ExactPreview` deadlocks.** The hub loop
  (`AgroServer/Hubs/Simulation.cs`) only advances when `requests.Preview` is
  set, and that call never arrives. Leave the checkbox off.
- The renderer's rebuild rate **paces the simulation stream**: `buildPlants`
  re-arms `previewRequest`, and the server emits a frame only when asked.
  `appstate.offlineRendering` suppresses that so an export cannot perturb it.
- `hubConnection.on("reject", …)` never fires — the server method is
  `Rejected()`. Name mismatch, still unfixed.
- Scene payloads travel as base64 inside the SignalR JSON protocol, ~33 %
  overhead. `getScene`/`decodeSceneAt` re-parses from base64 on every frame
  change, with no cache; this dominates playback once rendering is cheap.

## Test environment quirks

- jsdom under jest 27 lacks `TextDecoder` (`BinaryReader` builds one at module
  scope) and `Blob.arrayBuffer()`. Polyfilled in `tests/__mocks__/browserMocks.ts`;
  expose raw bytes from writers rather than going through Blob.
- enzyme's dependency chain does not load here; `setupTests.ts` fails soft so
  one broken import cannot abort the whole suite during setup.
- **TypeScript parameter properties** (`constructor(private x)`) do not parse
  under the jest babel config, though webpack accepts them. Use explicit fields
  in anything unit-tested.
- Chromium is a **snap** and cannot read `/tmp`. Test fixtures must live under
  `$HOME`, and killing the snap wrapper process raises `EACCES` — shut the
  browser down over CDP (`Browser.close`) instead.
- Headless needs `--use-angle=swiftshader` or there is no WebGL2 context.
- **WebGPU is available in headless here, but only with the right flags.** The
  default set exposes no `navigator.gpu` at all. This works, via SwiftShader's
  Vulkan backend (slow but correct, which is what parity testing needs):

      CHROME_EXTRA_FLAGS="--enable-unsafe-webgpu --enable-features=Vulkan --use-webgpu-adapter=swiftshader"

  Confirm with `node tools/visual/gpuprobe.mjs URL`. Observed limits:
  `maxTextureDimension2D` 8192, `maxStorageBuffersPerShaderStage` 8,
  preferred canvas format `rgba8unorm`.
- File choosers and downloads require a **trusted** click
  (`Input.dispatchMouseEvent`); a script-initiated `.click()` carries no user
  activation and is silently ignored.

## Frontend code map

| Path | Role |
| --- | --- |
| `src/appstate.ts` | signals singleton, SignalR wiring, frame history, playback FSM |
| `src/render/RendererHost.tsx` | backend selection, mounting, all signal effects |
| `src/render/core/PlantColors.ts` | per-organ colour; `VisualMappingOptions` lives here |
| `src/render/core/PrimitiveTransform.ts` | the wire transform decode, incl. the post-scale half-length recentring |
| `src/render/core/BuildScene.ts` | simulation data → neutral draw list |
| `src/render/three/ThreeRenderer.ts` | the WebGL backend |
| `src/render/offline/` | offscreen stills, frame loop, ZIP/WebM encoders |
| `src/helpers/BinaryReader.ts` | v7 scene parser; must mirror `Agro/Organs.cs` |

Data model: every primitive carries a row-major 3×4 `affineTransform` and a
9-slot `stats` array whose slot 4 means **irradiance for leaves but woodRatio
for stems and roots**. Buds (`Primitives.Sphere`) are disabled everywhere.

`greenColors` is seeded (`core/Palette.ts`). It was previously shuffled with
`Math.random()` at module load, which made leaf colours differ on every reload
and screenshot comparison impossible. Keep it deterministic.
