# Volunteer herbaceous species

Added on 5 November 2026. All three presets use `Behavior.Herbaceous` and are
registered in `/Simulation/species`. Their leaf and petal meshes use the shared
lamina pipeline for contacts, light measurements, playback and geometry exports.

| Preset | Implemented architecture | Appearance |
| --- | --- | --- |
| Persicaria minor | Annual, alternate leaves on branching aerial shoots; terminal racemes; no vegetative spread | Narrow entire blades, green foliage, small pink five-part flowers on interrupted slender racemes |
| Lactuca serriola | Taprooted rosette followed by a leafy bolting stem and terminal panicle; one reproductive season | Lobed, clasping leaf outline with marginal teeth; glaucous green foliage; small pale-yellow ligulate heads |
| Taraxacum officinale aggregate | Persistent taprooted basal rosette; independently renewed unbranched leafless scapes | Deep backward-pointing leaf lobes, green foliage; yellow heads represented by 54 ligules in three rings |

The botanical authorities are omitted from the request names, as with the existing
presets. The requested names are *Persicaria minor* (Huds.) Opiz, *Lactuca serriola*
L., and the *Taraxacum officinale* aggregate. The aggregate preset represents a
common lobed form, not a particular microspecies or a confirmed photograph ID.

## Botanical basis and modelling boundaries

