using System.Runtime.InteropServices;

namespace Agro;

/// <summary>
/// The herbaceous drought response: a water-stress signal, growth restriction, and a target-canopy
/// controller that commits leaves to senescence gradually instead of deleting them. See
/// Agro/DROUGHT_RESPONSE.md for the model and the evidence behind it.
/// </summary>
public partial class PlantFormation2
{
	/// <summary>Fraction by which a fully water-stressed blade closes its stomata, cutting both
	/// assimilation and the water it uses. The default of <see cref="LeafPhysiology.WaterStressSensitivity"/>.</summary>
	internal const float StomatalStressSensitivity = 0.7f;

	/// <summary>
	/// Plant water fill (allocatable water over tissue storage) read as fully watered. Measured
	/// with the weather overridden to 5 mm of rain a day: all seven species held 0.94-0.99 at 1 h
	/// and 4 h ticks over 120 days. Longer ticks read drier (0.69-0.97 at 24 h) because a root
	/// refills at most its own storage per tick.
	/// </summary>
	internal const float WellWateredFill = 0.9f;

	/// <summary>Time constant of the water filter. Long enough that a night without uptake is
	/// not a drought, short enough that a week without rain is.</summary>
	const float WaterFilterHours = 48f;

	/// <summary>Reserve below which carbon is short: the seven days of maintenance the old
	/// emergency rule deleted tissue at, kept as a warning that stops new foliage instead.</summary>
	const float CarbonWarningHours = 168f;

	/// <summary>An episode ends after the stress has stayed this far below the onset for
	/// <see cref="EpisodeQuietHoursToEnd"/> hours - one rainy day does not end a drought.</summary>
	const float EpisodeHysteresis = 0.05f;
	const float EpisodeQuietHoursToEnd = 72f;

	/// <summary>Hours of unpaid maintenance before the plant starts reverting crowns to buds,
	/// one a day. Leaves are never deleted for energy under the drought response.</summary>
	const float StarvationToleranceHours = 336f;

	/// <summary>Hours after germination before the drought signal is read.</summary>
	const float EstablishmentHours = 336f;

	/// <summary>Whether this plant runs the drought response: herbaceous, with a positive
	/// <see cref="SpeciesSettings.CanopyAdjustmentHalfTimeDays"/>. Zero restores the old rules.</summary>
	internal bool DroughtResponse { get; private set; }

	/// <summary>Plant water stress: 0 well watered, 1 no water in the tissue. From the filtered fill.</summary>
	internal float DroughtStress;
	float WaterFillFiltered = 1f;
	/// <summary>Carbon stress: 0 with at least seven days of maintenance in the pool, 1 with none.</summary>
	internal float CarbonStress;
	bool DroughtEpisode;
	float EpisodeQuietHours;
	/// <summary>Green, uncommitted leaf area when the episode began, m² (2 L R per blade segment).</summary>
	float ReferenceCanopyArea;
	/// <summary>Leaf area the plant is adjusting towards during an episode, m².</summary>
	internal float CanopyTarget;
	/// <summary>Green leaf area not yet committed to senescence, m², as of the last census.</summary>
	internal float UncommittedLeafArea;
	float SenescenceCarry;
	float StarvationHours;
	uint LastCanopyUpdate;

	/// <summary>Multiplies every herbaceous growth rate: a dry plant grows less before it sheds anything.</summary>
	internal float DroughtGrowthFactor => DroughtResponse ? (1f - DroughtStress) * (1f - CarbonStress) : 1f;

	/// <summary>The water term every expansion site applies: the old one-tick sufficiency times the drought factor.</summary>
	internal float GrowthWaterFactor => Math.Min(1f, WaterBalance) * DroughtGrowthFactor;

	/// <summary>
	/// Whether new leaf-bearing organs may be started. During a drought episode a plant above its
	/// target canopy stops producing foliage before it loses any, and a plant short of carbon
	/// stops new construction - otherwise it builds leaves that the controller then has to remove. A
	/// plant with no green leaf left can only recover by building one, whatever its reserve.
	/// </summary>
	internal bool CanopyGrowthAllowed => !DroughtResponse
		|| ((CarbonStress <= 0f || UncommittedLeafArea <= 0f) && !(DroughtEpisode && UncommittedLeafArea >= CanopyTarget));

