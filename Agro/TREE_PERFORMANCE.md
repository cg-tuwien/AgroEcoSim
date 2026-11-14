# Tree run performance

What a long single-tree run costs, what was removed from it without changing a result, how that is
proved, and what is left. Started 13-14 November 2026 for the three tree presets (Celtis australis,
Amelanchier alnifolia 'Obelisk', Acer platanoides 'Globosum'), whose 20-year tuning runs take hours.

Every change recorded here is **exact**: exported frames and checkpoints are the ones the previous
code produced. No light sample, time step or mechanical rule was touched. The approximate options
that were designed and rejected for that reason are listed at the end.

## What a run costs

Three runs from seed 42 on 13 November: Release, .NET 8, 4 h ticks, render mode 3, mixed contact
engine, explicit roots, the frontend's default field (10 x 4 x 10 m at 0.05 m), one tree each, all
three sharing a 32-core machine.

| species | simulated | wall time | summer organs / leaves, last year | growth of the organ count |
| --- | --- | ---: | --- | --- |
| Amelanchier 'Obelisk' | 20 years | 5.6 h | 56,870 / 19,221 | linear, about +1,500 winter organs a year |
| Acer 'Globosum' | 20 years | 8.5 h | 149,440 / 33,018 | linear, about +2,800 a year |
| Celtis australis | 16 years, stopped | 8.5 h (year 16 alone 3.3 h) | 599,167 / 166,646 | x1.46 to x1.6 a year |

**Cost is linear in the organ count.** Per organ, gravity, the light scene, the leaf gather and the
census stay flat from year 13 to year 16 of the Celtis; only the solid pass steps up (about x1.5 per
organ between years 14 and 15, not yet explained). So a species' run time is its organ trajectory.
Amelanchier and Acer grow linearly and finish; Celtis multiplies its organs by 1.5 a year while
gaining 0.3 m of height, each simulated year costs 1.5 to 1.8 times the one before, and years 17 to
20 would need another 35 to 45 hours. No constant-factor optimisation outruns that; the crown growth
itself is the subject of a separate diagnosis (plan Stage 4).

A single tree is mostly serial: the outer tick is parallel across plants, and one tree keeps four to
six cores busy. Three runs side by side therefore cost little more than one - except in the soil,
below.

## Where a tick goes

`PhaseTiming` (`Agro/PhaseTiming.cs`) splits a step exactly, with four time stamps between the
callbacks, and the formations keep counters for their own share. `AGRO_PLANT_TIMING=n` prints one
`PLANT_TIMING` line every `n` steps; `AGRO_COLLISION_TIMING=1` prints the contact engine's stages.
Before this, a fifth to a half of a tree's tick was in code nothing timed.

Year 10, mid-August, ms per tick, machine shared with three other runs. Measured with changes 1 to
5 below already in, because the timers arrived with them; the unchanged build's gravity was about
twice and its idle soil tick 12 to 36 ms more:

| | Celtis, 38k organs | Acer, 30k | Amelanchier, 13k |
| --- | ---: | ---: | ---: |
| whole tick | 416 | 481 | 244 |
| foliage contacts (pairs within the crown) | 114 | 209 | 136 |
| solid pass (obstacles and soil) | 128 | 97 | 42 |
| light: scene build / leaf gather / growth guidance | 16 / 25 / 36 | 87 / 21 / 8 | 9 / 17 / 7 |
| gravity | 23 | 14 | 7 |
| census: collision hierarchy / the rest | 19 / 17 | 14 / 9 | 5 / 5 |
| organ tick, shoots / roots | 16 / 9 | 11 / 4 | 5 / 3 |

The foliage pass is the first or second phase for every species and more than half of an
Amelanchier's tick (a dense column: ten microseconds per organ against three for the Celtis). Acer
pays a fifth of its tick building the light scene, because a lobed blade and a samara are many
triangles each.

## What was removed, and why each is exact

Measured back to back on the same checkpoints, baseline against the changed build, 30 ticks each on
an otherwise idle machine:

| checkpoint | organs | before | after | |
| --- | ---: | ---: | ---: | ---: |
| Celtis, year 14, mid-August | 270,633 | 2,742 ms | 2,150 ms | -22 % |
| Celtis, year 13, 1 February | 75,392 | 392 ms | 290 ms | -26 % |
| Acer, year 12, mid-August | 55,633 | 743 ms | 659 ms | -11 % |
| Amelanchier, year 12, mid-August | 21,826 | 352 ms | 308 ms | -13 % |
| Celtis from seed, first 18 months (3,240 ticks) | under 120 | 43 s | 15 s | -65 % |

