# Collision acceleration

The CPU implementation is the default. It retains the growth, retry, light-sampling
and biological parameters described in [COLLISIONS.md](COLLISIONS.md). Numerical
ordering can differ from older builds; checkpoint/resume must reproduce the new
implementation exactly.

## Execution and geometry controls

- `SimulationRequest.CollisionGeometryToleranceM` defaults to `0.0005` metres.
  Values must be finite and in `[0, 0.0005]`; zero disables simplified geometry.
  The server records the resolved value with the saved request.
- `AGRO_COLLISION_BACKEND=Auto|Cpu|Cuda` selects an operational backend. `Auto`
  currently uses CPU. `Cuda` requests the optional native separation prototype;
  missing libraries, drivers or device errors fall back to CPU with a diagnostic.
  See [the native build and deployment instructions](Collisions/native-cuda/README.md).
- `AGRO_CollisionWorkers` caps contact workers, from one to the process's available
  processor count. Batches with at most 256 units of estimated work stay serial;
  larger batches scale the active worker count with estimated work and use coarse
  contiguous partitions. This threshold is provisional.
- `AGRO_COLLISION_TIMING=1` reports solid, foliage and developmental phase times,
  triangle/interval counts and correction, validation and fallback rounds.

`SoftCollisions` still controls foliage interactions; solid contacts remain active.
Checkpoint version **3**, agent layouts and playback formats are unchanged.
Hierarchies, simplified templates, worker scratch, query caches and CUDA buffers
are reconstructible. They are not added to checkpoints or individual frames.

## CPU changes

Organ and plant trees refit unchanged membership. Deterministic 16-bin partitions
replace recursive full sorting when rebuilding; normalized internal surface area
triggers a rebuild after 50% deterioration. Plant-pair discovery traverses the
plant hierarchy. Neighbour schedules and organ candidate lists are reused while
their dependencies remain unchanged. Obsolete neighbour jobs are retired.

Candidates are ordered by plant index and stable organ IDs, independently of BVH
layout. Edge-coloured batches give each job exclusive plant ownership. Contact
history uses visit epochs; counters accumulate per owner and reduce at phase
boundaries. Serial development and rollback queries are counted too, without
double-counting jobs run on the calling thread.

Geometry stamps include the current and previous pose, stable identity, parent
and eligibility. Whole-body and attachment results have bounded caches; membership
changes invalidate ID maps. Static proofs include the obstacle revision and active
solid-pass settings. Development queries use the plant hierarchy instead of
scanning the population.

Supporting components rebase and refit locally. Unchanged joints safely within
their cumulative bend limit remain exact no-ops, avoiding numerical movement from
repeated quaternion normalization. Trial arrays, hormone scratch and developmental
collections are reused. The full developmental fallback is retained where a
support trial can change other joints; it must not discard independent growth.

Shared triangle trees support dual-template traversal, including attachment-clipped
fragments. Box transforms use centre/extents arithmetic with outward roundoff.
Triangle distance evaluates each vertex/face and edge/edge feature once. Conservative
plane tests reject separated facets before the expensive distance calculation.
Swept whole-sheet certificates precede triangle subdivision. Support projections
use `Vector<float>` with a scalar fallback; geometry remains float32 and persistent
IDs remain 64-bit. A pose-hashed vertex cache was measured slower than direct
transforms and removed.

Collision simplification preserves the projected boundary, attachment and slice
boundaries. An overlay of original and simplified triangles certifies the complete
surface relief error, scaled by the actual blade width. The limit is the smaller
of the request tolerance, 0.5 mm and 5% of full blade width. Unsupported/folded
templates, segmented Carex blades and petals remain exact. In this implementation
simplification is a conservative separation prefilter: both errors inflate the
query, and ambiguous results use the original geometry. Full geometry remains
authoritative for contact features, suspension, development and continuous sweeps.

Global validation passes still run after corrections and rollback; their unchanged
pair results are reused. This is not a replacement of every validation pass with a
fully incremental contact graph.

## Other pipeline work

Gravity, scene construction, irradiance gathering and guidance have separate
timers. The dense facade measurements continue to be dominated by collision
processing, so gravity and the single vegetation light hierarchy have not been
replaced with another algorithm. Due guidance uses bounded independent plant jobs.

Checkpoint snapshots are taken between ticks. Their compression shares the saved
frame writer's queue and byte budget; a snapshot larger than the budget runs alone.
`CompleteRun` drains the writer before publishing completion. Snapshot ownership
transfers to the writer; it never reads the changing simulation world. Compression
and file formats remain unchanged.

## Measurement protocol and regression coverage

