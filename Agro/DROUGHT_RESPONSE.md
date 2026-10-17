# Herbaceous drought response

Status 17 October 2026. A herbaceous plant that runs short of water now grows less, then commits
part of its existing foliage to senescence at a species-specific pace, lets the dead leaves hang
for a while and sheds them, and regrows leaves from the bare nodes once conditions allow. Before
this, the same plant kept growing at full speed until its tissue water reached zero and then lost
almost every organ in a single tick.

`SpeciesSettings.CanopyAdjustmentHalfTimeDays = 0` switches the whole response off and restores
the previous rules exactly; that is the A/B switch. Everything below applies to
`Behavior.Herbaceous` only. Trees (`TickDefault`) are untouched.

## The incident, and what it actually was

Run `759ee89b` (one *Campanula poscharskyana*, 10 x 10 m field at 5 cm, 4 h ticks, start
1 February) lost 1,490 of 1,584 organs on 24 May. The probe reproduced it (606 leaves in one
tick on day 112). It was **not** the seven-day emergency branch of `PlantFormation2.Census`:
`EnergyBalance` stood at about 1.9 million ticks of maintenance. The chain was:

1. Rain percolated below the 5 cm root cells within one 4 h tick (see "Soil" below), every root
   cell sat at 0 g from the second week on, and the plant's tissue water drained from 0.55 to 0
   between 28 April and 22 May while it grew from 280 to 600 leaves.
2. `Photosynthesize` returned before booking light when a blade had no water, so after a dry day
   532 blades reported zero resources. Their storage-allocation weight became zero against the
   few that still had water.
3. They received exactly their maintenance, ended the next tick at `Energy == 0`, and the
   herbaceous `Energy <= 0` rule deleted them. Zero-maintenance green axes went the same way, and
   a starving petiole fell through to the leaf case and deleted its own node with the crown above.

Commit 559d9a6 removed that mechanism for every plant, response or not (light is booked
regardless of water, `Energy < 0` is the rule, `LeafUnitRoot`). The plant then survived the
drought - but stayed green and inert at zero water for as long as the soil was dry, which is what
the response below is for.

## Model

Shared processes and three species parameters, following the separation of growth restriction,
senescence, death and detachment in the literature (Manzoni et al. 2015; the pea senescence and
Norway spruce carbon-starvation experiments; *Quercus ilex* canopy adjustment through reduced
production; the *Geranium macrorrhizum* ornamental drought trial with 60 % lower stomatal
conductance and 53 % less final green leaf area at acceptable appearance). The equations are an
empirical approximation, not a published law.

### Signals (every census, `PlantFormation.Canopy.cs`)

- **Water.** Plant fill `F = allocatable water / tissue storage`, filtered with a 48 h time
  constant (`1 - exp(-h/48)` per tick, the filter first and the clamp after, starting at 1), then
  `D = clamp(1 - F_f / 0.9)`. `0.9` (`WellWateredFill`) is just below what every species held
  with the weather overridden to 5 mm of rain a day: 0.94-0.99 at 1 h and 4 h ticks over 120 days.
- **Carbon.** `c = clamp(1 - reserve hours / 168)`: the old seven-day emergency horizon is now a
  warning that stops new construction. In the default energy accounting (`ConstructionEnergyPerDryGram
  = 0`) only blades and rhizomes pay maintenance and the pool holds months of it, so `c` is almost
  always zero; it matters under construction accounting.
- **Establishment.** `D` is held at zero for the first 336 h after germination. Every species'
  fill dipped to 0.2-0.5 for a few days while the seed's water moved into tissue that grew faster
  than its first roots could fill it, and Carex lost two of its four seedling blades to an episode on
  day 5 of a run that never saw a drought again. Seedling drought is not modelled.
- **Episode.** Starts when `D` exceeds `DroughtSenescenceOnset`, ends after `D` has stayed at or
  below `max(0, onset - 0.05)` for 72 h (an onset under the hysteresis could otherwise never end). At the start the green, uncommitted leaf area `A_ref` is recorded and held
  for the whole episode; re-reading the shrinking canopy every day would compound to zero.

### Growth restriction

Every herbaceous expansion rate is multiplied by `(1 - D)(1 - c)` (`GrowthWaterFactor` replaces
`min(1, WaterBalance)`; scape and floral meristem growth gain the factor too). Stomatal regulation
cuts each blade's light-limited assimilation by `1 - 0.7 D`, which also slows the water it uses.
During an episode, while the green area is at or above the target, and while carbon is short
unless no green leaf is left at all, no new leaf-bearing organ starts: crown continuation, crown-bud activation, leaf renewal, node-bud regrowth and flower
commitment are refused after their random draw (so a plant without an episode draws exactly the
same sequence), and staged developments whose children include leaves wait.

### Target canopy (daily, after `AG.NewDay`)

```
s       = clamp((D - onset) / (1 - onset))
target  = A_ref (1 - s)
dA      = max(0, A_green - target) (1 - 2^(-dt / T_half)) + carry
```