	void InitDroughtResponse()
		=> DroughtResponse = Parameters.Behavior == Behavior.Herbaceous && Parameters.CanopyAdjustmentHalfTimeDays > 0f;

	/// <summary>Per census: the filtered water stress, the carbon reserve, starvation and the episode state.</summary>
	void UpdateDroughtSignals(double allocatableWater, double waterStorage, double energy, double energyRequirementPerTick)
	{
		if (!DroughtResponse) return;
		var hours = (float)World.HoursPerTick;
		// Filter the fill, then map it: clamping first would bias ticks that swing day and night
		// against 24-hour ticks that do not.
		var fill = waterStorage > 1e-12 ? (float)Math.Clamp(allocatableWater / waterStorage, 0d, 1d) : 1f;
		WaterFillFiltered += (fill - WaterFillFiltered) * (1f - MathF.Exp(-hours / WaterFilterHours));
		// The first two weeks after germination move the seed's water into tissue that grows faster
		// than its first roots can fill it: every species' fill dipped to 0.2-0.5 for a few days and
		// Carex lost two of its four seedling blades to an episode on day 5 of a run that never saw a
		// drought again. Seedling stress is not modelled; the signal starts after establishment.
		DroughtStress = GetAge_h() < EstablishmentHours ? 0f : Math.Clamp(1f - WaterFillFiltered / WellWateredFill, 0f, 1f);

		var requirementPerHour = energyRequirementPerTick / hours;
		CarbonStress = requirementPerHour > 1e-30
			? (float)Math.Clamp(1d - energy / requirementPerHour / CarbonWarningHours, 0d, 1d) : 0f;
		StarvationHours = energy < energyRequirementPerTick ? StarvationHours + hours : Math.Max(0f, StarvationHours - hours);

		var onset = Parameters.DroughtSenescenceOnset;
		if (DroughtStress > onset)
		{
			if (!DroughtEpisode)
			{
				DroughtEpisode = true;
				// The canopy before the episode, held for its whole length. Re-reading the shrinking
				// canopy every day would compound the reduction towards zero.
				ReferenceCanopyArea = UncommittedLeafArea;
				SenescenceCarry = 0f;
			}
			EpisodeQuietHours = 0f;
		}
		else if (DroughtEpisode)
		{
			// At or below zero for an onset under the hysteresis, which would otherwise never end.
			if (DroughtStress <= Math.Max(0f, onset - EpisodeHysteresis))
			{
				EpisodeQuietHours += hours;
				if (EpisodeQuietHours >= EpisodeQuietHoursToEnd)
				{
					DroughtEpisode = false;
					ReferenceCanopyArea = 0f;
					SenescenceCarry = 0f;
					EpisodeQuietHours = 0f;
				}
			}
			else
				EpisodeQuietHours = 0f;
		}
		var response = DroughtEpisode ? Math.Clamp((DroughtStress - onset) / (1f - onset), 0f, 1f) : 0f;
		CanopyTarget = DroughtEpisode ? ReferenceCanopyArea * (1f - response) : 0f;
	}

	[StructLayout(LayoutKind.Auto)]
	readonly record struct BladeCandidate(float Score, ulong Id, int Root, int Owner, float Area);
	readonly List<BladeCandidate> BladeCandidates = [];
	static readonly Comparison<BladeCandidate> BladeOrder = static (a, b) =>
	{
		var c = b.Score.CompareTo(a.Score);
		return c != 0 ? c : a.Id.CompareTo(b.Id);
	};

