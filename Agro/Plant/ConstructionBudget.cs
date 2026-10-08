using System.Numerics;

namespace Agro;

/// <summary>
/// Construction accounting in grams of glucose equivalent. The configurable
/// cost converts grams of new dry tissue to grams of glucose required.
/// Uses physiological volume, never the folded or collision envelope.
/// </summary>
internal static class ConstructionBudget
{
    internal static float Density(SpeciesSettings species, float woodRatio)
        => species.DensityDryStem + Math.Clamp(woodRatio, 0f, 1f) * (species.DensityDryWood - species.DensityDryStem);

    internal static double Volume(Vector2 size, bool blade)
        => blade ? (double)size.X * size.Y * (2d * AboveGroundAgent.LeafThickness)
            : 4d * size.X * size.Y * size.Y;

    internal static float Cost(double volume, SpeciesSettings species, float woodRatio)
        => (float)(Math.Max(0d, volume) * Density(species, woodRatio) * species.ConstructionEnergyPerDryGram);

    /// <summary>Limits an expansion to the available reserve and returns its debit.</summary>
    internal static float Fund(Vector2 before, ref Vector2 proposed, bool blade, float available,
        SpeciesSettings species, float woodRatio)
    {
        if (species.ConstructionEnergyPerDryGram == 0f) return 0f;
        var initial = Volume(before, blade);
        var coefficient = (double)Density(species, woodRatio) * species.ConstructionEnergyPerDryGram;
        var required = Math.Max(0d, Volume(proposed, blade) - initial) * coefficient;
        if (required == 0d) return 0f;
        available = MathF.Max(0f, available);
        if (required > available)
        {
            // Volume is monotonic for a pure expansion. Retargeting that contracts
            // either dimension is kept at its cheaper contracted baseline first.
            var baseline = Vector2.Min(before, proposed);
            var target = proposed;
            var low = 0f; var high = 1f;
            for (int i = 0; i < 24; ++i)
            {
                var mid = (low + high) * .5f;
                var trial = Vector2.Lerp(baseline, target, mid);
                if (Math.Max(0d, Volume(trial, blade) - initial) * coefficient <= available) low = mid;
                else high = mid;
            }
            proposed = Vector2.Lerp(baseline, target, low);
            required = Math.Max(0d, Volume(proposed, blade) - initial) * coefficient;
        }
        return MathF.Min(available, (float)required);
    }
}