`dt` is the time since the last daily update, so the exponential is exact for a fixed target at any
tick length. Whole blades (all segments of a segmented blade, plus their petiole) are committed in
order of `min(1, age / LeafMaximumAgeHours) + (1 - relative light)`, ties by stable id, while the
remaining quota is at least half the next blade's area; the remainder carries to the next day,
bounded by one blade. Under carbon shortage, blades whose own daily production is below their own
daily maintenance are shed at the same half-time, first in the order, and no area target is set:
shrinking the canopy to save carbon removes the producers and deepens the shortage.

### Leaf lifecycle

- **Senescing** (`LeafSenescenceStressDuration` 140 h ± 60, the previously unused setting):
  maintenance, assimilation forecast, energy and water storage all scale with the living fraction
  `1 - senescence`, so the census neither funds nor fills dying tissue. No growth, no renewal, no
  energy death. The exported colour follows the existing senescence curve, green to yellow to brown;
  the wire format is unchanged.
- **Dead attached** for `DeadLeafAttachmentHours` (default 336 h): zero maintenance and assimilation,
  still occludes light, still collides, not counted as a leaf in `NewDay`.
- **Detached** in two phases by the leaf's owner (first petiole segment): its children are removed,
  and on a later tick the bare segment becomes a **node bud**. Shortening a petiole that still
  carried its blade moved the dying blade inside the same collision pass. The conversion waits
  while the organ is growth-blocked (the blocked-growth restore would undo the shortening) and drops
  the petiole's contact deformation at once (a stored deformation of a bud fails the resume check).
- **Node bud regrowth**: a bud on a non-rhizome stem or meristem, at least 72 h old, outside
  `ResetPending`, regrows a petiole and blade (hazard one per week) through `RenewPetiole`, using the
  shed petiole's rest frame. It gets an allocation efficiency of 1, or it could never fund the leaf.

### Energy without deletion

Under the response nothing is deleted for energy. Below maintenance the plant shares what it has in
proportion to maintenance alone (`DistributeEnergyByMaintenance`); the old requirement weighting
multiplied by production efficiency and handed zero to every green axis, every root that absorbed
nothing and every blade with a dry day. A uniform share below one is still short for every
maintained organ, so an organ (or root) the census funded below its maintenance ends the tick at
zero instead of negative and is kept - otherwise the whole root system and every non-producing
blade would go in one tick, which is the mass death this replaces. After 336 h of unpaid
maintenance the plant reverts its least productive crown to its crown bud, at most one a day, in
the rhizome's frame and rebased at once. Roots and rhizomes are never removed this way. Measured
with `ConstructionEnergyPerDryGram = 1.5` and no rain from 1 April: the canopy senesced by June,
the pool ran out in September, one crown reverted, both roots survived, no organ died of resources.

### State

Leaves reuse `SenescenceStart`/`SenescenceDuration` (already copied by `RetainPhysiology`); the
organ layout is unchanged. The plant's filter, episode, reference and target area, carry,
starvation hours and last controller step are checkpointed after its water budget, and the tray
survey gained `deadLeafArea_m2` - live checkpoint 35.

## Parameters

| Setting | Default | Meaning |
| --- | --- | --- |
| `DroughtSenescenceOnset` | 0.3 | Stress at which leaves start to senesce. Growth slows below it already. |
| `CanopyAdjustmentHalfTimeDays` | 7 | Days to commit half the excess above the target. **0 = response off.** |
| `DeadLeafAttachmentHours` | 336 | How long a dead leaf hangs before it falls. |
| `LeafSenescenceStressDuration` (± Var) | 140 h (± 60) | Senescence duration (existing setting, now used). |

All seven presets use the defaults. Two parameters are deliberate: one would force "starts early"
and "responds quickly" to vary together. Species differences should be fitted only where
observations separate them (reduced production, senescence, detachment and regrowth are different
signals in façade photographs); the shared constants (`WellWateredFill`, the 48 h filter, the 0.7
stomatal sensitivity, the 168 h carbon warning, the 336 h starvation tolerance, the weekly node-bud
hazard) are engineering settings, not measured tolerances.

## Measurements

**The incident request** (run 759ee89b, 4 h ticks, 200 days, probe mode A):

| | old rules | drought response |
| --- | --- | --- |
| largest loss of leaves in one tick | 606 (day 112), then 2 leaves left | 3 |
| leaf organs 2 May / 1 June / early Aug | 324 / 2 / 48 (stored run, 3 Aug) | 174 / 137, of them 60 green, 22 senescing, 55 dead / 362 (10 Aug) |
| resource deaths | 1,515 | 0 |
| node buds at the peak | - | 48, regrowing after the 30 May rain |

The plant stops adding leaves when the episode starts (2 May; a short one in early April ended after
ten days), sheds towards a target that falls to about half its early-May canopy by the end of May, and regrows through node buds and new crowns in
June. With `CanopyAdjustmentHalfTimeDays = 0` the per-tick water, energy, organ and death counts over
960 ticks are identical to the build before the response.

**Every species, 730 days** (seed 42, 4 h, render mode 3, 1 x 1 m field at 5 cm, start 1 March,
probe mode C), old rules against the response:

