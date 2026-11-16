# Organ colours

How the colour of every above-ground organ is decided, how it travels, and why. Written
15 November 2026, after two reports: every stem of every species was the same bright green,
and old leaves and petals went black. Revised the same day after the first version was tried
in the app: healthy leaves too bright, dead leaves pale grey, petals washed out, and no
strawberry leaf ever died in autumn (see "Round 2"). Revised once more that evening: the
round-2 colours were dull in both viewers, "as if in a linear colour space" (see "Round 3").
**The tables of round 2 keep their lightness and hue; the chroma in them is superseded by
round 3.** Round 4 (16 November) gave Bergenia, Heuchera and both Geraniums a winter colour and made an
old leaf die through its colours instead of vanishing.

One place decides: [`OrganColorContext`](OrganColorContext.cs), built once per plant per
exported frame. It reads the plant and changes nothing, so the export stays parallel and a
frame does not depend on which frames were exported before it.

## What was wrong (measured with the probe below, on HEAD before the change)

| organ | authored | shown on screen | cause |
| --- | --- | --- | --- |
| dead Campanula leaf | brown (82, 60, 38) | (38, 25, 15) | phenology colours were written as **linear** bytes and read as **sRGB**: darkened twice |
| its petiole | same | (27, 17, 9) | same, side-lit |
| Persicaria leaf, healthy | (69, 124, 52) | (33, 91, 23) | same |
| any stem | - | (0, 255, 0) top-lit, (0, 202, 0) side-lit | viewers ignored the stem bytes and drew `#009900` under a hemisphere light of 3.175 |
| Taraxacum ligule | (255, 212, 35) | (255, 255, 13) | fixed colours were sRGB, but treated as an albedo under that light: three times over-exposed |

Three further defects sat behind those: the stress branch of the senescence blend read
`StressCurve` - a modifier triple - as an OKLab colour, which is black (latent only because
the agent's `Stress` is never assigned); the healthy-season tint was applied along the dry
direction instead of the species' own; and the late-drying pass *darkened* tissue that in
reality gets brighter.

## The wire convention

**Every colour a preset authors is an appearance. Every colour a frame carries is an
sRGB-encoded albedo.** `ColorUtils.AppearanceToWire(linear, exposure)` divides by the light
and encodes; nothing else writes a colour.

- `LaminaExposure = 3.175`: the viewers' hemisphere on a sky-facing sheet (`HEMI_INTENSITY`
  in `ThreeFrontend/src/render/core/Lighting.ts`, mirrored by the Godot viewer).
- `RoundOrganExposure = 1.845`: the mean of that hemisphere over the normals of a cylinder
  or an ellipsoid, `3.175 (1 + linear(#70)) / 2`. A vertical stem therefore shows its
  authored colour instead of 58 % of it.

`ThreeFrontend/tests/organColors.test.ts` pins both numbers against the lighting constants.
If the hemisphere ever changes, change them together.

The convention is exact for the default lighting. The daylight tier lights a sunlit face
about 1.5 times brighter and a shaded one much less, as it should.

**Frame flag 4, `OrganColors`**, is set by the exporter on every frame. With it, viewers
take the wire colour for `Stem` and `Meristem` as they always did for petioles and floral
axes. Without it - every frame stored before this change - they keep the old green-to-wood
ramp verbatim, the Celtis bark swatch included, so stored runs and fixtures render exactly
as before. A run resumed across the change switches look at the resume frame; nothing
retro-corrects old frames.

## Round 2: authored colours were pastel

Fixing the decode made every preset show exactly what it authored, and that exposed the
values themselves. Measured in CIELAB (D65) they were far lighter and greener than leaves are:

| | healthy leaf L\* | a\* | dead leaf L\* | chroma |
| --- | --- | --- | --- | --- |
| presets before | 51 - 64 | -33 to -38 | 58 - 63, then 20-25 % greyed | 24 |
| measured | 35 - 45 | -20 to -10 | 41 - 51 | 22 - 28 |

The measured healthy leaf is Munsell plant-tissue 5GY 4/4 (recorded for mature leaves in
plant patents; colorimeter studies put green leaves at a\* -20 to -2); the dead one is Munsell
10YR 5/4 - 7.5YR 4/4. The old pastel values had only looked acceptable because the double
decode darkened and saturated them. Geranium was the worst: both species used the generic
default curve at L\* 64 while their own presets sat unused.

So every leaf curve is now **authored in CIELAB** (`ColorUtils.OkLabFromCieLab(L, a, b)`),
beside the measurement it comes from, and `Agro --color-selftest` checks every preset against
those bands without running a simulation:

| | band |
| --- | --- |
| healthy mature leaf | L\* 25 - 54; green (a\* < -5) or anthocyanic (a\* > 5, L\* < 40); chroma >= 30 since round 3 |
| dead leaf, either path | L\* 38 - 58, chroma >= 18 (28 since round 3), hue 40 - 90 (brown) |
| a senescing leaf at any stage | L\* >= 30 |
| open petal | white (L\* >= 75) or coloured (chroma >= 35); Carex exempt, it has no perianth |
| petal at its drop | L\* >= 38 |
| stems, any stage | L\* 30 - 85 |

