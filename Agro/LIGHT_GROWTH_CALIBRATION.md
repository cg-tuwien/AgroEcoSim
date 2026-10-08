# CPU light, expansion calibration and contact trials

## Update (2026-10-03)

Soil now debits delivered water in regular fields and imported trays; see
[SOIL_WATER_BUDGET.md](SOIL_WATER_BUDGET.md). Live checkpoints now use version 20; see [COLLISIONS.md](COLLISIONS.md) for the post-contact and floral retry changes.
The glucose-equivalent construction and maintenance model is still an **opt-in
experiment**, not a published calibration for these cultivars. Its positive-cost
mode must pass matched 1-hour/4-hour CPU-light and shade probes before it can
replace the default. A 755-day rigid Bergenia replay of the archived September
request (seed 42, four-hour CPU ticks, clonal spread disabled) had eight whole
leaves at day 330, four meeting a 10 cm length and 10 cm full-width threshold,
and zero petals throughout. Its mode was changed from V1 to rigid for this
control, and deterministic scheduling was enabled; it is not a V1 or V2 result.
Soil-budget correction by itself therefore did not cure the sparse, nonflowering
Bergenia behavior. The 2026-09-30 status and tables below describe their
original measurements and checkpoint version at that date.

## Status (2026-09-30)

Follow-up: the [resource-unit audit](#resource-unit-audit) below corrects lamina
allocation weights, reserve horizons, root growth units and photosynthetic water
demand. The [construction-budget experiment](#construction-budget-experiment) adds
transactional tissue costs, disabled by default. The [seed-budget and maintenance
follow-up](#seed-budget-and-maintenance-follow-up) separates hydration from reserves,
conserves their transfer at germination, and fixes premature root death. These
corrections are active by default. Live checkpoints now use version 10.
The earlier calibration table and
version-7 validation below describe the preceding implementation, not the final
resource-corrected measurements.

CPU light integrates six solar positions per hour. Direct beam, diffuse angular radiance,
analytic reflected surface light and obstacle measurements use the same interval. Growth
rate coefficients below are a provisional calibration against CPU mode 3. Ambient mode 0
remains a constant-light diagnostic. The absolute photosynthetic budget and relative
resource allocation have not been replaced or multiplied by another irradiance factor.

The broader light-response defect is **not resolved**: the default still has no
construction-energy debit, and experimental costs have not passed biological acceptance.
Widening petiole
bend limits did not consistently improve developed-leaf count, so the normal limits and
mode 0 default are retained. V1 still has three spans; the proposed four-span model is
pending versioned controls, geometry and compliance validation.

## Sky integration and compatibility

`SkyInterval` averages ten-minute midpoint skies in W/m² (or W/m²/sr); consumers multiply
by the tick duration exactly once to obtain Wh/m². Dark samples remain in the denominator.
A four-hour tick has 24 samples, a one-hour tick six, a 24-hour tick 144. Sky construction
is cached per world/tick, including repeated growth-guidance and illumination queries.
Ground radiance and zenith-only CIE factors avoid repeated sample work. Moving direct
shadows still require visibility queries per leaf and solar position; six times as many
positions are not free just because the sky itself is shared.

The viewport deliberately retains a representative sun spot. It does not calculate plant
energy. The old CPU direct beam already sampled several sun positions; its diffuse sky
and analytic reflections incorrectly used the brightest instantaneous shape scaled to
average GHI. These now use the angular time integral. Cloud coverage is still the existing
per-tick mean, not an independently reconstructed ten-minute weather history.

Live checkpoints advance from 6 to 7 because continued light/growth semantics change.
Irradiance offset arrays serialize their meaningful prefix, excluding trailing -1 capacity;
otherwise equivalent warm/resumed buffer histories wrote different bytes. Old live states
are explicitly rejected; V7/V8 **scene playback** and recorded mesh assets are unchanged.
The later shell milestone must select a subsequent checkpoint version.

## Provisional CPU calibration

User-approved target: existing `LeafGrowthTime` values, pending measured expansion curves.
`LeafExpansionRateScale` multiplies blade/petiole developmental increments, not photosynthesis,
resource creation, mature dimensions, growth probabilities, clonal spread or support limits.
It applies to herbaceous organs under all light models; custom settings default to 1.
Numeric request overrides are retained and UI fields use the existing generator.
Use refreshed preset values to apply the calibration. An older/custom request that omits
the new rate field keeps the compatibility default of 1; an explicit value is never
silently replaced with a preset coefficient.

Seed 42; March 1 2026; 180 days; one plant; clonal initiation/activation disabled;
mode 3; foliage collisions disabled to isolate expansion, hard soil collisions retained.
Times are birth-to-first-90%-intrinsic-area, conditional on reaching that threshold.
For segmented Carex, sum the sections as one blade. Unreached leaves are not zeros.
These are model results, **not measured species durations**.

| Preset | Existing duration, days | Rate scale | Median days to 90%, 1h / 4h | Reached / observed, 1h / 4h |
|---|---:|---:|---:|---|
| Geranium macrorrhizum | 7 | 1 | 16.0 / 15.2 | 12/14 / 12/14 |
| Geranium × cantabrigiense | 14 | 1.25 | 14.2 / 14.8 | 12/14 / 12/14 |
| Bergenia cordifolia | 84 | 1.15 | 102.4 / 87.3 | 11/20 / 10/20 |
| Heuchera micrantha | 20 | 2 | 27.5 / 29.1 | 14/14 / 14/14 |
| Campanula poscharskyana | 14 | 1.5 | 12.0 / 13.0 | 28/28 / 28/28 |
| Carex morrowii Variegata | 36 | 1.4 | 32.5 / 31.3 | 10/10 / 10/10 |
| Fragaria vesca Rügen | 20 | 1.8 | 19.7 / 20.2 | 24/24 / 24/24 |

Acceptance is partial. Macrorrhizum's 2.7 trial did not improve expansion, so it stays at 1;
its supporting geometry is limiting. Heuchera and Bergenia remain slower than the target.
A Bergenia 1.4 trial stalled at two tiny leaves in the one-hour run and was rejected.
Do not compensate for these constraints with successively larger multipliers.

The earlier exploratory `baseline` runs outside this archive did not fully suppress basal
rhizome buds and counted Carex sections: they are not the single-crown calibration above.

## Light response: evidence and next correction

The `cpu-final-shade` cases put a radius-10 m umbrella above the plant. For Fragaria at
four-hour ticks, median measured leaf exposure over days 31–149 fell from about 3585 to
1073 Wh/m²/day, while conditional median expansion fell from 20.2 to 14.9 days. Stored
above-ground energy at day 180 fell from about 380 to 127 model units; tissue volume was
similar. Thus lower absolute light reaches the energy store, but does not reliably limit
structural growth. Relative allocation alone does not spend that store on construction.

A controlled trial multiplying expansion by `clamp(Energy/EnergyStorageCapacity,0,1)`
changed little because the reserves were already full. It was removed. Neither that
unsuccessful trial nor an arbitrary absolute-light denominator is in production.

Research supports carbon limitation, stored assimilate use at night and hydraulic limits;
it does not support a universal stronger light exponent for these seven cultivars:

- [Graf et al., PNAS 2010](https://pmc.ncbi.nlm.nih.gov/articles/PMC2889127/): Arabidopsis
  starch supply and growth at night; supports retaining a reserve rather than stopping
  growth whenever the sun sets. Not a Bergenia calibration.
- [Pantin et al., Plant Physiology 2011](https://pmc.ncbi.nlm.nih.gov/articles/PMC3177277/):
  developmental changes in carbon and hydraulic limitation of leaf expansion. Not evidence
  that every individual blade must expand monotonically faster in higher light.
- [Fragaria × ananassa shade experiment](https://www.frontiersin.org/journals/plant-science/articles/10.3389/fpls.2021.647585/full):
  evidence for light effects on growth, but not cultivar-specific constants for F. vesca Rügen.

The next correction must define the existing energy unit before assigning a construction
cost per dry mass. The current photosynthetic conversion, water debit, volume-based
maintenance and cylinder-based storage weights do not establish a calibrated carbon unit.
Then charge only **accepted** new tissue, refund rolled-back increments and pending births,
retain the plant-wide allocation rule, and test mass/energy conservation under uniform
attenuation and day/night cycles. Arbitrary construction coefficients would hide this
unit problem, so none have been introduced here.

## V1 and mode 0 contact findings

Both contacting branches can be suspended when coupled corrections, redirection and
partial-growth trials fail certification. This is not an unconditional stop on first
contact. Physiology continues; retries are normally daily and become 72-hour retries after
three failures. Larger motion can wake a retry early. Collision guarantees remain in force.

New settings:

- `PetioleContactBendDegrees`: 0 inherits the global limit (normally 25°); explicit finite
  values through 90° are accepted. In mode 0 this is a **base-joint** limit. In V1 it is a
  combined base plus internal budget. Gravity stiffness is unchanged.
- `ContactLeafFoldingEnabled=false`: V1 retains its three mechanical petiole spans while
  removing blade-fold controls. One biological petiole and leaf are still counted.

Archived September-start request e9bd78f470cd4e36bc6b715c50319d44, shortened to 120 days,
CPU light, seed 42, disabled clonal spread, deterministic scheduling. Compare only matched
sampling stages in the manifest. The latest six-position-sky mode-0 comparison gave:

| Petiole limit | 1h: leaves / reached 90% | 4h: leaves / reached 90% |
|---|---|---|
| 25° | 4 / 2 | 4 / 1 |
| 45° | 8 / 1 | 4 / 1 |

More small leaves is not a developed-density success. Earlier V1/rod-only 60° trials were
also inconsistent across tick sizes. Neither 45° nor 60° is enabled by default.
These angles are engineering safeguards, not measured Bergenia yield limits.
[Pilea petiole mechanics experiments](https://academic.oup.com/jxb/article/73/4/1236/6459314)
support bending/torsional flexibility but do not validate a Bergenia-specific safe angle.

Four spans would add one independent two-axis internal joint (six internal angular
coordinates instead of four). That requires consistent rod geometry, compliance, CCD,
attachment transport, lighting, rendering/export and a recorded profile/control version;
changing only a loop bound would corrupt geometry and old playback. It has not been done.

## Validation and reproduction

Artifacts and exact requests: `canopy-validation/cpu-light-v1-sun6/`.
`manifest.json` identifies scenario stages and task-local harnesses outside the repository.
`summary.json` reports the conditional expansion measurements above. Each run stores daily
counts, dimensions, finite resources and individual leaf histories; final calibration runs
also retain daily light/production data. Benchmarks use fixed populations and geometry hashes,
not successful growth producing unequal workloads.

Required regressions added for this mismatch:

1. Six positions/hour, including 24h/48h intervals; four-hour versus four one-hour integrals
   under identical weather/geometry; seasons, orientations, dawn/dusk and dark intervals.
2. Horizontal diffuse integral equals DHI + groundAlbedo × GHI under the model's 128-direction
   normalization quadrature. An independent dense quadrature found an existing ~0.9% dawn
   normalization discrepancy; this has not been represented as exact continuous integration.
3. Moving wall shadow against a one-minute reference (960.620 vs 960.554 Wh/m² in the fixture).
4. 180-day CPU single-crown open/shaded probes at both tick sizes; record unreached leaves
   instead of treating conditional median times as universal expansion durations.
5. 25°/45° mode-0 and 25°/60° V1/contact-only comparisons, preserving certified collision
   response. No acceptance based on microscopic leaf counts alone.
6. Checkpoint v7 roundtrip, explicit v6 rejection and byte-identical next-day continuation
   and exported frames, in managed and NativeAOT CPU mode at one/four-hour ticks.

333 sky assertions and 72 checkpoint/consumer assertions passed. Build and frontend check
logs are archived alongside the results. Full 790-plant seasonal/performance acceptance,
flowering recovery, a corrected carbon construction budget and four-span V1 remain open;
these short contact trials cannot establish those claims.

Fixed-population light benchmark: 16 plants, 282 sensors, 723 primitives, identical exported
geometry hashes. Repeated same-tick solves fell from about 2.89 to 2.60 ms after sharing the
sky and common CIE factors. On distinct ticks (new solar quadrature each time), the new
median was about 6.20 ms versus 2.66 ms before. These small-scene timings are illustrative, not a multicore or
end-to-end acceptance gate. Fresh sky construction allocates through the existing solar
position library; warmed angular radiance queries allocate nothing. The extra solar
positions therefore have a measurable cost despite being shared across all leaves.

Frontend typechecking and 108 focused tests passed, including legacy scene decoding and
an actual backend V8 petiole frame at 60°. The decoder accepts the configurable range
through 90° and rejects larger/nonfinite coordinates; it retains the same three-span map.
The actual net8 NativeAOT server also published and served all eight species/default
records with the new fields in a localhost smoke test using a separate temporary run store.
After the sky caching/kernel optimization, a repeated 180-day Fragaria four-hour run had
identical daily biological snapshots, leaf histories and exported final-frame bytes.

## Resource-unit audit

This pass preserves absolute light capture followed by relative resource allocation.
It adds no light multiplier, changes no species rates or dimensions, and does not
enable V2 or alter petiole subdivision. The corrections are:

| Path | Defect | Correction |
|---|---|---|
| `AboveGroundAgent.EnergyStorageCapacity` | Applied solid-axis `4 L R²` to leaves and petals | Use the existing organ-specific `Volume()`; a lamina weight scales linearly with width, and segment weights sum to the whole blade |
| `PlantFormation2.Census` | Divided per-tick maintenance by tick hours twice | Reserve seven days of maintenance, with one-tick minimum for unusually long intervals |
| Emergency pruning in the same method | Subtracted one tick of maintenance from a multi-day reserve deficit | Remove demand over the same reserve horizon, including descendants |
| `UnderGroundAgent.Tick` | Growth increments lacked elapsed hours; elongation's volume was `4 L² R` | Apply the empirical rates per hour once; use the existing root volume `4 L R²` |
| Root radial-growth clamp | Reduced uptake capability using the proposed increment even when the support admitted less | Use only the accepted radius increment |
| `AboveGroundAgent.PhotosynthPerTick` | Nonphotosynthetic axes and floral organs requested assimilation water | Only leaves request it; all organs retain tissue-water storage allocation |

At Bergenia's nominal half-width 0.095 m and the model's 0.0001 m physiological
thickness, the old lamina reserve weight was 1,900 times the volume-based weight.
This was an allocation weight, not a hard storage cap. Correcting it does **not**
delete carbohydrate or limit stored energy. Non-lamina storage retains the existing
formula and wood factor. The historical reserve coefficient is now documented as
an empirical weight, not a measured three-month survival duration.

The root rates retain the previous one-hour coefficients. Their numerical magnitude
is still empirical; this is a units correction, not a measured Bergenia root-growth
calibration. Four-hour growth now integrates four hours of that rate, rather than
one old increment. Explicit stepping, branching and resource feedback still produce
different seasonal trajectories at the two tick sizes.

### Physical and botanical limits

The carbon budget remains incomplete. `Energy` is explicitly in custom units;
assimilation adds `incident Wh × 0.005` and subtracts the same numerical quantity
from water in grams. Maintenance uses organ-volume expressions, while continuous
construction has no substrate debit. The coefficients therefore cannot yet be
interpreted as a consistent glucose, carbon or chemical-energy budget.

[Penning de Vries et al. (1974)](https://pubmed.ncbi.nlm.nih.gov/4367755/) provide a
biochemical construction-cost framework. [Poorter et al. (2006)](https://pubmed.ncbi.nlm.nih.gov/16303828/)
measure leaf construction cost, light environment and payback time across woody and
herbaceous plants; their shade leaves also change specific leaf area. These support
charging substrate for newly constructed dry matter and treating shade acclimation
separately. They do not provide a universal Bergenia expansion multiplier.
[Penning de Vries (1975)](https://research.wur.nl/en/publications/de65ef58-2420-4085-85f9-32187c4b5330)
treats maintenance separately from construction; neither coefficient can be dropped
into the current custom-unit pool without a conversion.

Further reachable approximations identified, not changed in this pass:

- Nonwoody axes have zero maintenance in the current above-ground formula when
  `WoodFactor == 0`. This is not a physiological statement about living petioles.
- Stored `Energy` does not contribute to mechanical mass in `GetWeight`. Giving it
  carbohydrate-mass units requires including reserve mass as well as structural
  dry mass and water; simply renaming the field would leave that balance inconsistent.
- The 500 W/m² photosynthetic-water forecast remains constant even at night; actual
  photosynthesis still uses integrated measured light. Water demand, biochemical
  water consumption, transpiration and hydraulic limits need distinct definitions.
- Growth has direct relative-production gating in addition to resource allocation.
  Any replacement should preserve stored-carbon-supported growth, including darkness.
- Root uptake uses the empirical surface proxy `Length × Radius`; volume uses a
  square cross-section. Changing either normalization requires checking its fitted
  coefficient and reconciling resource and mechanics geometry.
- Physiological blade volume is a constant-thickness rectangular envelope, not an
  outline-integrated cultivar-specific dry-mass model. Mechanical thickness, rendered
  relief and physiological thickness must remain distinct.
- Analytic static-surface reflection in CPU light does not trace that surface's own
  shading. An umbrella probe is therefore not a uniform photon-attenuation fixture.

The next carbon-budget change needs a single explicit substrate unit across seed
reserves, assimilation, maintenance and construction; a dry-mass conversion; and
charges for **accepted** growth, births and renewals in roots and shoots. Collision
rollback must refund rejected construction, with partial growth charged only once.
Stored carbon should remain available at night. Validate conservation and uniform
attenuation before recalibrating species. No arbitrary carbon cost or stronger light
exponent was introduced here.

### Resource-corrected seasonal measurements

All seven presets: seed 42, CPU light, March 1 2026, 180 days, one plant, clonal
initiation/activation disabled, foliage contacts off, hard soil retained, 1h and 4h.
Existing expansion scales remain fixed. Times below are conditional median days from
first observation to 90% intrinsic area; the reached/observed count includes censored
leaves. Carex sections are aggregated as whole blades.

| Preset | Median days, 1h / 4h | Reached / observed, 1h / 4h |
|---|---:|---|
| Geranium macrorrhizum | 14.17 / 16.25 | 12/16 / 12/16 |
| Geranium × cantabrigiense | 14.35 / 15.25 | 12/14 / 12/14 |
| Bergenia cordifolia | 99.67 / 117.58 | 10/20 / 14/20 |
| Heuchera micrantha | 28.50 / 27.75 | 14/14 / 14/14 |
| Campanula poscharskyana | 13.04 / 12.83 | 28/28 / 28/28 |
| Carex morrowii Variegata | 32.69 / 34.67 | 10/10 / 10/10 |
| Fragaria vesca Rügen | 19.85 / 21.58 | 24/24 / 24/24 |

The allocation-only intermediate comparison left Bergenia and Fragaria expansion
essentially unchanged. The complete corrections change the trajectories, but **do
not pass the biological light-response gate**. Bergenia still misses its provisional
84-day target; its 4h median is slower than the previous 87.33 days, with more leaves
reaching 90% (14 instead of 10), so the conditional medians represent different cohorts.
Flower petals appeared in the corrected spring Bergenia cases at days 69.125 and
76.167; this does not validate the archived September-start 755-day contact case.

Four additional umbrella runs covered Bergenia and Fragaria at both tick sizes.
Fragaria's 4h median leaf exposure over days 31–149 fell from 4,238 to 1,083 Wh/m²/day,
yet its 90%-area median fell from 21.58 to 15.75 days (all 24 leaves reached it in both).
Final above-ground stored energy fell from 216.21 to 67.66 custom units. Absolute light
is influencing the pool; it still does not impose a reliable construction budget.
Neither this shade response nor denser microscopic foliage counts as acceptance.

### Regression and compatibility checks

- 120 analytic/actual-Tick assertions: lamina width scaling, unchanged axis capacity,
  tiny organs, additive segment storage, absolute-pool conservation with relative
  allocation weights, reserve horizons/pruning at 1/4/24/48/200 hours, real root Tick
  volume/time response and accepted-radius maturation, leaf-only assimilation demand
  with water storage preserved for other organs.
- Eighteen final 180-day CPU cases above checked finite shoot/root resources and
  geometry. These establish numerical stability, not measured biomass acceptance.
- Repeating the final 4h Fragaria case produced identical daily biological records,
  leaf histories and exported scene bytes (timing fields excluded).
- 72 managed and 72 NativeAOT checks: version-8 roundtrip, explicit version-7 live
  rejection, byte-identical next-day checkpoints and exported frames at both tick
  sizes, V1 petiole controls retained, six sky samples/hour and finite CPU gathers.
- Core prescribed build and server build pass. No species-schema, scene-wire or
  frontend changes were made in this audit; legacy playback decoding was not rerun.
- Scoped `git diff --check` passes; the previously untracked calibration report was
  checked separately for whitespace errors.
- Live checkpoint version 8 changes continuation semantics without a state-layout
  change. V7/V8 scene playback and recorded assets remain unmodified.

Reproduction data, requests, intermediate ablations, final frames, build logs,
source hashes and this pass's source patch are in
`canopy-validation/resource-unit-audit/`. Temporary harnesses remain outside the
repository under `/tmp/gf-resource-audit/`. Run timings overlap other probes and are
not performance benchmarks. Repeat the explicit fixtures and both tick sizes before
future carbon-budget or leaf-expansion calibration changes.


## Construction-budget experiment

This pass adds a transactional debit controlled by
`SpeciesSettings.ConstructionEnergyPerDryGram`. **The default is zero in every
preset.** Positive values are experimental model energy units per gram of added
dry tissue, using the existing physiological volume and density proxies. They
are not grams of glucose per gram dry matter. Numeric request overrides and the
generated species editor expose the same field.

Continuous blade, petiole, shoot and root expansion now has an optional reserve
budget. Insufficient reserves reduce the proposed geometric increment. Shoot
expansion holds a temporary debit until collision resolution, then refunds the
rejected fraction, including complete rollback. Mechanical folding is not new
tissue. Settlement happens before developmental transactions and charges accepted
volume once. Root uptake maturation follows the funded radial increment.

Births and renewals pay for new tissue at commit. Resource transfers to children
remain transfers, separate from the cost. An unfunded shoot proposal remains
pending without setting a contact obstruction. Successful root branching
opportunities also survive a funding delay. Germination biomass remains an initial
condition; conversion of existing tissue to wood is not charged by this added-volume
budget. Those are explicit limits, not a closed carbon balance.

### Physical and botanical limits

The constructor cost literature defines substrate mass needed per dry biomass,
not a dimensionless growth accelerator. [Poorter et al. (2006)](https://academic.oup.com/jxb/article/57/2/355/489926)
provides construction-cost and light-environment evidence; its measurements cannot
be inserted directly into the simulator's custom `Energy` unit. The tested value
**1.5 is an engineering sensitivity value**, not a species measurement or a unit
conversion. [Poorter et al. (2014)](https://pmc.ncbi.nlm.nih.gov/articles/PMC3912841/)
studies whole-plant growth and carbon balance under irradiance treatments. Individual
leaf expansion times alone do not establish whole-plant carbon balance, and shade
responses can alter allocation and leaf structure.

A further source-level problem at that stage was `SeedAgent` germination: `seedReserve = Water_g`
passes a water mass into the initial organs' energy reserves. Soil water uptake can
therefore alter the initial energy allocation. The follow-up below fixes that coupling.
This construction-budget pass did not guess a new seed
carbohydrate mass. Assimilation, volumetric maintenance and reserve mass also need a
consistent physical unit contract. Stored reserves are not currently capped. The
construction debit closes one accounting path; it does not repair all of these.

### CPU seasonal sensitivity, 180 days

Seed 42, March 1 2026, CPU mode 3, 1h/4h ticks, clonal spread disabled, foliage
contacts disabled, hard terrain constraints retained. Existing provisional
`LeafExpansionRateScale` values are held fixed, as are dimensions, growth times
and crown probabilities. The primary matrix has 14 open cases and four umbrella
shade cases, each with construction cost 1.5. Times below are conditional medians
among leaves reaching 90% intrinsic area; the denominator includes censored leaves.
Carex sections are grouped into whole blades. These are sensitivity results,
**not replacement calibration targets**.

| Species | Open 90% days, 1h / 4h | Reached / observed, 1h / 4h |
|---|---:|---:|
| G. macrorrhizum | 15.98 / 14.92 | 12/14 / 12/14 |
| G. × cantabrigiense | 13.83 / 15.08 | 12/14 / 12/14 |
| Bergenia | 107.79 / 114.08 | 13/20 / 12/20 |
| Heuchera | 29.60 / 28.08 | 14/14 / 14/14 |
| Campanula | 12.83 / 11.75 | 28/28 / 28/28 |
| Carex | 36.71 / 32.83 | 10/10 / 10/10 |
| Fragaria | 18.81 / 18.75 | 24/24 / 24/24 |

Under the umbrella, Bergenia reached 90% in 86.83/90.33 days (17/20 and 15/20),
and Fragaria in 15.71/15.33 days (24/24 each). This does not establish the intended
carbon-limited response. The umbrella also changes directional light and does not
provide uniform irradiance attenuation. A separate 18-case matrix at expansion
scale 1 is retained as an uncalibrated sensitivity experiment, not mixed into this
table. With the cost disabled and the existing Fragaria scale 1.8 restored, the
180-day 4h control reproduces the preceding resource-audit run's leaf histories,
daily biological records and final scene bytes exactly (timing fields excluded).

### Regression and compatibility acceptance

- 298 focused assertions cover full/partial refunds, tiny blades, petals, support
  organs, no overdraft, reindexing, and root birth transfers. Production pending
  shoot births remain unfunded without obstruction, then commit once. Pending
  funding and root branch intent survive a checkpoint; funding after restore
  produces identical serialized state.
- 72 managed and 72 NativeAOT integration assertions cover immediate roundtrip,
  byte-identical next-day states and exported frames at 1h/4h, retained V1 rod
  controls, CPU gathers, and six solar positions/hour. Older v8 live states are
  rejected explicitly.
- Live checkpoint **v9** stores the changed agent layouts. Scene records and old
  playback assets are unchanged. Existing frontend decoding, transform parity
  and cache suites pass: 20 tests. Frontend typecheck passes.
- Core prescribed build and server build pass with existing warnings. No running
  server or stored request was modified. Experimental cost remains zero by default.

Keep these refund, retained-birth, root-transfer and disabled-control scenarios as
regressions when changing resource accounting. Before default enablement, establish
physical assimilation/maintenance/seed-reserve units and test whole-plant carbon
balance alongside matched light treatments. No contact, V1 subdivision or V2
acceptance gate is claimed by this accounting experiment.

Requests, measurements, final scenes, checks, source hashes and the source delta
from this pass are archived under `canopy-validation/construction-budget/`.
Temporary diagnostic harnesses remain outside the repository.


## Seed-budget and maintenance follow-up

### Changes enabled by default

`SeedAgent.Energy` is now separate from `Water_g`. Water delivery cannot create
energy. The optional constructor `reserveEnergy` is finite, nonnegative and in
**custom model energy units**. Its omitted default is fixed from the initial
radius using the old nominal budget formula, `500 * (100 * radius³) + 100 * radius`.
It no longer includes hydration overshoot or changes with subsequent water uptake.
The legacy constructor argument named `energy` still means initial water, for
source compatibility; no request JSON fields changed.

This default is a compatibility-scale estimate, not a measured biochemical seed
reserve. Initialization currently samples a generic propagule radius up to 2 cm;
it is not species-specific seed morphology, and should not be interpreted as a
Bergenia seed mass or as the age of the photographed nursery plant. Dormant-seed
respiration, physical carbon conversion and species-specific initial reserves
remain uncalibrated. Existing empirical hydration loss and temperature response
are retained; this does not establish realistic germination dates.

The swelling proxy now uses `delta(radius³) = 3 * water_g / (4π * 1e6)`, with water
density in g/m³. The previous coefficient added 100 times that volume. This is an
additive-volume approximation; real imbibition also involves tissue deformation
and trapped gas. [Leopold, volumetric components of imbibition](https://pubmed.ncbi.nlm.nih.gov/16663281/)
and [Koizumi et al., soybean MRI observations](https://pubmed.ncbi.nlm.nih.gov/18565982/)
provide experimental context, not a fitted Bergenia swelling coefficient.

Uptake requests are capped at the remaining hydration threshold. Already hydrated
seeds germinate at the next tick boundary before another interval of hydration loss,
so the cap cannot trap them perpetually just below the threshold. Delivered water
is retained, including deliberately excessive deliveries in the diagnostic fixture.
At germination, water and energy transfer separately using the existing 30% root,
30% rhizome, 40% aerial partition. These fractions remain provisional model values.
Petiole and blade helpers already divide each organ's allocation across segments;
the seed now counts biological organs when allocating the aerial share. The prior
segment-dependent denominator lost resources for subdivided petioles.

The physiological distinction is supported by experiments on stored-carbon
mobilization during establishment, including [Penfield et al. (2004)](https://pmc.ncbi.nlm.nih.gov/articles/PMC520966/).
Those Arabidopsis results support separating hydration from stored substrate;
they do not supply a reserve value for Bergenia or the other six presets.

### Root deletion exposed by the corrected startup

The first matrix exposed complete root-system loss in 4h G. × cantabrigiense and
Fragaria. Tracing showed adequate whole-plant reserves, but allocation gave some
parent roots exactly their maintenance requirement. After subtracting maintenance,
`Energy == 0` triggered death and removed their still-funded descendants. This was
not an energy deficit. Roots now die on an actual shortfall (`Energy < 0`); an
exactly funded root rests without further expansion. No reserve multiplier or
species growth-rate adjustment was used to hide the failure.

In the 180-day comparison, G. × cantabrigiense changed from zero developed leaves
and zero surviving roots at 4h to 12 developed leaves and 156 roots. Fragaria changed
from zero developed leaves/roots to 24 developed leaves and 154 roots. These are
mechanism regressions, not botanical root-count targets.

### Final CPU probe results

Seed 42, March 1 2026, CPU mode 3, 180 days, clonal spread disabled, hard terrain
constraints retained and foliage contacts disabled. Existing expansion-rate presets
and dimensions are unchanged. All seven species have finite recorded resources.
Both tick sizes germinate after two numerical ticks (2h / 8h); this is tick-boundary
quantization, not demonstrated biological germination speed.

| Species | Conditional median days to 90% area, 1h / 4h | Reached / observed, 1h / 4h |
|---|---:|---:|
| G. macrorrhizum | 14.67 / 16.25 | 12/16 / 12/16 |
| G. × cantabrigiense | 14.08 / 14.08 | 12/14 / 12/14 |
| Bergenia | 87.58 / 118.33 | 10/18 / 13/20 |
| Heuchera | 28.69 / 28.08 | 14/14 / 14/14 |
| Campanula | 12.10 / 12.83 | 28/28 / 28/28 |
| Carex | 31.00 / 37.42 | 10/10 / 10/10 |
| Fragaria | 19.88 / 17.92 | 24/24 / 24/24 |

Four umbrella cases and four construction-cost sensitivity cases supplement the
14 open cases. The shade cases still do not establish the desired light-response
calibration. Construction cost 1.5 is especially sensitive to startup: Fragaria
fails to develop mature leaves at either tick size. The cost therefore remains
zero by default. These probes do not establish seasonal dense-contact or V2
acceptance; the remaining Bergenia timestep difference is recorded, not tuned away.

### Persistence and validation

Live checkpoint v10 includes the seed energy pool and rejects v9 explicitly.
Pre-germination state can also be restored into a world that has already germinated.
The test exposed stale CPU-radiosity history: restoring an empty history did not
clear the old buffer tail. Restore now clears that history; serialization omits
unused trailing radiosity slots so spare capacity cannot change saved bytes.
Scene playback formats and archived mesh assets are untouched.

- 97 focused assertions cover hydration-independent reserves, water-volume units,
  germination conservation across subdivisions and both ticks, invalid reserve
  input, and exact/insufficient maintenance with dependent roots.
- The preceding 298 construction/refund/transaction assertions still pass.
- 76 managed and 76 NativeAOT checks cover pre-germination rewind, subsequent
  continuation, v9 rejection, byte-identical next-day states and scene frames,
  retained V1 controls, CPU gathers and six solar positions/hour.
- Prescribed core and server builds pass with existing warnings. Scoped diff and
  untracked-report whitespace checks pass. No frontend or playback-wire code changed.

### Remaining water-ledger defect

An isolated default-soil diagnostic requested and delivered 3.125 g from a cell
holding 12.5 g. The cell still held 12.5 g afterward. Seed energy correctly stayed
at 0.53980684 model units, but soil water was not debited. The regular and imported
soil request paths need a separate conservative withdrawal correction, including
scarce-water sharing and virtual-soil storage. This pass records the defect; it does
not claim a closed soil-to-plant water balance. Fixing the seed does not solve the
remaining assimilation/maintenance unit contract either.

Final and pre-root-fix ablations, requests, frames, diagnostics and source evidence
are archived in `canopy-validation/seed-budget/`. Harnesses remain under
`/tmp/gf-seed-budget/` outside the repository.
