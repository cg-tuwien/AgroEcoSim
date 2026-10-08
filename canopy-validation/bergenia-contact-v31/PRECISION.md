# Precision reference and research follow-up

The v30 failure between attached petioles 5 and 28 is measured with a separate
double-precision quadratic minimizer. It enumerates all four boundary minima and
the admissible interior stationary point, then clips rod segments against the
attachment box. Input decimals are first converted back to their recorded
single-precision values. This is independent of the production parameter solver.
The diagnostic copy records the attachment radius directly, rather than recovering
it from a rounded millimetre value. [precision-reference.json](precision-reference.json)
retains six early failing intervals.

For the first interval, double clipping/distance on the **recorded rounded rod
endpoints** gives approximately −9.999987 µm at its start, −10.000637 µm at its
midpoint and −10.000366 µm at its end. The start lies about 13 picometres inside
the admitted side of the 10 µm boundary; intermediate points cross it by fractions
of a nanometre. This difference cannot be dismissed merely as distance-witness
rounding: the represented endpoint coordinates change too.

Evaluating the same float pose coefficients with double quaternion transforms
instead gives approximately −10.001770 µm initially. Normalizing the quaternion
coefficients defines yet another geometry. These are useful sensitivity comparisons,
not a certificate of the original continuous trajectory: different coordinate
evaluation rules and normalization policies produce different inputs at this scale.
Dense temporal samples cannot establish whole-path clearance. The current accepted
frames and witnesses remain single precision; this step does not replace their
geometry contract with an exact real-number one.

The independent segment-distance fixtures exercise translations, skew segments,
crossings and zero-length endpoints without rounding their minimizing witnesses
before measuring distance. Those establish the retained measurement correction;
they do not validate every near-parallel/degenerate geometry or robust interval
arithmetic. The existing relative parallel threshold and 10 nm segment-degeneracy
cutoff remain unchanged.

## Scientific alignment

[Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/)
documents both missed collisions and overly conservative decisions as simulation
problems. Its interval method supports treating an unresolved numerical predicate
separately from obstruction. Our float bounds and finite-offset subset do not
establish equivalence or its mathematical guarantees.

[Rigid IPC](https://ipc-sim.github.io/rigid-ipc/) models curved rigid trajectories.
Consequently, keeping exact endpoint frames does not permit replacing the complete
rotation path with its chord. The retained solver still uses trajectory bounds and
bounded subdivision. No collision skin or numerical work budget was relaxed.

The official [.NET 8 quaternion implementation](https://raw.githubusercontent.com/dotnet/runtime/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Numerics/Quaternion.cs)
uses weighted linear blending for nearly identical quaternions. An isolated
experiment evaluates that branch as start plus interpolated difference; it still
fails the historical scape fixture and is not enabled. Nor did a clipped convex-hull
plane experiment release it. Neither experiment justifies increasing softness,
ignoring a tiny motion or silently accepting a numerical work-limit failure.

The next precision gate is a consistent original-trajectory evaluation through
subdivision, with independently bounded rounding, clipping and transform errors.
Then inspect the remaining coupled constraints and physiological opportunities.
Keep the invalid-start, swept-crossing, exact-rollback and checkpoint gates. The
larger [midterm review](../bergenia-contact-v30/LITERATURE.md) still applies to V2;
this numerical increment does not implement BAC/C-IPC or claim static equilibrium.
