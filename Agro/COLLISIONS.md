# Collision response and canopy recovery

CPU hierarchy/cache changes, conservative collision geometry, optional CUDA,
operational controls and performance regression scenarios are documented in
[COLLISION_PERFORMANCE.md](COLLISION_PERFORMANCE.md). This document describes the
**certified** engine. A second, single-pass engine that never suspends growth is selectable
per run with `SimulationRequest.ContactEngine = Soft`; see [SOFT_CONTACTS.md](SOFT_CONTACTS.md).
Nothing below changes under that switch except that none of it runs.

On 14 October 2026 the second half of `ResolveHard` - the admissibility test, redirect, shorten,
rollback and escalation - was extracted into `EnforceStaticAdmissibility` so the mixed engine can
run it after its soft pass; the memo moved into `StaticAdmission`, which each engine's per-plant
work owns, and the clearance counters go through it. Nothing changed for this engine: a Geranium
beside a wall and in a wall corner, 90 days at 4 h, exports byte-identical frames before and after
(`D6D31032670EA425`, `5D1F603F4DE8B621`), and the checkpoint self-test now runs under all three
engines with a wall across the stand. The `shrinkNewborns` branches of that method, and the
set-aside second escalation, are reached only from the mixed engine. The default engine for a
request that omits the field is now `Mixed`; pass `Certified` explicitly to run this one.