Use Release, seed 42, deterministic world execution, explicit one-hour/four-hour
ticks and fixed worker/runtime settings. Keep temporary console probes outside
the repository. Measure initialization separately; warm up before recording three
repetitions. Capture wall time, all-thread allocations, GC pauses, peak RSS,
candidates, cache reuse, subdivisions, correction/rollback rounds and saved bytes.
Do not compare the old incomplete worker counters against the new totals.

Exercise these cases in addition to the botanical scenarios in `COLLISIONS.md`:

1. Refit/rebuild trees against brute-force pairs, including transported plants and
   birth/removal membership changes; rebuilding must not reorder responses.
2. Stem/leaf and same-crown leaf contacts, short segments, both directions of a
   swept crossing, widening, finite edges, attachment clipping, tiny curved leaves
   and simultaneous tray contact. A coarse or device result must never certify
   separated endpoints of an intersecting sweep.
3. Compare bounded triangle distances with the original segment/triangle reference,
   including degenerate/tiny triangles; compare SIMD support with scalar extrema.
4. Compare zero tolerance and the default, including simplification-level changes.
   Check exported frames after warm-cache versus rebuilt-cache checkpoint resume,
   organ reindexing and guidance refresh. Compare 1/2/4/8/16/32 worker caps.
5. Exercise the bounded writer with queue depth one, a byte budget smaller than a
   snapshot, interleaved frames/checkpoints, and immediate completion. Read back
   every frame and the latest checkpoint byte-for-byte.
6. For CUDA, check ABI failure, no-driver fallback and all CPU verification axes.
   On working hardware compare actual device certificates, injected query failures
   and earlier/later chunks of a batch. Include packing, uploads, dispatch, readback,
   verification and fallback in the timing. Auto requires at least 20% end-to-end
   improvement on the target machine before enabling GPU execution.

The long acceptance run remains September P003 through overwintering and its next
April–June. Record monthly living leaf area, projected coverage, crowns, paused
tips, rejected births and mortality by reason, with finite mass and water. A short
snapshot timing does not establish the provisional 5% canopy-agreement bound.
The 2x speed and 50% allocation reductions are engineering targets, not guarantees.

## Measured results, 22 September 2026

Local hardware: Ryzen 9 5950X, 32 logical processors, RTX 3060 Ti. The probes used
a frozen .NET 8.0.31 runtime, Release binaries, seed 42 and isolated simulation
processes. P003 used eight available processors/contact workers and render mode 3.
The reference source was commit `201e5fd`; a temporary CPU-only enum/backend shim
was needed because its world constructor already referred to the optional backend
being developed in the worktree. The shim did not change reference collision code.

The local September OBJ and `fav11_seeds_P003.json` were loaded through the real
`TerrainBuffer` importer, with the frontend's coordinate/soil-offset convention.
The dense fixture was `canopy-validation/p003-guidance.bin`, step 360 at four-hour
ticks: about 124,000 organs, 66,000 leaves and 790 plant formations. Initialization
and checkpoint restoration took about 5.2–5.8 seconds, separately from ticking.

| Measurement | Reference | Optimized CPU |
| --- | ---: | ---: |
| Dense P003, mean of three ticks after one warm-up tick | 101.67 s | 46.92 s |
| Transient allocation per tick, same interval | 415.81 MB | 208.80 MB |
| Small four-plant scene, three repetitions, first tick excluded | 37.15 ms | 17.78 ms |
| Small-scene transient allocation per tick | 1.190 MB | 0.133 MB |

The four-tick profile above preceded the final worker-granularity cap. Later
independent two-tick repetitions of the final CPU path measured **47.52, 48.54 and
49.15 seconds** for the warmed tick. Other local runs reached approximately 57
seconds; timings vary with the execution environment and should not be treated as
a deployment guarantee. The measured dense improvement is approximately 1.8–2.2x;
the four-tick allocation reduction is 49.8%, close to the 50% target. The first tick
still allocates about 1.6 GB while reconstructing caches and scene data.

The final CPU repetitions peaked at 2.80–2.93 GB RSS. A reference process was
observed at 3.08 GB high-water RSS. The checkpoint/resume probe deliberately keeps
two worlds alive and peaked at 4.07 GB; that number is not comparable to a single
simulation's memory usage. Warm GC pauses in the final CPU repetitions averaged
25 ms per tick. Final-sample checkpoint size was 40.40 MB raw / 16.25 MB gzip-fastest;
the final frame was 7.15 MB raw / 5.80 MB zlib-fastest. The reference compressed
sizes were 16.29 MB and 5.80 MB respectively. Acceleration adds no playback payload.

