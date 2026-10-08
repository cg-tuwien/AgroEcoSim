# Coupled continuation witnesses: v26

The floral continuation retry now retains up to sixteen endpoint contact
witnesses and distributes corrections through their shared support system.
It improves isolated thickening and recovers a flower in the one-hour controlled
case. The four-hour crown still stalls, and release acceptance remains incomplete.

## Implementation and scientific scope

The small unilateral tangent response uses the existing compliance and contact
leverage, both bodies, and relative gradients below common ancestors. Its matrix
and dual variables use double precision; the existing geometry and gradients
remain floats. Already-cleared witnesses retain a conservative clearance allowance
instead of behaving as pinned seams. Canonical ordering preserves contact-side
memory. The just-evaluated penetrating witness is reused when its query order
matches, avoiding a redundant distance query without changing feature tie order.

Every changed body still receives endpoint and complete-motion certificates.
Rejected trials restore all participants exactly. Sixteen contacts, 256 joints,
64 dual sweeps, sixteen trials per dimension and eight corrections per trial are
work budgets, not equilibrium or physical-confinement tests. A nonsymmetric or
invalid local operator falls back to the existing single-contact response. Pending
births do not enter this continuation path. No tissue modulus, dimension, growth
duration, biological probability, resource debit or clonal-spread setting changed.

