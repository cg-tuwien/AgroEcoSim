using System.Numerics;
using Agro.Collisions;

namespace Agro;

public sealed partial class LaminaTemplate
{
    [ThreadStatic] static List<(int A, int B)>? SelfPairs;
    readonly record struct MaterialShape(float Length,float Width,Vector4 Angles);
    readonly record struct MaterialQuery(LaminaTemplate Template,MaterialShape From,MaterialShape To);
    struct MaterialResult { public bool Used, Clear; public MaterialQuery Query; }
    [ThreadStatic] static MaterialResult[]? StrainResults;
    [ThreadStatic] static MaterialResult[]? SelfResults;
    static MaterialShape Material(in OrganPose pose)=>new(pose.Length,pose.Width,pose.Deformation);
    // Fixed worker-local cache: continuous tests revisit the same triangle vertices
    // at endpoint and midpoint poses. Populate only requested vertices, and never
    // retain a buffer per organ or mutate a shared rest mesh.
    sealed class VertexCache
    {
        public LaminaTemplate? Template;
        public OrganPose Pose;
        public Vector3[] Points = [];
        public bool[] Known = [];
    }
    [ThreadStatic] static VertexCache[]? FoldVertices;
    [ThreadStatic] static int NextFoldVertices;
    VertexCache DeformedVertices(in OrganPose pose)
    {
        var entries = FoldVertices ??= Enumerable.Range(0,8).Select(_ => new VertexCache()).ToArray();
        foreach (var entry in entries)
            if (ReferenceEquals(entry.Template,this) && entry.Pose == pose) return entry;
        var selected = entries[NextFoldVertices++ & 7];
        selected.Template = this; selected.Pose = pose;
        if (selected.Points.Length < Vertices.Length)
        { selected.Points = new Vector3[Vertices.Length]; selected.Known = new bool[Vertices.Length]; }
        else Array.Clear(selected.Known);
        return selected;
    }
    Vector3 CachedVertex(VertexCache cache,int vertex)
    {
        if (!cache.Known[vertex])
        { cache.Points[vertex] = TransformVertex(cache.Pose,vertex); cache.Known[vertex] = true; }
        return cache.Points[vertex];
    }

    Vector2[] Midrib = [];
    public float[] HingeHeights { get; private set; } = [];
    float[] SampleCdf = [];
    int[][]? ConnectedSeams;

    void BuildDeformationBinding()
    {
        Midrib = Vertices.Where(p => p.Y == 0).Select(p => new Vector2(p.X,p.Z)).OrderBy(p => p.X).ToArray();
        HingeHeights = Vertices.Select(p => MidribHeight(p.X)).ToArray();
        SampleCdf = new float[Indices.Length/3];
        var total=0f;
        for(var i=0;i<SampleCdf.Length;++i)
        {
            var a=Vertices[Indices[i*3]];var b=Vertices[Indices[i*3+1]];var c=Vertices[Indices[i*3+2]];
            total+=MathF.Max(1e-20f,Vector3.Cross(b-a,c-a).Length());SampleCdf[i]=total;
        }
        if(total>0)for(var i=0;i<SampleCdf.Length;++i)SampleCdf[i]/=total;
    }

    internal void BindConnectedSeams()
    {
        // Refining a midrib seam can leave several very narrow facets between the
        // two incident faces. Exempt only its two vertex rings; distant material
        // on either side remains eligible for self-contact.
        var neighbours = new HashSet<int>[Vertices.Length];
        for (var i = 0; i < neighbours.Length; ++i) neighbours[i] = [];
        for (var t = 0; t < Indices.Length; t += 3)
            for (var a = 0; a < 3; ++a) for (var b = 0; b < 3; ++b)
                neighbours[Indices[t+a]].Add(Indices[t+b]);
        ConnectedSeams = new int[Indices.Length/3][];
        for (var t = 0; t < ConnectedSeams.Length; ++t)
        {
            var vertices = new HashSet<int>();
            for (var a = 0; a < 3; ++a)
                foreach (var v in neighbours[Indices[t*3+a]]) vertices.UnionWith(neighbours[v]);
            var connected = new List<int>();
            for (var other = 0; other < ConnectedSeams.Length; ++other)
                if (vertices.Contains(Indices[other*3]) || vertices.Contains(Indices[other*3+1]) || vertices.Contains(Indices[other*3+2])) connected.Add(other);
            ConnectedSeams[t] = connected.ToArray();
        }
    }

