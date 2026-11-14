# Plant ages and scheduled establishment

The maintained implementation uses live checkpoint **58**, recorded scene **V9**
and a 400-byte above-ground agent (UG remains 160 bytes). The 9 November source
integration applied the 30 named paths in
`/tmp/tree-maintained-integration-20261109/adoption-proposal.json`; current builds
and compatibility evidence are summarized in
[the implementation review](TREE_IMPLEMENTATION_REVIEW.md). Earlier checkpoint
numbers below describe their original changes, not the current writer format.

Each entry in `SimulationRequest.Plants` accepts `StartOffsetHours`, a signed
duration relative to the requested `InitialDate`. Missing values are zero.
The Plants editor presents the value as **Age**, in days (24 hours) or nominal
years (365 days). Switching the displayed unit does not change the duration.

| Value | Establishment |
| --- | --- |
| `0` | An ungerminated seed at the recorded start, preserving the existing path. |
| Positive | An ungerminated seed introduced later; germination still depends on hydration and temperature. |
| Negative | A hydrated germinated juvenile introduced earlier and grown in the shared scene. |

For example, `-8760` requests one year of prior growth, while `720` introduces
a seed thirty days after recording starts. Negative values specify germination,
not merely sowing: the juvenile's initial water and reserve are explicit initial
stocks. Survival, size, flowers and fruit then follow the simulation. An annual
that dies before recording is not replaced or resurrected.

Events occur on the first tick boundary at or after their requested time. The
server first snaps `HoursPerTick` using its existing rule. The editor displays
the effective offset when rounding changes it. Fractions are supported; there
is no hidden rounding when saving a session. Nonfinite or unrepresentable
dates/timestep horizons are rejected.

## The two clocks

`AgroWorld.Timestep` is the monotonically increasing internal clock. The public
`RecordedStartStep` separates preparation from recording, and `RecordedStep`
reports completed recorded steps. `InitialTime` and `TimestepsTotal()` retain
their recorded-window meaning. Physiology, contact retries and organ birth
times always use the internal clock; no organ is backdated with an unsigned
timestamp and the world is never rewound at the recording boundary.

Preparation begins at the oldest plant's effective establishment boundary.
Internal indices remain aligned to the recorded origin's daily resource blocks;
unused leading indices are skipped, so the soil receives no extra transport or
rain before that boundary. `PreparationStartStep` is derived from the request,
and progress uses `PreparedSteps` out of `PreparationSteps`. All formations and
random streams are allocated in request order.
Pending formations do not tick, gather resources, collide, shade or export.

`Initialize.World` constructs without executing preparation. This is essential
for checkpoint restore. `world.Run(n)` automatically prepares history and then
runs `n` recorded steps. Interactive callers use `RunPreparationStep()` for
cancellation, progress and intermediate checkpoints. `PrepareHistory` also
accepts a cancellation token. Sequential/parallel diagnostic entrypoints use
their selected scheduler throughout preparation.

The same soil, terrain, obstacles and light model operate before recording.
Their state, including indirect-light feedback and soil depletion, is retained.
The existing recorded weather stream remains anchored to the requested start
month. Earlier months use deterministic seed-and-calendar streams, so adding
an older plant does not change rainfall/cloud cover on recorded dates. Dated
temperature overrides apply to both history and recording; missing dates use
the existing climate-normal fallback.

## Recording, persistence and UI

No historical frames are stored. Obstacle measurements and tray surveys begin
at the requested start date; historical shedding counters are baselined there.
Preparation sends a separate `Preparation(step, totalSteps)` SignalR event.
`SimulationResponse.CompletedSteps` is an absolute recorded count, including
steps before resume; zero means no recorded scene exists yet.

Live checkpoint version 58 includes the history boundary, pending activation,
woody/reproductive state and inherited woody shoot inclination, with woody
training heights referenced to the local soil surface and the bounded woody
water-request budget needed by the next root tick. It also preserves whether a
root segment starts a lateral axis, so serial continuations retain their correct
radius ceiling. A checkpoint during preparation is
stored at recorded step zero and resumes its internal clock without repeating
the history. Future plants beyond the original horizon remain pending and can
activate after an extension. Older live checkpoints are rejected; saved
playback readers remain supported.

It additionally carries accepted growth-unit/light state, real-axillary module
identity and retained developmental transactions, body-owned samara phases,
secondary-growth material state, and the effective module-control binding.
Restore requires the same root/contact modes and compatible module controls;
old live states are not reheadered or migrated by inferring missing state.
The new mainline selector defaults to `Off` in the class and presets. Its opt-in
`Auto` mode derives effective training from the supported clear-trunk form;
explicit `RealAxillary` retains its declared training height. These controls do
not turn a historical age into a nursery-size shortcut.

