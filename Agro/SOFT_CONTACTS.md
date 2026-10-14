# The soft contact engine

A second contact engine beside the certified one, selected per run by
`SimulationRequest.ContactEngine` (`Certified`, the default, or `Soft`) or, when the request
omits the field, by `AGRO_CONTACT_ENGINE`. The certified engine and its documents
([COLLISIONS.md](COLLISIONS.md), [COLLISION_PERFORMANCE.md](COLLISION_PERFORMANCE.md),
[CONTACT_DEFORMATION.md](CONTACT_DEFORMATION.md)) are unchanged; the two are meant to be
run against each other on the same request.

## Why a second engine

The certified engine treats every growth increment as a transaction that must be proven
non-penetrating along its swept path, to a 10 µm tolerance. Anything it cannot prove is rolled
back and the organ is suspended for 24 h, then 72 h; births wait as transactions until their
geometry is admitted. On the archived Bergenia request
(`canopy-validation/bergenia-contact-v30/request.json`, seed 42, 1 September 2025, CPU light)
that engine never produced a petal in 755 days at either tick size, while the same request with
foliage contacts switched off bloomed in April. Contact handling was the blocker, not physiology.
The mechanisms are listed in the plan that led here; the short version is that a plant in a
rosette is in contact with itself all the time, so a model in which contact vetoes growth stalls
every organ that touches another.

## What it does

One pass per tick, after Census and before the light model:

1. Broad phase as the certified engine: swept organ boxes per plant, the plant-pair hierarchy
   (`CollisionWorld.RefreshNeighbours`), and the neighbour pairs edge-coloured into batches so
   that no plant is touched by two jobs at once.
2. Foliage pass, **once per candidate pair**: one endpoint distance query through the existing
   kernels (`FoliageGeometry.DistanceUncached`, with the level-of-detail rejection prefilter),
   a side decision, and a correction split between the two bodies by their joint compliance
   (`ContactMobility`, `QueueContactCorrection`). The corrections of all pairs are accumulated
   and applied once per plant by `EndContactSweep`, with the existing per-joint cap and the
   cumulative elastic limit (`PetioleContactBendDegrees`, 25° by default). No swept test, no
   validation pass, no rollback, no retry.
3. Solid pass: up to `SoftHardPushRounds` pushes out of obstacles and soil through the same
   compliance, then one closed-form clip: an organ that elongated into an obstacle and is still
   inside it after the pushes loses that part of **this tick's** elongation which lies behind the
   surface, never more. A shoot under a tray therefore stops at the tray and grows along it.
4. Nothing is suspended, nothing is staged. Births reach Census the tick they are sampled
   (`AgroWorld.StagesDevelopment` is false), and the plant-side gates of the certified engine
   (`GrowthBlocked`, pending developments, retries) are simply never set, except by the
   rhizome-against-rhizome arrest, which is a plant-side mechanism and keeps working.

A pair that still overlaps after a tick is corrected again next tick. Residual overlap is
measured and reported (`SoftContactEngine.MaxFoliagePenetrationMetres` and friends, and the
`SOFT_CONTACT_TIMING` line under `AGRO_COLLISION_TIMING=1`); it is never an error.

### Crossing prevention without continuous collision detection

Each touching pair remembers the side it was on (`SoftContactMemory`: normal, features, last
epoch; ~44 B per pair, expired after `ContactMemoryGraceEpochs` ticks apart). While a pair
stays in contact the remembered side wins: a witness normal that **reverses** (dot below −0.5)
means the sheet was passed through, and the correction pushes back to the remembered side with
the depth measured from the deepest material point. A normal that merely rotates - a rod
sliding off a blade's face onto its edge - is accepted and the memory follows it. A pair found
already overlapping with no memory takes its side from where the bodies were last tick. Two
invariants make this sound:

- `FoliageSkin` (1 mm) exceeds the largest relative approach two organs make in one tick
  (sub-millimetre at 4 h), so every approaching pair is remembered before it can cross;
- `MaxCorrectionDisplacement` (1 mm) caps the witness motion any one joint's turn may cause
  per tick, so a correction cannot carry a remembered pair through a 100 µm sheet.

