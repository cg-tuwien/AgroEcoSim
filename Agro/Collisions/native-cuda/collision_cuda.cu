// Optional ABI v1. Compile with nvcc; the managed simulator has no CUDA dependency.
#include <cuda_runtime.h>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <new>

struct Vec3 { float x, y, z; };
struct Quat { float x, y, z, w; };
struct Template { Vec3 centre, extent; float referenceHalfWidth; };
struct Pose { Vec3 base; Quat rotation; float length, width; int templateIndex; };
struct Query { int a, b; float clearance; int enabled; };
static_assert(sizeof(Template) == 28 && sizeof(Pose) == 40 && sizeof(Query) == 16, "ABI layout mismatch");
struct Context {
    int device{};
    cudaStream_t stream{};
    Template* templates{}; int templateCount{};
    Pose* poses{}; Pose* poseStaging{}; int poseCapacity{};
    Query* queries{}; Query* queryStaging{};
    int* axes{}; int* axisStaging{}; int queryCapacity{};
};

__device__ Vec3 add(Vec3 a, Vec3 b) { return {a.x+b.x,a.y+b.y,a.z+b.z}; }
__device__ Vec3 sub(Vec3 a, Vec3 b) { return {a.x-b.x,a.y-b.y,a.z-b.z}; }
__device__ Vec3 mul(Vec3 a, float k) { return {a.x*k,a.y*k,a.z*k}; }
__device__ float dot(Vec3 a, Vec3 b) { return a.x*b.x+a.y*b.y+a.z*b.z; }
__device__ Vec3 cross(Vec3 a, Vec3 b) { return {a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x}; }
__device__ Vec3 rotate(Vec3 v, Quat q) {
    const Vec3 xyz{q.x,q.y,q.z};
    const Vec3 t=mul(cross(xyz,v),2.f);
    return add(v,add(mul(t,q.w),cross(xyz,t)));
}
__device__ bool project(const Pose& pose, const Template* templates, int templateCount, Vec3 axis, float& lo, float& hi) {
    if (pose.templateIndex < 0) {
        const float a=dot(pose.base,axis);
        const float b=dot(add(pose.base,mul(rotate({1,0,0},pose.rotation),pose.length)),axis);
        lo=fminf(a,b); hi=fmaxf(a,b);
    } else {
        if (pose.templateIndex >= templateCount) return false;
        const auto& t=templates[pose.templateIndex];
        const Quat inverse{-pose.rotation.x,-pose.rotation.y,-pose.rotation.z,pose.rotation.w};
        const auto local=rotate(axis,inverse);
        const float relief=t.referenceHalfWidth>0 ? pose.width/t.referenceHalfWidth : 1.f;
        const float mid=dot(pose.base,axis)+local.x*(t.centre.x+1.f)*pose.length*.5f
            +local.y*t.centre.z*relief-local.z*t.centre.y*pose.width;
        const float radius=fabsf(local.x)*t.extent.x*pose.length*.5f
            +fabsf(local.y)*t.extent.z*relief+fabsf(local.z)*t.extent.y*pose.width;
        lo=mid-radius; hi=mid+radius;
    }
    return isfinite(lo) && isfinite(hi);
}
__global__ void separate(const Template* templates, int templateCount, const Pose* poses, int poseCount,
                         const Query* queries, int count, int* results) {
    const int i=blockIdx.x*blockDim.x+threadIdx.x;
    if(i>=count) return;
    results[i]=0;
    const auto query=queries[i];
    if(!query.enabled || query.a<0 || query.b<0 || query.a>=poseCount || query.b>=poseCount
        || !isfinite(query.clearance) || query.clearance<0) return;
    const auto a=poses[query.a], b=poses[query.b];
    Vec3 axes[6]={rotate({1,0,0},a.rotation),rotate({0,1,0},a.rotation),rotate({0,0,1},a.rotation),
                  rotate({1,0,0},b.rotation),rotate({0,1,0},b.rotation),rotate({0,0,1},b.rotation)};
    for(int axisIndex=0;axisIndex<15;++axisIndex) {
        auto axis=axisIndex<6 ? axes[axisIndex] : cross(axes[(axisIndex-6)/3],axes[3+(axisIndex-6)%3]);
        const float length=sqrtf(dot(axis,axis));
        if(!isfinite(length) || length<=1e-6f) continue;
        if(axisIndex>=6) axis=mul(axis,1.f/length);
        float al,ah,bl,bh;
        if(!project(a,templates,templateCount,axis,al,ah) || !project(b,templates,templateCount,axis,bl,bh)) continue;
        const float scale=fmaxf(1.f,fmaxf(fmaxf(fabsf(al),fabsf(ah)),fmaxf(fabsf(bl),fabsf(bh))));
        // This is only a proposed certificate. The CPU recomputes its axis and projections.
        if(fmaxf(al-bh,bl-ah)>=query.clearance*fmaxf(1.f,axisIndex<6?length:1.f)+scale*4e-7f) {
            results[i]=axisIndex+1; return;
        }
    }
}

