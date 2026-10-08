using System.Numerics;

namespace Agro;

public partial struct AboveGroundAgent
{
    // Escrow crosses Census (including reindexing), but is settled before pending
    // births commit. No per-contact allocation and no geometry scans are required.
    internal float ConstructionStartVolume, ConstructionProposedVolume, ConstructionEscrow;

    internal void FundExpansion(in AboveGroundAgent before, SpeciesSettings species)
    {
        if (species.ConstructionEnergyPerDryGram == 0f) return;
        var size = new Vector2(Length, Radius);
        var cost = ConstructionBudget.Fund(new(before.Length, before.Radius), ref size,
            Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel,
            MathF.Max(0f, Energy - EnoughEnergy(species)), species, WoodRatio());
        Length = size.X; Radius = size.Y;
        ConstructionStartVolume = before.Volume();
        ConstructionProposedVolume = Volume();
        ConstructionEscrow = cost;
        Energy -= cost;
    }

    internal void SettleConstruction()
    {
        if (ConstructionEscrow > 0f)
        {
            var proposed = ConstructionProposedVolume - ConstructionStartVolume;
            var accepted = MathF.Max(0f, Volume() - ConstructionStartVolume);
            var fraction = proposed > 0f ? Math.Clamp(accepted / proposed, 0f, 1f) : 0f;
            Energy += ConstructionEscrow * (1f - fraction);
        }
        ConstructionStartVolume = ConstructionProposedVolume = ConstructionEscrow = 0f;
    }

    // Only the basal emergence axis gets this target; later compressed nodes
    // retain their sampled NodeDistance. Normal growth and contact checks apply.
    internal void SetEmergenceTarget(float distance) => LengthVar = MathF.Max(LengthVar, distance);

    internal static AboveGroundAgent RetainPhysiology(AboveGroundAgent structure, in AboveGroundAgent physiology)
    {
        structure.Energy = physiology.Energy;
        structure.Water_g = physiology.Water_g;
        structure.CurrentDayProductionInv = physiology.CurrentDayProductionInv;
        structure.CurrentDayEnvResources = physiology.CurrentDayEnvResources;
        structure.CurrentDayEnvResourcesInv = physiology.CurrentDayEnvResourcesInv;
        structure.PreviousDayProductionInvariant = physiology.PreviousDayProductionInvariant;
        structure.PreviousDayEnvResources = physiology.PreviousDayEnvResources;
        structure.PreviousDayEnvResourcesInvariant = physiology.PreviousDayEnvResourcesInvariant;
        structure.GrowthBlocked = physiology.GrowthBlocked;
        structure.ObstructionHours = physiology.ObstructionHours;
        structure.GroundSupported = physiology.GroundSupported;
        structure.SoilAnchored = physiology.SoilAnchored;
        structure.LoadLimited = physiology.LoadLimited;
        structure.Stress = physiology.Stress;
        structure.SenescenceStart = physiology.SenescenceStart;
        structure.SenescenceDuration = physiology.SenescenceDuration;
        return structure;
    }

    internal void TransportDevelopment(Quaternion carry)
    {
        SetOrientation(Quaternion.Normalize(carry * Orientation));
        baseOrientation = Quaternion.Normalize(carry * baseOrientation);
        restOrientation = Quaternion.Normalize(carry * restOrientation);
        targetOrientation = Quaternion.Normalize(carry * targetOrientation);
    }

    internal void RebaseBirthTime(uint delay)
    {
        BirthTime += delay;
        if (FlowerAgent.BirthTime != 0 || FlowerAgent.FlowerStartTime != 0)
        {
            var flower = FlowerAgent;
            flower.BirthTime += delay;
            if (flower.FlowerStartTime != 0) flower.FlowerStartTime += delay;
            FlowerAgent = flower;
        }
    }

    internal void GrowSuspendedRadius(PlantFormation2 plant)
    {
        // Rhizomes already perform safe radial growth in Herbaceous.Tick while suspended.
        var species = plant.Parameters;
        if (IsRizome || Energy <= EnoughEnergy(species) || Organ is OrganTypes.Leaf or OrganTypes.FlowerPadel or OrganTypes.Bud) return;
        var limit = IsRizome ? species.RhizomeSegmentRadius
            : Organ == OrganTypes.Petiole ? species.PetioleRadius + RadiusVar
            : Organ is >= OrganTypes.FlowerBud and <= OrganTypes.FlowerBaseBud ? RadiusVar : species.ShootMaximumRadius;
        if (Parent >= 0 && !plant.AG.GetIsRizome(Parent)) limit = MathF.Min(limit, plant.AG.GetBaseRadius(Parent));
        var reserve = Math.Clamp(Energy / MathF.Max(1e-12f, EnergyStorageCapacity(species)), 0f, 1f);
        var rate = Organ == OrganTypes.Petiole || Organ is >= OrganTypes.FlowerBud and <= OrganTypes.FlowerBaseBud
            ? MathF.Max(0, limit * GrowthTimeVar) : 2e-5f * plant.World.HoursPerTick * species.HerbaceousGrowthRateScale;
        Radius = MathF.Max(Radius, MathF.Min(limit, Radius + rate * reserve * Math.Clamp(plant.WaterBalance, 0, 1)));
    }
}
