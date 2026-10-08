using System.Numerics;

namespace Agro;

/// <summary>Export-only triangle geometry. The simulation retains compact controls.</summary>
internal static class MechanicalMesh
{
    internal static MeshData? For(PlantSubFormation<AboveGroundAgent> ag,int organ)
    {
        var pose=ag.GetContactPose(organ);
        if(ag.HasContactDeformation && ag.GetOrgan(organ)==OrganTypes.Leaf)
        {
            var template=ag.GetLaminaTemplate(organ)!;
            var mesh=new MeshData { Indices=template.Indices.ToList() };
            for(var i=0;i<template.Vertices.Length;++i)mesh.Positions.Add(template.TransformVertex(pose,i));
            return mesh;
        }
        if(!pose.CurvedRod)return null;
        var result=new MeshData();
        const int sides=8;
        // The same three cylinders and two joint spheres emitted by BuildScene.
        // Their overlap closes the joints; all pieces retain one organ identity.
        for(var span=0;span<3;++span)
        {
            var offset=result.Positions.Count;
            var rotation=Quaternion.Normalize(pose.Rotation*ContactDeformation.RodRotation(pose.Deformation,span));
            for(var end=0;end<2;++end)for(var side=0;side<sides;++side)
            {
                var angle=2*MathF.PI*side/sides;
                result.Positions.Add(pose.RodNode(span+end)+Vector3.Transform(new Vector3(0,MathF.Cos(angle)*pose.Radius,MathF.Sin(angle)*pose.Radius),rotation));
            }
            result.Positions.Add(pose.RodNode(span));result.Positions.Add(pose.RodNode(span+1));
            for(var side=0;side<sides;++side)
            {
                var a=offset+side;var b=offset+(side+1)%sides;var c=b+sides;var d=a+sides;
                result.Indices.AddRange([a,b,c,a,c,d,offset+2*sides,b,a,offset+2*sides+1,d,c]);
            }
        }
        for(var node=1;node<3;++node)
        {
            var offset=result.Positions.Count;var centre=pose.RodNode(node);
            for(var y=0;y<=sides;++y)for(var x=0;x<=sides;++x)
            {
                var theta=MathF.PI*y/sides;var phi=2*MathF.PI*x/sides;
                result.Positions.Add(centre+pose.Radius*new Vector3(-MathF.Cos(phi)*MathF.Sin(theta),MathF.Cos(theta),MathF.Sin(phi)*MathF.Sin(theta)));
            }
            for(var y=0;y<sides;++y)for(var x=0;x<sides;++x)
            {
                var a=offset+y*(sides+1)+x+1;var b=a-1;var c=b+sides+1;var d=a+sides+1;
                if(y>0)result.Indices.AddRange([a,b,d]);
                if(y<sides-1)result.Indices.AddRange([b,c,d]);
            }
        }
        return result;
    }
}
