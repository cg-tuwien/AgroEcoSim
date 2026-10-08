# Experimental carbohydrate accounting

`SpeciesSettings.ConstructionEnergyPerDryGram > 0` selects this accounting. Zero
keeps the existing model-energy accounting and all predefined presets still use
zero. The public JSON field name and requested numeric values are unchanged.
The positive-cost path remains experimental; the acceptance failures below
prevent enabling it as a default.

## Unit contract

- One energy unit is **one gram of glucose equivalent**. `GetIrradiance` supplies
  Wh/m² accumulated over the biological tick, so a leaf with intrinsic one-sided
  area `2 × Length × Radius` produces
  `irradiance × area × 0.005 g glucose/Wh`, limited by available water. The
  coefficient is the pre-existing empirical light response, now expressed as
  1.389 g/MJ of incident shortwave energy; it is **not** a measured quantum yield,
  nor a Bergenia or cultivar calibration. The CPU tracer's actual integrated
  irradiance enters this equation. The allocation system then distributes the
  plant's conserved absolute carbon pool using relative organ weights.
- The net photosynthetic reaction consumes six moles of water per mole of
  glucose, i.e. `108/180 = 0.6 g water/g glucose`. This is a *chemical* debit.
  The simulator does not yet model transpiration or stomatal conductance from
  this coefficient, so it must not be interpreted as total plant water use.
  Nonphotosynthetic axes do not request photosynthetic water.
- Construction charges `new physiological volume × dry-tissue density ×
  ConstructionEnergyPerDryGram`. The setting is g glucose/g dry tissue and
  includes synthesis/growth respiration. Expansion and accepted births are
  charged once; growth rolled back by contact gets the corresponding debit
  refunded. Existing seed reserve and initial germination tissue are initial
  conditions, not newly created biomass. Their inherited numeric reserve is
  interpreted as glucose equivalents but has not been measured from seed mass.
  Mechanical folding changes neither volume nor the construction bill.
