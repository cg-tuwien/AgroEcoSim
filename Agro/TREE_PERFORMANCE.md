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

## Gravity on all cores, and a profiler (14 November 2026, later)

Gravity was the largest phase of a mature tree's tick still on one core (165 of 1,075 ms). Three
pieces, each identical in every bit to the plain pass:

1. **The walk.** A woody joint reads its parent's carried turn, its own organ, the loads summed
   before the walk, and its children as the walk has not yet touched them (the mean moment over
   a rod reads each child's attachment offset, which the child's own visit turns later). It writes
   only itself. Any order that takes a parent before its children therefore gives the same bits:
   the joints that carry most of the tree are bent one after another and the subtrees left over
   side by side (`GravityOnAllCores`, the split of the contact sweep). The body of the loop became
   `GravityJoint`; herbaceous plants, which turn their whole subtree at every joint, keep the
   plain queue around it.
2. **The loads.** Each organ's own weight and centroid is independent of every other and is
   filled in parallel. The sums up the tree are float sums and depend on the order of their
   terms. The plain pass works in fronts: the leaves in index order, then the organs whose last
   child was in the front before, in the order those children came. So a child arrives at its
   parent in the front of its *height* above the leaves, and within a front in the order of its
   *key*: the leaf at the end of its chain of last-arriving children. Both are known once an
   organ's children are, so each organ adds its children sorted by (height, key) - the plain
   pass's order - and subtrees are summed side by side (`SumLoadsOnAllCores`). It serves the
   contact pass's lazy load sum as well.
3. **Bases and height.** A base is its parent's base plus the parent's reach, whatever the
   order; the height is a maximum (`PlaceAllOnAllCores`). The census's rebase uses it too.

| checkpoint | organs | before | now | gravity before | gravity now |
| --- | ---: | ---: | ---: | ---: | ---: |
| Celtis, year 14, mid-August | 270,633 | 1,069 ms | 905 ms | 164 ms | 24 ms |
| Celtis, year 13, 1 February | 75,392 | 206 ms | 170 ms | 42 ms | 12 ms |
| Acer, year 12, mid-August | 55,633 | 372 ms | 353 ms | 19 ms | 5 ms |
| Amelanchier, year 12, mid-August | 21,826 | 184 ms | 174 ms | 8 ms | 4 ms |

Both columns measured back to back on the idle machine, 30 ticks. Against the frozen baseline the
mature Celtis is at 2,742 -> 905 ms per tick, on 14.7 cores.

**`dotnet-trace` is installed now** (`~/.dotnet/tools/dotnet-trace`, a global tool). Only the
`dotnet-sampled-thread-time` profile works on this runtime (`cpu-sampling` needs .NET 10 and
`collect-linux`); it suspends every thread a hundred times a second, which triples a tick's wall
time, so read shares from it and never milliseconds. Three small scripts in
`~/greenfdt-fixtures/tree-accel/traces/` read the converted speedscope file: `main.py` (one
thread, inclusive and exclusive), `serial.py` (the main thread's time outside every parallel
loop: what a single plant still does on one core) and `under.py` (the callees of one method over
all threads, as a share of working time). Two traps:

- **A sample lands where a thread can be stopped, not where it was.** The runtime stops a thread
  at a return, so a short method called tens of millions of times a tick collects the samples of
  the loop around it. The trace put 46 % of the facade's computing in the body of
  `TriangleDistance`; a counter and two stopwatches put it in its 3 % of calls that measure in
  full. Shares by *inclusive* time of a large method held up; exclusive time of a small one did
  not.
- **A timing taken while anything else runs says nothing.** Twice a change that removed 60 % of
  the dominant operation "measured" as no gain, because a background job had the other cores.
  Count the operation first (counts do not depend on the load), then time on an idle machine.

What the main thread of the 269k-organ Celtis still does on its own, by share of its time in a
tick (`serial.py`, with the pieces above in): replaying the contacts on the joints 6.5 %, the
organ tick 5.5 % (shoot) and 1.9 % (root), the organ tree after a birth (`FinishUpdate`) 2.8 %,
the top levels of the light scene's hierarchy 1.9 %, the root census 1.6 %, the serial part of
the hierarchy refit 1.3 %, the breadth-first order per topology 0.8 %.

## The planted facade: what a tick costs there, and what was done about it

The facade is the scene that matters most - 4,134 herbaceous plants in the trays of the imported
FAV11 model, 1.77 million static triangles - and none of the work above reaches it: its plants
have a few dozen organs each and are already ticked side by side. Measured from stored states of
the salvaged run (`probe-facade/` in the fixtures; render mode 3, Mixed engine, idle machine):

| state | organs | baseline | after the tree work | with the sweep changes |
| --- | ---: | ---: | ---: | ---: |
| step 240 (10 February) | 200,616 | 281 ms | 295 ms | 294 ms |
| step 600 (11 April) | 578,233 | 1,879 ms | 1,888 ms | 1,406 ms |
| step 720 (1 May) | 724,705 | 5,051 ms | 5,103 ms | 3,240 ms |

Twenty ticks from each state. A stand of 400 plants of the seven species on a plain field, 600
steps from 1 March, took 295, 279 and 295 s in the same three builds: without trays there is
nothing for the sweep to do, and the differences are the noise of the measurement.

