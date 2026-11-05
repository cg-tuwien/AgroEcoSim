# Numerical boundaries and timestep regressions

The November 2026 audit covered 194 first-party C# files, the browser code, the
native CUDA helper and repository scripts. These corrections address its 27
findings. Species lifespan constants are unchanged; see [leaf turnover](LEAF_TURNOVER.md).

## Simulation contracts

Regular voxel soil applies its existing empirical 0.012 m/hour transport scale
in hourly substeps, independent of the external plant timestep. Fractional travel
interpolates the two adjacent transport kernels; below one cell it transfers a
fraction and retains the rest. Transfers are limited by the available source
water and destination capacity. Rain for a tick is divided over its substeps.
Columns remain independent and may run in parallel. This corrects numerical
transport; it does not calibrate a soil hydraulic model or make plant uptake,
weather averaging and stochastic development invariant to timestep size.

Resource inheritance uses the most recent completed statistics block before the
current tick. A new organ retains the existing formation fallback until it has
contributed a full day. Spent flower support has a 0.05 hourly loss probability;
its tick probability is `1 - 0.95^HoursPerTick`. Individual seeded histories need
not lose support on the same hour at different tick sizes.

Positive senescence durations round upward to at least one tick. Propagated
deadlines are compared before subtraction, so an expired deadline cannot wrap.
The unused illustrative growth helper now compounds to exactly twice its initial
volume after 12 hours; production growth does not call it.

Live checkpoint 39 rejects older dynamics. Saved playback and reports remain
readable. Historical stored-run metadata is reconciled using the old request
conversion, including its former narrowing behavior; new requests store the
resolved timestep and reject values outside 1–65535 hours before conversion.
Stored metadata version 4 triggers this reconciliation.

Seeds travel as decimal strings, with numeric JSON still accepted by the server.
The browser protects legacy seed number tokens before parsing JSON. Step-duration
telemetry remains in 100 ns units but uses a monotonic clock and 64-bit integers.
Simulation-time export plans expose a length and indexed access without allocating
all output frames. Their final frame always names the requested final scene.

## Regression matrix

Use temporary net8.0 console probes outside the repository, seed 42 and explicit
tick lengths. Browser regressions are in the existing Jest suite.

| Finding | Regression and expected result |
| --- | --- |
| Fractional soil transport | Half-full 5 cm cells over four hours: positive movement, conserved total water, identical soil-only results at 1 h and 4 h. Repeat with 256 independent columns. |
| Zero-depth grid | Depths 0.20, 0.25 and 0.30 m at 0.50 m resolution initialize and run; invalid/nonfinite dimensions fail before allocation. |
| Tick/hour comparison | For birth at tick zero, own daily data first becomes eligible after the completed block: ticks 25, 7 and 3 at 1 h, 4 h and 12 h. |
| Flower support probability | Equal 24-hour survival probability at 1 h and 4 h; verify probability or a deterministic ensemble, not identical individual loss dates. |
| Zero petal duration | Fragaria's 96 h fade takes 96, 24 and 1 ticks at 1 h, 4 h and 168 h. Petals subsequently disappear. |
| Senescence underflow | Default at 720 h gets one positive duration tick; an inherited expired deadline completes rather than becoming 65,534 ticks. |
| Tiny probabilities | `AccumulatedProbability(1e-8, 24)` remains about `2.4e-7`; also test the fourth-root-branch hazard. |
| Stored tick metadata | Request 5 h runs and stores 4 h, including request JSON; reconcile historical records with mismatched metadata. |
| Tray zero | Two trays and fixed random 0.75: explicit indices remain `[0, 1]`. |
| Fractional export endpoint | Four simulated hours, 25 fps, 7 h/s: final frame is the last scene at exactly 4 h. |
| Zero export speed | Reject zero, negative, NaN and infinite speed/rate before constructing a plan. Large valid plans remain lazy. |
| LAI daylight saving | Vienna, start 2026-03-15, step 503 at 1 h: report date 2026-04-05. Match backend local-start-plus-half-tick arithmetic. |
| Noon daylight saving | Select actual local 12:00 across spring and autumn transitions. |
| Midnight conversion | Auckland 2026-09-27 starts at 2026-09-26 12:00Z; Sydney 2026-10-04 at 2026-10-03 14:00Z. |
| Wide seeds | Round-trip 0, 42, 9007199254740993 and ulong.MaxValue through JSON, browser reopen/resubmit and LiteDB; PCG draws remain identical. |
| Telemetry wrap | A 600-second step remains 6,000,000,000 ticks. |
| RNG certainty endpoint | State `(576458553280167936, 1)` must succeed at probability one. |
| RNG zero normalization | Seed 9624256412291681990 with one normalized weight gives 1, not NaN. |
| Inclusive RNG ranges | Singleton ranges return their sole value; full signed/unsigned ranges match raw draws without overflow. |
| Integer probability | Probability one converts to uint.MaxValue, not zero. |
| GUID seed sign extension | Lower hash occupies only the lower 32 bits; upper entropy is retained. |
| Leaf segment narrowing | Length 0.07 and segment length 1e-12 produces the existing cap of 16. |
| Child counters | A subtree with 256 leaf children propagates 256 blades to ancestors and leaves no waiting children. |
| Unused growth constant | Hourly factor is `2^(1/12)`; the 12-hour factor is 2. |
| Generated C# arithmetic | `1 / 2` generates 0, `1f / 2` generates 0.5; unsupported expressions fail explicitly. |
| Legacy importer narrowing | Segment boundary 256 remains 256 in the integer index array. |
| Public run-loop bounds | Unsigned lengths and cumulative timestep bounds cannot wrap; test both sequential and parallel entry points near uint.MaxValue. |

Also retain the all-species age-gate probes, physical whole-blade shedding counts,
weekly/yearly sums, dead-attached versus shed distinction, repeated Finish calls,
and checkpoint/parallel parity described in LEAF_TURNOVER.md. Full-year Fragaria
and Heuchera probes must compare stable blade IDs to the reported removal count,
and check water and mass for finite values.

The server's species-generation build uses one MSBuild worker and separate build
artifacts, then runs the built generator. This permits the normal server build to
validate the generator without a failing nested parallel build.