Two general mechanics corrections of 12 October 2026 do change this engine's results, after bloom
and inside panicles only: a floral axis no longer softens or gains wood density with senescence,
and a bud collides as a capsule of its own radius rather than a ball of its length (see "Gravity
and support" in `AGENTS.md`). Byte-identity fixtures recorded before that date differ from that
point on; the 10-day pre-contact fixture and the checkpoint self-test are unaffected.

## Growth and survival

Collision handling does not call above-ground organ death. A failed increment
suspends that organ's elongation or widening; maintenance, photosynthesis, ordinary
senescence, resource mortality and independent sibling growth continue. Safe radial
strengthening is checked separately. `ObstructionCuts` remains a zero-valued legacy
diagnostic. `NaturalDeaths` and `ResourceDeaths` count removed organs, including
descendants, separately from rejected growth and developmental proposals.

The first three failed attempts are separated by 24 simulated hours; subsequent
attempts are separated by 72 hours. A 2 cm tip displacement, released contact or
changed obstacle list can wake a retry sooner. These intervals are computational
defaults, not measured botanical constants.

Births, crown activation, rhizome continuation, branching, flower commitment and
leaf renewal are staged in `PlantSubFormation.Growth.cs`. A pending transaction
retains sampled organs, stable parent IDs, local child references and resource
shares. Geometry rejection does not resample or charge the transaction.
Physiology continues while waiting. Renewal excludes the retiring petiole and its
descendants from the replacement's fit check, but retains them until the new
geometry is accepted. New organ ages start at acceptance.

Contact response first distributes elastic turns through existing support joints.
Compliance uses section geometry, tissue stiffness and the actual contact lever.
A joint's **total** bend, including gravity and repeated contact corrections, is
limited relative to its transported rest frame. Head-on impacts use a finite-angle
fallback. Failed elongation tests deterministic tangent directions, ranked by
deformation and the cached light cue, then retains a certified partial increment.
Pending births also test bounded alternative emergence directions without changing
their sampled branching angles.

Rollback restores changed contact components and then checks neighbouring geometry
again. Reverting a support transports its descendants but preserves their separate
elongation, widening and joint bends. Ancestor thickening alone does not expand a
child's rollback because it does not move the child's attachment.
The final fallback only removes rejected proposals and rechecks both constraint
sets; it does not restart elastic corrections that could recreate the collision.
Every moving support on the affected path participates, and support loads are
refreshed after restoration. Final restoration copies accepted frames exactly,
including sub-tolerance support changes: normalizing an unchanged quaternion can
move a tiny leaf across the finite attachment-exemption boundary and expose a
previously excluded contact. Repeated rollback must be idempotent.
Exhausting relaxation or a continuous-query budget never authorizes unchecked
motion. `UnresolvedFoliageContacts` reports configurations already intersecting in
the preceding state that could not be repaired; this must be checked in probes,
not hidden by reporting only collision deaths.

## Shapes and mechanics

`LaminaTemplates` is shared by simulation collision geometry and the leaf/petal
mesh endpoints. It uses the displayed coarse-outline triangulation, the same
segmentation slices, and the V7 export transform. Relief is authored in metres at
the morphology's reference width and scales with the developing blade's width in
both export and collision geometry. Previously a micrometre seedling retained a
mature blade's millimetres of curvature, repeatedly blocking crown development.
The immutable templates and their triangle hierarchies are shared across plants;
world-space triangle meshes are not stored per organ.

Stem–stem, stem–leaf and leaf–leaf discovery includes leaves in plant and organ
bounds. Parent–child segments are connected; other organs interact, including
leaves within one crown and exposed rhizomes. Petals are checked as sheets rather
than being omitted behind a receptacle bounding box; the old aggregation threshold
is retained only for species-file compatibility. Only a small immediate attachment neighbourhood is
excluded. Clipping that neighbourhood from both bodies avoids the old error where
ignoring the nearest joint contact also hid a second crossing farther along a leaf.

Continuous tests cover relative translation, parent rotation, elongation and
widening, using vertex–face and edge–edge distances. Swept bounds and conservative
subdivision prevent a blade from crossing a sheet between otherwise separated
endpoints. Common parent rotation and translation, including elongation of the
shared support, are removed from relative queries.
Persistent contact sides/features use stable organ IDs and are released after
separation, allowing motion around finite leaf edges. At coincident surfaces a
remembered side takes precedence over the organ-base fallback; a resolved nonzero
separation vector determines the current side after sliding around an edge.

Solid and foliage constraints alternate. Stems have flat ends against solids;
using a spherical cap on a very short widening crown falsely drove it into the
soil and repeatedly rolled back the plant. Buds retain rounded proxies.
The solid tolerance is 0.1 mm; the foliage numerical skin is 0.01 mm.
Default relaxation uses eight rounds, at most 5 degrees per joint per round,
with a 25 degree cumulative elastic limit.

Geranium scape continuations inherit their parent's accepted tangent:
`ScapeGravitropism = 0` means zero repeated steering. Floral lateral construction
does not apply generic shoot gravitropism. Floral supports use descendant centres
of mass and scape stiffness, so an offset bloom creates a moment even on an
initially vertical axis.

Soil support uses actual imported hulls, not the virtual resource lookup. Embedded
rhizomes are anchored; hanging spans retain their descendant weight and moments.
Frictionless upward surface reactions reduce residual bending demand before
overload suspends growth. Contact does not erase a branch's weight or create a
new root. Existing Carex segments and petiole joints supply the degrees of freedom;
there is no blanket petiole segmentation or deformable leaf-mesh solver.

## Directional light guidance

`SimulationRequest.GrowthLightGuidanceEnabled` is optional and defaults to true.
Mode 0 stays directionless. Modes 1 and 2 keep their scalar renderer irradiance
while CPU geometry supplies guidance; mode 3 reuses its CPU scene.
`SoftCollisions` controls foliage contacts, including same-crown contacts.
Solid-obstacle and soil constraints are independent of that switch.

Active vegetative apices and crowns probe 64 fixed upper-hemisphere directions,
weighted by diffuse overcast sky luminance and shaded by obstacles and canopy.
Nearby developing organs share the crown cue. The plant RNG is not used.
The cache refreshes after 24 hours or 2 cm of tip movement, smooths over 48 hours,
requires confidence 0.1, ignores changes below 2 degrees, and caps vegetative
steering at 3 degrees/day. These are **provisional model defaults**.
Steering is applied only with newly added meristem/petiole length; mature and
stalled organs retain their rest frames. Geranium floral continuations do not
receive light steering.

Separating growth orientation from elastic deformation follows the modelling
distinction in [Bastien et al., combined shoot tropism model](https://journals.plos.org/ploscompbiol/article?id=10.1371/journal.pcbi.1004037).
The numerical defaults above are not claimed to be calibrated from that paper.
Species habit references include [RHS Geranium macrorrhizum](https://www.rhs.org.uk/plants/7891/geranium-macrorrhizum/details/)
and [Kew's description](https://powo.science.kew.org/taxon/urn%3Alsid%3Aipni.org%3Anames%3A373281-1/general-information).

## Persistence, computation and saved runs

Live checkpoints are **version 23**. They include stable IDs, sampled developmental
transactions, independent admission and elongation retry state, sparse contacts and light-filter history. Older live
checkpoints are rejected explicitly; existing saved playback remains readable.
Version 13 separates pending organ-admission retry deadlines from the owner's
vegetative elongation retries. Version 14 changes certified scape emergence,
relative sweep subdivision and Bergenia's scape rest curvature. A request saved
before `BasalScapeEmergenceMaxTiltDegrees` existed inherits that value from its
named preset; an explicit zero still disables the search. Neither version
changes recorded V7/V8 scene playback. Version 15 retains the same payload
but changes accepted foliage rollback and certified post-rollback scape retries;
v14 and v15 live checkpoints are rejected, while V7/V8 playback remains readable.
Version 16 certifies each attached sweep subinterval with its own minimum
interpolated junction and organ radii; the fixed whole-sweep minimum had stalled
a growing Bergenia scape while its current pose was clear.
Versions 17–18 preserve sampled support-radius increments after the last contact
fallback. Version 19 keeps unfinished floral precursors viable through a bounded
bloom window. Version 20 certifies sampled leaf/petiole increments serially after
coupled rollback and expires an unplaceable first scape within its original
spring opportunity. These versions change live continuation without altering
recorded scene matrices or the checkpoint payload layout.
Version 21 preserves sampled floral-axis increments after the last fallback,
certifies bounded independent partial length/radius recovery, and corrects the
continuous test for changing organ thickness. The unchanged binary layout does
not imply continuation compatibility: v20 live checkpoints are rejected, while
their recorded V7/V8 playback remains readable.
Version 22 makes attachment containment use the same centre lines / blade surfaces
as attachment clipping. It removes false swept rejection of a neighbour's safe
bend without enlarging the excluded junction. The payload layout is unchanged;
v21 live continuation is rejected, and recorded playback remains readable.
Version 23 orders growth-retry distance and sweep queries exactly like the main
contact solver: stable organ IDs within a plant, active-plant order across plants.
Finite conservative CCD budgets must not certify a motion in the main pass and
then reject that identical motion solely because a retry reverses the pair.
The payload layout is unchanged; v22 live continuation is rejected explicitly.
Recorded playback and its geometry assets are unchanged.
Version 8 corrected resource allocation and root-growth units. Version 9 adds
construction escrow and retained root-branch funding intent to the agent layouts;
version 10 adds the separate seed energy pool, corrected germination/maintenance
semantics and canonical CPU-radiosity persistence. Version 11 saves debited soil
water and the imported virtual reservoir; see [SOIL_WATER_BUDGET.md](SOIL_WATER_BUDGET.md)
and [LIGHT_GROWTH_CALIBRATION.md](LIGHT_GROWTH_CALIBRATION.md).
V7 playback records contain no new collision or guidance payload. The old geometry
in a saved run is not repaired retroactively.
Version 5 also requires unmodified numerical species inputs: older stored requests
contain coefficients already converted by initialization and cannot reliably rebuild
the same live world. See [BERGENIA_CALIBRATION.md](BERGENIA_CALIBRATION.md).

Collision meshes, BVHs, clearance proofs and bounded exact-query memoization are
derived and reconstructible. Template hierarchies prune triangle candidates.
Template lookup uses non-capturing cached factories, and immutable hierarchies use
worker-owned traversal scratch. Per-plant exact pair caches survive worker changes;
whole-body clearance certificates expire conservatively with relative motion.
Cached pair results expose the same bounded query result as a fresh query; extra
historical clearance must not let warm caches bypass a sweep that resume would run.
Independent per-plant constraints run in parallel. Cross-plant pairs use a
deterministic sequence of batches with no plant shared inside a batch; independent
pairs run in parallel, and development commits remain ordered. Accepted motion is never certified
by silently dropping candidates. Near-contact search can still be expensive, so
measure the complete collision phase on representative crowded scenes.

The existing run archive and storage changes are retained: indexed `.agro`
records load lazily, legacy JSON playback stays readable, compression remains
lossless, and the server applies a shared 128 MiB pending raw-frame budget.
There is no per-frame contact mesh or guidance history. Resuming preserves the
old tail in a recovery directory and publishes a revised ordered frame index.

## Regression workflow

Use a temporary net8 console probe outside the repository, seed 42, explicit
initial date and render mode. Compare 1-hour and 4-hour steps for mechanics/timing,
inspect finite mass/water and actual exported V7 frames, and check live checkpoint
resumption across contact, retry, reindexing and guidance refresh.

Required scenarios:

| Scenario | Acceptance |
| --- | --- |
| Prolonged obstruction | No collision death; daily retries then 72-hour backoff; siblings and safe thickening continue. |
| Rejected birth/renewal and obstacle removal | Sampled opportunity and resources retained; old viable foliage stays until replacement fits; removal wakes growth. |
| Thin versus thick support chain | Correction shared by stiffness, section and leverage; cumulative bend cap holds; elastic relaxation after release. |
| Head-on scape below tray | Deflection or safe partial extension, no newly penetrating retained increment. |
| Supported versus hanging rhizome | Actual reaction reduces residual moment without deleting weight or anchoring an airborne span. |
| Carex approaching either side of Bergenia | No sheet piercing; finite-edge escape remains possible. |
| Coincident contact with contrary organ bases | Preserve the established contact side; use the base-side fallback only without contact history. |
| Same-crown, widening and separated projected leaves | Same-crown contacts checked; width sweeps checked; projection overlap alone is not a collision. |
| Seedling curved lamina | Relief scales with actual blade width in collision and V7 geometry; a microscopic leaf must not retain millimetres of mature relief. |
| Shared parent elongation/rotation | Remove common attachment transport before testing relative motion; separated sibling leaves must not be suspended by rigid shared movement. |
| Support rollback | Revert the responsible support increment while retaining independently admissible child elongation and bend; ancestor thickening does not trigger branch rollback. |
| Attachment-boundary rollback | Restore accepted poses bit-for-bit, including tiny support-frame changes; a rejected tip at the exemption boundary must not drift outside it during repeated restoration. |
| Leaf plus tray contact | Alternating constraints and local rollback preserve mutual admissibility. |
| Geranium flowering | Accepted tangent inherited; offset bloom mass bends supports at both tick sizes. Record daily bud/petal counts through the season; an empty final snapshot can follow a completed bloom. |
| Persistent sky opening | 64 deterministic rays, cached/smoothed cue, bounded new-tissue steering; mature frames and scalar energy unchanged. |
| Live checkpoint v16 | Byte-identical resumed exports through pending transactions, reindexing, contact release and guidance refresh; exclude spare irradiance buffer capacity. |
| Seed hydration and resting roots, 1h/4h | Water delivery must not create energy; germination conserves both pools across segmented blades/petioles; exactly funded maintenance retains the root and descendants, while a shortfall is detected. See LIGHT_GROWTH_CALIBRATION.md. |
| Four-plant contact scheduling | Independent contacting pairs run concurrently; checkpoint replay remains identical across worker scheduling. |
| Existing playback/archive consumers | Existing scene records and archive random access still decode. |

The reference photos span 4 April, 11 May, 13 May, 8 June and 12 June 2026:
69 days of **established perennial spring growth**, not a seedling calibration.

The facade acceptance run uses September's complete OBJ and all 790 plants in
`fav12_seeds_P003.json`, through a year and the following April–June. Compare the
old build, corrected mechanics without guidance, and corrected mechanics with
guidance. Record monthly actual triangulated living leaf area, living crowns,
projected canopy coverage, paused tips, rejected births, mortality reasons,
runtime, allocations, peak memory, exported-frame size and saved-run compression.
Keep irrigation, nutrition and planting-size calibration outside this revision.

Build with `dotnet build Agro/Agro.csproj --no-restore -m:1 -v:minimal`.
Also build the server consumer (optionally `-p:SkipSpeciesGeneration=true` to avoid
rewriting generated species assets), run relevant playback checks, and run
`git diff --check`. Passing compilation alone is not acceptance of canopy recovery.

## Broad elastic foliage (experimental)

`ContactDeformationMode.BroadLeafV1` adds four blade coordinates and two
two-axis petiole joints without adding biological organs. Defaults remain `None`.
See [CONTACT_DEFORMATION.md](CONTACT_DEFORMATION.md) for the V8 geometry contract,
mechanics assumptions, measured acceptance gates and outstanding release work.

Add these to any contact regression run: translated/tiny folded blades; material
strain and nonadjacent facet sweeps; tip/tray confinement followed by removal;
off-centre loading and reversible release at 1 h and 4 h with growth frozen;
identical exported frames after v6 continuation; V7 legacy playback; WebGL/WebGPU
vertex parity, picking and baked geometry downloads. Keep the existing support,
relative sibling sweep, pending development and independent-pair regressions.
## BAC shell regression gate

The shell replacement remains outside production collision handling. See
[`SHELL_MECHANICS.md`](SHELL_MECHANICS.md) for its fixture definitions, current
results and outstanding integration gates. Preserve the native-versus-refined
Bergenia flank-load fixture, fitted rest directors, Hessian-only/boundary
derivative checks, large-load release and serial/parallel equality. A work limit
or an inadmissible initial shell must never be interpreted as physical confinement.
The current roughly 50% compliance difference under refinement is an unresolved
gate, not a target to encode as correct behavior.
Also retain the full-path strain regression: an endpoint-valid 180-degree
rotation must reject its collapsing linear vertex path; safe small rotations,
translations and ordinary expansion must pass at tiny and adult scales.
Near-limit cancellation must return uncertified rather than authorize a step.
This strain check does not replace vertex-face/edge-edge or curved-support CCD.
Keep smooth-rest references separate from the unchanged faceted mesh. Compare
both flanks, the distal tip and area-distributed loading before accepting a
compact shell; record its error and complete persistent-state cost (8 KiB ceiling,
4 KiB target). Include encoding/recovery force checks: float vertex rounding can
destroy equilibrium even when the geometric error appears tiny. Preserve the
plane-contact force-balance, frictionless sliding and release tests, including
the case where one large step reaches equilibrium and the next correction is
below roundoff. State updates must reject overflow/failed validation without
changing accepted bytes, revision or dropping contact witnesses. These are
standalone shell regressions; full production collision acceptance is pending.

Preserve the three-span petiole's end-moment, transverse-tip and uniform-load
compliance, including both tip displacement and rotation. Test cached attachment
derivatives at zero and finite bends, off-centre contact, and equality of the
blade junction and actual rod tip. The coupled strip must deform both rod and
blade, recover on release, and reduce to the independent shell/beam references
when one component is fixed. Certify the coupled fixture's accepted steps against
whole-path plane and self-contact checks; endpoint gaps alone do not certify
curved sweeps. Include conditional Fixed48 state encoding at tiny and mature scales:
failed force balance, coordinate overflow or excess witnesses must retain the
previous accepted state. Select/record a finer coordinate unit or full precision
when required; the 4 KiB target never overrides the physical acceptance checks.


The standalone linear CCD gate also requires the published Tight Inclusion sample
queries (record the dataset commit and exact rational-to-double conversion).
Keep false-clear, conservative-contact and uncertified counts separate. Budget
exhaustion must retain its work/capacity diagnostic and certified motion prefix.
Check both vertex/face directions, unique edge pairs, self-contact, coplanarity,
degenerate features, nonzero clearance, tiny leaves and common translations.
Explicitly test an edge already piercing a face at the initial state; VF/EE
first-contact events alone cannot certify that start. Compare mesh-BVH outcomes
with exhaustive feature enumeration and repeat changed geometry through the same
worker buffers. Check zero steady-state allocation and managed/NativeAOT parity.
These are straight vertex paths, not certificates for curved support trajectories;
full contact solving and production integration remain required.


For the standalone curved support kernel, preserve both arc-versus-chord cases:
an arc collision with a clear endpoint chord, and a clear arc whose chord hits.
Test VF, EE, mesh pairs, half-spaces, initial piercing and the certified prefix
against exhaustive features and known analytic crossing times. Remove common
translation/base rotation and the full common tip frame before relative tests.
Check the exact documented rotation-vector/length interpolation; unsupported
ranges must return uncertified, never fall back to chords. Repeat varying
lengths, origins, base rotations and bend angles through warm and cold transform
caches. Compare sampled enclosures against independent high-precision kinematics,
retain zero warm-query allocation, and require managed/NativeAOT numeric parity.
The load/release fixture must preserve equilibrium and recovery with path checks
on every accepted step. These checks do not certify finite-radius rods, arbitrary
ancestor trajectories, production contact forces or seasonal biological recovery.


Retain the energy-resolved shell accuracy checks: analytic half-cylinder bending,
independent StVK extension, thickness scaling and rigid-motion invariance. Keep
membrane, bending and strain-barrier energies separate so numerical errors cannot
cancel in a misleading total. Conforming local refinement must preserve authored
vertices and the projected outline, split shared edges without cracks, retain
ancestry, reject invalid/over-budget requests and produce the same identity for
reordered or duplicate edge requests. Uniform profile identities must remain
unchanged. Compare compact candidates against both flanks, tip and distributed
loads, and check exact encoded force balance within the 8 KiB budget. The latest
candidates remain rejected; their measured errors are not regression targets to
accept as physically correct. No biological or material tuning substitutes for
mesh convergence.

Retain rest-preserving surface-transfer checks before using a compact control
mesh: exact rest recovery and rigid-motion covariance, analytic Jacobians and
full energy Hessians including cross-sample coupling, independent polar/SVD
agreement, tiny and degenerate triangles, stale-output clearing and repeated
bulk-frame refresh. Shared bindings and worker scratch must be accounted apart
from organ state; warm evaluation must allocate nothing. Compare both geometry
and membrane energy after reconstructing the fine-reference states. The current
projected-triangle mapping has sub-percent displacement errors but unacceptable
membrane energy, and must remain rejected. Passing strain limits is insufficient.
Prolongation is not a collision certificate: fine motion, contact, strain and
BAC directors require their own validated coupling and safe continuation.


The exact coarse/fine shell reference must compare matrix-free Hessian products
with independently assembled dense/sparse operators, retaining all BAC director
and geometric-stiffness terms. Validate the complete operator before eliminating
clamped rows. For reduced stiffness comparisons, coarse constraints must imply
the fine attachment constraints: check the transfer Jacobian on fixed fine
vertices and constrain its null space. Do not silently discard reactions from
fine vertices that still move with coarse controls. Keep coarse positional,
fine positional and director residuals separate (N, N and N m respectively),
with explicit clamp masks; reduced convergence is not full-space equilibrium.
Preserve prepared-state/load snapshots, invalidation on unsuccessful or
energy-only trials, rigid-motion/identity-transfer checks, fine-strain rejection,
zero warm allocation and managed/NativeAOT parity. The projected mapping remains
rejected even with freely relaxed directors. Its large reference state and
workspace are diagnostics, never permission to exceed the accepted pair budget.

The mechanical-response subspace must also retain full, explicitly masked force
and director-torque residuals. Test loads omitted from basis construction;
agreement on training loads or a small reduced gradient is insufficient. Its
affine path guard must reject a rigidly rotated endpoint whose linear midpoint
collapses, and leave the prepared energy/load snapshot unchanged on either path
outcome. Compare serial and parallel face batches exactly, including invalid
strain followed by recovery, and check managed/NativeAOT products against an
independent operator. Do not report basis-dependent reduced gradients as forces.
Reference subspaces remain outside production contact handling until large-fold,
contact and full equilibrium gates pass. Account for shared basis assets and
bounded worker storage separately from the complete leaf/petiole state.

Nonlinear subspace regressions must include a held-out load level, not just
reconstruction of training snapshots. The 108-coordinate Bergenia reference can
reproduce its trained 50 µN equilibrium but fails full force balance at 25 µN,
despite a 0.0911 mm maximum position error and converged reduced equations.
Keep that failure visible. Compare current-tangent corrections and free-load
release against the full shell without changing material or strain tolerances;
free-load release is not contact-release certification. Check full fine products
against assembled deformed-state operators, retaining fixed reactions. Reusing
the geometric ordering for a reference factor must preserve its permutation,
and independent sparse-reference costs must not be presented as production
contact performance. Compact coefficient bytes exclude the separately measured
shared basis and bounded worker arrays, and are not a complete pair-budget pass.
For the bare-blade unloading reference, require both force balance and a small
remaining Newton correction; a force-only stop left 17 nm of recovery unfinished.
Continue a progressing reference solve after a work budget without calling that
budget physical confinement. The full reference recovers to 1.49e-12 m after 57
accepted strain-certified increments. This does not replace self/solid-contact
release tests or establish production latency.

For optional shell tangent caching, compare both full and reduced products with
direct differentiation, including a compressed state with negative stiffness.
Preserve prepared state across product failures/path queries; invalidate cached
blocks on new, failed and energy-only state evaluations. Reject insufficient
worker budgets before allocating the blocks. Check serial allocation, deterministic
worker reductions and NativeAOT parity on both small and fine curved fixtures.
Benchmark complete state-evaluation/product batches: building a cache costs more
than one directional product, so short batches must retain the uncached option.
Cache payload is worker memory, never an uncharged cache proportional to leaves.
The 163-coordinate loading/unloading basis still fails the untrained 25 µN force
test at 23,648 free vertices, despite a 14.3 µm maximum position error. Preserve
that rejection and the distinct nonconvergence of the fixed-budget PCG probes.

For static shell condensation, compare Schur products, corrected residuals and
lifted Newton steps against an independent full solve, including nonzero interior
forces and mixed position/director retention. A zero retained correction must
still relax the interior; no interior must reduce to the full free system. Check
implicit energy derivatives after re-relaxation, loading/release with complete
strain-path checks, clamp reactions, negative retained stiffness, unstable interior
rejection, snapshot invalidation, budget checks, allocation and NativeAOT parity.
Measure full fine residuals, never call reduced stationarity physical equilibrium.
The earlier 339-coordinate fine reconstruction was only a local sparse-LU oracle
result; its near-solution start did not establish cold C# reconstruction. The fresh
fixture below now covers one cold reconstruction with the projected C# path.
Keep the earlier cold geometric initializer's complete range
0.9076735–1.0238616 and halfway range 0.9537838–1.0119278 as rejected cases;
earlier extrema covered only the prefix through the first rejected face.
Do not relax the 2% bound
or claim that either numerical rejection proves physical confinement. The 2,712
coordinate bytes alone do not establish complete per-pair storage or checkpoint
continuation. No saved run may depend on an unrecorded nearby fine-state cache.

For sparse control prediction, verify affine precision on curved and tiny rest
surfaces, exact saved controls, immutable profile budgets, invalid-input clearing
and zero warmed allocation. Compare vertices with independent interpolation and
scan the complete strain range even after an invalid element. An admissible
starting shape does not establish force equilibrium or collision clearance.

For the default guarded shell roundoff line search, retain the 7-by-4 strip loading
sequence 1, 5, 10, 20, 40, 80, 0 µN. At a 1e-13 N force gate and 1e-7
nondimensional displacement gate, compare zero energy allowance with 1e-19 J:
the 40 µN case must recover equilibrium through the guarded fallback while
preserving clamps and the full strain path. Reject ascending directions, real
energy increases, insufficient force/slope improvement, nonfinite inputs and
oversized steps. Invalid strain or plane starts must remain invalid. The option
defaults to 1e-19 J; zero remains available for the disabled comparison. Also
exercise a 7-by-7 strip at geometry scales 0.001, 1 and 10, scaling thickness
with length and forces/tolerance with length squared. Compare serial and parallel
loading, reversal, ceiling contact and release with seed 42 and exact states.
These fixtures do not certify the large leaf reconstruction or enable V2.

For compact cold reconstruction, retain the fresh 1,589- and 24,209-vertex
Bergenia fixtures in `canopy-validation/shell-reconstruction-2026-09-30.zip`.
Rebuild exclusively from 339 saved free control coordinates, the shared rest
surface/directors and immutable sparse interpolation weights. At the recorded
25 microNewton load, require exact retained coordinates, the full equivalent-force
residual below 1e-9 N (including retained vertices) and vertex agreement with the
independent sparse-LU equilibrium within 1e-8 m. Enforce the strain path throughout.
Increasing the load by 10% at the saved vertex must fail the complete force gate,
even when the interior solver converges; the accepted output must remain unchanged.
Also retain invalid-input, alias/owner, budget, strain/plane rejection, restored
clamps, zero settled-call allocation and serial/parallel/NativeAOT checks.
The 2,712 saved bytes exclude petiole/contact state and immutable assets. Record
all reusable worker storage separately: the large direct solve currently needs
about 176 seconds and 2.4 GiB process peak, so it fails the production cost gate.
These are fixed-rest free-blade reconstruction cases, not self-contact, support
coupling, growth, saved-run compatibility or biological acceptance.


For sparse shell factorization, preserve exact accumulation order when streaming
factor-row traversal. Compare against the previous numeric factor with the same
permutation, including diagonal equilibration, rejected indefinite tangents and
successful reuse after rejection. Enforce the complete numeric workspace budget
and zero warmed factor/solve allocations. Separator covers must cover every cut
edge and match brute-force minimum cardinality on small graphs; deterministic
ordering and graph partitioning may not drop DOFs or mechanical couplings.
Re-run both archived cold reconstruction sizes with their existing acceptance
gates, including reactions at saved controls. For the roundoff fallback, keep the
1e-7 trial-step bound independent of the final displacement tolerance; verify final
stopping at 1e-8 and 1e-10 as well as 1e-7, with scaled contact/release fixtures.
The expanded managed/NativeAOT suite has 444 checks. These solver optimizations do
not constitute production contact, checkpoint or seasonal acceptance.


For direct-factor reuse, apply the current tangent and check its true linear
residual before accepting the direction. Curvature failure, residual failure or
reuse-budget exhaustion must rebuild the factor, not report confinement or accept
an inaccurate step. Reuse may not cross Solve calls. Keep zero as the fresh-factor
comparison and exercise a one-iteration budget that forces rejection/rebuild.
Compare 25-by-25-vertex loading, reversal, ceiling contact and release at three
geometry scales, plus the two archived cold reconstructions; preserve force,
strain and final displacement gates, exact serial/parallel states and output on
rejection. Record accepted/rejected reuse and factorization attempts separately.
The default 16-iteration reuse budget applies only to direct systems above 3000
DOFs and adds no leaf state or worker arrays. Production acceptance remains separate.


For vectorized shell factors, validate every shared contiguous run's bounds and
row continuity, including partial runs entered mid-column. Compare scalar and
SIMD solutions exactly with unchanged permutation/column accumulation, including
clamped zero coefficients, nonfinite/indefinite rejection and subsequent recovery.
Reset numeric run cursors on each factorization. Exercise managed SIMD, hardware
intrinsics disabled and NativeAOT separately; require zero warmed factor/solve
allocations in each runtime (the previous NativeAOT integer sort allocated).
Re-run the 219 contact/release checks and both saved cold reconstruction sizes
without changing force, strain or displacement gates. Report shared run metadata
and reusable cursor arrays separately from saved leaf state, and keep repeated
fixed-population timings separate from production biological/performance gates.


For prescribed shell-factor profiles, copy the mask into a shared immutable asset
and reject a freed profile-prescribed DOF before any Solve state change. Reject
wrong-shell and incompatible reconstruction-workspace profiles. Additional temporary
clamps may be added and released. Compare with unrestricted factors on independent
matrix residuals and loaded/contact/release fixtures; ordering changes may alter
roundoff but must not change force, strain or displacement acceptance. Reset after
failed factors and preserve zero warmed allocations in managed and NativeAOT.
After cold reconstruction, restore physical clamps and include forces on retained
controls in the complete acceptance check; a changed control load must still fail.
Count shared mask storage separately from worker arrays and saved leaf coordinates.
Keep the unrestricted saved-state hash and serial/parallel/runtime determinism
checks. This is a fixed-rest reference optimization, not production anchorage or
permission to prune contact couplings.


For packed shell scatter bindings, compare assembly exactly with a scalar
21-by-21 traversal in global-DOF lower-triangle order. Boundary element maps
repeat vertex coordinates: preserve both local off-diagonal contributions when
they land on a global diagonal. Verify every row mask and packed slot, including
all-fixed and sparse prescribed masks plus temporary clamps and release. Require
zero warmed assembly/factor allocations with managed intrinsics enabled, disabled,
and NativeAOT. Retain both reconstruction hashes and contact-release diagnostics.
Report shared binding arrays separately from saved organ state and worker arrays.


For grouped factor updates, require four consecutive ascending source columns,
a common contiguous suffix, and unchanged operation order per destination row.
Compare exact solutions with the previous single-column kernel and independent
scalar assembly; exercise sparse prefixes, short tails, zero multipliers,
temporary clamps and release. Require nonzero grouped-update coverage in managed,
NativeAOT and software-vector fallback tests, plus reset after failed pivots.
Retain zero warmed allocations, both cold reconstruction hashes, and the
contact/release diagnostics. No tolerance or stiffness changes may compensate
for a difference introduced by grouping. Account for diagnostic worker scalars
separately from array payloads and saved organ state.


For the fixed-rest compact shell container, capture must cold-reconstruct and
compare all positions and edge directors, then run the caller's geometry/rod
validator before committing. Exercise wrong loads, another equilibrium branch,
nonfinite unretained coordinates, moved clamps, aliased outputs, callback
rejection/exception, witness overflow and release/replacement of contacts.
Failure must preserve the old revision/data or all caller output buffers. Measure
the complete container (3,368 accounted bytes in the 339-coordinate fixture),
including rod angles, witnesses, identity and reserved bookkeeping; separately
report immutable assets and active-worker arrays. Passing this fixture does not
certify live biological rest-size updates, checkpoint continuation or V9 playback.

For surface-area plane barriers, verify constant-gap energy under refinement and
alternate triangulation, convergence against a nonuniform analytic gap integral,
and matching energy/gradient/Hessian area weights. Repeat derivative checks after
pulling contact forces back through the petiole. Preserve the legacy nodal API's
units and results. The plane primitive is not a substitute for general two-body
contact, self-contact or curved-trajectory certification.

For parallel factor updates and matrix products, compare every result bitwise
with independent serial arithmetic at 1/2/4/8/16/32 workers, including sparse,
all-fixed and changed clamp masks. Join workers after injected exceptions; check
failed pivot recovery and diagnostic reset. Warm kernels must not allocate per
contact/dispatch. Separate runtime startup allocations from steady work and keep
worker scratch bounded. Use paired isolated timings; interrupted seasonal or
fixture runs are correctness evidence only.

The 755-day Bergenia control reproduces archived request
`e9bd78f470cd4e36bc6b715c50319d44`, with deterministic scheduling, seed 42,
1-hour/4-hour ticks, ambient/CPU light and explicit mode 0/1. Keep the archived
numeric dimensions, 84-day growth time, crown probabilities and disabled clonal
spread fixed. Record whole leaves, individual intrinsic-area expansion thresholds,
actual exported frames and first open petals. Existing contact-enabled controls
currently fail developed-leaf/flowering acceptance; they are retained failure
regressions, not successful biological calibration. A separate diagnostic with
`SoftCollisions=false` keeps soil/solid constraints and isolates foliage contact.
Recovery there must not be used to justify disabling production foliage collision.
Managed/NativeAOT comparisons exclude wall-clock time and compare all daily
biological fields and selected exported scene bytes.
# CPU light and petiole limit regression additions

See [LIGHT_GROWTH_CALIBRATION.md](LIGHT_GROWTH_CALIBRATION.md) for the six-position-per-hour
CPU sky checks, single-crown open/shaded calibration and matched 25°/45° mode-0 trials.
Keep live-checkpoint v16 continuation byte-identical when irradiance offset buffers have
different spare capacities after resume. Scene playback versions are independent.
V1 remains three mechanical petiole spans. Wider bend limits are configurable but are
not a demonstrated cure for crown stalling; count developed whole leaves in acceptance.

## Bergenia scape attachment-sweep regression (v16)

Reproduce the archived September 2025, seed-42 one-crown CPU-light case at
1-hour and 4-hour ticks. At the first-season one-hour scape, the common-crown
petiole/meristem pair has a growing attachment neighbourhood. A whole-sweep
certificate using its starting radius (4.257 mm in the diagnostic checkpoint)
rejects the later increment even though its endpoint and sampled intermediate
poses are clear under the corresponding current-radius contact definition.
The narrowed check first tries that cheap whole-sweep certificate, then, only
for attached pairs it could not certify, tests 2/4/8/16 subintervals. Each uses
the minimum junction and organ radii over that subinterval; those radii
interpolate linearly, so the exclusion never exceeds the permitted neighbourhood
at any instant. Every interval must independently pass the existing continuous
vertex/face and edge/edge certificate. A genuinely crossing scape still fails
and restores its exact prior length and radius.

The exact one-hour diagnostic continuation reaches its 1.412 cm first-segment
target by day 242 and forms further floral organs, versus a 0.660 cm stall with
the previous whole-sweep bound. This is a mechanism regression, not evidence of
open petals or botanical density. Fresh v16 seasonal and checkpoint comparisons
must be assessed separately from the version-header-adjusted diagnostic state.


## Bergenia post-contact and floral retry regression (v20)

The exact archived September 2025 CPU-light request, seed 42, is the biological
control; do not replace it with the historical January or ambient-light cases.
The v16 fresh 330-day control produced 10/12 living leaves at 1 h/4 h, but only
1/1 reached half intrinsic expansion, 1/0 reached 90%, and neither flowered.
The four-hour first-scape proposal was retained from 14 March to day 330, blocking
its vegetative owner. In the one-hour control, a supported floral meristem was
still unfinished at 571 h, beyond its 504 h per-organ maximum, and senesced
before producing a successor. These are distinct failures.

The post-fallback retry records only the dimensions actually sampled by growth.
In the four-hour day-240 retry fixture, a leaf's sampled blade length and radius
increase from 24.623205 to 24.839489 mm and 10.003411 to 10.091327 mm;
a petiole's sampled length grows from 13.712277 to 13.838585 mm. Both pass the
complete endpoint and swept certificate when replayed serially after fallback.
A third blade fails even at 25% of its increment and remains blocked. Ancestor depth and stable organ ID set a deterministic order even after
removal/reindexing; the per-plant proposal lists are reused each tick. A pure radius retry checks the changed
support alone only after its descendants have already been certified unchanged;
other trials retain complete subtree checks.

The header-adjusted day-240 fixture is a **mechanism check**, not a valid old
live-checkpoint migration. Through day 245, staged replay grows four tracked
blades at each tick size, including blades the prior fallback left fixed. At 4 h,
blocked organs fall from 16 to 7; at 1 h, from 7 to 1. Resources remain finite
and no unresolved contacts are accepted. The exact archived four-hour pending
scape is released near day 266 if its first basal segment still cannot be placed;
its owner resumes vegetative growth without consuming the proposed child's
resources. This deadline follows the existing phase duration and nominal bloom
lead, capped by senescence. [RHS describes 'Rotblum' as spring-flowering](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details),
but the precise cutoff is a provisional model rule, not a cultivar measurement.

Fresh version-20 CPU-light worlds saved at day 8 and continued through day 9
produce byte-identical state and exported frames after resume at both tick sizes,
with finite resources and zero unresolved contacts. The older day-240 mechanism
fixture also retains byte-identical continuation after forced removal/reindexing;
its resulting organ loss is not a botanical survival test. Version-16 live states
are rejected explicitly. Crossing, near-miss and common-support translation and
rotation CCD fixtures still pass.

These short fixtures do not establish the developed-leaf density, spring bloom,
full-season runtime or one-hour/four-hour agreement gates. Fresh 330-day runs and
mature version-20 checkpoint continuation must be assessed separately.

## Archived request defaults in managed and NativeAOT servers

Keep all public `SpeciesSettings` request properties settable. In the .NET 8/10
source generator used by this checkout, an init-only property is placed in a
generated object initializer even when absent from JSON, replacing its declared
default with `default(T)` ([runtime issue 84484](https://github.com/dotnet/runtime/issues/84484)).
The actual archived Bergenia request omits `LeafExpansionRateScale`; the HTTP
path previously supplied zero and failed validation, while reflection-based
console probes correctly supplied one. The same issue affected other omitted
material, boolean and biological settings. Field names and numerical defaults
are unchanged; explicit zero must never be treated as an omitted value.

Regression: compare reflection and source-generated deserialization for every
public writable species property under .NET 8 and .NET 10. Cover `{}`, explicit
nondefault numbers, false and zero, case-insensitive names, and all eight species
in the archived request. Each target passes 1,320 property comparisons; an
explicit zero expansion scale still fails validation. Exercise the actual HTTP
simulation and scene export at 1 h/4 h after a NativeAOT publish, not just the
managed console harness. This request-construction fix does not change the live
state layout or checkpoint version. Runtime settings must still be resolved and
initialized before simulation; mutating them during a run is unsupported.

The corrected native HTTP endpoint completes ten days of the archived CPU-light
request at both tick sizes. At 4 h, its V8 scene is byte-identical to managed
.NET 10. At 1 h, geometry/profile assets, topology, colors and reported volume
agree, but transform coefficients differ by up to 2.116e-6 and analytics by
0.001953125. Both scenes decode completely. This is a successful request/export
compatibility check, not cross-runtime bitwise determinism or seasonal acceptance.
The installed server and original run store were not replaced.

The fresh four-hour day-240 v20 state resumes for a further day with identical
serialized state and exported frames, including a midway save/restore, finite
resources and zero unresolved contacts throughout the checked continuation.

A separate 100-day Carex seed-42 ambient-light check (1 m cube, 0.1 m cells)
fails the historical no-downward-organs expectation in both the preserved v16
simulator and v20. At 1 h/4 h, v16 has 30/29 downward organs; v20 has 35/23.
Neither has inverted organs, nonfinite resources or final unresolved contacts.
These are different growth trajectories, so the count alone cannot assign a
mechanism. Retain this as an unresolved generic regression gate rather than
claiming that all species behavior has passed.

### Canonical retry poses and bounded candidate ordering

NativeAOT exposed three uninitialized alignment bytes after `OrganPose.Rounded`
in the raw growth-retry checkpoint writer. One repeated fixture differed only
at those three bytes; exported geometry and physical fields were identical.
`WriteValue<OrganPose>` now writes the existing 60-byte layout explicitly,
zeroing offsets 41–43. Old v20 poses remain readable because these bytes carry
no state. This is canonical serialization, not a new state layout or version.
A poisoned-padding fixture checks both boolean values and four byte patterns,
preserving every actual field and the 60-byte roundtrip (16 checks).

The last-blocker-first optimization is retained after this diagnosis. It stores
one bounded ordering hint per collision world, checks stable organ IDs, and
reorders only candidates already found by the normal broad phase. Every tested
pair still receives the complete endpoint/swept certificate; the hint never
substitutes for a clearance result. Restore and plant-membership changes clear
it. No per-leaf storage is added.

With canonical serialization, all twelve paired NativeAOT runs (three baseline
and three optimized repetitions at each tick size) produce identical final
checkpoint bytes and exported scenes. Median foliage time improves from
62.52 to 60.43 ms at 1 h and 345.96 to 338.51 ms at 4 h, approximately 3.3% and
2.2%. These are matched organ populations with evolving geometry over one day,
not the full seasonal or 790-plant performance gate. The earlier logs named
`rejected-ordering-native-*` record the initial investigation before padding was
diagnosed; the final measurements are `canonical-*` in
`canopy-validation/bergenia-contact-v20/`.

The final combined production source passes NativeAOT day-240 continuation at
both 1 h and 4 h using fresh native checkpoints: immediate state/frame roundtrip
and another half-day after resume are byte-identical. Repeating after leaf
removal/reindexing passes at both tick sizes. All four continuations have finite
resources and zero unresolved contacts at every checked tick. The ordering hint
is rebuilt after restore rather than serialized. Logs are `final-native-*.log`
in the same evidence directory. The prescribed core build and final NativeAOT
server publish succeed with existing warnings; the installed server is unchanged.

The separate restore-first repair experiment was not adopted: it added work at
1 h and changed the four-hour continuation without improving organ sizes. A
midpoint penetration shortcut had no measured benefit.

## Changing thickness and final floral recovery (v21)

Continuous foliage and attachment queries previously compared a midpoint
distance with the **maximum endpoint radii** when deciding that penetration had
occurred. This is a valid conservative separation bound, but not the actual
midpoint thickness. Two parallel capsules maintaining a 0.2 mm gap while one
grows from a 1 mm to a 3 mm radius and retreats were falsely rejected, including
two- and four-part sweeps. The reversed thinning/approaching path failed too.
Arrange the separation diagonally in XZ so their AABBs overlap; an axis-aligned
fixture can be pruned before reaching the faulty test.

The midpoint penetration test now uses interpolated midpoint radii. Separation
proofs still bound the complete path: a fixed plane subtracts each endpoint's
own radii before taking the minimum clearance, and the existing arc bounds
cover nonlinear rotation, bending and size changes. Linear interpolation of
thickness makes this proof conservative. Maximum radii remain in the broader
distance/motion bounds. Budget exhaustion still rejects an uncertified path.
This follows the finite-thickness and continuous-path distinction used by
[C-IPC](https://ipc-sim.github.io/C-IPC/); it does not implement its optimization
solver or establish published CCD-benchmark acceptance.

The last fallback now also retains sampled `FlowerMeristem`, `FlowerStem` and
`FlowerPedicel` size proposals. Floral length and radius can be retried
independently, using at most eight bisection steps per dimension from the
accepted size. Every retained trial requires the existing endpoint and full
subtree swept certificate, including the same bounded scape subinterval policy
as the main pass. No sampled increment is exceeded, and pending developmental
owners remain excluded. A failed search restores the accepted dimensions.
This uses existing reusable worker lists and adds no per-leaf state or new
checkpoint fields. Version 21 nevertheless rejects v20 live continuation
because the geometry acceptance behavior has changed.

Required deterministic fixtures:

| Fixture | Acceptance |
| --- | --- |
| Thickening while retreating, constant 0.2 mm gap | Direct, 2-, 4- and 16-part sweeps certify. |
| Thinning while approaching with the same gap | All four sweep partitions certify. |
| Thickening into actual overlap | Every partition rejects. |
| Attachment-clipped thickening | Clear path certifies; the actual collision rejects. |
| Crossing under common support translation/rotation | Relative crossing rejects; rigid shared motion certifies. |
| Analytic parallel capsule paths with changing radii | No unsafe acceptance against the exact quadratic minimum. |
| Floral recovery after the final coupled fallback | Retain only a certified sampled full/partial increment; no newly penetrating endpoint or sweep. |
| Native v21 continuation, both tick sizes | Immediate state/frame roundtrip and continued state/frames are byte-identical, including leaf removal/reindexing; reject v20 live state. |

The targeted fixture suite passes, including 500 seed-42 analytic capsule
paths: 366 certify, 134 reject actual obstruction, and there are zero unsafe
acceptances or conservative rejections in that set. This is not coverage of all
curved-shell or published adversarial cases.

All six NativeAOT checkpoint fixtures pass at 1 h/4 h: fresh early growth,
mature state, and mature state after leaf removal/reindexing. Each checks finite
resources and zero reported unresolved foliage contacts on every continued
tick. The mature 1 h fixture uses a controlled same-layout historical state;
the mature 4 h fixture uses a fresh v21 seasonal state. The former is engineering
evidence, not a supported live-state migration or fresh seasonal acceptance.
The core build and isolated NativeAOT server publish pass with existing warnings.

Serial matched-starting-state native measurements use three warmups and fifteen
one-tick repetitions, excluding snapshot restoration from timing. Only the two
CCD thickness files differ between benchmark builds; recovery logic is the
same. The 1 h fixture has 52 organs and the 4 h fixture 41, unchanged during
each measured tick. Median total/foliage times are 371.13/368.91 ms before and
377.99/375.25 ms after at 1 h, and 88.79/85.49 ms before and 85.00/82.62 ms after
at 4 h. Thus this fix adds about 1.8% total time in the first fixture and removes
about 4.3% in the second. Frame hashes agree between builds for these individual
steps. Existing whole-tick allocations remain about 1.01 MB/0.86 MB, respectively;
these measurements do not attribute allocations to contacts or pass the zero
per-contact allocation, settled-scene, multicore or 790-plant gates. Concurrent
seasonal runs also limit timing precision.

Evidence is retained in `canopy-validation/bergenia-contact-v21/`. Seasonal
flowering and developed-leaf density remain separate biological gates; see
`BERGENIA_CALIBRATION.md`. In the one-hour controlled state, the accepted scape
certifies clear, while a 1 micrometre extension exposes a petiole capsule overlap
outside the attachment exclusion. Enlarging that exclusion or dropping the
collision would not demonstrate a mechanically feasible emergence path. The
four-hour controlled state also retains a sampled successor transaction, so
its owner is excluded from independent replay. Investigate junction geometry,
coupled neighbour response and retained-parent continuation explicitly.

## Attachment containment and scape-driven neighbour yield (v22)

`Parts` clips rod centre lines and blade surfaces against the attachment box;
collision thickness is applied when comparing the surviving parts. The swept
empty-outside proof previously tested a thickness-expanded box. A short, thick
scape could have no outside parts throughout a neighbour's bend, yet fail that
proof. Erosion and subdivision then exposed artificial fragments near the seam
and exhausted the CCD budget. This is numerical rejection, not proof of physical
confinement.

`AttachmentContained` uses a temporary zero-offset shape for **this containment
proof only**. Intrinsic blade width, authored relief, curved rod nodes and the
rotational/deformation arc bound remain included. All normal distance and swept
checks keep their original collision thickness. Attachment size, tissue moduli,
angular limits, growth parameters and the eight-round rigid solver are unchanged.
There is no additional persistent state or per-query allocation in this change.

Feature enumeration uses that same containment proof before querying the other
blade. Surviving features retain a conservative bound padded by the maximum
endpoint collision offset. A swept separating-plane test uses each facet's
actual curved-surface normal and both faces, with endpoint thickness and the
existing nonlinear arc bounds. It reuses projections and rejects unusable planes
before transforming their end geometry. These are sufficient separation proofs;
they do not treat iteration-budget exhaustion as physical confinement.

The deterministic regression cases are:

| Fixture | Required result |
| --- | --- |
| Fully clipped thick scape with a yielding sibling; both pair orders | Certify the sweep. |
| Scape grows while its centre line stays inside the existing box | Certify the sweep. |
| Curved petiole bends internally or releases inside that box | Certify the sweep. |
| Tiny curved Bergenia blade, including basal lobes, inside the box | Certify the sweep. |
| Scape emerges through a still-obstructing sibling | Reject. |
| Both centre-line endpoints contained, but rotation exits and crosses outside the box between them | Reject; endpoint containment alone is insufficient. |
| Existing relative sibling/common-parent translation and rotation crossings | Continue rejecting real crossings and accepting shared transport. |
| Seed-42 analytic parallel-capsule suite | 366 certified separations, 134 rejected intersections, zero unsafe acceptances in 500 cases. |
| Tilted curved facets sliding on either face | Certify; reject a crossing between separated endpoints. |
| Seed-43 parallel tilted facets with changing thickness and normal translation | 151 certified separations, 348 rejected intersections, zero unsafe acceptances in 499 cases; one tolerance-boundary case omitted. |

The growth-driven fixture removes other foliage from the previously diagnosed
one-hour crown, retains the scape, neighbouring petiole, its attached blade and
their original support chain, and freezes biology after compaction. A 10 micrometre
scape increment is then submitted to the **production** `ResolveVegetation`
method. With v21 it retains only about 0.782 micrometres and essentially no neighbour
bend. With v22 it retains all 10 micrometres and about 15 degrees of petiole bend,
using four correction rounds; independent subtree clearance checks pass and
unresolved contacts are zero. Identical frozen geometry gives the same result
with the mechanical tick setting at 1 h and 4 h. This small feasible-contact
fixture proves recovery of a numerical rejection, not seasonal flowering or a
developed-leaf-density improvement. It is a controlled same-layout diagnostic,
not a supported migration of an old live checkpoint.

The complete crown still rejects larger motions against additional foliage.
Repeating the full-crown diagnostic also exposes conservative clearance failures
after replay in both the v21 baseline and v22; a zero unresolved-contact counter
alone therefore does not establish an independent motion certificate for that
artificial replay. Do not enlarge the attachment exclusion or accept uncertified
motion to make that fixture pass. Further evasion work needs coordinated contacts
and, if using deformation-first continuation, certificates for each motion stage.

Native v22 state/frame roundtrip and continuation checks pass at both tick sizes,
including removal/reindexing and reconstructed caches. V21 live checkpoints are
explicitly rejected. The scene codecs and recorded geometry assets are unchanged.
The core build and an isolated NativeAOT server publish pass with existing
warnings. Evidence is in `canopy-validation/bergenia-contact-v22/`; temporary
diagnostic source and large checkpoints remain under `/tmp`.

The final serial NativeAOT benchmark restores identical starting states before
each tick, excludes restore time, and uses three warmups and fifteen repetitions.
Only this attachment file and the checkpoint version differ from the rebuilt v21
baseline. Both fixtures keep their organ populations fixed during the measured
step; corrected certificates can produce different accepted geometry.

| Fixture | Total median / p95, v21 | Total median / p95, v22 | Median change |
| --- | --- | --- | --- |
| 1 h, 52 organs | 258.46 / 269.27 ms | 344.66 / 364.44 ms | +33.4% |
| 4 h, 41 organs | 78.38 / 89.14 ms | 84.18 / 90.04 ms | +7.4% |

In the one-hour case, the median sweep-interval count rises from 57,990 to 90,641,
and triangle tests from 470,396 to 673,075: removing a false rejection opens more
trials before full-crown rollback. Conservative pruning reduces redundant work,
but the **25% performance gate still fails** for this case. Whole-tick allocations
are approximately 0.99 / 1.00 MB before / after at 1 h and 0.864 / 0.865 MB at 4 h.
Those totals include biology and lighting; they do not establish zero steady-state
per-contact allocations. There is no added per-leaf storage. The one-hour scene
hash changes; the four-hour hash agrees. Larger stands, multicore scaling and the
790-plant panel remain unvalidated for this correction. The scoped playback
checks pass (five suites, 34 tests); no scene codec or frontend source was changed.

## Consistent growth-retry certificates (v23)

The full-crown replay above exposed an ordering mismatch rather than a new
physical collision. Its failing pair was leaf stable ID 24 against leaf stable
ID 19, sharing support index 16. Both endpoints were clear. The same relative
sweep returned false in 24/19 order and true in the main solver's 19/24 order.
Bounded feature traversal can exhaust its conservative CCD budget in one order
while proving separation in the other. A failed certificate is not evidence of
confinement, and querying an already accepted path differently can stall retries.

`GrowthTrialClear` now uses the main solver's canonical order for the current
distance, previous distance and relative sweep. Same-plant order uses stable
organ IDs, not compacted array indices; cross-plant order uses the active-plant
indices used to build `Neighbours`. The remembered failure still identifies the
original affected organ and neighbour. Both bodies and the full path remain in
every certificate. It does not accept whichever order happens to pass, increase
the work budget, enlarge attachment exclusions or reduce collision thickness.
This adds no persistent state, worker cache or per-contact allocation.

The complete frozen-crown replay now passes independent `GrowthTrialClear`
queries for every organ, including after removal/reindexing of an unrelated
leaf. It still retains only about 0.82 micrometres of the proposed 10 micrometre
scape increment. In a diagnostic deformation-first search, the neighbouring
petiole can yield in certified steps until another foliage constraint blocks
further movement. The isolated scape fixture retains all 10 micrometres and
about 15 degrees of petiole bend at both mechanical tick settings. Removing this
certificate mismatch does not establish a globally feasible crown equilibrium.

Required regression cases now include:

| Fixture | Acceptance |
| --- | --- |
| Full-crown relative leaf sweep accepted by the main pass | Independent retry queries from either affected branch use the same certificate; no reverse-order false obstruction. |
| Remove an unrelated leaf before the same replay | Stable-ID order survives compaction; all independent subtree queries pass. |
| Current/previous endpoint distance and swept queries | Use one canonical order throughout, retaining actual crossings and finite thickness. |
| Two independent contacting pairs of plants | Queries from both plants agree; configured worker limits of one and four yield identical accepted states and exports. |
| Native v23 contact continuation at both tick sizes | Immediate and continued state/frame equality, including removal/reindexing and rebuilt caches; reject v22 live checkpoints. |

The final uncached 12-day four-plant ambient-light fixtures have exactly two
neighbouring plant pairs and 81 final organ clearance checks at each tick size.
They record 413 / 117 contact corrections at 1 h / 4 h. Configured worker limits
of one and four give byte-identical states and exports, finite resources and
zero unresolved contacts. A longer 30-day four-hour test of the subsequently
removed feature-bound cache also agrees between those worker limits, with 2,338
corrections and 179 final checks. These are scheduling regressions, not multicore
speed measurements. Ambient light isolates scheduling; it is not biological
calibration. Native CPU-light seasonal continuation results are recorded in
`BERGENIA_CALIBRATION.md`. Playback checks pass in five suites (34 tests).

A worker-local cache for repeated swept feature bounds was also tested. Its
controlled NativeAOT continuations were byte-identical to the uncached v23 solver,
but initial timing results did not show a consistent benefit, particularly at
four-hour ticks. The experiment was removed. Its measurements are retained as
an experiment, not evidence of an accepted speed improvement. Final timing and
build evidence is in `canopy-validation/bergenia-contact-v23/`; large checkpoints
and temporary diagnostic harnesses remain under `/tmp`.

The final uncached NativeAOT benchmark runs four independent processes per
version/tick size, each with three warmups and fifteen measured single ticks.
Every measurement restores the same state first and excludes restore time; all
timing processes run serially without other simulations or builds. The following
are medians of process medians and medians of process p95 values, respectively:

| Fixed-population fixture | Total median / p95, v22 | Total median / p95, v23 | Median change |
| --- | --- | --- | --- |
| 1 h, 52 organs | 344.20 / 364.02 ms | 334.16 / 370.87 ms | -2.9% |
| 4 h, 41 organs | 82.60 / 96.11 ms | 85.75 / 105.67 ms | +3.8% |

Process-level timing ranges overlap. Bounded memo placement changes repeated
query cost between processes, while every measured scene hash agrees. This is
a correctness repair, not a demonstrated substantial speed improvement. The
failed v22 one-hour performance gate against v21 is not cleared by these modest
results. Whole-tick allocations remain about 1.00 MB / 0.86 MB at 1 h / 4 h;
those include physiology and lighting, not just contacts. No per-leaf storage
was added, and larger-stand/790-plant performance gates remain outstanding.

### Retained triangle-distance pruning

After the existing face-normal lower bounds, `TriangleDistance` tests each
triangle's three edge planes. Nearly coplanar facets can overlap on world axes
and face normals while remaining separated within the blade plane. An edge
plane can certify that their distance exceeds the requested search ceiling,
avoiding six vertex/face and nine edge/edge distance calculations. The same
outward search padding applies. This adds no cache, allocation or per-organ
state, and does not alter contact thickness, attachment exclusions or CCD budgets.

The deterministic seed-44 regression compares 10,000 finite-search queries with
the exhaustive calculation obtained by disabling distance pruning with an
infinite search. It includes tiny, near-coplanar and slender triangles: zero
mismatches at the fixture's scale-relative numerical tolerance. Retain this
scenario alongside the seed-42 capsule and seed-43 tilted-facet CCD batches;
both batches report zero unsafe acceptances and zero conservative rejections.
Actual interior crossings must still fail, including growing thickness and
attachment-neighbourhood escape. This comparison validates distance pruning,
not a general formal floating-point collision guarantee.

Three new independent NativeAOT processes per variant and tick size, each with
three warmups and fifteen restored single ticks, compare the order-only v23
implementation with the retained pruning. Timings exclude restore; all timing
processes run serially with other builds and simulations idle.

| Fixed population | Order-only median / p95 | With pruning median / p95 | Median change |
| --- | --- | --- | --- |
| 1 h, 52 organs | 336.30 / 350.57 ms | 313.09 / 324.16 ms | -6.9% |
| 4 h, 41 organs | 85.00 / 110.51 ms | 82.40 / 95.98 ms | -3.1% |

Values are medians of process medians and process p95 values. Accepted export
hashes agree between the variants. Complete 14-day CPU-light continuation states
also agree byte-for-byte at both tick sizes. Fresh/mature NativeAOT state/frame
roundtrip and continuation checks pass, including removal/reindexing and rebuilt
caches. The final four-plant 12-day scheduling checks agree byte-for-byte with
worker limits of one and four; these are not multicore speed measurements.

Deeper CCD experiments were removed. Charging only uncertain subdivisions,
adding facet-plane sweep certificates and traversing paired swept BVHs allowed
the full frozen-crown increment and an additional half-expanded leaf in one
four-hour continuation. However, the difficult one-hour fixed-population case
initially took about twice as long; successive pruning still left substantial
overhead. Even the bounded paired-traversal variant was about 25–30% slower
than order-only v23. None recovered flowering in the tested seasonal intervals.
These private experiments used checkpoint label v24 but were not released;
the retained live checkpoint version is v23. Their partial biological gains do
not establish acceptance, and sampled clear paths do not replace continuous
certificates. The retained solver keeps its original bounded CCD work allowance.

Final evidence and explicitly rejected experiment logs are in
`canopy-validation/bergenia-contact-v23/`. Neither V2 default enablement nor the
dense-stand/790-plant performance gates follow from these small fixtures.

A final interleaved serial comparison against the verified v21 native baseline
uses three independent processes per version/tick, the same populations and
starting snapshots, and the same warmup/restore-exclusion rules. Corrected
certificates can change the accepted geometry, so matching populations do not
imply matching poses between v21 and v23.

| Fixed population | v21 median / p95 | Final v23 median / p95 | Median overhead |
| --- | --- | --- | --- |
| 1 h, 52 organs | 265.09 / 287.57 ms | 303.24 / 310.25 ms | +14.4% |
| 4 h, 41 organs | 82.24 / 96.77 ms | 83.53 / 90.63 ms | +1.6% |

The cumulative 25% median-overhead gate now passes for these two scoped fixtures.
This supersedes the earlier failing v22 timing result for those fixtures only;
it does not establish the dense-stand/panel gate, zero per-contact allocations,
multicore scaling or biological acceptance. Process-level timing variation and
raw rows are retained in `performance-release.json` and its referenced logs.

## Classified motion certificates (v24)

`FoliageGeometry` now returns `Clear`, `Intersecting` or `Uncertified` internally.
The last result includes work/depth exhaustion and insufficient separation proof;
it does not establish physical confinement. Both non-clear outcomes still reject
the proposed path. The bounded motion cache stores the outcome, so cache hits
preserve retry decisions. Legacy boolean callers accept only `Clear`.

Confirmed intersections skip redundant temporal retries when the attachment
exemption is fixed. Growing attachment regions retain their conservative retries:
intersection outside a whole-sweep minimum exclusion need not remain outside the
larger justified region later in the interval. Nonjunction floral retries use four
certified subintervals regardless of `HoursPerTick`.

The existing 256-interval narrow-phase budget remains. After it is exhausted,
disjoint swept bounds or complete finite-thickness facet-plane separation may
still certify a candidate, including the nonlinear arc bound. Other candidates
remain uncertified; no additional distance recursion or sampled-path acceptance
is allowed. There is no new per-organ state or per-contact allocation in this
code path. Whole-tick allocation measurements do not establish the separate
zero-allocation release gate.

Add these regressions to further contact changes:

| Fixture | Required result |
| --- | --- |
| Interior crossing, normal versus exhausted query budget | `Intersecting` versus `Uncertified`; neither accepts motion. Warm cached classification agrees. |
| Separated tilted finite-thickness facets, zero distance-query budget | A complete separating-plane certificate remains admissible; a crossing between separated endpoints does not. |
| Identical frozen crown at 1 h and 4 h mechanical settings | Canonical accepted organ poses agree, independent of biological tick duration. |
| Earlier full crown, 10 micrometre scape proposal | About 0.82 micrometres retained; all independent certificates pass. This unresolved case must not be reported as full recovery. |
| Later full crown, 10 micrometre versus nominal two-axis proposal | The small proposal succeeds; the approximately 913/275 micrometre length/radius proposal still stalls. Both v23 and v24 show this distinction. |
| Native v24 continuation, fresh and contacting/reindexed crowns | Immediate and continued state and exported-frame equality, including rebuilt caches; reject v23 live checkpoints. |

The 28-day CPU-light comparison does not recover flowering or developed-leaf
density at either tick size. Recorded playback and wire formats are unchanged;
live checkpoints advance to v24 because continuation acceptance can differ.
Evidence, matched baseline fixtures and scoped performance measurements are in
[`bergenia-contact-v24`](../canopy-validation/bergenia-contact-v24/README.md).

## Coupled continuation of sampled floral growth (v25)

Capture physiological dimension proposals before either the hard or foliage
constraint pass. Previously, the first hard rollback could erase a proposal before
the foliage solver recorded it. After the usual validated fallback and stationary
retries, existing floral axes can now retry their remaining sampled extension and
thickening through separate, bounded continuation paths. This does not create
another biological opportunity or commit pending births.

Each trial starts from the last certified waypoint. Endpoint foliage contact
distributes a correction between both contacting bodies using the existing
compliance and leverage calculation, with relative derivatives at common ancestors.
Changed descendants and cross-plant neighbours receive their own endpoint and
continuous-motion certificates. Static obstruction and a failed separation proof
without endpoint penetration supply no invented reaction. Failed trials restore
all participating frames exactly; accepted trials wake affected growth retries.
Unchanged, unjoined neighbours are held at their already-certified current poses;
their earlier tick motion is not replayed or used to invent an old-penetration
exemption in the new continuation interval.
Contact history is looked up in the main pass's canonical plant/stable-ID order;
reversed trials reverse the remembered normal, preserving the contact side at
coincident witnesses.

The limit is sixteen adaptive trials per dimension and eight contact corrections
per trial. Whole-joint corrections have a local 0.1-degree step cap, independent of
biological tick duration; cumulative joint limits and V1 shape safeguards remain
unchanged. Budget exhaustion leaves an uncertified remainder. This is bounded
geometric recovery using the existing mechanics, not a converged shell/contact
energy minimization or an implementation of C-IPC.

Scratch is allocated lazily for participating formations and reused: one 116-byte
`GeometryState` per capacity slot, plus a reusable participant list. The measured
52-organ formation uses 6,032 bytes of new geometry scratch in total. Capacity can
grow with the existing geometric allocation policy; this is per-organ scratch,
not a new mesh or contact table hidden in a global cache. Ordinary species with no
coupled floral retry allocate no continuation geometry array.

Add these fixtures to later changes:

| Fixture | Required result |
| --- | --- |
| Two anchored plants, one scape growing into the other's blade | Retain about 17.97 mm of a 23 mm proposal, with petiole yield, unchanged organ counts and complete certificates; identical frozen poses at 1 h and 4 h. |
| Same fixture with a solid ceiling at the existing cylinder-contact tolerance | Zero accepted waypoints and exact restoration of both plants; the ceiling supplies no foliage reaction. |
| Two plants after certified earlier support/scape extension | Retain the remaining 5 mm extension with the raised neighbour stationary; no contact correction or replay of its earlier motion. |
| Complete day-303 crown, manually imposed nominal scape dimensions | About 71.35 micrometres of extension retained, no radial gain, all certificates pass; matching frozen poses at both tick settings. This scape is already senescing, so this is a geometry fixture only. |
| Native v25 continuation through removal/reindexing and cache rebuilding | Byte-identical continued state and actual exported frames; reject v24 live state explicitly. |

Live checkpoints advance to v25 because acceptance and retry scheduling changed;
their payload layout is unchanged. Recorded playback and wire formats are
unchanged. Fresh seasonal results and performance limits are recorded in
[`bergenia-contact-v25`](../canopy-validation/bergenia-contact-v25/README.md).

## Multiple continuation witnesses (v26)

Floral continuation now retains a bounded set of endpoint witnesses and applies
a unilateral tangent response through both bodies' existing compliance. Common
ancestors use relative gradients. Cleared witnesses retain usable conservative
clearance; they must not become pinned constraints because a proximity query
returned its capped search distance. Canonically ordered penetrating witnesses
are reused without changing feature tie order. Full endpoint and swept-motion
certificates remain mandatory, and exhausted budgets do not prove confinement.

Add these regression cases to subsequent contact changes:

| Fixture | Required result |
| --- | --- |
| Scape thickening between two neighbouring plants | Both leaves yield; retain approximately 4.022 mm radial growth with rigid blades and 6.216 mm with V1, with all certificates passing. |
| Two contacting leaves sharing an unanchored crown support | Both yield, the common support stays fixed, and the tangent forces agree with the independent two-row active-set solve. |
| Previously cleared witness while another body yields | Retain a nonpositive penetration allowance from a sufficiently wide proximity query; certify the complete resulting motion. |
| Ceiling and two-wall confinement | No accepted waypoint; restore all poses exactly. |
| Frozen accepted fixtures at 1 h and 4 h | Identical canonical poses within each runtime; retain support, removal/reindexing and cache-rebuilding checkpoint checks. |
| Full day-240 crown, manual 1,000/250 micrometre proposal | Currently no certified increment in either version; retain the numerical-failure classification rather than treating this as physical confinement. |
| CPU-light biological comparison | One-hour flowering gain and corrected leaf targets must be checked separately from the still-stalled four-hour case. Count dimensions against the species target plus sampled variation, not the variation alone. |

Checkpoint v26 rejects older live states; its payload and saved playback formats
are unchanged. The shared-support retry is slower, the end-to-end performance
gate is not established, and biological/default-enablement acceptance remains incomplete. Evidence,
the probe reporting correction and bounded scratch accounting are in
[`bergenia-contact-v26`](../canopy-validation/bergenia-contact-v26/README.md).

## v27: query hints and staged V1 evasion

See [`bergenia-contact-v27`](../canopy-validation/bergenia-contact-v27/README.md)
for source-scoped evidence. Retain the two-neighbour radial, common-support,
single-witness, ceiling and confining-wall fixtures at 1 h and 4 h. V1 must
certify both evasion and growth legs, retain its intrinsic dimensions and strain
limits, and restore staged bends on a failed development transaction. Optional
clearance guards can leave an invalid local tangent, never the final certificate.
Check a remembered foliage failure followed by a solid failure: it must not
produce a phantom reaction. Compare exported frames and checkpoint bytes through
V1 leaf removal/reindexing and cache rebuilding. v27 rejects older live checkpoints
while retaining playback. The four-hour seasonal crown and richer V1 performance
gate remain unresolved; keep Bergenia's default mode at 0.

## v28: coupled angular limits and bounded query retention

Experimental mode-1 floral continuation now uses one reciprocal angular
compliance tensor per shared joint. A saturated joint's angular inequality is
solved against the aggregate contact load: it can still yield sideways or move
inward. Removing its response independently for each contact direction produced
an asymmetric tangent matrix. The rigid mode-0 response and its work budget remain
unchanged. A scaled direct working-set fallback handles small ill-conditioned
mode-1 contact systems; rank deficiency and exhausted budgets remain numerical
failures, subject to the existing full motion certificates.

Retain these additional regressions:

| Fixture | Required result |
| --- | --- |
| Two contact loads on a joint at its angular limit | Reciprocal tangent, no outward excess, retained lateral response; forces agree with an independent constrained solve. |
| Aggregate load reverses inward | Angular reaction releases and the joint moves inward. |
| Angular limit removes the only admissible response | Numerical rejection; no enormous force inferred from cancellation noise. |
| Nearly parallel contact rows, off-diagonal correlation 0.999999 | Direct fallback reaches the analytic nonnegative force solution and <=1e-8 m residual. |
| Four-way query memo, same bounded entry counts | Same certified poses as the previous memo, matching frozen 1 h/4 h poses and byte-identical checkpoint continuation through release/removal/cache rebuilding. |
| Four plants in two independent neighbouring pairs, twelve ambient days | At 1 h/4 h, configured worker limits of one and four give identical accepted states/exports, finite resources and zero unresolved contacts; all final organ clearance queries pass. This does not measure multicore scaling. |

The distance/motion memo retains 4096/2048 entries per initialized worker with
four-way LRU sets. Keys and cached collision results are unchanged. This reduces
repeated expensive queries in the measured common-support fixture without
increasing entry storage. It does not certify zero allocations or full-scene
performance. No new accepted per-leaf fields were added.

Live checkpoint v28 rejects older live states because the experimental acceptance
path changed; payload and saved playback formats are unchanged. The four-hour
seasonal scape still stalls, and V1 remains experimental. Mechanical evidence,
storage accounting and matched timings are recorded in
[`bergenia-contact-v28`](../canopy-validation/bergenia-contact-v28/README.md).

## v30: continuous tiny-motion admission

Distance-based interval certificates now reserve the complete combined motion
bound against the existing finite-thickness tolerance. Root and midpoint tests
use the same rule. Attachment exclusions remain eroded by material motion and
attachment-centre movement. A small motion or a clear midpoint alone no longer
authorizes the whole path: bounded subdivision continues, retaining distinct
intersection and numerical-uncertainty outcomes. The original corrective gap,
work budgets, species coefficients and default mode are unchanged.

| Regression | Required result |
| --- | --- |
| Point proxies with 2–4 µm motion, initially 11 µm inside the thickness offset; midpoint enters the 10 µm tolerance | Refuse the full interval, including attachment-aware queries and the root tiny-motion case. A clear midpoint cannot erase the invalid start. |
| Persistent blade slide at −9 µm proxy clearance | Accept only through an interval/plane proof under the unchanged tolerance. |
| Edge/wall crossing, widening, rotation arc and basal-lobe crossing | Refuse the crossing even when endpoint or midpoint checks alone could miss it. |
| Tiny rest-curved and folded blades | Preserve width-dependent relief; sampled material-point travel must remain inside the trajectory bound. Sampling is not a formal CCD proof. |
| Official Tight Inclusion rational sample subset | Refuse every represented collision-positive query. Record float conversion, finite offset, excluded trajectories and negative-query outcomes separately. |
| Frozen contact/angular fixtures at 1 h and 4 h | Preserve certified growth, reciprocal response, inward release and exact restoration under impossible confinement. |
| Four plants, independent contact pairs, one/four workers | Byte-identical state and exported frames within each tick size; finite resources and zero unresolved accepted contacts. |
| Fresh and native-origin mature checkpoint, with removal/reindexing | Byte-identical immediate/continued state and exported frames after cache rebuilding; reject older live states. |
| Native v30, archived seed-42 request at day 240, attached petioles 5/28 | Preserve the clear starting state and exact rollback. Audit recursive decisions within a nanometre of the 10 µm boundary against a double-precision clipped-segment/interpolation reference before declaring physical confinement. The independent precision reference remains pending. |

Live checkpoint **30** rejects older live checkpoints because these certification
decisions affect continuation. Its payload layout and V7/V8/V9 saved playback
are unchanged. Probe-only header promotion for historical controls is not a
production compatibility feature. Results and the midterm research review are
in [`bergenia-contact-v30`](../canopy-validation/bergenia-contact-v30/README.md).

## v31: segment measurement and exact interpolation frames

Segment parameters and their closest-point residual are evaluated with relative
double arithmetic. Round returned world witnesses separately; measuring their
rounded separation can change translated contact decisions. Scalar locals avoid
NativeAOT overhead from value-return vector helpers. Endpoint interpolation copies
the original pose at zero/one, including `Rounded`; stationary base, rotation and
deformation fields remain unchanged. Skin, work limits and botanical settings stay
fixed. Live checkpoint **31** rejects older live states; playback formats and the
checkpoint payload layout are unchanged.

| Regression | Required result |
| --- | --- |
| Skew, crossing and point-degenerate segments, at the origin and translated by metres | Compare squared distance with an independent double constrained-quadratic minimum. Account for the float return ABI and near-zero roundoff; witnesses need not reproduce that unrounded distance when subtracted as floats. |
| Pose interpolation at zero/one; stationary fields while length changes | Preserve all endpoint fields exactly, including rounding classification. No drift or repeated quaternion normalization of a stationary frame. |
| v30 invalid-start, persistent-slide and swept-crossing cases | Retain full-interval decisions; no new tiny-motion or midpoint-only acceptance. |
| Native fresh/mature state, cache rebuilding, removal/reindexing and V1 fixtures | Immediate/continued checkpoint bytes and exported frames agree within each runtime; reject v30 live states explicitly. |
| Native historical day-240 attached petioles 5/28 | Distinguish rounded endpoints, double transforms and normalization. Reference differences alone do not certify a continuous trajectory or authorize greater overlap. Preserve the clear start and exact rollback if the requested increment cannot be certified. |
| Seed 42, CPU light, fixed dimensions/durations/probabilities, no clonal spread | Record whole leaves, both-dimensional 50%/90% counts, flowering, finite resources and unresolved contacts at both tick sizes. Increased microscopic counts do not pass density acceptance. |

The historical scape remains blocked after this increment. Failed clipped-hull and
alternative close-quaternion experiments are not enabled. Evidence and the precision
reference are in [`bergenia-contact-v31`](../canopy-validation/bergenia-contact-v31/README.md).

## v32: attachment ranges and finite-thickness emergence

Attached rod pairs retain their original represented endpoints and double clipping
parameters until distance measurement. Attachment containment uses the same box
faces without world-float rounding. No organ state is added. Checkpoint version 32
rejects earlier live states; recorded playback and the payload layout are unchanged.

| Regression | Required result |
|---|---|
| Independent attachment clipping: ordinary/tiny/curved/translated rods, parallel and point segments | Match a separate plane-partition and constrained-distance reference; retain nonempty outside portions. |
| Coincident zero-length rods at `(100.000030517578125,100,100)`, collision radii 20 µm, attachment centre `(100,100,100)`, half-extent 30 µm | Reject the contact outside the mathematical exemption; rounded-out box faces must not certify it clear. |
| 1-hour/4-hour native fixtures, V1, common parent, ceiling and confined partial growth | Preserve nonpenetration, exact rejected rollback and byte-identical resumed state/export after removal/reindexing. |
| Thick scape crossing an attachment face by submicrometre increments | Contact must reflect continuously varying finite-thickness geometry. **Known failing gate:** centre-line clipping followed by full-radius expansion creates a capsule-cap contact jump. |

The last scenario is a physical geometry-model issue beyond the retained numerical
fix. Fresh CPU-light seasonal probes still fail biological acceptance; no default
folding enablement or botanical retuning is justified. Evidence and the next
mechanical target are in
[`bergenia-contact-v32`](../canopy-validation/bergenia-contact-v32/README.md).