| species | mature leaf L\*, a\*, b\* |
| --- | --- |
| default, trees | 39 - 40, -18, 26 - 28 |
| Fragaria 'Rügen' | 35, -18, 25 (dark glossy) |
| Geranium macrorrhizum | 44, -13, 22 (matte grey-green) |
| G. x cantabrigiense | 42, -18, 27 |
| Bergenia | 36, -17, 20 |
| Heuchera 'Palace Purple' | 29, +30, -5 (burgundy, unchanged) |
| Campanula, Taraxacum, Persicaria, Carex | 40 - 42, -17 to -20, 25 - 30 |
| Lactuca | 50, -14, 16 (glaucous) |

Petals of the coloured species gained chroma: G. macrorrhizum magenta (50, 52, -10), fading
bluer as it drops; Bergenia (52, 57, -5); Persicaria (58, 38, 6). White, cream and yellow
corollas stay. A flower that states no colour is now white (`LeafPhenology.WhitePetals`); it
used to bloom in the LEAF curve, green.

Under any sun lighting tier a sunlit face is about 1.45 times brighter (linear) than these
values. That is physically right and stays.

## Round 3: the measured chroma is dull on a screen

Round 2 authored what a colorimeter reads on a leaf, and in both viewers the plants looked
washed out - "soft tones, missing saturation, as if in a linear colour space". Nothing was
wrong in either viewer: the stored run the report was made on (G. x cantabrigiense, 755 days)
was opened in the web app and its top-lit leaves measured the authored (84, 106, 53) to the
unit. The authored colour itself was the dull one: an olive at chroma 32, next to a terrain
drawn at chroma 60.

Three things make the colorimeter value the wrong one to author:

- **It is one leaf surface, not a plant.** In a canopy the light that reaches the eye has
  passed through leaves and bounced between them, and each pass multiplies the spectral
  contrast: canopy-scale absorption features are more than twice the single-leaf ones (Baret
  et al. 1994). The default viewer light traces none of that, so the authored colour has to
  carry it.
- **Foliage is remembered and preferred more saturated than it measures** (Bartleson 1961,
  eight of ten familiar objects, grass and foliage among them), and a screen is two orders of
  magnitude dimmer than daylight, which lowers colourfulness further (the Hunt effect).
- **The look everyone was used to was the double decode's.** A strawberry leaf authored
  (76, 145, 66) went out as linear bytes, was decoded as sRGB and lit at 3.175: (37, 125, 31)
  on screen, CIELAB (46, -47, 44), chroma 64. Round 1 showed the authored pastel instead
  (L\* 54, chroma 53: "too bright"); round 2 the colorimeter value (L\* 35, chroma 31:
  "pale"). The lightness of round 2 was right and stays.

So the rule is: **the measured lightness and hue, about 1.5 times the measured chroma.**

| colour class | chroma gain | example, before -> after (sRGB top-lit) |
| --- | --- | --- |
| foliage greens (hue 105-160) | 1.5 | default leaf (80, 101, 47) -> (67, 104, 28) |
| yellows, oranges, reds, browns, olives | 1.4 at chroma 28 and below, falling to 1.15 at 50 and to 1 at 80 | dead leaf (138, 102, 67) -> (146, 99, 50); Fragaria red (175, 45, 45) -> (182, 35, 41) |
| pinks, magentas, wine | 1.4 falling to 1.2 | G. macrorrhizum petal (192, 78, 137) -> (203, 65, 141); Persicaria (203, 112, 130) -> (221, 100, 128) |
| blues, violets | 1.25 falling to 1.1 | Campanula (110, 95, 180) -> (108, 92, 194) |
| whites, creams, greys, bark (chroma under 9, or L\* 85 and up) | 1 | unchanged |

Every colour stays inside sRGB, and a green whose blue channel fell under 28 was turned a few
degrees bluer instead of sitting on the gamut edge. Stems, scapes and buds took the same
gains; the lightest young-stem greens (L\* above 66) also came down five units, because a
light saturated stem is the "bright green" of the first report.

| species | mature leaf L\*, a\*, b\* (chroma) |
| --- | --- |
| default, Persicaria | 40, -27, 37 (46) |
| trees | 39 - 40, -27, 34 - 35 (44) |
| Fragaria 'Rügen' | 35, -27, 31 (41) |
| Geranium macrorrhizum | 44, -25, 36 (44), matte: a little lighter than the glossy species |
| G. x cantabrigiense | 42, -27, 39 (47) |
| Bergenia | 36, -25, 30 (39) |
| Campanula, Taraxacum, Carex | 41 - 42, -25 to -30, 38 (46 - 48) |
| Lactuca | 50, -21, 24 (32), glaucous |
| Heuchera 'Palace Purple' | 30, +41, -6 (41) |

Dead leaves end at L\* 40-55, chroma 30-36 (default (46, 14, 35) authored, (46, 13, 33) after
the late desaturation). The self-test gained two floors so this cannot drift back: healthy
leaf chroma at least 30, dead leaf chroma at least 28.