The `Crossings` and `SuspectedTunnels` counters are the signal that these bounds are too loose
for a given tick length; `NewSides` counts first contacts that were already overlapping.

### The attachment exemption is a ramp, not a box

Every organ of a node overlaps its siblings at the junction by construction. The certified
engine clips the centre lines against a box and then applies the full radius to what survives,
which jumps from "clear" to millimetres of penetration as a tip crosses the box face
(`canopy-validation/bergenia-contact-v32/ATTACHMENT_STALL.md`). Here the exemption is a weight:
zero within `r_att = max(2 mm, 2·r_junction + r_A + r_B)` of the junction, one beyond twice
that, smooth in between, applied to the correction depth. Two rods leaving one junction always
have their nearest points at the junction, so for attached rod pairs the engine samples the
points where the exemption ends and the tips against the other rod's axis and responds at the
best-scored sample. Every term is continuous in the poses; near-parallel sibling petioles fan
out over a few ticks instead of stalling.

## Settings (`CollisionSettings`, none read by the certified engine)

| Setting | Default | Env |
| --- | --- | --- |
| `FoliageSkin` | 1 mm | `AGRO_FoliageSkinMm` |
| `ContactRelaxation` | 0.7 of the overlap per tick | `AGRO_ContactRelaxation` |
| `MaxCorrectionDisplacement` | 1 mm (0 disables) | `AGRO_MaxCorrectionDisplacementMm` |
| `DeepPenetrationThreshold` | 5 mm, diagnostic | `AGRO_DeepPenetrationMm` |
| `ContactMemoryGraceEpochs` | 1 tick | `AGRO_ContactMemoryGrace` |
| `SoftHardPushRounds` | 4 | `AGRO_SoftHardPushRounds` |
| `SolidElongationClip` | on | `AGRO_NoSolidElongationClip` |
| `SoftJointSumRule` | on | `AGRO_NoSoftJointSumRule` |
| `SoftBoundsSkin` | on | `AGRO_NoSoftBoundsSkin` |
| `SoftBandLaw` | on | `AGRO_NoSoftBandLaw` |
| `SoftSlideRamp` | on | `AGRO_NoSoftSlideRamp` |
| `SoftNormalFilterHours` | 12 h (0 follows the witness exactly) | `AGRO_SoftNormalFilterHours` |
| `SoftRelaxUnloaded` | on | `AGRO_NoSoftRelaxUnloaded` |
| `SoftExactPairBudget` | 256 candidate triangle pairs (0 = always exact) | `AGRO_SoftExactPairBudget` |
| `SoftTinyLaminaLength` | 5 mm (0 = every blade uses its template) | `AGRO_SoftTinyLaminaMm` |

The five `Soft*` damping switches are described under "Shaking" below; `SoftExactPairBudget` under
"Full-core utilisation".

`SoftCollisions=false` still switches the foliage pass off under either engine; soil and
obstacle constraints stay.

`BroadLeafV1` folding runs through the same queued corrections. Under the certified engine the
elastic state is rebuilt from the unloaded shape every tick and found again by its correction
rounds; under one capped increment per tick that would never exceed five degrees, so under the
soft engine the folds **persist and relax**: `Gravity` keeps the accepted angles, relaxes
unloaded joints by the same 5 %/h response as contact turns, and `GravityDeformation` moves the
shape toward the unloaded equilibrium by that response instead of replacing it. The certified
path is unchanged (the branch is keyed on the engine).

Two blades pressed as far as their joints bend used to stay overlapped: the normal correction
was queued every tick and swallowed by the cumulative elastic limit in `EndContactSweep`. A pair
whose residual overlap exceeds half `DeepPenetrationThreshold` therefore also gets a tangential
step that separates the bodies' centres, through the same compliance and caps, the way pressed
leaves slide until an edge clears (`SoftContactEngine.Slides` counts them).

## Shaking, and the damping that stops it

