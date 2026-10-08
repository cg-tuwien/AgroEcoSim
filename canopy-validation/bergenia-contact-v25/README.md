# Certified coupled growth recovery: v25

Existing floral growth proposals now have a bounded continuation retry with
neighbour yielding. Isolated mechanical fixtures pass. Fresh CPU-light trials
still fail the Bergenia flowering and developed-leaf-density objectives.

## Implementation and scientific scope

`CollisionWorld.Continuation.cs` retries already-sampled extension and thickening
separately after ordinary fallback. Each trial restores the last certified state,
applies a dimensional increment, distributes endpoint contact corrections using
the existing compliance/leverage calculation and certifies every changed body,
including transported descendants and neighbours belonging to another plant.
Common ancestors use relative contact derivatives. Rejected trials restore all
participants exactly. Accepted motion wakes affected retry scheduling and updates
the checkpoint continuation state.
Unjoined neighbours are stationary at their already-certified current endpoints;
their earlier motion is not replayed or used as a previous-penetration exemption
for the new interval.
Contact-memory lookup uses canonical plant/stable-ID order and reverses the
remembered normal when a trial encounters the pair in the opposite direction.

The first hard pass now preserves the original physiological proposals, rather
than erasing some before the foliage solver records them. There is no resampling
of biology, extra birth, altered resource charging, species calibration or clonal
spread. Pending floral insertions do not yet enter this recovery path.

Sixteen adaptive trials per dimension and eight corrections per trial are work
budgets. Whole-joint steps are capped locally at 0.1 degrees. Exhaustion does not
prove physical confinement, and endpoint separation does not certify a sweep.
No reaction is invented for a static constraint or an uncertified foliage sweep
that has no endpoint penetration. The existing V1 shape/strain safeguards remain.
This is not a converged shell/contact equilibrium solver or BAC V2.