This is a statement about appearance on a display, not a measurement, and the gain is the one
number to revisit if the plants ever look garish: scale the chroma, keep the lightness and
the hue.

**A request that omits the autumn share now takes the preset's.** The same stored run carried
`AutumnLeafLossFraction: 0` for a species whose preset says 0.25: the species editor sends
every numeric setting, a tab loaded before the server was rebuilt did not know the new one,
and the C# default 0 means "off". `SpeciesSettings.InheritMissing` (the was-set flag the
scape emergence angle already had) gives an omitted share the preset's value and keeps an
explicit zero. Reload the tab after rebuilding the server all the same.

## Round 4: a winter colour, and old leaves that die instead of vanishing

Reported after two-year runs of Bergenia and Heuchera: no winter phase at all, and leaves
"disappeared but never went through colouring". Both were true, for separate reasons. The
stored Bergenia run (`a9e78ea1`, 3,030 frames) read every January leaf at CIELAB (34, -23, 28),
green, and held 56 leaves from August to February while its oldest ones were being replaced.

**Old leaves vanished green.** Past `LeafMaximumAgeHours` the herbaceous tick swapped a leaf for
a fresh one in the same tick (`RenewPetiole`): the old blade was removed green and a seedling-
sized one appeared at its node, all year round, winter included. Under the canopy response
(every herbaceous preset) the canopy controller now retires such a leaf instead
(`PlantFormation2.RetireAgedLeaves`, daily, not inside the autumn window): it is committed to
the seasonal path like an autumn leaf, dies over `LeafSenescenceSeasonalDuration`, hangs for
`DeadLeafAttachmentHours` and falls to a node bud that regrows a leaf when growth allows - so
not in winter. The swap's probability, `(age / 7000 h)^2` per hour, was near-certain within hours
of the maximum age for every preset, so the timing is kept. Plants without the canopy response
keep the swap. Carex's blades hang from a sheath (a petiole in the model), so its year-old
leaves now turn straw from May of the second year instead of being swapped green.

**There was no winter colour to see.** Bergenia's healthy-season curve pushed the green 0.024
along a magenta axis, which does not turn a green leaf red; Heuchera's deepened the purple
towards magenta all winter, the one direction 'Palace Purple' does not go. Both are re-authored
(`BergeniaCordifoliaPreset.BergeniaHealthySeason`, `HeucheraMicranthaPalacePurplePreset.HealthySeason`):

| species | season | full tint (sunlit), CIELAB | half the tint |
| --- | --- | --- | --- |
| Bergenia | early November | (34, 4, 20) dark gold | green-bronze |
| | early December | (31, 23, 16) copper-red | (34, -4, 29) |
| | January - February | (29, 33, 13) deep red | (33, 0, 29) dark gold; (31, 16, 22) copper at three quarters |
| | end of March | fading to bronze; green from late April | |
| Heuchera | June | (30, 39, -3) purple, a little bronzed in August | |
| | January | (27, 40, 17) mahogany | |

Winter reddening of evergreen leaves is anthocyanin made under cold **and** strong light (the
favoured explanation is photoprotection; Hughes 2011), and the nurseries say the same of
Bergenia: the colour is strongest on sun-exposed leaves and stays green in a mild winter or in
shade. So the tint is **per leaf**: `ColorModel.SeasonWeight` of the blade's light relative to
the plant's best-lit leaf (`PreviousDayEnvResourcesInvariant / DailyResourceMax`, 0.25 when
shaded, 1 from 70 % of the best), times the pigment it has had time to make
(`OrganColorContext.PigmentBuilt`): none for a leaf made since 1 March, so a blade unfolding in
March is green while the old ones are still red and a seedling sown on 1 March does not come up
in winter colours; otherwise a smooth ramp over the organ's first four weeks, so a leaf made in
October colours later than the old ones. A first version ramped with the blade's expansion and
let a fast-growing Geranium seedling reach full tint in days; a four-week ramp alone still
turned it olive-gold by 21 March. Petioles take the
full light weight and the same ramp. A partial weight
scales the lightness shift and the push linearly and the chroma loss by its square
(`ColorModel.WeightSeasonModifier`): scaled linearly, a half-tinted leaf crossed a grey olive at
chroma 15; this way it passes through gold and copper, which is the "dark red and dark gold" a
Bergenia shows in January. Fragaria's existing bronze winter cast is weighted the same way.

**The Geraniums have one too** (added on request the same day). Neither has a study of its own
on winter pigment, but the descriptions agree and the mechanism is the general one: G.
macrorrhizum is semi-evergreen and its overwintering leaves are "attractively tinted with red"
(Cornell) in "red, bronze and amber" (Gardenia.net, Here She Grows: "a deep maroon"); G. x
cantabrigiense 'Biokovo' keeps most of its leaves, which take "reddish tones in autumn and
winter" (Promesse de Fleurs, plant d'Avenir), "bronze and red" (Pleasant Run), "dark red"
(Greenthumb). In very hard winters G. macrorrhizum's foliage can collapse to brown instead; the
model has no frost damage, see "Not modelled". At full light, January:

| species | November | December | January | three quarters of the tint, January |
| --- | --- | --- | --- | --- |
| G. macrorrhizum | (42, 0, 26) olive-amber | (39, 15, 22) bronze | (37, 24, 20) bronze-red | (39, 10, 29) amber |
| G. x cantabrigiense | (39, 1, 26) amber | (37, 19, 19) bronze | (34, 29, 15) dark red | (36, 13, 27) copper |

A first, weaker version (G. macrorrhizum (39, 21, 20), G. x cantabrigiense (37, 21, 26), both
lighter and yellower) read in the web viewer as dry brown leaves over the brown soil rather
than as red-tinted ones; the tint has to reach a\* 25-30 at L\* 35-37 to read as red. The
self-test now requires a\* 15 and up on a sunlit January Geranium leaf. The probe's plants of
both Geraniums are small (8 to 14 leaves, see AGENTS.md on their late emergence), so all their
leaves are well lit and the whole plant colours; in a dense mat the inner leaves stay green.
Decoded frames, seed 42: G. macrorrhizum on 5 January of its first winter 7 of 8 leaves red
(a\* above 15) and 1 gold, bronze-gold in late March, green from mid April; G. x cantabrigiense
7 of 9 red and 2 gold. Seedlings sown on 1 March are green from the first frame, exactly as
before the change.

The export caches the colour of the grown healthy blades per weight step (nine steps, an
inline array in `OrganColorContext`, so a context still allocates nothing). No simulation state
was added, so `CheckpointVersion` stays 63; a resumed run continues under the new rules, and
`--checkpoint-selftest` still resumes byte-identically.

Measured against the committed build (`cfb0f7ca`), seed 42, 4 h ticks, render mode 3, one plant
from 1 March, frames decoded by the Godot core:

| species | leaves on 29 Feb of year 2, after / before | bloom, year 2, after / before |
| --- | --- | --- |
| Bergenia | 109 / 76 | 7 Apr - 8 Jun / 8 Apr - 14 May |
| Heuchera | 61 / 99 | 4 Aug - 7 Sep / 4 Aug - 9 Sep |
| Fragaria | 544 (137 browning) / 734 | 25 May - 1 Dec / 23 May - 2 Dec |
| Campanula | 1612 (749 browning) / 1779 | 21 Jun - 20 Oct, unchanged |
| Carex | 3768 / 3976 | 13 Apr - 29 May / 13 Apr - 20 May |
| G. macrorrhizum, G. x cantabrigiense | 10 / 10, 10 / 11 | unchanged; 28 May - 1 Jul / 25 May - 28 Jun |
| Taraxacum | 2 / 9 | spans shifted by up to three weeks, the same months |
| Persicaria, Lactuca (260 d) | - | 9 Jul / 10 Jul; 13 Aug / 22 Jul (Lactuca, still inside its July - September window) |

- **Winter leaf counts fall** where the leaf lifespan is short, because a leaf that ages out
  between December and February is no longer replaced by a fresh one in the same tick: node buds
  regrow only from March. Heuchera (200 days) and Taraxacum (110 days) carry the fewest leaves
  into March. That is the semi-evergreen habit, not a loss of the plant.
- **Bergenia carries more leaves in its second year**, on three seeds: 114 / 78, 134 / 116 and
  145 / 94 on 2 September. The year-old leaves are retired in March and April through bronze and
  brown while new crowns come; the swap had removed each one with its reserves. Its bloom on
  seed 7 runs 9 April - 19 May (before: 6 April - 3 June), so the June tail of seed 42 is the
  seed, not the change.
- Bergenia's leaves on screen (decoded frames): on 5 January of the first winter 23 of 29 red
  (a\* above 15) and 6 gold-bronze (a\* -5 to 15); on 19 February of the second 35 red and 21
  gold of 109, the rest green - a bigger plant shades more of itself. In the web viewer the
  plant reads dark red and dark gold in January and is green, bronze and brown in late
  November, with the autumn leaf loss in it.

## Leaves, petioles, petals: the phenology curve

`ColorModel.Evaluate(adulcy, season, stress, senescence, pathStress)` in
[`Plant/Leaf/Phenology.cs`](Plant/Leaf/Phenology.cs), in OKLab.

The senescence paths follow the pigments:

1. chlorophyll is dismantled first and unmasks the carotenoids - yellow;
2. the carotenoids follow;
3. what remains is the brown of oxidised, polymerised phenolics over pale cell wall.

Dry dead tissue reflects **more** than a green leaf across the visible: red brightens first
(the yellow stage), blue last (grey litter), and drying itself raises reflectance. So no path
ends darker than the living leaf and `LateDarkening` is zero; but the brown phenolics stay,
so every path ends on a mid brown, L\* 42 - 50, and the late greying (`LateDesaturation`) is
only 0.08. At 0.2 with tan end keys a dead leaf read as a grey tan. Black is frost, rot or
fire, none of which the simulation has.