At step 600 all 25 cores are busy for the whole tick, 47 s of computing in 1.9 s, and it goes to
one place: **the mixed engine's admissibility test, 60 % of it in the sweep of a blade against
obstacle triangles**. The reasons, each counted:

- A leaf is "clear of obstacles" by its midrib's distance less its half width, so in a tray
  every leaf within its own width of a wall is tested in full: 34,000 of 158,000 shoot organs.
- **Half of the plants have an organ the pushes cannot free, every tick** - 7,500 leaves and
  2,700 floral organs inadmissible at the first exact round, 2,600 still at the eighth, many of
  them clear where they stand but with a wall between this tick's pose and the last (`hit=False`
  in `AGRO_TRACE_SOLID_ENFORCE`), which no push can change. Such a plant runs all eight
  `HardRounds`; each round turns joints near the crown, every organ above moves by a hair, its
  memoised verdict no longer applies, and it is tested again: 4.5 tests per organ per tick.
- One sweep meets 18 static triangles, subdivides to 23 intervals, asks the blade's own
  hierarchy 13 times and looks at 200 of its facets. The loop over those facets was two thirds
  of the sweep's time, and 3 % of the looks measured a facet in full (six piercing tests,
  fifteen closest features).

What was changed, all of it in `StaticCollider.Lamina.cs`:

1. **A sweep interval needs to know which of three bands the blade's distance is in, not the
   distance** (`BladeDistance`): below the collision threshold, above the clear threshold, or in
   between. Once one facet is found in between, a facet that a projection puts beyond the
   collision threshold need not be measured. A blade lying along a wall had every facet at about
   the least distance, so the plain loop measured them all, at every level: 26.2 million full
   measurements in four ticks, 10.0 million after, 973 intervals handed back to the plain
   loop. The decisions are the plain loop's - see the
   method's remarks for the guard that makes that so, sized to the rounding of a closest point
   at 37 m from the origin, and for the fallback to the plain loop when a measurement lands
   inside the guard. This is the one change in this file whose identity rests on an error bound
   rather than on doing the same arithmetic; it is checked frame by frame on the facade.
2. What an interval works out about the blade alone - support along a normal, arc bound, the
   pose half way and the motion to either side - was computed again for every static triangle
   and is computed once (`SweepInterval`).
3. A candidate facet shares its corners with its neighbours and the same blade is tested against
   one triangle after another; each corner is transformed once per pose (`BladeVertices`).
4. The distance test's projection onto the static triangle's own normal is tried first, with the
   triangle's half of it worked out once (`FoliageGeometry.FacetAxis`); it separates more than
   half of all candidates before the distance test is called at all.

What is left there, in the order of what it would buy:

- **The eight rounds.** A plant whose only inadmissible organs are ones a push cannot help still
  runs every round and re-tests itself each time. Ending the rounds when a round queued pushes
  only for organs it had already pushed without effect, or not re-pushing an organ whose
  inadmissibility is a crossing, would remove most of the 4.5 tests per organ - and changes
  results, so it is a proposal, not something done here.
- **A true blade clearance.** Measuring a blade's real distance to the obstacles once and
  skipping the test while its travel stays inside it would make 45 % of the blade tests
  unnecessary (counted, with no disagreement in half a million), but one measurement costs more
  than one test, so it only pays with a cache that lasts several ticks. Not built.
- The foliage pass (a fifth of the tick) and the light gather (5 %) are as they were.

## How "exact" is checked

The tree implementation is not committed, so "the previous code" exists as a frozen copy:
`~/greenfdt-fixtures/tree-accel/baseline/` holds the source snapshot, the patch against `HEAD` and a
Release build. Beside it, `runs/` holds one tree per species grown from seed 42 by the baseline, with
checkpoints and frames every February and August and, in the last three years, mid-May (flush and
bloom) and early November (leaf fall). Three checks, all against that baseline:

- `ab.sh 24 200 -- celtis/y11-summer ...` restores a checkpoint in both builds, runs 24 and 200
  ticks and compares the state and the exported frame. Twenty-one checkpoints of the three species -
  winter, flush and bloom, summer and leaf fall, 5,543 to 225,026 organs - all identical with every
  change of the first round in; twenty-six, from 113 organs up, with the second round in as well;
  twenty-two with gravity and the sweep changes.
- `probe-facade/` (`FacadeProbe hash`, with `FROM=states/stepNNNN.bin.gz`) runs the salvaged
  4,134-plant facade request on its imported model from a stored state and hashes the exported
  frame of every tick: identical to the baseline over 100 ticks from step 240, 60 from 480, 40
  from 600 and 20 from 720. It is the only check with trays, and the one that matters for any
  change to the solid pass.
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

At 269k organs (Celtis, year 14, August), of 905 ms: contacts 396 (evaluation
192, replay 80, the rest 124), light 208, census 126, organ tick 113, gravity 24.

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
- **Census** apart from the hierarchy (about 95 ms): every tick with a birth rebuilds the organ
  tree and its index from scratch. Append on births, remap on deaths; `FinishUpdate` alone, a
  depth-first walk over a stack of tuples, is 2.8 % of the main thread and can be three array
  passes over the breadth-first order with the same numbering.
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
