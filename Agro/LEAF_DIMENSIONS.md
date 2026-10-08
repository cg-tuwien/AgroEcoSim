# Leaf dimensions and Bergenia calibration

The subsequent default-terrain, January-start investigation is recorded in
[BERGENIA_CALIBRATION.md](BERGENIA_CALIBRATION.md). It fixes initial crown emergence,
sampled-petiole maturity limits and request mutation, advances live checkpoints to
v5, and documents the remaining contact/shape limitations. It also records the
user's subsequent identification as Bergenia cordifolia 'Rotblum': 19.5 cm pot
rim diameter, a largest current attachment-to-tip blade of about 16 cm, and
petioles about 5 cm long. Those measurements challenge the retained adult targets
for this plant; 16 cm is an observed largest blade, not a population mean. The
historical v4 measurements below are not a validation of the later changes or a
cultivar-specific size calibration.

## Physical contract

All lengths are metres. `LeafRadius` is **half the full blade width**: 0.035 means
a 7 cm wide blade. It is neither a diameter nor the radius of a leaf-shaped solid.
`LeafLength` is the longitudinal attachment-to-tip axis used by the agent. An
authored outline may extend behind the petiole junction or slightly beyond the
nominal tip, so its total botanical blade envelope must be measured separately.
Petioles retain their actual axis length and cylinder radius.

The coarse authored outlines used by the viewer are not all normalized to a
transverse span of two. The correction is `2 / (maxY - minY)`, measured once per
whole morphology before slicing. Collision templates apply it to private vertex
copies; new V7 matrices apply it to the transverse basis of the unchanged served
mesh. Asymmetric blades are not recentered, basal lobes are not cropped, and
longitudinal segmentation and width-dependent relief retain their meanings.
Collision simplifications are derived from the corrected vertices. Petal
geometry is unchanged by this leaf correction.

This also leaves old playback unchanged: old matrices still multiply the same
served vertices. Live checkpoint version 4 rejects older live states because
their accepted contact geometry and clearance caches used different widths.
The V7 frame format and species JSON property names are unchanged.

The internal CPU light model continues to use its existing rectangular leaf
proxy. It is not a triangle-accurate silhouette tracer; geometry agreement tests
refer to exported meshes and contact geometry, not to that approximation.

## Botanical evidence and calibration status

Retrieved 28 September 2026:

| Target | Evidence and interpretation |
| --- | --- |
| Geranium macrorrhizum blade | [InfoFlora / Flora Helvetica](https://www.infoflora.ch/en/flora/geranium-macrorrhizum.html) gives basal blade widths of 6–10 cm. A 3.5 cm half-width and 4.5 cm attachment axis produce an approximately 6.96 × 7 cm blade with this outline. This is a representative target, not a measurement of the site's 'Spessart' cultivar. |
| Geranium petiole | The same source gives 10–20 cm petioles. The 18 cm mean is retained; the inherited variation is not a botanical hard bound. |
| Bergenia mature blade | [Missouri Botanical Garden](https://www.missouribotanicalgarden.org/gardens-gardening/your-garden/help-for-the-home-gardener/advice-tips-resources/visual-guides/plants-for-dry-shade) describes blades reaching about 25 × 20 cm; [RHS](https://www.rhs.org.uk/plants/2209/bergenia-cordifolia/details) gives lengths up to 30 cm. These are mature upper sizes, not a requirement for every leaf. The retained nominal geometry is 26.79 × 19 cm. |
| Bergenia habit | Both sources describe clumping foliage and spring flowering. Retain the compact crown, spring floral commitment and slow clonal spread. |
| Bergenia density | The user's site observation is approximately twice the simulated number of leaves **per crown**. Ten nodes at two leaves per node is a provisional capacity target, not a published universal count of 20 leaves per crown. Halving internode length preserves the nominal compressed-axis budget. |
| Bergenia expansion | The user observes slower individual leaf expansion but has no measured replacement duration. Retain `LeafGrowthTime = 2016` hours and its existing variation; measure individual leaves before changing it. It also controls petiole growth. |
| Heuchera 'Palace Purple' | [NC State Extension](https://plants.ces.ncsu.edu/plants/heuchera-palace-purple/) describes blades 3–5 inches (7.6–12.7 cm) wide, supporting the retained 11 cm nominal width. Its broader metadata bin says 3–6 inches; the descriptive range is used here. The 18 cm petiole still needs direct cultivar-specific evidence; floral-stem height is not petiole length. NC State notes that the cultivar's species identity is debated, so this evidence is cultivar-specific. |

Post-flowering Bergenia crown continuation is 0.002 per hour, reusing its existing
flowering-phase rate as an explicit calibration choice. Dormant continuation and
post-flowering flower commitment remain zero. Increasing leaf-node capacity does
not guarantee twice the surviving leaf count when resources or contacts limit
development; count leaves on matched crowns and ages, not only per plant.

## Regression scenarios

Use temporary `net8.0` console probes referencing `Agro/Agro.csproj`, seed 42,
explicit initial date, 1-hour and 4-hour ticks, and deterministic execution.
Preserve an independently built baseline before editing presets. Do not compare
against an old binary whose source is unknown.

| Scenario | Required measurement or acceptance |
| --- | --- |
| All seven mature leaf templates | Transform every vertex. Whole-blade width equals `2 * Radius`; record total longitudinal envelope separately. |
| Tiny leaves and asymmetric outline | Check scale 1 and 0.0001, fixed attachment, retained basal lobes, and proportional relief. Bounds and inverse triangle queries must include all transformed vertices. |
| Segmented Carex | Measure the assembled blade, not individual slices; all slices share the whole-blade correction and retain taper. |
| Real V7 export | Decode actual records independently and transform the served raw vertices. Compare with collision vertices; petiole length and radius must be unchanged. |
| Collision approximations | Full and simplified templates preserve the same boundary and width, with finite certified relief error. |
| Translated tray and widening blade | Test at origin and FAV11 coordinates; thin wall crossing and widening into an edge are blocked, while a 7 cm blade clears a wall 4 cm to its side. |
| Numeric species override | Request a different radius/node cap on a predefined name; the world must honor the numbers while retaining predefined complex assets. Also check a custom-only species and the predefined fallback. |
| Bergenia through overwintering | Track live complete leaves per crown, compressed crown height, clonal crowns, resources and daily buds/petals through day 470. Compare ambient isolation with CPU light. |
| Individual leaf expansion | Track stable organ IDs and each sampled target; report times to 50% and 90% of both length and width, plus leaves that die or remain below those thresholds. Never compute a population duration only from successful leaves without reporting censoring. |
| Checkpoint and playback | New checkpoints resume to identical exported bytes across contact, renewal/removal and guidance refresh; reject v3 live checkpoints; old saved frames and raw meshes remain readable. |
| FAV11 September P003 | Use the repaired all-detected OBJ, first one representative per species, then all 790 plants. Inspect early and established organs against tray dimensions and site photos; keep plant counts distinct from organ counts. |

P003 soil sections measure approximately 8.6 cm high and 10.2 cm deep in the
source OBJ. Tray shells have taller back edges (about 25.9 cm total height).
The import rotates axes but does not rescale coordinates. A universal scene or
plant scale adjustment would conceal the leaf contract error.

Existing collision, resource, flower and saved-run scenarios in
[COLLISIONS.md](COLLISIONS.md) still apply. Build both simulator and server,
regenerate species descriptions using SchemeGenerator, check the frontend types
and playback tests, and run a scoped `git diff --check`.

## Measured implementation checks, 28 September 2026

The geometry correction passes its dimensional contract. **The Bergenia settings
remain provisional: the biological acceptance criteria do not pass at both tick
sizes.** The following measurements must not be presented as a completed
photographic or full-facade calibration.

The audit transformed every template vertex, including all Carex slices. Values
below are nominal mature dimensions, without organ-to-organ variation. Length is
the complete unbent blade envelope; Fragaria's envelope includes all three
leaflets. Unchanged presets are recorded as model targets, not newly established
botanical measurements.

| Preset | Old drawn width (cm) | Corrected width (cm) | Corrected complete length (cm) |
| --- | ---: | ---: | ---: |
| Geranium macrorrhizum | 12.36 | 7.00 | 6.96 |
| Geranium × cantabrigiense | 11.32 | 7.00 | 9.22 |
| Bergenia cordifolia | 19.14 | 19.00 | 26.79 |
| Heuchera micrantha | 16.78 | 11.00 | 9.37 |
| Campanula poscharskyana | 3.34 | 4.00 | 4.34 |
| Carex morrowii 'Variegata' | 0.60 | 0.60 | 30.00 |
| Fragaria vesca 'Rügen' | 10.78 | 8.00 | 6.24 |

The temporary geometry probe passed 7,955 assertions: actual V7 records versus
collision vertices, ordinary and 0.0001-scale leaves, asymmetric attachment
preservation, shared slice normalization, inverse BVH queries, simplified
collision boundaries, translated sheet crossings, widening, and cylinder length
and radius. Independently hashing served vertices and triangle indices in the
frozen pre-change binary and corrected binary gave identical hashes for all seven
species. The numeric override checks exercised actual initialized worlds.

The existing checkpoint self-test passed all 12 resume cases. Four nearby plants
in two independent pairs, with CPU light, additionally passed 192 one-hour and 48
four-hour byte-for-byte frame comparisons over contact and forced leaf removal /
reindexing. Both fixtures recorded zero unresolved foliage contacts. Their
accelerated lifespan and forced removals are mechanics fixtures; they are not
botanical evidence or proof that normal leaf renewal is calibrated.

Core and server builds passed with existing warnings, frontend type checking
passed, and 26 playback/transform tests passed across five suites. The species UI
was regenerated with the existing SchemeGenerator executable. Compiling that
generator itself encountered existing duplicate assembly-attribute artifacts;
no unrelated generated directories were cleaned to work around it.

### Bergenia through day 470

Eight independent runs used the frozen pre-change or corrected build, seed 42,
one plant, an initial date of 1 March 2025, and the same flat-field request. Both
ambient light (mode 0) and CPU light (mode 3) were evaluated at 1 h and 4 h.
Water and mass stayed finite and nonnegative. Counts below are complete living leaves,
not petioles or segments. The primary crown is followed through the parent-linked
export; its axis length is the sum of its crown segments, excluding rhizome and
petioles. Clonal crowns are counted separately.

| Light / tick | Primary-crown leaves, before → after | Primary axis (cm), before → after | Total crowns, before → after |
| --- | ---: | ---: | ---: |
| Ambient / 1 h | 10 → 12 | 2.25 → 1.31 | 5 → 8 |
| Ambient / 4 h | 10 → 20 | 2.16 → 2.35 | 5 → 7 |
| CPU / 1 h | 10 → 10 | 2.15 → 0.99 | 8 → 3 |
| CPU / 4 h | 10 → 20 | 2.13 → 2.01 | 6 → 7 |

Thus the four-hour runs reach the doubled capacity while keeping a compressed
axis. The one-hour runs do not. Crown counts also change despite unchanged
rhizome parameters: resource availability, contact-constrained development and
changed random-number consumption affect the realized trajectory. These single
seeded runs do not establish unchanged clonal spread.

Stable leaf IDs were sampled daily. Expansion requires **both** sampled length
and width to reach the stated fraction. Times below are medians only among leaves
that reached it, with daily sampling resolution; the denominators include every
leaf observed across the run, including leaves later replaced or lost.

| Build / light / tick | Reached 50% / tracked | Median days to 50% | Reached 90% / tracked | Median days to 90% |
| --- | ---: | ---: | ---: | ---: |
| Before / ambient / 1 h | 10 / 49 | 60.56 | 1 / 49 | 86.88 |
| After / ambient / 1 h | 13 / 84 | 51.67 | 4 / 84 | 90.23 |
| Before / ambient / 4 h | 11 / 50 | 46.50 | 3 / 50 | 76.67 |
| After / ambient / 4 h | 11 / 95 | 47.67 | 5 / 95 | 81.67 |
| Before / CPU / 1 h | 7 / 72 | 64.75 | 2 / 72 | 95.62 |
| After / CPU / 1 h | 0 / 39 | — | 0 / 39 | — |
| Before / CPU / 4 h | 5 / 46 | 61.83 | 1 / 46 | 123.33 |
| After / CPU / 4 h | 2 / 82 | 62.33 | 0 / 82 | — |

At day 470, 66/68 corrected ambient-one-hour leaves, 72/76 ambient-four-hour
leaves, and all 30 and 66 CPU-light leaves respectively had never reached 90%.
Some are young replacement leaves; others have stalled. These heavily censored
results cannot establish a botanical expansion duration. The 84-day nominal
setting is consequently retained, as agreed.

A diagnostic repeat with foliage contacts disabled, leaving CPU lighting and
solid constraints enabled, produced 38 leaves on two corrected crowns by day 180.
Across 470 days, 27/121 leaves reached 50% and 12/121 reached 90%; the medians among
those leaves were 109.58 and 220.25 days. The corresponding pre-change diagnostic
reached 50% in 37/86 leaves and 90% in 9/86, with medians 78.63 and 165.83 days.
This identifies contact constraints as a substantial contributor to the stalled
one-hour case, with resource/lighting effects still present. Disabling contacts
is an isolation experiment, not an accepted production solution.

Flowering also remains conditional on the developed crown and resources. The
corrected ambient runs produced flowers in the second spring, whereas the CPU
runs did not. Snapshot counts alone cannot establish the complete flowering
window. In the corrected four-hour final exports, inverted organs were floral
parts; no leaf or petiole was inverted. This does not validate every branch of an
inflorescence or every transient posture.

### FAV11 site measurements

The actual September repaired OBJ and P003 seeds were imported through the same
`TerrainBuffer` parser as the server, preserving coordinate units. The seven
representatives completed 180 days under CPU light with finite, nonnegative
water and mass. Their decoded exports were inspected at a common physical scale
against `example_photos/closeup_IMG_7224.JPG`. This supports checking blade shape
and scale; it does not prove a density or age match to the photographed plants.

The corrected full panel completed the first 30 days with 50,056 above-ground
organs, 8,694 complete leaves and 1,243 crowns. It retained finite, nonnegative
water and mass, and **zero unresolved foliage contacts at every step**. Elapsed time
was 1,104.6 seconds including import, shared with other diagnostics; this is not
a controlled performance benchmark. The initial 180-day panel request was stopped
after its day-30 export because the controlled Bergenia biological acceptance had
already failed. Full-panel mature and overwintering acceptance is **not complete**. The frozen
pre-change panel also passed the first 30 days without unresolved contacts, ending
with 49,939 organs, 8,530 complete leaves and 1,266 crowns.

At day 30 the full-panel median Geranium blade was 5.40 × 5.39 cm and its petiole
14.50 cm long; Heuchera was 2.23 × 2.66 cm with a 4.86 cm petiole, and Bergenia
1.59 × 1.13 cm with a 1.10 cm petiole. The geometry correction therefore does not
resolve every concern about the age at which petioles attain adult length.

The complete panel contains 790 plants: 153 macrorrhizum 'Spessart', 171
cantabrigiense 'Saint Ola', 135 Carex 'Variegata', 135 Fragaria 'Rügen', 91
Campanula, 66 Bergenia and 39 Heuchera 'Palace Purple'. All 790 appear in the
day-14 export. Basal blades and whole petiole chains were measured from the
actual recorded matrices, excluding floral bracts. Carex's blade lengths sum
its slices, rather than mistaking a segment for a whole leaf.

| P003 at day 14 | Blade length (cm), before → after | Blade width (cm), before → after | Petiole length (cm), before → after |
| --- | ---: | ---: | ---: |
| Geranium macrorrhizum | 5.08 → 3.81 | 5.74 → 3.83 | 8.93 → 10.50 |
| Heuchera | 1.67 → 1.82 | 3.09 → 2.16 | 3.74 → 3.92 |
| Bergenia | 1.33 → 1.21 | 0.99 → 0.89 | 0.83 → 0.87 |

These are population medians of developing organs, not mature targets or matched
individual leaves. Petiole trajectories can change through contact and resource
feedback even though their numeric targets and cylinder transforms are unchanged.
In particular, the corrected Geranium petioles are still relatively long early
in this request; the adult 10–20 cm reference does not validate their age curve.

At day 180 the representative Geranium, Heuchera and Bergenia median blades were
respectively 2.98 × 2.88, 2.57 × 2.74 and 2.79 × 2.03 cm, with petioles 8.76,
5.05 and 1.70 cm long. Their median basal petiole diameters were 2.35, 1.91 and
1.10 mm. Replacement leaves and arrested expansion keep these distributions
well below the mature dimensions; they are not proof of mature-canopy acceptance.

### Remaining biological acceptance

1. Diagnose which same-crown contacts suspend the one-hour Bergenia trajectory,
   recording the accepted/rejected increments, attachment exclusions, support
   limits and light/production history of the same stable organ IDs. Keep physical
   blade dimensions fixed while doing this; do not hide contact failures by
   shortening the growth duration or reducing mature blades.
2. Repeat matched-crown measurements across seeds and both tick sizes after that
   constraint is understood. Require the increased living leaf density and a
   compressed crown together, and compare clonal extent/activation by season.
3. Compare established second-spring geometry with the site photographs. The
   photographed plants are established perennials, so initial seedlings and
   unknown-age leaves cannot calibrate adult petiole lengths or an expansion
   duration. Obtain cultivar-specific petiole/age measurements before changing
   Heuchera's target; the blade width already has cultivar-specific support above.
4. Complete the full 790-plant mature/overwintering comparison after resolving the
   failed density/expansion acceptance. Keep per-step unresolved-contact checks;
   do not reuse the older facade report's failed run as a successful baseline.

### Diagnostic artifacts and reproduction

The task-local harness is `/tmp/greenfdt-leaf-probe`, with the original simulator
frozen in `/tmp/greenfdt-leaf-baseline`. Corrected probe binaries are in
`/tmp/greenfdt-leaf-final-probe`; the contact-isolation variant is in
`/tmp/greenfdt-leaf-isolation`. Logs are `/tmp/greenfdt-leaf-*.log`; decoded
summaries, per-leaf histories and exported frames are in
`/tmp/greenfdt-leaf-results`. These temporary diagnostics are not runtime assets.

Examples (repository build artifacts must already be restored):

```bash
dotnet /tmp/greenfdt-leaf-final-probe/Probe.dll geometry
dotnet /tmp/greenfdt-leaf-final-probe/Probe.dll contacts 1
dotnet /tmp/greenfdt-leaf-final-probe/Probe.dll contacts 4
dotnet /tmp/greenfdt-leaf-final-probe/Probe.dll single 4 3 470 repeat-bergenia
dotnet /tmp/greenfdt-leaf-final-probe/Probe.dll panel 4 3 180 repeat-p003
```

The panel mode checks unresolved foliage contacts every step and retains a
failure checkpoint and export on the first failure. A completed single-plant or
seven-plant run does not replace that panel acceptance criterion. Source and
binary hashes for this implementation are retained with the diagnostic output.