- Maintenance is provisionally `0.015 g glucose/(g dry tissue·day)` at a
  nominal 25 °C, multiplied by dry tissue mass and tick hours. The rate is not
  temperature-adjusted and is not measured for any of these cultivars.
  [Penning de Vries (1975)](https://edepot.wur.nl/218532) reports approximately
  8–60 mg glucose/(g dry matter·day) for maintenance at 25 °C. The roughly 30%
  whole-plant respiration/assimilation ratio reported by
  [Pons and Poorter (2014)](https://www.frontiersin.org/journals/plant-science/articles/10.3389/fpls.2014.00012/full)
  includes growth respiration and must not be copied into maintenance in
  addition to the construction cost.

[Poorter et al. (2006)](https://academic.oup.com/jxb/article/57/2/355/489926)
report a broad 1.2–1.8 g glucose/g dry leaf mass construction range and state
that growth respiration is already included. The 1.5 g/g probe setting below
is a physically plausible sensitivity case, **not** a calibrated constant for
all blades, petioles, stems and roots. The physiological 0.1 mm leaf-thickness
proxy and preset dry-tissue densities are also not cultivar measurements.

## Seedling carbon routing

New shoots and root tips can be nearly massless before they reach light or
water. Allocating surplus only by their present volume left the March Bergenia
4-hour seedling with a buried 10 µm crown and zero CPU irradiance by day 10;
it could never finance emergence despite having an initial seed reserve.
The positive-cost path gives an expanding blade, petiole or crown axis an
allocation weight based on *remaining target construction cost per existing
nominal growth hour*. A root tip uses its remaining sampled target length at
its **current radius** and the existing 0.2 mm/h axial coefficient; no adult
root diameter is invented. These weights redirect already available carbon,
they do not mint it. Actual growth still pays the construction debit and keeps
the same water, contact and support limits. The root weight matters because a
microscopic root that receives only maintenance cannot grow enough uptake
surface to supply a lit crown.

## Reproducible acceptance evidence (seed 42)

All runs used the internal CPU light solver, one plant, 1 × 1 × 1 m field,
0.1 m soil resolution, a randomly buried initial seed, 1 March 2026 start,
`SoftCollisions=false`, eight light samples, and stock presets. They retained
the presets' clonal-spread settings; leaf counts are whole-plant counts, not
leaves per crown. The temporary probe lives outside the repository under
`/tmp/greenfdt_resource_probe`.

| Positive cost 1.5 g/g, day 30 | 1-hour leaves | 4-hour leaves | Gate |
|---|---:|---:|---|
| Bergenia cordifolia | 2 | 8 | Emerges at both ticks; divergent early branching |
| Geranium macrorrhizum | 2 | 2 | Emerges; small blades |
| Geranium × cantabrigiense | 2 | 8 | Emerges; strong tick difference |
| Heuchera micrantha | 2 | 6 | The 4-hour crown survives; 1-hour blades plateau at 8.2 mm |
| Campanula poscharskyana | 20 | 16 | Both crowns survive; no blade exceeds 5 cm yet |
| Carex morrowii 'Variegata' | 48 | 64 | Emerges; segmented blades below 5 cm at day 30 |
| Fragaria vesca 'Rügen' | 24 | 24 | Both crowns retain foliage and expand blades |

The matched zero-cost 4-hour controls have 14 Heuchera, 46 Campanula and 24
Fragaria leaves at day 30. Before the warning-policy correction, the positive-
cost runs lost all Fragaria foliage near day 11, Campanula at day 22 and
4-hour Heuchera at day 23 while carbon, water and CPU light were still present.
`PlantFormation.Census` had been deleting its least efficient above-ground
organ as soon as the pool fell below an alert of at least **28 days of maintenance**.
Deleting a support recursively deleted its illuminated descendants. In the
positive-cost mode the alert now redistributes the existing carbon by maintenance
requirement without deleting tissue; the seven-day emergency still permits
shedding. No carbon is created. The zero-cost **alert** branch is unchanged; the
separate seven-day emergency time-normalization correction applies at both
cost settings. The debit paths were checked for a repeated tick multiplier or duplicate construction
charge, and none was found.

At day 60, positive-cost Fragaria retains 24 leaves at both tick sizes,
Campanula 28 at both, and Heuchera 2/14 at 1-hour/4-hour ticks. The 1-hour
Heuchera case remains stalled at an 8.2 mm largest blade. Its whole-plant
carbon pool stays around 0.41–0.47 g through day 95, then root radial growth
consumes it: four roots become six, the leading root radius rises from 1.67
to 3.75 mm, and the lower-bound root dry-mass estimate rises from 0.0465 to 0.2887 g
from day 95 to 100. This estimate uses the minimum configured dry-tissue
density of 200 kg/m³, so at 1.5 g glucose/g it implies **at least 0.363 g**
construction cost against 0.467 g available on day 95. The emergency then
removes 18 above-ground organs, including both leaves, by day 100. Maintenance
is paid throughout, but is not the dominant five-day drain. The present root
radius heuristic and local funding can spend most of the whole-plant reserve
before the aerial crown establishes. A global source–sink and reserve budget
needs separate validation; changing a species coefficient would hide this
failure. The alert correction does **not** validate all-species carbon
calibration or authorize default construction cost. The alert and emergency
reserve durations are numerical policies, not measured cultivar tolerances.

For Bergenia at day 90, the positive-cost unshaded case has 14 leaves and
12 blades longer than 5 cm at 1-hour ticks, and 14/12 at 4-hour ticks.
The 4-hour shaded case remains watered after adding the root sink (26 roots,
37.8 g above-ground water, 13.60 g above-ground dry mass versus 35 roots,
38.2 g water and 14.08 g dry mass unshaded). Before this root routing change,
one 1.7 mm root supplied the shaded plant at day 45 and water collapsed.
However, at 1-hour ticks the shaded day-90 plant built **more** above-ground
dry mass than the unshaded plant (20.46 versus 12.90 g) because it initiated
more foliage and later intercepted more total light, despite less irradiance
per leaf. This fails a simple monotonic-shade acceptance gate. The response
cannot yet be called a calibrated light-growth relationship.

Leaf expansion itself is not required to decrease monotonically with shade:
[Pantin et al. (2011)](https://pmc.ncbi.nlm.nih.gov/articles/PMC3177277/)
found that metabolic and hydraulic limitation change with leaf age and that
some low-light treatment did not slow early expansion. Carbon conservation,
finite water, emergence and developed biomass are the stronger engineering
checks here. All seven presets still need CPU-light, cultivar-aware seasonal
calibration before a positive cost becomes their default.