| species | height m | width m | leaves | crowns | bloom, year 1 / year 2 | days with D > 0.3 |
| --- | --- | --- | --- | --- | --- | --- |
| Bergenia | 0.43 / 0.44 | 0.94 / 0.96 | 60 / 94 | 3 / 5 | 05-13..06-25, 04-08..06-20 / 05-08..06-19, 04-06..06-16 | 2 |
| Campanula | 0.25 / 0.26 | 0.95 / 0.93 | 2456 / 2400 | 58 / 59 | 06-20..10-14, 06-20..10-18 / 06-20..10-17, 06-20..10-21 | 6 |
| Carex | 0.47 / 0.46 | 0.82 / 0.83 | 4448 / 4496 | 56 / 56 | 04-12..06-06 / 04-12..06-13 | 2 |
| Fragaria | 0.25 / 0.27 | 0.46 / 0.41 | 640 / 1048 | 5 / 9 | 05-23..11-29, 05-22..12-04 / 05-23..11-20, 05-22..12-03 | 0 |
| G. macrorrhizum | 0.42 / 0.42 | 1.08 / 1.11 | 1046 / 1016 | 100 / 96 | 05-26..08-09, 05-06..08-20 / 05-26..08-25, 05-06..08-11 | 0 |
| G. x cantabrigiense | 0.26 / 0.25 | 0.70 / 0.71 | 622 / 558 | 58 / 52 | 05-24..08-28, 05-24..09-04 / 05-26..08-26, 05-24..09-01 | 3 |
| Heuchera | 0.58 / 0.59 | 0.63 / 0.61 | 142 / 142 | 10 / 10 | 06-17..08-17 / 06-16..08-08 | 6 |

In this setup the 1 m deep field keeps its water and the response is nearly idle; the differences are
the divergence of two runs whose growth rates differ slightly from the start (the stomatal and growth
factors respond to small `D` too), not drought losses - Fragaria never entered an episode. No species loses more than 8 % of its living leaves in a day.

**Parallel against sequential tick**: 36 plants of all seven species, 100 days at 4 h, identical
frames.

**Carbon starvation** (`ConstructionEnergyPerDryGram = 1.5`, Campanula, no rain from 1 April, 240
days): the canopy senesced by June, the pool ran out in September, one crown reverted to its bud,
both roots survived, and no organ died of resources.

## Soil: what drives the stress in a box field

The signal is only as good as the plant's water supply, and in the regular voxel soil that supply is
dominated by a tick-length artefact, measured on the incident request:

| ticks | root cells dry | plant fill in May | drought episode |
| --- | --- | --- | --- |
| 1 h | 0 of 29-95 | 0.98-0.99 | never |
| 4 h | all, from week 2 | 0.0-0.3 | from mid-April |

`GravityDiffusion` moves a cell's water `(int)(WaterCellsPerStep + min(1, fill))` cells down per
tick, with `WaterCellsPerStep = 1.2 cm/h x hours / cell height`. At 1 h and 5 cm cells that is
`(int)(0.24 + fill)`: nothing moves until a cell is three-quarters full, so rain stays in the root
cells. At 4 h it is `(int)(0.96 + fill)`: almost any water moves a cell down every tick, past the
5 cm root cells before the roots ask for it. Each root also reads only one soil cell and requests at
most its own free storage per tick. Until the soil task fixes transport and uptake, a box-field run
at 4 h ticks is in drought for most of a dry spring, and the response reacts to that faithfully -
so do not calibrate the onset or half-time against box-field runs at 4 h ticks. Imported trays use a
pooled tray water model without this transport artefact; the per-tick root refill cap applies there
too.

The same cap makes the signal itself depend on tick length when water is plentiful: with 5 mm of rain
a day the fill was 0.94-0.99 at 1 h and 4 h ticks but 0.69-0.97 at 24 h, so a well-watered plant at
24 h ticks reads `D` up to 0.23 - growth cut by up to 23 % and assimilation by 16 %, below the default
onset. At 48 h it can cross the onset. Normalising root uptake to the tick (a root requesting its
share of the tick's demand on top of its free storage) belongs to the soil task.

## Verification

- Probe (outside the repository): `/tmp/greenfdt-drought-probe` - mode A replays a stored request,
  B overrides the weather for a wet or dry control, C runs the 730-day habit metrics, D compares the
  parallel and sequential ticks.
- `Agro --checkpoint-selftest` has two Campanula scenarios in a rainless 1 x 1 m field at 5 cm,
  540 steps at 4 h, resumed at 1, 135, 270 and 539 steps on every engine, plus a `BroadLeafV1`
  variant: all frames identical. The drought commits, kills, sheds and regrows node buds inside that
  window.
- Feature off (`CanopyAdjustmentHalfTimeDays = 0`): the incident request's per-tick water, energy,
  organ and death counts over 960 ticks are identical to the build before the response.

## Open

- Soil transport and root uptake (above) - the largest single effect on when the response fires.
- A seedling that meets drought holds its tiny reference canopy for the whole episode; the memo's
  alternative, a potential canopy from the structural growth model, would let it develop.
- No acute dehydration injury path; no summer dormancy rule; no functional-group presets.
- The weather planner always starts with January's statistics, whatever `InitialDate` is.
