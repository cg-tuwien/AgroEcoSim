using System.Numerics;
using System.Runtime.CompilerServices;

namespace Agro;

/// <summary>Shared material seams, cut once without changing the rest surface.</summary>
internal static class ContactDeformationBinding
{
    static readonly ConditionalWeakTable<LeafMorphology, MeshData> Cache = new();
    internal static MeshData For(LeafMorphology morphology) => Cache.GetValue(morphology, static m =>
    {
        var mesh = LaminaTemplates.Build(m);
        // The sides must share a midrib edge. Triangles spanning it would shorten as
        // a crease closes, falsely registering strain and preventing an admissible fold.
        mesh = Split(mesh, 1, 0);
        mesh = Split(mesh, 0, -1f/3f);
        mesh = Split(mesh, 0, 1f/3f);
        return mesh;
    });

    static MeshData Split(MeshData source, int axis, float plane)
    {
        var result = new MeshData();
        var welded = new Dictionary<Vector3, int>();
        // Canonical edge intersections ensure neighbouring triangles share bit-identical
        // vertices; no tolerance welding can accidentally join the two edges of a tiny tip.
        var intersections = new Dictionary<(int,int),Vector3>();
        Span<Vector3> polygon = stackalloc Vector3[4];
        for(var triangle=0;triangle<source.Indices.Count;triangle+=3)
        for(var side=0;side<2;++side)
        {
            // A facet lying entirely on a cut plane belongs to one side only.
            if(side==1 && source.Positions[source.Indices[triangle]][axis]==plane
                && source.Positions[source.Indices[triangle+1]][axis]==plane
                && source.Positions[source.Indices[triangle+2]][axis]==plane)continue;
            var count=0;
            for(var corner=0;corner<3;++corner)
            {
                var a=source.Indices[triangle+corner];var b=source.Indices[triangle+(corner+1)%3];
                var p=source.Positions[a];var q=source.Positions[b];
                var inside=side==0?p[axis]<=plane:p[axis]>=plane;
                if(inside)polygon[count++]=p;
                if((p[axis]<plane&&q[axis]>plane)||(p[axis]>plane&&q[axis]<plane))
                {
                    var key=a<b?(a,b):(b,a);
                    if(!intersections.TryGetValue(key,out var cut))
                    { var start=source.Positions[key.Item1];var end=source.Positions[key.Item2];
                      cut=Vector3.Lerp(start,end,(plane-start[axis])/(end[axis]-start[axis]));cut[axis]=plane;intersections.Add(key,cut); }
                    polygon[count++]=cut;
                }
            }
            for(var corner=1;corner+1<count;++corner)
            {
                var a=polygon[0];var b=polygon[corner];var c=polygon[corner+1];
                if(Vector3.Cross(b-a,c-a).LengthSquared()<1e-20f)continue;
                Add(a);Add(b);Add(c);
            }
        }
        return result;
        void Add(Vector3 p)
        { if(!welded.TryGetValue(p,out var id)){id=result.Positions.Count;result.Positions.Add(p);welded.Add(p,id);}result.Indices.Add(id); }
    }
}
