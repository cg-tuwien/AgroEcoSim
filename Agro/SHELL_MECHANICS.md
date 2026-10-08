# BAC shell implementation and acceptance status

Status, 2026-09-30: **shell/petiole energy coupling, bounded state storage and standalone curved-path CCD implemented;
production integration has not passed the compact-mesh validation gate.** This is not a released `BroadLeafV2`
pipeline, and it does not fix the seasonal growth/flowering failures in run
`e9bd78f470cd4e36bc6b715c50319d44` yet.

`ContactDeformationMode` now defaults to `None` (0), correcting the discrepancy
between the previous source and its opt-in documentation. Explicit 0/1 settings
retain their meanings. There is deliberately no selectable mode 2 until the
shell can pass the first integration gate. V7/V8 playback and live checkpoint
version 6 are unchanged by this stage; V9 and live checkpoint v7 are outstanding.

## Implemented numerical components

- `Plant/Leaf/Shell/BacShell.cs`: immutable, rest-curved BAC lamina with double
  precision positions and one edge-director angle per edge; separate plane-stress
  StVK membrane and BAC bending coefficients. Biological dimensions supply rest
  geometry; these routines do not change physiology, growth or organ counts.
  `EnergyTerms` exposes membrane, bending and strain-barrier contributions from
  that same expression graph, optionally per face, without new equations or
  steady evaluation allocations.
- `ShellDifferentiation.cs`: shared scalar expression graphs with analytic chain
  rules, including exact first and second derivatives and direct Hessian-vector
  products. Each worker reuses its evaluation arrays. Production derivatives do not use finite differences.
- `ShellEquilibrium.cs`: quasistatic Newton iteration, strain-admissible Armijo
  paths, local positive-semidefinite Hessian projection, matrix-free PCG and
  topology-aware incomplete Cholesky. The optional sparse direct reference path
  retains the exact Hessian when it is positive definite. Work exhaustion returns
  `Unconverged`; it is never reported as physical obstruction. A solver's candidate
  state is not an accepted simulation state.
- `ShellSparseFactor.cs`: deterministic symbolic/numeric Cholesky for small
  reference fixtures. Numerical elimination uses left-looking column updates
  with reusable dense scratch, precomputed row incidence and element-to-factor
  bindings, replacing repeated sparse-row intersections and assembly lookups.
  This is not yet a validated production contact-component preconditioner.
- `ShellFactorPattern.cs`: immutable symbolic factor data shared by a mesh
  profile, including element assembly slots. Small fixtures keep minimum-degree
  ordering; larger references use geometric nested dissection and elimination-tree
  reach. Numeric factors remain reusable worker workspace. Direct-reference
  workspaces no longer also construct an unused IC(0) graph.
- `ShellDofOrdering.cs`: the same deterministic minimum-degree/geometric ordering,
  exposed separately for independent sparse reference solves. It builds no numeric
  factor or per-organ cache. Extraction preserves the complete permutation.
- `ShellMesh.cs`: immutable, identified rest profiles and uniform reference
  refinement. Optional cubic interpolation samples one smooth height field through
  the authored vertices at every refinement level. This is geometric reconstruction,
  not additional measured Bergenia curvature; legacy meshes are unchanged.
- `ShellMesh.Adaptive.cs`: offline conforming refinement of explicitly selected
  edges. Incident faces share midpoints and preserve original vertices, winding,
  material boundary segments and ancestry. Adaptive descendants share the original
  immutable height field. Existing uniform profile hashes remain byte-identical;
  adaptive profiles use a separate internal hash version 2. This is not scene V9,
  checkpoint v7 or a selectable deformation mode.
- `ShellSurfaceProlongation.cs`: rest-preserving normal/polar position transfer,
  analytic first/second derivatives and energy pullbacks, with frame reuse for
  bulk evaluation. Caller-provided correspondences are explicit; this is not the
  full PSQ method or an accepted coarse/fine constitutive approximation.
- `ShellProlongedEnergy.cs`: exact reference energy on the prolonged fine surface,
  with independent fine BAC directors, analytic gradient and matrix-free Hessian
  products including geometric stiffness. It reports coarse and fine positional
  residuals separately from director torques. This is a reference kernel, not a
  compact state, nonlinear equilibrium solver or production contact integration.
- `ShellSubspaceEnergy.cs`: exact fine BAC energy restricted to an immutable,
  mechanically supplied position/director basis. It provides analytic gradients,
  matrix-free Hessian products, full force/torque residuals and affine-path strain
  checks. Independent face batches use reusable scratch and ordered reductions;
  SIMD projection preserves the fine-row summation order. A compact coefficient
  vector is not proof of adequate contact mechanics or the complete storage gate.
  Optional packed element tangents amortize repeated products at one state;
  their payload requires an explicit worker budget and is disabled by default.
- `ShellStaticCondensation.cs`: exact state-dependent interior elimination for
  Newton reference solves, with retained-coordinate Schur products, corrected
  residuals, full-step reconstruction and physical fine residual diagnostics.
  It uses explicitly budgeted worker arrays/factors, not per-organ fine caches.
  Nonpositive interior tangents and inaccurate solves are numerical failures.
  This is not a compact-state decoder or a production contact solver.
- `ShellPlaneContact.cs`: coupled frictionless infinite-plane barrier fixtures,
  analytic normal gradients/Hessians and reaction forces in newtons. Positive
  endpoint gaps certify the linear path against an infinite plane only. These
  fixtures do not implement finite-triangle, self-contact or curved-support CCD.
- `PetioleBeam.cs`: three-span circular beam with a basal bend and two internal
  bends; six angles in total. Compliance quadrature matches small-deflection
  end-moment, transverse-tip-force and uniform-load beam responses.
- `PetioleAttachment.cs`: shared tip-frame kinematics and analytic first/second
  derivatives. Each active worker prepares these once per rod configuration and
  reuses them across blade vertices and contacts, including basal lobes.
- `ShellPetioleEnergy.cs`: one objective combining local BAC elasticity, petiole
  elasticity, distributed/vertex loads and plane barriers. Its exact matrix-free
  Hessian includes blade/rod cross terms and rotational geometric stiffness.
  This is an energy kernel, not yet the production contact-component solver.
- `ShellStateStorage.cs`: bounded shell/rod state and contact-witness storage with
  post-encoding validation. Details and measured allocations are below. This
  internal representation is not a V9 scene or v7 live checkpoint format.
- `ShellStrainPath.cs`: whole-interval strain-domain checks for linear vertex
  motion. The two metric bounds are positive definite only if their leading
  minors and determinants stay positive. Quadratic minima and recursively
  subdivided quartic Bernstein bounds test this condition. Roundoff padding uses
  operands before cancellation; unresolved intervals reject the numerical step.

- `ShellContinuousCollision.cs` / `ShellCollisionMesh.cs`: linear VF/EE inclusion
  predicates and immutable topology/BVH with reusable swept bounds.
- `ShellSupportMotion.cs`: outward-rounded enclosures of the actual composed
  three-span petiole and blade trial path; explicit interpolation contract below.
- `ShellCurvedCollision.cs` / `ShellCurvedMeshCollision.cs`: curved VF/EE and mesh
  predicates, common-frame cancellation and a bounded support-transform cache.
- `ShellSupportPlaneCollision.cs`: full-path half-space checks for the standalone
  coupled objective. Neither a positive endpoint gap nor a chord substitutes for
  this check when the supporting petiole rotates.

For face metric `a`, rest metric `a0`, BAC curvature `b` and rest curvature `b0`,
the energy uses `M = inverse(a0) * (a-a0)` and
`B = inverse(a0) * (b-b0)`. The constitutive quadratic is
`lambda/2 * trace(X)^2 + mu * trace(X*X)`, scaled by `A*h/4` for membrane and
`A*h^3/12` for bending. Each BAC edge curvature contribution is
`2 * altitude * tan(dihedral/2 + orientedDirector)`. Rest curvature is preserved,
including the original basal lobes and attachment vertex.

Both principal stretches must lie strictly between 0.98 and 1.02. A spectral
log barrier enforces that domain. It is expressed using `C-I` and a stable
`log(1+x)-x` evaluation: direct subtraction of nearly equal determinants caused
an energy noise floor and unnecessary Newton stagnation near equilibrium.
This numerical correction changes neither the material nor the strain bound.
The barrier scale is provisional. Its sensitivity and a complete contact/strain
continuation strategy have not been validated.
The path check now rejects endpoint-valid motions that violate the strain bound
in between, including a 180-degree rotation interpolated through a collapsed
triangle. Its floating-point padding is conservative engineering protection,
not an outward-rounded interval-arithmetic proof. It does not test contact,
director-angle singularities or curved support trajectories, and must not be
presented as a Tight Inclusion collision certificate.

Curved-rest director fitting follows Better Bending section 8.1: hold vertices
fixed, minimize the zero-rest-curvature bending objective over directors, then
construct the actual curved shell using those directors and its computed rest
curvature. `ForRestDirectorFit` is only this preprocessing objective. It must not
be used as the physical energy of the curved blade. The regular constructor also
accepts explicitly supplied rest directors; omitted directors are zero.

Element derivatives and Hessian projection can run concurrently inside one
lamina. Workers write disjoint element arrays; the global reduction is in fixed
face order. One-worker and multi-worker solves gave identical numerical outputs.
The force residual is the maximum free nodal force or director moment divided
by characteristic blade length, expressed in equivalent newtons; it is not a
contact-reaction or pressure measurement. The iteration has no `HoursPerTick` input. Phase counters distinguish derivative,
factorization, linear-solve and line-search work; projection time is a subset of
factorization time on the direct path, not an additional disjoint phase.
When a large Newton step reaches equilibrium, convergence may use the next
correction's displacement norm. Requiring another Armijo decrease below floating
point resolution incorrectly returned `Unconverged` on a frictionless sliding
fixture. The force criterion still has to pass; a small correction alone is not
success. `EvaluateForceResidual` rechecks the exact restored state without a
factorization or allocation.

## Material inputs and botanical provenance

New numeric species fields are preserved by the existing request/serialization
path and generated UI:

| Field | Units | Default / resolution |
| --- | --- | --- |
| `LeafMembraneElasticModulus` | Pa | 0: `LeafElasticModulus`, then `GreenElasticModulus` |
| `LeafBendingElasticModulus` | Pa | Same fallback, independently overridable |
| `LeafMechanicalThickness` | m | 0: existing 0.0001 m tissue assumption |
| `LeafPoissonRatio` | dimensionless | 0.3, provisional; must be in (-1, 0.5) |

The UI explicitly labels these as reserved for shell validation: **modes 0 and 1
do not consume them.** Regeneration also supplies defaults when loading older
species settings and avoids the old generic “Bending” UI rule that would cap a
bending modulus at 1 Pa. Mechanical thickness is independent of rendered relief,
contact offset, the midrib keel and physiological mass.

The lamina-only fixtures below retain the current fallback modulus (100 MPa for
both terms), thickness 0.1 mm and Poisson ratio 0.3. They do not yet include the
existing midrib reinforcement or petiole/support compliance, so they are not a
whole-leaf biomechanical calibration or a fair seasonal V1/V2 comparison.

