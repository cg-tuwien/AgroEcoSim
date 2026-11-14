# Geranium seasonal development

## Model and limits

The two Geranium presets apply an empirical environmental limit to vegetative
development. Adult leaf/petiole dimensions and nominal expansion durations are
unchanged. The initial condition is still a generic propagule, not a measured
nursery transplant. A 5 x 5 cm nursery plug does not specify foliage dimensions.

The provisional defaults are:

| Setting | Geranium presets | Custom/older species |
| --- | ---: | ---: |
| `VegetativeTemperatureResponseEnabled` | `true` | `false` |
| `VegetativeGrowthBaseTemperatureC` | 5 C | 5 C, inactive |
| `VegetativeGrowthFullTemperatureC` | 20 C | 20 C, inactive |
| `VegetativeGrowthLightReferenceWhM2` | 6000 Wh/m2/day | 0, legacy relative production |

The thermal factor is `clamp((dailyMeanC - baseC) / (fullC - baseC), 0, 1)`.
The absolute-light factor is `clamp(previousDayWhM2 / referenceWhM2, 0, 1)`.
Their product replaces relative-production scaling where it previously drove
vegetative expansion. Existing energy, water, geometry and support limits still
apply. Stem strengthening and contact-suspended vegetative strengthening receive
the same environmental restriction. Crown continuation, crown-bud activation and
node-bud regrowth scale developmental time through their event hazard, preserving
the conversion from hourly rates to tick probabilities.

A basal crown's already-sampled emergence axis uses thermal development and its
existing reserve/water checks until it reaches its emergence target. Requiring
leaf light before this axis can leave the soil deadlocks a new propagule: its
microscopic first leaves remain shaded by their own supports. The exception is
bounded by the existing axis target, supplies no artificial irradiance, and does
not release adult leaf/petiole expansion. It also applies to basal crowns from
light-gated rhizome buds. It is reserve-supported development under the existing
model, not a new carbon-conserving construction account.

Petioles/supports use the existing upward propagation of daily leaf exposure;
they do not receive another set of traced rays. Temperature is cached per plant
per tick. Light is the previous day's integrated exposure, so development can
continue at night. It is recorded independently of water status. Unconfigured
custom species retain their existing relative-production response.

This is a developmental limit, not a complete carbon balance or a measured
Geranium growth law. It neither creates assimilates nor enables the experimental
construction-energy debit. The default does not model heat injury, obligatory
vegetative dormancy, nutrients or nursery juvenile allometry. Existing flowering
rules remain in force; warm winter periods can allow existing foliage to expand.

