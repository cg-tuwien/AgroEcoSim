# Bergenia contact certificates: retained v23

The retained changes align growth-retry pair order with the main contact solver
and add conservative edge-plane lower bounds to triangle-distance queries.
No material coefficient, species dimension, growth duration, crown probability,
clonal setting, scene codec or deformation default changed in this step.

`biology-summary.json` records the order-only v23 comparison with v22. The final
pruned implementation reproduces those complete 14-day continuation states
byte-for-byte at both tick sizes. Scape growth improves partially at one-hour
ticks; flowering and developed-leaf density remain unresolved. These controlled
private snapshots are not supported checkpoint migrations or fresh 755-day runs.

`performance-pruning.json` compares order-only v23 with its retained distance
pruning. Each variant/tick has three independent processes, three warmups and
fifteen measured restored single ticks per process. Restore is excluded. Timing
processes run serially with other builds and simulations idle. The median changes
are -6.9% at one-hour ticks and -3.1% at four-hour ticks, with matching exported
geometry. Whole-tick allocation counts include physiology and lighting; they do
not establish zero steady-state per-contact allocation.

`performance-release.json` repeats the cumulative comparison against the verified
v21 native baseline, interleaving three independent processes per version/tick.
Median overhead is +14.4% at one-hour ticks and +1.6% at four-hour ticks, passing
the 25% gate for these two small fixtures. Corrected certificates change accepted
poses between versions. This scoped result does not clear the full release gate.

The logs cover deterministic CCD fixtures, 10,000 distance-pruning comparisons,
isolated petiole yield, full-crown partial growth, removal/reindexing, NativeAOT
checkpoint state/frame equality, four-plant scheduling, core/server builds and
five frontend suites. Worker-limit equality does not establish multicore speedup.
The four-hour isolated mechanics fixture changes the mechanical tick setting
after capturing identical biological geometry; it does not rebind a one-hour
weather history and advance it as a four-hour simulation.

`experiments/` contains rejected cache and deeper CCD experiments. Private v24
labels denote temporary builds only. Those experiments are absent from the final
source, and their additional growth is not a retained production result. Larger
stands, the repaired FAV11/P003 panel and V2 release acceptance remain outstanding.
Diagnostic harnesses, native executables and large checkpoint files remain under
`/tmp`.
