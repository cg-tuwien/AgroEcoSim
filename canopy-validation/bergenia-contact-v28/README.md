# Reciprocal contact response and query retention: v28

Experimental V1 now solves a saturated support's angular limit against its
combined contact load. This retains sideways compliance and permits inward
release. A bounded four-way memo also makes the measured rigid common-support
retry more consistent and reduces its median by 33.6%. The four-hour seasonal
scape remains stalled. Bergenia's default remains mode 0; this is a mechanical
increment, not V2 or biological acceptance.

## Problem and implementation

The day-240 four-hour crown exposed asymmetric rows in the coupled tangent.
Joint 50 responded to one contact direction but not another: the old per-row
`ContactTurnAvailable` gate removed a saturated joint's compliance independently
for each load. A multi-contact tangent needs one shared elastic operator plus an
angular inequality. A diagnostic reproduced reciprocal terms of 0 and
-4.464814e-5 before the change.

The implementation is restricted to mode-1 floral continuation:

- [PlantSubFormation.Contacts.cs](../../Agro/Plant/PlantSubFormation.Contacts.cs)
  supplies one cached spectral angular compliance per joint, using the existing
  beam coefficients. Matrix products use double precision; accepted rotations
  retain the existing representation. Near-saturated joints supply an outward
  angular constraint relative to their transported base.
- [CollisionWorld.ContinuationLimits.cs](../../Agro/Collisions/CollisionWorld.ContinuationLimits.cs)
  projects active constraints in that elastic metric and releases them under
  inward aggregate load. It first uses the existing 64-sweep contact relaxation;
  a diagonally scaled direct working-set fallback handles at most sixteen rows.
  Pivot/rank checks reject unresolved systems without fictitious compliance.
- [CollisionWorld.Continuation.cs](../../Agro/Collisions/CollisionWorld.Continuation.cs)
  applies contact forces and angular reactions together, retaining the global
  bounded step and complete endpoint/swept-motion certificates. Angular reactions
  are moments in N m; contact reactions are forces in N, not pressure. Eight
  angular active-set updates and the direct solver's work budget are numerical
  limits, not evidence of physical confinement.
- [FoliageGeometry.Memoization.cs](../../Agro/Collisions/FoliageGeometry.Memoization.cs)
  uses four-way LRU sets instead of two-way replacement, retaining the exact same
  4096 distance and 2048 motion entries per initialized worker. Pure query keys,
  values and collision work budgets are unchanged. The previous memo showed
  large cost differences between processes with identical accepted poses;
  conflict eviction was suspected, and the new memo reduces the observed spread.

Mode 0 retains its old directional response, 64-sweep budget and no direct
fallback. An exploratory broader change reduced work in one mature diagnostic
but certified no additional growth and increased some rigid retry costs; it was
not retained. Intrinsic dimensions, biomass, species settings, light accounting,
growth durations, crown probabilities and clonal spread are unchanged.

