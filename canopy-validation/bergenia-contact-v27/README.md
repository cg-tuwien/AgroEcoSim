# Contact retry cost and staged V1 evasion: v27

The rigid retry path is faster in the shared-support fixture. Experimental V1
now completes an isolated thickening request that v26 only partly retained, but
its richer response costs substantially more. The four-hour seasonal crown still
stalls. Bergenia remains in mode 0 by default; this is not V2 or a release-gate pass.

## Implementation

All modes reuse the previous failing pair only as a query-order hint. Each pair
still enters through the ordinary broad phase and receives a fresh certificate.
A solid failure clears that hint's reaction identity, preventing an old foliage
failure from being interpreted as a current force. During continuation, pairs of
changed bodies receive one canonical visit; static checks and checks against
stationary neighbours remain mandatory. The contact gradient no longer computes
the same tissue compliance twice. Matrix dot products accumulate in doubles.

Only an explicitly selected `ContactDeformationMode = 1` enables the richer path
for the growing floral axis:

- Touching neighbours enter as clearance inequalities before the response. A
  passive guard preserves the accepted tolerance and supplies no extra applied
  load merely because it touches. Discovery repeats when a new repairing contact
  enters, rather than for every correction.
- One scale bounds the complete coupled correction, preserving its relative
  compliance weights. Existing joint, folding and strain limits remain in force.
- If direct growth-plus-bending crosses foliage, first certify evasion with the
  old intrinsic dimensions, then certify the sampled growth. Both legs require
  endpoint and swept checks. Failed trials restore the original waypoint,
  including staged bends; successful movement wakes the affected organs.
- A multiple-contact trial can use sixteen corrections instead of eight. Sixteen
  contacts, 256 joints and 64 dual sweeps remain bounded work limits. V1 requires
  a tangent complementarity residual no greater than 10 nm before using that
  response. This is not proof of nonlinear mechanical equilibrium.
- Direction-dependent limits can invalidate a symmetric tangent. Optional
  nonreacting guards may leave that local solve, but never the collision
  certificate. Invalid repairing rows retain the existing bounded fallback.

No biological dimensions, stiffness coefficients, expansion durations,
probabilities, resource accounting or clonal-spread settings changed. No new
species fields were added, so generated species descriptions require no change.
Live checkpoints advance to v27 and explicitly reject older live states. Payload
layout, recorded meshes, scene decoding and saved playback remain unchanged.

## Research and botanical scope

