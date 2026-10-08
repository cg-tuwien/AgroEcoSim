# Bergenia: the 330-day run and balcony photographs

## Status

The supplied run is genuinely stalled; increasing the crown-node ceiling did not
resolve it. This investigation fixes three identifiable errors: initial crown
emergence, leaf support measured against the wrong petiole target, and mutation
of numeric settings before a request is saved. **Crown density and articulated
blade shape are not yet botanically calibrated.** Contacts still arrest several
leaves and the vegetative apex. Do not interpret a larger count of tiny leaves
as successful calibration, or compensate by shortening the 84-day growth setting.

## Archived evidence

The matching run is `runstore/runs/55d485f815824db9ba882de241776624/`.
Its request SHA-256 is
`fab159e2e9bf73e9f5c67af1a955fd4a7bb43cef4f513eafd3dab75706ad771b`.
It contains 1,980 recorded V7 frames and a v4 live checkpoint: seed 42,
2026-01-01, 7,920 hours, four-hour ticks, CPU light (`RenderMode = 3`), normal
parallel execution, one Bergenia at `(5, -0.01, 5)` in the default soil field.
The default field is 10 × 10 m and 4 m deep; the run-list's single tray is one
soil field, not a measured balcony pot. Frame files total about 2.08 MB, with a
roughly 459 KiB checkpoint. The original archive was read, not modified.

The recorded request already has ten crown nodes, 2 mm node distance, and
0.002/hour post-flowering continuation. It also explicitly disables basal
rhizome activation and axillary rhizome buds. This is a single-crown experiment;
its request overrides must not be silently replaced by the spreading preset.

Decoding all recorded frames gives two leaves at day 0.33, four at day 63.33,
and six at day 81.33. No additional leaves appear over the remaining 249 days.
The sixth blade is too small to be conspicuous in the viewport.

| Stable leaf ID | Final total blade envelope, length × width (cm) | Petiole length (cm) | Fraction of sampled blade target | Days from birth to 50% / 90% |
| --- | ---: | ---: | ---: | --- |
| 4 | 25.88 × 18.75 | 14.00 | 0.953 | 85.33 / 124.00 |
| 6 | 1.74 × 1.29 | 0.77 | 0.063 | neither reached |
| 9 | 5.72 × 3.87 | 2.55 | 0.212 | neither reached |
| 11 | 23.29 × 16.31 | 10.24 | 0.853 | 83.83 / not reached |
| 15 | 0.24 × 0.18 | 0.47 | 0.009 | neither reached |
| 17 | 5.16 × 3.70 | 2.40 | 0.189 | neither reached |

Blade length here is the transformed whole-mesh envelope, including basal lobes;
width is the corrected transverse envelope. Expansion requires both attachment
axis length and radius to reach the fraction of that organ's own sampled target.
Petioles are their actual cylinder axes, not their projected vertical heights.

The archived checkpoint has ten suspended organs, including the apex; above-ground resources
are finite, its modeled water-availability ratio is about 109, and no above-ground
organ died from resource depletion. Several organs also hold large energy reserves. These
facts exclude simple resource starvation as the immediate explanation; they do
not establish that every resource-model magnitude is biologically calibrated.
One-day continuation tracing repeatedly encounters same-crown blade pairs
(leaf IDs 6/15 and 9/17), then restores their accepted geometry. After repeated
failures the retries are 72 hours apart. These traces describe rejected motion,
not proof of penetration in the final saved frame.

## Implemented mechanics corrections

1. **Initial emergence.** `SeedAgent.Tick` previously assigned the initial shoot
   only a compressed internode target. In this run the first three crown segments
   ended approximately 7.6, 5.2 and 2.9 mm below the soil surface. Later rhizome
   crowns already account for their local soil surface. The initial herbaceous
   crown now uses the same local upward surface query and existing
   `CrownNodeHeightAboveSoil` clearance to raise its growth target. It grows to
   that target through the existing resource and contact checks; the seed is not
   moved and the shoot is not instantly extended. Subsequent nodes retain their
   2 mm targets. Report this basal emergence axis separately from the compressed
   crown when comparing axial height.
2. **Sampled petiole maturity.** `LimitLeafGrowthToPetioleSupport` divided by the
   species mean petiole length/radius. A petiole sampled smaller than the mean
   could never count as mature. In the actual archive, leaf 4's ceiling is exactly
   its petiole radius divided by 2.5 mm (0.953); leaf 11's ceiling is its petiole
   length divided by 12 cm (0.853). The limiter now uses that vegetative petiole's
   sampled targets, summed over its segments for length. Young supports still
   constrain leaf growth and the mechanical load solver still applies. Floral
   bracts retain their fixed support dimensions; their fields are not variances.
3. **Unmodified input coefficients.** `SpeciesSettings.Init` multiplied public
   `ShootsGravitaxis` by 0.4 and public `TwigsBendingApical` by
   `TwigsBendingByLevel`. The server serializes the request after initialization,
   so rebuilding it applied the factors again. These runtime coefficients are
   now private to the simulation and public numeric values remain unchanged.
   JSON names and preset assets remain unchanged. Live checkpoints advance to
   **v5** because earlier requests do not mark their already-converted values.
   Old recorded V7 playback and served mesh vertices remain readable/unchanged.
   Start a fresh simulation to test emergence; this does not relocate an old crown.

The retained species settings include the 84-day leaf/petiole growth duration,
adult blade and petiole dimensions, ten-node cap, continuation probabilities and
rhizome activation parameters. These corrections do not establish a new botanical
leaf-expansion duration.

## Photographic ground truth and its limits

`DateTimeOriginal` gives the following chronology (file editing dates differ):

| Capture date | Files in `example_photos/bergenia/` | Supported observation |
| --- | --- | --- |
| 2025-04-17 | `_MG_5581`, `_MG_5585`, `_MG_5606` | Already flowering; a small established plant with old damaged blades and young folded leaves. |
| 2025-05-22 | `_MG_6725`, `_MG_6742`, `_MG_6743` | Several expanded blades; the inflorescence is finishing. |
| 2025-07-06 | `_MG_8091`, `_MG_8094`, `_MG_8105`, `_MG_8111` | Substantial additional foliage and a mixture of blade sizes. |
| 2025-09-30 | `_MG_3122`, `_MG_3134`, `_MG_3163`, `_MG_3200` | Dense foliage, continuing young leaves, and retained older leaves with some damaged margins. |

This is a 166-day observation interval, not a known 166-day-old plant. Multiple
views of the same date must not be summed as different leaves. Occlusion, unknown
starting age and uncertain crown boundaries prevent a defensible exact per-crown
count. The user identifies the plant as **Bergenia cordifolia 'Rotblum'** and gives
its pot's top diameter as **19.5 cm**. This provides an approximate photographic
scale reference, but perspective, blade tilt and different distances from the
camera prevent applying one pixel-to-centimetre ratio to every blade. The images
establish continued seasonal development and varied, nonplanar blade shapes.