Worker caps 1/2/4/8/16/32 were exercised three times each on a contacting four-plant
snapshot. All 18 exported frame sequences were identical. Mean tick times were
about 239/207/236/238/239/234 ms respectively; this small workload benefits from
two workers and is not a scaling result for a full facade. The final scheduler
limits workers by estimated work rather than starting all available workers for
every batch. A dense facade worker sweep on the deployment machine remains useful.

### CUDA evaluation

CUDA 13.3 (`nvcc` 13.3.73) built the native library for SM 86 and SM 89. Host driver
595.71.05 reports CUDA 13.2 and successfully executes the compiled SM 86 kernel.
The restricted sandbox fails `nvidia-smi` with exit code 9 and CUDA initialization
with status 304; the identical status command succeeds outside that sandbox.
Both actual device execution and the managed no-device fallback were tested.

An independent native probe ran 36,994 queries and checked all 33,259 returned
certificates against separately enumerated box vertices. Capacity growth, partial
pose uploads, disabled queries, invalid inputs and ABI rejection were exercised.
The managed verifier also rejected all 15 proposed axes for a swept crossing.

The first integrated prototype's per-pair threshold submitted no work on P003.
Batching now combines crowns and plant pairs into chunks of at most 16,384 queries.
An experiment that used stronger swept certificates changed suspension decisions
despite geometric clearance; that behavior was rejected. The production prototype
offloads endpoint correction checks and keeps swept acceptance on CPU.

Three isolated endpoint-prototype runs measured **49.05, 48.34 and 48.40 seconds**
per warm tick, versus the CPU's 48.40-second mean. Across each two-tick sequence,
CUDA submitted about 4.60 million queries and CPU-verified 2.44 million certificates.
Backend packing, transfers, kernels, synchronization, readback and verification
took about 0.55 seconds cumulatively across those two ticks; end-to-end timing also
includes grouping and CPU fallback. GPU process RSS peaked at 2.94–2.95 GB.
Per-process peak VRAM was not captured; the bounded native pose/query/result buffers
account for about 1.56 MiB plus shared template bounds and CUDA context overhead.

Every CPU/GPU export in those runs matched, including the separate removal/reindex
and checkpoint-resume test. The prototype provides no measured end-to-end benefit
on this workload, so **Auto remains CPU**. RTX 6000 Ada/Threadripper qualification
has not been performed.

### Functional checks and remaining acceptance

- Core build with `--no-restore -m:1`: passed. Server/consumer build: passed with
  `SkipSpeciesGeneration=true`; the separate generator invocation fails in this
  environment and was not changed. Existing compiler warnings remain.
- Six relevant frontend suites: **45 tests passed**, covering stored runs, run
  indices, playback decode parity, lamina slices and terrain formats.
- Twenty BVH refit/rebuild rounds against brute-force results; 20,000 triangle
  reference and bounded-query comparisons; 3,200 SIMD support comparisons; swept,
  short-segment, tiny-leaf and simplification-bound checks: passed.
- Four-plant 30-day runs at one-hour and four-hour ticks: finite mass, energy and
  water, no unresolved contacts, and exact resumed exports. Default versus zero
  geometry tolerance matched every four-hour frame. Timestep equivalence of plant
  density was not asserted; their existing developmental paths differ.
- P003 default versus zero tolerance: every frame in the four-tick continuation
  matched. Resume after the third tick matched the continuing world's next frame.
  The inherited fixture begins with two unresolved contacts; the reference still
  had two after four ticks, whereas the optimized run had zero. This does not
  establish that an arbitrary historical checkpoint is penetration-free.
- Removal/reindexing with guidance active, on CPU and CUDA: resumed exports matched
  for 12 ticks, and CPU/GPU frame sequences matched each other.
- Bounded storage writer: 20 interleaved frame/checkpoint pairs round-tripped
  exactly with queue depth one and a 1 KB byte budget; completion retained the
  latest checkpoint. `git diff --check` passed.

The **year-long and established second-spring P003 comparisons have not been run**.
Monthly living area/coverage agreement within 5%, a fresh sparse-facade timing set,
all facade worker counts and deployment GPU qualification remain unverified.
These snapshot results should not be presented as full seasonal acceptance.

Parallel scheduling follows [Microsoft's guidance on shared writes and small loops](https://learn.microsoft.com/en-us/dotnet/standard/parallel-programming/potential-pitfalls-in-data-and-task-parallelism).
The optional native prototype uses persistent device buffers and batched transfers
as described in [NVIDIA's CUDA best practices](https://docs.nvidia.com/cuda/cuda-c-best-practices-guide/).