[InfoFlora](https://www.infoflora.ch/en/flora/geranium-macrorrhizum.html)
describes adult G. macrorrhizum basal leaves 6-10 cm wide and petioles 10-20 cm
long. Those adult ranges do not establish juvenile timing. Research on
[low-temperature growth limitation](https://www.frontiersin.org/journals/plant-science/articles/10.3389/fpls.2014.00794/full)
supports separating tissue development from assimilation at low temperature;
it does not establish these coefficients for the site's Geranium cultivars.
The 6000 Wh/m2/day reference is an explicit empirical model parameter, not a
photosynthetic saturation measurement or the ambient solver's daily integral.

## Temperature and calendar inputs

`SimulationRequest.DailyAirTemperatures` accepts optional entries with a required
local calendar `Date` and required finite `MeanC`. For example:

```json
{
  "DailyAirTemperatures": [
    { "Date": "2025-11-01", "MeanC": 6.5 },
    { "Date": "2025-11-02", "MeanC": 0 }
  ]
}
```

Duplicate dates are rejected. Missing dates use a smoothly interpolated Vienna
Hohe Warte **climate normal**, including when a run specifies other coordinates;
it is not observed site weather. The frontend preserves the series when saving,
loading and rerunning a scenario; there is no new weather editor.

The bundled monthly values come from GeoSphere Austria's
[official 1991-2020 workbook](https://klimaportal.geosphere.at/klimamittel/klimanormalperiode_19912020.xlsx),
`Temperatur!FJ22:FJ33` (Wien_Hohe_Warte, 198 m):
`1.1, 2.8, 6.9, 11.9, 16.3, 20.0, 21.9, 21.6, 16.6, 11.2, 6.2, 1.8` C.
These normals use homogenised daily `(Tmax + Tmin)/2`; GeoSphere explains the
difference from other daily-mean definitions on its
[methodology page](https://klimaportal.geosphere.at/klimamittelwerte_1991_2020.html).
Monthly means are anchored at month midpoints and interpolated at local noon.
Daily temperature and its clamped development factor are integrated over each
tick using the actual local calendar, including daylight-saving transitions.

Clouds and rain now also begin at the requested local month/day rather than
January 1, and use actual leap-year month lengths. Complete monthly plans and
the dedicated weather RNG preserve prefixes when the requested horizon grows.
Existing soil temperature and root-water/germination thresholds are unchanged.
The soil's existing surface-evaporation calculation reads air temperature and
therefore now responds to seasonal air temperature.

Live checkpoints use version 36 because continued weather/development semantics
changed. Version 35 and earlier live states are rejected; saved scene playback
is unchanged. Resume with the same temperature series and species settings.

## Regression scenarios

Use temporary net8.0 console probes referencing `Agro/Agro.csproj`, explicit
dates, seed 42 and mixed contacts. Keep the diagnostic harness outside the repo.

| Scenario | Required observation |
| --- | --- |
| Both Geraniums, November 1 through June 30, 1 h and 4 h, CPU light | Daily organ counts, whole-blade dimensions, whole-petiole lengths, exported canopy bounds and winter plateau versus spring acceleration. |
| Matched 0 C / 20 C with identical light and water | Cold suppresses actual vegetative increments; healthy foliage is retained; warm winter development remains possible. |
| Uniform full / half / quarter irradiance with identical temperature | Direct developmental factors follow absolute exposure; report whole-plant trajectories separately because shading and branching feed back. |
| Suspended petiole/support | Cold/dim conditions cannot bypass the same developmental limits through safe radial growth. Floral growth rules remain independent. |
| Midmonth, leap day, year boundary and DST starts | Correct calendar input, 1 h / 4 h rain sums and cloud/temperature averages; extended horizons retain exact prefixes. |
| Checkpoint/resume and parallel scheduling | Identical actual exported frames and checkpoint state with the same input, including an extended horizon. |
| March Geranium flowering, Carex 100 days at 1 h / 4 h, other-species smoke | Follow buds before petals, finite resources and foliage orientation; record any baseline limitations rather than tuning them away. |

The comparison facade request and measured validation evidence are in
`canopy-validation/geranium-seasonal-growth/`. The original salvaged request is
preserved. The comparison uses refreshed numeric settings for both Geraniums;
other explicit species overrides and all planting locations remain intact.

## Measured checks (23 October 2026)

An exact pre-change core build was frozen before editing; source and assembly
hashes are archived with the validation. The matched trajectories use one generic
propagule, seed 42, CPU light with eight hemisphere samples, mixed contacts,
November 1 2025 through June 30 2026, at both tick sizes. These are model results,
not measurements of the November nursery transplants.

| Longest whole petiole by November 30 | Before, 1 h / 4 h | Corrected, 1 h / 4 h |
| --- | ---: | ---: |
| G. macrorrhizum | 20.66 / 19.08 cm | 2.37 / 2.31 cm |
| G. x cantabrigiense | 13.66 / 13.66 cm | 0.32 / 0.79 cm |

The corrected sizes remain approximately constant through February and expansion
resumes in spring. Macrorrhizum's largest blade width by November 30 changes from
7.54 / 6.71 cm to 0.614 / 0.607 cm. The one-hour cantabrigiense begins its rapid
spring expansion later than the four-hour plant; these trajectories do not prove
tick-size invariance of complete plant development. The prescribed responses and
their time integration are tested separately.

At 20 C with otherwise matched March CPU light, the largest macrorrhizum blade
axis on day 15 is 3.54, 2.34 and 1.17 cm under full, half and quarter light.
All treatments can eventually reach the retained adult petiole target, so final
size alone is an inadequate light-response test. Both March-start Geraniums
produce buds before petals through day 162. Additional constant-temperature
November controls distinguish warm winter growth from a calendar prohibition.

Actual imported P006 soil and two tray meshes were also exercised through the
server's null-world construction followed by `SetWorld`. Daily exported bounds
and the upper-tray height datum are recorded; foliage above that datum can be
beside the tray, so a negative vertical clearance alone is not a penetration
measurement. The full 4,134-plant comparison request has not been simulated.

Carex's 100-day ambient controls retain finite resources and zero inverted
organs. The existing guide's zero-downward expectation is already unmet by the
frozen baseline (30 / 17 downward organs at 1 h / 4 h); the corrected-calendar
runs have 21 / 15. This observation is recorded without changing Carex mechanics.

Daily CSVs, before/after plots, response fixtures, resume checks and weather
assertions are archived under `canopy-validation/geranium-seasonal-growth/validation/`.
No field-size tolerance or cultivar-specific adult calibration is claimed.