Reported on playback of dense rosettes: the leaves trembled every tick. The frame is exported after
the contact pass, so a pair at rest shows the same pose every frame; what alternated was the
solver. Per tick, `Gravity` relaxes every elastic offset toward its target by `r = 1 - 0.95^HoursPerTick`
(0.185 at 4 h), then the contact pass queues `0.7 x` the band deficit of every pair and applies the
**sum** once (Jacobi). With `k` pairs loading one joint the error multiplier per tick is
`mu = (1 - r)(1 - 0.7k)`: +0.24 for one pair, -0.33 for two, -0.90 for three, -1.47 for four, i.e. a
sustained period-2 limit cycle bounded only by the caps, and *shorter* ticks are less stable because
`r` is the only damping. Four smaller switches compounded it: the 1 mm displacement cap was per
pair, not per joint; the push ran at full gain right up to the band edge, so a pair was pushed across
the band and pulled back by gravity; the tangential slide switched on and off at exactly 2.5 mm of
residual overlap; and a blade's witness alternated between a face and an edge, turning the push by
a right angle between ticks. A fifth was in the broad phase: the boxes had no skin margin, so a pair
resting inside the band dropped out of the candidates, lost its side memory, and came back as a
new side.

Each fix is a switch in `CollisionSettings`, so they can be A/B tested one at a time:

- `SoftJointSumRule` - `EndContactSweep` caps a joint's summed turn at the largest single-pair
  contribution it received and at the displacement cap over its longest lever. Contributions that
  ask for the same motion count once; contributions on opposite sides still cancel in the sum.
  `mu` becomes independent of `k`: +0.24 at 4 h, +0.29 at 1 h.
- `SoftBandLaw` - the push is scaled by `x(2 - x)`, `x` the band deficit over the skin, so the gain
  fades to zero at the band edge and a pair settles at the band asymptotically. Unchanged at and
  beyond physical contact. Local gain at most 0.93, so with the joint-sum rule `mu` stays in
  [+0.06, +0.82] for any `k`.
- `SoftBoundsSkin` - the swept boxes are inflated by the skin under the soft engine only, so a
  resting pair stays a candidate. Prerequisite for the band law.
- `SoftSlideRamp` - the slide ramps in between a quarter and a half of `DeepPenetrationThreshold`
  instead of switching on at the half.
