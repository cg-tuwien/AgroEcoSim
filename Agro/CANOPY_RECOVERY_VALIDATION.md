# Canopy recovery implementation and validation

Status reviewed on 20 September 2026: the implementation and focused checks are in place.
**Full-facade acceptance failed.** Both corrected P003 runs stopped on unresolved
foliage contacts (details below) and have not
reached the established second-spring April–June comparison. Passing the checks
below does not establish that the facade now reaches the photographed density.

## Implemented behaviour

- Obstruction suspends inadmissible growth instead of killing above-ground tissue.
  Photosynthesis, maintenance, admissible thickening and independent siblings
  continue. Daily retries back off to 72 hours after three failures; separation,
  meaningful movement and obstacle changes can wake growth sooner.
- Contact correction uses the organ's supporting chain, stiffness, section and
  leverage. The cumulative joint limit includes gravity and repeated corrections.
  Tangent alternatives, partial extension and local rollback retain admissible
  growth. Surface reactions preserve weight and residual moments on rhizomes.
- Crown activation, continuation, branching, flowers and replacement leaves use
  retained developmental transactions. Rejection preserves sampled choices and
  resource shares. A viable old leaf stays until its replacement fits, subject to
  its own normal senescence.
- Stem/leaf and leaf/leaf checks include the displayed triangulated leaf shape,
  slices, rotation, parent motion, elongation and widening. Separate leaves in a
  crown interact outside the attachment neighbourhood. Swept checks cover sheet
  crossings; uncertified movement is suspended. Contact history is released after
  separation or movement around an edge.
- Geranium floral continuations inherit the accepted tangent, with no repeated
  upward steering or light steering. Floral laterals retain their authored angles.
  Descendant centres of mass and scape stiffness determine bending under bloom load.
- Optional `GrowthLightGuidanceEnabled` defaults to true. Guidance uses 64 fixed
  diffuse-sky directions, a 24-hour/2-cm refresh, 48-hour smoothing, confidence 0.1,
  a 2-degree deadband and a 3-degree/day cap on growing vegetative tissue. Mode 0
  remains directionless. Guidance does not replace scalar photosynthetic irradiance.
- Live checkpoints are version 3; older live checkpoints fail explicitly.
  Playback retains its existing V7 layout, with no per-frame contact or guidance
  payload. Existing playback readers remain supported.

An additional geometry defect was found during validation: tiny leaves retained
the full mature mesh relief while their length and width were microscopic. Relief
now scales with actual width in both collision geometry and V7 matrices. An
isolated imported P003 Heuchera reached 12 leaves and about 0.017 m² at 30 days,
instead of two almost unchanged seedling leaves. This is a geometry correction;
irrigation, nutrition and planting-size/growth-rate calibration were not changed.

The implementation details and provisional defaults are in [COLLISIONS.md](COLLISIONS.md).

## Focused evidence

Temporary net8 probes use seed 42. Harnesses remain outside the repository at
`/tmp/greenfdt-collision-probe` and `/tmp/greenfdt-runstore-probe`.
Logs and the source/assembly hash manifest are in [canopy-validation](../canopy-validation/).

| Check | Result |
| --- | --- |
| Geometry, survival, mechanics, development and guidance | 81 checks passed; includes 800 hours of obstruction without collision death, sibling growth, safe partial extension, cumulative bend limits, supported/hanging rhizomes, blocked crown activation and renewal, obstacle removal and exact repeated rollback at attachment boundaries. |
| Actual Carex/Bergenia templates | Both directions of sheet crossing rejected; finite-edge escape and separated overlapping projections accepted; widening and coincident contacts checked. |
| Persistent contacts | Touching leaves retain a sparse contact; moving around an edge releases it. Coincident surfaces retain their remembered side even when the organ bases suggest the opposite side. |
| Geometry optimization | 10,000 seeded transformed real-template pairs matched the preceding exact-distance implementation. |
| Checkpoint replay | 210 consecutive exported V7 frames matched byte-for-byte with four plants, contact, organ deletion/reindexing, tray removal, retries and guidance refresh. No unresolved foliage contacts occurred in any of the 120 warmup or 210 replay steps. The existing ambient checkpoint self-test also passed. |
| Light | Analytic diffuse irradiance matched `DHI + groundAlbedo * GHI`; mode 1/2/3 scalar irradiance stayed unchanged by guidance. The refresh interval and 1-hour/4-hour steering cap were checked. |
| CPU ray optimization | Static/dynamic ray-hit and leaf-area results matched over changing vegetation counts, including empty scenes. |
| Playback consumers | 63 tests passed across six suites: decoding parity, lamina slices, stored runs, loading, frame indexing and history retention. |
| Actual saved run | All 180 frames and the v3 checkpoint of a 30-day single-Heuchera run round-tripped byte-for-byte through `RunStore`. |