#if defined(_WIN32)
#define EXPORT extern "C" __declspec(dllexport)
#else
#define EXPORT extern "C" __attribute__((visibility("default")))
#endif

EXPORT void agro_cuda_destroy(void* pointer) {
    auto* c=static_cast<Context*>(pointer); if(!c) return;
    cudaSetDevice(c->device);
    if(c->stream) cudaStreamSynchronize(c->stream);
    cudaFree(c->templates); cudaFree(c->poses); cudaFree(c->queries); cudaFree(c->axes);
    cudaFreeHost(c->poseStaging); cudaFreeHost(c->queryStaging); cudaFreeHost(c->axisStaging);
    if(c->stream) cudaStreamDestroy(c->stream);
    delete c;
}
EXPORT int agro_cuda_create(int abi, int templateSize, int poseSize, int querySize, void** result) {
    if(!result) return -2; *result=nullptr;
    if(abi!=1 || templateSize!=sizeof(Template) || poseSize!=sizeof(Pose) || querySize!=sizeof(Query)) return -1;
    int devices=0; auto error=cudaGetDeviceCount(&devices);
    if(error!=cudaSuccess) return static_cast<int>(error);
    if(devices==0) return static_cast<int>(cudaErrorNoDevice);
    auto* c=new(std::nothrow) Context(); if(!c) return -3;
    error=cudaGetDevice(&c->device);
    if(error!=cudaSuccess) { delete c; return static_cast<int>(error); }
    error=cudaStreamCreateWithFlags(&c->stream,cudaStreamNonBlocking);
    if(error!=cudaSuccess) { delete c; return static_cast<int>(error); }
    *result=c; return 0;
}
EXPORT int agro_cuda_set_templates(void* pointer, const Template* templates, int count) {
    auto* c=static_cast<Context*>(pointer); if(!c || count<0 || (count>0 && !templates)) return -2;
    auto error=cudaSetDevice(c->device); if(error!=cudaSuccess) return static_cast<int>(error);
    error=cudaStreamSynchronize(c->stream); if(error!=cudaSuccess) return static_cast<int>(error);
    Template* replacement=nullptr;
    if(count>0) {
        error=cudaMalloc(reinterpret_cast<void**>(&replacement),static_cast<size_t>(count)*sizeof(Template));
        if(error!=cudaSuccess) return static_cast<int>(error);
        error=cudaMemcpy(replacement,templates,static_cast<size_t>(count)*sizeof(Template),cudaMemcpyHostToDevice);
        if(error!=cudaSuccess) { cudaFree(replacement); return static_cast<int>(error); }
    }
    cudaFree(c->templates); c->templates=replacement; c->templateCount=count; return 0;
}
EXPORT int agro_cuda_set_poses(void* pointer, const Pose* poses, int first, int count, int capacity) {
    auto* c=static_cast<Context*>(pointer);
    if(!c || first<0 || count<0 || capacity<0 || first>capacity-count || (count>0 && !poses)) return -2;
    auto selected=cudaSetDevice(c->device); if(selected!=cudaSuccess) return static_cast<int>(selected);
    if(capacity>c->poseCapacity) {
        auto error=cudaStreamSynchronize(c->stream); if(error!=cudaSuccess) return static_cast<int>(error);
        Pose* replacement=nullptr; Pose* staging=nullptr;
        error=cudaMalloc(reinterpret_cast<void**>(&replacement),static_cast<size_t>(capacity)*sizeof(Pose));
        if(error!=cudaSuccess) return static_cast<int>(error);
        error=cudaHostAlloc(reinterpret_cast<void**>(&staging),static_cast<size_t>(capacity)*sizeof(Pose),cudaHostAllocDefault);
        if(error!=cudaSuccess) {cudaFree(replacement);return static_cast<int>(error);}
        if(c->poseCapacity>0) {
            error=cudaMemcpyAsync(replacement,c->poses,static_cast<size_t>(c->poseCapacity)*sizeof(Pose),cudaMemcpyDeviceToDevice,c->stream);
            if(error==cudaSuccess) error=cudaStreamSynchronize(c->stream);
            if(error!=cudaSuccess) {cudaFree(replacement);cudaFreeHost(staging);return static_cast<int>(error);}
        }
        cudaFree(c->poses);cudaFreeHost(c->poseStaging);
        c->poses=replacement;c->poseStaging=staging;c->poseCapacity=capacity;
    }
    if(count==0) return 0;
    std::memcpy(c->poseStaging+first,poses,static_cast<size_t>(count)*sizeof(Pose));
    return static_cast<int>(cudaMemcpyAsync(c->poses+first,c->poseStaging+first,
        static_cast<size_t>(count)*sizeof(Pose),cudaMemcpyHostToDevice,c->stream));
}
EXPORT int agro_cuda_evaluate(void* pointer, const Query* queries, int count, int* axes) {
    auto* c=static_cast<Context*>(pointer);
    if(!c || count<0 || (count>0 && (!queries || !axes))) return -2;
    if(count==0) return 0;
    auto selected=cudaSetDevice(c->device); if(selected!=cudaSuccess) return static_cast<int>(selected);
    if(count>c->queryCapacity) {
        auto error=cudaStreamSynchronize(c->stream); if(error!=cudaSuccess) return static_cast<int>(error);
        cudaFree(c->queries);cudaFree(c->axes);cudaFreeHost(c->queryStaging);cudaFreeHost(c->axisStaging);
        c->queries=nullptr;c->axes=nullptr;c->queryStaging=nullptr;c->axisStaging=nullptr;c->queryCapacity=0;
        error=cudaMalloc(reinterpret_cast<void**>(&c->queries),static_cast<size_t>(count)*sizeof(Query)); if(error!=cudaSuccess) return static_cast<int>(error);
        error=cudaMalloc(reinterpret_cast<void**>(&c->axes),static_cast<size_t>(count)*sizeof(int)); if(error!=cudaSuccess) return static_cast<int>(error);
        error=cudaHostAlloc(reinterpret_cast<void**>(&c->queryStaging),static_cast<size_t>(count)*sizeof(Query),cudaHostAllocDefault); if(error!=cudaSuccess) return static_cast<int>(error);
        error=cudaHostAlloc(reinterpret_cast<void**>(&c->axisStaging),static_cast<size_t>(count)*sizeof(int),cudaHostAllocDefault); if(error!=cudaSuccess) return static_cast<int>(error);
        c->queryCapacity=count;
    }
    std::memcpy(c->queryStaging,queries,static_cast<size_t>(count)*sizeof(Query));
    auto error=cudaMemcpyAsync(c->queries,c->queryStaging,static_cast<size_t>(count)*sizeof(Query),cudaMemcpyHostToDevice,c->stream);
    if(error!=cudaSuccess) return static_cast<int>(error);
    separate<<<(count+127)/128,128,0,c->stream>>>(c->templates,c->templateCount,c->poses,c->poseCapacity,c->queries,count,c->axes);
    error=cudaGetLastError(); if(error!=cudaSuccess) return static_cast<int>(error);
    error=cudaMemcpyAsync(c->axisStaging,c->axes,static_cast<size_t>(count)*sizeof(int),cudaMemcpyDeviceToHost,c->stream);
    if(error!=cudaSuccess) return static_cast<int>(error);
    error=cudaStreamSynchronize(c->stream);if(error!=cudaSuccess) return static_cast<int>(error);
    std::memcpy(axes,c->axisStaging,static_cast<size_t>(count)*sizeof(int));return 0;
}
