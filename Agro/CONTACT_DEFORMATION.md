# Contact deformation: BroadLeafV1

Status: experimental, opt-in. **Bergenia is not enabled by default.** The dense
contact performance gate and the seasonal developed-leaf-density target have failed.
This is not a calibrated Bergenia tissue model.

Current CPU light/calibration changes use live-checkpoint v7; historical v6 results below
refer to their tested build. See [LIGHT_GROWTH_CALIBRATION.md](LIGHT_GROWTH_CALIBRATION.md)
for the petiole-only V1 switch and configurable contact limits. V1 still has three spans.

The replacement BAC shell is being validated separately; see
[`SHELL_MECHANICS.md`](SHELL_MECHANICS.md). Its native-mesh refinement gate has
not passed. Mode 2 and V9 are not available yet. The shared species default has
been corrected to 0; previously it selected 1 despite this opt-in documentation.

## Configuration and geometry

Set the numeric species property `ContactDeformationMode` to `1` for
`BroadLeafV1`, or `0` for `None`. Existing and custom species default to `None`.
Numeric request overrides remain effective. BroadLeafV1 requires one biological
blade segment; Carex's biological segmentation remains on the existing path.
No mature dimensions, 84-day expansion setting, crown probabilities or rhizome
activation settings are changed by enabling this profile.

A blade has two longitudinal bend coordinates and two independent side folds,
in radians. A petiole has two internal joints, each with two angular coordinates.
The existing basal support joint remains. Three mechanical spans still account
for exactly one petiole and one distal blade attachment. Leaf counts, intrinsic
length/half-width, developmental expansion and physiology do not count mechanical
sections as organs. The actual curved petiole tip transports its descendants.

The whole immutable mesh is split at the midrib and the two longitudinal seams.
Canonical edge intersections and shared vertices avoid cracks, including basal
lobes. Zero coordinates reproduce the authored surface. The original served
mesh is unchanged. Bergenia's bound profile has 176 vertices / 298 triangles;
its legacy mesh has 114 vertices / 178 triangles.

Every accepted blade deformation checks principal facet stretches, including
shear, against 2%. This also limits how far an intrinsically curved blade can
fold: the 75-degree limit is a numerical ceiling, not a promise that every mesh
can reach that angle without strain. Self-contact uses nonadjacent material
facets; two local vertex rings at connected seams are excluded to avoid treating
the refined midrib's narrow incident facets as independent bodies. Its normalized
midsurface separation margin is numerical, not a new biological thickness.

Implementation: `Plant/Leaf/ContactDeformation*.cs`,
`Plant/Leaf/LaminaTemplates.Deformation.cs`, `PlantSubFormation.Deformation.cs`.

## Mechanics and units

Stiffness comes from the existing tissue moduli (Pa), actual dimensions (m),
rod second moment `pi*r^4/4` and the existing effective longitudinal leaf keel.
Transverse folds use lamina thickness. Those inputs are model parameters, not
measured 'Rotblum' tissue properties. Angular compliance is in rad/(N m).
Dividing basal and internal petiole compliance into three `L/(3EI)` contributions
preserves the original **pure end-moment angular compliance**. This fixture does
not establish exact continuous-beam deflection for every distributed load.

Limits: 25 degrees per internal longitudinal blade bend, 75 degrees per side
fold, and a combined basal/internal petiole bend budget equal to the existing
whole-organ limit. Corrections are bounded to five degrees per coordinate/sweep.
They are provisional numerical safeguards.

A 0.25-degree scan of the actual Bergenia binding found a substantially smaller
strain-admissible range: simultaneous midrib bends about 4.75-5 degrees, symmetric
cupping about 5.25-5.5 degrees, and a single side about 5.5 degrees. This held at
full size and at 0.1/0.01 scales, in both directions. It diagnoses the current
mapping on the authored curved surface; it is **not a Bergenia flexibility
measurement**. Flat test blades can fold much further isometrically. Revising
the binding to preserve the surface metric during useful broad articulation is
necessary before interpreting stalled seasonal growth as a stiffness calibration
problem. Do not relax the strain tolerance simply to force a leaf-count target.

Each growth tick starts from the unloaded reference shape under gravity.
Linearized unilateral reactions distribute over both contacting organs, blade
controls and their support chains, using compliance times contact leverage.
Shared control derivatives use relative displacement; anchored rhizomes remain
fixed. During relaxation, reactions can decrease as constraints release. Elastic
coordinates never become a permanent crease or growth orientation.

