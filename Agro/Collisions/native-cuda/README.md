# Optional CUDA collision prototype

The CPU remains the default, including `AGRO_COLLISION_BACKEND=Auto`. Set
`AGRO_COLLISION_BACKEND=Cuda` to evaluate the optional library. Missing libraries,
unsupported ABI, missing drivers and CUDA initialization/query errors select CPU
with a diagnostic. `Cpu` never loads the library. This setting is operational and
is not part of saved playback or checkpoint state.

The library proposes separating-axis certificates for batches of whole organs.
Every returned axis is reconstructed and its projection checked by the existing
CPU geometry rules. Swept queries inflate required clearance by conservative
whole-organ motion bounds. Attachment exemptions and individual mesh features
remain CPU work, as do every ambiguous pair, contact normal, correction, support
reaction, contact history update and rollback. No GPU result permits penetration.

The integrated prototype dispatches **endpoint correction passes only**, combining
small crowns into batches rather than applying a dispatch threshold to every pair
of plants. Swept acceptance remains on CPU. A stronger swept certificate is safe
geometrically but can release an increment that the CPU's finite subdivision
budget suspends; experiments observed this difference. Keeping the existing CPU
sweep policy preserves backend-independent growth decisions. The conservative
sweep certificate verifier remains available for direct regression probes, but
an endpoint certificate never skips the production swept check.

Build on a machine with a CUDA toolkit supporting Ampere and Ada:

```sh
cmake -S Agro/Collisions/native-cuda -B /tmp/agro-cuda-build -DCMAKE_BUILD_TYPE=Release
cmake --build /tmp/agro-cuda-build --parallel
```

Without CMake, the equivalent Linux build is:

```sh
mkdir -p /tmp/agro-cuda-build
/usr/local/cuda-13.3/bin/nvcc -shared -Xcompiler -fPIC --fmad=false -std=c++17 \
  -gencode arch=compute_86,code=sm_86 -gencode arch=compute_89,code=sm_89 \
  Agro/Collisions/native-cuda/collision_cuda.cu \
  -o /tmp/agro-cuda-build/libagro_collision_cuda.so
```

Install `libagro_collision_cuda.so` beside the simulator's executable or into its
configured native-library search path. The C ABI uses explicitly sized blittable
records and checks their sizes at initialization; `LibraryImport` and `SafeHandle`
work with .NET 8/10 and NativeAOT without compiling .NET code at runtime. CUDA
compilation is optional and is not a dependency of the managed project build.

Immutable template bounds and capacity-managed query/pose buffers remain on the
device. Changed pose ranges use bounded, reusable page-locked staging memory;
each batch ends with one result readback and stream synchronization. The prototype
does not upload static terrain: its first bounded experiment is plant-pair SAT,
and a terrain/BVH offload should only follow evidence that it would remove a
remaining bottleneck. Maximum batch size is 16,384 pairs.

Before enabling automatic GPU selection, require all contact/sweep/rollback and
checkpoint tests to pass, then demonstrate **at least 20% end-to-end improvement**
over the optimized CPU path on the target Threadripper/RTX 6000 Ada, including
packing, transfer, kernel, synchronization, scalar verification and CPU fallback.
Measure initialization separately, and report batch sizes, rejection rates, CPU
and GPU times and memory. Small batches and heavily intersecting canopies may
remain faster on CPU. Automatic selection is deliberately not enabled by an
unmeasured constant or by GPU presence alone.

The development environment's CUDA 13.3 compiler successfully builds both targets.
The host has an RTX 3060 Ti with driver 595.71.05 (`nvidia-smi` reports CUDA 13.2).
The compiled library initializes successfully with host GPU access. Inside the
restricted development sandbox, `nvidia-smi` fails and CUDA initialization returns
status 304 (OS error); this is an access restriction, not a broken host driver.
GPU probes must run with device access. Compiler or device availability alone does
not enable Auto; the end-to-end performance gate still applies.
