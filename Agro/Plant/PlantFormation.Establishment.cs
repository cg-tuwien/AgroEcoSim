namespace Agro;

public partial class PlantFormation2
{
    uint EstablishmentStep;
    bool HistoricalEstablishment;
    public bool IsActive { get; private set; } = true;

    internal void ConfigureEstablishment(double offsetHours)
    {
        EstablishmentStep = World.ActivationStep(offsetHours);
        HistoricalEstablishment = offsetHours < 0;
        // Preserve the exact ordinary-seed path when no scheduling was requested.
        IsActive = EstablishmentStep == 0 && !HistoricalEstablishment;
    }

    bool ActivateForTick(uint timestep)
    {
        if (IsActive) return true;
        if (timestep < EstablishmentStep) return false;
        IsActive = true;
        if (!HistoricalEstablishment) return true;
        var seed = SrcDst_Seed().Item1[0];
        seed.Establish(this, timestep);
        return false; // Germination itself is this interval's development.
    }

    void WriteEstablishmentState(BinaryWriter writer)
    {
        writer.Write(EstablishmentStep);
        writer.Write(HistoricalEstablishment);
        writer.Write(IsActive);
    }

    void ReadEstablishmentState(BinaryReader reader)
    {
        if (reader.ReadUInt32() != EstablishmentStep || reader.ReadBoolean() != HistoricalEstablishment)
            throw new InvalidDataException("The checkpoint plant establishment schedule differs from the request.");
        IsActive = reader.ReadBoolean();
    }
}