A senescing leaf leaves its own colour fast and then follows the authored path: the blend
is `1 - (1 - s)^2.2` (2.8 on the stress path), 85 % on the path by half way. With the old
exponent of 1.15 a dying strawberry leaf stayed half green and its red stage peaked at a
dull a\* 24; it now reaches a\* 42.

`pathStress` chooses the path and nothing else, and it is **the leaf's own**: the agent's
`Stress` field (declared long ago, never written, already in the checkpointed layout) is set
when the leaf is committed and read at export. A leaf the drought controller retires in a
drought episode dries like hay, with chlorophyll still in it: dull olive, khaki, brown
(`StressSenescenceCurve`, path 1). A leaf retired for carbon gets the plant's water stress at
that moment; one resorbed in autumn, a tree's autumn leaf and a ripening annual go yellow,
then brown (`SeasonalSenescenceCurve`, path 0). Read off the plant at export, as in the first
version, a leaf changed path when an episode ended.

Left alone on purpose: `LifecycleCurve.Evaluate(adulcy * 0.5f)` - only the first half of each
lifecycle curve is reached, and every preset is tuned against that.

## Stems: `StemColorModel`

[`Plant/StemColorModel.cs`](Plant/StemColorModel.cs). Four authored colours and three scales.

| key | meaning |
| --- | --- |
| `Young` | fresh extension growth: transparent epidermis over chlorenchyma, paler and yellower than the blade because a stem holds a third to two thirds of the leaf's chlorophyll |
| `Mature` | a hardened herbaceous stem; a woody shoot's first periderm, its twig colour |
| `Old` | mature bark of a woody axis; the caudex or persistent base of a herb; every rhizome organ |
| `Dead` | standing dead, straw |

- maturity `m = max(woodRatio, age / MatureAgeHours)` - trees use the wood ratio the
  mechanics already compute, herbs (whose aerial stems never lignify) their age;
- ageing `o = max(smoothstep(R/4, R, radius), smoothstep(MatureAge, OldAge, age))` - girth
  drives bark because it is monotone and tapers base to tip; time browns a herb's base;
- dying `s`.

Colour = `Young -> Mature` by `m`, `-> Old` by `o`, `-> Dead` by `s`, in OKLab, tabulated per
species as 128 x 32 wire colours: one lookup per segment. Measured on the 169,499-organ
Celtis fixture: export 90 ms before, 87 ms after.

A meristem takes maturity from its wood ratio alone - a growing tip is fresh tissue however
long it has grown; a tree's resting bud is twig-brown because its wood ratio says so.

`dying` has two sources, both read-only:

- a spent herbaceous flower raises the wood factor of its bud, pedicel and scape to one
  before they are removed (`FlowerHelper.receit`) - that *is* a drying inflorescence;
- `PlantSubFormation.MonocarpicRipening()`: a monocarpic plant past flowering, from
  `FloweringSeasonEnd` to `SeasonalSenescence` (about 18 days for Persicaria, 29 for
  Lactuca). Stems go to straw and leaves and petioles run their seasonal path, so the annual
  yellows and dries before the census removes it. Colour only: nothing in the simulation
  changes. Do **not** call `GetFloweringPhase` from an export - it mutates phenology state,
  and the result would depend on the frame stride.

Vegetative stems of perennial herbs never reach `Dead`: the simulation has no stem
senescence or winter die-back, and this change does not add one.

## Autumn leaf loss of perennial herbs

Herbs used to lose leaves only to drought; `RenewPetiole` swapped an old blade for a new one
with no colour stage, so a strawberry kept every leaf green through the winter. Real
evergreen and semi-evergreen perennials lose their older summer leaves from autumn into
winter while the younger winter leaves overwinter: Fragaria vesca makes distinct summer and
winter cohorts and its summer leaves load more anthocyanin before they die; Geranium
macrorrhizum and G. x cantabrigiense colour orange-red; Heuchera, Bergenia and Carex lose
their oldest leaves over winter and are tidied in spring.

`SpeciesSettings.AutumnLeafLossFraction` is the share of the plant's green leaf area it sheds
each autumn, taken only from leaves at least four weeks old when the autumn begins
(`AutumnLeafLossStartSeason`, default 0.6 = 6 October; it runs until dormancy at
`SeasonalSenescence`). The canopy controller (`PlantFormation.Canopy.cs`) does it in a pass
of its own after the drought and carbon pass, so a plant with no share runs exactly the old
code: on the first daily update of the autumn it measures the summer leaves and how much of
them to keep, then commits the oldest and most shaded with the canopy half-time, over
`LeafSenescenceSeasonalDuration` (20 +- 5 days) on the seasonal path. They then hang dead for
`DeadLeafAttachmentHours` and fall to node buds, which regrow from March. Age-based renewal
pauses in the window, after its random draw - in autumn an old leaf dies, it is not swapped.
Four values join each plant's drought state; `CheckpointVersion` is 63.