[C-IPC](https://ipc-sim.github.io/C-IPC/) supports coupled thin structures,
finite-thickness contact and complete-path certification. Its reference solver
was not integrated here. [RHS describes 'Rotblum'](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
as having wavy blades and stout spring flowering stems, without a stiffness ratio
or measured expansion duration. The current tissue constants are unchanged.

## Mechanical fixtures

| Fixture | Result |
| --- | --- |
| Two anchored plants, scape into neighbour's blade | About 17.97 mm of a requested 23 mm extension retained; neighbour yields; all seven organs certify; six accepted waypoints in sixteen trials. |
| Same fixture under a solid ceiling at the existing cylinder tolerance | No accepted waypoint; both plants' poses restored exactly; no invented contact correction. |
| Certified earlier motion raises the neighbour and extends the scape | The remaining 5 mm extension completes without a correction or additional neighbour motion, at both tick settings. |
| Complete 52-organ day-303 crown, manually imposed nominal scape dimensions | About 71.35 micrometres of a 913-micrometre length proposal retained; no radial gain from the 275-micrometre radial proposal; about 2.17-degree neighbouring petiole turn. |

The two-plant accepted-pose SHA256 agrees at 1 h and 4 h:
`0B66C7B562752B7979E3A61FA23E8C16911FB80D87E32C7E55115B8514447451`.
The ceiling rejection agrees at both settings:
`1BB78D13218E293CB477FD2F2554B5070B388E967EE3903554FFD864C36A9A33`.
The full-crown frozen-pose hash also agrees:
`8D7EFF8A73F1730D6A0D4FF8CED7714F75AA9040A5FC209E44D7A0BB7FC69B4D`.
These hashes include canonical organ poses, not phenology colours or raw struct
padding. The unchanged global solid-contact tolerance is 0.1 mm; flat cylinder
ends are tested using their projected section radius, not capsule endcaps.

The full-crown diagnostic requires an important qualification: saved scape ID 52
is already senescing (`flowerSupport=false`, wood ratio about 0.56). Normal
biology samples no extension for it. Its manual nominal proposal tests geometry
only. Construction charging is zero in the request; a diagnostic clone fully
funds that proposal. Neither live recovery nor resource shortage follows from
this fixture. The earlier, tighter full crown still retains only about 0.82
micrometres of a 10-micrometre proposal.

## Fresh seasonal comparison

Use the archived numerical request with seed 42, CPU light, initial date
1 September 2025, disabled clonal spread and mode `None`. Preserve the 755-day
request horizon for world/weather initialization while stopping the probes at
the stated observation day. The v24 control is an isolated source copy differing
only in this increment's collision changes and checkpoint version. Both runs
start with no organs; old snapshots are not used to claim a fresh seasonal run.

The four-hour pair runs from birth through day 330. Both observe twenty leaf IDs,
five reaching both 50% intrinsic dimensions and four reaching both 90%, with no
open petals. The scape grows to 11.64 mm in v25 versus 10.59 mm in v24, remaining
about 0.366 mm in radius against a 5.58 mm sampled target. It then senesces.
Leaf threshold times are identical between versions: 43, 63, 74, 97 and 211 days
to both 50% dimensions; 77, 100, 112 and 150 days to both 90%. These daily
observations exclude censored leaves from the reported durations.

The one-hour pair is observed from birth through day 275; final comparison rows
and threshold times are in `results.json`. Both versions observe twenty leaf IDs,
six reaching both 50% dimensions and three reaching both 90%, with no petals.
The scape length/radius is 3.0517/1.4541 mm in v25 versus 3.0243/1.4538 mm in v24,
against targets of 15.1306/5.9038 mm. The observed half-expansion times are
52, 54, 59, 78, 86 and 127 days, and 90% times 137, 144 and 191 days, in both
versions. Thresholds require both intrinsic
length and half-width, not projected folded dimensions or surface area. First
observation and threshold times have daily resolution. Agent length is the
attachment axis, not the entire basal-lobe blade envelope. Existing 84-day growth
coefficients are retained as provisional model targets.

The seasonal v25 binary predates the final fixes for unjoined neighbours' motion
history and reversed contact-memory lookup. The final source preserves the
full-crown frozen-fixture pose hash and the prior native continuation hashes
from both mature snapshots, including removal/reindexing and 12 hours of resumed
continuation. A fresh seasonal comparison of the final source was not rerun;
these short checks do not establish full-run equivalence.

Resources remain finite and every growth tick reports zero unresolved contacts.
The final v25 snapshots export nonempty scenes and have no downward/inverted
above-ground organ axes. These checks do not imply flowering or density success.
The observed 'Rotblum' largest blade of 16 cm, petioles about 5 cm and 19.5 cm pot
diameter are a separate comparison; unknown blade width and initial plant age
remain calibration limits. No parameter was changed to fit the outcome.

## Compatibility, storage and performance

Checkpoint v25 explicitly rejects older live states. Payload layout and saved
playback formats are unchanged. Native immediate/restored and continued state
and actual exported-frame comparisons pass, including fresh and mature crowns,
removal/reindexing, an accepted synthetic recovery and rebuilt query caches.
Four-plant worker-limit comparisons preserve deterministic state/frame equality
at 1 h and 4 h; they do not measure multicore speedup. Frontend checks pass five
suites and 34 tests. The prescribed core build and isolated NativeAOT server
publish succeed with existing warnings.

Reusable continuation scratch is 116 bytes per capacity slot, allocated lazily
per participating formation. The 52-organ fixture uses 6,032 bytes in total.
The bounded participant list and temporary solver stack are separate; no mesh
copy or per-contact persistent table is added. Existing V1/V2 storage is not
included in this incremental number. Whole-tick allocation observations do not
establish the global zero-per-contact-allocation gate.

`performance-final.json` describes the final serial fixed-population benchmarks
and their raw rows. A separate manually imposed, frozen growth benchmark measures
the active recovery path, with all initial and accepted geometry checked. Timed
sections exclude initialization, restore, diagnostic queries and export. Growth
simulations and builds are idle during recorded release timings. The ordinary
tick fixtures and the active recovery fixture must not be conflated: the former
do not repeatedly impose growth on a senescing scape.

| Fixed population / work | v24 median / p95 | v25 median / p95 | Median change |
| --- | --- | --- | --- |
| Ordinary 1 h tick, 52 organs | 315.90 / 348.45 ms | 308.67 / 322.54 ms | -2.3% |
| Ordinary 4 h tick, 41 organs | 77.74 / 85.72 ms | 76.31 / 86.27 ms | -1.8% |
| Active imposed nominal growth, 52 organs, constraint solve only | 233.29 / 235.27 ms | 286.08 / 289.55 ms | +22.6% |

Values are medians of three process medians and process p95 values. Ordinary
accepted export hashes agree between versions. Active accepted length is zero
in v24 and 71.35 micrometres in v25, so the geometric work differs despite the
matched population. Active whole-solve allocation medians are 906,784/943,744
bytes, including existing solver allocations and
excluding restore. The active median/p95 increments are 22.6%/23.1%, below the
scoped 25% target. The limited margin supplies no larger-scene release claim;
nine samples per process limit tail interpretation. Small ordinary-tick timing
differences are not evidence of a general speedup.

These small fixtures do not establish the dense-stand/790-plant gate, full
755-day biological acceptance, published adversarial CCD benchmark coverage,
static equilibrium or V2 default enablement. The next unresolved issues are
coordinated multi-constraint recovery, constrained scape thickening and retained
floral insertion; a longer-lived or faster-growing preset would not establish
that the mechanical problem was solved.

## Evidence provenance

`manifest.json` records source, request, binary and snapshot hashes. Large
checkpoints stay under `/tmp`; raw compact logs and leaf tracking tables are
copied here. `implementation.patch` is this increment against the preserved
v24 source, excluding unrelated worktree changes. Rebinding an old same-layout
snapshot header is diagnostic-only and is explicitly labelled in those logs;
it is not a supported checkpoint migration. Diagnostic harnesses remain outside
the repository, with their provenance recorded in the manifest.