- `SoftNormalFilterHours` - a remembered contact's normal follows the witness through a first-order
  low-pass (`1 - exp(-HoursPerTick / tau)`, the light-guidance filter's form). The reversal test
  that detects a sheet crossing still runs on the raw witness.

Measured with a probe that runs one plant tick by tick (stored frames with an even frame stride
alias a period-2 away) and records, per organ keyed by its stable id, the rotation of its axis
since the previous tick, over the last 430 days of a 730-day run from 1 March, seed 42, 4 h, CPU
light, all five switches off against all five on (same build):

| per-tick organ rotation | Geranium macrorrhizum off | on | Heuchera off | on |
| --- | --- | --- | --- | --- |
| median of the per-tick median | 1.91 deg | **0.08 deg** | 7.45 deg | **0.25 deg** |
| median of the per-tick 90th percentile | 5.84 | 0.31 | 16.7 | 0.94 |
| median of the per-tick 99th percentile | 9.68 | 0.71 | 25.5 | 2.39 |
| fraction of organs turning more than 0.2 deg | 0.83 | 0.24 | 0.99 | 0.56 |
| sheet crossings over the run | 2.78 M | 1.64 M | 417 k | 105 k |
| petal days / first petal day | 167 / 86 | 173 / 86 | 155 / 472 | 157 / 473 |
| leaves at day 730 | 1,136 | 1,068 | 156 | 122 |

Which switch does it: Heuchera again, 730 days, each switch off on its own against all on (final
build, so the absolute figures differ slightly from the table above):

| variant | median per-tick median | 90th | 99th | leaves at day 730 |
| --- | --- | --- | --- | --- |
| all off | 7.50 deg | 16.2 | 22.2 | 162 |
| all on | 0.20 | 0.87 | 2.33 | 146 |
| without the joint-sum rule | **7.47** | 13.9 | 20.2 | 144 |
| without the bounds skin | 0.20 | 0.89 | 2.28 | 144 |
| without the band law | 0.17 | 0.83 | 2.28 | 148 |
| without the slide ramp | 0.20 | 0.86 | 2.14 | 128 |
| without the normal filter | 0.26 | 1.04 | 2.63 | 110 |

The joint-sum rule is the fix; the normal filter is worth a quarter of what remains; the other
three are neutral within the noise and stay on for the reasons above. The leaf count ranges from
110 to 162 across variants that differ only in sub-millimetre contact details, so it is a
sensitive outcome, not a quality trend.

`SoftRelaxUnloaded` (13 October 2026) lets petals and unloaded joints relax their contact turns
under the soft engine; `Gravity` used to skip petals outright and release unloaded joints only
under a deformation mode, so a pressed flower head stayed pressed. Measured on the same two
plants over 730 days, off against on: Heuchera per-tick median 0.197 -> 0.183 deg, 99th
percentile 2.33 -> 2.23; Geranium 0.074 -> 0.066 and 0.65 -> 0.60; bloom dates unchanged.
Small, and in the right direction.

A twenty- to thirty-fold reduction in the motion a viewer sees, with the bloom unchanged. The
leaf count moves (Heuchera -22 %, Geranium -6 %): leaves that no longer thrash shade each other
more steadily. The remaining 2 deg at the 99th percentile on Heuchera is real motion of its
petioles under load, not alternation. Two further levers exist and are not implemented: relaxing
petals and unloaded joints under the soft engine (`Gravity` skips them), and an under-relaxed
applied turn (`lerp(previous, queued, beta)`, 12 B per organ of checkpointed state). Rewriting the
stored frames after a run was rejected: it would need a V8 reader on the server, it would invalidate
the analytics computed on the true pose, and it would break resume. If residual shaking is still
visible, the place for a filter is the viewport (a 1-2-1 kernel over three decoded frames removes
exactly a period-2 and nothing slower), not the store.

## Full-core utilisation

Reported as "threads run only briefly; a 32-core machine is mostly idle". Measured with a probe that
drives one tick phase by phase through the private tick methods and records wall time, process CPU
time (so the effective core count per phase) and allocation, after 600 warm-up ticks of a 0.25 m
grid of Carex and Bergenia at 4 h, CPU light, seed 42 - the frames' SHA-256 are the byte-identity
fixture for everything below. Before:

| plants (organs) | ms per tick | collisions | light | export | effective cores, collisions |
| --- | --- | --- | --- | --- | --- |
| 1 (2,107) | 152 | 138 (91 %) | 8 | 3 | **1.0** |
| 100 (103 k) | 870 | 642 | 169 | 33 | 18 |
| 400 (429 k) | 3,169 | 2,226 | 707 | 129 | 24 |

So at a stand the engine already ran on 24 of 32 cores; the complaint is the single plant, which was
entirely serial, and the serial phases around a stand: the soil's one lock, the neighbour refresh,
the schedule, the `BeginContactSweep` loop, the epilogue, the export, one deflate thread, and
contiguous job ranges that parked most workers behind whichever range held the mature plants.
What changed, in order of effect:

- **Lock-free, ordered soil requests.** Each plant buffers its water requests on itself
  (`PlantFormation2.WaterRequests`); `SoilRequestRouter` hands them to the soil once per tick in
  formation order, cells are processed in ascending index. That removed the one lock every root
  of every plant took during the parallel tick *and* made the parallel tick reproducible: it is
  byte-identical to the sequential one at 100 and 400 plants over 650 steps, with a 1 h, a 4 h and
  a worker-cap variation, and on the 4,134-plant facade over 430 steps (two parallel runs and one
  sequential run, identical frames). `Deterministic` is a diagnostic now; at 400 plants it costs
  0.8 s of serial plant tick and census per tick for nothing.
- **`WeightedSchedule`** (`CollisionWorld.Scheduling.cs`): jobs sorted by last tick's measured
  cost, longest first, each worker pulling the next from a shared cursor. Physics untouched.
- **One job per plant before and after the cross-plant batches** (`PreparePlant`,
  `FinishPlant`): three barriers and two serial loops gone; the lamina cache is completed on the
  plant's own job so later readers never resize it. Stage masks replaced `List.Contains` in the
  schedule; the facade's 2,502 trays tick and deliver in parallel.
- **A single plant's own pairs are evaluated in parallel.** `VisitPair` is split into a read-only
  `EvaluatePair` (distance, side, depth) and a per-plant `RespondPair` (memory, mobility,
  queueing). With fewer plants than half the workers the sorted pair list is evaluated over
  fixed 32-pair chunks on all cores and the responses replayed in pair order: bit-identical for
  any chunking or worker cap (1 against 32 measured), and the per-chunk counters are summed in a
  fixed order so the diagnostics match too.
- **A sampled blade-to-blade distance** (`FoliageGeometry.DistanceSampled`, soft engine only).
  Two blades lying along each other put every triangle of the overlap within the band of every
  triangle opposite, so the exact query's candidate count is the product of the triangle counts:
  95 % of a single Carex's contact time went there. Past a budget of candidates the query
  samples each vertex of either blade against the other's triangle tree, pruned by the best
  distance so far, then refines exactly around the best vertex and the triangle it landed on. It
  is an upper bound on the true distance; `Agro --sheet-distance-selftest` compares it against
  the exact query on 600 random pairs per species (along, crossing, offset, -1 to +3 mm apart):
  never below the exact minimum, mean overestimate under 5 um, maximum 0.33 mm (Carex 0.000 mm
  after the second refinement), and the touching verdict never differs. The exact loop also
  prunes candidates by the current best, which is result-identical and benefits both engines.
- **Export and storage.** Each plant's frame record is written into its own buffer in parallel
  and concatenated in order (bytes identical); the run store deflates on a quarter of the cores
  with one committer keeping the index in step order, and the queue and byte budget scale with
  it. The simulation loop runs on its own thread, and both entry points set the thread pool's
  minimum to the core count so the first fork/join of a run is not throttled by pool injection.

Server GC was tried and rejected: at 400 plants it changed nothing (3.16 against 3.17 s per tick)
and raised the peak working set from 2.9 to 7.6 GB.

After (same probe, same requests; the plant differs where the physics did):

| plants | ms per tick before | after | collisions before | after | effective cores, collisions |
| --- | --- | --- | --- | --- | --- |
| 1 | 152 | **28** | 138 | 15 | 4.9 |
| 100 | 870 | **470** | 642 | 252 | 24.5 |
| 400 | 3,169 | **1,631** | 2,226 | 824 | 30.4 |

(12 October, before the tiny-blade capsules and the solid-pass carry-over: 53, 775 and 2,519 ms per tick; after the capsules alone 28, 502 and 1,746.) The parallel tick
stayed byte-identical to the sequential one at 100 plants after every change.

- **Tiny blades meet other organs as capsules** (`SoftTinyLaminaLength`, 13 October 2026). With
  the costliest pairs traced (`AGRO_TRACE_SOFT_PAIRCOST=1`), the single Carex's critical path was
  newborn leaf slices a tenth of a millimetre long carrying their full 46-triangle templates: a
  cluster of them at one node, every one within the band of every other, cost 1-2 ms a pair and
  10-15 ms a chunk while the other chunks took a tenth of that. A blade shorter than 5 mm is now a
  capsule of its own half width, which the rod kernels answer in under a microsecond. The parallel
  evaluation of that plant went from 33-48 ms to 3-4 ms of wall time at 130 -> 70 ms of CPU, and
  the whole contact pass from 41 to about 13 ms. The pair sort materialises its stable-id keys
  once instead of looking both ids up in a delegate per comparison; the order is unchanged.

- **The solid pass carries a contact across its rounds** (13 October 2026). On the facade the
  contact pass was half solid pass, and the solid pass was 2.2 s of CPU a tick: every push round
  asked the two-million-triangle scene again about the same 11,000 organs resting on their tray
  walls, and support detection then asked a third time about all 70,000. A push now remembers
  its witness in the organ's own frame together with the surface normal; the later rounds of
  the same tick measure the remaining depth against the plane through them (exact for a tray
  wall, within the push for anything curved), and support detection takes the push's witness
  where there was a push and skips the organs whose cached clearance rules a contact out.
  Facade, 4,134 plants in trays, 70,000 organs, quiet machine: solid pass 2.2 -> 1.3 s of CPU,
  contact pass 135 -> 99 ms, tick 262 -> 215 ms. The Bergenia under a 25 cm disc still never
  passes it (0 organ-ticks through, flowers open) and its leaves still spread around its edge.
  `AGRO_COLLISION_TIMING=1` now also prints the solid pass by stage.

The single plant's remaining wall time is its serial half - the pair query, the response
replay (about 4.5 ms for 3,800 contacts), the solid pass and the epilogue - so it does not reach
32 cores and will not without splitting the plant itself. The facade request (4,134 plants in trays, 76,500 organs at
step 430) ran at 1.2-1.6 s per tick with eight other simulations sharing the machine; 83 % of it
is the contact pass at 15-24 effective cores.

`AGRO_COLLISION_TIMING=1` now prints the phases (`neighboursMs`, `prepareMs`, `respondMs`,
`crossMs`, `finishMs`, `batches`) and, for a single plant, the parallel evaluation's wall time.

## Cost and memory

The engine owns one pair list and one sparse memory dictionary per plant and allocates
nothing per pair in steady state. It never calls the memoised distance and static-contact
wrappers, so the certified engine's per-plant evaluation tables, pair caches and per-thread
memos are not allocated at all under it.

Measured on this machine (Ryzen, 32 threads), seed 42, deterministic:

| Scene | Soft engine | Certified engine |
| --- | --- | --- |
| Archived Bergenia, 755 days at 4 h (4,530 ticks) | 40 s wall, 22 s in the foliage pass, ~5 ms per tick | Same build, same request: 840 s for the first 444 days, 430 ms per tick at day 330 and rising; 44 of 51 organs suspended at day 330 |
| Same at 1 h (18,120 ticks) | 129 s wall, ~4 ms per tick | - |
| Four Bergenias in two contacting pairs, 60 days | 22 ms per tick | - |
| 100 plants, seven species on a 0.5 m grid, 120 days from 1 March (12,400 organs, 3,200 leaves at the end) | 99 s wall for 720 ticks; 107 ms per tick in the foliage pass on average; 272 MB managed heap, 945 MB peak working set including the 3.2-million-cell soil field | see the regression table |
| 400 plants, same mix on a 0.45 m grid, 90 days (50,400 organs, 12,300 leaves at the end) | 142 s wall for 540 ticks; 193 ms per tick in the foliage pass, 2.7 s per simulated day at the end including the CPU light tracer; 351 MB managed, 1.39 GB peak working set | not run (the certified engine took 47 s per tick at 124,000 organs in `COLLISION_PERFORMANCE.md`) |

The certified engine is unreliable on the 100-plant mixed stand: in three of twenty-seven
attempts it aborted on simulation day 3 with an `AccessViolationException` in
`FoliageGeometry.ProjectAttachmentFeature`, reached from `AttachmentSweepUncached` through
`GrowthTrialClear` during `TryRedirectGrowth` (`ResolveCapturedVegetation`), once directly and
twice through `AttachmentInterval`. The plants are three-day-old seedlings, so nothing in the
lifecycle changes above is involved, and the stack is shallow, so it is not an overflow. The
same request passes in the other attempts, with one collision worker or many, under heavy CPU
load or idle, with tiered compilation disabled (eight of eight), with on-stack replacement or
dynamic PGO disabled, and on the .NET 10 runtime; the crashes happened on .NET 8.0.31 with
default settings while other simulations were running. The code on that path is bounds-checked
managed code without unsafe blocks, so a corrupted reference inside a struct passed by `in`
across a garbage collection is the leading suspicion, but it is not established. Left for the
certified engine's own work; the probe reproduces the scenario with
`--engine Certified --grid 10 --spacing 0.5 --mix --days 10 --hpt 4 --every 1` on the archived
request started 1 March 2026, and the soft engine completes the same stand every time.

The foliage pass is linear in candidate pairs and parallel over plants, so a 10,000-plant stand
of this density (about 1.25 million organs) projects to a few seconds per tick on this machine,
comparable to the CPU light tracer, against roughly ten minutes per tick extrapolated from the
certified engine's 790-plant measurement. Measure it before quoting it.

## Checkpoints

Live checkpoint version 33 writes one engine tag byte ahead of the collision payload. The
certified payload after it is unchanged; the soft payload is the sorted list of contact
memories, each with its age in ticks, so a resumed run makes the same side decisions on its
first tick as the uninterrupted one. A checkpoint resumes only under the engine that wrote it.
Recorded playback (V7/V8) is unchanged.

## Lifecycle changes made alongside, under both engines

These are not part of the engine and are deliberately separate, so an A/B run can include or
exclude them:

- A FlowerMeristem whose parent is thinner than it already is no longer shrinks every tick
  (`FlowerHelper.grow`, the increment is clamped at zero like the stem and bud branches).
- A crown already at `CrownMaximumNodes` still hands over to a successor apex after a flower
  commitment. Without it a Bergenia, which fills its ten nodes before its second spring, could
  never commit again; the cost is one node with its leaves per flowering year beyond the cap
  until basal node abscission exists.
- A floral segment or bud that a solid keeps from reaching its target length still advances the
  chain after twice its nominal growth duration (`AboveGroundAgent.ElongationOverdue`), so an
  inflorescence under a tray flowers from compressed internodes instead of expiring. Under the
  certified engine a blocked segment never reaches that code, so it changes nothing there.

## Regression scenarios

All with the archived Bergenia request unless stated, seed 42, deterministic, CPU light,
`ContactEngine = Soft`. The probe lives outside the repository (`AGENTS.md`, plant-behavior
probe workflow); it reads the request, overrides the engine, tick length and horizon, and
records one CSV row per day.

| Scenario | Result (8 October 2026) |
| --- | --- |
| Certified engine with the field absent or `Certified` | Dispatch is one line; the certified payload and every helper it uses are unchanged, and a 10-day run exports byte-identically before any contact occurs |
| Certified engine, same Bergenia request, same build, 4 h | Day 330: 20 leaves, 7 half-expanded, 4 at ≥ 90 %, no petals, 44 of 51 organs suspended; day 450: 0 at ≥ 90 %, no petals. Matches the v30-v32 findings |
| Bergenia 755 days, 4 h | Petals 9 Apr-20 May 2026 and 23 Apr-4 Jun 2027; 22 leaves, 20 at ≥ 90 % at day 330; finite resources; mean residual foliage overlap 0.36 mm over the run; overlap beyond 5 mm on 12 of 755 days, all inside the crowded panicle or single days, none persisting (before the sliding response one leaf pair stayed 5.5 mm overlapped for six weeks); 2,757 tangential slides; 43 s wall |
| Bergenia 755 days, 1 h | Petals 11 Apr-7 May 2026 and 16 Apr-11 May 2027; 24 leaves at the end; 179 s wall for 18,120 ticks |
| Same request, foliage contacts off (`SoftCollisions=false`) | Petals 9-17 April in the controls with seed 43 or 1 h ticks; the seed-42 4 h control skipped 2026 on the commitment dice (one 2 % roll per tick over 45 days) and bloomed 24 Apr-24 May 2027 |
| Scape under a 25 cm disc (`umbrella` obstacle over the seed) | Plant stays under the disc (max height 0.25 m), 6,589 elongation clips over 330 days, 22 leaves with 21 fully expanded, petals 10 Apr-25 May; no organ pushed through the disc |
| Four plants in two contacting pairs, 60 days, worker caps 1/4/32 | Byte-identical state and export hashes; checkpoint at day 30 and resume: state and export identical |
| Timestep drift, 100 days, 1 h vs 4 h | 51 organs, 20 leaves, 9 half-expanded in both; height 0.355 vs 0.356 m; leaf reach 0.308 vs 0.267 m |
| Carex morrowii 'Variegata', 100 days from 1 March, 4 h and 1 h, soft vs certified on the same request | Both engines: 96 organs, 80 blade segments, no downward or inverted organs, no deaths. Soft: all 80 segments at ≥ 90 % of target, 2.0 s in the foliage pass; certified: 56 at ≥ 90 %, 10,046 rejected motions, 18.2 s |
| Geranium macrorrhizum, 162 days from 1 March, 4 h, soft vs certified | Both: first buds day 63, first petals day 85, 12 leaves. Soft: petals to day 125, 12 leaves fully expanded, 0.8 s foliage; certified: petals to day 115, 10 fully expanded, 9 organs suspended at the end, 21.2 s |
| Bergenia with `ContactDeformationMode = 1` (`BroadLeafV1`), 330 days, 4 h, soft | Petals 17 Apr-30 May 2026; 22 leaves, 20 at ≥ 90 %; 44 deformation states with folds persisting across ticks, up to 6.5° on a blade and 8.5° on a petiole; 187 s wall, 43 ms per tick (the V1 narrow phase is about nine times the rigid cost, as under the certified engine) |
| Parallel against sequential tick, 100 and 400 plants (Carex/Bergenia grid), 650 steps; facade request, 4,134 plants, 430 steps (12 October 2026) | Byte-identical frames in every case; sequential result unchanged by the request buffering; one collision worker against 32 identical on one plant |
| Shaking, Geranium macrorrhizum and Heuchera, 730 days, damping switches off against on (12 October 2026) | Per-tick median organ rotation 1.9 -> 0.08 deg and 7.4 -> 0.25 deg; see "Shaking" above |
| Facade request (`salvaged-run-requests/b886d0a6...json` with `fav11_complete_geometry_model.obj`), 4,134 plants, steps 400-430, quiet 32-core machine (13 October 2026) | 215 ms per tick at 22 effective cores: contacts 99 ms (28 cores), light 57, post 28, tick 16, census 13, export 9; 11 cross-plant batches cost 5 ms, the solid pass 45 ms |
| Facade request through the server's terrain path (`SoilFormationsList(null, obj, ...)` then `SetWorld`, as the hub does) against the constructor path, repaired September model, 4,134 plants, 6 steps (14 October 2026) | Both run; exported frames byte-identical. Before the fix the request walk threw `NullReferenceException` in `SoilRequestRouter.Gather` on step 1 of every uploaded-terrain run, while every probe passed the world to the constructor and never saw it |
| Archived Bergenia under a 25 cm disc 25 cm above the seed, 330 days, 4 h (13 October 2026) | No organ tip inside the disc's radius rises above it (tallest 24.6 cm against the 24 cm underside); leaves reach 37 cm around its edge; 22 leaves; petals 7 Apr-17 May; 5,513 elongation clips |
| Geranium macrorrhizum and G. x cantabrigiense, four plants on a 0.5 m grid, 730 days, soft engine (13 October 2026) | Second-year stand, 542 and 251 scapes: median azimuth excursion of a scape tip about its root 0.05 and 0.08 of a turn, largest 0.17 and 0.19; chain tip `dirY` never below 0.74 and 0.67; inflorescence tops 33-36 and 17-21 cm |
| Geranium macrorrhizum and G. x cantabrigiense, four plants on a 0.5 m grid, 200 days, soft engine (13 October 2026) | Scape tips circle no more: the azimuth of each scape tip about its root drifts at most 39 degrees over the bloom (before the azimuth-fixed steering: a full 360 degree circle on one scape, 212 degrees with the steering off), 90th-percentile azimuth motion under 2 degrees per tick (before: 4); chain tip `dirY` stays above 0.79 |
| Scape posture, Heuchera / Carex / both Geraniums, 730 (Carex 450) days, soft engine, after the mechanics fixes and the species retune (12 October 2026) | Heuchera: base chain tip never below dirY 0.97 through bloom, chain angle at most 13 deg, flowers 41 cm above the foliage (before: tip at -0.27, chain 114 deg); Carex 0.99 / 9 deg; G. x cantabrigiense 0.70 / 46 deg, +12 cm; G. macrorrhizum 0.63 / 51 deg, +19 cm (at 2e8 one peduncle in two still hung at -0.58; 3e8 and a 0.01/h rate hold it). Heuchera under the certified engine: 0.94 / 20 deg |

Add rows as scenarios are run; keep the certified engine's own table in `COLLISIONS.md`.
