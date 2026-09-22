cmake_minimum_required(VERSION 3.24)
project(agro_collision_cuda LANGUAGES CXX CUDA)
add_library(agro_collision_cuda SHARED collision_cuda.cu)
set_target_properties(agro_collision_cuda PROPERTIES CUDA_ARCHITECTURES "86;89" CUDA_STANDARD 17 CXX_STANDARD 17)
# Preserve ordinary IEEE arithmetic. This is a conservative proposal kernel, never an exact-contact solver.
target_compile_options(agro_collision_cuda PRIVATE $<$<COMPILE_LANGUAGE:CUDA>:--fmad=false>)