    internal float MidribHeight(float x)
    {
        if(Midrib.Length==0)return 0;
        if(x<=Midrib[0].X)return Midrib[0].Y;
        for(var i=1;i<Midrib.Length;++i)
            if(x<=Midrib[i].X)
            { var a=Midrib[i-1];var b=Midrib[i];return a.Y+(b.Y-a.Y)*(x-a.X)/MathF.Max(1e-20f,b.X-a.X); }
        return Midrib[^1].Y;
    }

    internal void SampleSurface(in OrganPose pose,float u,float v,float area,out Vector3 point,out Vector3 normal,out float weight)
    {
        var first=0;var end=SampleCdf.Length-1;
        while(first<end){var middle=(first+end)/2;if(u<SampleCdf[middle])end=middle;else first=middle+1;}
        var lower=first==0?0:SampleCdf[first-1];var probability=MathF.Max(1e-20f,SampleCdf[first]-lower);
        var root=MathF.Sqrt(Math.Clamp((u-lower)/probability,0,1));
        Triangle(pose,first,out var a,out var b,out var c);
        point=a*(1-root)+b*(root*(1-v))+c*(root*v);
        var cross=Vector3.Cross(b-a,c-a);var size=cross.Length();
        normal=size>1e-20f?cross/size:Vector3.Transform(Vector3.UnitY,pose.Rotation);
        weight=area>1e-20f?size*.5f/(probability*area):0;
    }

    public MeshData AuthoredMesh() => new()
    {
        Positions = Vertices.Select(p => new Vector3(p.X,p.Y/WidthScale,p.Z)).ToList(),
        Indices = Indices.ToList()
    };

    internal Vector3 MaterialPoint(in OrganPose pose, Vector3 point, out float hingeHeight)
    {
        hingeHeight = 0;
        var best = float.PositiveInfinity;
        var result = Vector3.Zero;
        // A contacted mesh is small and immutable; this scans only that blade, never a
        // stand's vertices. The returned barycentric material point also covers basal lobes.
        for (var triangle = 0; triangle < Indices.Length / 3; ++triangle)
        {
            Triangle(pose, triangle, out var a, out var b, out var c);
            var p = StaticCollider.ClosestPointOnTriangle(point, a, b-a, c-a);
            var d = Vector3.DistanceSquared(p, point);
            if (d >= best) continue;
            var u = b-a; var v = c-a; var w = p-a;
            var uu = Vector3.Dot(u,u); var uv = Vector3.Dot(u,v); var vv = Vector3.Dot(v,v);
            var denominator = (double)uu * vv - (double)uv * uv;
            if (denominator <= 1e-35) continue;
            var wb = (float)((Vector3.Dot(w,u) * (double)vv - Vector3.Dot(w,v) * (double)uv) / denominator);
            var wc = (float)((Vector3.Dot(w,v) * (double)uu - Vector3.Dot(w,u) * (double)uv) / denominator);
            result = LocalPoint(pose, Vertices[Indices[triangle*3]]) * (1-wb-wc)
                + LocalPoint(pose, Vertices[Indices[triangle*3+1]]) * wb
                + LocalPoint(pose, Vertices[Indices[triangle*3+2]]) * wc;
            hingeHeight = (HingeHeights[Indices[triangle*3]]*(1-wb-wc)
                + HingeHeights[Indices[triangle*3+1]]*wb + HingeHeights[Indices[triangle*3+2]]*wc) * ReliefScale(pose.Width);
            best = d;
        }
        return result;
    }

