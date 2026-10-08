# Midterm graphics and animation cross-check

Reviewed 7 October 2026. The retained v30 change corrects continuous collision
admission. It does not implement a new shell model or establish static equilibrium.
The seasonal comparisons keep biological coefficients fixed.

| Primary source | Relevant finding | Consequence here |
| --- | --- | --- |
| Huang et al., [Geometric Contact Potential](https://arxiv.org/html/2402.00719v3), 2025 revision, sections 6.1.1–6.1.4 | Contact range and inappropriate nearby interactions can introduce rest stress and spurious repulsion. | Investigate excess separation, but measure the result. The smaller correction-gap experiment failed seasonal/performance checks and was reverted. This paper does not prescribe a negative gap or justify weakening collision acceptance. |
| Wang et al., [Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/), TOG 2021, and its [sample queries](https://github.com/Continuous-Collision-Detection/Sample-Queries) | Degenerate CCD cases require careful numerical treatment. False negatives and excessive conservatism can both damage simulation. | Remove midpoint-only and unchecked tiny-motion acceptance; test full intervals and distinguish Intersecting from Uncertified. The reference subset below is useful regression evidence, not a proof of equivalence to Tight Inclusion. |
| Andrews, Erleben and Ferguson, [Contact and Friction Simulation](https://erleben.github.io/pubs/2022/andrews.ea22/andrews.ea22.pdf), SIGGRAPH 2022 course, pp. 72–74, 142–143 | Complementarity residuals include inactive inequalities; accepted solver paths need CCD. | Keep angular inequalities, reciprocal compliance and full-path checks in experimental V1. A bounded iteration budget is not proof of physical confinement. Current reduced-joint continuation is not a converged equilibrium method. |
| Ferguson et al., [Rigid IPC](https://ipc-sim.github.io/rigid-ipc/), SIGGRAPH 2021 | Rotating bodies follow curved trajectories. | Keep articulated trajectory bounds and subdivision. An endpoint chord is insufficient for rotating supports; finite samples can falsify a bound but cannot certify its complete path. |
| Li, Kaufman and Jiang, [C-IPC](https://ipc-sim.github.io/C-IPC/), SIGGRAPH 2021 | Thin structures, contact offsets and strain limits can enter a unified energy, with conservative advancement. | This remains the appropriate V2 framework. Its strain limiter does not validate our symmetric ±2% engineering bound or supply plant tissue coefficients. Do not advertise the V1 retry as C-IPC. |
| Chen et al., [Offset Geometric Contact](https://graphics.cs.utah.edu/research/projects/ogc/), SIGGRAPH 2025 | Offset surfaces and vertex-specific movement bounds support local parallel contact treatment under the method's assumptions. | A comparison for V2 performance. Those bounds require a derivation for our articulated, growing geometry before replacing CCD; cloth/GPU timings are not forecasts for GreenFDT. |
| Lu et al., [Domain-decomposed CPU Cloth](https://sig25ddmpd.github.io/), SIGGRAPH 2025 | Domain partitions can expose useful CPU work while managing interface coupling. | Supports parallel component/element work for larger V2 systems. The four-plant deterministic fixture is too small to establish multicore scaling, and concurrent independent diagnostic processes do not constitute solver parallelism. |
| Chen et al., [Better Bending author implementation](https://github.com/evouga/better-bending) and [libshell](https://github.com/evouga/libshell), SIGGRAPH 2026 | The author code supplies shell energies and derivatives, including BAC. | BAC remains a candidate for V2 after mechanics/refinement validation. The paper PDF was inaccessible in this review; no renewed full-paper comparison or implementation-equivalence claim is made. |

## Retained decision

Use the existing 10 µm finite-thickness clearance tolerance consistently in
distance-based interval bounds. A distance certificate must reserve the full
combined material-point motion. Remove acceptance based solely on small motion
or a clear midpoint, including at attachments. Continue bounded subdivision;
return numerical uncertainty when its budget ends. Preserve the original +10 µm
contact correction target, stiffness values and solver work limits.

The distance calculation and trajectory bounds still use single precision rather
than outward-rounded interval arithmetic. The tolerance can permit small overlap
of finite-thickness proxies. These tests do not establish a mathematical
intersection-free guarantee, or a converged shell/contact equilibrium.

## Published CCD subset

Four official rational sample files contain 324 queries. Our pose model can
represent 250 translating vertex–face queries and 54 translating edge–edge
queries exactly before conversion to floats; 20 independently deforming edge
queries are excluded. Comparison applies **20 µm radius per body** and converts
rational coordinates to single precision. This is deliberately a finite-offset
regression, not the original zero-thickness benchmark.

Managed .NET 8 and NativeAOT .NET 10 both refuse all 145 represented
collision-positive queries, with zero Clear false negatives and zero
Uncertified outcomes. Of 159 originally negative queries, 126 are clear and
33 are refused; the changed offset/precision prevents interpreting the latter
as the original benchmark's false-positive rate. File hashes and query counts
are recorded with the results. The remaining published dataset and general
deforming primitive trajectories remain untested.

## Botanical limits

The [RHS 'Rotblum' profile](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
describes evergreen, wavy foliage and stout spring flowering stems. It supports
the qualitative distinction but provides no stiffness ratio, contact angle or
leaf-expansion duration. The user's largest blade of 16 cm, petioles about 5 cm
and pot top diameter of 19.5 cm are field observations with unknown blade width
and initial plant age. The 84-day setting remains a provisional model target.
No botanical coefficients were changed to compensate for stalled contact.