[Onoda et al., 2015, Table 1](https://repository.kulib.kyoto-u.ac.jp/server/api/core/bitstreams/85728bca-0f83-4ccc-8c6c-03f22741dc77/content)
reports mature Bergenia **'Abendglut'** lamina tensile modulus 8.3 MPa, effective
bending modulus 23.2 MPa and thickness 0.426 mm, with five replicates. Samples
were fully developed, kept hydrated and cut between major veins. These support
separate stretching/bending inputs; they do not establish properties of
'Rotblum', young leaves or the reinforced midrib. A material-resolution check
verified independent inputs for this sensitivity case; no seasonal sensitivity
run has been performed. The ±2% strain bound and Poisson ratio are engineering
parameters, not measured Bergenia biology.

The user's 'Rotblum' measurements remain separate observations: largest blade
16 cm from petiole junction to tip, petioles about 5 cm, pot top diameter 19.5 cm.
Blade width and initial plant age remain unknown. No dimensions, 84-day growth
duration, crown probabilities, flowering parameters or clonal spread settings
were tuned in this stage.

## Numerical evidence

The equations were checked against the authors' open implementation,
[libshell](https://github.com/evouga/libshell), commit
`6072d9e4b3c9f99c8cfe538b8b5560954d87dfdf`. The independent C++ comparison ran
outside this repository; no native library or source dependency is shipped.

| Check | Result |
| --- | --- |
| Native Bergenia topology | 114 vertices, 178 triangles, 291 edges; 633 scalar DOFs |
| Rest energy and force, stable barrier | Exactly zero for the fixture |
| Rigid rotation + translation | Energy below 7e-28 J; maximum gradient about 1.6e-12 |
| Independent C++ energy comparison, strain barrier disabled | Relative error 1.29e-16 |
| Independent C++ gradient comparison | Maximum absolute difference 6.94e-18 |
| Central differences, every native face | Max gradient error 2.51e-8 (scale 0.0104); Hessian error 1.00e-4 (scale 24702) |
| Single-triangle and two-triangle boundary fixtures | Derivatives and Hessian-only calls pass |
| 10% imposed axial stretch | Rejected as `InadmissibleStart` |
| Flat strip, 12 then 24 longitudinal cells | Cantilever error against Euler–Bernoulli drops from 14.2% to 6.86%; further refinement needed |
| Serial / parallel element evaluation | Identical energies, residuals, iteration counts and displacement |

The cantilever has length 0.15 m, width 0.005 m, thickness 0.0001 m, E=100 MPa,
nu=0, two transverse cells and a total transverse end force of 1e-7 N. Two basal
vertex rows are held fixed. The comparison uses the free length after that
clamped strip. This demonstrates a convergence trend, not a precision compliance
certificate, and does not validate the three-span petiole.

### Native mesh gate: not passed

Use the corrected physical Bergenia surface at nominal adult dimensions. Fix
vertices 0, 24 and 25 (the original artificial clamp), apply 1e-5 N to the positive
transverse flank in the surface-relief direction and solve. Uniformly refine
each triangle into four using edge midpoints, preserve the same load vertex and
fix midpoints of already clamped edges. Fit rest directors for each mesh.

| Mesh | Newton steps | Flank displacement | Force residual | Principal stretch range |
| --- | ---: | ---: | ---: | --- |
| Native 114 / 178 | 33 | 4.247379 mm | 4.23e-13 N | 0.999994968–1.000004605 |
| Refined 405 / 712 | 18 | 2.122734 mm | 4.90e-13 N | 0.999985898–1.000016253 |

The approximately 50% compliance change remains after consistent clamping and
director fitting. Zero-initialized directors give a similar discrepancy. This
is not budget exhaustion or strain-bound clipping. It does **not** prove BAC is
unsuitable, nor that the refined result is botanical truth. The native surface
has not met the required mesh-validation gate. Do not hide this discrepancy by
changing tissue stiffness or relaxing the strain bound.

Further isolation on 29 September gave these converged displacements:

| Diagnostic | Native | First refinement | Second refinement |
| --- | ---: | ---: | ---: |
| Legacy material, 1e-7 N flank load | 42.3032 µm | 21.2561 µm | 15.1363 µm |
| Legacy material, distributed 1e-5 N load | 2.90588 mm | 1.37279 mm | Not run |
| Legacy material, clamp junction one-ring, 1e-5 N | 2.51024 mm | 1.43748 mm | Not run |
| Legacy material, alternate triangulation, 1e-5 N | 4.42451 mm | 2.21373 mm | Not run |
| Published Abendglut sensitivity, 1e-7 N flank load | 3.59698 µm | 3.12040 µm | 3.05420 µm |

Second refinement has 1,521 vertices and 2,848 triangles. Distributed loads use
triangle area weights on vertices with transverse coordinate above 3 cm;
refinement also refines that quadrature. The alternate triangulation uses
projected Delaunay edge flips. These tests show the discrepancy persists beyond
a single concentrated load, force amplitude, clamp or triangulation. They do not
identify its root cause conclusively. Flattened and normal-interpolated reference
surfaces were also diagnostic experiments, not changes to the authored mesh.

The published-material case is labelled separately: it keeps Poisson ratio 0.3
but uses the measured mature Abendglut moduli and thickness. Its last refinement
changes compliance by about 2.1%, whereas the legacy case still changes by about
28.8%. No production material, geometry or species values were changed to obtain
this result; it does not establish Rotblum mechanics or pass the primary gate.

Refining the production state is also a budget decision. The latest user limit
is **about 8 KiB per enabled leaf/petiole pair, with 4 KiB preferred**. The storage
implementation enforces 8,192 bytes, including its witnesses and bookkeeping.
The dense 35,724-byte float state is a reference only. Moreover, counting floats
alone was optimistic: rounding all vertex coordinates can spoil force balance.
See the precision audit below. Shared immutable assets and bounded worker scratch
are separate; per-organ rest data, rollback copies or caches must not be hidden
in that category. Integration has not yet established the complete runtime budget.

### Smooth reference and compact-mesh comparisons

Refining straight facets and resampling a smooth rest surface are different
models. `CubicHeightField` fits a zero-smoothing cubic radial-basis interpolant
plus an affine polynomial through the 114 authored samples. Every reference
level samples that same field, rather than repeatedly refitting it. Original
vertices, attachment and the projected outline remain exact. The maximum new
height difference from the faceted surface is about 1.105 mm. This interpolation
choice needs profile versioning if adopted; it is not a silent playback change.

An independent SciPy sparse solve of the assembled analytic derivatives gave:

| 1e-7 N load | Level 2 displacement | Level 3 displacement | Relative change |
| --- | ---: | ---: | ---: |
| Positive transverse flank | 10.14372 µm | 9.94348 µm | 2.01% |
| Opposite flank | 14.57879 µm | 13.96685 µm | 4.38% |
| Distal tip | 24.35649 µm | 23.48461 µm | 3.71% |
| Whole blade, area-distributed; observe first flank | 3.71742 µm | 3.66577 µm | 1.41% |

This passes a provisional 5% successive-refinement comparison for these small
loads. It does not establish large-load convergence, buckling, contact behaviour
or botanical accuracy. The original faceted surface converges to a different
small-load result: level 4 gives 12.96487 µm on the first flank.
The level-2 smooth reference also reached 128.106 mm flank displacement at
1e-3 N with principal stretches 0.997866–1.003403; release recovered vertex
coordinates within 7.86e-13 m. Self-contact was not checked. Its largest load and
release took tens of seconds, excluding setup: this is not a production solver.

Smaller geometry candidates preserve the original samples, add triangle centroids
or split edges, and apply projected Delaunay flips before sampling the same smooth
field. They have **not** passed the accuracy gate. A 292-vertex, 1,701-DOF candidate
uses 6,804 bytes even with all-float values and predicts 11.74672 µm at the first
flank, about 18.1% above the fine reference. Adding 30 samples near the attachment
gives 322 vertices and 11.10317 µm, still about 11.7% high. Pure longest-edge
refinement performed worse. None was installed as a production mesh. Small memory
use alone cannot authorize a stiffness change.

### Attachment boundary and compact candidates

The original clamp holds two basal-lobe vertices about 14 mm from the junction.
It remains a useful artificial boundary condition but is not a measured petiole
attachment patch. A separate diagnostic inserted five samples 3 mm from vertex 0,
held that small patch, freed the two lobe vertices, and refined the same smooth
field. At 1e-7 N flank load, levels 0/1/2/3 gave 31.52570 / 15.82493 / 16.27670 /
19.45782 micrometres. The last change is about 19.5%; apparent convergence at the
previous pair of levels was misleading. The 3 mm patch itself is provisional;
it is not a botanical measurement or a species-setting change.

Compact adaptive sampling also remains unaccepted: a 199-vertex candidate chosen
by area-weighted interpolation error gives 17.48381 micrometres, while adding
another 16 samples changes it to 23.42004 micrometres. Other sample-selection rules
vary further. Matching a single load accidentally is insufficient. These results
rule out treating the earlier smooth-reference comparison as a universal compact
mesh certificate. No candidate was substituted into served/rendered geometry.

A further boundary-condition diagnostic fixes the BAC directors only where both
adjacent triangles are wholly in that attachment patch. Levels 0/1/2/3 give
31.51857 / 15.81815 / 16.26314 / 19.42413 micrometres: the director constraint
barely changes the discrepancy. Level 4, with 24,209 vertices, gives 20.56661
micrometres (force residual 9.63e-13 N). The last difference is still 5.56% of the
finer result, outside the provisional 5% refinement gate. This diagnostic does
not justify changing production attachment, tissue modulus or mesh. The fine
solve uses temporary reference storage, not a proposed per-leaf state.

### Energy-resolved mesh accuracy and local refinement

The additional diagnostics leave the primary material, strain bound, authored
samples and biology unchanged. They extract energy components directly from the
assembled scalar graph. In the 3 mm attachment fixture, the strain barrier is
only 0.028–0.312% of total elastic energy across all four loads and levels 0–4;
principal stretches remain close to one. Strain-bound clipping therefore does
not explain this small-load mismatch. At level 2, about 26.0% of the first flank
load's energy lies within 6 mm of the junction. The four-iteration rest-director
fit has a maximum remaining director gradient below 1e-18 N m across these
references. These diagnostics localize the discrepancy without treating the
provisional attachment radius as botanical data.

The independent sparse reference now covers all four loads at level 4:

| Load, 1e-7 N total | Level 3 displacement | Level 4 displacement | Change relative to level 4 |
| --- | ---: | ---: | ---: |
| First flank | 19.42413 µm | 20.56661 µm | 5.555% |
| Opposite flank | 31.28812 µm | 32.49524 µm | 3.715% |
| Tip | 98.59780 µm | 102.88043 µm | 4.163% |
| Area-distributed; observe first flank | 14.04847 µm | 14.78905 µm | 5.008% |

Two comparisons still miss the provisional 5% reference-convergence gate.
Level 4 is a numerical reference, not exact physical or botanical truth.

An analytic half-cylinder fixture follows the geometry in Better Bending section
8.2 (radius 32.5 mm, height 122 mm), with the unchanged primary numerical material.
Both mesh directions refine together; only directors relax while the cylindrical
vertex positions are prescribed. Its bending energy converges to
`E_b h^3 pi H / (24 (1-nu^2) R)`: relative errors at 8/16/32/64 cells per direction
are 18.065%, 9.406%, 4.802% and 2.426%. The coarse mesh also stores substantial
spurious membrane energy because straight triangle chords are not exactly
isometric. Bending and membrane errors must be inspected separately; agreement
of their sum can hide cancellation. The paper's benchmark likewise isolates
bending. This test is not a large-fold or Bergenia compliance acceptance test.

`RefineEdges` supplies a reusable red/green refinement operation, with one midpoint
per selected edge and consistent splits on both incident faces. Requests have an
explicit vertex ceiling; duplicate/reversed input edges normalize deterministically.
Nonexistent edges, over-budget requests, nonfinite points and degenerate resulting
faces fail explicitly. It is preprocessing, not dynamic remeshing. The same
original height field is retained once per profile and shared by descendants;
no mesh hierarchy or added scratch is stored per biological leaf. Original uniform
profile identities and served leaf assets are unchanged. Full state cost must
still be checked with `ShellStateStorage.RequiredBytes`, not vertex count alone.

Two offline selection experiments combined normalized energy from the four
load cases: area-weighted centroid insertion with constrained edge flips, and
longest-edge bisection using `RefineEdges`. Neither passed compliance acceptance.
The centroid trials give:

| Vertices | Accounted state | Actual allocation | Largest response difference from level 4 |
| --- | ---: | ---: | ---: |
| 219 | 7,016 B | 6,992 B | 6.956% |
| 239 | 7,616 B | 7,592 B | 12.744% |
| 249 | 7,920 B | 7,896 B | 12.551% |

These counts include six double rod angles, eight contact-witness slots and
128 bytes of bookkeeping, using Fixed48 positions and float directors. All twelve
small-load states pass post-encoding force/strain validation; the largest force
residual is 2.287e-10 N against a 1e-9 N threshold. Repeated updates allocate zero
bytes. This is not full live-contact/checkpoint acceptance, and the 4 KiB target
is not met by these candidates. All-float positions fail badly (up to 5.263e-4 N),
so apparent savings from float-only coordinate counts remain unacceptable.

Bisection trials with 219/239/249/257 vertices also fail: their worst response
differences are 13.815%, 33.610%, 33.062% and 28.449%. Their candidate height-field
refit differs from the original reference by at most 5.686 nm at level-3 samples;
this separate reconstruction detail is recorded rather than silently equating
the profiles. A selection rule based on energy without an area factor collapsed
triangles and was rejected before any mechanical solve. No trial mesh was adopted.

The useful result is a verified refinement/diagnostic path and a measured separation
of the storage and accuracy limits. Simply adding samples up to the memory ceiling
does not guarantee improved mechanics. Continue with error-controlled approximation
and independent load/refinement checks; do not select a candidate because it
accidentally matches one displacement or change tissue stiffness to force a pass.
[Progressive Shell Quasistatics](https://pcs-sim.github.io/psq/) provides a research
reference for consistent coarse/fine curved-shell representations; its nonlinear
prolongation is **not implemented** by this midpoint refinement utility.

Validation: 349 energy checks cover analytic membrane extension, bending convergence,
thickness scaling, rigid-motion invariance, inadmissible states and allocations;
6,144 mesh checks include all two-triangle edge-selection patterns, seams, area,
ancestry, preserved samples, input order, budgets and unchanged uniform identities.
Both suites produce identical numerical output in managed .NET and NativeAOT.
The prior coupled rod/plane load-release fixture remains numerically unchanged.

### Rest-preserving coarse/fine transfer and the remaining stiffness error

`ShellSurfaceProlongation` implements the position-transfer kernel in equations
4–7 of [Progressive Shell Quasistatics](https://pcs-sim.github.io/psq/psq-combined.pdf):
a barycentric material anchor plus its stored normal and in-plane offsets,
transported by the current triangle normal and the rectangular polar rotation.
A closed-form 2x2 inverse square root avoids differentiating an SVD at repeated
singular values. Analytic Jacobians, coordinate Hessians and point-energy
pullbacks include geometric stiffness; interactions between different samples
also require the documented cross-sample Hessian blocks.

This is a kernel, **not the full PSQ method or an accepted compact shell model**.
The caller supplies material correspondences. Intrinsic decimation/parameterization,
BAC director reduction and safe refinement are not implemented here. The exact
fine-energy reference added below retains independent fine directors; it does
not supply an accepted compact approximation. The paper's refinement procedure checks contact and strain;
rest recovery and correct derivatives alone do not provide those guarantees.
In particular, neither a linear chord between prolonged endpoints nor a coarse
mesh's clearance certificate certifies the intervening fine-surface path.

All bindings are immutable profile assets. Bulk transfer prepares each current
triangle frame once in reusable worker storage. It introduces **zero additional
persistent bytes per organ**, and never stores a hidden fine mesh per leaf.
For the 119-control-vertex / 186-face attachment fixture and 24,209 target vertices,
the original transfer-only measurement allocated 1,169,560 B for the shared
binding profile, 68,400 B for its worker, and 581,040 B for the caller's fine-position
buffer. The later directional-derivative graph adds worker scratch; the complete
reference worker is measured separately below.
These are explicitly separate from the 4 KiB target / 8 KiB ceiling on accepted
leaf/petiole state. Repeated bulk evaluation allocates zero bytes; a warmed
100-evaluation sample averages 0.218 ms, versus 18.1 ms for one pass through the
individual-sample API. These local measurements do not establish the end-to-end
25% performance gate or multicore scaling.

Validation includes 12,222 deterministic assertions with identical numerical
output in managed .NET and NativeAOT. They cover rest recovery, arbitrary rigid
motion, tiny scales, repeated singular values, first/second derivatives,
point-energy and cross-sample Hessian assembly, invalid geometry, stale-output
clearing, frame refresh and zero allocation. Maximum finite-difference errors
are 1.246e-11 for the Jacobian and 4.335e-10 for coordinate Hessians; an independent
NumPy SVD comparison differs by at most 1.388e-17 m. On all four Bergenia reference
loads, single and bulk APIs agree at every fine vertex; rest recovery error is
5.552e-17 m. No constructor accepts overflowing or degenerate rest frames.

The critical accuracy experiment restricts the existing level-4 equilibrium to
its original 119 retained control vertices, then reconstructs the fine positions.
It therefore measures loss from the transfer, independently of solving a new
coarse equilibrium. Correspondences in this experiment use the projected XZ
triangles, **not PSQ's intrinsic decimation map**. Roundoff in saved float geometry
puts some projected boundary weights at -2.014e-6; preprocessing clips and
renormalizes them, and stores the exact rest offset. The runtime kernel itself
rejects out-of-triangle weights. The original primary material and all biological
parameters remain unchanged.

| Load | Relative displacement RMS error | Fine membrane energy / reference | Energy at coarse interfaces |
| --- | ---: | ---: | ---: |
| First flank | 0.239% | 3,728x | 97.9% |
| Opposite flank | 0.259% | 2,839x | 96.6% |
| Tip | 0.204% | 4,747x | 97.8% |
| Distributed | 0.204% | 4,494x | 97.9% |

The RMS values are unweighted coordinate-error norms divided by the reference
displacement norm, not area-weighted norms.
Here an interface face has at least one vertex bound to a coarse face other than
its refinement parent: 8,707 of 47,616 fine faces. These results localize much of
the transfer error to changes of transporting frame across coarse elements.
They do not prove that the original BAC discretization has the same cause.
All transferred states satisfy the +/-2% strain bound, which demonstrates why
that bound and visually small position errors cannot replace energy/force checks.
The old linear-displacement-plus-fixed-offset comparison is worse, with membrane
energy ratios of 33,124–58,181, and does not preserve arbitrary rigid rotations.

A separate offline sensitivity experiment blends incident triangle rotations at
vertices (rest-area weights), interpolates those frames and projects to a proper
rotation. It reduces the membrane ratios to 199–305, still grossly unacceptable.
This experiment is not PSQ, has not undergone nonlinear derivative/contact tests,
and is **not included in the runtime kernel or adopted as a profile**.
Do not use either transfer experiment to enable V2 or as a replacement stiffness.

The next accuracy gate needs a consistent material mapping and coarse/fine energy
approximation, including the director variables, verified by force and energy as
well as geometry. Optimizing only vertex counts or displayed shape is insufficient.
If that approximation cannot satisfy the existing storage and mechanics gates,
report the tradeoff explicitly; do not soften the tissue, relax strain, or store
unaccounted fine state. Seasonal, V9/checkpoint-v7 and production-contact integration
remain outstanding. The prescribed core build and server build pass with zero
errors (312 and 155 existing warnings); the new kernel's NativeAOT probe passes.
Scoped whitespace and recorded-JSON checks pass. Production server AOT,
frontend/playback and seasonal runs were not repeated: their consumers and
schemas are unchanged at this standalone stage.

### Exact fine-energy composition and a rejected reduced stiffness model

`ShellProlongedEnergy` now evaluates `E(P(c), d) - f · (P(c), d)`, where `P` is
surface prolongation, `c` contains coarse positions, and `d` contains **independent
fine BAC edge directors**. It preserves the original material and strain barrier.
Its Hessian product includes `Jᵀ H J v` and the geometric term from the fine energy
and external loads. `BacShell.EvaluateElementProduct` and the transfer's weighted
Hessian contraction obtain this directly from analytic directional chain rules;
they do not form a dense matrix or use finite differences. State and loads are
snapshotted in reusable worker storage, and invalid/energy-only trials invalidate
the prepared product. No contact or motion certificate is implied.

This follows the variational composition in
[PSQ section 3.1, equation 2](https://pcs-sim.github.io/psq/psq-combined.pdf), adapted
to BAC directors. It does not reproduce PSQ's complete algorithm: this experiment
still uses the projected correspondences above, and does not implement the
[intrinsic prolongation](https://www.dgp.toronto.edu/projects/intrinsic-prolongation/)
constructed through mesh decimation. The paper also explicitly limits how coarse
a proxy can be. Failure of this particular mapping is not evidence against every
PSQ approximation or the BAC constitutive law.

The new deterministic probe passes **3,620 checks** in both managed .NET and
NativeAOT, with identical reported numerical values. Exact products agree with
independently assembled dense Hessians to 4.27e-14 absolute error in the small
coupled fixture; finite differences differ by at most 2.21e-8. Local BAC products
agree with the existing dense element matrices to 1.14e-12. Checks include
position/director cross terms, external-load geometric stiffness, rigid motion,
identity transfer, masks, aliases, cache invalidation, inadmissible fine strain
and zero warm allocation. The 12,222 transfer checks and 174 rod / 43 attachment /
19 coupled contact-release checks also pass unchanged.

For the full 24,209-vertex / 47,616-triangle reference, an independently assembled
sparse matrix gives the same product to **2.59e-15 relative L2 error** (4.10e-12
maximum absolute error). Managed and NativeAOT results are identical. This test
uses the complete unconstrained operator, including fixed-coordinate reactions;
removing fixed fine rows before comparing to a complete pullback would compare
different operators.

A separate small-load stiffness test uses the same 3 mm attachment clamp in both
models. The coarse clamp alone leaves 29 nominally fixed fine vertices depending
on free coarse vertices. Enforcing the fine clamp Jacobian adds 12 independent
linear constraints, leaving **327 coarse positional coordinates and 70,304 free
fine directors**. The reference fine system has 141,248 free coordinates. A sparse
director-block factorization and exact Schur complement solve the reduced linear
system; the fine comparison uses an independent sparse direct solve. This is the
rest tangent stiffness, not a nonlinear or contact solve. Each loading has total
force 1e-7 N; the distributed load uses triangle-area weights, and its observed
displacement is at the first flank. No tissue or biological coefficients change.

| Load | Fine linear deflection | Reduced linear deflection | Relative error |
| --- | ---: | ---: | ---: |
| First flank | 20.564 µm | 0.07599 µm | -99.630% |
| Opposite flank | 32.480 µm | 0.07752 µm | -99.761% |
| Tip | 102.784 µm | 0.20570 µm | -99.800% |
| Distributed | 14.780 µm | 0.03170 µm | -99.786% |

The fine linear deflections agree with the existing nonlinear reference to within
0.1%, so large-deformation effects cannot explain this discrepancy. Reduced
positional residuals are below 1.3e-17 N and director residuals below 1.3e-23 N·m,
yet fine positional residuals reach **2.12–6.48e-6 N**. Fine direct solves leave at
most 1.93e-14 N residual. `TryMeasureStationarity` therefore reports both coarse
and fine maximum absolute positional residuals, excluding explicit clamp masks,
and reports edge-director residuals as torques. A converged reduced system cannot
be labelled full fine equilibrium or evidence of physical confinement.

**This projected mapping remains rejected.** Relaxing directors and solving the
reduced equations accurately does not rescue it. The next approximation must
represent low-energy shell motion and the attachment constraints together, and
pass force/energy/compliance comparisons before contact or seasonal integration.
Increasing iteration limits cannot resolve this approximation error.

Memory limits remain **4 KiB target / 8 KiB maximum per accepted pair**. This
reference exposes 72,181 coordinates and would require **290,096 B** if all fine
directors were kept using the existing compact state accounting; it is explicitly
not a candidate runtime state. Its reusable fine-reference worker allocates
9,933,176 B, measured separately, and evaluations/products allocate zero bytes
once warm. Three warm complete energy/gradient/product evaluations average
644 ms managed and 714 ms NativeAOT on this machine. These serial diagnostic
measurements are not production performance or multicore acceptance. Local
10,000-product timings are 168 ms managed / 47 ms AOT versus 393 / 402 ms for dense
element Hessians; this does not establish a speedup for an iterative solver that
can reuse already assembled blocks. No additional per-organ storage is introduced.

The temporary projects `/tmp/gf-shell-pullback-check` and `/tmp/gf-shell-proxy`
contain the derivative/operator and clamped stiffness comparisons. Measurements
and definitions are recorded in `SHELL_MECHANICS_RESULTS.json`. Production modes,
biology, renderer/light consumers, playback and checkpoint formats are unchanged.

### Petiole compliance and coupled objective

The three spans end at fractions (6-sqrt(6))/10, (6+sqrt(6))/10 and 1 of intrinsic
petiole length. Bends at the base and the two internal junctions have compliance
weights 1/9, (16+sqrt(6))/36 and (16-sqrt(6))/36. Each two-axis hinge has stiffness
EI/(L*w), with circular second moment I=pi*r^4/4. This Gauss-Radau quadrature
integrates moment products through degree four exactly. Consequently both end
rotation and deflection agree with Euler-Bernoulli theory for an end moment, a
transverse tip force and a uniform transverse load in the small-deflection limit.
It avoids the extra flexibility of assigning whole-organ compliance to every span.

These are numerical spans, not new biological organs. The basal hinge replaces
the beam's basal compliance; integration must not also retain the former petiole
hinge spring. No separate leaf hinge spring is present in `ShellPetioleEnergy`.
Each rod hinge uses a two-component rotation vector; analytic Rodrigues
coefficients and their derivatives remain finite at zero angle. Frame transport
keeps the blade junction at the actual rod tip. The whole-organ bend safeguard
limits the sum of hinge-angle magnitudes, rather than granting each hinge a full
organ's allowance. There is no timestep in this static constitutive model.

The separate rod probe uses L=12 cm, r=2.5 mm and E=100 MPa as a numerical fixture,
not an additional Bergenia material measurement. Its maximum relative deflection
error among the three small-load beam comparisons is 1.40e-10. All 174 rod checks
pass, including derivatives, orthonormal frames, span lengths, zero-angle behavior,
release and allocations. Another 43 attachment checks validate full nine-variable
point-energy derivatives: three local blade coordinates plus six rod angles.
Reducing finite-difference steps reduces derivative error as expected. With cached
kinematics, 10,000 point/Hessian pullbacks take about 22 ms in managed .NET and
allocate zero bytes (about 5.7 ms in NativeAOT); this is not an end-to-end plant benchmark.

The coupled fixture uses an eight-triangle strip (5 cm by 1 cm, E=100 MPa,
h=0.1 mm) and a deliberately flexible synthetic rod (L=6 cm, r=0.3 mm, E=10 MPa).
A three-vertex local clamp attaches the strip, without an added hinge spring.
Two distal forces total -3e-6 N vertically. The frictionless plane is at -8 mm,
with 1 mm barrier activation and coefficient 0.1 N/m. A temporary dense Newton
reference solves the shared objective with the existing strain-path guard.

| Coupled fixture | Result |
| --- | --- |
| Load / release iterations | 11 / 10 |
| Minimum endpoint plane gap | 0.938444 mm |
| Maximum vertex reaction | 1.18593e-6 N |
| Blade tip / petiole tip height | -7.06156 / -2.67860 mm |
| Blade tip deflection in its local frame | -0.418652 mm |
| Maximum petiole hinge component | 0.0451197 rad |
| Principal stretches | 0.99999999998 to 1.00000000046 |
| Maximum release coordinate/angle error | 1.274e-11 |
| Fixed blade, force-plus-moment beam compliance error | 9.50e-8 relative |
| Fixed rod, difference from independent shell solve | 1.39e-17 |

Managed .NET and NativeAOT give identical numerical outputs for all 236 rod,
attachment and coupled assertions, and for the contact/storage encoding probes.
Nineteen coupled assertions pass, including analytic energy/Hessian products,
positive endpoint gaps, deformation of both components, attachment continuity,
release, limiting cases, compressed-state force balance and zero allocation in
repeated energy/Hessian evaluations. The updated temporary reference now checks
whole-path local strain, self-contact and the composed curved path against its
planes before accepting a trial. All 33 accepted steps pass; zero additional
steps are rejected, and the 11/10 load/release iterations and numerical results
above are unchanged in managed .NET and NativeAOT. This certifies those fixture
paths only. General two-body contact forces, finite-radius rod contact, torsion,
shear, buckling/stability, large-load beam convergence, shared-support equilibrium
and full component preconditioning remain unvalidated.

### Linear continuous collision checks and feature pruning

`ShellContinuousCollision` checks linearly moving vertex/triangle and edge/edge
features. Each relative feature map is multiaffine in time and material
coordinates. Directed-rounding intervals enclose its corner control points;
de Casteljau subdivision and hull-distance bounds conservatively exclude boxes.
Triangle coordinates also obey `u+v<=1`. A time-ordered bounded queue reports a
certified prefix before the first unresolved box. For nonzero separation, a
material corner certified inside the clearance supplies a witness without
subdividing an entire contact volume into microscopic boxes.

The result distinguishes `Clear`, `PotentialContact` and `Uncertified`. The last
includes an explicit work, workspace or numerical-resolution limit. A possible
contact is conservative, not evidence of physical confinement. A zero prefix
certifies no positive motion and does not certify the initial state. These
outcomes must remain separate when developmental continuation is integrated.

`ShellCollisionMesh` precomputes unique edges and balanced face/edge BVHs per
immutable topology. `ShellMeshCollision` refits swept bounds in worker scratch,
prunes feature pairs, removes duplicate self-edge pairs, and excludes only
incident connected features. It checks both vertex/face directions. It also
checks initial segment/triangle intersections: VF/EE first-contact events alone
can miss a segment already piercing a face. Every query refits its supplied
geometry; no stale clearance cache can authorize changed vertex motion.
These routines return a motion certificate, not a contact manifold or forces.

The published [Tight Inclusion sample queries](https://github.com/Continuous-Collision-Detection/Sample-Queries)
were checked at commit `a744562c7a842e9eee033fcea290e75a4a8e7b4e`:
58 CSV files, 207,279 queries, including 607 reference-positive cases. All rational
input coordinates were verified to be exactly representable as doubles before
conversion. This is the full public **sample repository**, not the complete
60-million-query benchmark described by the paper.

| Default query outcome | Count |
| --- | ---: |
| Certified clear | 206,622 |
| Possible contact | 648 |
| Uncertified | 9 |
| Reference contacts incorrectly classified clear | 0 |
| Possible contacts among reference-negative queries | 49 |

Defaults are zero minimum separation, 1e-9 m spatial tolerance, 1e-6 normalized
time tolerance, 32,768 boxes visited and 1,024 queued boxes. The nine uncertified
cases comprise eight reference contacts and one reference-negative query; five
hit the work budget and four the queue capacity. A diagnostic retry with four
times both budgets resolves six; three remain uncertified. No retry is silently
labelled obstruction. This evidence supports the implementation but is not a
proof over all floating-point inputs or completed production collision acceptance.

The temporary probe passes 738 primitive assertions, 523 mesh assertions and six
native-mesh assertions in both managed .NET and NativeAOT. Numerical logs agree
exactly, including prefixes and queue work. Fixtures include swept crossings with
clear endpoints, coplanarity, degeneracies, finite separation, tiny scales, common
translation, initial piercing, self-contact, invalid input, budget outcomes and
allocation checks. BVH results equal exhaustive feature enumeration for 500
seed-42 triangle-motion pairs. Native Bergenia rest geometry is clear at scales
1, 1e-3 and 1e-6; crossing copies are never certified clear. Two of those difficult
mesh crossings remain uncertified at default budgets.

Repeated primitive and whole-mesh queries allocate zero bytes after warm-up.
A translating 289-vertex / 512-face grid uses 4,997 primitive queries including
initial-state checks, versus 877,168 unpruned feature combinations before
adjacency exclusions. Its 800 edges are unique. Accounted reusable bounds are
125,968 bytes for that grid and 45,040 bytes for native Bergenia; the default
inclusion queue is 450,624 bytes per active worker. These are bounded worker
buffers, not persistent organ state. No new per-leaf storage was introduced.

The latest measured full-dataset pass takes about 0.98 s managed / 1.11 s
NativeAOT, with median query latency 0.40 / 0.41 microseconds and p95 3.36 / 4.07
microseconds. Input conversion/loading is excluded; query timing instrumentation
is included. The NativeAOT pass ran alongside the server build. These exploratory
measurements do not establish multicore scaling or the 25% end-to-end gate.

These linear predicates remain useful in a common transported frame. The curved
extension below handles the explicit three-span support path; existing V1
collision handling is not routed through either standalone kernel.

### Curved support paths and collision certification

`ShellSupportMotion` interpolates local blade vertices, origin, base rotation
vector, rod length and six rod bend components linearly in the continuation
parameter. Their composed world-space trajectory is curved. This matches the
standalone beam/attachment convention; it does **not** represent arbitrary
quaternion slerp or an unspecified chain of moving ancestors. Callers must use
this exact path or supply a separately certified path representation.

Each rotation is enclosed with interval Rodrigues coefficients. Degree-18 Taylor
polynomials in squared angle use outward-rounded coefficients and operations;
for squared angle in [0,16], the sinc remainder is below `2^-77`, and the cosc
remainder is smaller. This avoids assuming a particular `Sin`/`Cos` rounding
error. Time subdivision tightens the rotation bounds, while material-coordinate
subdivision uses affine feature bounds. Unsupported ranges return `Uncertified`,
never an endpoint-chord approximation. The 4-radian domain is a numerical limit,
not a botanical flexibility setting.

Equal origins and base rotations cancel before feature bounds; an entirely
shared transported tip frame cancels exactly, including its changing length.
Self-contact therefore uses the linear local-vertex predicate. Mesh pairs refit
conservative curved swept bounds, test initial edge/face piercing and traverse
both VF directions plus unique EE pairs. A whole-interval mesh enclosure that
exceeds the rotation domain returns uncertified; continuation must reduce that
interval rather than treating it as physical obstruction. The plane predicate
certifies every vertex against the same composed path. Plane clearance belongs
in the plane offset; it is not counted a second time as feature separation.

A 10 cm rod rotating from -45 to +45 degrees crosses a triangle at x=9.5 cm even
though its endpoint chord is clear. The analytic first-contact parameter is
0.29783475179148017; the curved VF query brackets it between
0.297834750264883 and 0.2978347521275282 (945 boxes). The complementary fixture,
where the chord crosses an obstacle but the arc clears it, returns `Clear` in
seven boxes. EE, full meshes, half-spaces and initial piercing have corresponding
fixtures. A plane version brackets the first crossing in 42 boxes.

Managed .NET and NativeAOT produce identical numerical results for 5,237 curved
primitive/kinematic checks and 51 mesh checks. Thirty seed-42 mesh pairs agree
with exhaustive feature enumeration, including the certified prefix. A separate
90-digit decimal Rodrigues oracle passes 5,355 sampled component-enclosure
checks, including tiny rotations and 3.9 radians. Sampling corroborates the
interval construction; it is not an exhaustive mathematical proof.
Changed lengths, origins, base rotations and rod angles give identical warm-cache
and fresh-workspace results. The updated linear kernel retains the same published
sample outcomes (zero false clears); exact-zero interval arithmetic changes its
visited-box total to 2,544,010.

The fixed 64-entry transform cache includes every support-path value and the
exact time interval. It caches kinematics, never clearance or geometry. In the
arc fixture, it reduces cold transform evaluations from 682 to 118 without
changing the result or subdivision. Repeated curved primitive and mesh queries
allocate zero bytes. Accounted curved queue/cache storage is 305,176 bytes per
active worker, plus the supplied 450,624-byte linear workspace. Native mesh
curved bounds require 50,576 bytes per worker; these buffers are reusable and
are **not persistent per-organ caches**. No additional persistent per-leaf state
was added; the conditional 3,872-byte compact state remains unchanged.

A single 50-query arc observation takes 80.73 ms managed / 31.04 ms NativeAOT.
This is a microbenchmark, not multicore, seasonal or <=25% end-to-end acceptance.
The cache and exact-zero arithmetic reduce redundant interval work; broad
performance claims still require fixed-population integrated benchmarks.

**Remaining scope:** finite-radius petiole geometry, arbitrary shared ancestor
paths, thickness/seam policies, general contact witnesses/forces, the production
coupled optimizer, births/renewals/flowers, and cache/checkpoint integration.
The predicates certify geometric paths; they do not establish force balance or
physical confinement. Biological settings, tissue coefficients and growth
probabilities were not changed in this continuation.

### Bounded state and precision audit

`ShellStateStorage` owns one byte buffer. Its default stores double vertex coordinates,
six double petiole angles, the immutable profile SHA-256, stable organ IDs,
revision/count metadata, bounded 32-byte contact witnesses and explicitly reserved
bookkeeping. It enforces a conservative x64 ownership allowance, including object,
array and owning-reference overhead. Oversized construction is rejected before
allocating the payload; excess witnesses are rejected rather than discarded.
Unaffected species do not construct this experimental storage.

The native 114-vertex / 291-edge fixture, eight witness slots and 128 bytes of
additional bookkeeping gives:

| Director encoding | Accounted bytes per pair | Measured managed allocation |
| --- | ---: | ---: |
| Double positions and double directors | 5,720 (5.59 KiB) | 5,696 |
| Double positions and float directors | 4,560 (4.45 KiB) | 4,536 |
| Fixed48 positions and double directors | 5,040 (4.92 KiB) | 5,016 |
| Fixed48 positions and float directors | 3,872 (3.78 KiB) | 3,848 |

The float-director option with double positions does not round vertices. The
additional `Fixed48` option stores signed 48-bit local coordinates in an explicit
power-of-two metre unit. The default unit, 2^-48 m, covers [-0.5, 0.5) m with at
most 2^-49 m position error. It preserves substantially more spatial precision
than ordinary floats while saving two bytes per coordinate. Petiole angles remain
doubles. A required caller validator checks the exact decoded shell, rods and
witnesses, including applicable geometry and equilibrium criteria, before committing. Failed
validation leaves the previous stored state and revision intact. Caller scratch
cannot alias its input state. The float-director option is conditional, not a
blanket promise that quantization is harmless; all-double storage remains available
within the ceiling for this topology. Contact capacity or extra caches increase
the accounted size and remain subject to the same hard limit.

Tests use fitted native directors and loads 1e-5, 3e-5, 1e-4, 3e-4, 1e-3 N and
release, with absolute force tolerance 1e-9 N and relative tolerance disabled.
All four tested position/director combinations pass restored-state force and strain checks. The largest compact
residual was below 7.00e-10 N. Double-position encodings remain bit-identical. In contrast, rounding every
scalar to float produced residuals up to 2.75e-4 N. Thus the earlier all-float
byte counts are not an accepted precision design. Updates/reads allocate zero
after construction with reusable scratch and a cached validator.
Managed .NET and NativeAOT produce identical reported contact, storage and
load/release results, including measured allocation sizes. The prescribed core
build passes net8.0/net10.0 with existing warnings and zero errors. The 9,344
strain-path assertions and 3,778 profile assertions also pass after these changes.

The Fixed48 unit is not universally adequate for tiny blades. Scaling the curved
rest fixture to 1e-4 and 1e-6 of nominal dimensions makes the default unit fail
force balance, and the validator rejects it. Explicit finer units (2^-62 and
2^-68 m respectively) pass those rest-state tests. These are precision stress tests
with unchanged material thickness, not physiological seedling simulations. Profile
integration must select/record a suitable unit, handle growth beyond its range,
and revalidate any repacking or use doubles. There is no silent saturation,
automatic scale drift or acceptance based solely on small geometric error.
3,926 additional assertions cover signed encoding, rounding error, representable
limits, invalid values and unchanged state after rejection. The coupled strip's
compressed state also retains force balance (1.25e-11 N equivalent residual).

These are storage and mechanics fixtures, **not complete checkpoint continuation
or full-runtime memory acceptance**. No per-organ `BacShell`, dense mesh, numeric
factor or duplicate rollback array may be retained outside this accounting when
the production integration is implemented. Shared topology and bounded active
worker workspace must be measured separately. The current native geometry still
fails the refinement gate despite fitting the storage ceiling.

### Plane-contact fixtures

The 53 assertions cover analytic barrier derivatives, normal-spring force balance,
frictionless sliding, positive gaps, contact reaction units, invalid starts,
constraint release and settled allocation-free solves with both IC(0) and sparse
direct paths. A clamped strip under a 1e-6 N total tip load contacts a plane,
converges in nine Newton steps, and stays 0.19445 mm above the offset plane.
Removing load and plane recovers the unloaded strip within 1.04e-12 m.
An infinite plane is intentionally a first fixture; this does not pass the full
same-crown, self-contact, obstacle, insertion or collision-certification gates.

A native-shell continuation through 1e-5, 3e-5, 1e-4, 3e-4 and 1e-3 N reached
about 144 mm flank displacement while keeping principal stretches within the
bound; release returned to the curved rest state. With the whole-path strain
guard and column factorization, the fitted-director fixture needed 370 Newton
steps for the largest-load release; maximum vertex-coordinate recovery error
was 1.18e-10 m. This establishes large
lamina deformation and elastic recovery in the isolated numerical model.
**Self-contact and swept collision safety are not checked in this fixture.**

The column factorization was benchmarked against the prior row-intersection
implementation on identical fixtures. Each case reused its solver workspace,
discarded two warmups and measured seven rest-to-load solves. Setup/director
fitting were excluded. The p95 is the nearest-rank statistic (the maximum of seven).

| Mesh / element workers | Previous median / p95 | Updated median / p95 | Updated allocated bytes / solve |
| --- | ---: | ---: | ---: |
| Native / serial | 908.5 / 910.2 ms | 457.1 / 467.1 ms | 0 |
| Native / parallel | 652.8 / 667.4 ms | 207.0 / 210.5 ms | 76,888 |
| Refined / serial | 5,226.0 / 5,369.9 ms | 1,373.7 / 1,375.8 ms | 0 |
| Refined / parallel | 4,474.4 / 4,601.4 ms | 746.3 / 772.1 ms | 109,152 |

The equilibria agree within 7e-16 m at the loaded vertex and retain the same
33/18 Newton and linear iteration counts. Native/refined parallel medians improve
by 3.15x/6.00x. Parallel allocation includes `Parallel.For` scheduling overhead;
the serial numerical paths allocate nothing after setup. These are fixed shell
fixtures, **not** matched full-growth/contact populations or proof of the ≤25%
end-to-end gate. These measurements precede the shared-symbolic-pattern and
convergence-check changes above. Production sleeping, contact work and lighting
remain unmeasured.
The matrix-free IC(0) solve agrees with the native direct-reference displacement
within 0.43 micrometres, but needs 17,302 PCG iterations across 43 Newton steps
(about 2.44 s). This directly identifies preconditioning as unfinished work;
parallel element evaluation alone does not address it. NativeAOT executed the
fitted small-load/release fixture with the same numerical outputs as managed .NET.

## Required work before production integration

The following requested stages are **not implemented or accepted**:

1. Resolve the compact-mesh compliance discrepancy; complete buckling, alternate
   triangulation and refinement tests; include midrib reinforcement and validate
   the calibrated petiole under large deformation and its actual attachment patch.
   Energy diagnostics, conforming refinement and exact fine-energy pullbacks are
   available, but the compact trials and projected coarse/fine stiffness still fail.
2. Extend the shell/petiole/plane objective to shared supports and both contact
   bodies; build the convergent continuation algorithm;
   integrate the standalone linear/curved VF/EE and self-contact predicates,
   extend them to full support chains and finite-radius rods, and add contact
   witnesses, robust insertion and partial-development outcomes.
3. Separate biological opportunities from mechanical retry timing and validate
   blocked births, renewals and flowering recovery. This stage has not changed
   any of those production paths.
4. Introduce mode 2, immutable shell profiles, V9 records, both renderers, exports,
   one-sensor-per-leaf CPU illumination and live checkpoint v7 together. No partial
   scene/checkpoint format migration has been made here.
5. Reproduce the archived 755-day request and the full 0/1/2, timestep and light
   matrix. Measure developed leaves and blooming, not only organ counts.
6. Validate the complete integrated pair within 8 KiB (target 4 KiB), contact
   allocations, matched-population performance, FAV11/P003 scaling and the ≤25%
   runtime target. The bounded container's measurements above do not yet include
   a production integration or establish its end-to-end memory use.

The unresolved geometry gate is why the new shell is not connected to live
growth. V2 must remain unavailable/default-off until its dependent integration
and acceptance work is complete. This avoids presenting another unvalidated
solver as a remedy for the reported V1 regression.

## Reproduction and build checks

Following the repository probe convention, temporary net8.0 console projects
reference `Agro/Agro.csproj` outside the checkout:

- `/tmp/greenfdt-shell-probe`: native/fitted/refined load-and-release fixtures and
  the C++ oracle comparison. `direct small fit`, `direct refine clamp fit` and
  `direct fit` select the reported cases. `serial` fixes the worker count to one.
- `/tmp/greenfdt-shell-derivatives`: all-face and boundary derivative checks.
- `/tmp/greenfdt-shell-fixtures`: the strip compliance comparison.
- `/tmp/greenfdt-shell-simulation`: seed-42, ambient, default-mode Bergenia at
  1 h and 4 h ticks for 30 days; finite resources, then eight continuation steps
  with byte-identical exported frames and checkpoint bytes after reload. This
  is a compatibility smoke test, **not seasonal biological acceptance**.
- `/tmp/greenfdt-shell-aot`: NativeAOT compilation and execution of the actual
  numerical kernels, including load/release (the unused kernel would otherwise
  be trimmed from a production server executable).

The 29 September continuation recreated probes at `/tmp/gf-shell-next` and
`/tmp/gf-shell-path`; earlier temporary directories were no longer available.
`/tmp/gf-shell-mesh` checks profile identity, exact authored samples, seams and
rigid motion. `/tmp/gf-rod-check` exercises the rod, attachment derivatives and coupled strip,
including rigid-component limits and compressed force balance.
`/tmp/gf-ccd-check` exercises the linear feature and mesh CCD kernels and the
published sample dataset, including execution of the actual kernels in NativeAOT.
`/tmp/gf-shell-accuracy` records the four-load energy/refinement audit using an
independent SciPy sparse solve; `/tmp/gf-shell-energy` checks analytic energy
components and `/tmp/gf-shell-adaptive` checks conforming profile refinement.
`/tmp/gf-shell-refine-candidate` builds temporary energy-guided candidates and
`/tmp/gf-shell-candidate-storage` validates their exact encoded force balance.
The energy/refinement kernels were executed in NativeAOT; core/server builds
pass with 312/155 existing warnings and zero errors. No biology, species UI,
renderer, playback or live checkpoint format changed in this mesh continuation.
`/tmp/gf-curved-check` exercises curved support/mesh/plane paths, cache invalidation,
common motion, independent high-precision enclosures and NativeAOT parity. The
updated `/tmp/gf-rod-check` additionally certifies each accepted coupled trial's
plane and self-contact path. Final build results are recorded in the companion
JSON; frontend consumers and production server NativeAOT were not changed or
rerun in this curved-collision continuation.
`/tmp/gf-shell-acceptance` exercises plane contact and bounded storage,
including post-encoding force balance, rejection rollback, witness overflow,
measured allocation and large native-leaf load/release. Its storage comparison
uses an absolute 1e-9 N force tolerance, with the relative term disabled.
The baseline benchmark at `/tmp/gf-shell-baseline` compiles the prior factor
implementation outside the checkout against the same shell equations/path guard.
Machine-readable measurements are in `SHELL_MECHANICS_RESULTS.json`. The path
probe covers scales from 1 µm to 2 m, translations, safe/unsafe rotations,
near-limit endpoints, randomly sampled accepted paths and zero query allocations
(9,344 assertions), plus a sparse strip load/release fixture. These are strain
and elasticity fixtures, not the still-outstanding contact/seasonal tests.
Managed .NET and NativeAOT both passed the path assertions and strip fixture;
the strip used 16 Newton steps for loading and 16 for release, with identical
reported recovery error (2.09e-12). The prescribed core build passed for net8.0
and net10.0 with existing warnings, and scoped whitespace checks passed. The server build was also repeated with `SkipSpeciesGeneration=true`: zero errors
and 169 existing warnings. Frontend consumers were not changed or revalidated
in this continuation; V9 integration is still outstanding.

Core and server builds passed with pre-existing warnings. NativeAOT server
publication succeeded to `/tmp/greenfdt-shell-server-aot`; no running server was
replaced. Frontend type checking and the four existing scene/decode/transform/
stored-run suites passed (28 tests). The species generator was run through its
isolated artifacts path; generated files were not hand-edited.
Those server/frontend results belong to the earlier integration preparation;
this continuation changes only the standalone shell library and its documentation.

### Mechanical response subspaces and deterministic parallel evaluation

`ShellSubspaceEnergy` evaluates the full fine BAC energy at `q = qRest + U z`.
Both positions and directors are rows of `U`; the reduced gradient and Hessian
product are exactly `Uᵀ g` and `Uᵀ H U v`. Every fine element and strain barrier
is evaluated. There is no sampled integration, material softening or hidden
fine state on an organ. The immutable basis is an explicit input, not a new
species preset. This constant linear mapping needs no geometric Hessian term;
that would change for a nonlinear manifold.

The kernel snapshots fine state and loads, exposes maximum absolute fine
positional residual in N and director residual in N m, and masks only explicitly
fixed coordinates. Reduced coordinates have basis-dependent units, so the
reduced gradient is deliberately not reported as a force. Its affine-path
guard certifies surface strain, not contact, director admissibility or rotating
support paths. A rejected path query leaves the prepared state intact.

The follow-up compact mesh search tested eleven conforming meshes with 207–255
vertices. The best new candidate still had 10.49% worst compliance error; it did
not improve the earlier 219-vertex candidate. A separate smooth RBF displacement
space with relaxed fine directors remained 79–95% too stiff. Neither diagnostic
is used by the simulator.

For the new mechanical basis, an independent sparse solve constructs responses
to three coordinate forces at 64 deterministically selected authored vertices.
The force/response Gram matrix orders those responses by compliance and
normalizes them in the rest-energy metric. These are static responses, not
vibration eigenmodes. Twelve small-load comparisons include the original four
loads, five area-weighted Gaussian patches, and three unseen fine-vertex loads.
Some original point loads overlap construction; the entire suite is not held
out. Each comparison has total force 1e-7 N and the same 3 mm clamp.

With 32 coefficients, worst load-compliance error against the full reference is
**1.81%**, compared with 99.6–99.8% for the rejected projected mapping. Here
compliance means `fᵀu`, not the maximum vertex displacement; the energy-norm
error is also recorded separately. This is a substantial small-load improvement,
but fine positional residuals still fail equilibrium, especially at unseen point
loads. Increasing the basis to 96 lowers worst compliance error to 0.706% without
establishing full force balance. The fine reference itself still has the
previously reported mesh-convergence limitations.

Finite-deformation tests added 36 static derivative directions from the first
eight responses, then orthogonalized them against the original 32. The
68-coordinate basis preserves the primary material and strain limit. Derivative
construction uses offline centered differences of analytic Hessian products;
halving the perturbation changes the right-hand sides by at most 1.10e-6 relative
L2. Runtime energy derivatives remain analytic. This is linear basis enrichment,
not a quadratic-manifold runtime implementation.

At a 5e-5 N flank load, Newton reaches a reduced gradient of 8.53e-15, with a
maximum positional component change of **7.31 mm**, compared with 0.62 mm for the
32-coordinate response-only solve (also checked with Newton). The full positional residual is **3.82e-4 N**:
this is **not** an accepted equilibrium or large-fold validation. Further offline
residual enrichment to 75 coordinates increases the displacement to 10.72 mm and
lowers energy, but leaves a 4.17e-4 N positional residual. All accepted trial
segments pass the strain guard. This experiment also fails full equilibrium;
lower energy and larger visible bending are insufficient. One training load
cannot establish general contact response, release or buckling accuracy.

Newton remains the equilibrium method. Runge–Kutta would integrate actual or
artificial settling dynamics and would need its own stability, timestep and
continuous-motion checks. It cannot repair directions absent from the same
compact representation. Numerical stagnation and work exhaustion remain solver
outcomes, never evidence of confinement. Explicit versus implicit Runge–Kutta
methods and their stiffness distinction are summarized in the
[SciPy integration reference](https://docs.scipy.org/doc/scipy/reference/generated/scipy.integrate.solve_ivp.html).

The new kernel passes **631 assertions** in managed .NET and NativeAOT with
identical reported numerical errors: maximum dense-product discrepancy
4.16e-17 and finite-difference discrepancy 6.01e-10 in the small fixtures. A
separately assembled full fine operator agrees to 1.47e-14 absolute error.
Checks cover identity and rigid translation, position/director coupling, explicit
reaction masks, aliases, load snapshots, failed-trial invalidation, recovery,
fine-strain rejection, and a valid rigidly rotated endpoint whose affine path
collapses. These are mechanics kernel checks, not a seasonal simulation pass.

Fine element batches run within a single leaf. Workers write independent slots;
the final face scatter and sum have a fixed order. Measured serial/4/16/32-worker
energy, gradient and products are byte-identical. On the fixed 47,616-face,
32-coordinate reference, median energy-plus-product latency was respectively
510.0 / 151.7 / 68.1 / 59.5 ms; p95 was 512.7 / 159.9 / 69.1 / 97.8 ms across
seven warm measurements. These expensive reference timings do **not** establish
the production 25% overhead gate. SIMD acts across independent reduced
coordinates; a separate projection-only measurement improved median time from
2.594 to 2.272 ms with identical output hashes and zero warm allocations.

Serial repeated evaluation/product allocates zero bytes. Parallel dispatch still
allocates roughly 4–17 KiB per evaluation/product batch, depending on worker
count; there is no task per face or contact. Reusable workspace is about 4.72 MiB
serial or 13.53 MiB with 32 workers and must be bounded by active workers, not
retained per organ. The 32-coordinate basis itself is **35.27 MiB**; the enriched
basis is **74.94 MiB**, shared reference data kept outside the repository. Their
coefficient payloads are only 256 and 544 bytes, but that is not complete
leaf/petiole persistence, an archive-size pass, or permission to keep a distinct
fine basis per leaf. The complete pair target remains 4 KiB, with 8 KiB ceiling.

These bases describe one fixed adult rest geometry and clamp. Reuse through
biological expansion, changing dimensions and petiole attachment has not been
validated. Production contacts, V9/checkpoint-v7, rendering/lighting integration,
release and seasonal flowering/density acceptance remain pending. Mode 2 stays
unavailable and the default remains 0. Core and server builds pass; the standalone
kernel passes NativeAOT. Production server AOT and frontend/playback were not
rerun for this standalone-only addition.

### Current-tangent reference and nonlinear snapshot checks

The next check uses the **current deformed tangent**, rather than rest stiffness,
to correct the incomplete 75-coordinate result. `ShellSubspaceEnergy` now exposes
the prepared full fine residual and full tangent product, before projection or
clamp elimination. These reuse worker arrays, retain fixed-coordinate reactions
and allocate no new organ state. Energy-only and failed trials still invalidate
prepared products. A failed direction clears its output without changing the
prepared state. The ordering extraction above lets an independent sparse solver
use the same geometry ordering without constructing the simulator's factor data.

On the 24,209-vertex / 47,616-face reference (144,451 position/director DOFs), an
offline exact Newton solve with Armijo backtracking converged at both loads below.
It uses the unchanged 100 MPa primary moduli, 0.1 mm mechanical thickness, 3 mm
numerical clamp and 2% strain bound. No biological timestep enters the objective.
Every accepted affine increment passes the full strain path guard. These are
free-blade point-force fixtures: no petiole, gravity, obstacle or self-contact is
included, so they do not certify the complete production motion path.

| Case | Maximum positional component change | Maximum free positional residual | Principal stretches |
| --- | ---: | ---: | ---: |
| Full reference, 50 µN flank load | 19.964 mm | 4.75e-12 N | 0.999013–1.001476 |
| Full reference, 25 µN at the same point | 9.602 mm | 1.11e-12 N | 0.999541–1.000620 |
| Compact 108-coordinate model, unseen 25 µN load | 9.521 mm | **3.16e-4 N: fails** | 0.999544–1.000626 |

The 50 µN solve took 34 accepted increments from the earlier approximate state;
reducing that load to 25 µN took 36. Final full Newton corrections were negligible.
Numerical factorization costs about 12–14 seconds per fine reference iteration on
this machine. This is an offline oracle, not an acceptable production solver cost.
The extraction does not speed up the existing C# factor: its ordering is unchanged.
No SciPy or NumPy runtime dependency was added to the simulator.

Adding the 34 full-Newton trajectory snapshots to the old 75-coordinate span
tests whether a compact representation can retain the trained result. A Gram
matrix truncation to 95 coordinates lost enough precision to fail full force
balance (8.69e-8 N), despite only 1.88e-8 maximum mixed-coordinate error. Weighted
pivoted QR followed by rest-energy normalization retains 108 coordinates and
reconstructs the trained equilibrium with 7.75e-10 N maximum free force residual.
The decoded state meets the 1e-9 N threshold, but with modest numerical margin.
Managed and NativeAOT diagnostics are identical. This is an endpoint encoding
check of a trained state, not a new certified motion or general contact model.

The **unseen load is deliberately excluded from all training snapshots**. Its
reduced equations converge to 5.12e-13 in basis-dependent units while full force
balance fails. Against the full reference, the maximum Euclidean position error
is 0.0911 mm, RMS error is 0.0417 mm, and load-work error is 0.0888%. Those small
shape/compliance errors do not justify acceptance. One current-tangent full
correction cuts the residual to 6.10e-6 N, still outside tolerance. This isolates
missing local deformation directions; it does not support switching to
Runge–Kutta or relaxing the force criterion. Four full corrections in total reach
1.00e-12 N and agree with the independent 25 µN reference to 8.49e-10 m maximum
vertex distance. Those corrected fine coordinates are not representable by the
current compact state. Unloading is recorded in the companion JSON.

Removing the 50 µN load and solving the full shell restores the curved rest
surface to **1.49e-12 m maximum vertex distance** and 1.01e-12 rad maximum
director error, with 1.09e-12 N residual. Recovery took 57 accepted increments,
all with certified strain paths. The initial 40-step work budget was exhausted
while recovery was progressing; continuation was allowed and exhaustion was
not called confinement. Force balance first passed with a 17 nm correction
remaining. Requiring the remaining positional correction below 1e-9 m and the
director correction below 1e-8 rad took one additional increment. This verifies
elastic recovery in this bare-blade reference, not contact release or the compact
model's recovery. The 2% bound and material coefficients were unchanged.

The 108 double coefficients occupy **864 bytes**. Their immutable reference basis
occupies **124,805,664 bytes (119 MiB)** and remains outside the repository. This
is not a complete pair container, archive-size pass or permission to put a unique
basis on every organ. Petiole angles, contacts, pending development, bookkeeping
and ownership still count toward the 4 KiB target / 8 KiB ceiling. No production
state format or checkpoint version is changed by this experiment.

The updated kernel passes **1,191 assertions** under managed .NET and NativeAOT,
including full residual/product checks, reaction masks, invalidation, original
ordering parity and deterministic parallel evaluation. On a deformed fine state,
the new full product agrees with a separately assembled operator to 7.29e-16
absolute / 1.33e-15 relative L2 error; the copied gradient agrees exactly.
Serial/32-worker results agree byte for byte and warm serial evaluation still
allocates zero bytes. Existing rod/attachment/coupled fixtures pass 174/43/19
assertions, including 33 certified steps and release error 1.27e-11. Core and
server builds pass with existing warnings; standalone NativeAOT passes.
Production server AOT and frontend/playback were not rerun for these standalone
changes. V2 remains unavailable: compact general mechanics, contact integration,
mesh convergence, full pair storage, performance and seasonal acceptance remain
open. The material sensitivity and Rotblum comparisons remain separate.

### Broader snapshot audit and bounded tangent reuse

The broader basis adds the free-unloading trajectory while keeping the 25 µN
equilibrium out of training. It contains 89 loading/unloading snapshots and the
previous response/derivative directions, with weighted QR retaining 163
coordinates. Nearly-rest snapshots below 1e-7 m displacement are omitted to avoid
normalizing roundoff. The solve starts from the earlier 108-coordinate
approximation at 25 µN, not the full-reference equilibrium. This is an untrained
load magnitude at the same spatial point, not a new contact-location test.

The worst positional force component falls from 3.16e-4 N to **4.38e-5 N**, but
still fails the unchanged 1e-9 N criterion. Maximum vertex error against the full
reference falls to **14.3 µm**, and load-work error is 0.0819%. Every one of the
23,648 free vertices has a residual-vector norm above 1e-9 N; 12,110 exceed 1e-6 N.
Thus a handful of extra point coordinates would not resolve the measured defect.
The 1,304-byte coefficient payload and 188,364,104-byte shared basis do not pass
complete pair storage, archive size, general contact or full equilibrium gates.
The basis remains a temporary reference asset, not a production species profile.

`ShellSubspaceEnergy.Workspace` now accepts an optional
`tangentCacheBudgetBytes`. Zero keeps direct directional differentiation. A
positive value must cover the complete cache payload before allocation. After a
successful evaluation, the first product lazily computes the current element
Hessians; later products reuse them. An energy trial, new evaluation or failed
state invalidates them. Invalid product directions clear output while preserving
the prepared state; strain-path queries likewise leave that state intact.

Only the symmetric half of each 21-by-21 element tangent is stored: 231 doubles
instead of 441. Opposite off-diagonal entries are averaged to remove derivative
roundoff, without eigenvalue clipping or regularization. A compressed-sheet
fixture verifies that negative stiffness remains negative. Cached versus direct
fine products differ by 4.07e-15 after normalization by maximum output magnitude
(or one when larger). Serial and eight-worker cached products agree byte for byte.

The cache requires **87,994,368 bytes (83.9 MiB)** for the fine reference, or
328,944 bytes for a 178-face mesh. These are explicit *worker* payloads in
addition to existing scratch, array overhead and a 441-double temporary stack
buffer per active element worker. They must not be retained per leaf. The caller
must bound active workspaces across a solve; the API does not impose a global
memory cap. No organ state, species contract or checkpoint format changed.

Measured on the same deformed fine reference with eight workers, five warm
batches each; each batch includes energy/gradient evaluation and cache creation:

| Products per state | Direct median / p95 | Cached median / p95 |
| --- | ---: | ---: |
| 1 | 77.3 / 175.7 ms | 501.6 / 521.9 ms |
| 8 | 413.2 / 421.8 ms | 536.4 / 546.8 ms |
| 32 | 1,477.4 / 1,511.7 ms | 668.1 / 708.7 ms |
| 64 | 2,892.6 / 3,013.1 ms | 805.5 / 812.7 ms |

Caching is slower for short batches, which is why it remains opt-in. The 64-product
batch is 3.59 times faster. In the separate 163-coordinate Newton probe, total
elapsed time falls from 60.49 to 31.77 seconds with the same five evaluations of
the tangent and ten energy/gradient evaluations. That is one diagnostic run per
variant, not a latency distribution or the production 25% performance gate.
The resulting vertex positions differ by at most 8.00e-16 m. Both variants still
fail full force balance. Warm serial cache refresh/products allocate zero bytes;
parallel task dispatch still allocates per batch (about 192 KiB for 64 products).

Independent fixed-tangent PCG probes at this state also reject simple
preconditioners. After a 200-iteration work budget, relative linear residuals are
0.0961 with diagonal scaling, 0.0586 with an additive 163-coordinate coarse
correction, 0.0729 with a rest-tangent factor and 0.00238 with that factor rotated
into local deformed frames. The requested tolerance is 1e-7. None converges; no
failed preconditioner was added to production. These observations favor testing
state-dependent spatial corrections/condensation over increasing a global basis
or interpreting exhausted work as confinement. They are not a validated solution
to the remaining compact representation problem.

Validation: **226 new cache checks plus the existing 1,191 subspace checks pass
in managed .NET and NativeAOT**. They include budget rejection, lazy refresh,
prepared-state isolation, invalidation/recovery, full and reduced products,
negative stiffness, deterministic parallel results and zero serial allocation.
The fine 163-coordinate cached energy, gradient, Hessian and residuals are also
byte-identical between managed and NativeAOT runs. Core/server builds and scoped
whitespace checks pass. Production server AOT and frontend/playback were not
rerun: this change is confined to the experimental kernel. V2 remains unavailable
and the 4 KiB target / 8 KiB complete-pair ceiling remains in force.

### Nonlinear static condensation: local accuracy and reconstruction limits

`ShellStaticCondensation` partitions the fine shell into retained coordinates R
and interior coordinates I. For each current state it factors the exact interior
Hessian and evaluates:

- reduced residual: `gR - HRI * inverse(HII) * gI`;
- reduced tangent: `HRR - HRI * inverse(HII) * HIR`;
- reconstructed Newton correction:
  `dI = -inverse(HII) * (gI + HIR * dR)`.

The interior residual term matters before reconstruction has converged. Dropping
it would turn an incomplete interior relaxation into incorrect retained forces.
These are state-dependent equations; a rest-state linear basis cannot substitute
for them. The kernel snapshots state and loads, reuses symbolic factor structure,
equilibrates the numeric matrix without adding stiffness, checks original
analytic products after iterative refinement, and reports full nonfixed force
and director-torque residuals separately. A zero retained correction reuses the
interior correction already computed during preparation.

All full arrays/factors are reusable worker storage with an explicit array-payload
budget, including derivative scratch. Shared factor-pattern payload is separately
reported. Array/object headers are additional; this is not a total process-memory
cap. Warm preparation, products and corrections allocate zero bytes in the serial
fixture. No fine arrays or factors were added to organs. This reference kernel
currently requires a positive-definite interior tangent. Indefinite/singular
interiors and inaccurate solves return distinct numerical failures; it does not
clip the physical Hessian or interpret failure as confinement. Negative curvature
in retained coordinates is preserved for the outer solver to handle.

On a curved strip, independent pivoted full Newton solves and condensed steps
agree within **1.13e-12** in maximum mixed-coordinate difference across nonlinear
loading and release. The loading sequence is 2, 10, 20, 10, 0 µN at the free tip;
maximum positional-component displacement reaches **6.98 mm**. All 130 accepted
increments pass the whole-interval strain check. Final maximum position-component
recovery error is 1.31e-16 m; maximum mixed position/director error is 4.74e-15.
The largest converged positional residual in the sequence is 5.34e-13 N.
This is a mechanical strip fixture, not a calibrated Bergenia or contact test.

Finite differences of the *re-relaxed* interior verify the implicit derivatives:
energy directional-derivative error 1.36e-12 J per perturbation parameter and
relative Hessian-product error 1.51e-7. Algebraic Schur products agree with a
separately assembled dense solve to 8.45e-13 after normalization by max(1, output
magnitude). Tests also exercise mixed position/director retention, no remaining
interior, clamp reactions, snapshot isolation, rejected states, negative stiffness,
explicit budget rejection and zero warmed allocation. **1,834 checks pass in
managed .NET and NativeAOT**, with byte-identical diagnostic output and final
state hash. The existing 1,191 subspace and 226 cache checks pass in managed .NET;
their NativeAOT results remain those of the preceding stage.

A separate fine-reference reconstruction experiment retains the positions of
113 unclamped vertices from the 119-vertex junction profile: **339 doubles,
2,712 bytes**. Their values are sampled from the previously converged 25 µN
solution. The initial interior comes from the rejected 163-coordinate approximation,
with the retained positions replaced by the exact samples. Directors and all
remaining fine coordinates then relax freely. This tests local reconstruction
of a known solution; it is not prediction of an unknown load response.

An independent sparse-LU oracle recovers the full solution after ten accepted
strain-certified increments (eleven tangent evaluations): maximum vertex error
4.57e-15 m, director error 3.13e-12 rad, positional residual **1.05e-12 N**, torque
1.34e-18 N m. It takes **147.8 seconds** including setup and checks. The first
three interior matrices have negative pivots; the oracle accepts only descent
steps with admissible strain paths. The current C# Cholesky kernel deliberately
rejects these matrices, so this experiment is not an end-to-end C# decoder test.
Neither solver is accepted as the production reconstruction path.

The existing normal/polar geometric interpolation from saved control positions
fails: the complete surface has principal stretches **0.9076735–1.0238616**.
A halfway interpolation has range **0.9537838–1.0119278** and is also rejected.
Earlier reported extrema stopped at the first invalid face; the complete-surface
diagnostic now continues scanning after rejection. These are
failures of that initializer/path, not evidence that the final controlled shape
is physically impossible. No inadmissible predictor was accepted into simulation.

The 2,712-byte coordinate payload suggests room for other pair state but does not
establish the complete 4 KiB target / 8 KiB ceiling. A robust, branch-preserving,
fast reconstruction from saved data alone is still missing. Contact witnesses,
rod state, metadata, restore determinism, assets, cache rebuilding and full
performance must also be accounted for. V2 remains unavailable; V9/checkpoint-v7,
seasonal flowering/density and release gates remain pending. No material, growth
or clonal-spread parameter was changed.

This follows the static-elimination motivation of
[ConJac (Weidner, Kim and Sueda, 2020)](https://graphics.cs.yale.edu/publications/conjac-large-steps-dynamic-simulation).
ConJac applies condensation at velocity level for dynamics. The new kernel instead
uses exact static Newton elimination; it does not implement that paper's dynamic
integrator or establish its performance results for BAC shells.

Core and server builds pass with existing warnings, as does the standalone
NativeAOT probe. Frontend/playback and the production server AOT pipeline were not
rerun because their contracts and code paths are unchanged. Diagnostic harnesses
and large reference assets remain under `/tmp`, not in the repository.

### Sparse control predictor: admissible starting shape, not equilibrium

`ShellControlPredictor` accepts immutable precomputed sparse displacement weights.
It checks affine precision in all three coordinates, exact retained-vertex rows,
finite inputs, distinct controls and an explicit shared-profile payload budget.
Its reusable scratch contains only control displacements. Prediction preserves
rest geometry and rigid transformations of the curved surface; saved control
positions are copied exactly. A finite prediction is explicitly not an accepted
state: interior coordinates and directors still need relaxation, followed by
force, strain, contact and continuation checks.

The focused reference test uses 16-neighbour, affine-augmented thin-plate RBF
weights generated offline, following the
[SciPy RBF interpolation definition](https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.RBFInterpolator.html).
SciPy is a diagnostic preprocessing dependency, not a simulator dependency. The
119 control positions comprise 113 free vertices (339 doubles, **2,712 bytes**)
and six prescribed rest vertices. They are sampled from the known 25 µN solution;
all other starting coordinates are reconstructed from rest geometry and these
controls alone, with rest directors and explicit clamps.

On the 24,209-vertex reference, initial principal stretches are
**0.9980421–1.0025266**, inside the unchanged 2% bound. Backend positions agree
with independent SciPy interpolation within 1e-12 m. Warm prediction averages
**0.585 ms** over 100 calls with zero measured current-thread allocations.
The immutable binding costs **5,305,040 array bytes per shared profile**, and
predictor worker scratch costs **2,856 bytes**. This excludes fine output arrays,
relaxation workspace and object headers. No such arrays are stored on organs.
Neither the complete pair budget nor a production reconstruction time is proven.

The 21 focused checks also cover exact rest/control positions, affine precision
at normal and 1e-6 geometry scale, invalid rows, budgets, workspace ownership,
alias rejection, nonfinite input clearing, complete strain extrema independent
of face order, and NaN director rejection. The prescribed core build passes;
NativeAOT and frontend checks were not rerun for this limited step.

A separate cold sparse-LU experiment with a global 2D RBF initializer approaches
the reference shape but stalls at about **2.04e-9 N** positional residual because
its energy-only line search accepts vanishing steps. It was stopped, not marked
converged. No line-search changes were integrated in this step. Cold equilibrium
reconstruction, branch preservation, coupled contacts, compatibility and seasonal
acceptance remain open; V2 remains unavailable.

### Default guarded line-search fallback near roundoff

`ShellEquilibrium.RoundoffEnergyAllowance` enables a restricted derivative-based
fallback only when the ordinary Armijo test fails. Its default is **1e-19 J**;
zero disables it for comparisons. This absolute numerical allowance is not a
universal error estimate across all energy scales or a new force tolerance.
`RoundoffLineSearchSteps` counts its accepted steps per solve.
No per-organ arrays, species fields or saved formats were added.

The predicate in `ShellLineSearch` uses approximate-Wolfe equations 4.1–4.2 from
[Hager and Zhang (2005)](https://www.math.lsu.edu/~hozhang/papers/cg.pdf), with
delta=1e-4 and sigma=0.9. Additional restrictions require a descending direction,
finite data, a nondimensional step no larger than 1e-7, and at least halving
the complete free force residual (including
director torques converted with the existing characteristic length). Candidate
energy must remain within the supplied uncertainty; the existing continuous
strain-path check still applies. Final equilibrium criteria are unchanged. This
is a local acceptance fallback, not the paper's complete conjugate-gradient
algorithm or its convergence proof, and not general collision certification.

A flat 7-by-4 vertex shell (5 mm longitudinal / 4 mm transverse spacing,
100 MPa moduli, 0.1 mm thickness, default Poisson ratio 0.3) has its first two
vertex rows fixed. A tip loading sequence of 1, 5, 10, 20, 40, 80, 0 µN compares
identical solvers with allowance zero and 1e-19 J. Both require force <=1e-13 N,
zero relative force tolerance and nondimensional displacement <=1e-7. At 40 µN,
Armijo alone exhausts 80 iterations at 3.02e-13 N; the optional fallback converges
in 20 iterations at **3.75e-14 N**, using one rescued step. Stretch remains
0.9999857–1.0000199. Other loading stages converge, and release returns within
1e-8 of the mixed rest coordinates. An earlier four-load sequence at 1e-11 N
force tolerance yields exactly identical states with the option on and off.

**56 focused managed checks pass**, including synthetic cancellation, insufficient
force/slope improvement, real energy increases, oversized steps, invalid inputs,
unchanged clamps, release, rejected strain/plane starts and zero predicate
allocations. The prescribed core build and scoped whitespace checks pass.
Those first checks covered managed execution only.

Default enablement adds a 7-by-7 vertex strip at geometry scales 0.001, 1 and 10,
scaling mechanical thickness with length and applied forces/force tolerance with
length squared. Seed 42 selects the two load magnitudes. Each scale exercises
loading/reversal/release and a separate ceiling-contact/release sequence. The
ceiling is 50 µm above rest with 25 µm barrier activation at unit scale; positive
contact counts are required at both loaded states, then release recovers rest. Both
one-worker and four-worker default solvers converge with byte-identical states
and identical solver results; both agree in convergence with the disabled
comparison. The original 40 µN stall regression still takes 20 iterations and
one rescued step with the constructor default. **168 checks pass in managed
.NET and NativeAOT**, with byte-identical diagnostic output (including release
state hash). The prescribed core build and scoped whitespace checks pass.
This enables the fallback in the experimental shell solver only, not Bergenia's
V2 pipeline. No species coefficients, acceptance gates or saved formats changed.

At the time of the line-search checks the previous 24,209-vertex fixture was
missing and its stall had not been rerun. The next section records a fresh
reconstruction fixture and its separately measured outcome. A stricter
1e-12 displacement test was nonconverged in that earlier small fixture. The later
trial-step correction is tested down to 1e-10 below; it does not establish the
1e-12 case. Production integration remains pending. V2 remains unavailable.

## Cold reconstruction from saved controls (30 September)

`ShellControlReconstruction` now expands compact saved coordinates through the
immutable predictor, starts directors at their fitted rest values, and relaxes
all remaining free DOFs. It uses **no previous fine-state snapshot**. The physical
clamp mask is restored before checking complete force balance, including retained
vertices. Only interior convergence, admissible strain and a full residual below
an independent absolute gate publish the output. Invalid input/prediction, work
exhaustion and residual failure are distinct outcomes; all leave the accepted
output unchanged. Residuals use equivalent newtons: director torques are divided
by the shell characteristic length.

The fixture was rebuilt afresh from the current authored Bergenia blade, with five
attachment-edge insertions at 3 mm, fitted curved rest directors and two/four levels
of cubic-height-field refinement. It is not a byte-restoration of the deleted
historical fixture. The load is explicitly 25 µN in local +Y at authored vertex 36,
on the positive-Z flank. Moduli remain 100 MPa, thickness 0.1 mm and Poisson ratio
0.3. There is no gravity, petiole or contact in this free-blade fixture. Independent
sparse LU equilibrates the same BAC element model and certifies the complete linear
strain path at each accepted increment; it checks the global solve independently,
not the shell formulation itself.

| Fresh fixture | Level 2 | Level 4 |
| --- | ---: | ---: |
| Vertices / triangles | 1,589 / 2,976 | 24,209 / 47,616 |
| Full DOFs | 9,331 | 144,451 |
| Saved free coordinates / bytes | 339 / 2,712 | 339 / 2,712 |
| C# Newton / linear iterations | 5 / 6 | 5 / 6 |
| NativeAOT reconstruction seconds, single measurement | 2.143 | 176.187 |
| Complete residual, N | 7.9221e-11 | 3.3944e-12 |
| Maximum vertex error vs independent equilibrium, m | 1.1158e-12 | 1.9118e-14 |
| Principal stretch range | 0.99976488–1.00013712 | 0.99962568–1.00036555 |

The complete gate is 1e-9 N; interior tolerances are 1e-10 N, zero relative force,
and 1e-8 nondimensional displacement. Retained coordinates are exact. The fresh
full-size predictor has stretch 0.99653444–1.00537327, unlike the rejected older
initializer. Independent cold sparse-LU reconstruction also converges (12 accepted
increments at level 4), reaching 3.3943e-12 N positional residual and 1.8376e-18 Nm
director residual. The C# solver retains the default guarded roundoff fallback;
this result does not establish that the fallback was necessary. Existing projected
Newton suffices; no LDL replacement or material softening was introduced.

**23 checks pass in both managed .NET and standalone NativeAOT.** Level-2 serial
and 32-worker states are byte identical; managed/AOT diagnostics agree except timing.
The reconstructed state SHA-256 is
`80A2F0C7124F46C58DB946E166209B20B12AD0D59447CD3C696F335CF809B031`.
A 10% larger load at the saved control leaves the interior converged but 2.5e-6 N
unbalanced: the complete gate rejects it and preserves the output. Other checks
cover input/owner/alias validation, worker budgets, strain/plane rejection, numerical
exhaustion, clamp restoration and failed capture. Eight warmed settled-rest calls
allocate zero bytes on the calling thread; this is not a loaded-contact allocation
benchmark. The previous 168-check line-search suite still passes. Full-size NativeAOT
separately passes convergence, force, exact-control and reference-agreement checks.

**Accuracy passes; production cost does not.** The 2,712 saved bytes exclude
petiole/contact bookkeeping and growth-dependent rest assets. Neither the 4 KiB
target nor the 8 KiB complete-pair ceiling is certified. Fine coordinates remain
reusable active-worker scratch. Exposed array payload measurements are:

| Array payload, bytes | Level 2 | Level 4 |
| --- | ---: | ---: |
| Additional immutable reconstruction profile | 94,666 | 1,445,866 |
| Immutable predictor binding | 328,640 | 5,305,040 |
| Additional reconstruction worker | 80,360 | 1,161,320 |
| Reusable solver arrays and numeric factor | 46,327,739 | 1,200,504,043 |
| Shared symbolic factor pattern | 30,806,496 | 838,230,624 |

Those figures exclude object headers, IC dictionaries and thread-local parallel
scratch; shared BAC assets are also separate. The full NativeAOT process peaked at
2,518,084 KiB (about 2.4 GiB) and ran 179.60 seconds including setup. This is unsuitable
for production per-leaf reconstruction and is not a median/p95 or scaling benchmark.
Factor size/cost is the next bottleneck; compact storage alone cannot enable V2.

The verified [reference archive](../canopy-validation/shell-reconstruction-2026-09-30.zip)
contains rest surfaces, clamps, DOF maps, predictor bindings, equilibria, controls
and diagnostics, with formats, units and per-file hashes. Its compressed size is
9,314,534 bytes; SHA-256 is
`53d405b792ff4dce20611884d26b869339e1d0aa9d72e7e20941fadf566b0491`.
It excludes scratch matrices and diagnostic harness source, and is not a playback
or checkpoint format. Branch uniqueness, self-contact, support coupling, changing
biological rest geometry, contact-release continuation and seasonal acceptance
remain open. Fixed-rest profiles must be shared, not instantiated per organ with
fine arrays. No species coefficients, renderers or saved formats change here.

The prescribed core build passes (312 warnings, zero errors), as does the server
build with `SkipSpeciesGeneration=true` (169 warnings, zero errors). Scoped
whitespace and archive/source-hash checks pass. NativeAOT validation here is the
standalone mechanics probe, not a server NativeAOT publish. Frontend code is
unchanged and was not rechecked in this step.

## Sparse factor cost and trial-step recovery (30 September)

The experimental direct solver now uses a minimum vertex cover of each geometric
bipartite cut when constructing nested-dissection separators. The deterministic
Hopcroft-Karp implementation sorts cut neighbours and uses iterative augmenting
paths. This minimizes the separator for that particular cut, not global Cholesky
fill. The approach follows the matching-based edge-cut conversion discussed in
[Hendrickson and Rothberg, section 2.6](https://digital.library.unt.edu/ark:/67531/metadc687829/m2/1/high_res_d/448063.pdf);
it does not reproduce their complete multilevel BEND algorithm. Small systems
retain the existing minimum-degree ordering. Profiles compute ordering once.

On the saved full Bergenia topology, factor entries fall from 62,804,849 to
45,756,172 (27.1%). Input coefficients now occupy only 3,996,862 structural entries,
rather than a second factor-sized array. A streamed cursor for each factor column
replaces the two fill-sized row lookup tables. Sorting active columns preserves
the original arithmetic order for a given permutation. These changes remove no
matrix coupling and introduce no stiffness approximation. Numeric payload budgets
include the new integer cursor arrays; symbolic patterns remain separately reported.

Ordering alone exposed a near-equilibrium stall: the cold solve took ten Newton
iterations and 169.108 seconds, despite cheaper factors. A trace showed a
3.1012e-8 nondimensional correction being halved, followed by steps as small as
2.38e-7 of the proposed correction. The roundoff prefilter incorrectly tied trial
admission to the 1e-8 final stopping tolerance. It now uses the predicate's existing
1e-7 tiny-step bound independently. A trial may exceed the stopping tolerance;
the subsequent equilibrium still must satisfy the original force and displacement
gates. The 1e-19 J energy allowance, approximate-Wolfe slopes, required force
halving and complete strain-path check are unchanged. This remains the restricted
local fallback described above, not a convergence claim for general Newton solves.

| Full 24,209-vertex cold reconstruction | Previous reference | Revised |
| --- | ---: | ---: |
| NativeAOT solve seconds | 176.187 | 89.017 |
| Newton iterations | 5 | 5 |
| Solver array bytes | 1,200,504,043 | 595,961,947 |
| Shared pattern bytes | 838,230,624 | 284,740,180 |
| Whole-process peak resident KiB | 2,518,084 | 1,306,692 |

The final full residual is 3.39442e-12 N and maximum vertex error against the
independent equilibrium is 1.91176e-14 m; stretch remains 0.9996256810–1.0003655527.
All 339 saved coordinates remain exact. Saved control payload is still 2,712 bytes.
The complete force gate remains 1e-9 N and the interior final displacement gate
1e-8. Phase timings are in the JSON results. These are single process measurements,
not median/p95 statistics or seasonal/population performance acceptance. Large
worker memory and direct factorization remain unsuitable for production per-leaf
reconstruction. V2 remains unavailable; the complete pair storage gate is still open.

Validation includes 75,964 cover cases (every bipartite graph up to 4-by-4, random
seed-42 larger graphs and a 100,000-by-100,000 thin case), checking minimum size
against brute force on small graphs. All 76 factor checks pass: independently
computed residuals, bit-identical old/new factor results with the same ordering,
equilibration, indefinite rejection, recovery, exact budgets and zero warmed
factor/solve allocations. All 23 reconstruction checks pass in managed and NativeAOT,
including wrong-load rejection and unchanged output. New ordering changes low bits
relative to the old permutation; this is not a cross-version checkpoint claim.

The expanded line-search suite passes 444 checks in managed and NativeAOT with
byte-identical diagnostics. It includes final displacement tolerances 1e-7, 1e-8
and 1e-10 at geometry scales 0.001, 1 and 10, contact/release, unchanged force and
strain acceptance, serial/parallel identity and the existing 40 µN stall regression.
The source factor storage comparison and allocation checks are managed tests;
NativeAOT additionally exercises the actual small and full reconstruction paths.
No biological coefficients, growth rates, scene/checkpoint formats or renderer
interfaces change in this step.

The [performance evidence archive](../canopy-validation/shell-factor-performance-2026-09-30.zip) contains logs,
source hashes and a manifest referring to the preserved geometry fixture. It is
23,659 bytes; SHA-256
`76d62234f41ef24a888eea17f4f9284055f5f6fd8a5266f18622efc52b29144c`.
Temporary diagnostic harnesses remain outside the repository.

The final prescribed core build passes with 312 warnings and zero errors; the
incremental server build with `SkipSpeciesGeneration=true` also passes with zero
errors. Scoped whitespace and evidence-archive checks pass. NativeAOT checks here
use the standalone mechanics probes; no server NativeAOT deployment or frontend
change is included.

## Reusing direct factors within Newton solves (30 September)

`ShellEquilibrium` now tries the previous positive Cholesky factor as a PCG
preconditioner for direct systems above 3,000 DOFs. The default work budget is
16 iterations (`MaximumFactorReuseIterations`); zero disables reuse. This follows
the established separation of the current operator from a retained preconditioner
in [PETSc's reuse interface](https://petsc.org/main/manualpages/KSP/KSPSetReusePreconditioner/).
It is not a PETSc dependency or an implementation of multilevel StiffGIPC.

Every Newton iteration still assembles and applies the current tangent. Reuse must
pass a fresh matrix-product check, `||H d + g||₂ <= 1e-6 ||g||₂` at the reference's
unchanged linear tolerance, and give a descending direction. Detected nonpositive
curvature, nonfinite arithmetic, inaccurate residual or budget exhaustion causes
a fresh factorization and linear solve. The finite reuse budget is not an
obstruction or equilibrium criterion. The previous factor's positivity does not
certify positivity of the new tangent; normal descent, line-search, strain-path
and final equilibrium checks remain required. No old matrix replaces the current
mechanical response. Reuse never crosses a `Solve` call, so changed clamps,
loads or contact settings start afresh and no hidden cache must be saved.

| Full reference | Fresh factor each iteration | Bounded factor reuse |
| --- | ---: | ---: |
| NativeAOT solve seconds | 89.017 | 39.257 |
| Newton iterations | 5 | 5 |
| Linear iterations, including unsuccessful reuse work | 6 | 49 |
| Factorization phase seconds | 83.827 | 28.267 |
| Linear-solve phase seconds | 1.450 | 7.255 |

There are three numeric factorization attempts, four accepted reuses and one
rejected reuse on this full case. Complete residual is 3.39442e-12 N and maximum
vertex error remains 1.91176e-14 m; stretch is 0.9996256810–1.0003655527. All 339 saved
coordinates remain exact. The current objective, moduli, final force/displacement
gates and collision/strain checks are unchanged. The 2,712 saved control bytes,
595,961,947 solver-array bytes and 284,740,180 shared-pattern bytes are unchanged.
Only scalar settings/counters are added to the solver worker. Process peak is
1,307,456 KiB. This is a single fixed-population measurement;
39-second-scale reconstruction and the large workspace still fail production cost
requirements. The complete 4–8 KiB leaf/petiole-pair gate remains open; V2 is not enabled.

Validation: **219 checks pass in managed and NativeAOT** on 25-by-25 vertex strips
(3,651 DOFs), with seed-42 loading, reversal, active ceiling contact and release
at geometry scales 0.001, 1 and 10. Each compares a fresh-factor reference, one
worker and four workers, including exact serial/parallel states, unchanged force
and strain acceptance, reference-shape agreement, a fresh initial factor per call
and actual reuse. Managed/AOT diagnostics are byte identical. All **23 cold
reconstruction checks** also pass in both runtimes, with exact serial/32-worker
states and matching cross-runtime state hashes. The existing **444 managed
line-search checks** pass unchanged. The cold reconstructed state hash is
`12285937F70099507039ABE939E84B9AF4D2F144F6602D411AC805FA0100C535`.

A deliberately one-iteration reuse budget rejects all five reuse attempts on the
smaller cold fixture, rebuilds factors and still converges; exhaustion does not
become a false confinement result. Settled reconstruction retains its zero warmed
allocation result. Changed controls/loads, rejected starts, output rollback and
physical-clamp restoration remain covered by the reconstruction suite. These
standalone tests do not establish seasonal biology, arbitrary contact groups,
checkpoint continuation or whole-panel performance. Renderer and saved formats
are unchanged.

The [reuse evidence archive](../canopy-validation/shell-factor-reuse-2026-09-30.zip) holds diagnostics and
source hashes referencing the preserved geometry and previous performance fixtures.
It is 14,347 bytes; SHA-256
`9b8c7dbad03ce7e6fc3a1f4898387d98f5f8c8c0c07c7b4971129bbb818e333f`.
Diagnostic harnesses remain outside the repository.

The prescribed core and server builds pass with zero errors (312 and 155 warnings
respectively); the server uses `SkipSpeciesGeneration=true`. Scoped whitespace,
archive integrity and current source-hash checks pass. NativeAOT validation is the
standalone mechanics probes, not a server deployment. Frontend code is unchanged.

## Contiguous Cholesky updates and NativeAOT allocations (30 September)

The direct shell factor now precomputes consecutive row runs of at least 16
entries in its immutable shared symbolic pattern. Within those runs, vector
operations update independent doubles without changing the ascending column
accumulation order or introducing fused multiply-add or horizontal reductions.
Short/gapped regions and machines without SIMD use scalar arithmetic. Exact-zero
multipliers contribute no update. This is the constant-times-vector update used
by [BLAS DAXPY](https://www.netlib.org/blas/daxpy.f), specialized here for the
sparse factor's stored runs; no external BLAS dependency is added. The physical
objective, shell material, strain path and all convergence gates are unchanged.

A run cursor per column avoids repeatedly searching the run table. Cursors reset
on every factorization, including after a failed pivot. The 487,158 runs cover
88.77% of the symbolic update arithmetic on the full reference. A first attempt
using only each column's trailing run covered 26.47% and regressed to 41.790 s;
that implementation was discarded.

NativeAOT also exposed an existing allocation in the integer sorting used by
factor traversal: three warmed factor/solve repetitions on the smallest fixture
allocated 7,056 bytes. Passing one cached comparison delegate to the span-sort
overload eliminates those allocations. Both paths still sort unique column IDs
in ascending order. The generic comparer-to-delegate path is visible in the
[.NET 8 sort implementation](https://github.com/dotnet/runtime/blob/v8.0.0/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/ArraySortHelper.cs).

| Full NativeAOT reference, three fresh processes per version | Previous scalar factor | Current vector factor |
| --- | ---: | ---: |
| Median reconstruction seconds | 41.019 | 34.036 |
| Median factorization-phase seconds | 29.693 | 23.062 |
| Median complete process seconds, including setup | 43.376 | 36.447 |
| Maximum observed process peak, KiB | 1,307,596 | 1,298,088 |

Reconstruction improves by 17.0% and the factor phase by
22.3%. Final allocation-fix measurements follow the baseline
measurements rather than interleaving them. Three samples on one fixed population
are not a p95 estimate or an end-to-end production performance gate. NativeAOT
uses two double lanes here; managed execution uses four, and the scalar fallback
was tested with hardware intrinsics disabled.

All six full solves retain identical reported mechanical results: five Newton
iterations, 49 linear iterations, full force residual 3.39442e-12 N, maximum
reference vertex error 1.91176e-14 m and stretch 0.9996256810–1.0003655527.
There is no added saved leaf state; the controls still occupy 2,712 bytes.
The full factor adds 577,804 reusable worker-array bytes and 4,475,072 shared
pattern-array bytes. Total solver arrays are 596,539,751 bytes and shared pattern
arrays are 289,215,252 bytes; a single cached static delegate is outside these
array counts. These remain large worker costs, and the complete 4–8 KiB
leaf/petiole-pair gate is still open. V2 remains unavailable.

Validation: **8,903 factor and run-binding assertions** pass in managed SIMD,
managed scalar fallback and NativeAOT. These include exact scalar-reference
solutions, independent residuals, run bounds/continuity, nonfinite and indefinite
rejection, recovery, explicit numeric budgets and zero warmed factor/solve
allocations. Many assertions check topology bindings rather than separate
mechanical fixtures. The **219 loading/contact/release checks** and **23 cold
reconstruction checks** pass in managed and NativeAOT; contact diagnostics and
reconstruction hashes match the previous implementation, including exact worker
agreement. The **444 managed strict-line-search checks** pass. Core and server
builds pass with zero errors; the server uses `SkipSpeciesGeneration=true`.
No server deployment, frontend or saved-format change is included.

[Evidence archive](../canopy-validation/shell-vector-factor-2026-09-30.zip):
33,765 bytes; SHA-256 `fab6650d8a2267ff4bca1638cfcaf25592f3ecb569c68cab1146e09361b91bcc`. It contains measurements,
validation logs and source hashes; harnesses remain outside the repository.
The original reconstruction archive is unchanged. Seasonal biology, production
contacts, renderer/checkpoint compatibility and panel performance remain open.

## Shared factor profiles for prescribed increments (30 September)

`ShellFactorProfile` builds the symbolic pattern from only the couplings between
DOFs that may move. Its copied prescribed mask and pattern are immutable shared
assets. Prescribed DOFs retain isolated unit diagonals and the full state-vector
indexing; this is an exact restriction of Newton increments, not a reduced blade
model. The connection to Dirichlet elimination is described by
[PETSc](https://petsc.org/release/manualpages/Mat/MatZeroRowsColumns/).
Unlike that routine's preserved sparsity pattern, this profile omits guaranteed-zero
links before ordering and fill discovery. Prescribed increments are zero, so no
nonzero boundary-increment correction is needed in the right-hand side; the held
coordinates themselves need not be zero.

Use `ShellControlReconstruction.CreateSolver()` to share one lazily constructed
profile across reconstruction workers. The existing caller-supplied unrestricted
solver remains supported for comparison or changing constraints. General fixed
shell fixtures can pass a shared profile as the third `ShellEquilibrium` argument.
Profiles belong to one shell asset, are copied from complete masks and are not
stored in a global mask cache. Do not create them per biological leaf.

Before any solve changes its state, all profile-prescribed DOFs must still be
fixed. A violation throws a configuration error; it is not confinement or a
partial-growth result. Additional temporary clamps may be added and released.
A reconstruction workspace rejects an incompatible solver profile at construction.
After reconstruction, the physical mask is restored and the full force check still
includes saved controls: the optimization creates no physical anchor and cannot
hide reactions at those controls. Calling Solve directly on that released mask
is rejected; Reconstruct reinstates the compatible mask before its interior solve.

| Full NativeAOT reference, three interleaved processes per path | Unrestricted pattern | Prescribed profile |
| --- | ---: | ---: |
| Median reconstruction seconds | 33.620 | 32.020 |
| Median factor phase seconds | 22.848 | 21.721 |
| Median whole-process seconds, including setup | 35.939 | 34.351 |
| Maximum observed process peak, KiB | 1,302,136 | 1,244,300 |
| Factor entries | 45,756,172 | 43,775,343 |
| Solver array bytes | 596,539,751 | 579,788,463 |
| Shared symbolic/profile array bytes | 289,215,252 | 280,904,339 |

The measured solve reduction is 4.8%; arrays shrink by
25,062,201 bytes (23.9 MiB). The new shared total includes the 144,451-byte
prescribed mask. There are 3,542 prescribed increments: 3,203 physical clamps
and 339 retained coordinates. Saved controls remain 2,712 bytes, with no added
saved leaf state. CLR headers, the Lazy wrapper and thread-local element scratch
are outside the array figures. These are still large worker costs and not the
complete 4–8 KiB leaf/petiole-pair acceptance. Three samples on one shell do not
establish p95, seasonal or production end-to-end performance. V2 remains unavailable.

The profile reorders the free equations, so last-bit differences from the old
pattern are expected. Acceptance tolerances are unchanged: complete force residual
is 3.39442e-12 N, maximum reference vertex error 1.91176e-14 m and stretch
0.9996256810–1.0003655527. Five Newton iterations are retained; linear iterations
fall from 49 to 48 on this case. The unchanged material, geometry, rest state and
biological parameters were used for both paths.

Validation passes in managed and NativeAOT: **18 profile-contract assertions**,
**6,834 factor assertions** (including per-prescribed-DOF diagonal checks, five
mask patterns on four grids, independent residuals, temporary-clamp release,
rejected pivots/recovery, numeric budgets and zero warmed allocations), **219
contact/release checks** and **26 cold reconstruction checks**. Serial/parallel
states match exactly for the tested smaller reconstruction and contact fixtures,
and managed/NativeAOT hashes agree. Wrong control loads still fail the full force
gate while retaining accepted output. The **24 unrestricted NativeAOT checks**
retain the previous reconstructed hash. The **444 managed strict-line-search
checks**, core/server builds and scoped whitespace checks pass. Server builds use
`SkipSpeciesGeneration=true`; no server deployment or frontend change is included.

[Evidence archive](../canopy-validation/shell-prescribed-factor-2026-09-30.zip):
22,082 bytes; SHA-256 `4bd9a8b332347dd0d9945773c43c406a79eb24826702f3e7fefecd6b9db96372`. The original geometry archive is
unchanged. Harnesses remain outside the repository; production contact,
checkpoint/rendering, biological and panel-performance gates remain open.

## Compact element scatter bindings (30 September)

The shared factor pattern now stores only contributing element-to-matrix indices,
with one 21-bit column mask per local row and one starting offset per face.
Traversal retains the previous ascending row/column summation order. At boundary
triangles, the BAC element repeats vertex coordinates; all local contributions to
a repeated global diagonal remain present, including those above the local
triangle. A local triangular-half shortcut would change the stiffness.
Temporary clamps skip a packed row by its population count and still permit
release. Pattern construction remains once per shared profile; assembly allocates
nothing. This changes storage and traversal only: rest geometry, material,
force/strain/displacement gates and biological parameters are unchanged.

| Full NativeAOT reference, three interleaved processes per path | Dense scatter bindings | Packed bindings |
| --- | ---: | ---: |
| Shared symbolic/profile array bytes | 280,904,339 | 243,812,767 |
| Active solver array bytes | 579,788,463 | 579,788,463 |
| Median reconstruction seconds | 32.346 | 32.603 |
| Median whole-process seconds | 34.573 | 34.919 |
| Maximum observed process peak, KiB | 1,237,948 | 1,200,892 |

Shared array storage falls by **37,091,572 bytes (35.4 MiB)**. Runtime is
approximately unchanged; this is a memory improvement, not a demonstrated
speedup. The 2,712 saved-control bytes and active solver arrays are unchanged.
The complete leaf/petiole storage gate remains open; these large reference-worker
and shared-asset costs do not establish production feasibility. Array totals
exclude CLR headers and thread-local scratch. Three samples do not establish p95
or full-pipeline performance.

All full-size numerical diagnostics are unchanged: five Newton iterations,
48 linear iterations, full force residual 3.39442e-12 N, maximum reference vertex
error 1.91176e-14 m and principal stretches 0.9996256810–1.0003655527.
Validation passes **6,894 factor assertions** in managed, NativeAOT and managed
hardware-intrinsics-disabled runs. These include exact independent scalar
assembly, repeated boundary coordinates, packed binding coverage, five mask
patterns across four grids, temporary-clamp release, independent residuals,
failed-pivot recovery and zero warmed assembly/factor allocations.
Also passing: **18 profile-contract**, **219 contact/release**, and **26 cold
reconstruction checks** in managed and NativeAOT; **24 unrestricted NativeAOT**
checks; **444 managed strict-line-search checks**; core and server builds.
Both reconstruction hashes, serial/parallel agreement and cross-runtime contact
diagnostics are preserved. No production growth, renderer or checkpoint path is
changed; V2 remains unavailable pending the existing release gates.

[Evidence archive](../canopy-validation/shell-packed-scatter-2026-09-30.zip):
31,456 bytes; SHA-256 `df7ef7f733901518dd59fd9a8249a6fb44beda899ca8a8099215fc60e95965a0`. The source fixture archive is
unchanged; diagnostic harnesses remain outside the repository.

## Ordered four-column factor updates (30 September)

The sparse Cholesky worker can now apply four consecutive source columns together
when they share a contiguous suffix of at least 32 rows. It reads each destination
value once, performs the original four multiply/subtract operations in ascending
column order, then stores it once. Sparse prefixes remain on the previous path;
short tails, incompatible row ranges and zero multipliers fall back. Grouping
changes traversal across independent rows, not the operation order within a row.
There is no dot-product reassociation, fused multiply-add, altered pivot criterion
or added stiffness. The source-column schedule and symbolic factor remain unchanged.

This is a narrow application of the column-grouping/cache principle described in
[SuperLU's research report](https://www.netlib.org/lapack/lawnspdf/lawn127.pdf).
It is not a complete supernodal factorization or a new parallel solver. No BLAS
runtime dependency is introduced. Existing vectorization and the scalar fallback
use the same ordered arithmetic.

| Full NativeAOT reference, three interleaved processes per path | Previous kernel | Batched suffixes |
| --- | ---: | ---: |
| Median reconstruction seconds | 32.514 | 31.406 |
| Median factor phase seconds | 22.132 | 21.145 |
| Median whole-process seconds | 34.789 | 33.698 |
| Maximum observed process peak, KiB | 1,200,068 | 1,195,808 |

Measured median reconstruction time falls **3.4%**;
the factor phase falls **4.5%**. This is a modest
reference-kernel improvement, not acceptance of seasonal or full-panel performance.
Factorization remains the largest cost. Shared arrays stay at 243,812,767 bytes,
active solver arrays at 579,788,463 bytes, and saved controls at 2,712 bytes.
One 32-bit worker diagnostic counts successful groups and resets at every
factorization; array figures exclude object layout and headers. There are no new
matrix buffers or saved organ fields. The complete leaf/petiole storage gate
remains open, as do production V2 integration and its other release gates.

All full-size numerical diagnostics match the previous executable exactly:
five Newton iterations, 48 linear iterations, full force residual
3.39442e-12 N, maximum reference vertex error 1.91176e-14 m and principal stretches
0.9996256810–1.0003655527. Validation passes **6,915 factor assertions** in managed,
NativeAOT and managed hardware-intrinsics-disabled runs. The fixtures exercise
92,064 grouped updates per runtime and compare bit-identical solutions against
the previous factor kernel with independent scalar assembly. They also cover
mask changes, repeated boundary coordinates, independent residuals, zero updates,
failed-pivot recovery, diagnostic reset and zero warmed allocations.
Also passing: **18 profile-contract**, **219 contact/release**, and **26 cold
reconstruction checks** in managed and NativeAOT; **24 unrestricted NativeAOT**
checks; **444 managed strict-line-search checks**; core/server builds and scoped
whitespace checks. Both reconstruction hashes, serial/parallel agreement and
cross-runtime contact diagnostics are unchanged. No biological parameters changed.

[Evidence archive](../canopy-validation/shell-batched-factor-2026-09-30.zip):
29,344 bytes; SHA-256 `97de8297d5889948f1664ac64d3bcef72aa19dc779cfa7f7228282f393163921`. Source fixtures and earlier
results remain unchanged; diagnostic harnesses remain outside the repository.

## Extended validation and compact controls, 2026-09-30

The fixed-rest compact reconstruction is now backed by a bounded transactional
container, `ShellControlStateStorage`. It stores 339 free control coordinates as
doubles (2,712 bytes), six petiole angles (48), eight stable-ID contact witnesses
(256), a 128-byte header with the immutable profile SHA-256 and organ IDs, 128
reserved metadata bytes, and a conservative 96-byte ownership allowance:
**3,368 bytes per enabled leaf/petiole pair** in this fixture. Measured managed and
NativeAOT allocation is 3,336 bytes. This accounts for the whole container, not
only coefficients. It remains an experimental fixed-rest representation, not the
complete live-growth state or a V9/v7 serialization contract.

Capture cold-reconstructs the full shell and compares both positions and BAC
edge directors against the candidate. Restore rechecks the full equilibrium
under the supplied current loads and contact configuration. Both require the
caller's additional geometry/rod/contact validation before committing. Failed
validation, wrong loads, incompatible profiles, nonfinite data, aliased outputs
and numerical nonconvergence preserve the previous contents or output buffers.
A different equilibrium branch is rejected rather than silently substituted.

Fine vertices/directors remain bounded active-worker scratch. The extra capture
array costs 2,712 bytes per worker, bringing reconstruction scratch to 1,164,032
bytes at level 4. The solver/factor arrays are still about 580 MB and the shared
factor pattern about 244 MB; they are not per-leaf caches. Rest-area quadrature
adds 193,672 shared bytes at that level. These large reference costs remain a
production-performance blocker. Biological rest-size changes and an economical
shared profile/worker representation still need integration and validation.

### Contact quadrature

`ShellPlaneContact.SurfaceBarrier` integrates a barrier density with immutable
barycentric rest-area weights. Energy, gradient and Hessian receive the same
weight in both `ShellEquilibrium` and `ShellPetioleEnergy`. The coefficient has
units N/m³; multiplying its evaluated density by material area gives energy in
joules and reactions in newtons. Rendered relief, physiological area and tissue
moduli are unchanged. The four-argument legacy nodal barrier remains unchanged.

This follows the material-area quadrature principle in section 4.1 of
[Convergent IPC](https://arxiv.org/abs/2307.15908). The implementation here is a
one-sided infinite-plane primitive; it is not that paper's complete two-body
contact discretization or a certification of general contact convergence.

The 244-check analytic fixture covers constant gaps across refinement and
alternate diagonals, second-order convergence for nonuniform gaps, finite-
difference derivative checks, exact uniform equilibrium and release, and the
coupled blade/petiole gradient and Hessian. Both runtimes pass with zero warmed
coupled-kernel allocations. At 32 subdivisions the varying-gap energy differs
from its analytic integral by 0.0983%. Per-vertex legacy energy is deliberately
still vertex-count dependent for old fixture comparisons.

### Deterministic parallel kernels

Expensive factor columns partition their destination rows between workers. Every
row retains the original ascending source-column arithmetic; cursors advance only
after workers join. Expensive matrix products compute disjoint element results
and scatter in the original face/local-row order. They reuse the existing
element-gradient scratch after assembly. The reusable dispatcher joins all work
after an injected error and allocates no item per dispatch. Small work stays
serial. Worker selection follows `MaximumWorkers`, with explicit serial overrides
and a cap of 32. These are reference-kernel improvements, not an unrestricted
per-organ concurrency policy for production.

The final interleaved NativeAOT benchmark uses three fresh processes per path.
Existing parallel element assembly remains enabled in both paths; “serial” here
means serial factor updates and matrix products, not a single-thread solver.
The same 5 Newton / 48 PCG iterations and accepted geometry were retained.

| Reference reconstruction | Serial kernels | Parallel kernels |
|---|---:|---:|
| Median reconstruction | 30.802 s | 28.936 s |
| Median factor time | 20.480 s | 19.041 s |
| Median linear solve | 6.780 s | 6.329 s |
| Median whole process | 33.044 s | 31.214 s |
| Solver array payload | 579,788,463 B | 579,788,975 B |

This is **6.1% less reconstruction time**, with up to 9 factor workers and 32
product workers. It does not pass the production latency gate: a roughly
29-second reconstruction and 1.2 GB reference process remain far too costly for
routine per-leaf growth. Dispatcher objects/event overhead is additional to the
512-byte array increase, shared by each worker workspace, not charged per leaf.
An independent triangular-substitution SIMD prototype was slower and was not
included. Correctness runs interrupted by benchmarks are not timing evidence.

Factor tests pass 73 checks, matrix-product tests 47, with bitwise comparisons
against independent serial arithmetic in managed, NativeAOT and software-vector
paths. Warm factor/product kernels allocate zero bytes in those fixtures. One
initial software-vector dispatcher allocation check observed runtime startup
allocation; the warmed rerun passed. This is not a guarantee that the runtime
will never allocate during a worker dispatch.

### Extended mechanical fixtures

The 75-state matrix covers flank, tip, reverse and ceiling loads at quarter,
half and full dimensions, plus full-resolution flank/reverse/ceiling references.
All physical solves and cold reconstructions converged. Maximum reconstruction
position error was 5.76e-12 m, principal stretches ranged from 0.999567 to
1.000701, maximum displacement was 25.8 mm, and maximum displacement after
release was 1.41e-9 m. The smallest active plane gap was 56.6 micrometres.
These are numerical fixtures with unchanged primary material coefficients;
they do not establish measured Bergenia stiffness or general contact acceptance.

Compact-container tests pass 142 checks at level 2 and 42 at level 4, including
cold restore, changed loads, branch and director mismatch, failure rollback,
aliasing and callback rejection. Capture inputs and restore outputs are rejected
before mutation when their rod/contact buffers overlap mutable solver loads,
clamps or reconstruction scratch. The complete container stays 3,368 bytes at
both resolutions. Exact biological rest-size updates and live continuation are
still outside this fixture.


### Stronger fold and profile limitation

A 100-micronewton flank load at full resolution converged in 115 Newton / 1,784
PCG iterations, with 57.75 mm maximum displacement and principal stretches
0.998461–1.002887. The original 119-control predictor produced stretches
0.9551–1.0454 and was correctly rejected before relaxation. This disproves
unqualified acceptance of that compact profile under stronger loads.

The whole straight candidate motion exhausted a one-million-feature self-CCD
budget. Two subdivisions of the *same* path were both certified clear, using
1,464,407 feature queries in total; the full linear strain path also passed.
The return to the 25-micronewton state likewise certified clear in two intervals
(1,625,567 feature queries). Final release returned to rest within 2.78e-9 m;
all five physical equilibria converged. The original compact profile still rejects
the strong state, as recorded separately from the enriched profile.
No obstruction was inferred from the initial budget exhaustion, and no collision
exclusion or strain limit was relaxed. This fixture does not certify rotating
supports or general coupled contact trajectories.

An offline enriched profile adds 19 retained vertices selected from the failed
reference shape. It stores 396 free double coordinates, taking the complete
container from 3,368 to **3,824 bytes**. The shared interpolation bindings remain
separate from per-organ state. Its predicted principal stretches are
0.990632–1.015327. Fifteen held-out weaker flank/reverse/ceiling states also have
admissible predictors (0.997367–1.002697), and all 15 subsequently pass full
cold capture with equilibrium, strain and whole-state comparison. Managed and
NativeAOT capture and cold restore of the strong state converge
in 7 Newton / 82 PCG iterations, with 5.46e-15 m maximum vertex error,
1.19e-12 rad director error and 1.21e-12 N full-force residual.

This is an experimental profile trained on a particular reference case, not
proof that 138 controls suffice for every contact or biological size. The
medium reference deflects 49.30 mm at the same load, versus 57.75 mm at full
resolution; this is not a passed mesh-convergence gate. The 3,824-byte result
therefore advances the storage investigation without enabling V2.


[Fine-shell load and release figure](../canopy-validation/shell-strong-fold-2026-09-30.png).

The [extended mechanical evidence archive](../canopy-validation/shell-extended-validation-2026-09-30.zip) contains input profiles,
raw states, test logs, isolated benchmark samples and SHA-256 manifests (266 files).
Archive SHA-256: `b708ce20a196352b167cc7e3ee13aeb2d34f725627f8f71b63e45bf099f48c41`.

### Seasonal contact controls and biological limits

The archived 755-day request `e9bd78f470cd4e36bc6b715c50319d44` was rerun with
seed 42, deterministic scheduling and the current preset's complex assets.
Only tick size, light model and explicit deformation mode change between the
contact-enabled cases. Numeric dimensions, 84-day expansion, crown probabilities
and disabled clonal spread stay fixed. This is a controlled comparison, not
restoration of the archived executable or an exact replay of its physics.

| Mode | Light | Tick | Days completed | Whole leaves at last day | ≥90% area at last day | First petals |
|---|---|---|---:|---:|---:|---|
| 0 | ambient | 1 h | 755 | 6 | 0 | none |
| 0 | ambient | 4 h | 755 | 8 | 6 | none |
| 0 | CPU | 1 h | 755 | 10 | 0 | none |
| 0 | CPU | 4 h | 755 | 12 | 2 | none |
| 1 | ambient | 1 h | 755 | 2 | 0 | none |
| 1 | ambient | 4 h | 755 | 16 | 1 | none |
| 1 | CPU | 1 h | 755 | 10 | 0 | none |
| 1 | CPU | 4 h | 755 | 20 | 0 | none |

“90%” means `(actual length × radius)/(sampled target length × radius) ≥ 0.9`,
proportional to intrinsic blade area; it does not mean that both linear dimensions
have reached 90%. First 50%/90% times are retained per stable leaf ID at tick
resolution. Renewal creates a new leaf lifetime. A final snapshot alone can miss
flowering, so open petals are checked every tick.

In the foliage-contact isolation controls, 40 leaf lifetimes reached 90% in each
case. The medians among those lifetimes are 81.38/82.08 days under ambient light
and 161.75/177.83 days under CPU light (1/4-hour ticks). Remaining lifetimes are
censored or failed to reach the threshold; these medians are not an unconditional
growth duration or a published botanical target. The 84-day nominal setting is
unchanged, and CPU environmental/resource limits still affect elapsed expansion.

The diagnostic `SoftCollisions=false` controls retain soil and solid constraints.
All four combinations of 1/4-hour ticks and ambient/CPU light flower. The maximum
number of simultaneously developed leaves is 20 in both ambient cases and the
4-hour CPU case, and 18 in the 1-hour CPU case. Their final developed counts are 12/18
(ambient, 1/4 h) and 12/8 (CPU, 1/4 h), with 20 whole leaves each. First petals
appear on 9–17 April 2026. This is consistent with the general spring flowering
reported by [RHS](https://www.rhs.org.uk/plants/2209/bergenia-cordifolia/details),
checked on 30 September 2026; it does not validate cultivar-specific onset or
maturity. The 20-leaf capacity remains a provisional model parameter, not a
published universal Bergenia crown count. Foliage contact is therefore strongly
implicated in the stalling, without changing growth rates or enabling spread.
Disabling it is an isolation experiment, not the proposed production fix.

Resources and geometry remained finite in completed cases (water, energy and
volume checked; volume is not a full mass audit). `crownNodes` in the raw CSV
counts non-rhizome stem organs, not crowns, and `maxHeight` measures organ bases,
not complete blade-tip crown height. Those outputs must not be interpreted as
passed crown-height, whole-mass or flowering-morphology gates. The user's Rotblum
largest-blade 16 cm / petiole about 5 cm observations remain a separate dimensional
comparison; these experiments deliberately retain the archived larger targets.

Managed and NativeAOT rigid 4-hour CPU cases agree in all 755 daily biological
records and all six selected exported frames, byte for byte; wall-clock timing is
excluded. The current frontend reader also decodes the new V8 frames and recorded
geometry identities. In the completed 1-hour ambient V1 case, its day-755 frame
contains only 0.0428° maximum blade-side fold versus 7.30° maximum petiole
coordinate, matching the reported visible lack of blade folding. This is one
case's exported deformation, not a bound over all V1 states.

Existing frontend playback/lamina/build-scene suites pass 24 tests. Core and
server builds pass with existing warnings; standalone mechanics and the seasonal
probe run under NativeAOT. The server also publishes as linux-x64 NativeAOT on
net10.0, starts with an isolated temporary run store, and serves all eight species
records over HTTP. This startup/serialization check does not establish V9/v7
compatibility or full NativeAOT server simulation acceptance. Seasonal timing includes concurrent work and
some benchmark pauses; it is not a fixed-population performance comparison.

[Seasonal developed-leaf comparison](../canopy-validation/bergenia-contact-controls-2026-09-30.png).

The [complete seasonal evidence archive](../canopy-validation/bergenia-seasonal-controls-2026-09-30.zip) contains all 13
755-day cases, daily records, leaf lifetimes, 78 exported frames and SHA-256 manifests.
All exported leaf counts agree with the corresponding daily records.
Archive SHA-256: `1e32c4df81ddae065f85f9537b283824920049d4bd40eedca399df3b4f95be57`.

## Research alignment

[Better Bending, 2026](https://zhenchen-jay.github.io/uploads/Better_bending.pdf)
supports the BAC discretization and supplies the reference formulation and
rest-director/convergence methodology. Agreement with its equations does not
establish convergence of this particular coarse authored mesh.

[Barbič and James, 2005](https://www.cs.cornell.edu/~djames/research/)
supports mechanical subspaces and modal-derivative enrichment. Its precomputed
cubic StVK force evaluation is not reproduced here: BAC directors and strain
barriers are evaluated in full. The extension of static derivatives to response
bases is discussed by
[Rutzmoser et al., 2017](https://arxiv.org/abs/1610.09906).
Those methods motivate the experiment; they do not validate this Bergenia basis.
In particular, the comparison by
[Vizzaccaro et al., 2020](https://arxiv.org/abs/2007.05067)
identifies limitations of quadratic reductions for curved structures. No
universal large-deformation accuracy is inferred from small-load compliance.
The smooth reference uses the standard cubic-plus-affine formulation described
in the [SciPy RBFInterpolator documentation](https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.RBFInterpolator.html).
SciPy is used only as an independent temporary oracle; no runtime dependency
was added to the simulator.

[Harmon and Zorin, 2013](https://cims.nyu.edu/gcl/papers/harmon2013sil.pdf)
demonstrates local-response augmentation for contact deformations omitted by a
global basis. Its volumetric response functions and cubature do not establish
accuracy for this curved BAC lamina, whose measured residual is not localized.
[ConJac, 2020](https://graphics.cs.yale.edu/publications/conjac-large-steps-dynamic-simulation)
motivates considering condensation when a fixed subspace cannot retain static
equilibrium. Its velocity-level dynamic method is not implemented here, nor does
it establish this project's compact-state or collision-safety requirements.

[Discrete Elastic Rods](https://www.cs.columbia.edu/cg/rods/) supports explicit
material-frame transport and coupled rod/surface kinematics. The small six-angle
Gauss-Radau beam here is our reduced quadrature construction, not a reproduction
of that paper's complete rod model. Its compliance claims follow the stated
moment integrals and the independent beam fixtures; they do not establish
Bergenia torsion or large-deformation mechanics.

[C-IPC](https://ipc-sim.github.io/C-IPC/) motivates the planned coupled shell/rod,
thickness, strain and contact solve. [Tight Inclusion](https://continuous-collision-detection.github.io/tight_inclusion/)
and [rigid-body CCD](https://ipc-sim.github.io/rigid-ipc/assets/rigid_ipc_paper.pdf)
remain the continuous-collision references. The new linear inclusion kernel uses
the former paper's multiaffine interval-subdivision principle and public sample
queries; it is an independent managed implementation, not the authors' code.
The curved extension follows the rigid-body paper's requirement to bound the
actual trajectory, using direct interval kinematics for our reduced rod instead
of reproducing that paper's complete algorithm. The temporary coupled reference
uses the new plane and self-contact checks alongside the strain guard. Production
component optimization and arbitrary ancestor-path certification remain pending.

[StiffGIPC](https://arxiv.org/html/2411.06224v2) and
[domain-decomposed CPU cloth](https://sig25ddmpd.github.io/) motivate improved
conditioning and parallel coupled domains. Element parallelism and IC(0) here
are initial building blocks, not implementations or reproductions of those
papers' full preconditioners or performance claims.
