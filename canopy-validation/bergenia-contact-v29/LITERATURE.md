# Midterm contact and animation cross-check, 7 October 2026

**Historical experiment, reverted.** The smaller correction target discussed
below was tested and rejected: it did not recover seasonal flowering and made
V1 certification substantially more expensive. The final implementation retains
the original target. See the [v30 cross-check](../bergenia-contact-v30/LITERATURE.md)
for the retained collision fix, additional primary literature and reference CCD
validation. This file records the investigated hypothesis, not current behavior.

This review accompanies the v29 floral-contact recovery increment. It checks
the current reduced-joint V1 work and the remaining V2 direction separately.
The papers supply numerical methods; they do not supply Bergenia material
parameters or validate the biological expansion trajectory.

| Primary source | Relevant finding | Assessment of this implementation |
| --- | --- | --- |
| Huang et al., [Geometric Contact Potential](https://arxiv.org/html/2402.00719v3), 2025 revision, sections 6.1.1–6.1.4 | A fixed contact range can cause spurious repulsion and rest stress; contact locality and orientation matter. | This supports investigating unnecessary separation of already admissible crown contacts. Our smaller correction target is an engineering inference, not this paper's potential or an intersection-free proof. |
| Andrews, Erleben and Ferguson, [Contact and Friction Simulation](https://erleben.github.io/pubs/2022/andrews.ea22/andrews.ea22.pdf), SIGGRAPH 2022 course, pp. 72–74 and 142–143 | Complementarity residuals must include inactive inequalities. A work budget does not establish convergence. CCD must constrain the accepted optimization path. | The V1 tangent checks residuals and angular inequalities; every accepted continuation waypoint retains endpoint and swept-motion certification. Rigid mode still uses its earlier bounded directional response. Neither is a full equilibrium solve. |
| Ferguson et al., [Intersection-free Rigid Body Dynamics](https://ipc-sim.github.io/rigid-ipc/), SIGGRAPH 2021 | Rotation follows curved trajectories; endpoint-linear CCD alone is insufficient. | Retain conservative trajectory subdivision and common-parent relative motion. Shrinking a correction target must never replace path certification. Formal equivalence to the paper's CCD is unestablished. |
| Li, Kaufman and Jiang, [C-IPC](https://ipc-sim.github.io/C-IPC/), SIGGRAPH 2021 | Rods, shells, thickness offsets and strain constraints can be coupled through one energy with conservative advancement. | Still the relevant V2 framework. V1 continuation only seeks certified development through bounded elastic corrections; it does not minimize that energy or demonstrate shell equilibrium. |
| Chen et al., [Offset Geometric Contact](https://graphics.cs.utah.edu/research/projects/ogc/), SIGGRAPH 2025 | Normal offsets and vertex-specific movement bounds permit local parallel contact updates under the method's assumptions. | A useful performance comparison for V2. Do not substitute its local bounds for articulated-organ CCD without deriving and testing the required bounds. Published cloth/GPU speedups are not estimates for this simulator. |
| Chen et al., [Better Bending author code](https://github.com/evouga/better-bending) and [libshell](https://github.com/evouga/libshell), SIGGRAPH 2026 | The author implementation provides shell energies, derivatives and bending models, including BAC in libshell. | BAC remains the chosen V2 research direction. Current V1 controls are not BAC. The author repositories were inspected; the linked paper PDF could not be fetched during this review, so no renewed full-paper comparison is claimed. |

## Decision and limits

Continue the measured reduced-joint recovery work, while retaining conservative
acceptance and explicit failure diagnostics. Reducing a numerical separation
target is preferable here to inferring physical confinement from exhausted
correction budgets or increasing species growth/stiffness constants to mask it.
The precise v29 target is not prescribed by these sources.

The existing foliage acceptance tolerance is 10 micrometres. The old corrective
target was +10 micrometres; from an accepted contact near −10 micrometres it
demanded about 20 micrometres of displacement even for tiny new growth. The new
target is −9 micrometres, retaining a 1 micrometre reserve within the **same**
acceptance tolerance. It can retain tolerance-level proxy overlap; this is not
a new mathematical guarantee of zero physical penetration. Thickness, geometry,
query budgets and endpoint/swept tests are unchanged. Target sensitivity and
hard-obstacle refusal must be reported alongside any seasonal improvement.

For V2, the remaining requirements are still one coupled energy, converged force
balance, mesh/strain convergence, curved-path certification, reversible recovery,
and measured performance and storage. The current results do not clear those
gates or justify enabling V1/V2 by default.

## Botanical cross-check

The [RHS 'Rotblum' profile](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
describes evergreen clumps, wavy foliage and stout spring flowering stems. This
supports the qualitative leaf/scape distinction, but supplies neither a stiffness
ratio nor an allowable contact angle. No biological coefficient changes in v29.
The user's largest blade of 16 cm, petioles about 5 cm and pot top diameter of
19.5 cm remain field observations; blade width and initial age are unknown.
The 84-day model setting remains a provisional target, not a measured cultivar
expansion duration. Spring flowering recovery and developed-leaf density must be
evaluated independently.