A seedling's tick was 12 ms throughout on an idle machine (31 to 39 ms with three runs sharing it);
it now falls from 9 ms to between 1 and 2 ms as the soil fills during the first year. The first
ticks after a restore are slower than that, because which columns were settled is not stored and
every column is walked once more.

1. **The soil skips columns in which nothing can move** (`SoilFormationRegularVoxels.Tick`). The
   hourly transport of a column reads and writes that column only, so an hour that changes no cell
   leaves the next hour the same inputs: the column is at a fixed point until rain or a root writes to
   it. Settled columns leave an active list; rain puts every column back and a withdrawal puts its own
   column back. Inside an active column the pass starts above the saturated zone: water only moves
   down into cells with room, so a source whose every reachable cell is at capacity writes nothing,
   and the soil fills from the floor up. This was 36 ms of every tick on the default field - the
   whole cost of a seedling's tick and 26 minutes of a 20-year run - and it spread over about 21
   cores, which is why three concurrent runs slowed each other's idle tick from 12 to over 30 ms.
   Measured on the Celtis fixture: 81.5 % of the cells are at capacity by August of the first year
   and all of them by its end, after which the transport has nothing to do.
2. **One bounds pass per tick instead of two** (`PlantSubFormation.Gravity`). Gravity ended by
   computing every organ's collision bounds and refitting the hierarchy; the census did both again
   before the contact pass, the first reader, ran. Under the soft and mixed engines nothing reads the
   hierarchy in between. The certified engine keeps the refit: its staged development lets another
   plant's growth trial query this plant while the formations are still ticking.
3. **The census does not recompute attachment points gravity just computed**
   (`BasesFollowGravity`), unless a message moved an organ in between (a herbaceous petiole
   reorienting) or the formation has no gravity pass (roots).
4. **The collision hierarchy maps agents to leaves through an array** (`Bvh.leafOfAgent`) rather than
   a hash table that a refit probed twice per organ.
5. **The contact pass sums the loads only when something reads them**
   (`PlantSubFormation.EnsureMechanicalLoads`). It summed every organ's weight up the tree a second
   time each tick, after gravity, for the support solve; on most ticks nothing rests on anything.
6. **Soil queries are not asked about organs out of the soil's reach**
   (`StaticCollider.AboveSoilReach`). On a regular field the soil is one horizontal face, so the
   vertical gap to it bounds every distance a soil query measures. When the gap exceeds the radius,
   the arc bounds of the sweep and the queries' own search bands, the contact query finds nothing,
   the previous base is outside the soil and the sweep certifies at its first test - which is the
   answer returned without building, hashing and walking. The admissibility test alone went from 59
   to 13 ms at 38k organs. Imported troughs (walls) and deformed rods take the queries as before.
7. **The contact sweep computes an organ's pose and bend limit only where it reads them**
   (`EndContactSweep`). A tree runs that sweep five times a tick over every organ, and almost all of
   them take the no-op branch.
8. **Growth-guidance rays are cast in parallel** (`RefreshGrowthGuidance`). What an apex sees does
   not depend on another apex; the rays of the due apexes run on all cores and the records are
   updated in organ order afterwards, as the single loop did. On a tree every short shoot is an apex
   and a sixth of them come due each tick: 117 to 15 ms at 106k organs.

Not optimisations, but needed on the way: `AGRO_UnwatchedGraceMinutes` (0 = never) so a run of
several hours survives a closed laptop - the server stopped any run nobody had watched for five
minutes.

## How "exact" is checked

The tree implementation is not committed, so "the previous code" exists as a frozen copy:
`~/greenfdt-fixtures/tree-accel/baseline/` holds the source snapshot, the patch against `HEAD` and a
Release build. Beside it, `runs/` holds one tree per species grown from seed 42 by the baseline, with
checkpoints and frames every February and August and, in the last three years, mid-May (flush and
bloom) and early November (leaf fall). Three checks, all against that baseline:

- `ab.sh 24 200 -- celtis/y11-summer ...` restores a checkpoint in both builds, runs 24 and 200
  ticks and compares the state and the exported frame. Twenty-one checkpoints of the three species -
  winter, flush and bloom, summer and leaf fall, 5,543 to 225,026 organs - all identical with every
  change above in.
