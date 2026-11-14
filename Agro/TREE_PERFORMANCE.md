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

## The contact pass on all cores (14 November 2026)

After the list above a mature tree still spent 1.4 of its 2.15 s per tick in the contact pass, on
one core for most of it: only the geometry of the candidate pairs ran in parallel. The second round
went through everything around that evaluation. Same checkpoints, same idle machine, 30 ticks:

| checkpoint | organs | baseline | first round | now | against baseline |
| --- | ---: | ---: | ---: | ---: | ---: |
| Celtis, year 14, mid-August | 270,633 | 2,742 ms | 2,150 ms | 1,075 ms | -61 % |
| Celtis, year 13, 1 February | 75,392 | 392 ms | 290 ms | 203 ms | -48 % |
| Acer, year 12, mid-August | 55,633 | 743 ms | 659 ms | 378 ms | -49 % |
| Amelanchier, year 12, mid-August | 21,826 | 352 ms | 308 ms | 175 ms | -50 % |

Where the tick of the 269k-organ Celtis goes now, in milliseconds, with the figure before this
round in brackets: foliage contacts 304 (810), solid pass and the application of the
foliage turns 103 (620), census 145 (312), gravity 165, organ tick
109, light 211. The tree now keeps twelve cores busy instead of six.

Every item is a way of getting the same numbers. Three patterns carry all of them:

- **A question about one organ is asked on all cores; what it leads to is done in organ order.**
  Whether an organ needs a push, is admissible, rests on something, what its bounds are: none of
  these reads anything another organ's answer writes. The pushes, the support reactions and the
  hierarchy updates that follow add into joints and nodes the organs share, so they stay serial
  and in the order they always had.
- **A walk that passes over almost every organ is replaced by a scan for the organs it does not
  pass over.** The scan is read-only and parallel; the organs it finds are then visited in the
  order the walk would have reached them.
- **A result that depends on the order of visits is put back into that order.** The per-tissue
  turn sum (`ContactTurnByOrgan`) is a float sum, so even this diagnostic is added up in the walk's
  breadth-first order after a parallel sweep, from values noted per joint.

9. **The plant's own candidate pairs are found and ordered on all cores**
   (`SoftContactEngine.CollectOwnPairsParallel`, `Bvh.CollectOverlapsUnder`). The serial search
   walked the whole hierarchy, looked up two stable ids per pair and sorted 1.4 million pairs by
   them: 245 ms. The result is a set in a canonical order, so any way of producing it is the same
   result. Each pair belongs to exactly one node of the hierarchy, so blocks of nodes are searched
   independently; and organs are born with increasing ids and a census keeps them in order, so id
   order is index order and the sort becomes one bucket per first organ. That the ids ascend
   strictly with the index is checked on every call; when they do not, the serial search runs as
   before. 245 to 20 ms.
10. **Every joint and organ is described once per sweep** (`PlantSubFormation.PrepareContactSweep`,
    `ContactJoint`, `ContactOrgan`). A contact asks every joint between the organ and the common
    ancestor for its stiffness, its bend and its position, three times, and an organ is in ten
    candidate pairs that each ask for its shape, its id and whether it moved. Read from the organs
    these are spread over the 400-byte agent, the tree cache, the lamina cache, the previous
    geometry and the soil that owns the field origin; described once they are two cache lines.
    There is one description and one use of it, with and without the cache
    (`DescribeJoint`, `DescribeOrgan`), so the cached answer is the uncached one. The descriptions
    are dropped by the sweep that turns the joints, before it turns anything.
11. **A pair's mobilities are measured with its geometry** (`MeasureMobility`). The split of a
    correction between two bodies walks both paths to the common ancestor; it reads the joints
    and writes nothing, and a response only queues turns that nothing reads before the sweep
    ends. For a plant whose organs do not change shape under contact the walk therefore gives the
    same value in the parallel evaluation as in the serial replay. Plants with contact deformation
    keep the serial order, because there a queued correction does change the next pair's answer.
    The replay went from 355 to 80 ms with items 10 and 11.
