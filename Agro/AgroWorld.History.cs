using Utils;

namespace Agro;

public partial class AgroWorld
{
    /// <summary>Internal step at which the requested recording begins.</summary>
    public uint RecordedStartStep { get; private set; }
    /// <summary>First simulated internal step; earlier indices only align resource-day blocks.</summary>
    public uint PreparationStartStep { get; private set; }
    public uint PreparationSteps => RecordedStartStep - PreparationStartStep;
    public uint PreparedSteps => Timestep <= PreparationStartStep ? 0 : Math.Min(Timestep - PreparationStartStep, PreparationSteps);
    bool HistoryCompleted;
    public bool IsPreparing => Timestep < RecordedStartStep;
    public uint RecordedStep => Timestep >= RecordedStartStep ? Timestep - RecordedStartStep : 0;

    void InitializeHistory(SimulationRequest? request)
    {
        if (TotalHours < 0) throw new ArgumentOutOfRangeException(nameof(TotalHours));
        double earliest = 0;
        foreach (var plant in request?.Plants ?? [])
        {
            var step = EffectiveEstablishmentStep(plant.StartOffsetHours);
            earliest = Math.Min(earliest, step);
            // Validate even future events beyond this run; an extension may reach them.
            _ = TimeZoneInfo.ConvertTimeToUtc(InitialTime, TimeZone).AddHours(step * HoursPerTick);
        }
        // Keep daily physiological blocks aligned with recording, but do not simulate the
        // unused indices before the first establishment: even an empty scene transports
        // soil water and receives rain. The internal clock may therefore begin above zero.
        var preparationSteps = Math.Ceiling(-earliest / StatsBlockLength) * StatsBlockLength;
        if (preparationSteps + TimestepsTotal() > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PlantRequest.StartOffsetHours), "Preparation and recording exceed the supported timestep range.");
        RecordedStartStep = checked((uint)preparationSteps);
        PreparationStartStep = checked((uint)(preparationSteps + earliest));
        HistoryCompleted = RecordedStartStep == 0;
        _ = GetTime(0);
        _ = GetTime(RecordedStartStep + (uint)TimestepsTotal());
        // InitialTime is the recording origin; set season from the actual internal origin.
        if (RecordedStartStep > 0) Timestep = PreparationStartStep;
    }

    internal long EffectiveEstablishmentStep(double hours)
    {
        var step = Math.Ceiling(hours / HoursPerTick);
        if (!double.IsFinite(hours) || step < -int.MaxValue || step > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PlantRequest.StartOffsetHours), "Establishment time must be finite and within the supported timestep range.");
        return (long)step;
    }

    internal uint ActivationStep(double hours)
    {
        var step = (long)RecordedStartStep + EffectiveEstablishmentStep(hours);
        if (step < 0 || step > uint.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(PlantRequest.StartOffsetHours));
        return (uint)step;
    }

    static Pcg HistoryWeatherRng(ulong state, ulong increment, DateTime month)
    {
        // Fixed integer mixing, never HashCode (which is process-randomized).
        var key = unchecked(state ^ increment ^ ((ulong)(month.Year * 12 + month.Month) * 0x9E3779B97F4A7C15UL));
        key = unchecked((key ^ (key >> 30)) * 0xBF58476D1CE4E5B9UL);
        key = unchecked((key ^ (key >> 27)) * 0x94D049BB133111EBUL);
        return new Pcg(key ^ (key >> 31));
    }

    /// <summary>Executes one historical step, for cancellable preparation and progress reporting.
    /// Returns false when the recording boundary has already been reached.</summary>
    public bool RunPreparationStep()
    {
        if (!IsPreparing) { CompleteHistory(); return false; }
        base.Run(1);
        if (!IsPreparing) CompleteHistory();
        return true;
    }

    public void PrepareHistory(Action<uint, uint>? progress = null, CancellationToken cancellationToken = default)
    {
        while (IsPreparing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunPreparationStep();
            progress?.Invoke(PreparedSteps, PreparationSteps);
        }
        CompleteHistory();
    }

    void CompleteHistory()
    {
        if (HistoryCompleted || IsPreparing) return;
        Vegetation.BeginRecording(this);
        HistoryCompleted = true;
    }

    /// <summary>Runs recorded steps, preparing shared history first when needed. World construction
    /// itself never simulates, so restoring a checkpoint never repeats preparation.</summary>
    public override void Run(uint simulationLength)
    {
        PrepareHistory();
        if (simulationLength > (uint)TimestepsTotal() - RecordedStep)
            throw new ArgumentOutOfRangeException(nameof(simulationLength));
        base.Run(simulationLength);
    }

    public new void RunSequential(uint simulationLength)
    {
        while (IsPreparing) base.RunSequential(1);
        CompleteHistory();
        base.RunSequential(simulationLength);
    }

    public new void RunParallel(uint simulationLength)
    {
        while (IsPreparing) base.RunParallel(1);
        CompleteHistory();
        base.RunParallel(simulationLength);
    }
}
