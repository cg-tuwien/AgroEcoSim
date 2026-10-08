# Finite thickness at an attachment cutoff

The fresh native four-hour v32 world is admissible at day 240. Its scape (stable
ID 47) is 7.405732 mm long and 5.259694 mm in radius. Petiole 23 shares crown
node 21 (junction radius 5 mm). The attachment half-extent is 16.787107 mm.
An isolated query restores the same accepted frames before every elongation and
holds all radii, orientations, neighbours and resources fixed.

| Additional scape length | Tip relative to box face | Query clearance |
|---|---:|---:|
| 0 µm | -0.6221 µm | capped at +10 µm |
| 0.6 µm | -0.0671 µm | capped at +10 µm |
| 0.8 µm | +0.1173 µm | -1,945.7093 µm |
| 1 µm | +0.3017 µm | -1,945.7741 µm |
| 250 µm | +230.6793 µm | -2,020.7786 µm |

The positive number is the distance query's search cap, not a measured physical
gap. Complete values are in [attachment-thickness-audit.log](attachment-thickness-audit.log).
This happens between **two rods**, rather than a blade feature changing suddenly.
Both the accepted world and the no-yield trial are evaluated with the same v32
precision and the same common-parent frame.

The cause indicated by the source and this controlled experiment is the order of
operations: remove the attachment box from the centre lines, then apply the full
collision radii to the retained pieces. A fully contained centre line contributes
no collision body; an arbitrarily short protruding piece contributes a full-radius
capsule end. This construction is not the full capsule volume with its attachment
portion removed. It can introduce a large contact abruptly as the tip crosses the
cutoff, even though biological growth is continuous.

The bounded recovery path spends eight corrections per failed increment. In this
fixture the initial gap is near -1.95 mm even after the proposed growth increment
has been made extremely small. Most of the reaction is therefore spent resolving
the newly introduced capsule cap. Three direct attempts retain only about
0.37-0.61 µm of certified elongation. That is admissible partial progress, not
evidence that the original desired increment is physically impossible or that
mechanical equilibrium was reached.

The next implementation should make finite-thickness attachment geometry vary
continuously. Evaluate contact outside the justified attachment region on the
thickened geometry, preserving witnesses for both support systems. A bounded
convex-clipping/support-query approach for the six outside half-spaces is a candidate
to prototype; it needs an independent geometric reference before production use.
Do not fix this by enlarging the exemption, relaxing the collision skin, discarding
swept checks, or changing botanical dimensions. Merely shrinking a centre-line
exemption still creates an artificial cap and does not establish the required
geometry contract.

Add fixtures where a thick short scape, two petioles and a rod/blade pair emerge
through an attachment boundary, including translated, rotated and tiny cases.
Measure continuity and independent finite-volume nonpenetration. Then require
growth to resume through feasible contact, and preserve the ceiling, sibling,
common-parent, rollback and checkpoint regressions. This gate currently **fails**.

## Scientific cross-check

[Eberly's segment-distance analysis](https://www.geometrictools.com/Documentation/DistanceLine3Line3.pdf)
explains cancellation near parallelism and constrained minimum-distance searches.
V32 uses relative coordinates and a near-parallel boundary/interior fallback only
for the restricted kernel. It does not replace the public kernel with Eberly's
constrained conjugate-gradient implementation or claim its guarantees.

[C-IPC](https://ipc-sim.github.io/C-IPC/) enforces thickness offsets on rod mid-lines
and shell mid-surfaces while coupling contact and strain. It supports treating
finite thickness consistently. It does not validate our attachment-box exclusion
or the proposed convex-clipping implementation; that connection is an engineering
inference requiring its own fixtures.

[Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/)
and [Rigid IPC](https://ipc-sim.github.io/rigid-ipc/) still support conservative
whole-path checks and explicit numerical nonconvergence. Correct distance clipping
does not resolve recursive trajectory rounding or establish static equilibrium.

No new botanical claim or Bergenia stiffness value is inferred from these tests.
The existing Rotblum observations, provisional 84-day expansion setting and
published cultivar-specific material data retain their previous qualifications.
