using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    internal readonly bool CorrectsOrdinaryLateralUnitTrial(SpeciesSettings species)
        => species.Behavior == Behavior.Woody && species.TreeGrowthUnitsEnabled
            && IsTreeUnitAxis && TreeOrder >= 2 && TreeModuleRootStableId == 0;

    internal readonly Quaternion LateralTreeUnitTrialTurnWithLight(SpeciesSettings species,
        float oldExtension, float newExtension, in TreeLightStep step, Quaternion sampledReference,
        Quaternion openingLocal, bool atTip = false)
    {
        if (oldExtension == newExtension) return Quaternion.Identity;
        // This programme belongs to the proposal's material frame, sampled after
        // initial reference setup and before its intrinsic turn. A later support
        // motion must carry the correction, not recompute the old arch against gravity.
        var currentReference = TreeUnitReferenceFrame();
        var evaluator = this;
        evaluator.RestoreTreeUnitFrame(sampledReference);
        var opening = openingLocal == Quaternion.Identity ? Quaternion.Identity
            : Quaternion.Normalize(sampledReference * openingLocal * Quaternion.Conjugate(sampledReference));
        var turn = atTip
            ? evaluator.TreeUnitTrialTurnWithLight(species, oldExtension, newExtension, step, atTip: true)
            : evaluator.TransportTreeUnitTrialOpeningWithLight(species, oldExtension, newExtension, step, opening);
        if (turn == Quaternion.Identity) return turn;
        var carrier = Quaternion.Normalize(currentReference * Quaternion.Conjugate(sampledReference));
        return Quaternion.Normalize(carrier * turn * Quaternion.Conjugate(carrier));
    }

    internal Quaternion TransportLateralTreeUnitTrialWithLight(SpeciesSettings species,
        float oldExtension, float newExtension, in TreeLightStep step, Quaternion sampledReference,
        Quaternion openingLocal)
    {
        var reference = TreeUnitReferenceFrame();
        var turn = LateralTreeUnitTrialTurnWithLight(species, oldExtension, newExtension, step,
            sampledReference, openingLocal);
        if (turn != Quaternion.Identity) TransportTreeUnitFrame(turn, reference);
        RestoreTreeLightMaterial(step.At(newExtension));
        return turn;
    }
}
