using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    // A finite unit shares one sampled/frozen budget across all of its internodes.
    // The reference is local to baseOrientation: external support/contact transport
    // carries it automatically. An intrinsic turn inversely updates the local value
    // so that the programme does not chase its own developing curvature.
    float TreeShootAllowance, TreeUnitStartProgress;
    Quaternion TreeUnitReferenceLocal;
    int TreeFlushDecisionCycle;
    bool TreeFlushDecisionApproved;

    // Geometry rollback must restore this alongside its associated base frame.
    // Preserve the raw zero/uninitialised value and accepted quaternion exactly;
    // normalising a fallback frame can invalidate a certified contact boundary.
    internal readonly Quaternion CaptureTreeUnitReference() => TreeUnitReferenceLocal;
    internal void RestoreTreeUnitReference(Quaternion reference) => TreeUnitReferenceLocal = reference;
    internal readonly Quaternion CaptureTreeUnitFrame() => TreeUnitReferenceFrame();
    internal void RestoreTreeUnitFrame(Quaternion frame)
        => TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(baseOrientation) * frame);
    internal readonly bool IsTreeUnitAxis => TreeIsTerminal && Organ is OrganTypes.Meristem or OrganTypes.Stem;
    internal readonly bool NeedsTreeRelayOpening => TreeRole == TreeBudRole.Relay && TreeShootExtension <= 0f;
    internal readonly bool IsTreeRelayOf(in AboveGroundAgent parent)
        => TreeRole == TreeBudRole.Relay && Organ == OrganTypes.Bud && TreeOrder == parent.TreeOrder
            && TreeShootCycle == parent.TreeShootCycle;
    internal readonly float TreeTrialExtension(float proposedLength, float trialLength)
        => MathF.Max(TreeUnitStartProgress, TreeShootExtension - (proposedLength - trialLength));
    internal readonly float TreeTrialExtension(float startLength, float proposedLength,
        float trialLength, in TreeLightStep step)
        // Explicit endpoints prevent cancellation from inventing tiny accepted
        // growth/light expenditure when the trial accepts exactly zero length.
        => trialLength <= startLength ? step.StartExtension
            : trialLength >= proposedLength ? TreeShootExtension
            : Math.Clamp(step.StartExtension + (trialLength - startLength),
                step.StartExtension, TreeShootExtension);
    internal readonly Quaternion TreeRelayOpeningTurn(SpeciesSettings s, Vector3? light, TreeModuleStep module = default)
    {
        if (module.IsDorsiventral) return Quaternion.Identity;
        var target = TreeFrameToward(baseOrientation, TreeUnitTargetAtExtension(s, 0f, light));
        return target == baseOrientation ? Quaternion.Identity
            : Quaternion.Normalize(target * Quaternion.Conjugate(baseOrientation));
    }

    readonly float TreeAnnualAllowance(PlantFormation2 plant)
    {
        var s = plant.Parameters;
        var ageYears = plant.GetAge_h() / (24f * 365f);
        var maturity = Math.Clamp(ageYears / s.TreeMatureAgeYears, 0f, 1f);
        var release = TreeCrown ? s.TreeApicalControlRelease(ageYears) : 0f;
        return (TreeCrown ? s.TreeAnnualExtension : MathF.Max(.8f, s.TreeAnnualExtension))
            * (1f - .8f * maturity) * s.TreeExtensionByOrder(TreeOrder, release);
    }

    readonly Quaternion TreeUnitReferenceFrame()
        => TreeUnitReferenceLocal.LengthSquared() > .5f
            ? Quaternion.Normalize(baseOrientation * TreeUnitReferenceLocal) : baseOrientation;

    void BeginTreeGrowthUnit(PlantFormation2 plant)
    {
        TreeShootAllowance = TreeAnnualAllowance(plant);
        TreeShootExtension = TreeUnitStartProgress = 0f;
        BeginTreeUnitLight();
        if (TreeUnitReferenceLocal.LengthSquared() < .5f)
            TreeUnitReferenceLocal = Quaternion.Identity;
        plant.AG.FreezeTreeModule(this, TreeShootAllowance, TreeShootCycle);
    }

    void InitializeManagedTreePrimaryReference(SpeciesSettings species)
    {
        // This is the explicitly managed single-trunk form, not a model of basal
        // erection in an untreated Troll-model seedling. Run it with first positive
        // material development: exact geometry rollback restores the old material
        // cycle, so a rejected initial proposal cannot lose this training on retry.
        if (TreeOrder == 1 && species.TreeStemCount == 1
            && !(species.TreeGraftHeight > 0f && TreeCrown))
        {
            var upright = TreeFrameToward(TreeUnitReferenceFrame(), Vector3.UnitY);
            TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(baseOrientation) * upright);
        }
    }

    internal static float TreeMeanSmoothstep(float start, float end)
    {
        start = Math.Clamp(start, 0f, 1f);
        end = Math.Clamp(end, 0f, 1f);
        if (end - start < 1e-5f)
        {
            var x = (start + end) * .5f;
            return x * x * (3f - 2f * x);
        }
        static double Integral(double x) => x * x * x * (1d - .5d * x);
        return (float)((Integral(end) - Integral(start)) / (end - start));
    }

    internal static Quaternion TreeFrameToward(Quaternion frame, Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        var axis = Vector3.Transform(Vector3.UnitX, frame);
        var dot = Math.Clamp(Vector3.Dot(axis, direction), -1f, 1f);
        var cross = Vector3.Cross(axis, direction);
        if (cross.LengthSquared() < 1e-16f)
            return dot >= 0f ? frame : Quaternion.Normalize(
                Quaternion.CreateFromAxisAngle(Vector3.Transform(Vector3.UnitZ, frame), MathF.PI) * frame);
        // atan2 retains small growth/trial angles whose float dot product has
        // already rounded to1; acos(dot) would silently discard those increments.
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.Normalize(cross), MathF.Atan2(cross.Length(), dot)) * frame);
    }

    internal readonly Vector3 WoodyUnitProgrammeTarget(SpeciesSettings s, Vector3? light = null,
        bool atTip = false, TreeModuleStep module = default)
        => TreeUnitTargetAtExtension(s, TreeShootExtension, light, atTip, module);

    internal readonly Vector3 TreeUnitTargetAtExtension(SpeciesSettings s, float extension,
        Vector3? light = null, bool atTip = false, TreeModuleStep module = default)
        => module.IsDorsiventral
            ? Vector3.Transform(Vector3.UnitX, TreeModuleProgramFrame(s, extension, CaptureTreeLightMaterial(), module, atTip))
            : ApplyTreeLight(TreeUnitIntrinsicTarget(s, extension, atTip), extension, atTip, CaptureTreeLightMaterial());

    internal readonly Vector3 TreeUnitIntrinsicTarget(SpeciesSettings s, float extension, bool atTip = false,
        TreeModuleStep module = default)
    {
        if (module.IsDorsiventral)
            return Vector3.Transform(Vector3.UnitX, TreeModuleProgramFrame(s, extension, default, module, atTip));
        var reference = TreeUnitReferenceFrame();
        var referenceAxis = Vector3.Transform(Vector3.UnitX, reference);
        var start = atTip ? extension : TreeUnitStartProgress;
        var phase = TreeMeanSmoothstep(start / MathF.Max(1e-6f, TreeShootAllowance),
            extension / MathF.Max(1e-6f, TreeShootAllowance));
        var inclination = Math.Clamp(TreeInclination(reference)
            + (TreeOrder == 1 ? 0f : s.TreeLateralUnitArchRadians * phase), 0f, MathF.PI);
        var horizontal = new Vector3(referenceAxis.X, 0f, referenceAxis.Z);
        if (horizontal.LengthSquared() < 1e-8f)
        {
            var across = Vector3.Transform(Vector3.UnitZ, reference);
            horizontal = new Vector3(across.X, 0f, across.Z);
        }
        horizontal = Vector3.Normalize(horizontal.LengthSquared() < 1e-8f ? Vector3.UnitX : horizontal);
        return horizontal * MathF.Sin(inclination) + Vector3.UnitY * MathF.Cos(inclination);
    }

    void TurnTreeUnitRestFrame(Vector3 direction)
    {
        var reference = TreeUnitReferenceFrame();
        var next = TreeFrameToward(baseOrientation, direction);
        if (next == baseOrientation) return;
        var carry = Quaternion.Normalize(next * Quaternion.Conjugate(baseOrientation));
        TransportDevelopment(carry);
        TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(baseOrientation) * reference);
    }

    internal Quaternion TransportTreeUnitTrialProgress(SpeciesSettings s, float oldExtension,
        float newExtension, Vector3? light)
        => TransportTreeUnitTrialOpening(s, oldExtension, newExtension, light, Quaternion.Identity);

    internal Quaternion TransportTreeUnitTrialOpening(SpeciesSettings s, float oldExtension,
        float newExtension, Vector3? light, Quaternion opening)
        => TransportTreeUnitTrialOpeningWithLight(s, oldExtension, newExtension,
            CaptureTreeLightStep(s, oldExtension), opening);

    internal Quaternion TransportTreeUnitTrialOpeningWithLight(SpeciesSettings s, float oldExtension,
        float newExtension, in TreeLightStep step, Quaternion opening, TreeModuleStep module = default)
    {
        var reference = TreeUnitReferenceFrame();
        var carry = TreeUnitTrialTurnWithLight(s, oldExtension, newExtension, step, module: module);
        if (opening != Quaternion.Identity)
        {
            var oldTurn = oldExtension > 0f
                ? Quaternion.Normalize(TreeUnitTrialTurnWithLight(s, 0f, oldExtension, step) * opening) : Quaternion.Identity;
            var newTurn = newExtension > 0f
                ? Quaternion.Normalize(TreeUnitTrialTurnWithLight(s, 0f, newExtension, step) * opening) : Quaternion.Identity;
            carry = Quaternion.Normalize(newTurn * Quaternion.Conjugate(oldTurn));
        }
        // Remove/add only the intrinsic phase increment. Any existing elastic or
        // contact-induced offset remains composed with the material frame.
        if (carry != Quaternion.Identity) TransportTreeUnitFrame(carry, reference);
        RestoreTreeLightMaterial(step.At(newExtension));
        return carry;
    }

    internal readonly Quaternion TreeUnitTrialTurnWithLight(SpeciesSettings s, float oldExtension,
        float newExtension, in TreeLightStep step, bool atTip = false, TreeModuleStep module = default)
    {
        var floor = atTip ? 0f : TreeUnitStartProgress;
        oldExtension = MathF.Max(floor, oldExtension);
        newExtension = MathF.Max(floor, newExtension);
        if (oldExtension == newExtension) return Quaternion.Identity;
        if (module.IsDorsiventral)
        {
            var oldFrame = TreeModuleProgramFrame(s, oldExtension, step.At(oldExtension), module, atTip);
            var newFrame = TreeModuleProgramFrame(s, newExtension, step.At(newExtension), module, atTip);
            return oldFrame == newFrame ? Quaternion.Identity
                : Quaternion.Normalize(newFrame * Quaternion.Conjugate(oldFrame));
        }
        var reference = TreeUnitReferenceFrame();
        var oldTarget = ApplyTreeLight(TreeUnitIntrinsicTarget(s, oldExtension, atTip), oldExtension, atTip, step.At(oldExtension));
        var newTarget = ApplyTreeLight(TreeUnitIntrinsicTarget(s, newExtension, atTip), newExtension, atTip, step.At(newExtension));
        var oldProgramme = TreeFrameToward(reference, oldTarget);
        var newProgramme = TreeFrameToward(oldProgramme, newTarget);
        return oldProgramme == newProgramme ? Quaternion.Identity
            : Quaternion.Normalize(newProgramme * Quaternion.Conjugate(oldProgramme));
    }

    internal readonly Quaternion TreeUnitTrialTurn(SpeciesSettings s, float oldExtension,
        float newExtension, Vector3? light, bool atTip = false)
    {
        var floor = atTip ? 0f : TreeUnitStartProgress;
        oldExtension = MathF.Max(floor, oldExtension);
        newExtension = MathF.Max(floor, newExtension);
        if (oldExtension == newExtension) return Quaternion.Identity;
        var reference = TreeUnitReferenceFrame();
        var oldProgramme = TreeFrameToward(reference, TreeUnitTargetAtExtension(s, oldExtension, light, atTip));
        var newProgramme = TreeFrameToward(oldProgramme, TreeUnitTargetAtExtension(s, newExtension, light, atTip));
        return oldProgramme == newProgramme ? Quaternion.Identity
            : Quaternion.Normalize(newProgramme * Quaternion.Conjugate(oldProgramme));
    }

    internal void TransportTreeUnitFrame(Quaternion carry, Quaternion reference)
    {
        TransportDevelopment(carry);
        RestoreTreeUnitFrame(reference);
    }

    bool TreeFlushDecision(PlantSubFormation<AboveGroundAgent> formation, int cycle)
    {
        var plant = formation.Plant;
        var probability = plant.Parameters.TreeAxillaryFlushProbability;
        // Disabled/default controls consume no new random numbers.
        if (TreeIsTerminal || probability >= 1f) return true;
        if (TreeFlushDecisionCycle != cycle)
        {
            TreeFlushDecisionCycle = cycle;
            TreeFlushDecisionApproved = probability > 0f && plant.RNG.NextFloat() < probability;
            formation.MarkDevelopmentPhysiology(this);
        }
        return TreeFlushDecisionApproved;
    }

    readonly float TreeRankRoll(SpeciesSettings s, int node, int lateral, bool moduleDorsiventral = false)
        => LateralAngle + node * s.LateralRoll + lateral * MathF.Tau / s.LateralsPerNode
            + (s.TreeDorsiventralShoots && (TreeOrder > 1 || !TreeIsTerminal || moduleDorsiventral) ? MathF.PI * .5f : 0f);

    readonly Quaternion TreeAxillaryFrame(SpeciesSettings s, float roll, float pitch, bool moduleDorsiventral = false)
    {
        var direction = Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, roll)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -pitch);
        if (!s.TreeDorsiventralShoots || TreeOrder == 1 && TreeIsTerminal && !moduleDorsiventral)
            // An upright primary trunk has no dorsal side. Establish the two
            // daughters' dorsal-up frame once, rather than inheriting opposite
            // arbitrary signs from the trunk's transverse coordinate axes.
            return TurnUpwards(direction);
        return TreeFrameToward(Orientation, Vector3.Transform(Vector3.UnitX, direction));
    }

    void CloseTreeGrowthUnit(PlantSubFormation<AboveGroundAgent> formation, int id, int cycle)
    {
        // An arrested, never-extended apex is still the same dormant bud. Its
        // initial capsule length alone is not evidence of a completed growth unit.
        if (TreeShootExtension <= 0f || GrowthBlocked || formation.HasPendingDevelopment(id)) return;
        if (formation.TryCloseTreeModule(ref this, id, cycle)) return;
        var plant = formation.Plant;
        var s = plant.Parameters;
        var axis = Vector3.Transform(Vector3.UnitX, Orientation);
        var rank = TreeRankRoll(s, 0, 0);
        // With the negative lateral pitch, the leaf lies on minus localY.
        // Prefer a real current-owner axil; otherwise this small capsule carries
        // an explicitly virtual, unexpanded distal leaf primordium at that rank.
        var side = -Vector3.Transform(Vector3.UnitY,
            Orientation * Quaternion.CreateFromAxisAngle(Vector3.UnitX, rank));
        var axilOffset = Vector3.Zero;
        if (formation.TryGetDistalTreeAxil(id, Orientation, out var petiole))
        {
            var leafAxis = Vector3.Transform(Vector3.UnitX, petiole.Orientation);
            var transverse = leafAxis - axis * Vector3.Dot(leafAxis, axis);
            if (transverse.LengthSquared() > 1e-10f)
                side = Vector3.Normalize(transverse);
            axilOffset = petiole.BaseOffset;
        }
        // An actual distal axillary bud, appressed to the bearer, replaces the
        // spent apex next spring. Accepted bearer wood, leaves and fruit remain.
        var appressed = TreeFrameToward(Orientation, Vector3.Normalize(axis + side * .1f));
        var relay = new AboveGroundAgent(plant, id, OrganTypes.Bud, appressed, Energy * .12f,
            radius: MathF.Min(Radius, .001f), length: .003f,
            initialResources: MathF.Max(PreviousDayEnvResourcesInvariant, formation.DailyResourceMax * .25f),
            initialProduction: MathF.Max(PreviousDayProductionInvariant, formation.DailyProductionMax * .25f))
        {
            Water_g = Water_g * .12f,
            TreeOrder = TreeOrder,
            TreeRole = TreeBudRole.Relay,
            TreeCrown = TreeCrown,
            TreeShootCycle = cycle,
            TreeLeafCycle = cycle,
            TreeAxisSetPointAngle = TreeAxisSetPointAngle,
            TreeUnitReferenceLocal = Quaternion.Normalize(Quaternion.Conjugate(appressed) * TreeUnitReferenceFrame()),
            BaseOffset = axilOffset + side * (Radius + .0004f) - axis * .001f,
            LateralAngle = 0f,
            WoodFactor = .5f
        };
        Organ = OrganTypes.Stem;
        WoodFactor = MathF.Max(WoodFactor, .15f);
        relay.InheritTreeUnitLight(this, newUnit: true);
        formation.Birth(relay);
        Energy *= .88f;
        Water_g *= .88f;
    }
}