12. **The common ancestor is looked up only where it is used**: for a pair that touches, and for
    two rods, which can still be found touching by sampling along them. Most candidate pairs have a
    blade and do not touch.
13. **A sweep that turns a few joints visits a few joints** (`TurnQueuedJointsOnly`). The solid
    pass ends a sweep four times a tick, and each walked every organ to turn a few dozen: joints
    bent past their limit are clamped again every round. A parallel scan finds the joints that
    have a turn queued or stand outside their limit; they, and what they carry, are visited in
    breadth-first rank through a priority queue, which is the order of the walk. 140 to 8 ms.
14. **The sweep that turns everything runs on several cores** (`TurnJointsOnAllCores`,
    `TreeCacheData.SplitForWalk`). A contact between two limbs turns the joints down to their
    fork, however little, and everything above a turned joint is carried, so the foliage sweep
    takes the full step on nearly every organ. A joint needs its parent's transported turn and
    nothing else: the nodes that carry most of the tree are turned one after another, the subtrees
    left over side by side. The moved subtrees are then sorted by breadth-first rank and the
    turn sum is added in breadth-first order (`TreeCacheData.BreadthFirst`, built once per
    topology). 97 to 32 ms.
15. **A large moved subtree is rebased and refitted on all cores** (`TreeCacheData.UpdateBases`,
    `PlantSubFormation.RefitSubtree`). A node's base is its parent's base plus the parent's reach,
    in any order that takes parents first; a bound is a function of its own organ; and the
    hierarchy does not depend on the order in which its leaves are written. 131 to 30 ms.
16. **A later push round asks about the organs whose answer can have changed**
    (`SoftContactEngine.ResolveHard`): the ones the round before moved and the ones that have
    touched a solid this tick, whose contact is carried from round to round. For any other organ
    the round would repeat the question it asked last time, of the same pose, and get the same
    answer. The clearance cache's skip and query counters now count what was asked, which on a
    scene with walls is less than before; they are diagnostics.
17. **The solid pass's questions are asked on all cores** (`SolidQuestions`, `SupportQuestions`),
    and the admissibility of every organ is evaluated once, in parallel, into the memo both later
    walks read (`CollisionWorld.AllStaticallyAdmissible`). When nothing is inadmissible - a tree in
    the open, on almost every tick - neither walk runs. The solid pass went from 400 to 45 ms; what
    is left is mostly the load sum the rhizome base's support test needs.
18. **The rhizome organs are listed once per census** (`PlantSubFormation.RhizomeOrgans`) instead
    of being looked for among all organs five times a tick. An organ is a rhizome from birth or
    never: `AboveGroundAgent.IsRizome` is now `init`-only, so the compiler holds that.
19. **The collision hierarchy is built on several cores, node for node the same**
    (`Bvh.BuildOnAllCores`). The serial recursion numbers a node, then its whole left subtree,
    then its right one, and a subtree over k leaves has 2k - 1 nodes, so where every node goes is
    known as soon as the split above it is. The two halves of a split share no leaves, nodes or
    scratch. With the leaf bounds computed in parallel as well, the census's hierarchy build went
    from 168 to 37 ms.

Two things that went wrong on the way and are worth knowing:

- **A lambda in a recursive method allocates on every call, not only where it runs.** C# creates
  the closure object where the captured variables are declared, and captured parameters are
  declared at the method's entry. The first parallel hierarchy build allocated 26 MB a tick - one
  closure for each of 538,000 nodes, of which 130 forked. Keep the fork in its own method
  (`BuildHalvesSideBySide`, `PlaceBelowOnAllCores`, `RefitLargeSubtree`); the per-contact rebase
  and refit of a small subtree must not allocate at all.