    static double Dot(Vector3 a,Vector3 b) => (double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z;

    /// <summary>Principal in-plane stretch of every facet, including shear.</summary>
    public bool WithinStrainTolerance(in OrganPose pose, Vector4 coordinates)
    {
        if(!float.IsFinite(pose.Length) || !float.IsFinite(pose.Width)
            || !float.IsFinite(coordinates.X+coordinates.Y+coordinates.Z+coordinates.W))return false;
        if(coordinates==Vector4.Zero)return true;
        var query=new MaterialQuery(this,default,new(pose.Length,pose.Width,coordinates));
        var cache=StrainResults ??= new MaterialResult[512];
        ref var entry=ref cache[query.GetHashCode() & (cache.Length-1)];
        if(entry.Used && entry.Query==query)return entry.Clear;
        var clear=WithinStrainToleranceUncached(pose,coordinates);
        entry=new(){Used=true,Query=query,Clear=clear};return clear;
    }

    bool WithinStrainToleranceUncached(in OrganPose pose, Vector4 coordinates)
    {
        var lower = (1-ContactDeformation.StrainTolerance)*(1-ContactDeformation.StrainTolerance);
        var upper = (1+ContactDeformation.StrainTolerance)*(1+ContactDeformation.StrainTolerance);
        for (var triangle = 0; triangle < Indices.Length / 3; ++triangle)
        {
            var a = LocalPoint(pose, Vertices[Indices[triangle*3]]);
            var b = LocalPoint(pose, Vertices[Indices[triangle*3+1]]);
            var c = LocalPoint(pose, Vertices[Indices[triangle*3+2]]);
            var u = b-a; var v = c-a;
            var aa = ContactDeformation.BladePoint(a, pose.Length, coordinates, HingeHeights[Indices[triangle*3]] * ReliefScale(pose.Width));
            var du = ContactDeformation.BladePoint(b, pose.Length, coordinates, HingeHeights[Indices[triangle*3+1]] * ReliefScale(pose.Width))-aa;
            var dv = ContactDeformation.BladePoint(c, pose.Length, coordinates, HingeHeights[Indices[triangle*3+2]] * ReliefScale(pose.Width))-aa;
            double g00=Dot(u,u), g01=Dot(u,v), g11=Dot(v,v);
            double h00=Dot(du,du), h01=Dot(du,dv), h11=Dot(dv,dv);
            var det=g00*g11-g01*g01;
            if (det <= 1e-35) continue;
            var trace=(g11*h00+g00*h11-2*g01*h01)/det;
            var product=(h00*h11-h01*h01)/det;
            var discriminant=Math.Sqrt(Math.Max(0,trace*trace-4*product));
            if (!double.IsFinite(trace) || .5*(trace-discriminant)<lower-1e-5 || .5*(trace+discriminant)>upper+1e-5) return false;
        }
        return true;
    }

    internal bool DeformationAdmissible(in OrganPose source, Vector4 coordinates)
    {
        if (!WithinStrainTolerance(source,coordinates)) return false;
        return SelfSweepAdmissible(source, source with { Deformation=coordinates });
    }

    internal bool SelfSweepAdmissible(in OrganPose source, in OrganPose target)
    {
        if (source.Deformation == Vector4.Zero && target.Deformation == Vector4.Zero) return true;
        // Material tests are independent of translation/rigid rotation. Keep a bounded
        // worker cache so support retries and final rollback validation reuse exactly
        // the same certificate; all dimensions and coordinates remain in the key.
        var query=new MaterialQuery(this,Material(source),Material(target));
        var cache=SelfResults ??= new MaterialResult[512];
        ref var entry=ref cache[query.GetHashCode() & (cache.Length-1)];
        if(entry.Used && entry.Query==query)return entry.Clear;
        var clear=SelfSweepUncached(source,target);
        entry=new(){Used=true,Query=query,Clear=clear};return clear;
    }

    bool SelfSweepUncached(in OrganPose source,in OrganPose target)
    {
        // Self-contact concerns the lamina midsurface. Normalize its material scale
        // and remove the common rigid frame, so micrometre seedlings and translated
        // facade leaves receive the same numerical crossing test. The small radius
        // is a numerical separation margin, not a new biological leaf thickness.
        var scale = MathF.Max(1e-20f, MathF.Max(source.Length,target.Length));
        var from = source with { Base=Vector3.Zero, Rotation=Quaternion.Identity,
            Length=source.Length/scale, Width=source.Width/scale, Radius=1e-5f };
        var to = target with { Base=Vector3.Zero, Rotation=Quaternion.Identity,
            Length=target.Length/scale, Width=target.Width/scale, Radius=1e-5f };
        var pairs=SelfPairs ??= [];
        // Sweep each facet's own endpoint bounds plus a conservative nonlinear arc
        // bound, rather than padding every facet by the whole blade's motion.
        QuerySelfSweepPairs(from,to,pairs);
        foreach(var (first,second) in pairs)
        {
            if(first>=second || (ConnectedSeams != null ? Array.BinarySearch(ConnectedSeams[first],second)>=0 : SharesVertex(first,second))) continue;
            var a0=new ContactShape(from,this,first); var a1=new ContactShape(to,this,first);
            var b0=new ContactShape(from,this,second); var b1=new ContactShape(to,this,second);
            if(!FoliageGeometry.SweepClear(a0,a1,b0,b1)) return false;
        }
        return true;
    }

    bool SharesVertex(int a,int b)
    {
        for(var i=0;i<3;++i) for(var j=0;j<3;++j)
            if(Indices[a*3+i]==Indices[b*3+j]) return true;
        return false;
    }
}