The solver takes eight rounds, extending to sixteen for unresolved connected
contact groups involving an enabled plant. Stiffness is independent of tick size
and iteration count. This is a bounded nonlinear approximation, not a guarantee
of globally minimum elastic energy. Failed proposals retain certified partial
growth or restore accepted geometry. Endpoint and continuous tests include shape
coordinates and curved supports. Conservative facet sweep bounds use
`max(|p''|)/8` for deviation from the endpoint chord; every rollback is revalidated.

Diagnostics expose angles/revisions, rejected angular increments, estimated
unilateral reactions in **newtons**, foliage penetration residual in metres,
solid trial correction depth, rejection counts, and phase timing counters.
Reaction estimates are solver diagnostics, especially when a proposal is clamped
or rolled back. No contact area is defined, so none of these values is pressure.

## Cost and caching

Rest bindings, topology, stiffness geometry factors and sampling tables are
shared. Controls are stored by stable organ ID only for enabled formations.
Worker scratch caches requested deformed vertices and refits small triangle BVHs.
Bounded material-coordinate caches also reuse exact strain and self-sweep certificates
across rigid translation and rotation; changes in dimensions or controls invalidate them.
Sweep BVHs use each facet's swept bounds; padding every facet by the complete
blade motion produced an excessive number of candidates.

Clearance/query keys include full deformation, not just base and orientation.
Unchanged free-deformation certificates are reused; contact proposals use reusable
scratch. Fully settled-organ scheduling still needs further work: gravity visits
enabled organs to detect changed loads, and crowded contact groups can exhaust
relaxation. That is one reason the default-enable performance gate remains closed.

Measured warm contact-proposal loop: 10,000 calls, **zero allocated bytes**.
The deformation dictionary and state objects amortize to **473 bytes per
leaf/petiole pair**, excluding shared morphology/worker caches and other solver
bookkeeping. The complete scene memory gate must also account for optional light
sensors and enlarged collision-pose records; the latter are shared infrastructure,
including on rigid paths. The 1 KiB whole-feature budget is not certified by the
state-object measurement alone.

## Light, export and compatibility

CPU tracing uses the folded surface and one area-weighted irradiance per
biological leaf, retaining its per-leaf ray budget. Folded surface normals and
projected interception change; intrinsic physiological area does not. Petiole
geometry is three cylinders joined by two spheres in light/render/export paths;
collision uses conservative span capsules. Mechanical subdivisions do not create
extra leaf sensors or photosynthetic organs.

The legacy CPU leaf approximation and the new triangulated profile do not have
identical interception even at zero angles. Consequently a `None` versus
`BroadLeafV1` CPU-light comparison includes this geometry change; it is not a
pure stiffness experiment. Ambient-light fixtures isolate mechanical behavior.
External light protocols currently lack the required area-weighted folded-leaf
sensor contract, so enabled plants select CPU light with an explicit diagnostic.
External triangle exports and OBJ exports contain the deformed geometry.

New scenes use V8. Multibyte numbers remain little endian; strings retain the
existing .NET length-prefixed UTF-8 convention:

1. Version byte 8 and existing export-flags byte.
2. Immutable asset table: int32 count, then SHA-256 identifier string, int32
   payload length and payload for each unique asset.
3. Existing plant count. Each plant adds its asset identifier after its name.
4. Each organ adds a deformation kind after parent/organ identity: 0 none,
   1 blade, 2 petiole; kinds 1/2 carry four float32 angles. Existing V7 organ
   transforms/analytics follow unchanged.

An asset contains mode byte, profile-version byte (1), blade-slice count, all
slice meshes and a petal mesh, with per-vertex midrib binding heights. Assets are
hashed/cached once and embedded in each independently decodable keyframe.
Downloads therefore contain the actual assets used by their run. Playback resolves
recorded identifiers, not the current preset. This deliberately costs frame and
archive space; asset deduplication across frames is future optimization.

WebGL and WebGPU use the same profile definition. Picking, voxel/shadow geometry
and glTF downloads use the folded surface. The V7 reader and legacy mesh endpoints
remain available. Live checkpoints advance from v5 to **v6**, retain accepted
controls and stable IDs, and validate the immutable mesh/profile identity.
Older live states are rejected explicitly; their playback remains readable.

## Botanical evidence and calibration boundaries

