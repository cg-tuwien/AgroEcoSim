using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    // Identity only; sparse per-root metadata lives on the above-ground formation.
    // Zero is the exact disabled/default path. Serial continuations copy this value.
    internal ulong TreeModuleRootStableId;
    internal readonly float TreeModuleAllowance => TreeShootAllowance;
    internal readonly int TreeModuleCycle => TreeShootCycle;
    internal readonly float TreeModuleExtension => TreeShootExtension;
    internal readonly record struct TreeModuleMaterialFrame(Quaternion Orientation, Quaternion Base,
        Quaternion Rest, Quaternion Target, Vector3 Offset)
    {
        internal bool SameBits(in TreeModuleMaterialFrame other)
        {
            static bool F(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
            static bool Q(Quaternion a, Quaternion b) => F(a.X,b.X)&&F(a.Y,b.Y)&&F(a.Z,b.Z)&&F(a.W,b.W);
            return Q(Orientation,other.Orientation)&&Q(Base,other.Base)&&Q(Rest,other.Rest)&&Q(Target,other.Target)
                &&F(Offset.X,other.Offset.X)&&F(Offset.Y,other.Offset.Y)&&F(Offset.Z,other.Offset.Z);
        }
    }
    internal readonly record struct TreeModuleLocalMaterial(Quaternion ReferenceLocal, TreeLightMaterial Light);

    internal readonly TreeModuleMaterialFrame CaptureTreeModuleMaterial()
        => new(Orientation, baseOrientation, restOrientation, targetOrientation, BaseOffset);

    internal readonly TreeModuleLocalMaterial CaptureTreeModuleLocalMaterial()
        => new(TreeUnitReferenceLocal, CaptureTreeLightMaterial());

    internal void RestoreTreeModuleMaterial(in TreeModuleMaterialFrame frame,
        in TreeModuleLocalMaterial material, bool absoluteFrames)
    {
        if (absoluteFrames)
        {
            // Safe only after literal equality with the retained post-own-growth
            // tuple proves no independently observable frame/attachment change.
            Orientation = frame.Orientation; baseOrientation = frame.Base;
            restOrientation = frame.Rest; targetOrientation = frame.Target;
            BaseOffset = frame.Offset;
        }
        TreeUnitReferenceLocal = material.ReferenceLocal;
        RestoreTreeLightMaterial(material.Light);
    }
    internal readonly float TreeModuleSiteRoll(SpeciesSettings species, bool dorsiventral)
        => TreeRankRoll(species, 0, 0, dorsiventral);

    internal readonly Quaternion TreeModuleProgramFrame(SpeciesSettings species, float extension,
        in TreeLightMaterial material, in TreeModuleStep module, bool atTip = false)
    {
        if (!module.HasProgram)
            throw new InvalidOperationException("A living dorsiventral module requires a frozen finite opening allowance.");
        var reference = TreeUnitReferenceFrame();
        var local = TreeModuleProgram.LocalFrame(module.OpeningStartLocal, module.OpeningLengthM,
            module.AllowanceM, species.TreeLateralUnitArchRadians, TreeUnitStartProgress, extension, atTip);
        var frame = Quaternion.Normalize(reference * local);
        var width = extension - TreeUnitStartProgress;
        var response = atTip ? material.Response
            : width > 0f ? material.Integral / width : material.StartResponse;
        var angle = response.Length();
        if (angle <= 0f) return frame;
        var turn = Quaternion.Normalize(reference * Quaternion.CreateFromAxisAngle(response / angle, angle)
            * Quaternion.Conjugate(reference));
        return Quaternion.Normalize(turn * frame);
    }

    void TurnTreeModuleMaterial(SpeciesSettings species, float beforeExtension, float extension,
        in TreeLightStep light, in TreeModuleStep module)
    {
        if (extension == beforeExtension) return;
        var reference = TreeUnitReferenceFrame();
        var oldFrame = TreeModuleProgramFrame(species, beforeExtension, light.At(beforeExtension), module);
        var newFrame = TreeModuleProgramFrame(species, extension, light.At(extension), module);
        var carry = oldFrame == newFrame ? Quaternion.Identity
            : Quaternion.Normalize(newFrame * Quaternion.Conjugate(oldFrame));
        if (carry != Quaternion.Identity) TransportTreeUnitFrame(carry, reference);
    }

    internal void RestoreZeroTreeModuleMaterial(in AboveGroundAgent before)
    {
        // Funding may accept radius independently; restore only the old material
        // frames/reference/response, without normalising an accepted fallback.
        RestoreTreeModuleMaterial(before.CaptureTreeModuleMaterial(), before.CaptureTreeModuleLocalMaterial(), absoluteFrames: true);
    }

    internal AboveGroundAgent SampleTreeModuleBud(PlantFormation2 plant, int ownerIndex,
        Quaternion budFrame, Quaternion ownReference, Vector3 siteOffset, int cycle)
        => new(plant, ownerIndex, OrganTypes.Bud, budFrame, Energy * .12f,
            radius: MathF.Min(Radius, .001f), length: .003f,
            initialResources: MathF.Max(PreviousDayEnvResourcesInvariant, plant.AG.DailyResourceMax * .25f),
            initialProduction: MathF.Max(PreviousDayProductionInvariant, plant.AG.DailyProductionMax * .25f))
        {
            Water_g = Water_g * .12f, TreeOrder = TreeOrder, TreeRole = TreeBudRole.Relay,
            TreeCrown = TreeCrown, TreeShootCycle = cycle, TreeLeafCycle = cycle,
            TreeAxisSetPointAngle = TreeInclination(ownReference),
            TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(budFrame) * ownReference),
            BaseOffset = siteOffset, LateralAngle = 0f, WoodFactor = .5f
        };

    internal void CommitTreeModuleClosure()
    {
        Organ = OrganTypes.Stem;
        WoodFactor = MathF.Max(WoodFactor, .15f);
        Energy *= .88f; Water_g *= .88f;
    }

    internal void PrepareTreeModuleRetry(in AboveGroundAgent owner, int parent, uint attemptTick, Quaternion carry)
    {
        // Copying a canonical sample preserves its original phenotype and bud
        // cycles. Only physical introduction gets a new age; zero timestamp
        // sentinels and unrelated floral/physiological clocks are not rebased.
        StableId = TreeModuleRootStableId = 0;
        Parent = parent; BirthTime = attemptTick;
        if (carry != Quaternion.Identity) TransportDevelopment(carry, carryAttachment: true);
        Energy = owner.Energy * .12f; Water_g = owner.Water_g * .12f;
    }

    internal void TransferTreeModuleRetryShare()
    {
        // The donor is already accepted perennial wood: no organ/wood/pose reset.
        Energy *= .88f; Water_g *= .88f;
    }
}