- `scenarios.sh` runs seventeen scenarios from seed in both builds and compares the soil's water and
  the exported frame after **every** tick: the soil alone at 1, 4 and 24 h ticks on serial and
  parallel fields, Carex over 100 and 450 days, Geranium, Campanula, a stand of four species beside
  a wall under each of the three engines in the parallel and in the sequential tick, and three young
  trees. All identical.
- `Agro --checkpoint-selftest`: 100 resume cases identical.

**A raw checkpoint is not reproducible, and that is not a difference.** Agents are written as raw
memory, and the padding of a newborn agent (10 of a shoot agent's 400 bytes, 6 of a root agent's
160) is whatever the stack held. Six runs of the *unchanged* baseline from one checkpoint gave five
different raw hashes with identical frames; with the padding zeroed they agree. The probe therefore
zeroes padding before it hashes a state (`probe/Padding.cs`), and any raw-byte comparison of two
runs has to do the same. It also means two stored checkpoints of the same run can differ in a few
dozen bytes for no reason anyone should chase.

The fixtures must not live under `/tmp`: it is a RAM disk on this machine, and a reboot took the
first set.

## What is left, by measured share

At 271k organs (Celtis, year 14, August) after the changes: foliage contacts 810 ms, solid pass 536,
census and organ tick about 425, gravity 168, leaf gather 133, of 2,150. The tree keeps under six
cores busy.

- **Foliage pass.** Its evaluation is already parallel; pair discovery, the sort and the response
  replay are serial (the replay alone is 56-88 ms of an Amelanchier's 136-186). Planned: parallel
  discovery and sort, an O(1) common-ancestor query, a replay partitioned by branch so that each
  joint still receives its turns in pair order.
- **Solid pass.** What remains is the sweep itself, run up to four times because joints bent past
  their limit are clamped again each round, and the foliage turns' application. Planned: restrict the
  later rounds to subtrees that hold a turn, and parallel per-organ scans with a serial queue.
- **Census.** Every tick with a birth rebuilds the organ tree, its index and the collision hierarchy
  from scratch; in season that is every tick. Planned: append on births, remap on deaths, and a
  parallel hierarchy build with the same node layout.
- **Acer's light scene** (a fifth of its tick): fill the plant's primitives in parallel at offsets
  from a prefix sum.
- **Gravity**: fork over branch subtrees below the trunk; each organ's arithmetic is unchanged.

Not exact, designed and set aside: fewer light rays or sun positions, a sky radiance table, a refit
of the ray hierarchy (a different tree shape can change which of two equidistant hits wins), growth
guidance deferred to daylight, and a dormancy path that skips gravity (the elastic relaxation never
reaches a bitwise fixed point).

## Two things the measurements showed about the model, not its speed

- **The default field's soil has no drainage, runoff or evaporation.** Every cell is at capacity by
  the end of the first year, and the surface cells then accumulate rain without bound (at year 11
  the field holds 5.9 times its capacity and one cell 397 times). A tree on it never meets a water
  limit after its first summer. This is why the settled-column skip is so effective, and it is worth
  knowing when a preset's water response is tuned.
- **Celtis's organ count grows geometrically** while the other two grow linearly - but its leaf
  area at year 16 is not too high for its size, it is about half of a measured tree's. The
  simulated tree is 9.85 m tall with 166,646 leaves, about 270 m2 at the 16 cm2 of outline a
  simulated blade has. Taufflieb et al. (ISPRS Archives XLIX-B3-2026, 1123; street trees in Strasbourg,
  10-13 m tall with crowns 5-8 m across, unpruned for two years) collected every leaf of one
  hackberry in a net: **521 m2 of leaf, a true leaf area index of 11.4**; their laser estimates
  for the neighbouring trees were 384 m2 in May and 323 m2 in July after a pruning. A real hackberry
  of that size therefore carries 200,000 to 300,000 leaves, and a per-leaf simulation of it is
  several hundred thousand organs by nature. What the model lacks is the ceiling: at the measured
  rate the simulated tree passes 521 m2 around year 18, still under 11 m tall, and nothing slows it -
  dormant buds are never removed and no shaded branch is shed. See the plan's Stage 4 before retuning
  a coefficient to hide that.