	/// <summary>
	/// Daily, after <c>AG.NewDay</c>: commit whole blades to senescence so the green canopy approaches
	/// the drought target with the species' half-time, and shed blades that cost more than they earn
	/// while carbon is short.
	/// </summary>
	void UpdateCanopy(uint timestep)
	{
		if (!DroughtResponse) return;
		var hoursPerTick = World.HoursPerTick;
		var dtHours = LastCanopyUpdate == 0 ? 0f : (timestep - LastCanopyUpdate) * (float)hoursPerTick;
		LastCanopyUpdate = Math.Max(1u, timestep);
		var carbonShort = CarbonStress > 0f;
		if (dtHours <= 0f || (!DroughtEpisode && !carbonShort))
		{
			SenescenceCarry = 0f;
			return;
		}

		var species = Parameters;
		// Exact for a fixed target whatever the tick: 1 - 2^(-dt / T½).
		var step = 1f - MathF.Pow(2f, -dtHours / (24f * species.CanopyAdjustmentHalfTimeDays));
		var resourceMax = AG.DailyResourceMax;
		var blockHours = World.StatsBlockLength * (float)hoursPerTick;

		BladeCandidates.Clear();
		var uncommitted = 0f;
		var netNegative = 0f;
		var largest = 0f;
		for (int i = 0; i < AG.Count; ++i)
		{
			ref readonly var blade = ref AG.GetAgent(i);
			if (blade.Organ != OrganTypes.Leaf || blade.FlowerAgent.flowerBase) continue;
			var parent = blade.Parent;
			if (parent >= 0 && AG.GetOrgan(parent) == OrganTypes.Leaf) continue; //only blade roots
			if (blade.InDroughtSenescence) continue;

			var area = BladeArea(i);
			uncommitted += area;
			largest = MathF.Max(largest, area);
			var owner = AboveGroundAgent.LeafUnitRoot(AG, i);
			if (LeafUnitHasPendingDevelopment(i, owner)) continue;

			// Old blades and poorly lit ones go first; light is booked whether or not the
			// blade had water, so this ranks position, not the drought itself.
			var age = Math.Min(1f, (timestep - blade.BirthTime) * (float)hoursPerTick / species.LeafMaximumAgeHours);
			var light = resourceMax > 0f ? Math.Clamp(blade.PreviousDayEnvResourcesInvariant / resourceMax, 0f, 1f) : 1f;
			// Its own production against its own maintenance over the same stats block - a day,
			// or one tick for ticks of a day or longer.
			var losing = blade.PreviousDayProductionInvariant * blade.Length * blade.Radius * 2f
				< blade.LifeSupportPerHour(species) * blockHours;
			if (losing) netNegative += area;
			var score = (carbonShort && losing ? 2f : 0f) + age + (1f - light);
			BladeCandidates.Add(new(score, blade.StableId, i, owner, area));
		}

		var waterQuota = DroughtEpisode ? Math.Max(0f, uncommitted - CanopyTarget) * step : 0f;
		// Carbon sheds only blades that lose carbon, and sets no area target: shrinking the canopy
		// to save carbon removes the producers and deepens the shortage it answers.
		var carbonQuota = carbonShort ? netNegative * step : 0f;
		var quota = Math.Max(waterQuota, carbonQuota) + SenescenceCarry;

		BladeCandidates.Sort(BladeOrder);
		foreach (var candidate in BladeCandidates)
		{
			if (quota < 0.5f * candidate.Area) break;
			CommitLeafUnit(candidate.Root, candidate.Owner, timestep);
			quota -= candidate.Area;
			uncommitted -= candidate.Area;
		}
		// Fractional quota carries over, so a small plant still sheds at its rate instead of
		// always rounding down to nothing. Bounded, so blades that cannot be committed (a pending
		// development) do not bank an avalanche.
		SenescenceCarry = Math.Clamp(quota, -largest, largest);
		UncommittedLeafArea = Math.Max(0f, uncommitted);

		if (StarvationHours > StarvationToleranceHours)
			RevertWeakestCrown();
	}

	float BladeArea(int root)
	{
		var area = 0f;
		for (int index = root, guard = 0; index >= 0 && guard <= SpeciesSettings.MaximumLeafSegments; ++guard)
		{
			area += AG.GetLength(index) * AG.GetBaseRadius(index) * 2f;
			index = NextBladeSegment(index);
		}
		return area;
	}

	int NextBladeSegment(int index)
	{
		var children = AG.GetChildren(index);
		for (int c = 0; c < children.Count; ++c)
			if (AG.GetOrgan(children[c]) == OrganTypes.Leaf)
				return children[c];
		return -1;
	}

