# Soil water budget and regression scenarios

Water requests are in grams. Both soil implementations now subtract a delivered
amount from the same water store that supplied it. A request is eligible only if
its seed or root can receive the message; failed deliveries leave the soil
unchanged. If valid requests exceed available water, they share that water in
proportion to demand. Each accepted float-valued delivery is bounded by the
remaining store, including float-rounding cases. This is a local transfer
invariant: **soil debit = water delivered to recipients**. When several plants
request the same cell in parallel, arrival order may change low float bits of
individual grants; use deterministic scheduling for byte-identical comparisons.

`SoilFormationRegularVoxels` keeps its live per-cell water balance in double
precision. This matters when a root takes much less than one float unit in the
last place (ULP) of a large cell. `GetWater_g` still returns a float for API
compatibility, so one query can round away a small real debit. Check the
double-precision balance or compare aggregated transfers when testing amounts
below a query ULP. Request lists are created only for cells that actually receive uptake
requests; a large empty field no longer allocates one list and hash slot per voxel.
Vertical diffusion now visits each column and limits each
transfer to both the source's remaining budget and the destination's free
capacity. It leaves water in the source when the destination is full. Fields
with at least 256 columns transport independent columns in parallel. The
AgroWorld scheduler finishes soil transport before parallel plant ticks, then
delivers soil water before parallel plant inbox processing. Uptake does not
race a changing soil balance.
Parallel plant request order can still change low bits of individual grants.

Imported tetrahedral soils currently use `DoVirtual = true`: one shared water
pool per named soil object, with rain collected over that object's catchment.
The pool is double precision and is debited by seed and root deliveries. The
per-tetrahedron finite-water branch also has a double-precision live balance,
source-normalized diffusion weights and bounded source-to-destination
transfers. That finite branch was exercised in focused fixtures, but is not the
active imported-tray model.

Live checkpoint version **14** records the double-precision regular cells,
the imported soil's live virtual pool (or finite cells), and each plant's
`UnallocatedWater_g` and `WaterDemandPerTick_g`. The latter two are doubles
written after `EnergyProductionMax` in the raw plant state. Restore requires
the same request and soil geometry. Older live checkpoints are explicitly
rejected because continuing them under the corrected water budget would change
the simulation. Their already-recorded scene playback remains readable.

## Reproducible checks

Use seed 42 and repeat changed behavior at 1-hour and 4-hour ticks. Temporary
probe projects belong under `/tmp`, not in the repository. The focused fixture
used two competing seeds in a depleted cell, followed by root withdrawals and
checkpoint continuation:

| Case | 1-hour and 4-hour result |
| --- | --- |
| Regular cell competition | 12,500 g initially; 12,500 g delivered in total; 0 g remained. |
| Imported virtual-pool competition | 260.4167 g initially; 260.4167 g delivered in total; 0 g remained. |
| Root delivery | An additional 2 g transferred from the selected regular cell or imported pool. |
| Checkpoint continuation | Restored and uninterrupted states were byte-identical for each soil model and tick size. An older live checkpoint was rejected. |

Before the plant-water capacity change, a growing one-plant Bergenia CPU-light
case restored at day 30 and continued through day 60 byte-identically at both
tick sizes, comparing a checkpoint after every resumed day (33,762 bytes at
1 hour; 32,882 at 4 hours at day 60). Repeat this case for version 14 before
claiming identical long-run continuation under the new reserve.

A separate 256- and 1,024-column regular-soil fixture compared complete serialized
states after each of 16 alternating rainy/dry ticks. Serial and parallel states
were byte-identical at both tick sizes, as were two independent parallel runs.
On a fixed 200 × 80 × 200 soil grid, 12 measured ticks after warmup gave median
soil-tick times of 17.97 to 4.29 ms (1 hour) and 27.73 to 4.77 ms (4 hours)
for serial versus column-parallel transport. Three unrelated seasonal runs were
using cores during this measurement; these are concurrent-load timings, not a
claim about full-run speed. The temporary fixture is
`/tmp/gf-soil-parallel-verify/SoilParallelVerify.csproj`.

An independent world-scheduling fixture compared complete checkpoints after
each of 120 sequential versus explicit parallel ticks on a 256-column field.
One-plant and four-plant cases matched byte for byte at both 1-hour and 4-hour
ticks. Separate 240-tick four-plant parallel runs finished with finite,
nonnegative water in all 2,304 cells. Its temporary project is
`/tmp/gf-world-barrier-verify/WorldBarrierVerify.csproj`. This does not imply
all large parallel stands are deterministic: request arrival order can differ
when many plants share a cell. In a four-seed same-cell stress fixture, two of
twelve 4-hour parallel attempts diverged from sequential execution by at most
about 1.4 × 10⁻⁹ g in one cell. Replaying only the soil tick from the identical
pre-step checkpoints was byte-identical, locating that difference after
transport, in concurrent plant request ordering. This does not affect the
soil-before-plants race fix, but byte-identical parallel shared-cell
continuation remains an open scheduling issue.

For a small-withdrawal regression, queue 100 requests of 0.0001 g against a
regular cell holding about 12,500 g. Its double-precision balance must fall by
the sum of accepted receipts, about 0.01 g. The public float query can show a
different rounded difference (0.009765625 g in the fixture). A checkpoint
after 50 deliveries, followed by 50 more, must match uninterrupted execution
byte for byte.

