using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    // Material response is local to the carried unit reference. Its line integral
    // retains already formed tissue when a later cue reverses or reaches its goal.
    Vector3 TreeLightResponse, TreeLightStartResponse, TreeLightIntegral;
    float TreeLightPath;
    int TreeLightCycle;
    // A sampled developmental commitment survives obstruction and is not resampled
    // until its requested extension has actually been accepted.
    Vector3 TreeLightTarget;
    float TreeLightCommitmentEnd;
    bool TreeLightCommitment;

    internal readonly record struct TreeLightMaterial(Vector3 Response, Vector3 StartResponse, Vector3 Integral, float Path,
        int Cycle = 0);
    internal readonly TreeLightMaterial CaptureTreeLightMaterial()
        => new(TreeLightResponse, TreeLightStartResponse, TreeLightIntegral, TreeLightPath, TreeLightCycle);
    internal void RestoreTreeLightMaterial(in TreeLightMaterial value)
    { TreeLightResponse = value.Response; TreeLightStartResponse = value.StartResponse; TreeLightIntegral = value.Integral; TreeLightPath = value.Path; TreeLightCycle = value.Cycle; }

    internal readonly record struct TreeLightStep(float StartExtension, Vector3 Response, Vector3 Integral,
        Vector3 SegmentStartResponse, float Path, Vector3 Target, float CommitmentEnd, float Rate, float Budget,
        int Cycle = 0)
    {
        internal TreeLightMaterial At(float extension)
        {
            var length = MathF.Max(0f, extension - StartExtension);
            var delta = Target - Response;
            var distance = delta.Length();
            var moveLength = Rate > 0f && distance > 0f
                ? MathF.Min(length, MathF.Min(MathF.Max(0f, CommitmentEnd - StartExtension),
                    MathF.Min(distance / Rate, MathF.Max(0f, Budget - Path) / Rate))) : 0f;
            var direction = distance > 0f ? delta / distance : Vector3.Zero;
            var turn = Rate * moveLength;
            var response = turn > 0f ? Response + direction * turn : Response;
            var integral = Integral + Response * length;
            if (turn > 0f)
                integral += direction * (Rate * (moveLength * length - .5f * moveLength * moveLength));
            return new(response, SegmentStartResponse, integral, MathF.Min(Budget, Path + turn), Cycle);
        }
    }

    internal readonly TreeLightStep CaptureTreeLightStep(SpeciesSettings s, float startExtension)
        => new(startExtension, TreeLightResponse, TreeLightIntegral, TreeLightStartResponse, TreeLightPath,
            TreeLightCommitment ? TreeLightTarget : TreeLightResponse,
            TreeLightCommitment ? TreeLightCommitmentEnd : startExtension,
            TreeLightCommitment ? s.TreeUnitLightDeviationRadians / MathF.Max(1e-6f, TreeShootAllowance) : 0f,
            s.TreeUnitLightDeviationRadians, TreeLightCycle);

    internal readonly TreeLightStep CaptureTreeLightProposal(in AboveGroundAgent before, SpeciesSettings s)
        => new(before.TreeShootExtension, before.TreeLightResponse, before.TreeLightIntegral,
            before.TreeLightStartResponse, before.TreeLightPath,
            TreeLightCommitment ? TreeLightTarget : before.TreeLightResponse,
            TreeLightCommitment ? TreeLightCommitmentEnd : before.TreeShootExtension,
            TreeLightCommitment ? s.TreeUnitLightDeviationRadians / MathF.Max(1e-6f, TreeShootAllowance) : 0f,
            s.TreeUnitLightDeviationRadians, before.TreeLightCycle);

    TreeLightStep AdvanceTreeLightResponse(SpeciesSettings s, float beforeExtension, float extension, Vector3? cue,
        TreeModuleStep module = default)
    {
        if (extension <= beforeExtension) return CaptureTreeLightStep(s, beforeExtension);
        // A completely rejected spring increment restores last year's material
        // with its certified frames. The new annual physiology/commitment remains;
        // initialise its material only once some extension is proposed again.
        if (TreeLightCycle != TreeShootCycle)
        {
            ResetTreeLightMaterialForUnit();
            if (!module.IsDorsiventral) InitializeManagedTreePrimaryReference(s);
        }
        if (TreeLightCommitment && beforeExtension >= TreeLightCommitmentEnd)
            TreeLightCommitment = false;
        if (!TreeLightCommitment && cue is Vector3 measured && measured.LengthSquared() > 1e-12f
            && (TreeOrder != 1 || module.IsDorsiventral) && s.TreePhototropicWeight > 0f && s.TreeUnitLightDeviationRadians > 0f)
        {
            var intrinsic = TreeUnitIntrinsicTarget(s, beforeExtension, atTip: true, module: module);
            // Keep the complete cue magnitude and all three components. A tiny
            // horizontal component of an overhead sky must remain a tiny cue.
            var strength = Math.Clamp(measured.Length(), 0f, 1f);
            var desired = intrinsic * (1f - s.TreePhototropicWeight * strength)
                + measured * s.TreePhototropicWeight;
            var target = Vector3.Zero;
            if (desired.LengthSquared() > 1e-12f)
            {
                desired = Vector3.Normalize(desired);
                var cross = Vector3.Cross(intrinsic, desired);
                var length = cross.Length();
                var angle = MathF.Atan2(length, Math.Clamp(Vector3.Dot(intrinsic, desired), -1f, 1f));
                if (length > 1e-8f)
                    target = Vector3.Transform(cross / length, Quaternion.Conjugate(TreeUnitReferenceFrame()))
                        * MathF.Min(angle, s.TreeUnitLightDeviationRadians);
            }
            TreeLightTarget = target;
            TreeLightCommitmentEnd = extension;
            TreeLightCommitment = true;
        }
        var step = CaptureTreeLightStep(s, beforeExtension);
        RestoreTreeLightMaterial(step.At(extension));
        return step;
    }

    internal void CompleteTreeLightCommitment()
    {
        if (TreeLightCommitment && TreeShootExtension >= TreeLightCommitmentEnd)
            TreeLightCommitment = false;
    }

    void BeginTreeUnitLight()
    {
        // Opening/annual bookkeeping can precede hydration or any accepted
        // extension. Leave formed material intact until the positive-step guard.
        TreeLightTarget = Vector3.Zero;
        TreeLightCommitmentEnd = 0f;
        TreeLightCommitment = false;
    }

    void ResetTreeLightMaterialForUnit()
    {
        TreeLightStartResponse = TreeLightResponse;
        TreeLightIntegral = Vector3.Zero;
        TreeLightPath = 0f;
        TreeLightCycle = TreeShootCycle;
    }

    internal void InheritTreeUnitLight(in AboveGroundAgent parent, bool newUnit = false)
    {
        TreeLightResponse = TreeLightStartResponse = parent.TreeLightResponse;
        TreeLightIntegral = Vector3.Zero;
        TreeLightPath = newUnit ? 0f : parent.TreeLightPath;
        TreeLightCycle = parent.TreeLightCycle;
        TreeLightTarget = newUnit ? Vector3.Zero : parent.TreeLightTarget;
        TreeLightCommitmentEnd = newUnit ? 0f : parent.TreeLightCommitmentEnd;
        TreeLightCommitment = !newUnit && parent.TreeLightCommitment;
    }

    readonly Vector3 ApplyTreeLight(Vector3 intrinsic, float extension, bool atTip, in TreeLightMaterial material)
    {
        var width = extension - TreeUnitStartProgress;
        var response = atTip ? material.Response
            : width > 0f ? material.Integral / width : material.StartResponse;
        var angle = response.Length();
        if (angle <= 0f) return intrinsic; // exact53 arithmetic on the no-cue path
        var reference = TreeUnitReferenceFrame();
        var rotation = Quaternion.Normalize(reference * Quaternion.CreateFromAxisAngle(response / angle, angle)
            * Quaternion.Conjugate(reference));
        return Vector3.Transform(intrinsic, rotation);
    }
}
