# Leaf turnover and lifespan evidence

Reviewed 5 November 2026. `LeafMaximumAgeHours` is an age of eligibility for
herbaceous leaf replacement, not a guaranteed maximum lifespan. Ordinary renewal
removes the old petiole and blade and creates a replacement directly. Drought or
carbon stress instead commits blades to senescence, retains dead foliage for the
configured attachment period, and then sheds it.

## Age renewal

The herbaceous renewal probability uses floating-point `ageHours / 7000f` after
the species' eligibility age. Integer division previously held this probability
at zero until 7,000 hours (291.67 days), defeating the 150-, 180- and 200-day
settings. The generic `Default` behavior's independent `/ 4032f` age calculation
also needs floating-point division.

Existing structural and resource gates remain: only the first petiole segment
renews; its parent must have become a stem rather than remain a meristem; new
foliage must be permitted. Certified replacement transactions retire the old
foliage only when the replacement is accepted. These rules can retain leaves past
their eligibility age. The named herbaceous species do not use `Default`'s
calendar-driven autumn shedding; evergreen habit alone would not justify adding
the same autumn leaf drop to every species.

## What the report measures

`leavesShed` is the **sum** of whole ordinary foliage blades physically removed
in each week, calendar year or whole run. It includes ordinary replacement,
stress detachment and blades lost when an ancestor is removed. A segmented blade
counts once. Floral bracts, rejected replacement proposals and births cancelled
before becoming live do not count. Dead blades still attached count only when
removed, so this is detachment, not the onset of physiological death.

Counts are recorded at accepted census removal and folded every tick using the
same local midpoint calendar as the other measurements. Existing canopy area and
blade-count fields remain daily means; `deadLeafArea_m2` remains the mean area of
attached dead stress foliage. Geometric ray-traced LAI includes attached dead
blades. Neither zero dead area nor steady net LAI establishes absence of turnover.
The HUD labels its maximum explicitly as **peak LAI** and shows the shedding total.
Historical documents without the new count mean **not recorded**, not zero.

Live checkpoint version 38 introduced both the organ-removal counter and survey
accumulators/baselines. Earlier live checkpoints are rejected; their saved
playback and LAI documents remain readable. Version 39 additionally requires the corrected
soil transport, probability arithmetic and physiological timing.

## Botanical evidence and retained settings

No numeric preset was changed during this correction. The values below remain
provisional, particularly where only foliage retention rather than individual
leaf survival has been measured. A species or cultivar mismatch is not a basis
for transferring a lifespan constant.

| Preset | Eligibility age | Evidence and decision |
| --- | ---: | --- |
| Geranium macrorrhizum | 730 days | No matching primary individual-leaf lifespan estimate located. Retain provisionally. |
| Geranium × cantabrigiense | 365 days | No matching primary individual-leaf lifespan estimate located. Retain provisionally. |
| Bergenia cordifolia | 360 days | Tagged-leaf observations support survival into the following growing season and sometimes a second winter, but do not provide a replacement scalar for this cultivar/site. Retain provisionally. |
| Heuchera micrantha / 'Palace Purple' | 200 days | The quantitative study located concerns H. americana, a different species. Retain provisionally. |
| Campanula poscharskyana | 150 days | No matching primary individual-leaf lifespan estimate located. Retain provisionally. |
| Carex morrowii 'Variegata' | 400 days | A wintering experiment establishes winter persistence, not an individual-leaf lifespan. Retain provisionally. |
| Fragaria vesca 'Rügen' | 180 days | Wild F. vesca summer leaves function for about 4–5 months and winter leaves for 7–8 months. This supports seasonal cohorts rather than one new universal threshold; the cultivar was not studied. Retain provisionally. |

- Åström et al. (2015), [Morphological characteristics and photosynthetic capacity
  of Fragaria vesca L. winter and summer leaves](https://www.sciencedirect.com/science/article/pii/S0367253015000626),
  *Flora* 215, 33–39. Tagged wild rosettes near Helsinki; spring and autumn cohorts
  have different seasonal replacement schedules.
- Golovkin (1973), *Переселение травянистых многолетников на Полярный Север*,
  printed pp. 128–129, [primary monograph](https://botany.az/uploads/library/pdf/vq1.pdf),
  [institute bibliography](https://pabgi.ru/biblioteka-2/). Marked leaves at Kirovsk;
  Bergenia crassifolia belongs to the group retaining most overwintered foliage
  throughout the next growing season, with some leaves overwintering twice.
  [Kew's synonymy](https://powo.science.kew.org/taxon/urn%3Alsid%3Aipni.org%3Anames%3A790358-1)
  links B. cordifolia to B. crassifolia var. crassifolia, but does not remove the
  cultivar and climate limitations.
- Skillman, Strain & Osmond (1996), [Oecologia 107, 446–455](https://doi.org/10.1007/BF00333934):
  the roughly 6–7 month leaf lifespans concern **H. americana**, not this preset.
- [Effects of Different Light Conditions on Wintering in Evergreen Ground Covers
  (2010)](https://www.kci.go.kr/kciportal/ci/sereArticleSearch/ciSereArtiView.kci?sereArticleSearchBean.artiId=ART001513514)
  includes C. morrowii 'Variegata'; its winter-retention result supplies no lifespan estimate.
- Chicago Botanic Garden's [Geranium trial](https://www.chicagobotanic.org/downloads/planteval_notes/no22_geraniums.pdf)
  and [Campanula trial](https://www.chicagobotanic.org/downloads/planteval_notes/no31_bellflower.pdf)
  measure plant performance and winter survival rather than individual-leaf lifetimes.

## Regression scenarios

Use temporary net8.0 probes, seed 42, explicit 1-hour and 4-hour ticks and mode 0.
Track whole nonfloral blade stable IDs, not all-organ death counters.

- Fragaria and Heuchera must renew eligible, structurally permitted leaves near
  their 180-/200-day gates, before the former artificial 291.67-day threshold.
- An isolated Campanula leaf must have positive renewal probability after its
  150-day gate at both tick sizes; retain meristem, growth and admission gates.
- Count a multi-segment blade once, repeated death requests once, and an ancestor's
  distinct blades separately. Exclude floral bracts and uncommitted births.
- Attached dead foliage must not increment shedding until removal. A rejected
  replacement must not increment it before acceptance.
- Sum weeks and years to the whole-run total; repeated `Finish()` must not alter
  counts. Resume across week/year boundaries, including an unsurveyed removal,
  without losing or duplicating events. Compare parallel and sequential results.