	bool LeafUnitHasPendingDevelopment(int bladeRoot, int owner)
	{
		for (int index = bladeRoot, guard = 0; index >= 0 && guard <= SpeciesSettings.MaximumLeafSegments; ++guard)
		{
			if (AG.HasPendingDevelopment(index)) return true;
			index = NextBladeSegment(index);
		}
		for (int index = AG.GetParent(bladeRoot), guard = 0; index >= 0 && guard <= SpeciesSettings.MaximumPetioleSegments
			&& AG.GetOrgan(index) == OrganTypes.Petiole; ++guard)
		{
			if (AG.HasPendingDevelopment(index)) return true;
			if (index == owner) break;
			index = AG.GetParent(index);
		}
		return AG.HasPendingDevelopment(owner);
	}

	/// <summary>Every blade segment and the petiole it hangs from senesce together, for one sampled duration.</summary>
	void CommitLeafUnit(int bladeRoot, int owner, uint timestep)
	{
		var species = Parameters;
		var hours = species.LeafSenescenceStressDuration + RNG.NextFloatVar(species.LeafSenescenceStressDurationVar);
		var ticks = (ushort)Math.Clamp(MathF.Round(hours / World.HoursPerTick), 1f, ushort.MaxValue);
		for (int index = bladeRoot, guard = 0; index >= 0 && guard <= SpeciesSettings.MaximumLeafSegments; ++guard)
		{
			AG.CommitDroughtSenescence(index, timestep, ticks);
			index = NextBladeSegment(index);
		}
		for (int index = AG.GetParent(bladeRoot), guard = 0; index >= 0 && guard <= SpeciesSettings.MaximumPetioleSegments
			&& AG.GetOrgan(index) == OrganTypes.Petiole; ++guard)
		{
			AG.CommitDroughtSenescence(index, timestep, ticks);
			if (index == owner) break;
			index = AG.GetParent(index);
		}
	}

	/// <summary>
	/// Prolonged unpaid maintenance: revert the least productive crown to its dormant crown bud,
	/// at most one a day. Roots and rhizomes are never removed this way.
	/// </summary>
	void RevertWeakestCrown()
	{
		var weakest = -1;
		var weakestProduction = float.MaxValue;
		for (int i = 0; i < AG.Count; ++i)
		{
			ref readonly var crown = ref AG.GetAgent(i);
			if (crown.IsRizome || crown.Organ != OrganTypes.Stem || crown.Parent < 0 || !AG.GetIsRizome(crown.Parent)
				|| AG.HasPendingDevelopment(i))
				continue;
			if (crown.PreviousDayProductionInvariant < weakestProduction)
			{
				weakestProduction = crown.PreviousDayProductionInvariant;
				weakest = i;
			}
		}
		if (weakest >= 0)
			AG.RevertCrownToBud(weakest);
	}

	void WriteDroughtState(BinaryWriter writer)
	{
		writer.Write(DroughtStress);
		writer.Write(WaterFillFiltered);
		writer.Write(CarbonStress);
		writer.Write(DroughtEpisode);
		writer.Write(EpisodeQuietHours);
		writer.Write(ReferenceCanopyArea);
		writer.Write(CanopyTarget);
		writer.Write(UncommittedLeafArea);
		writer.Write(SenescenceCarry);
		writer.Write(StarvationHours);
		writer.Write(LastCanopyUpdate);
	}

	void ReadDroughtState(BinaryReader reader)
	{
		DroughtStress = reader.ReadSingle();
		WaterFillFiltered = reader.ReadSingle();
		CarbonStress = reader.ReadSingle();
		DroughtEpisode = reader.ReadBoolean();
		EpisodeQuietHours = reader.ReadSingle();
		ReferenceCanopyArea = reader.ReadSingle();
		CanopyTarget = reader.ReadSingle();
		UncommittedLeafArea = reader.ReadSingle();
		SenescenceCarry = reader.ReadSingle();
		StarvationHours = reader.ReadSingle();
		LastCanopyUpdate = reader.ReadUInt32();
		if (!float.IsFinite(DroughtStress + WaterFillFiltered + CarbonStress + EpisodeQuietHours + ReferenceCanopyArea
			+ CanopyTarget + UncommittedLeafArea + SenescenceCarry + StarvationHours))
			throw new InvalidDataException("The checkpoint contains an invalid drought state.");
	}
}