Carex at 100 days was also measured at one-hour and four-hour ticks, with finite
nonnegative mass/water and no collision deaths. Stems and petioles remained upright;
zero and three leaf segments respectively pointed downward.
The total downward-organ diagnostic also includes dormant buds and must not be
interpreted as a foliage count. Exact counts are in the two Carex logs.

| Carex at 100 days | 1-hour ticks | 4-hour ticks |
| --- | ---: | ---: |
| All above-ground organs, including rhizomes | 507 | 450 |
| Leaf segments (not whole biological blades) | 368 | 320 |
| Downward leaf/petiole/stem organs | 0 / 0 / 0 | 3 / 0 / 0 |
| Inverted non-petal organs | 0 | 1 |
| Collision/resource deaths | 0 / 0 | 0 / 0 |

These counts demonstrate remaining timestep sensitivity; the runs are not claimed
to be numerically invariant. They also do not pass the older strict zero-downward
foliage criterion. The species has arching leaves ([RHS profile](https://www.rhs.org.uk/plants/99270/carex-morrowii-variegata-v/details));
that supports allowing distal arching, but does not by itself validate the measured
postures or justify ignoring inversion and support checks.
The four-hour inversion is the distal segment (slice 7 of 8) of one blade, with
axis Y = -0.909; the actual exported scene confirms it. It is not a culm or
whole-crown inversion, but the strict older zero-inversion criterion remains unmet.

Geranium is observed daily, rather than only at the old day-140/day-162 snapshots,
which can miss a completed bloom. An intermediate implementation produced no flowers:
rollback of a support also discarded safe descendant growth. Keeping the individual
increments restored flowering without changing growth rates. The final four-hour
trace first records buds on day 87 and petals on day 96, with up to 20 petals.
At day 162 it retains 34 living leaves and five petals, with zero collision or
resource deaths. The one-hour trace also first records buds on day 87 and petals on day
96, retaining 22 leaves and five petals at day 162, also without collision or resource
deaths. These isolated plants do not establish the facade's flowering performance.

## Computation and storage

- One million cached leaf-template lookups previously allocated 88,001,864 bytes;
  non-capturing factories reduced this to 1,864 bytes in the probe.
- The ray-buffer parity probe reduced retained buffers from 5,803,200 to 3,264,992
  bytes while preserving the measured results.
- Immutable mesh hierarchies use worker-owned traversal scratch. Bounded pair
  caches and conservative clearance certificates avoid repeated exact queries.
  Independent plant pairs run in deterministic parallel batches; no candidate
  budget silently accepts unchecked movement.
- The actual 180-frame stored-run probe wrote 753,894 raw frame bytes as 496,749
  compressed frame bytes. Its frame index was 1,440 bytes, its raw checkpoint
  57,679 bytes, and the entire small store 547,996 bytes. This is a small consumer
  validation, **not a full-facade storage estimate**.
- Contact processing remains expensive. An earlier seven-plant P003 exploration
  at day 210 took 2,166 seconds, accumulated about 111 GB of allocations and
  reached a roughly 2.0-GiB process high-water mark. That run preceded the final
  allocation/cache/contact-release changes, so it is diagnostic evidence rather
  than a final performance benchmark. It had zero detected solid penetrations,
  zero unresolved foliage contacts and zero collision deaths at that snapshot.

The exploration's [exported geometry](../canopy-validation/exported-geometry-review.png)
was inspected using the actual displayed meshes and exported transforms. It shows
one plant per species on separate scales, with trays omitted. It cannot establish
facade coverage. Both the current and legacy frontend readers decoded the exported
scene, consumed all bytes, and found finite transforms and statistics.

The final-source 30-day P003 sample (one plant of each of the seven species) finished
with 513 above-ground organs, 174 leaf segments, zero detected solid penetrations,
zero unresolved foliage contacts, no collision deaths and finite resources. It took
79.5 seconds after initialization, allocated 5,022,656,816 bytes cumulatively and
reached a 2,075,448-KiB process high-water mark while loading the complete terrain.
The 180 exported frames totalled 3,952,553 raw bytes and 2,694,822 zlib bytes;
the final snapshot was 43,196 bytes and the live checkpoint 294,967 bytes.
These measurements include a large terrain with only seven plants and cannot be
extrapolated as a full-facade performance result.

The [current exported geometry](../canopy-validation/exported-geometry-current.png)
was inspected using displayed meshes and actual export transforms. Current and legacy
readers also decoded that scene and the day-162 Geranium scene, consumed every byte
and verified finite values. The figure uses separate scales and omits the trays;
it is a geometry check, not a facade-density comparison.

## Full P003 comparison failed before completion

Both final corrected runs exited with an unresolved-foliage assertion on 17 September:
mechanics-only at step 90 (two contacts; 816 seconds elapsed), and mechanics plus
guidance at step 386 (two contacts; 18,209 seconds elapsed). Neither reached the
requested year or established second spring. These failures remain unresolved;
the focused regression passes below do not supersede them. The linked diagnostic
artifacts are local, uncommitted run output.

The final runs use the September OBJ (1,869,918 collision triangles), all 790 P003
plants, seed 42, four-hour ticks, mode 3, deterministic execution, and 470 days
starting 1 March 2026. They cover overwintering and the following April–June.
The photographs cover 4 April, 11 May, 13 May, 8 June and 12 June 2026: a 69-day
established-perennial reference, not a target for seedling growth-rate fitting.
Besides monthly measurements, the corrected runs capture 4 April, 11 May, 13 May,
8 June and 12 June of their second spring (simulation days 399, 436, 438, 464 and
468). The [live comparison](../canopy-validation/P003_COMPARISON.md) refreshes from
measured output and links the monthly CSV and species leaf counts.

- [Corrected mechanics: live status](../canopy-validation/p003-mechanics.status.json),
  [log](../canopy-validation/p003-mechanics.log),
  [metrics](../canopy-validation/p003-mechanics.metrics.json).
- [Corrected mechanics plus guidance: live status](../canopy-validation/p003-guidance.status.json),
  [log](../canopy-validation/p003-guidance.log),
  [metrics](../canopy-validation/p003-guidance.metrics.json).
- The frozen pre-change comparison was launched separately; its [log](../canopy-validation/p003-baseline.log)
  is mirrored from `/tmp/greenfdt-canopy-baseline-facade-full.log`. Its older area/crown diagnostics
  differ from the corrected probe, so those columns must not be compared as if
  they measured identical quantities.

The final probes record monthly triangulated living leaf area, crowns, projected
2-cm-grid coverage, paused tips, pending/rejected births, deaths by reason,
penetrations, finite resources, elapsed time, allocations, managed memory and
process peak memory. Every playback frame is exported and compressed to count
the actual encoded frame payload without retaining gigabytes of frame history.
The latest monthly checkpoint and each monthly exported snapshot are retained.
An unresolved foliage contact at any timestep stops a corrected full-facade run
and writes a failure checkpoint; monthly sampling cannot hide a transient failure.
Encoded payload counts are explicitly distinguished from an actual complete
saved-run file. Runs under the `intermediate-*/` directories were stopped while
contact-normal, rollback and cache-consistency defects were corrected. The
`intermediate-monthly-assertion/` runs used the final simulator assembly but were
restarted to add the per-step validation assertion. They are not completed
full-facade acceptance evidence.

The full comparison and second-spring density assessment remain outstanding.
Early 790-plant trials already spent seconds per timestep in contacts; these
changes should not yet be described as a demonstrated full-facade speedup.
Concurrent probes share the machine, so their wall times are not controlled
head-to-head performance measurements.

## Build checks

The required core build uses `dotnet build Agro/Agro.csproj --no-restore -m:1 -v:minimal`.
The server consumer is built with `--no-restore -m:1 -p:SkipSpeciesGeneration=true`.
Both target frameworks declared by the current projects are checked. Existing
warnings are retained. Frontend typechecking and `git diff --check` are also run;
build output is retained alongside the probe logs.