For transport, use at least two soil columns with different initial water and
verify that the second column actually changes; no column may be processed in
place of another. Also tick a three-layer, 1 cm-cell field for four hours: its
4.8-cell travel distance exceeds the field depth, and the coefficient lookup
must remain in range. In a 96-tick rain-and-diffusion fixture, account for initial
water plus rain minus delivered water and explicit boundary losses. The measured
drift was 0 g at 1-hour ticks and 1.46e-11 g at 4-hour ticks. Include a
fully saturated destination to ensure a rejected transfer remains at the
source. The finite tetrahedral fixture used a no-rain, saturated-destination
case, a withdrawal case, and checkpoint continuation.

For an import smoke test, load
`Nooshin_Export/2026-September/fav11_complete_geometry_model_repaired_all_detected.obj`
and `fav11_seeds_P003.json`. Map each seed to its named soil section before
initialization. A selected Bergenia seed's 0.125 g test withdrawal must debit
only its own tray. The probe initialized all 790 P003 plants across 857
imported soil objects, ran six 4-hour ambient-light ticks under both sequential
and explicit parallel world scheduling, and found finite plant resources and
finite, nonnegative tray pools within capacity. All six whole-tray water totals
matched between the two schedules in this startup case. This is a routing and
startup check; it does not establish seasonal growth or hydrologic closure.

## Plant tissue capacity and seed-water reserve

`PlantFormation2.Census` now treats the next-tick photosynthetic water
forecast as a *priority within* anatomical tissue capacity. For organ `i`,
let `C_i` be its current water capacity and `D_i` its forecast demand,
clamped to `[0, C_i]`. When available plant water is below the summed
priorities, each organ receives a proportional part of `D_i`. Above that,
remaining water is shared in proportion to `C_i - D_i`; an organ therefore
never receives more than `C_i` from the census. The rounded-down float
allotments leave their exact remainder in the double-precision
`UnallocatedWater_g` pool instead of minting water.

A hydrated seed can transfer much more water than its new microscopic tissues
can hold. The census retains that surplus in the plant pool and releases it as
tissue grows. Roots defer soil uptake while the pool exceeds the previous
census' one-tick water demand. Once the reserve falls below that demand, roots
may take up soil water again; a temporarily full root remains absorptive after
its store drains. This avoids the former irreversible loss of uptake when a
root reached capacity. `WaterBalance` counts only the water that can fit in
organs on this tick, not the entire unallocated pool.

The isolated allocator fixture at `/tmp/gf-water-capacity` checks a leaf with
2 g capacity and 0.3 g forecast beside an 8 g root: at 9 g total, the
leaf receives about 1.825 g and the root about 7.175 g. It also checks low
water and forecasts greater than tissue capacity. A seed-42 Bergenia on the
default terrain was then run for 25 days at both 1-hour and 4-hour ticks with
ambient and CPU light. All four runs completed with finite water and living
roots/leaves. The initial roughly 1.775 g unallocated reserve declined below
2 × 10⁻⁶ g. Logs are `/tmp/gf-water-capacity/run4.log`.

**Boundary still open:** development births can transfer water directly into a
new bud, meristem, petiole or blade before that organ enters the gathering
snapshot. Transient newborn overcapacity reached about 0.026 g at 1-hour ticks
and 0.049 g at 4-hour ticks in this fixture. A few CPU-lit already gathered
organs also exceeded their nominal capacity briefly by less than about
0.001 g. These are not proof of an end-to-end strict tissue-capacity invariant
or of a closed whole plant-soil budget. Future work should cap birth transfers
at the new organ's capacity and put their surplus into the same plant reserve,
then verify every post-step organ against its declared capacity.

## Scope of the budget

Rain is an external input. The imported virtual pool clips incoming rain at
capacity, so overflow is a boundary loss rather than stored water. Seed and
organ hydration, organ removal and other plant-side transfers need their own
ledger before claiming conservation for the complete plant–soil system.
Transpiration is not yet represented as a measured water flux. Soil travel
speed, catchment, retention and initial saturation are empirical model
parameters, not validated hydraulic properties for the P003 substrate.

Root uptake and storage remain a separate physical limitation. The current
`UnderGroundAgent.WaterAbsortionRatio_g_per_h_m2` is 1.5 × 10⁹ g/m²/h;
`Surface()` is only radius × length, and `mWaterAbsorbtionFactor` is used as an
on/off gate rather than a continuous multiplier. The 755-day rigid Bergenia
probe accumulated about 1.68 kg of plant water with 463 roots and 21
above-ground organs. That is finite but not evidence of plausible root volume:
root axes continue thickening toward parent radii and the default zero-cost
resource mode does not charge their new tissue to carbon. Measurements of
radial uptake differ by root type and age, so replacing the coefficient with
one untested constant would confound seedling emergence and the growth
comparison. [Ahmed et al. (2018)](https://academic.oup.com/jxb/article/69/5/1199/4785933)
measured distinct contributions of seminal, crown and lateral maize roots;
these are not Bergenia coefficients. A root-type/taper and uptake calibration
with organ-level volumes is needed before treating total plant water as a
botanical validation signal.