### Follow-up measurements supplied by the user

The following measurements describe the plant **about one year after the last
photograph**, not the dimensions of its leaves in the 2025 photo sequence:

| Quantity | Field observation and definition | Current preset comparison |
| --- | --- | --- |
| Pot top diameter | 19.5 cm; a diameter, not a radius or a measured soil depth. | The archived default soil field does not represent this pot. |
| Largest blade | About 16 cm from the petiole junction to the tip, excluding the petiole. The user explicitly identifies this as the **largest leaf**, not a typical mature-leaf size. | `LeafLength = 0.24` m is a nominal 24 cm attachment-axis target, 1.5 times this observed maximum. Its inherited 2 cm target variation does not represent measured variation in this plant. |
| Petiole | About 5 cm; supplied separately from blade length. Sample size and variation are unspecified. | `PetioleLength = 0.12` m is 2.4 times the observed length; the preset's 3 cm variation is also uncalibrated for this plant. |
| Blade width | Not measured. | The current full-width target is 19 cm; the pot's diameter must not be substituted for blade width. |

The 16 cm measurement uses the attachment-to-tip definition, so it must not be
compared directly with the table's **26.79 cm total mesh envelope**. With the
unchanged authored outline, a 16 cm attachment-axis target would have a total
longitudinal envelope of approximately 17.86 cm because basal lobes extend behind
the attachment. Changing the axis target to 14.34 cm to force a 16 cm envelope
would therefore misinterpret the user's measurement.

These observations challenge the retained adult targets for this particular
cultivar and growing condition. They do not establish a universal species maximum,
a typical 16 cm blade, a measured size distribution, or a leaf-expansion duration.
The original reasoning that every mature leaf should approach a published upper
size was incorrect. Numerical defaults are retained for now to keep these new
observations separate from an untested retuning; they are not validated by the
species-level maxima.

For the next dimensional comparison, use a declared largest-blade reference
(`LeafLength = 0.16`, zero length variation) and a separate 5 cm petiole reference
(`PetioleLength = 0.05`, zero length variation). These are controlled diagnostic
settings, **not fitted population means or new production defaults**. Measure
blade width before accepting a complete shape calibration. Compare blade size,
petiole reach and same-crown contacts together at 1 h and 4 h: smaller blades may
reduce contact, while shorter petioles may increase crowding. Neither outcome can
be inferred from the dimensions alone. Keep the 84-day setting unchanged.

Botanical sources checked 28 September 2026:

| Target | Source and measurement definition | Use in this investigation |
| --- | --- | --- |
| Habit and blade shape | [RHS B. cordifolia](https://www.rhs.org.uk/plants/2209/bergenia-cordifolia/details): evergreen, leathery, rounded and slightly wavy blades, up to 30 cm long. Species description, not a cultivar-specific mean. | Bounds the species description; does not validate the retained target for the measured Rotblum plant. Supports waviness qualitatively. |
| Blade width and cupping | [Cambridge University Botanic Garden](https://www.botanic.cam.ac.uk/the-garden/horticultural-collections/national-plant-collections/bergenia/): B. cordifolia has spoon-shaped blades up to 20 cm across; its discussion of B. crassifolia describes puckering and wavy edges. Many cultivated Bergenia are hybrids. | Supports cupped/wavy foliage qualitatively. Does not supply a curvature angle or justify treating all damaged curls as healthy anatomy. |
| Ground-hugging axes | [RHS genus guide](https://www.rhs.org.uk/plants/bergenia): leaves arise from thick horizontal stems at ground level. | Supports emergence to the soil surface while retaining compressed subsequent nodes; the existing 1 cm clearance is a model parameter. |
| Establishment and seasons | [RHS growing guide](https://www.rhs.org.uk/plants/bergenia/growing-guide): persistent foliage, spring flowers, surface-sown light-requiring seed; plants from seed usually flower in their second year. | The photographed flowering April plant is not a known newly germinated seedling. The simulator's generic buried seed and early flowering are not a calibrated nursery-stock initialization. |
| Cultivar identity and habit | [NaturaGart nursery](https://www.naturagart.de/Pflanzen/Stauden/Gehoelzrand-und-Teilschatten/Bergenie-rot.html) lists B. cordifolia 'Rotblum', rounded leathery persistent foliage and April–May flowering. | Corroborates the supplied cultivar name and qualitative habit; provides no measured blade/petiole length distribution. Nursery whole-plant height is not an organ length. |
| Density and timing | User's field observations and dated photographs; cultivar identified as Rotblum, initial age unknown. | Continued spring-to-autumn leaf production is an acceptance target. Twenty leaves per crown and the 84-day duration remain provisional model parameters, not published universal values. |

## Why the shape still needs work

Bergenia supplies explicit low-resolution vertices. `LeafMorphology.TryGetDefaultLeafMesh`
uses them directly, so changing `LeafDimensions.Curvature01` does not reshape this
blade. At nominal adult width its current authored relief spans about **2.13 cm**;
it is a shallow dome, not literally a zero-depth plane. The preset nevertheless
has one blade segment and one petiole segment. Whole-organ rotation cannot
express the photographed midrib fold, spoon-shaped cupping, wavy margin and
variable bending along the petiole/blade.

Changing the served vertices or the preset slice count immediately would also
change old playback: V7 records species names and instance matrices, while the
frontend fetches today's species mesh/slices. Therefore shape changes need a
playback-safe asset boundary before they become the default.

The next implementation should:

1. Persist/version the lamina asset and slice layout per run; fetch/cache by that
   identity in `SimulationController`, `appstate.ts`, `Species.ts` and the renderer.
   Legacy V7 runs must continue resolving the existing unsliced Bergenia mesh.
2. Add explicit broad-scale midrib folding/cupping and a restrained crenate/wavy
   margin to the new Bergenia asset, using the undamaged blades across the photo
   series. Keep cultivar, physical length/width definition and provisional relief
   amplitudes recorded. `Curvature01` alone is not an implementation of this.
3. Test a small bounded number of petiole/blade joints and progressive unfolding
   of young leaves against the recurrent same-crown contact pairs. Preserve
   physical dimensions, support loads, accepted-frame rollback and both solid
   and foliage collision checks. Do not globally disable contacts to fill crowns.
   Cache morphology/slices; avoid per-tick mesh reconstruction or vertex scans.
4. Distinguish seedling initialization from established nursery/division stock.
   Use the supplied photo sequence for an established-plant comparison with
   declared initial age/crown state; keep a separate seedling regression.
5. Re-run matched seasonal/overwintering probes before changing the growth duration.
   Require expanded leaves as well as total leaves, compact crown axes, finite
   resources, plausible flowering, and comparable behavior at both tick sizes.
   A single seed is diagnostic, not a distributional validation of a stochastic
   crown-contact model.

## Regression requirements

- Negative/zero/positive petiole target variation: a fully mature support permits
  full blade expansion; 50% length or radius still limits expansion to 50%.
  Check one and four petiole segments, 1 h/4 h, and fixed floral-bract support.
- A seed 1 cm below default soil gives the initial Bergenia crown a 2 cm target
  (soil emergence plus existing clearance), without immediate displacement.
  Later compressed node targets and clonal settings are unchanged.
- `Init` leaves public numeric settings unchanged. Serialize the actual request
  after creating its world, deserialize it, rebuild and resume: exported frames
  must match uninterrupted execution through contacts, removals and guidance refresh.
- Reject v4 live checkpoints explicitly; decode every old V7 frame and preserve
  raw mesh identity. Geometry/export widths, tiny relief, asymmetric blades and
  Carex slices must continue passing the dimensional contract.
- Rotblum dimensional reference: distinguish the observed largest 16 cm
  attachment-to-tip blade and approximately 5 cm petiole from population means.
  Measure width independently; record whole-envelope length separately. Compare
  the reference and current targets under both tick sizes before accepting any
  density improvement, and retain the archived 24 cm / 12 cm scenario as a control.
- Repeat this January-start, seed-42, one-crown case through day 330 at 1 h and
  4 h with ambient and CPU light. Track stable leaf IDs, sampled-target 50%/90%
  times, blocked organs, retries, flowering and the basal emergence axis separately.

## Measured validation

Results and limitations from the final NativeAOT comparisons are recorded below.

The original published NativeAOT binary was retained as the comparison build.
For a controlled fresh replay, the known v4 input conversions were inverted in a
**temporary diagnostic request**: stored gravitaxis 0.080000006 corresponds to
an input near 0.2, which produces that same effective runtime coefficient. This
is not an automatic migration of arbitrary old archives. All other active
numeric settings, seed, date, soil and CPU-light settings were preserved.
Unused species and predefined complex assets were omitted from the SignalR
message to fit its receive limit; initialization supplies the same assets.

| Native CPU run, day 330 | Whole living leaves | At least 50% expanded | At least 90% expanded | Suspended leaves |
| --- | ---: | ---: | ---: | ---: |
| Actual user archive, 4 h | 6 | 2 | 1 | 4 |
| Fresh original binary, 4 h | 10 | 2 | 1 | 8 |
| Final implementation, 4 h | 8 | 3 | 2 | 6 |
| Fresh original binary, 1 h | 8 | 1 | 0 | 4 |
| Final implementation, 1 h | 6 | 2 | 2 | 4 |

These are one-crown runs. The final four-hour run has three blades exceeding
half their sampled size and five smaller blades; it does **not** produce a
photographically validated dense rosette. The one-hour run remains sparser.
Final above- and below-ground resource checks are finite, and no above-ground
resource-depletion deaths were recorded in these CPU runs. Large modeled
water-availability ratios are not a validation of physiological magnitudes. The one-hour corrected run also opens flowers whereas the four-hour
one does not. Flowering and density remain sensitive to the trajectory.

Fresh four-hour normal-parallel and deterministic runs agree byte-for-byte in
this test. However, the original archive and a fresh run of its old binary agree
only through frame 391; at frame 392 (about day 65.5), tiny petiole-orientation
rounding differences appear, then propagate into different contact/development
histories. The original six-leaf archive remains the authority for what the user
observed. Its final count cannot be replaced with the ten-leaf fresh baseline.
This unresolved numerical/trajectory sensitivity must be included in future
contact acceptance tests rather than hidden by a single favourable run.

Moving the converted coefficients into runtime-only properties preserves all
**9,900 decoded CPU frames** in paired native runs (1,980 at 4 h and 7,920 at 1 h)
against the otherwise-identical mechanics build. The saved input now remains
about 0.2 instead of becoming 0.08. This independently checks that the
serialization correction itself does not retune the growth response.

Validation completed:

- Prescribed core build: zero errors, 304 existing warnings across its targets.
- Final `net10.0`, Linux x64 NativeAOT server published successfully using the
  repository script's build options, to an isolated `/tmp` output. The installed
  publish directory and original run store were not replaced.
- 54 focused assertions cover sampled support dimensions, immature support,
  four-segment petioles, floral-bract exceptions, initial emergence without
  teleportation, unmodified input coefficients, and explicit v4 rejection.
- 6,851 geometry/export assertions pass across seven species, tiny leaves,
  asymmetric blades, slices, collision bounds, and unchanged cylinder dimensions.
- Contact checkpoint probes at 1 h/4 h compare 192/48 resumed exports with
  uninterrupted execution after serializing/deserializing the actual request.
  They include two independent contacting pairs, forced organ removal/reindexing
  and guidance refresh. No unresolved foliage contacts occurred in these fixtures.
  They are accelerated mechanics fixtures, not botanical growth calibrations.
- Old and final native mesh endpoints return identical Bergenia leaf and slice
  payloads (SHA-256 `6382f0c0ca096fb8d0d8adb6797ca1a69c93cd6aa1e14de581289cb503b6d477`
  and `186d326fd163f4617f2762ed7fa17488f1d82af80be2859fd9a65ef1bf8e45f5`).
  All 1,980 original playback frames were decoded successfully.
- Scoped `git diff --check` passes. Temporary probes and isolated native run
  stores are under `/tmp/bergenia-diagnosis` and `/tmp/bergenia-*-native-runs`;
  diagnostic harnesses are not added to the repository.

The earlier 16/18-leaf exploratory results used the already-converted archived
coefficient as a new input, giving an effective value near 0.032. They are not
acceptance results for the actual archived setting. Raising the crown cap or
reducing the growth duration based on those results would be unjustified.
The full 790-plant P003 panel and another overwintering comparison have not been
repeated for these subsequent mechanics changes; the isolated crown still fails
biological acceptance, and the earlier panel results remain historical.

Final NativeAOT ambient-light isolation, using the same recovered input settings:

| Tick | Living leaves at day 330 | At least 50% | At least 90% | Suspended leaves |
| --- | ---: | ---: | ---: | ---: |
| 4 h | 6 | 5 | 5 | 4 |
| 1 h | 12 | 5 | 2 | 9 |

Above- and below-ground resources remain finite and no above-ground resource
deaths are recorded. Ambient light does not remove the crown/contact limitation.

Individual expansion times below are medians **only among successful leaves**;
the success count over all tracked leaves is essential. None of these crowns
lost/replaced a vegetative blade within the 330-day observation, so the final
leaf set is also the tracked set. Sampling uses the actual exported tick frames.

| Final light / tick | Reached 50% / tracked | Median days to 50% | Reached 90% / tracked | Median days to 90% |
| --- | ---: | ---: | ---: | ---: |
| CPU / 4 h | 3 / 8 | 46.00 | 2 / 8 | 83.42 |
| CPU / 1 h | 2 / 6 | 45.00 | 2 / 6 | 83.96 |
| Ambient / 4 h | 5 / 6 | 44.67 | 5 / 6 | 79.67 |
| Ambient / 1 h | 5 / 12 | 44.25 | 2 / 12 | 78.04 |

Thus the support correction removes the artificial maturity ceiling, but most
CPU-lit blades still fail to expand. The successful subset cannot establish a
new species-wide duration, nor can these unknown-age photographs supply one.

## Post-fallback growth replay: v20 CPU-light evidence

This is a subsequent mechanics test; earlier tables above remain historical.
The fresh seed-42, 4 h, 330-day replay uses the exact archived September 2025
request (`e9bd78f470cd4e36bc6b715c50319d44`), including its dimensions, 84-day
expansion setting, crown probabilities, rigid contact mode and disabled clonal
spread. Only growth increments already sampled by physiology are retried after
contact fallback, with endpoint and swept clearance certification.

| Managed .NET 8 run | Living leaves | At least 50% | At least 90% | Open petals seen | Wall time |
| --- | ---: | ---: | ---: | ---: | ---: |
| v16, 4 h, day 330 | 12 | 1 | 0 | 0 | 1,221 s |
| v20, 4 h, day 330 | 20 | 6 | 4 | 0 | 2,184 s |

Thresholds require **both** attachment-axis length and blade half-width to reach
the stated fraction of that leaf's sampled target. They are not fractional blade
area. Daily observations saw no leaf loss, so the cumulative successful counts
are also 6 and 4. Above- and below-ground resources remain finite. The first-scape
proposal is released after its bounded season deadline, but no scape or petals
are admitted in this run. Thus developed foliage improves substantially at 4 h;
flowering still fails. Neither the one-hour result nor tick-size agreement is
established by this table.

The timings include different growing organ populations and concurrent work;
they are not a matched-population overhead benchmark and do not pass the 25%
performance gate. NativeAOT seasonal measurements are assessed separately.
The final crown still has 14 blades below half expansion and 47 blocked organs:
20 living blades must not be presented as a dense mature rosette.

The request, daily/5-day CSV and mature checkpoint-continuation log are retained
in `canopy-validation/bergenia-contact-v20/`; large live checkpoints and temporary
diagnostic harnesses remain outside the repository. The exact day-240 four-hour
state passes byte-identical one-day continuation and exported-frame comparisons,
with finite resources and zero unresolved contacts throughout that fixture.

## NativeAOT v20: complete 755-day CPU-light run, 4-hour ticks

The exact archived request horizon is 755 days, starting 2025-09-01. This native
run is assessed separately from the shorter managed .NET 8 runs above. It keeps
the same biological settings and disabled clonal spread. It predates the
ordering-only optimization and canonical padding writer; their matched-state
fixtures establish unchanged accepted geometry for those fixtures, not a second
full seasonal replay.

| Native day | Living leaves | At least 50% | At least 90% | Open petals seen to date |
| --- | ---: | ---: | ---: | ---: |
| 330 | 18 | 5 | 4 | 0 |
| 365 | 18 | 4 | 3 | 0 |
| 605 | 20 | 10 | 10 | 0 |
| 730 | 20 | 8 | 8 | 0 |
| 755 | 20 | 2 | 2 | 0 |

Fifty leaf identities were observed over the run: sixteen reached both half
length and half width, fourteen reached both 90% thresholds. The maximum number
simultaneously above each threshold was ten. The final loss of developed foliage
coincides with another leaf-replacement cycle; `LeafMaximumAgeHours` remains
360 days. Evergreen habit alone is insufficient evidence to change a cultivar's
individual-leaf lifespan. No flowering occurred, although a floral meristem was
observed in the second season. Thus flowering and sustained developed-leaf
density both remain failed biological gates.

Resources were finite at every daily observation. This does not assert that
every intermediate collision residual was checked. Wall time was 1,575.9 s;
this is not a matched-population speed comparison. Daily/5-day observations,
per-leaf threshold dates and the completion log are stored as
`native-archived-exact-4h-755-v20.*` and `native-leaves-4h-755d.csv` in
`canopy-validation/bergenia-contact-v20/`. The completed 1-hour native run is
assessed separately below; no tick-agreement claim follows from this table.

The 1-hour native case has a first floral meristem at day 240
(stable ID 52): 923 hours old, 6.443 mm long against a 13.700 mm target, radius
2.290 mm against 4.122 mm, `GrowthBlocked=true`, `LoadLimited=false`, positive
energy and no children. Its supporting crown chain is also growth-blocked.
This read-only checkpoint inspection shows that precursor survival is working
past the nominal 504-hour floral maximum, but contact-blocked scape expansion
still prevents progression. Positive energy alone does not establish adequate
construction resources. At day 275 the case still has no petals, twenty
leaves, four above half expansion and two above 90%. The supporting state log is
`native-day240-1h-floral-state.log` in the evidence directory.

### Completed native one-hour comparison (v20)

The same exact archived request now completes 755 days at 1 h. Daily observations
remain finite and contain no petals. This run also predates the ordering-only
optimization and canonical padding writer.

| Native day | Living leaves | At least 50% | At least 90% | Open petals seen to date |
| --- | ---: | ---: | ---: | ---: |
| 330 | 20 | 4 | 2 | 0 |
| 365 | 20 | 4 | 2 | 0 |
| 605 | 20 | 12 | 8 | 0 |
| 730 | 20 | 8 | 5 | 0 |
| 755 | 20 | 2 | 1 | 0 |

Fifty-two leaf identities were observed; sixteen reached both 50% thresholds
and eleven reached both 90% thresholds. The maxima in the recorded five-day
samples are twelve and eight simultaneously developed leaves, respectively.
Wall time was 6,271.0 s. Different tick counts, trajectories and concurrent work
prevent interpreting this as a solver scaling benchmark. Flowering and sustained
developed density fail at both tick sizes; the numerical results do not establish
tick-size agreement. Raw observations and individual-leaf dates are retained as
`native-archived-exact-1h-755-v20.*` and `native-leaves-1h-755d.csv` in the same
evidence directory.

## Floral partial recovery and thickness-aware CCD: v21

This step changes collision certification and retention of already sampled
floral-axis growth, without changing dimensions, the 84-day expansion setting,
crown probabilities, leaf lifespan, or clonal spread. It adds bounded independent
partial length/radius retries after final rollback. The changing-thickness
midpoint and plane certificates are described in `COLLISIONS.md`.

Two controlled diagnostics restore historical v20 states under v21 mechanics by
changing the version byte in a **temporary probe byte array**, after verifying
that production rejects the original v20 checkpoint. The binary layout is
unchanged. These are comparisons from common historical configurations, not
supported live migrations or fresh seasonal reproductions:

- At 1 h, day 240–275, the first scape grows from 6.443 to 6.596 mm, and its
  radius from 2.290 to 2.302 mm, then stalls. Twenty leaves remain, four above
  half expansion and two above 90%; no petals appear. Wall time is 258.8 s.
- At 4 h, day 330–620, the final crown has twenty leaves, eleven above half
  expansion and ten above 90%, with no petals. A second-season scape reaches
  10.784 mm against a 13.467 mm sampled target, radius 1.871 mm against 4.242 mm,
  and retains a pending successor transaction. Wall time is 593.3 s. Leaf
  threshold identities are tracked only from the restored starting day.

Finite above-/below-ground resources are checked daily; every continued tick
checks zero reported unresolved foliage contacts. Read-only post-run trials
identify the remaining one-hour bottleneck: its accepted scape certifies clear,
but a 1 micrometre extension exposes a neighbour's petiole capsule outside the
attachment exclusion. Larger proposals also fail an attached blade sweep.
The four-hour pending developmental owner is deliberately excluded from the
independent replay. Investigate mechanically consistent junctions, coupled
neighbour yielding and continuation of a retained parent's sampled growth.
Neither a larger exclusion nor a stiffness increase alone is validated by these
diagnostics.

### Fresh 755-day NativeAOT reproduction, four-hour ticks

The fresh v21 run uses the exact archived September 2025 request: CPU light,
seed 42, rigid contact mode (`None`), original numeric coefficients and disabled
clonal spread. All daily resource checks remain finite, and every tick reports
zero unresolved foliage contacts. No open petals appear.

| Native day | Living leaves | At least 50% | At least 90% | Open petals seen to date |
| --- | ---: | ---: | ---: | ---: |
| 330 | 16 | 4 | 2 | 0 |
| 365 | 16 | 2 | 1 | 0 |
| 605 | 20 | 5 | 3 | 0 |
| 730 | 20 | 5 | 4 | 0 |
| 755 | 20 | 1 | 1 | 0 |

Of fifty-two observed leaf identities, nine reach both 50% thresholds and seven
reach both 90% thresholds. The maxima in the recorded five-day samples are five
and four simultaneously developed leaves. This is **worse developed foliage**
than the v20 four-hour seasonal control, despite passing the targeted geometry
fixtures. Flowering and sustained density remain failed biological gates.
Wall time is 1,108.9 s; different evolving populations and concurrent work prevent
claiming an end-to-end speed improvement. A fresh complete v21 one-hour season
has not been run; its controlled recovery interval establishes no tick agreement.
No V2 default enablement follows from these results.

Raw CSV, completion logs, per-leaf threshold dates, controlled diagnostics,
checkpoint checks and a matched-starting-state CCD benchmark are retained in
`canopy-validation/bergenia-contact-v21/`. Large checkpoints and diagnostic source
remain under `/tmp`. The core build, isolated NativeAOT server publish, native
checkpoint continuation and scoped diff checks pass separately from biology.

### Biological direction for the next contact fixture

The user's proposed stiff scape displacing compliant foliage is a reasonable
mechanical hypothesis. [RHS describes 'Rotblum'](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
as bearing spring flowers on stout stems and having wavy leaves; this supports
the morphology, not a measured stiffness ratio. The current model already uses
250 MPa for scape tissue and 100 MPa for petiole tissue. These are provisional
parameters, not measurements of 'Rotblum'. Contact response additionally depends
on cross-sectional bending rigidity, length, contact leverage, support joints
and remaining angular freedom. A contact at a fixed petiole base cannot gain
mobility merely by reducing stiffness farther along that petiole.

For the exposed one-hour scape/petiole pair, a follow-up trial measures local
normal mobilities of approximately 0.0000491 and 0.008858 m/N, respectively.
The petiole contact lever is 6.40 mm, so it does have bending leverage. Its
mobility is about 180 times the scape's; with the current compliance-weighted
pair distribution, about 99.4% of the local requested separation goes to the
petiole. These are computed model responses, not measurements of the plant.
They establish that the solver already prefers foliage displacement in this
pair, but do not prove that the full coupled crown has a feasible motion path.
Inspect junction exclusions, deformation-first continuation, additional contacts
and rollback before increasing material stiffness.

Measure a growth-driven scape/foliage fixture with the same contact load acting
on both bodies, stiffness-weighted deformation, reversible recovery and a
certified motion path. Prefer evasion and retain feasible partial growth;
distinguish exhausted numerical work from evidence of confinement. Persistent
soft contact can also inhibit real growth: the
[Acer primordium experiments](https://bsapubs.onlinelibrary.wiley.com/doi/10.3732/ajb.1100325)
show growth release after removal of neighbouring leaves, and
[Arabidopsis inflorescence experiments](https://pmc.ncbi.nlm.nih.gov/articles/PMC3025729/)
show reduced elongation under repeated mechanical perturbation. These are
cross-species evidence for possible responses, not Bergenia coefficients.
Finite growth force, bending/buckling and tissue limits remain necessary when
no feasible evasion exists. Do not impose unlimited scape priority or equate a
hard obstacle with an automatic stop when sliding around it remains possible.

## Scape / petiole containment correction: v22

The scape-driven fixture confirmed a numerical obstruction in the attachment
sweep. Clipping uses rod centre lines and blade surfaces, while its containment
proof used thickness-expanded bounds. A short scape still entirely within the
existing attachment box could consequently prevent its neighbour from yielding.
The proof now uses the same geometry as clipping, retaining the complete motion
bound. This does not enlarge the attachment region or reduce collision thickness
in any distance test. See the regression definitions in `COLLISIONS.md`.

With biology frozen, the isolated scape / petiole / blade fixture completes its
10 micrometre scape proposal and retains about 15 degrees of petiole bending at
both mechanical tick settings. The previous solver retained only about
0.782 micrometres and effectively no petiole bending. A tiny growth increment
demonstrates feasible mechanical release; it does not count as a developed leaf
or establish a biological growth rate.

Fourteen-day NativeAOT comparisons hold the archived request's dimensions,
84-day expansion, crown probabilities, CPU light and disabled clonal spread
fixed. Both builds start from the same previously accepted states:

| Controlled interval | Living leaf IDs observed, before / after | IDs observed above 50% / 90% during interval, before / after | Open petals, before / after |
| --- | --- | --- | --- |
| 1 h, day 275 to 289 | 20 / 20 | 4 / 2 and 4 / 2 | 0 / 0 |
| 4 h, day 240 to 254 | 16 / 16 | 4 / 2 and 4 / 2 | 0 / 0 |

These threshold counts include leaves already developed at the initial sample;
the controlled interval does not establish their birth-to-expansion times.
With the final containment and facet-plane pruning, the one-hour scape increases
from about 6.596 to 6.621 mm by day 289; its sampled target is about 13.700 mm.
This approximately 25 micrometre partial increment is insufficient for the
complete crown. The four-hour case retains a pending first floral transaction.
Resources remain finite and the production unresolved-contact counter stays zero
at every tick. These are controlled continuations, not new 755-day seasons:
production rejects v21 live checkpoints; the diagnostic verifies that rejection
before explicitly rebinding an unchanged-layout private copy to v22.

Botanical acceptance remains **failed / unresolved**: flowering did not recover,
and developed-leaf density did not improve in these intervals. The published
'Rotblum' description supports stout scapes and wavy foliage, not a stiffness
ratio. No material coefficient, species growth rate, dimension or clonal parameter
was changed to compensate. V2 remains experimental and has not been enabled by
this correction. Next investigate coordinated multi-contact evasion and retained
floral-parent continuation without discarding their motion or resource contracts.

The matched-population NativeAOT benchmark also leaves an engineering gate failed:
one-hour median tick time increases by 33.4%, although the four-hour fixture adds
7.4%. See `COLLISIONS.md` for counts, allocations and timing definitions. Opening
previously rejected candidate motions costs more work; pruning alone has not
resolved that cost. This correction therefore establishes an isolated mechanical
repair, not biological acceptance or a performance release for V2.

Logs, collision fixtures and checkpoint results are retained in
`canopy-validation/bergenia-contact-v22/`. Large states and diagnostic source stay
under `/tmp`. Native byte-identical continuation and build acceptance are separate
from the biological failures above.

## Growth-retry pair ordering: v23

The next full-crown diagnostic identified inconsistent conservative certificates:
a leaf sweep accepted in the main solver's stable pair order failed when a growth
retry reversed that pair. `GrowthTrialClear` now shares the main solver's order for
both endpoint distances and the complete relative sweep. See `COLLISIONS.md` for
the witness and regression definitions. The fix adds no per-leaf state and keeps
the material coefficients, dimensions, 84-day expansion, crown probabilities,
CPU light and disabled clonal spread of the archived request unchanged.

Fourteen-day NativeAOT comparisons start from identical accepted v22 states:

| Controlled interval | Living leaf IDs observed, v22 / v23 | IDs observed above both 50% dimensions / both 90% dimensions, v22 / v23 | Open petals, v22 / v23 |
| --- | --- | --- | --- |
| 1 h, day 289 to 303 | 20 / 20 | 4 / 2 and 4 / 2 | 0 / 0 |
| 4 h, day 254 to 268 | 16 / 16 | 4 / 2 and 4 / 2 | 0 / 0 |

At one-hour ticks the scape increases from 6.621 to 7.049 mm under v23; the v22
control stays at 6.621 mm. Its sampled target remains 13.700 mm. Growth resumes
partially, then stalls again against the complete crown. The four-hour case
still retains its pending first floral transaction. Resources remain finite and
the production unresolved-contact counter stays zero at every tested tick.
Threshold counts include leaves already developed when observation begins; they
are not birth-to-expansion times. A 0.428 mm scape increment is progress, not a
flowering or developed-leaf-density acceptance result.

These are controlled continuations of previously accepted geometry, not fresh
755-day seasons. Production rejects v22 live checkpoints; the diagnostic first
verifies that rejection, then rebinds the unchanged-layout private snapshot to
v23. Fresh and mature NativeAOT checkpoint tests at both tick sizes retain
byte-identical immediate and continued states/exports, including leaf removal
and rebuilt caches. Recorded playback is unchanged.

Botanical acceptance remains unresolved. The current
[RHS 'Rotblum' profile](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
supports stout flowering stems and wavy blades, but supplies no stiffness ratio
or leaf-expansion duration. No species coefficient was changed to force a
successful outcome, and V2 remains experimental. Further work must coordinate
the additional foliage constraints and investigate retained floral placement
without replacing numerical nonconvergence with a claim of physical confinement.

Evidence is retained in `canopy-validation/bergenia-contact-v23/`. The tested
feature-bound cache was removed after inconsistent timing results; it is not a
released optimization. The canonical pair ordering adds no persistent storage.

The final v23 implementation also retains conservative edge-plane pruning of
triangle distances. A fresh pair of 14-day NativeAOT CPU-light continuations
produces byte-identical complete states to order-only v23 at both tick sizes:
the biological counts and partial scape gain above are unchanged. Three-process
fixed-population timing comparisons show about 6.9% / 3.1% lower median tick cost
at 1 h / 4 h; see `COLLISIONS.md` for definitions and limits. No per-leaf state
or botanical coefficient was added.

A deeper private CCD experiment let the four-hour crown complete a retained
transaction: over days 268–282, observed living leaf IDs increased from 16 to 20
and IDs reaching both 50% dimensions from four to five. The control stayed at
16 and four. Both had two IDs above 90%, and neither opened petals. In the
one-hour comparison over days 303–317, both retained four/two developed IDs,
neither flowered, and the stalled scape died. The experiment's increased query
cost failed the performance objective and it was removed; these results are not
results of the retained solver. They remain useful evidence that conservative
certificate failures can suppress development, without proving that the full
crown has reached a feasible mechanical equilibrium.

The remaining task is coordinated evasion through the complete crown, including
retained floral placement. The isolated fixture accepts the full 10 micrometre
scape increment with approximately 15 degrees of neighbouring petiole yield at
both frozen mechanical tick settings. The complete retained crown accepts only
about 0.82 micrometres and then stalls. This distinction prevents an isolated
success from being reported as recovered flowering. Dimensions, leaf expansion,
crown probabilities and disabled clonal spread stay fixed for further comparisons.

## Classified certificates and remaining proposal-size stall: v24

The next implementation distinguishes observed intersection from numerical
noncertification and retains a bounded separating-plane fallback. It changes no
species dimensions, material coefficients, 84-day leaf expansion target, crown
probabilities or clonal spread settings. V2 remains experimental.

Matched frozen probes correct an initial preliminary interpretation: the later
day-303 crown already accepts a 10 micrometre scape increment under v23. Both
versions retain the complete increment and the same approximately 0.48-degree
neighbouring petiole bend. Their accepted organ poses agree between one-hour and
four-hour mechanical settings. The earlier day-275 crown retains only about
0.82 micrometres under either version.

For the later crown, the nominal sampled growth-time coefficient instead proposes
approximately 913 micrometres of length and 275 micrometres of radius growth.
Both versions roll that proposal back completely. This frozen diagnostic omits
resource/construction restrictions and concurrent biological proposals; it is not
the actual complete tick. It establishes that small-step feasibility cannot be
used as evidence that the nominal coupled growth proposal is admissible.

| Controlled CPU-light interval | Leaf IDs observed | IDs reaching both 50% / both 90% intrinsic dimensions | Open petals |
| --- | --- | --- | --- |
| 1 h, days 303–331, v23 and v24 | 20 | 4 / 2 | 0 |
| 4 h, days 268–296, v23 and v24 | 16 | 4 / 2 | 0 |

The one-hour scape remains at 7.049 mm and senesces by day 310. The four-hour
first floral transaction remains pending. Resources stay finite and no newly
accepted geometry leaves unresolved contacts, but biological acceptance fails.
Threshold observations include leaves already expanded when observation starts;
they are not birth-to-expansion durations. Reported agent length is the intrinsic
attachment axis, not the total basal-lobe blade envelope.

The remaining implementation task is bounded coupled continuation of biological
proposals, with neighbours yielding and every accepted subpath certified. The
existing eight-step dimensional bisection retries size against restored neighbours;
it does not re-solve their evasion at each proposed intermediate size. This is a
source-traced numerical limitation, not evidence for softer Bergenia tissue or
faster leaf expansion. Evidence is in
[`bergenia-contact-v24`](../canopy-validation/bergenia-contact-v24/README.md).

## Bounded coupled continuation and fresh controls: v25

Existing floral axes now retry sampled dimensions through certified intermediate
sizes while neighbours yield. Extension and thickening have independent trials,
and rejected trials restore both bodies and their supports. Physiological
proposals are captured before the first hard rollback. No botanical coefficient,
resource debit, leaf duration, crown probability or clonal-spread setting changed.

The complete frozen day-303 crown retains about 71.35 micrometres of the manually
imposed nominal 913-micrometre extension, versus zero in v24; its approximately
275-micrometre radial proposal remains blocked. The neighbouring petiole turns
about 2.17 degrees, and all accepted motion is certified. Frozen accepted poses
are identical under one-hour and four-hour mechanical settings.

An important diagnostic correction: stable scape ID 52 in that saved day-303
state already has `flowerSupport == false` and a wood/senescence ratio of about
0.56. A normal tick samples no extension for it. Manually extending it is only a
geometry experiment and cannot demonstrate recovery of a living scape. Its
nominal expansion is fully funded in the diagnostic clone, with construction
energy charging disabled by the archived request; lack of funding is not the
explanation established by this test.

Fresh CPU-light controls start on 1 September 2025 with seed 42 and preserve the
755-day request horizon for weather initialization. The four-hour pair was run
from birth through day 330. Both versions observe 20 leaf IDs, five reaching both
50% intrinsic dimension thresholds and four reaching both 90%, with no open
petals. The first scape reaches 11.64 mm in v25 versus 10.59 mm in v24, but remains
only about 0.366 mm in radius against a sampled target of 5.58 mm, then senesces.
This does not recover flowering or the developed-leaf-density objective.

The fresh one-hour pair reaches day 275 with 20 observed leaf IDs, six reaching
both 50% dimensions and three reaching both 90%, again without open petals.
The scape reaches 3.0517 mm / 1.4541 mm length/radius in v25, compared with
3.0243 mm / 1.4538 mm in v24, against targets of 15.1306 mm / 5.9038 mm.
The six observed half-expansion times are 52, 54, 59, 78, 86 and 127 days; the
three 90% times are 137, 144 and 191 days. Both versions have the same threshold
times. The small scape improvement does not meet biological acceptance.

These fresh runs precede the final fixes for unjoined neighbours' motion history
and reversed contact-memory lookup. The final source retains the frozen-fixture
poses and passes 12-hour resumed NativeAOT comparisons from both mature snapshots,
including removal/reindexing. Those short checks do not replace a fresh full-run
comparison of the final source.

For the five leaves that reach both 50% dimensions, observed birth-to-threshold
times are 43, 63, 74, 97 and 211 days in both versions. The four leaves reaching
both 90% take 77, 100, 112 and 150 days. These daily observations are conditional
on reaching the threshold; unexpanded leaves remain censored, and the median is
not a typical expansion duration for the entire crown. Agent length means the
intrinsic attachment axis, not the complete basal-lobe blade envelope.

The separate two-plant geometry fixture shows that feasible contact can permit
substantial growth with mutual yielding, and a solid ceiling rejects growth with
exact restoration. It does not supply a Bergenia material measurement. The
[RHS 'Rotblum' description](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
supports stout flowering stems and wavy blades, without a numerical stiffness
ratio. The [C-IPC research](https://ipc-sim.github.io/C-IPC/) supports coupled
thin-structure/contact treatment and conservative motion checking; this bounded
recovery is groundwork for that design, not its equilibrium solver.

The observed 'Rotblum' largest blade of 16 cm, approximately 5 cm petioles and
19.5 cm pot diameter remain a separate dimensional comparison. Unknown blade
width and initial age prevent fitting this seasonal trial to that photograph
sequence. BAC V2/default enablement and biological acceptance remain pending.
Full evidence, the fresh one-hour comparison and benchmark scope are recorded in
[`bergenia-contact-v25`](../canopy-validation/bergenia-contact-v25/README.md).

## Coordinated continuation witnesses: v26

The new continuation response retains several contact witnesses and distributes
their reactions through both bodies and shared supports, using existing tissue
coefficients. The isolated rigid-blade thickening fixture retains about 10% more
radial growth; the V1 fixture retains about 27.5% more. All newly accepted motion
still requires complete collision certification. No botanical parameter changed.

The matched one-hour case now opens a flower. Correct final snapshots at day 275
show eight leaves reaching both 50% intrinsic dimensions, versus six in v25, and
three reaching both 90% in either version. At four-hour ticks, day 330 remains at
four half-expanded and two near-mature leaves, without petals, in both versions.
This does not meet the doubled-density objective at both tick sizes. Final-source
continuations from day 240 retain the one-hour flowering gain and the four-hour
failure. Upright basal scapes do not establish full flower-posture acceptance;
some branch, bud and pedicel axes point downward.

This increment's development probe had a reporting error: it treated sampled
variation offsets as full leaf targets. Its raw threshold columns and threshold
CSV histories are invalid. The counts above were recomputed using species
dimensions plus sampled offsets. Correct individual expansion times cannot be
recovered from these final snapshots; retain the 84-day setting and report that
timing remains unmeasured. This correction does not invalidate older probes
without checking their separate target calculations.

The measured shared-support retry is still substantially slower; the end-to-end
performance gate is not established. Full seasonal/overwintering and panel gates remain outstanding, so no
deformation mode is enabled by default. Evidence and botanical measurement
definitions are in
[`bergenia-contact-v26`](../canopy-validation/bergenia-contact-v26/README.md).

## v27: retry cost and experimental staged evasion

The rigid shared-support fixture's retry median drops from about 7.17 to 2.07 ms
in the current serial NativeAOT control, without additional retained growth.
Single-contact and rigid radial cost remains approximately unchanged. The richer
mode-1 retry completes the synthetic 9 mm scape-radius target instead of retaining
7.42 mm, but costs about 4.4 times its v26 control. That performance gate fails.
No dimensions, moduli, growth durations, probabilities, resources or clonal-spread
settings were changed. Bergenia remains mode 0 by default.

Rigid continuation retains one open flower, eight half-expanded and three
90%-expanded leaves at 1 h through day 282. At 4 h it still has no petals, four
half-expanded and two 90%-expanded leaves. Resources remain finite and accepted
contacts resolve. These are continuations, not new whole-season validation or
measured leaf-expansion durations. The balcony cultivar comparison remains separate.
Source changes, research scope, checkpoint v27 compatibility, mechanical fixtures
and timing/measurement limits are recorded in
[`bergenia-contact-v27`](../canopy-validation/bergenia-contact-v27/README.md).

## v28: reciprocal V1 contact response

Mode-1 floral continuation now treats a saturated support's bend limit as a
constraint on the combined load, retaining lateral yield and allowing inward
release. Existing tissue coefficients, mature dimensions, the 84-day growth
setting, crown probabilities and clonal settings are unchanged. Mode 0 remains
the default and retains its previous mechanical response.

The final CPU-light, seed-42 continuations from day 282 to day 285 match v27
byte for byte apart from the checkpoint header. At day 285 the 1 h case has eight
leaves reaching both 50% intrinsic dimensions and three reaching both 90%; the
4 h case has four and two. Each has twenty whole leaves. The one-hour flower
retires during this interval in both versions; the four-hour scape is still
blocked at approximately 0.403 mm length and 1.243 mm radius. These short
continuations do not establish seasonal V1 acceptance or density improvement.

Frozen angular/contact fixtures pass at both tick sizes, including release and
impossible confinement. The bounded query memo improves the common-support
retry's consistency and median time, while the V1 retry retains substantial
between-process variation. Full performance and biological gates remain open.
Checkpoint v28 rejects older live states and preserves their playback. Results,
measurement definitions and remaining tasks are in
[`bergenia-contact-v28`](../canopy-validation/bergenia-contact-v28/README.md).

## v30: continuous admission and full native seasons

Tiny-motion and midpoint-only collision shortcuts are removed. Distance-based
interval certificates now apply the existing clearance tolerance consistently
while reserving the complete material-point motion bound. The original corrective
gap, stiffness, angles, work budgets, dimensions, 84-day expansion setting, crown
probabilities and disabled clonal spread remain fixed. Live checkpoint 30 rejects
older live states; playback formats and geometry assets are unchanged.

Fresh NativeAOT CPU-light comparisons reproduce the archived 755-day request,
starting 1 September 2025, at seed 42 and both tick sizes. Expansion thresholds
require both sampled intrinsic blade dimensions, independently of folding.

| Tick | Day 330: half / near-mature, v28 → v30 | Day 755: half / near-mature, v28 → v30 |
| --- | --- | --- |
| 1 h | 7 / 3 → 8 / 4 | 0 / 0 → 4 / 2 |
| 4 h | 5 / 4 → 9 / 4 | 1 / 1 → 7 / 4 |

All four cases carry twenty whole leaves at those snapshots. None produces
petals during 755 days; flowering remains unresolved in the native control and
candidate. Previous flower-positive managed continuations are different cases
and do not establish native seasonal acceptance. The doubled developed-density
objective still fails at both tick sizes. Completed-only expansion ages have
survivor bias; the nominal duration is not recalibrated from them.

The explicit V1 four-hour case finishes day 330 with sixteen leaves, six reaching
50% and two reaching 90%, without flowering. Measured side folds are shallow,
while internal longitudinal blade bends are negligible. V1 remains experimental.
Its mature checkpoint continues byte-identically, including removal/reindexing.

At four-hour day 330, median attachment-axis blade length increases from 60 to
106 mm without raising the crown. Largest blade axes near 256 mm and petioles
near 140 mm still exceed the user's 'Rotblum' largest blade of 160 mm and
petioles about 50 mm; total blade envelopes are longer because of basal lobes.
Cultivar dimensions and the unknown blade width/initial age remain separate
calibration limits.

The midterm graphics review supports conservative trajectories and coupled
shell/support mechanics, without validating the current reduced-joint retry as
equilibrium. A native mature fixture isolates an attached-petiole decision about
0.54 nm beyond the 10 µm clearance tolerance during recursive interpolation.
Independent robust distance/interpolation references are the next focused gate;
this is not evidence for changing tissue stiffness or biological probabilities.

Frozen mechanics, published CCD-subset checks, native checkpoint continuation,
frontend playback tests and builds pass within their documented scope. Matched
fixed-retry timing changes range from about −8.5% to +6.1%, with unchanged
persistent per-organ storage. Full-scene performance, allocation and V2 memory
gates remain open. Evidence, measurement definitions, rejected experiments and
botanical/research sources are in
[`bergenia-contact-v30`](../canopy-validation/bergenia-contact-v30/README.md).

## v31: measured distance precision and native day-330 comparison

The segment kernel now measures its closest-point residual before rounding the
returned witnesses, using relative double arithmetic. Interpolated endpoints and
stationary pose fields remain exact. Live checkpoint 31 rejects older live states;
saved playback and checkpoint payload layout stay unchanged. Collision skin,
mechanical limits, dimensions, provisional 84-day expansion, crown probabilities
and disabled clonal spread remain fixed. No species parameter is adjusted here.

Fresh NativeAOT seed-42 CPU-light cases reuse the reconstructed archived request
at both tick sizes, with a 330-day observation horizon. Both carry twenty whole
leaves, seven meeting the 50% threshold and five meeting 90%, with no petals.
The v30 day-330 results were eight/four at 1 h and nine/four at 4 h. The mixed
change does not satisfy the approximately doubled developed-leaf-density goal.
Thresholds require both sampled intrinsic blade dimensions; completed-only
expansion times have survivor bias. Resources stay finite and tickwise unresolved
contacts stay at zero, which does not establish physical or biological acceptance.

Historical and new day-240 scape fixtures still reject the requested elongation
and thickening, preserving clear accepted geometry and exact rollback. An
independent reference finds nanometre differences between rounded endpoints and
double quaternion transforms near the existing 10 micrometre clearance boundary.
Those differences do not certify the complete motion or justify greater overlap.
The one-hour scape remains about 10.3 mm through day 305 and disappears by day 310
without flowering. Its obstruction remains open.

Thirty-two frozen mechanical cases pass across managed/NativeAOT and 1 h/4 h;
frozen pose hashes match between tick sizes within each runtime. Eight native
fresh/mature checkpoint cases continue byte-identically, including removal and
reindexing. Numerical references, continuous fixtures, builds and frontend helper
checks pass within the reported scope. Fixed-retry timing varies between batches;
no general speedup, zero-allocation or full-scene performance gate is established.
This increment adds no persistent per-organ state.

Mode 0 remains the default while V1/V2 release gates fail. Overwintering, flowering,
developed density, V2 mechanics/storage, multicore dense stands and the repaired
790-plant FAV11 panel remain open. The 'Rotblum' observations (largest blade 16 cm,
petioles about 5 cm, pot top diameter 19.5 cm; unknown blade width and initial age)
remain a separate dimensional comparison. Evidence and the focused research
follow-up are in
[`bergenia-contact-v31`](../canopy-validation/bergenia-contact-v31/README.md).