[C-IPC](https://ipc-sim.github.io/C-IPC/) supports coupled thin structures,
finite thickness and conservative motion checking. [Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/)
provides the separate CCD reference. Checking both stages instead of accepting a
clear endpoint is consistent with this safety requirement. This implementation
remains a bounded compliance-based continuation heuristic, not a C-IPC energy
minimizer, BAC shell or demonstrated equilibrium solver.

[RHS 'Rotblum'](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
describes stout spring flowering stems and wavy foliage; its Bergenia genus
description identifies leathery leaves. It does not establish a measured scape
to petiole stiffness ratio. The observed largest 16 cm blade, roughly 5 cm
petioles and 19.5 cm pot remain a separate cultivar comparison with unknown
blade width and initial age. No numerical material calibration was inferred
from these descriptions.

## Mechanical evidence

The fixed scape starts at 1.2 mm radius and requests 9 mm. These are synthetic
engineering dimensions, not cultivar measurements.

| NativeAOT fixture | v26 | v27 |
| --- | --- | --- |
| Rigid blades, two neighbouring plants | Final radius 5.2219 mm | Same retained increment; both neighbours yield |
| Two leaves sharing an unanchored support, rigid blades | Final radius 4.8563 mm | Same retained increment; common support remains fixed |
| V1 blades, two neighbouring plants | Final radius 7.4156 mm | Full 9 mm target, about 25.5% more retained radial growth; both neighbours yield |
| Single-witness extension | Final length 29.9688 mm | Same retained extension |
| Ceiling / confining walls | Exact restoration | Exact restoration; no invented foliage reaction |

One-hour and four-hour frozen poses match within NativeAOT .NET 10. Managed
.NET 8 also receives scoped fixtures. The independent active-set reference was
extended from two rows to all enabled rows in the small fixtures: the V1
four-row force difference is approximately 4.3e-10 N and its tangent residual
approximately 5.7e-11 m. Full nonlinear certificates remain authoritative.
The staged-leg diagnostic counts certified attempts, including later rollback;
it must not be read as a count of committed developmental steps.

The complete day-240 four-hour crown still certifies no waypoint for a manual
1,000/250 micrometre length/radius proposal. Diagnostic experiments identified
both actual swept crossings and uncertified paths, then direction-dependent
joint-limit tangents in the larger contact set. Broader guarded trials did not
resolve this crown. These failures do not establish physical confinement.

## Biological continuation

The seed-42 controlled request retains CPU light, the original 755-day weather
horizon, 1 September 2025 start and disabled clonal spread. Its archived request
is `../bergenia-contact-v20/request.json`. The diagnostic imports v26 day-240
states by changing only the header locally because the payload layout is
unchanged; production does not support that migration.

The rigid continuations through day 275 reproduce the v26 reference payloads
byte for byte, apart from their compatibility header, before the final query
hint refinement. Further final-query continuations cover days 275–280. Inactive
force safeguards subsequently receive geometry/checkpoint checks and two-day
continuations through day 282 at both tick sizes, retaining the same leaf and
petal counts with finite resources and no unresolved accepted foliage contacts.

| Rigid observation | Whole leaves | Both intrinsic dimensions >=50% | Both >=90% | Petals |
| --- | --- | --- | --- | --- |
| 1 h, day 275 | 20 | 8 | 3 | 5 |
| 1 h, day 280 | 20 | 8 | 3 | 5 |
| 1 h, day 282 | 20 | 8 | 3 | 5 |
| 4 h, day 275 | 20 | 4 | 2 | 0 |
| 4 h, day 280 | 20 | 4 | 2 | 0 |
| 4 h, day 282 | 20 | 4 | 2 | 0 |

Five petals represent one open flower. Water and energy remain finite, and no
observed tick reports unresolved accepted foliage contacts. Correct thresholds
use species dimensions plus sampled variation offsets; raw variation fields are
not full targets. Initially present leaves are older than the observation start,
so these continuations do not establish individual expansion durations.

This phase does not establish seasonal V1 acceptance, density doubling at both
tick sizes, four-hour flowering, fresh 755-day or overwintering behavior, or a
dimensional fit to the balcony plant.

## Performance and storage

Final NativeAOT measurements run serially with growth and builds idle. Each of
three processes per version/case performs ten warmups and 35 measured fixed
population retries. The table gives medians of process medians; raw p95 values,
allocations and between-process variation are retained in `benchmarks-final.json`.
The earlier benchmark attempts 1–3 overlap some long V1 processes and are excluded
from this final aggregation. These are kernel timings, not end-to-end scene gates.

| Fixed retry | v26 median ms | v27 median ms | Change |
| --- | ---: | ---: | ---: |
| Single contact | 0.6468 | 0.6541 | +1.1% |
| Rigid radial | 0.9861 | 1.0131 | +2.7% |
| Shared support, rigid | 7.1656 | 2.0650 | -71.2% |
| V1 radial | 7.1196 | 31.2162 | +338.5% |

V1 retains more growth but fails the performance target in this fixture. Its
release/default gate remains closed. Zero steady-state per-contact allocations
are not established: raw measurements include existing query allocations and
the stopwatch. Dense stands, multicore scaling, full FAV11/P003, peak memory,
CPU-light and end-to-end archive measurements remain outstanding.

No accepted per-leaf state fields were added. Experimental V1 allocates bounded
world-shared arrays for 256 summed rotations/folds (7 KiB) and sixteen enabled
flags. It reuses the existing per-organ trial snapshot for transactional rollback;
that snapshot can be allocated on first use and is accounted separately from
world scratch. Existing v26 geometry states and bounded gradient/joint caches
remain. Mode 0 does not allocate the new rotation/fold arrays. This phase does
not claim the complete per-leaf storage acceptance gate passed.

## Checks and artifacts

Core, scoped probes, NativeAOT fixture/checkpoint/server publishes, saved-playback
checks and scoped whitespace checks accompany this report. No server was
deployed. `phase.patch` compares this phase against its starting dirty v26 files;
unrelated worktree changes were preserved. Harness sources and large checkpoints
remain under `/tmp/gf-contact-v27/`, outside the repository.

The raw logs and `results.json` distinguish fixture acceptance, byte-identical
continuation and remaining biological/performance failures. V2 and default
enablement require the remaining gates.