### Historical checkpoint changes

The preparation-start correction itself retained the version-42 layout and
verified unchanged zero-age/day-aligned histories. Version 43 added the
root-recharge decision; version 45 required bounded woody root rates and
the capacity-relative root branching signal. Version 46 replaces that decision
with a conserved demand budget and permits root throughflow, capped by basal
conducting capacity. Earlier live states are rejected.
Version 47 added the explicit root birth role and conservatively bounded woody
newborn water and water displaced by accepted geometry changes. Earlier live
states cannot supply the missing birth role and are not migrated by inference.
Saved playback remains readable; current histories skip the unused alignment prefix
from establishment.

Recorded scene version 9 adds a zero-based `sourcePlantIndex` to each visible
plant, preserving its original request position even while earlier plants are
pending or absent. The renderer's compact scene index remains separate. GLTF
plant names use the stable request index; optional metadata includes both
indices. Older scene versions remain readable and leave the unavailable source
index unspecified. Geometry assets retain their own v1/v2 versions.

`VirtualRoots: true` selects the aggregate root model; omission/false retains
explicit root segments. Root mode must remain unchanged for resume.

## Regression scenarios

The original scheduling, weather, terrain, request/session and preparation
reports retain their recorded version-47/50 pins. They are historical evidence,
not a claim that those complete suites were rerun on checkpoint 58. The maintained
builds, 100 targeted frontend tests, 3,002 exact Carex V9/census comparisons and
the separately pinned private-62 signed-history stages are identified in
[the implementation review](TREE_IMPLEMENTATION_REVIEW.md) and
[the handoff](TREE_WORK_HANDOFF.md). Mature all-species/root-mode acceptance
remains open.

A maintained checkpoint58 calendar bridge now passes 540 checks at 1 h and 4 h:
virtual roots, leap start 28 February 2024, mixed offsets [−48.5,0,+24.5] hours,
effective positive activation at 25/28 hours, and exact preparation/cold state,
V9, weather and reports. Actual mid-preparation resume at 6 of 12 steps preserves
the remaining six and four recorded suffix steps. Evidence is
`main-signed-age-calendar-bridge-v1/` (ZIP `0a95230d…`). This refreshes a focused
age/calendar path on the current format, not every historical integration suite.

The frozen 8658 signed-history diagnostic has completed internal 13610 with
recorded 0/preparing true in both quarter- and full-zone requests; matched saved
images isolate the declared proximal-fraction .25→1 control change. This is
historical framework development, not a recorded-start or mature-crown result.
The same full-zone history's continuation is authorized; no later completion is
claimed here.

Separately, the actual maintained native age-zero Maple catalogue case closes
its declared 4 h/Certified/virtual/Mode 0 reproductive coverage at 10490: ripe and
retained bodies shed with their own wings and surviving supports. A cold
component passes 210 checks, reproducing the 10489→10490 loss state/V9/reading/observer/events and
the ordinary cold suffix through 10494. Its exact native lineage and source-
predicate-consistent loss classification are in
[the implementation review](TREE_IMPLEMENTATION_REVIEW.md); it is not a new
all-timestep/environment scheduling matrix. Earlier controlled reproductive
fixtures and the stopped older CEA3 lifecycle retain their separate labels.

Use seed 42 and start with one to three plants, then compare 1-hour and 4-hour
ticks. Keep diagnostic harnesses under `/tmp`.

- Omitted versus explicit zero offsets: identical frames and checkpoint state.
- Mixed `[-48, 0, 24.5]` hours: existing juvenile at the recording boundary,
  seed at the start, future seed inert until its rounded activation boundary.
- Resume inside preparation, immediately before recording and after recording:
  identical exported geometry and measurement state to uninterrupted execution.
- A future offset beyond the first horizon, followed by extension and resume.
- Negative fractional offsets, winter starts, leap-year starts, and unchanged
  recorded weather when the historical interval is lengthened.
- A four-hour historical age at four-hour ticks executes exactly one historical
  step: no additional soil transport or rainfall from daily-block alignment.
- CPU light with a selected wall: no historical weeks/days/shedding in output,
  while physical shading feedback survives the boundary and checkpoint.
- Sequential versus parallel shared history, every contact engine, explicit
  and aggregate roots, and uploaded terrain constructed with a null world then
  attached through `SetWorld`.
- Editor save/load, imported planting distributions and stored-run forks retain
  signed hours; preparation cancellation with no scene completes the UI cleanly.
- An earlier scheduled plant becoming visible must not renumber existing plant
  request identities in playback or GLTF; legacy frames must not invent one.