| species | share | autumn path |
| --- | --- | --- |
| Fragaria 'Rügen' | 0.5 | yellow, strong red, red-brown, brown; the leaves that stay take a bronze-red winter cast |
| Taraxacum | 0.4 | yellow, red-purple flush, brown |
| Geranium macrorrhizum | 0.3 | gold-orange, orange-red, russet, brown |
| Heuchera, Campanula | 0.3 | plum to red-brown; yellow to brown |
| G. x cantabrigiense | 0.25 | gold, red to coppery orange, brown |
| Carex | 0.2 | straw |
| Bergenia | 0.15 | red-bronze, leathery brown |

The annuals keep their ripening; trees keep their own autumn.

**A lost leaf has to come back, and in the model it comes back from current leaves.** Spring
regrowth is fed by the canopy that survived, not by rhizome reserves as in a real perennial,
so a large share costs the next season's growth: Bergenia ends its second year with 96 leaves
instead of 132. The Geraniums are the extreme case: their node buds regrow only as
temperature and light allow (light reference 6000 Wh/m²), and G. macrorrhizum at a share of
0.5 never rebuilt enough canopy to flower in its second year (at 0 its bloom dates are
unchanged; at 0.3 it blooms 13 May - 5 June against 8 - 30 May). Check the second-year
bloom before raising a share.

## The inflorescence

- Scape, branches, pedicels: `FlowerSettings.ScapeColor` where the species states one
  (Bergenia and Heuchera red, the volunteers), otherwise the stem model. The four presets
  that set `(0, 0.6, 0)` "to match the stems" no longer need to.
- Bud: `FlowerCentreColor` only while it carries a petal, i.e. while the flower is open
  (the petals mark their parents once per frame). A closed bud is a calyx: `BudColor`, else
  the scape colour, else the young stem colour. Persicaria sets `BudColor` because its bud
  *is* the closed pink perianth.
- All of them dry towards `Dead` as above.

## Petals are shed, not burnt

`FlowerSettings.PetalAbscissionSenescence` (theta): the petal is removed when its senescence
reaches theta, and its colour is read at `senescence / theta`, so the authored curve ends
exactly at the drop. Petal fate is a family trait:

| fate | theta | species |
| --- | --- | --- |
| shed turgid, barely faded | 0.3 | both Geraniums (Geraniaceae), Fragaria (Rosaceae) |
| wilt, then drop | 0.5 | Lactuca, Taraxacum (Asteraceae ligules; the head closes over them) |
| wither in place, papery | 0.8 - 1 | Heuchera, Bergenia; Campanula (corolla persists); Persicaria (tepals persist around the achene); Carex (the slot stands for scales and anthers) |

This is the one part of the change that touches dynamics: a petal leaves up to
`(1 - theta) x PetalSenescenceDurationHours` earlier. The scape's lifetime is unchanged,
because the bud's wood factor continues at the same rate after the petals are gone. Tree
petals are deleted at their maximum age without senescence, so the setting is inert there.

## Probe

`Agro --color-selftest` checks the bands above in milliseconds. `ColorProbe`
(`~/greenfdt-fixtures/organ-colors/probe`, not in the repository) runs one
plant, exports the frame the server would store once a day, decodes it with the Godot
viewer's own `SceneDecoder` and `PlantColors.Natural`, and reports the on-screen colour per
organ type under the default hemisphere. It fails if any organ type other than a ripe fruit
has its brightest channel under 64. It reads the **exported** frame on purpose: a probe that
evaluated the curves directly would have passed while the wire was wrong. `restore <runDir>
<checkpoint>` does the same for a stored tree and adds stem colour by girth and the export
time; `plants=9` grows a mixed stand, whose frames must hash the same in parallel and with
`seq`.

Measured on HEAD and after (4 h ticks, seed 42, render mode 3):

| run | organ | before | after |
| --- | --- | --- | --- |
| Campanula, no rain | dead leaf | (38, 25, 15) | (148, 132, 106) |
| Taraxacum, no rain | dead leaf / petiole | (35, 25, 15) / (25, 17, 9) | tan; no leaf darker than the living green (72, 137, 48) |
| Lactuca, 260 d | stems, mean | (0, 202, 0) | (196, 199, 161) in June, (193, 174, 129) on day 240 with leaves at (179, 162, 119) |
| Lactuca, 260 d | wilting ligule | (115, 85, 38) | (234, 206, 99) |
| Heuchera, 520 d | withered petal | (91, 75, 57) | (182, 166, 140) |
| Bergenia, 450 d | withered petal | (73, 31, 31) | (180, 146, 132) |
| Celtis, 169,499 organs | stems by radius | one ramp | green under 2 mm, grey-brown twigs, (146, 143, 134) bark from 20 mm |

Round 2, against HEAD (seed 42, render mode 3, one plant in a 1x1 m field from 1 March;
colours are CIELAB as shown top-lit):

