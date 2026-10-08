# Attached-rod precision and remaining scape obstruction (v32)

This increment corrects attachment clipping; it does not complete the folding or
biological acceptance gates. Species dimensions, growth durations, crown rates,
light settings, material coefficients and clonal-spread settings are unchanged.

Previously, attachment clipping rounded new rod endpoints to world-space floats
before the v31 relative-double distance calculation. V32 retains original endpoints
and double parameter intervals until measurement. It also tests attachment-box
containment without rounding the box faces outward. The ordinary unrestricted
segment kernel, collision skin, motion budgets and recorded playback are unchanged.
Live checkpoint version is 32 because continuation decisions change; its payload
layout is unchanged and older live states are explicitly rejected.

The rod-pair path uses bounded stack scratch (twelve 40-byte ranges), no additional
persistent organ state, and no per-contact heap allocation. This is not a claim
that the entire existing continuation solver is allocation-free.

Validation results and source/evidence hashes are in [RESULTS.json](RESULTS.json)
and [MANIFEST.json](MANIFEST.json). Distance checks use a separate plane-partition
clipper and boundary/interior quadratic minimizer. The 6,013-case v31 run found
377 discrepancies; v32 passes those cases plus 15 near-parallel crossing cases.
The comparison allows two float ULPs or 0.1 picometre near zero. It is not a proof
of exact predicates for all inputs.

Native fixtures cover 1-hour/4-hour ticks, two/three/four contacting plants, common-parent
motion, V1 deformation, ceiling rollback, confined partial growth, and checkpoint
continuation after removal/reindexing. The recovered historical harness needed
two corrections: walls initially intersected a neighbour, and its two-row tangent
reference did not apply to the larger V1 system. Those failures also reproduced
on the unchanged v31 native binary. The repaired wall fixture permits certified
partial growth; it is not an impossible-confinement equilibrium test.

The CCD subset is the same rational source data used in v30, downloaded from the
authors and hash-verified. Only 304 of 324 cases have motions representable by
these rigid primitives. All 145 represented positive cases are refused; 33 negative
cases are also refused with the applied finite offsets. These are compatibility
checks, not equivalence to Tight Inclusion's interval guarantees. Dense motion-bound
samples can falsify a bound but cannot prove an entire trajectory.

Five alternating native benchmark pairs per fixed-population fixture give median
changes of +0.5% (single), -0.9% (radial) and -11.6% (radial V1). They do not establish
the full dense-stand/FAV11 performance gate. Existing benchmark allocations remain;
the new restricted-distance kernel allocates zero bytes over one million calls.

Fresh seasonal probes use the archived seed-42 request with CPU light, contact
mode 0 and disabled clonal spread. The four-hour day-330 result remains 20 leaves,
six past 50% expansion, five past 90%, and no petals (v31: 20/7/5/no petals).
The scape thickens and elongates farther, then stalls and expires. Changed contact
acceptance also changes later random sampling, so the different sampled scape target
is not evidence of a material-stiffness calibration. Biological acceptance fails.

| Tick | Age | v31 leaves / ≥50% / ≥90% | v32 leaves / ≥50% / ≥90% |
|---|---|---|---|
| 1 hour | day 240 | 20 / 7 / 4 | 18 / 5 / 3 |
| 4 hours | day 240 | 20 / 6 / 5 | 20 / 5 / 4 |
| 4 hours | day 330 | 20 / 7 / 5 | 20 / 6 / 5 |

Neither new run produces petals; resources stay finite and final unresolved
contacts stay zero. A new one-hour day-330 run was not performed in this increment.
The selected day-240 checkpoints and exact request are preserved in
[selected-checkpoints.zip](selected-checkpoints.zip), so the diagnostic does not
depend on the temporary probe directory surviving.

The next mechanical target is a measured discontinuity in attachment exclusion:
an additional 0.2 micrometre of elongation changes a capped-clear query to about
1.95 mm penetration. See [ATTACHMENT_STALL.md](ATTACHMENT_STALL.md). Increasing
iteration limits or tissue flexibility alone would conceal this geometry problem.
The experimental/default enablement gates remain unmet.
