# Collision response and canopy recovery

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

Live checkpoints are **version 3**. They include stable IDs, sampled developmental
transactions, retry state, sparse contacts and light-filter history. Older live
checkpoints are rejected explicitly; existing saved playback remains readable.
V7 playback records contain no new collision or guidance payload. The old geometry
in a saved run is not repaired retroactively.

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
| Live checkpoint v3 | Byte-identical resumed exports through pending transactions, reindexing, contact release and guidance refresh. |
| Four-plant contact scheduling | Independent contacting pairs run concurrently; checkpoint replay remains identical across worker scheduling. |
| Existing playback/archive consumers | Existing scene records and archive random access still decode. |

The reference photos span 4 April, 11 May, 13 May, 8 June and 12 June 2026:
69 days of **established perennial spring growth**, not a seedling calibration.

The facade acceptance run uses September's complete OBJ and all 790 plants in
`fav11_seeds_P003.json`, through a year and the following April–June. Compare the
old build, corrected mechanics without guidance, and corrected mechanics with
guidance. Record monthly actual triangulated living leaf area, living crowns,
projected canopy coverage, paused tips, rejected births, mortality reasons,
runtime, allocations, peak memory, exported-frame size and saved-run compression.
Keep irrigation, nutrition and planting-size calibration outside this revision.

Build with `dotnet build Agro/Agro.csproj --no-restore -m:1 -v:minimal`.
Also build the server consumer (optionally `-p:SkipSpeciesGeneration=true` to avoid
rewriting generated species assets), run relevant playback checks, and run
`git diff --check`. Passing compilation alone is not acceptance of canopy recovery.