| run | what | result |
| --- | --- | --- |
| Fragaria, live WebGL canvas, July | healthy leaf | (67, 89, 41) sRGB against the target (66, 89, 41); round 1 showed (76, 144, 65) |
| Fragaria, live canvas, late October | autumn | dark green leaves over red, orange-brown and brown dying ones |
| Fragaria, 300 d, 4 h | autumn loss | first leaf turns 11 Oct; red stage a\* 42; 224 leaves at the peak, 134 by mid-December; none before March |
| Fragaria, 300 d, 1 h | the same | a\* 42; 344 at the peak, 213 by late December |
| Campanula, Taraxacum, no rain | drought-killed leaf | (45, 7, 19): mid brown; round 1 (56, 2, 16) grey tan; HEAD (38, 25, 15) sRGB, near black |
| eight perennials, 730 d | bloom dates, year 2 | within 5 days of HEAD for Fragaria, Campanula, Carex, Bergenia, Heuchera, G. x cantabrigiense; G. macrorrhizum 13 May - 5 June against 8 - 30 May (with its share at 0.3) |
| eight perennials, day 730 (29 February) | leaves | Fragaria 734 / 836, Carex 3976 / 4864, Campanula 1779 / 2512, Bergenia 76 / 132, Heuchera 99 / 130 (after / before) |
| nine mixed plants, 110 d | parallel against sequential | identical frame hashes |

Checked in the real WebGL viewport as well (`simstill.mjs` beside the probe: one species
through the app against the server, then a screenshot of the live canvas): an upward-facing
Taraxacum leaf lobe read (71, 136, 47) against the then-authored (72, 137, 47). The offline
WebGL still (`renderStill`) came out un-encoded in headless Chromium - terrain (77, 30, 0)
where the canvas shows (149, 96, 0) - but the PNG export is correct on real hardware, so that
is a headless artefact. `makefixture.mjs` looks for a Save button the app no longer has.

## Not modelled

- stem senescence and winter die-back of perennial herbs; for plants without the canopy
  response, age-based renewal still swaps a blade without a colour stage;
- leaf ageing in thermal time: a leaf ages as fast in January as in July, so evergreen herbs
  with a short `LeafMaximumAgeHours` lose more leaves over winter than they would (a dandelion
  ends February with two or three, and none regrow before March);
- winter colour from the weather: the tint follows the calendar, not frost or the site's
  temperature, so a mild winter colours as much as a hard one; and no frost damage, which in a
  hard winter browns G. macrorrhizum's and Heuchera's foliage instead of reddening it;
- a petiole paler than its blade (petioles follow the leaf phenology);
- anthocyanin flush on the sunlit side of a twig, lenticels, bark texture;
- frost blackening and wet rot.

## Sources

