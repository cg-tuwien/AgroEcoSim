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

Add rows as scenarios are run; keep the certified engine's own table in `COLLISIONS.md`.