This follows the coupled-constraint principle in [C-IPC](https://ipc-sim.github.io/C-IPC/),
but is still bounded V1 continuation recovery, not a converged BAC shell or
C-IPC energy solver. The [RHS 'Rotblum' description](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
supports wavy foliage and stout flowering stems; it supplies no stiffness ratio
or angular limit. No new botanical value is inferred from these fixtures.

## Mechanical validation

Temporary probes use seed 42, ambient light and frozen synthetic geometry,
with both 1 h and 4 h settings. NativeAOT .NET 10 and managed .NET 8 fixtures pass.
Native accepted pose hashes match across tick sizes and before/after the memo
change. This does not assert cross-runtime byte equality.

| Fixture | Result |
| --- | --- |
| Saturated support, aggregate outward load | Angular reaction active; about 0.020 rad sideways turn and <=6e-8 rad outward remainder. Forces agree with an independent constrained reference to about 2e-16 relative error. |
| Same support, inward aggregate load | Bound releases; about -0.005 rad inward and 0.015 rad sideways response. |
| Bound removes the only response direction | Both production tangent and independent reference reject; no huge force accepted from cancellation noise. |
| Two correlated contact rows, off-diagonal 0.999999 | Direct fallback returns forces [0, 0.000101] N and zero residual, matching the analytic solution. |
| Rigid radial thickening between neighbours | Final radius 5.2218754 mm, unchanged from v27; all certificates pass. |
| Two contacts sharing a crown support | Final radius 4.8562502 mm, unchanged; common support stays fixed. |
| V1 radial thickening | Full 9 mm synthetic radius target retained; all certificates pass. This exaggerated target is a mechanical fixture, not a species calibration. |
| Single foliage contact | Final scape length 29.96875 mm, unchanged. |
| Ceiling/stale foliage hint and two confining walls | No accepted growth; exact pose restoration, no phantom foliage reaction. |
| V1 checkpoint after accepted contact, leaf removal and reindexing | Immediate and continued state/frame bytes match, including rebuilt caches; no unresolved accepted contacts. |

The complete day-240 rigid crown still retains zero of a manual +1000 micrometre
length/+250 micrometre radius proposal. The final mode-0 path uses 26 trials,
128 corrections and 89 coupled sets, with all final geometry clear. An exploratory
all-mode angular solve reduced corrections to 53 and coupled sets to 14 without
certifying a waypoint. A remaining pair had about -10.5 micrometres clearance.
Neither result establishes physical impossibility; clearance was not relaxed.

## CPU-light continuation and botanical limits

The final managed **Release** .NET 8 default-mode continuation starts from the
v27 day-282 states and runs through day 285. The request retains the archived
755-day weather horizon, 2025-09-01 start, seed 42, CPU light and disabled clonal
spread. Diagnostic header promotion is confined to the temporary probe; the
production loader rejects old live states. Control and final checkpoint payloads
are byte-identical except for the version header.

| Day 285 | Whole leaves | Both dimensions >=50% | Both >=90% | Petals | Above/below-ground organs |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 h | 20 | 8 | 3 | 0 | 123 / 214 |
| 4 h | 20 | 4 | 2 | 0 | 52 / 130 |

The one-hour flower present at day 282 retires by day 285 in both versions.
The four-hour meristem remains blocked at length 0.40286416 mm and radius
1.2429563 mm. Aggregate above-ground water/energy are 56.078959/860.095754 and
25.421401/660.901977 in the simulator's units, respectively. All inspected above
and below-ground resources remain finite; unresolved accepted contacts stay zero.
Exported frames contain 16341/9738 bytes and preserve visibility.

Thresholds use the species target plus the sampled variation offset, and require
both attachment-axis length and half-width to reach the fraction. The axis is
not total blade-envelope length. Leaves already present at the observation start
cannot supply birth-to-expansion times. The 84-day setting remains provisional.
The user's 'Rotblum' observations remain separate: largest blade 16 cm, petioles
about 5 cm, pot top diameter 19.5 cm; blade width and initial age are unknown.

Earlier exploratory Debug seasonal runs and incomplete one-hour runs are excluded
from final timing and biological acceptance. No new fresh 755-day run, seasonal
V1 comparison, doubled developed-leaf density, four-hour flowering, overwintering
or balcony dimensional fit is established by this increment.

## Performance and storage

Final NativeAOT retry benchmarks run serially with growth/builds idle. Each
process performs ten warmups and 35 measured fixed-population retries. Single
contact/radial cases use three paired processes; common support/V1 use six.
The table reports medians of process medians. Raw process p95 times and measured
allocations are retained in `cache-benchmarks.json` and the accompanying logs.

| Fixed retry | v27 median ms | Final v28 median ms | Change |
| --- | ---: | ---: | ---: |
| Single contact | 0.6533 | 0.6432 | -1.5% |
| Rigid radial | 0.9572 | 0.9501 | -0.7% |
| Common support, rigid | 1.2794 | 0.8494 | -33.6% |
| V1 radial | 31.0687 | 32.7779 | +5.5% |

The common-support process medians span 0.836–2.618 ms in v27 and 0.834–0.867 ms
in final v28. Its allocation median is 40 bytes in every final process, the
stopwatch's allocation. V1 still varies from 22.0 to 37.5 ms; its allocation
medians are 19288–19720 bytes, versus 21352–22264 in v27. Other fixtures retain
existing query allocations. This does not demonstrate zero per-contact
allocations or the end-to-end <=25% release gate. Before the memo change, a
separate serial batch had a 58.9% common-support median regression and 5.4% V1
increase; those raw results are retained, not merged into the final table.

No new accepted per-leaf fields are added. New numerical payload is bounded
world/component scratch: at the 256-joint/16-contact caps, tensors, angular-limit
records, coefficients and raw matrices total at most about 60.1 KiB, excluding
collection headers. Direct fallback uses about 2.5 KiB of stack space. Mode 0
does not build these numerical arrays. Existing geometry snapshots and V1 state
remain separate from this delta. The memo keeps its entry storage unchanged and
halves its recency metadata from 3 KiB to 1.5 KiB per initialized worker.
The complete 4–8 KiB per-leaf storage gate remains unestablished.

A separate managed Release, twelve-day ambient fixture places four plants in
two independent neighbouring pairs. At 1 h/4 h it performs 81/66 final organ
clearance checks and records 907/57 interplant contact visits. Configured worker
limits of one and four yield identical state and export hashes at each tick
size, finite resources and zero unresolved contacts. The small work population
can remain serial under the existing cost threshold; this is a scheduling
regression, not proof of multicore utilization or scaling.

## Compatibility, artifacts and remaining work

Live checkpoint version **28** rejects older live checkpoints explicitly; payload
layout, scene formats and saved playback are unchanged. Fresh eight-day and
mature default-mode NativeAOT cases at 1 h/4 h have byte-identical immediate and
continued states and actual exported frames, including removal/reindexing and
cache rebuilding. They reject a v27 header explicitly.

The prescribed core build passes with zero errors and 298 existing warnings.
Isolated NativeAOT fixture, checkpoint and server publishes pass; no server was
deployed. Five frontend decoding/transform/stored-run/lamina suites pass all
34 tests. Scoped whitespace checks cover tracked and new source/doc additions.
`phase.patch` compares against this increment's starting dirty files; unrelated
worktree changes are preserved. `manifest.json` and SHA files identify copied
evidence and source. Harnesses, binaries and large checkpoints stay in `/tmp`.

Remaining work is the four-hour mature-crown certification failure, seasonal
V1 recovery, individual expansion times, full CCD/BAC gates, four-plant parallel
execution at larger work populations, dense/full FAV11 panels, multicore scaling,
allocation/peak-memory/light/archive measurements and default enablement. Those
gates remain separate from the passing numerical and compatibility fixtures.