Senescence and dead tissue: [Autumn leaf color](https://en.wikipedia.org/wiki/Autumn_leaf_color);
Archetti et al. 2009, [Unravelling the evolution of autumn colours](https://www.fs.usda.gov/nrs/pubs/jrnl/2009/nrs_2009_archetti_001.pdf);
Krautler 2016, [Breakdown of chlorophyll in higher plants](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC4950323/);
[How the optical properties of leaves modify the absorption and scattering of energy](https://link.springer.com/chapter/10.1007/978-3-030-33157-3_14);
[Spectral reflectance overview](https://www.sciencedirect.com/topics/earth-and-planetary-sciences/spectral-reflectance) and
[Standards for reflectance](https://www.dcceew.gov.au/sites/default/files/documents/ssr195.pdf) (after Elvidge 1990);
standing litter greying in the Namib: [PLOS ONE 2015](https://journals.plos.org/plosone/article?id=10.1371%2Fjournal.pone.0126977).

Stems and bark: [The making of plant armor: the periderm](https://scispace.com/pdf/the-making-of-plant-armor-the-periderm-2du8g35h.pdf);
[bark of quaking aspen](https://digitalcommons.morris.umn.edu/cgi/viewcontent.cgi?article=2128&context=jmas);
[non-foliar photosynthesis review](https://link.springer.com/article/10.1007/s40003-026-01038-7).

Winter reddening: Hughes 2011, [Winter leaf reddening in 'evergreen' species](https://nph.onlinelibrary.wiley.com/doi/10.1111/j.1469-8137.2011.03662.x);
winter-red against winter-green evergreens, [J. Exp. Bot. 63(5)](https://academic.oup.com/jxb/article/63/5/1895/525718);
Bergenia winter colour, [Missouri Botanical Garden](https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?kempercode=b710),
[Gardenia.net](https://www.gardenia.net/plant/bergenia-cordifoliaheartleaf-bergenia), [Wikipedia](https://en.wikipedia.org/wiki/Bergenia);
Heuchera 'Palace Purple' (mahogany autumn colour, tattered winter leaves cleared in spring),
[Walters Gardens](https://www.waltersgardens.com/variety.php?ID=HEUPP), [JNI Plants](https://kb.jniplants.com/palace-purple-coralbells-heuchera);
Geranium macrorrhizum winter leaves, [Cornell](http://www.gardening.cornell.edu/homegardening/scene8fd1.html),
[Gardenia.net](https://www.gardenia.net/plant/geranium-macrorrhizum-cranesbill-grow-care-guide),
[Here She Grows](https://hereshegrows.com/2024/02/08/geranium-macrorrhizum-for-dry-shade/),
[Nature and Garden](https://www.nature-and-garden.com/gardening/geranium-macrorrhizum.html);
G. x cantabrigiense 'Biokovo', [Promesse de Fleurs](https://www.promessedefleurs.ie/perennials/perennials-by-variety/hardy-geranium-cranesbill/geranium-cantabrigiense-biokovo.html),
[Pleasant Run Nursery](https://www.pleasantrunnursery.com/plantname/Geranium-x-cantabrigiense-Biokovo),
[plant d'Avenir](https://plantdavenir.com/product/geranium-x-cantabrigiense-biokovo/),
[Greenthumb Wholesale](https://greenthumbwholesale.com/products/geranium-x-cantabrigiense-biokovo);
cold-induced anthocyanin in winter leaves, [Mikania micrantha, Front. Plant Sci. 2019](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6726734/).

Display appearance (round 3): Bartleson 1961, "Color in memory in relation to photographic
reproduction", Photogr. Sci. Eng. 5(6), as cited in the [CIE 2020 symposium papers](https://files.cie.co.at/x047_2020/x047_CIE_2020_Open_Access_Papers.pdf);
Fairchild, [Color Appearance Models](https://scis.uohyd.ac.in/~chakcs/cipclass/lecs/ColourAppearance.pdf) (memory colours, the Hunt effect);
Baret et al. 1994, [canopy reflectance sensitivity to leaf optical properties](https://www.sciencedirect.com/science/article/abs/pii/0034425794901465).

Measured colour: Munsell plant-tissue readings of mature leaves (5GY 4/4, 5GY 4/6) in
[US plant patent PP22505](https://image-ppubs.uspto.gov/dirsearch-public/print/downloadPdf/PP22505);
CIELAB stages of leaf senescence, [Photosynthetica 2022](https://ps.ueb.cas.cz/pdfs/phs/2022/02/08.pdf);
Munsell colour names for 10YR and 7.5YR browns, [USDA NRCS](https://www.nrcs.usda.gov/sites/default/files/2022-11/color-of-soil.pdf).

Autumn leaf loss: Fragaria vesca summer and winter leaf cohorts, [Flora (ScienceDirect)](https://www.sciencedirect.com/science/article/pii/S0367253023001068);
Geranium macrorrhizum, [RHS](https://www.rhs.org.uk/plants/details?plantid=862), [Missouri Botanical Garden](https://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?kempercode=f278), [Nature and Garden](https://www.nature-and-garden.com/gardening/geranium-macrorrhizum.html);
G. x cantabrigiense 'Biokovo', [Gardeners' World](https://www.gardenersworld.com/plants/geranium-x-cantabrigiense-biokovo-2/), [UW Master Gardener](https://mastergardener.extension.wisc.edu/files/2015/12/GeraniumBiokovo.pdf);
Heuchera, [Hayloft](https://hayloft.co.uk/how-to-grow-heuchera); Bergenia, [Gardeners' World](https://www.gardenersworld.com/how-to/grow-plants/how-to-grow-bergenias/).

Petals: van Doorn 2001, [Categories of petal senescence and abscission](https://research.wur.nl/en/publications/categories-of-petal-senescence-and-abscission-a-re-evaluation/);
van Doorn and Woltering 2008, [Physiology and molecular biology of petal senescence](https://academic.oup.com/jxb/article-pdf/59/3/453/1551847/erm356.pdf);
van Doorn and Stead 1997, [Abscission of flowers and floral parts](https://www.researchgate.net/publication/31248568_Abscission_of_flowers_and_floral_parts).

Species: Acer platanoides ([Wikipedia](https://en.wikipedia.org/wiki/Acer_platanoides), [NC State](https://plants.ces.ncsu.edu/plants/acer-platanoides/));
Celtis australis ([Trees and Shrubs Online](https://www.treesandshrubsonline.org/articles/celtis/celtis-australis/), [World Agroforestry](https://apps.worldagroforestry.org/treedb/AFTPDFS/Celtis_australis.PDF));
Amelanchier alnifolia ([Purdue Arboretum](https://www.arboretum.purdue.edu/explorer/plants/22475/), [Virginia Tech](https://dendro.cnre.vt.edu/dendrology/syllabus/factsheet.cfm?ID=193));
Lactuca serriola ([Kew POWO](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:228255-1/general-information), [Missouri Plants](https://www.missouriplants.com/Lactuca_serriola_page.html));
Persicaria minor ([Wikipedia](https://en.wikipedia.org/wiki/Persicaria_minor));
Taraxacum officinale ([Minnesota Seasons](https://www.minnesotaseasons.com/Plants/common_dandelion.html));
Bergenia ([KPU Plant Database](https://plantdatabase.kpu.ca/Plant/becf));
Heuchera 'Palace Purple' ([Missouri Botanical Garden](http://www.missouribotanicalgarden.org/PlantFinder/PlantFinderDetails.aspx?kempercode=d240)).

Leaf and dead-leaf colours are set from the measured CIELAB and Munsell values above; stem,
bark and petal colours are appearances chosen from the descriptions, not measured
reflectances.