- [UBC E-Flora: Persicaria minor](https://linnet.geog.ubc.ca/Atlas/Atlas.aspx?sciname=Persicaria+minor)
  supports the branching annual habit and narrow leaves. [BSBI](https://fermanagh.bsbi.org/persicaria-minor-opiz)
  informs the entire, unblotched blade. The preset uses 6 cm blades, about 7 mm wide.
- [Flora of New Zealand: Lactuca serriola](https://nzflora.info/factsheet/taxon/Lactuca-serriola.html)
  and [Kew's flora descriptions](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:228255-1/general-information)
  support an annual/biennial plant with a leafy flowering axis and an open panicle.
  [University of Texas herbarium](https://w3.biosci.utexas.edu/prc/K12/pages/Lactuca%20serriola.html)
  informs the lobed foliage and toothed ligules. This preset selects a spring cohort
  that bolts after 45 days; it does not model vernalisation or compass-leaf orientation.
- [Go Botany: Taraxacum officinale](https://gobotany.nativeplanttrust.org/species/taraxacum/officinale/)
  and [UC IPM](https://ipm.ucanr.edu/PMG/WEEDS/dandelion.html) support a persistent
  basal rosette and solitary heads on separate leafless stalks.
  [NSW Flora](https://plantnet.rbgsyd.nsw.gov.au/cgi-bin/NSWfl.pl?page=nswfl&lvl=gn&name=Taraxacum)
  describes the five-toothed ligules used by the shared floral mesh.

Dimensions describe representative individuals. Rates, bolting age, stiffness,
RGB colors, branch probabilities and root taper are explicit model assumptions,
not measured species calibration. The default flower windows describe Central
European seasons; a single annual cohort need not flower throughout that window.
The color curves include ageing and stress without baking a disease coating into
healthy leaves. Stems, buds and petal fate follow `ORGAN_COLORS.md`: prickly lettuce
has its whitish stem, knotweed reddens and keeps its tepals, the composites wilt and
drop their ligules, and the two annuals yellow and dry in the weeks before they die.

The imported proposal also discussed grasses, seed recruitment, dispersal,
root-fragment regeneration and disease. Those are independent future mechanisms.
These presets do not simulate a seed bank, windborne fruits, dandelion puffballs,
root-fragment recovery or a mildew diagnosis. Ligules represent the exposed part
of an entire floret; floral tubes, styles, involucres and individual leaf spines
are below the model's current resolution.

## Opt-in growth extensions

All new controls default to the previous behavior:

- `BoltingAgeHours`, `RosetteNodeDistance`, `RosetteMaximumNodes`: hold a
  compressed rosette before elongating its waiting apex. Existing basal nodes
  remain compressed.
- `HerbaceousBranchProbability`, `HerbaceousBranchMaximumOrder`: one axillary
  branch trial per new node, with a finite share of the parent's resources.
  This is not a probability per simulation tick.
- `RosetteScapes`: flowering preserves the rosette apex. Its leaves can still
  age and renew; scapes have separate attachment offsets and a shared count cap.
- `Monocarpic`: complete one reproductive season and senesce without rearming
  next spring. Crossing January does not kill an immature overwintering plant.
  Existing checkpointed flowering history carries this state.
- `Taproot`: use an elongated primary storage axis in explicit-root mode.
  Its 25 cm segment target is an approximation, not a universal adult root-depth
  cap. A proposed tip outside the soil volume does not extend the root.
- `RootRadiusTaper`: bound child radius relative to its parent. Persicaria uses
  0.6 and the two taprooted presets 0.55; one retains the previous behavior.

The shared seed initializer retains one millimetre-long basal support marked
internally as a rhizome. These presets create no spreading rhizome segments or
daughter crowns. Virtual roots remain the existing aggregate proxy.

The first probe exposed the legacy untapered root network: Persicaria accumulated
about 168 g of roots while its shoot weighed about 8 g. The equal-radius rods,
rather than plant mechanics, were the cause. Taper reduced that case to about
2.3 g of roots. An initially proposed threefold taproot-radius multiplier was
also removed; the primary axis already becomes dominant when its children taper.
Legacy germination and early root expansion remain empirical and timestep
sensitive; `root-seedling-audit.json` compares these presets with the existing
species. These runs establish architecture and numerical stability, not validated
seedling carbon allocation or growth under every climate/light model.

## Validation and regression scenarios

Temporary diagnostic console projects were kept under `/tmp`, targeting .NET 8
and referencing `Agro.csproj`. Saved evidence is in
[`canopy-validation/volunteer-species`](../canopy-validation/volunteer-species).
The principal runs use one plant, seed 42, a 1 March 2026 start, `RenderMode = 0`,
Mixed contacts and identical requested weather horizons for paired timesteps.

| Scenario | Required regression signal |
| --- | --- |
| All three, 1 h and 4 h ticks through flowering | Finite shoot/root water, energy, dimensions and mass; inspect organ counts, axis directions and floral targets, not just the final frame |
| Persicaria, 400 days | Aerial branching; narrow pink racemes; finite tapered root network; no spreading rhizomes; no plant/root records after seasonal death |
| Lactuca, days 30, 60 and peak flowering | Initial compressed rosette, then elongated leafy stem and an open terminal panicle; no inverted main stem or pedicel/bud chain |
| Both annual presets, 400 days | No second-year restart; roots and shoots both terminate after the reproductive season |
| Taraxacum, 730 days at 4 h | Basal crown remains compressed, leaves renew, independent leafless scapes flower in both years; plant survives |
| Taraxacum terminal scape fixture, 1 h and 4 h | Exactly one head per terminal scape; no duplicate fall-through birth |
| Composite-head fixture, 1 h and 4 h | Exactly 54 Taraxacum and 14 Lactuca ligules; fixed total count across rings; conserved birth energy |
| All three, checkpoint continuation | Exported frames and complete state byte-identical after resume |
| All three, Certified and Soft, 80 days | Finite state and readable nonempty exports; opt-in architecture remains runnable under each engine |
| All three, CPU light (mode 3), 180 days at 4 h | Finite daily resources and flowering; integration smoke with 16 hemisphere samples, not light-growth calibration |
| Coarse/fine leaf meshes and HTTP endpoints | Finite, consistently wound triangles, correct dimensions, one boundary, no overlapping triangulation, matching projected area, proportional seedling relief |
| Old saved frontend settings | All nine new fields retain defaults when absent; explicit values survive loading |

`probes.json` contains the final daily samples, paired-timestep metrics and
resource maxima. The leaf and full-plant PNGs use the actual frontend geometry
adapters; the full-plant gallery decodes saved V8 frames with production transforms
and natural recorded colors. Each plant is framed independently. The flower-head
PNG is a geometric fixture, not an application screenshot.

Final seed-42 maxima at 1 h / 4 h were 50.5 / 48.7 cm for Persicaria,
100.1 / 100.5 cm for Lactuca, and 29.3 / 30.2 cm for Taraxacum. All six paired
runs had zero inverted non-petal organs and finite shoot/root resources. Both
annuals ended on day 246 and exported no plant or root records through day 400.
Taraxacum retained twelve leaves and a crown under 4 cm through day 730 and
flowered again in its second year. Lactuca's flowering onset differed by fourteen
days between timesteps; the stochastic histories are not equivalent trajectories.

Two morphology regressions were corrected during validation: the unbranched
floral path created its terminal cluster twice, and Lactuca's successive 55/40
degree branch inclinations accumulated until pedicels inverted. Returning after
the terminal birth and using 45/20 degree panicle inclinations resolved those
specific failures. Seven checkpoint comparisons passed, including a growing
Lactuca panicle. The final backend build, frontend typecheck, old-settings loading
probe, and live species/mesh endpoint checks also passed.

## Lactuca and Taraxacum leaf refinement

The follow-up leaf refinement replaces the original angular width profiles with
independently drawn curved perimeters. [Before/after comparison](../canopy-validation/volunteer-species/leaf-refinement/comparison.png)
and [final front/oblique views](../canopy-validation/volunteer-species/leaf-refinement/leaves.png)
show the actual coarse meshes at the same physical dimensions and lighting.

- Lactuca has three unequal pairs of broad, hooked lobes, rounded U-shaped
  sinuses, small marginal teeth, clasping basal ears and an elongated terminal
  lobe. The shape follows the lobed form in the
  [Institute of Botany CAS photographs](https://pladias.cz/taxon/pictures/Lactuca%20serriola).
- Taraxacum has five staggered pairs of backward-pointing lobes, deeper narrow
  sinuses, occasional secondary teeth, a broader triangular terminal lobe and
  a long winged base. The
  [University of Wisconsin Extension photographs](https://hort.extension.wisc.edu/articles/dandelion-taraxacum-officinale/)
  illustrate this deeply divided form and the aggregate's variation.

The surfaces add shallow cupping, torsion and a raised midrib. Triangulation
preserves the hooked recesses and the midrib seam; interior edge optimization
prevents long triangle fans from making the rib appear corrugated. Mesh generation
runs once during preset initialization. Coarse meshes contain 199 Lactuca and
201 Taraxacum triangles; the fine meshes retain exactly the same projected outline.
Both renderers, contacts, exports and mesh-based LAI use these shared assets.

Nominal blade dimensions remain 18 x 6 cm and 18 x 5 cm. Persicaria's geometry,
all species parameters, flowers and colors are unchanged by this refinement.
The narrower outlines change the survey outline factors to 0.40242848 and
0.36600533 respectively. This is also a contact/LAI geometry change, rather than
only a shading change. Existing V8 playback retains its embedded geometry;
live checkpoints with incompatible geometry are rejected by the existing asset
hash check.

Regression checks for these shapes cover simple closed boundaries, winding,
triangle degeneracy/overlap, physical dimensions, seedling relief scaling,
coarse/fine area agreement, and rays through the hooked sinuses against an
independent polygon oracle. The 100-day Seed-42 probes at both 1 h and 4 h use
Mixed contacts and ambient light; all four retain finite shoot/root resources
with no inverted non-petal organs. This is a geometry integration check, not a
new species growth calibration.

The original evidence above remains as the initial implementation record.
Follow-up results and their exact source hashes are recorded separately in
[`leaf-refinement/manifest.json`](../canopy-validation/volunteer-species/leaf-refinement/manifest.json).
This refinement edits only `Plant/Leaf/VolunteerLeafMorphologies.cs` and this
document; its new validation artifacts are enumerated in that manifest.

## Files changed for this addition

Species and shared assets:

- `Agro/Species/Persicaria_Minor.cs`
- `Agro/Species/Lactuca_Serriola.cs`
- `Agro/Species/Taraxacum_Officinale.cs`
- `Agro/Plant/Leaf/VolunteerLeafMorphologies.cs`
- `Agro/Plant/Flower/FlowerMorphologies.cs`
- `Agro/Plant/Flower/FlowerSettings.cs`
- `Agro/Plant/Flower/FlowerHelper.cs`

Integration and behavior:

- `Agro/SpeciesSettings.cs`
- `Agro/Plant/AboveGroundAgent.cs`
- `Agro/Plant/AboveGroundAgent.Herbaceous.cs`
- `Agro/Plant/PlantFormation.cs`
- `Agro/Plant/PlantSubFormation.cs`
- `Agro/Plant/UnderGroundAgent.cs`
- `Agro/Plant/VirtualRootsFormation.cs`
- `SchemeGenerator/Reader.cs`
- `ThreeFrontend/src/helpers/Species.gen.ts`
- `ThreeFrontend/src/components/hud/Species.gen.tsx`
- `Agro/VOLUNTEER_SPECIES.md`

Validation artifacts are enumerated in `validation-manifest.json`. Existing
unrelated worktree changes were preserved; no diagnostic harness was added to
the repository.