- [RHS Bergenia cordifolia](https://www.rhs.org.uk/plants/2209/bergenia-cordifolia/details):
  leathery evergreen foliage, somewhat wavy margins, blades reaching 30 cm.
  This supports broad restrained elasticity rather than unrestricted cloth, but
  supplies neither a tissue modulus nor a leaf-expansion duration.
- [Couturier et al., 2012, Acer pseudoplatanus](https://bsapubs.onlinelibrary.wiley.com/doi/10.3732/ajb.1100325):
  developing leaf growth and folding respond to steric constraints. This supports
  retaining growth inhibition when deformation cannot create space. It does not
  transfer numerical stiffness or crease limits to Bergenia.
- User observations, Bergenia cordifolia 'Rotblum': largest measured blade
  attachment-to-tip 16 cm, petioles about 5 cm, pot top diameter 19.5 cm. Blade
  width and starting age are unknown. The dated photos show an already flowering
  plant, so they do not define seedling growth time or a universal leaf count.
- The doubled developed-leaf-density objective is a field-observation calibration
  target. The angular bounds, three spans, four blade coordinates and 2% strain
  tolerance are engineering choices. They are not published biological constants.

## Acceptance evidence and open gates

Temporary probes live under `/tmp/bergenia-folding`; no diagnostic harness is
part of the application. Results are recorded below to distinguish code checks
from biological acceptance.

- Core build: prescribed command, both target frameworks, zero errors.
- NativeAOT publish succeeded. A fresh 20-day CPU-light case based on archived run
  `55d485f815824db9ba882de241776624` completed with folding enabled. Its 120 V8
  frames occupy 638,090 packed bytes; final raw frame 8,100 bytes and compressed
  checkpoint 455,556 bytes. The final NativeAOT binary also resumed an existing v6
  run from day 20 to day 21, producing 126 frames. This is a smoke test, not a
  seasonal calibration. All 120 fresh NativeAOT frames also match the earlier
  stored pre-resume frames byte-for-byte.
- 6,851 existing geometry assertions pass, including all seven presets, tiny
  blades, widths, petiole units, thin-wall and widening sweeps, and explicit V7.
- 35 deformation assertions pass: mature/tiny translated blades, strain, flat
  isometric folding, analytical area/projected-light sampling, tip attachment,
  pure end-moment compliance and 12 byte-identical resumed frames.
- The actual folded CPU-light gather agrees with independent angular integration
  in an unoccluded CIE-overcast fixture: flat 299.980 versus 300.000 W/m2
  (`DHI + groundAlbedo * GHI`), and a 60-degree flat-material fold 262.215 versus
  262.217 W/m2. The fixture uses 65,536 samples to suppress sampling error; normal
  simulation sampling budgets are unchanged. Earlier direct-light checks verify
  projected interception without multiplying material area.
- Frozen off-centre load/release produces identical controls at 1 h and 4 h;
  release error is zero in that fixture, including joints smaller than 0.1 mm
  that previously fell outside the gravity update branch.
- 82,800 sampled swept vertices remain inside the conservative nonlinear chord
  bounds, including combined rigid rotation, size changes and folding.
- Four plants in two separated pairs, with a flat compliant test blade: 192
  resumed frames at 1 h and 48 at 4 h match byte-for-byte, including forced organ
  removal, natural deaths, reindexing and rebuilt caches. This small fixture
  exercises interplant contacts; it does not prove simultaneous parallel worker
  scheduling or retained renewal transactions (none occurred in this run).
- The existing rigid four-plant continuation check and 54 support-rollback
  assertions remain passing.
- Actual tray-tip contact at both tick sizes accepts growth from 120 to 124 mm,
  with identical folding/bending and estimated 0.02538 N reaction. Removing the
  tray permits 128 mm. Both contact and release geometry pass static certification.
  A closed box around the accepted plant rejects an impossible 1 m blade proposal
  and retains the certified 128 mm blade at both tick sizes.
- Actual WebGL2 transform-feedback and WebGPU compute agree with backend fixture
  vertices to 0.553 micrometres over 1,056 vertices. Production V8 playback and
  WebGPU picking pass; picking leaves the camera matrix unchanged. Both renderers
  export finite, embedded glTF geometry (18 meshes in the test scene). Frontend
  type checking and the production build pass.
- Legacy saved playback loads 163 objects / 8,616 triangles without browser errors.
- Representative repaired P003 plants, then all 790 plants, complete a three-day
  ambient run with finite resources and no unresolved contact failure. This does
  not validate mature facade coverage.
- Fixed population, 16 plants / 144 organs / 32 leaves: rigid collision phase
  **28.93 ms versus deformable 277.92 ms** in optimized Release builds; light
  0.332 versus 0.589 ms. Collision plus lighting is approximately **9.5 times**
  the rigid cost. Triangle tests rise from 2,917 to 178,138 per tick. This deliberately
  crowded fixture starts with overlaps (7 rigid / 6 deformable unresolved organs),
  so it is an adversarial cost fixture, not a biological success. It **fails** the
  25% engineering target. Warm whole-collision-tick allocations are 237,622 and
  369,252 bytes respectively; these include existing fallback machinery, unlike the isolated
  zero-allocation contact-proposal loop.
- Same fixed scene: raw V8 frame 19,533/22,989 bytes; packed frame 9,177/11,437
  bytes; measured process peak working set 92.2/103.1 MB. Measurements were
  separate processes with concurrent validation load and are not precision timing.

Default enablement requires the outstanding crowded-contact performance work,
full settled-organ scheduling and complete memory accounting, stronger equilibrium/
confinement and simultaneous scheduling coverage, and biological and mature-facade
acceptance. Do not compensate for these gaps by shortening the
84-day growth setting, changing crown probabilities or increasing clonal spread.

## Controlled seasonal comparison

Seed 42, one crown, archived default terrain and 1 January 2026 start; clonal
spread stays disabled. Dimensions, the 2,016-hour (84-day) growth setting and
crown probabilities are the archived request values. The old request's serialized
runtime gravity scaling is reversed before fresh initialization. These are fresh
runs on the current implementation, not continuation of the old live checkpoint.

Each cell is **whole living leaves / at least 50% expanded / at least 90%
expanded**. Expansion means the smaller of the intrinsic length and width ratios
to the sampled targets of that leaf; it does not mean projected folded area.

| Light / tick | Day 330 rigid | Day 330 folding | Day 470 rigid | Day 470 folding |
| --- | ---: | ---: | ---: | ---: |
| Ambient, 1 h | 12 / 7 / 2 | 2 / 0 / 0 | 12 / 2 / 1 | 2 / 0 / 0 |
| Ambient, 4 h | 6 / 5 / 5 | 12 / 3 / 3 | 6 / 4 / 3 | 12 / 1 / 1 |
| CPU, 1 h | 6 / 2 / 2 | 10 / 2 / 2 | 6 / 2 / 2 | 10 / 2 / 2 |
| CPU, 4 h | 18 / 3 / 2 | 10 / 2 / 0 | 20 / 1 / 1 | 10 / 1 / 0 |

The developed-leaf-density target **fails**. In particular, 4-hour ambient light
doubles the total number of leaves at day 330 but reduces the number reaching
90% expansion from five to three. The 1-hour ambient case remains at two tiny
leaves. The CPU comparisons also include the change from the legacy light quad
to the folded triangular surface, so they do not isolate stiffness alone.

Threshold crossing times were sampled every simulation tick. Among leaves that
reached 90% by day 330, conditional median ages are approximately 78-84 days in
the rigid cases and 80-83 days in the successful folding cases. Folding at 1-hour
ambient and 4-hour CPU light has no leaf reaching 90% by that date. These are
censored model outputs, not botanical duration estimates; the 84-day setting is
retained. The JSON record includes every tracked leaf and explicit null thresholds.

All cases retain one crown. Day-330 crown axial envelopes remain about 1.0-3.7 cm.
Above-ground water and mass are checked each tick; root resources are checked at
snapshots. Bud/petal snapshots, paused-growth counts, renewal/death counters, blade
dimensions and petiole length/diameter are retained in the result record. Flowering
snapshots do not establish exact anthesis dates. Persistent crown obstruction
remains; successful isolated fixtures do not establish seasonal biological acceptance.

For the separate 'Rotblum' comparison, the larger simulated blades at day 330
reach about 21.6-25.4 cm attachment-to-tip, versus the observed largest blade of
16 cm. Petioles attached to those largest blades measure about 8.0-14.0 cm,
versus the approximately 5 cm field observation. Whole-population petiole medians
include immature organs and are therefore not a like-for-like comparison.
Blade width and initial plant age are unknown, so these measurements do not justify
retuning the fixed dimensions during the mechanics comparison.

Detailed measurements: [CONTACT_DEFORMATION_RESULTS.json](CONTACT_DEFORMATION_RESULTS.json).