[C-IPC](https://ipc-sim.github.io/C-IPC/) supports coupled thin structures and
conservative complete-path checking. This bounded tangent retry is not its energy
minimizer or BAC V2. [RHS 'Rotblum'](https://www.rhs.org.uk/plants/72444/bergenia-rotblum/details)
describes stout flowering stems, leathery foliage and wavy blades; it supplies no
numeric stiffness ratio for this calibration. The field observation of a largest
16 cm blade, approximately 5 cm petioles and a 19.5 cm pot remains a separate
comparison, with unknown blade width and initial plant age.

## Mechanical checks

| Fixed fixture | v25 | v26 |
| --- | --- | --- |
| Scape thickening between two neighbouring plants, rigid blades | 3.6563 mm retained radial increment | 4.0219 mm retained, about 10% more; both neighbours yield |
| Same fixture with V1 shape controls | 4.8750 mm retained | 6.2156 mm retained, about 27.5% more |
| Two leaves sharing an unanchored crown support | 3.6563 mm retained | Same retained increment; both neighbours yield and the common support stays fixed |
| Single-witness scape extension | 17.9688 mm retained | Same retained extension |
| Solid ceiling or two confining walls | Exact restoration | Exact restoration, no invented foliage reaction |

All accepted bodies certify. Frozen accepted poses agree at one-hour and
four-hour settings, separately for managed .NET 8 and NativeAOT .NET 10.
The final two-row tangent responses agree with an independent active-set solve.
Native checkpoint checks pass through leaf removal, reindexing and cache
rebuilding, comparing saved bytes and actual exported frames.

The complete 52-organ day-240 crown rejects a manual 1,000/250 micrometre
length/radius proposal in both versions. Its solid checks pass; foliage yielding
creates additional conflicts. v26 performs 89 coupled tangent responses but
certifies no waypoint. This is an unresolved budgeted numerical case, not a
demonstration of impossible physical confinement or insufficient funding.

## Biological observations and a probe correction

The controlled request uses seed 42, CPU light, 1 September 2025, disabled clonal
spread and the original 755-day weather horizon. Fresh development runs stop at
day 275 for one-hour ticks and day 330 for four-hour ticks. They precede the final
clearance adjustment. Final-source continuations cover days 240–275 at both tick
sizes. The four-hour final continuation is byte-identical to the earlier v26
day-275 state. The one-hour continuation retains the flowering gain, with slightly
different frames and resource totals. The final query-reuse optimization preserves
all three mechanical fixture pose hashes; it has no separate full-season rerun.

**Reporting correction:** the development probe incorrectly divided leaf dimensions
by `GetLengthVar()` and `GetRadiusVar()`, which return variation offsets. Its raw
`half`, `ninety` columns and leaf threshold CSVs are invalid and must not be used.
Counts below were recomputed from saved agents using species dimensions plus
their sampled offsets, including segment and floral-leaf rules. Earlier reports
from other probes are not invalidated without checking their target calculation.

| Matched observation | Whole leaves | Both dimensions at 50% | Both at 90% | Open petals |
| --- | --- | --- | --- | --- |
| v25, 1 h, day 275 | 20 | 6 | 3 | 0 |
| v26 final continuation, 1 h, day 275 | 20 | 8 | 3 | 5 |
| v25, 4 h, day 330 | 20 | 4 | 2 | 0 |
| v26 development run, 4 h, day 330 | 20 | 4 | 2 | 0 |

The one-hour continuation first observes petals in its day-265 summary. Five
petals correspond to one open flower. Its 25 basal scape segments, crown stems,
petioles and leaves remain upright. Two of seven floral branch stems are inverted;
bud and pedicel axes also include downward orientations. Those flower postures
require separate botanical assessment; they do not establish acceptance.

Resources remain finite and no tick reports unresolved accepted foliage contacts.
Largest intrinsic blade axes are approximately 25 cm, with widths around 20 cm;
these are not fitted to the observed 'Rotblum' plant. Intrinsic axis length means
attachment-to-tip, distinct from the total basal-lobe envelope. Correct individual
50%/90% expansion times are **not established** by these runs: the faulty counters
cannot recover their history from final snapshots. The 84-day parameter remains
unchanged. Doubled developed-leaf density, flowering at both tick sizes, full
755-day reproduction and overwintering acceptance remain unmet or untested.

## Storage, performance and compatibility

There are no new persistent leaf or petiole fields. New solver scratch is bounded
per collision world and reused: 4,096 ushort slots (8 KiB), 256 matrix doubles
(2 KiB), two sixteen-double vectors (256 bytes), and bounded joint/gradient lists
and a joint map. Each gradient is 64 bytes; worst-case gradient capacity is 4,096.
The two-neighbour fixture uses eight joint entries and sixteen gradient capacity
slots. Lists and maps are reported separately from biological per-organ state.
Existing v25 continuation snapshots still consume 116 bytes per capacity slot.

Warm NativeAOT fixed-proposal benchmarks use ten warmups and 35 measured calls,
serially with growth runs and builds finished. Results and process-level variation
are in `results.json`. The ordinary single-contact path is similar, the rigid
two-neighbour case has a modest overhead, and V1 is faster in the measured fixture.
The shared-support case remains substantially slower. Across three final
processes, medians of process medians are:

| Retry kernel | v25 median / p95, ms | v26 median / p95, ms | Median change |
| --- | --- | --- | --- |
| Single-contact extension | 0.642 / 0.664 | 0.655 / 0.680 | +2.1% |
| Rigid two-neighbour thickening | 0.857 / 0.884 | 0.951 / 0.988 | +11.0% |
| Shared crown support | 5.537 / 5.667 | 8.342 / 8.575 | +50.7% |
| V1 two-neighbour thickening | 10.042 / 10.149 | 6.977 / 7.034 | -30.5% |

These are retry-kernel timings; the 25% end-to-end scene gate is not established.
Whole-call
allocations are measured, including the stopwatch; zero per-contact allocation
has not been established. Dense stands, the 790-plant panel, multicore scaling,
peak memory, lighting and archive-size gates remain outstanding.

Live checkpoints advance to v26 because continuation acceptance changes; their
payload layout is unchanged. Older live states are explicitly rejected. V7/V8
playback and geometry/profile assets remain unchanged. Core and NativeAOT server
builds and 34 scoped playback tests pass, with existing warnings. No default
deformation mode is enabled: biological and performance gates still fail.

`phase.patch` separates this increment from the pre-existing dirty worktree.
Compact raw evidence and its SHA256 manifest accompany `results.json`; temporary
diagnostic harnesses and approximately 52 MB checkpoint files remain outside the
repository.
