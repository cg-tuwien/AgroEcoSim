# Contact numerics: v31

7 October 2026. This step improves segment-distance measurement and preserves
accepted interpolation frames. It does **not** resolve the mature scape stall or
establish biological acceptance. Mode 0 remains the default. Tissue coefficients,
intrinsic dimensions, the provisional 84-day expansion target, crown probabilities,
clonal settings, collision skin and work budgets are unchanged.

## Retained implementation

`StaticCollider.ClosestPointsBetweenSegments` now subtracts input endpoints,
solves its segment parameters and measures their residual in double precision.
It rounds the returned witnesses separately. Previously, witness rounding occurred
before distance measurement, losing precision with translated geometry.
Public witness types and the float squared-distance return value are unchanged;
degenerate/parallel policies are not redesigned in this increment.

`OrganPose.Interpolate` returns the original frames exactly at zero and one.
Unchanged base, rotation and deformation fields remain unchanged at intermediate
times. This also preserves the endpoint's `Rounded` flag. No repeated normalization
is introduced. Full-path checks remain required; a clear midpoint or tiny motion
cannot authorize a trajectory.

Double-vector helper expressions were correct in the reference fixtures but costly
in NativeAOT. The selected implementation uses scalar locals. It adds no per-organ
state or allocations in the segment kernel. Live checkpoint **31** rejects older
live states because numerical admission affects continuation. Payload layout and
saved V7/V8/V9 playback formats remain unchanged.

## Precision reference and remaining obstruction

[PRECISION.md](PRECISION.md) records an independent constrained quadratic reference,
attachment clipping and the distinction between rounded endpoints and analytically
transformed poses. The native attached-petiole failure lies too close to the existing
tolerance to justify increasing permitted overlap or ignoring tiny movement.

Both historical native day-240 fixtures still restore the scape exactly after a
requested 1 mm elongation / 0.25 mm thickening. The new native day-240 states at
both tick sizes are clear but also retain no increment in that fixture. Clipped-hull plane
certification and an alternative close-quaternion interpolation were tested only
in isolated copies; neither released the historical scape and neither is enabled.

## Verification

- 6,006 independent segment-distance comparisons per runtime, including translated,
  crossing, skew and point-degenerate segments; tolerances account for the float
  squared-distance API and roundoff near exact intersections.
- 4,000 exact-endpoint and 8,000 stationary-frame comparisons per runtime.
- Eight mechanical fixtures at both 1 h and 4 h in managed .NET 8 and NativeAOT
  .NET 10: certified growth, same-crown contact, V1 response, angular constraints,
  ceiling/wall contact, exact impossible-confinement rollback and V1 checkpoints.
  Frozen single/radial/same-crown/V1/wall poses have identical hashes between
  tick sizes within each runtime. Managed/native poses need not match bit for bit.
- Eleven targeted continuous-collision fixtures and 304 representable published
  queries per runtime. All 145 collision-positive queries are refused. The 20 µm
  per-body offsets and rational-to-float conversion differ from the original
  zero-thickness benchmark; 20 deforming edge queries are excluded. Sampling
  190,080 material points finds no motion-bound excess, but is not a proof.
- Eight fresh/mature native continuation cases at both tick sizes, with and without removal/reindexing,
  reproduces immediate/continued state and exported frames byte for byte after
  cache rebuilding; v30 rejection is explicit. Synthetic V1 continuation also passes.
- Prescribed core build and NativeAOT server publish pass. Five frontend suites
  pass all 38 playback/geometry tests. These are helper checks, not browser pixel
  comparisons. No species fields or geometry assets changed.

## Fixed-population cost

Source-verified native v30/v31 probes alternate order over three paired processes,
with ten warmups and 35 measurements per process. Task-owned seasonal probes are
paused and resumed in a `finally` block. [benchmarks.json](benchmarks.json) retains
the first batch; [benchmarks-quiet-repeat.json](benchmarks-quiet-repeat.json) retains
a second batch after build jobs finish, with the remaining seasonal probe paused.
Both include raw measurements and executable hashes.

| Fixed retry | First batch, median change | Quiet repeat, median change |
| --- | ---: | ---: |
| Single neighbour | +0.2% | −0.2% |
| Radial neighbours | −1.1% | +0.04% |
| Same crown | +0.9% | −0.3% |
| V1 radial neighbours | −14.8% | +18.0% |

Changes compare the median of three process medians. V1 process timing and
allocations vary; these measurements do not establish a general speedup or
latency guarantee. The isolated native segment kernel has no added allocations
and runs about 24% faster in five paired repeats on fixed inputs. No persistent
per-organ storage is added by this increment. This scoped comparison is not the dense
stand, full-growth, CPU-light or 790-plant FAV11 gate. Existing solver allocations
remain; the zero-per-contact-allocation objective is not achieved.

## Biological evaluation

Fresh seed-42 CPU-light runs reuse the reconstructed 755-day request, with a
330-day observation horizon. Dimensions, probabilities and disabled clonal spread
are fixed. Reference v30 results come from its source-verified native cases in
[the preceding report](../bergenia-contact-v30/README.md). Identical original
archived executable/complex assets are not established.

Both final native runs finish day 330 with twenty whole leaves, seven reaching
50% and five reaching 90%. Neither produces petals at any daily observation.
Tracked resources remain finite on daily checks; maximum unresolved contacts
is zero on tickwise checks. Both exported scenes contain 9,645 bytes. Timing
includes task-owned pauses and is not a full-growth performance comparison.

| Tick | Whole leaves, v30 → v31 | ≥50% developed, v30 → v31 | ≥90% developed, v30 → v31 | Petals observed in v31 |
| --- | ---: | ---: | ---: | ---: |
| 1 h | 20 → 20 | 8 → 7 | 4 → 5 | 0 |
| 4 h | 20 → 20 | 9 → 7 | 4 → 5 | 0 |

The one-hour scape remains at approximately 10.3 mm through day 305, then is
absent by day 310 without anthesis. The day-240 frozen fixture rejects both
elongation and thickening, restores its original pose and leaves the accepted
configuration clear. Numerical work exhaustion is not proof of physical confinement.

[RESULTS.json](RESULTS.json) retains count/expansion definitions and censoring;
[the comparison plot](seasonal-comparison.png) shows five-day counts through
day 330. [CHECKS.json](CHECKS.json), raw logs, the focused
[source patch](selected-source.patch) and [MANIFEST.json](MANIFEST.json) identify
the selected implementation and evidence. Candidate overwintering and a new
complete 755-day comparison have not been run in this increment.

Expansion thresholds require **both** intrinsic attachment-axis length and blade
half-width to reach the sampled target fraction. They are not area fractions or
folded-envelope sizes. A mixed shift between half-expanded and near-mature leaves
does not establish increased developed-leaf density. Completed-only durations
have survivor bias; no botanical duration is inferred from them.

The user's 'Rotblum' largest blade of 16 cm, petioles about 5 cm and pot top
diameter of 19.5 cm remain separate field observations, with unknown blade width
and initial age. No botanical parameter was adjusted to compensate for contacts.

Flowering, doubled developed density at both tick sizes, V2 shell mechanics,
complete V2 memory/allocations, large-scene multicore scaling and the repaired
FAV11/P003 panel remain open. [The research follow-up](PRECISION.md) supports robust
continuous predicates; the current bounded retry is not a converged shell solve.