- **A sweep's stage clock can hide the stage.** The evaluation's stage clocks start the "sampled"
  stage after the candidate gathering that decided to sample, so the gathering - the larger half
  of a sampled blade pair - appeared nowhere. Time the pair, then the stages.

## How "exact" is checked

The tree implementation is not committed, so "the previous code" exists as a frozen copy:
`~/greenfdt-fixtures/tree-accel/baseline/` holds the source snapshot, the patch against `HEAD` and a
Release build. Beside it, `runs/` holds one tree per species grown from seed 42 by the baseline, with
checkpoints and frames every February and August and, in the last three years, mid-May (flush and
bloom) and early November (leaf fall). Three checks, all against that baseline:

- `ab.sh 24 200 -- celtis/y11-summer ...` restores a checkpoint in both builds, runs 24 and 200
  ticks and compares the state and the exported frame. Twenty-one checkpoints of the three species -
  winter, flush and bloom, summer and leaf fall, 5,543 to 225,026 organs - all identical with every
  change of the first round in; twenty-six, from 113 organs up, with the second round in as well.
- `scenarios.sh` runs seventeen scenarios from seed in both builds and compares the soil's water and
  the exported frame after **every** tick: the soil alone at 1, 4 and 24 h ticks on serial and
  parallel fields, Carex over 100 and 450 days, Geranium, Campanula, a stand of four species beside
  a wall under each of the three engines in the parallel and in the sequential tick, and three young
  trees. All identical.
- `Agro --checkpoint-selftest`: 100 resume cases identical.
- `stress.sh` builds the working tree with every "large plant" threshold lowered to a few organs
  (the block of the pair search, the sweep's scan block, the minimum sizes of the parallel walk,
  bounds, questions, admissibility and hierarchy build) and restores the sources. The parallel
  paths written for a quarter of a million organs then run on every Carex, Geranium and Campanula
  of the scenario suite, beside walls and under all three engines - the certified one ends contact
  sweeps too - and through every resume of the self-test. The trees in an open field never touch a
  solid, so this is the only check of those paths with real pushes, supports and inadmissible
  organs. Same expectations: identical on every tick.

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

At 269k organs (Celtis, year 14, August), of 1,075 ms: contacts 408 (evaluation
192, replay 80, the rest 136), light 211, gravity 165, census
145, organ tick 109.

- **Pair evaluation.** Parallel, and bound by the sixteen physical cores: 6.3 s of thread time a
  tick. By kind of pair: blade on blade, touching, 42 % (18,500 pairs at 144 us); rod and blade,
  apart, 26 %; the rest under 10 % each. Half of the touching blade pairs' time is the gathering of
  candidate triangle pairs that ends in the decision to sample instead - exact as it stands, since
  the decision is the count. Getting it cheaper means a count that does not enumerate; nothing
  simpler than that was found. Removing the oriented-box prefilter for rods is *not* exact: its
  projections carry a micrometre of rounding at ten metres, more than the margin it tests against,
  so it can call a pair apart that the distance kernel would find just inside the band.
- **Replay.** 80 ms, serial: 190,000 contacts, two walks each to the common ancestor. Each joint
  has to receive its turns in pair order. A replay partitioned into connected blocks of the tree,
  each block applying the path segments that cross it in pair order, keeps that; it needs a block
  decomposition per topology and the segments emitted by the mobility walk.
- **Gravity** (165 ms, serial): the same fork over subtrees as the contact sweep, plus the
  load sum in a precomputed order (it is also what the solid pass's last 22 ms wait for).
- **Census** apart from the hierarchy (113 ms): every tick with a birth rebuilds the organ
  tree and its index from scratch. Append on births, remap on deaths.
- **Organ tick** (109 ms): one random stream per plant drawn in organ order; not parallel.
- **Acer's light scene** (a fifth of its tick): fill the plant's primitives in parallel at offsets
  from a prefix sum.

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
