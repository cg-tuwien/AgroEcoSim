# Classified contact certificates: v24

This change distinguishes observed intersections from uncertified motion, retains
bounded geometric proofs after the distance-query budget, and makes the floral
nonjunction retry budget independent of biological tick duration. It does not
recover seasonal Bergenia flowering or developed-leaf density.

## Research and scope

The distinction between conservative noncertification and actual intersection
follows the reliability concern investigated by
[Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/).
Complete-path certification and coupled rod/shell contact are consistent with
[C-IPC](https://ipc-sim.github.io/C-IPC/). This is still the repository's custom
conservative solver; neither reference implementation nor its published benchmark
suite was integrated or reproduced here.

[RHS describes 'Rotblum'](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
as a clump-forming evergreen with wavy foliage and stout spring flower stems.
That supports investigating mutual evasion but supplies no stiffness ratio or
growth duration. No biological or material coefficients changed. Cultivar
observations remain separate from general species targets.

## Retained implementation

- Cache `Clear`, `Intersecting` and `Uncertified` outcomes rather than a boolean.
  Work/depth exhaustion is uncertified, never evidence of physical confinement.
- Skip redundant subinterval retries of observed intersections only when the
  attachment exemption is fixed. Retain retries when the justified exclusion
  grows, because its whole-sweep minimum can be overly conservative.
- Keep the 256-interval expensive-query cap. After exhaustion, disjoint swept
  bounds or a full finite-thickness facet-plane/arc certificate may still prove
  separation. Otherwise reject the path; no sampled gap authorizes acceptance.
- Use four nonjunction floral retry subintervals at every biological tick size.
- Advance live checkpoints from v23 to v24 without changing their payload layout.
  Older live continuation is rejected; playback formats and recorded assets are
  unchanged. No additional per-leaf persistent state is introduced.

The initially tested variant did not charge disjoint bounds against the query
budget. It increased the one-hour fixed-population median by approximately 62%
and was discarded. Its logs are retained as a rejected experiment, with timings
excluded from final release measurements. The last experimental repetition
overlapped a subsequent build and is unsuitable for timing conclusions.

A second experiment allowed up to 2,048 additional cheap hierarchy/plane visits
while keeping the original distance-query cap. A matched single-process native
check measured 309.84 to 324.68 ms at one-hour ticks (about +4.8%), with no increase
in accepted scape growth in the early, late or nominal frozen fixtures. It was
removed. This exploratory timing is not the repeated final release measurement.

## Mechanical findings and matched controls

Diagnostic console sources remain under `/tmp`; only evidence is retained here.
Frozen tests restore the identical input, initialize collision infrastructure
without a biological tick, capture accepted geometry and propose one scape
increment. A four-hour setting changes the mechanical configuration only after
capture. Private older snapshots are rebound only after strict rejection and
only for these same-layout controlled comparisons. This is not a supported live
migration or a fresh seasonal replay.

| Starting crown | Proposal | v23 and v24 accepted result |
| --- | --- | --- |
| Day 275, 52 organs | 10 micrometres of scape length | Approximately 0.82 micrometres retained; all independent certificates clear. |
| Day 303, 52 organs | 10 micrometres of scape length | Full increment retained; neighbouring petiole turns about 0.48 degrees. |
| Day 303, 52 organs | Nominal 913.34 micrometres length / 274.77 micrometres radius | Neither dimension advances; all final independent certificates clear. |

Within each version, canonical accepted organ-pose hashes match between frozen
one-hour and four-hour settings. The frozen full-scene byte hashes differ because
changing `HoursPerTick` also changes the biological time used by export. Checkpoint
continuation tests compare actual scene bytes at the same biological tick size.

The matched v23 control is copied from current source with only the five changed
files restored to their turn-start versions; `manifest.json` records hashes.
Both small-increment outcomes already existed under v23. Preliminary success of
the late-crown fixture therefore does not demonstrate an improvement from v24.

The source currently retries partial dimensions with eight bisections against
restored neighbours. It does not re-solve neighbour evasion at each intermediate
growth size. The next mechanics task is coupled, certified growth continuation,
including retained floral insertion. Iteration failure must not be labelled
physical confinement or compensated by softer tissue or faster growth.

## CPU-light biological comparison

Seed 42, render mode 3, archived 755-day request from 2025-09-01, contact mode
`None`, disabled clonal spread and all dimensions/probabilities/expansion settings
remain fixed. Compare 28-day continuations of v23 with v24 from the same accepted
starting states:

| Tick / interval | Observed biological leaf IDs, both versions | IDs observed above both 50% / both 90% intrinsic target dimensions | Open petals |
| --- | --- | --- | --- |
| 1 h / days 303–331 | 20 | 4 / 2 | 0 |
| 4 h / days 268–296 | 16 | 4 / 2 | 0 |

The one-hour scape stays at 7.049 mm before senescing by day 310. The four-hour
first floral transaction remains pending. Every tick checks unresolved contacts;
daily records check finite resources. These checks do not establish botanical
resource calibration. Biological acceptance remains failed at both tick sizes.

Threshold CSV records begin at restoration and include already-expanded leaves;
they are not measured birth-to-50%/90% expansion times. `maxBladeCm` in the raw
probe is the intrinsic attachment-axis length, not the full blade envelope;
`maxWidthCm` is `2 * Radius`. These are not folded envelope measurements.
The two intervals cover different seasonal dates and should not be interpreted
as a direct one-hour versus four-hour biological comparison. Full fresh 755-day,
overwintering, dense-stand and 790-plant evaluations remain outstanding.

## Fixed-population performance

Three independent native processes per version/tick, interleaved serially with
all builds and simulations idle. Each process warms up three times and measures
15 restored single ticks; restoration/export are outside the timed region. The
52-organ and 41-organ input populations match; their geometry need not match
across versions. Quantiles use the nearest lower order statistic.

| Population | v21 median / p95 | v23 median / p95 | v24 median / p95 | v24 versus v23 / v21 median |
| --- | --- | --- | --- | --- |
| 1 h, 52 organs | 267.33 / 280.95 ms | 311.31 / 329.02 ms | 315.73 / 321.12 ms | +1.4% / +18.1% |
| 4 h, 41 organs | 83.05 / 89.63 ms | 80.96 / 83.75 ms | 82.56 / 93.27 ms | +2.0% / -0.6% |

Both cumulative median-overhead results remain below the scoped 25% target.
v23/v24 exported frame hashes match in these fixed-population trials. Median
whole-tick allocations are approximately 995 KB and 863 KB for v24; they include
physiology and world bookkeeping, and are not per-contact allocation measures.
This does not certify dense stands, FAV11, zero steady-state contact allocations
or multicore speedup. It introduces no proportional per-organ storage.

## Verification

- Prescribed core build: zero errors, 298 pre-existing warnings.
- Isolated NativeAOT growth, performance, checkpoint and server builds pass;
  existing trim/AOT warnings remain. No running service is replaced.
- Actual crossing versus exhausted-budget classification, including a warm cache,
  passes. Zero-budget facet-plane proofs accept separation and reject crossing.
- 499 analytic tilted-facet and 500 analytic capsule cases: no unsafe acceptance
  or conservative rejection. 10,000 finite-search triangle checks against the
  exhaustive distance calculation: zero mismatches. Tiny curved leaves, basal
  lobes, thickening, rotating seam escape and common-parent sweeps pass.
- Four-plant 12-day ambient fixtures: two independent contact pairs, all 81 final
  independent organ certificates clear, finite resources and zero unresolved
  contacts. Worker limits one/four produce identical state/frame bytes at both
  tick sizes. Scheduling can keep cheap jobs serial; this is not a speedup claim.
- Fresh and mature/reindexed NativeAOT checkpoint cases at both tick sizes:
  immediate and continued state/frame equality, rebuilt caches and old-v23
  rejection pass.
- Five frontend decoding/transform/stored-playback suites: all 34 tests pass.
- Scoped `git diff --check` passes. Production diffs and source hashes are retained.

No default V2 enablement or biological release acceptance is justified by these
results. The change is bounded certification handling, not recovered crown growth.
