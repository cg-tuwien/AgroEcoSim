using System.Numerics;
using Agro.Collisions;

namespace Agro;

public sealed partial class LaminaTemplate
{
    internal Vector3 Support(in OrganPose pose, Vector3 direction)
    {
        if (pose.Deformation != Vector4.Zero)
        {
            var bestPoint = pose.Base;
            var bestDot = float.PositiveInfinity;
            foreach (var vertex in Vertices)
            {
                var point = Transform(pose, vertex);
                var dot = Vector3.Dot(point, direction);
                if (dot < bestDot) { bestDot = dot; bestPoint = point; }
            }
            return bestPoint;
        }
        var local = Vector3.Transform(direction, Quaternion.Conjugate(pose.Rotation));
        var relief = ReliefScale(pose.Width);
        float best = float.PositiveInfinity; int selected = -1, i = 0;
        if (Vector.IsHardwareAccelerated && Vertices.Length >= Vector<float>.Count * 2)
        {
            var length = new Vector<float>(pose.Length); var half = new Vector<float>(.5f);
            var width = new Vector<float>(pose.Width); var height = new Vector<float>(relief);
            var x = new Vector<float>(local.X); var y = new Vector<float>(local.Y); var z = new Vector<float>(local.Z);
            for (; i <= Vertices.Length - Vector<float>.Count; i += Vector<float>.Count)
            {
                // Keep the scalar multiply/add order, including relief and width scaling.
                var dots = ((new Vector<float>(SupportX, i) * length) * half) * x
                    + (new Vector<float>(SupportY, i) * height) * y
                    + (new Vector<float>(SupportZ, i) * width) * z;
                if (!Vector.LessThanAny(dots, new Vector<float>(best))) continue;
                for (int k = 0; k < Vector<float>.Count; ++k)
                    if (dots[k] < best) { best = dots[k]; selected = i + k; }
            }
        }
        for (; i < Vertices.Length; ++i)
        {
            var v = new Vector3(SupportX[i] * pose.Length * .5f, SupportY[i] * relief, SupportZ[i] * pose.Width);
            var dot = Vector3.Dot(v, local);
            if (dot < best) { best = dot; selected = i; }
        }
        return selected < 0 ? pose.Base : Transform(pose, Vertices[selected]);
    }
}
