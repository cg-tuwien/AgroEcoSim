using System.Buffers;
using System.Numerics;

namespace Agro;

/// <summary>
/// The colour of every above-ground organ of one plant, for one exported frame.
/// </summary>
/// <remarks>
/// <para>
/// One place decides what colour an organ is, so the parts of a plant cannot drift apart again:
/// leaves, petioles and petals follow a phenology curve; stems follow the species'
/// <see cref="StemColorModel"/>; a scape, a bud and a flower's centre state their own colour or
/// fall back to the stem's; and everything that dies dries towards straw or tan instead of
/// keeping the colour it had alive. See <c>Agro/ORGAN_COLORS.md</c>.
/// </para>
/// <para>
/// It reads the plant and nothing else, and it changes no simulation state, so plants are still
/// written concurrently and a frame does not depend on which frames were exported before it.
/// What is constant over the plant is resolved once in the constructor; per organ a stem costs
/// one table lookup and a full-grown healthy leaf one cached colour.
/// </para>
/// </remarks>
internal struct OrganColorContext
{
	readonly SpeciesSettings Species;
	readonly FlowerSettings Flowers;
	readonly StemColorModel Stems;
	readonly uint Timestep;
	readonly float HoursPerTick;
	readonly float Season;
	readonly bool Herbaceous;
	/// <summary>Whole-plant ripening of a monocarpic plant, 0 to 1; 0 otherwise.</summary>
	readonly float Ripening;
	readonly float PetalShed;

	readonly RGB8 ScapeWire, CentreWire, BudWire;
	/// <summary>Colour of a living bud that is not open: its own, else the scape's, else none.</summary>
	readonly Vector3? ClosedBud;
	/// <summary>Colour of an open flower's centre: its own, else the scape's, else none.</summary>
	readonly Vector3? OpenCentre;

	/// <summary>Whether organ <c>i</c> carries a petal: an open flower. Rented, null without petals.</summary>
	bool[]? OpenFlower;
	bool HasGrownBlade, HasGrownPetiole;
	RGB8 GrownBlade, GrownPetiole;

	public OrganColorContext(PlantFormation2 plant, int count)
	{
		var world = plant.World;
		Species = plant.Parameters;
		Flowers = Species.FlowerSettings;
		Stems = Species.EffectiveStemColors;
		Timestep = world.Timestep;
		HoursPerTick = world.HoursPerTick;
		Season = world.Season;
		Herbaceous = Species.Behavior == Behavior.Herbaceous;
		Ripening = plant.AG.MonocarpicRipening();
		PetalShed = Math.Clamp(Flowers.PetalAbscissionSenescence, 1e-3f, 1f);

		ClosedBud = Flowers.BudColor ?? Flowers.ScapeColor;
		OpenCentre = Flowers.FlowerCentreColor ?? Flowers.ScapeColor;
		ScapeWire = Fixed(Flowers.ScapeColor);
		CentreWire = Fixed(OpenCentre);
		BudWire = Fixed(ClosedBud);

		//A flower is open while it carries a petal. Nothing on the bud says so, and its parent
		//index is the only link, so the petals mark their buds once per frame.
		OpenFlower = null;
		var ag = plant.AG;
		for (int i = 0; i < count; ++i)
			if (ag.GetOrgan(i) == OrganTypes.FlowerPadel)
			{
				if (OpenFlower == null)
				{
					OpenFlower = ArrayPool<bool>.Shared.Rent(count);
					Array.Clear(OpenFlower, 0, count);
				}
				var parent = ag.GetParent(i);
				if (parent >= 0 && parent < count) OpenFlower[parent] = true;
			}

		HasGrownBlade = HasGrownPetiole = false;
		GrownBlade = GrownPetiole = default;
	}

	static RGB8 Fixed(Vector3? srgb) => srgb.HasValue
		? ColorUtils.AppearanceToWire(ColorUtils.SrgbToLinear(srgb.Value), ColorUtils.RoundOrganExposure)
		: default;

	/// <summary>Returns the rented scratch. The context must not be used afterwards.</summary>
	public void Dispose()
	{
		if (OpenFlower != null) ArrayPool<bool>.Shared.Return(OpenFlower);
		OpenFlower = null;
	}

	/// <summary>Colour of a leaf, a petal, a fruit or a fruit wing.</summary>
	public RGB8 Lamina(OrganTypes organ, in AboveGroundAgent agent)
	{
		switch (organ)
		{
			//A fruit's colour is ripened by the simulation and kept on the agent as authored
			//sRGB; only its way onto the wire is the same as everything else's.
			case OrganTypes.Fruit: return FixedFruit(agent.Color, ColorUtils.RoundOrganExposure);
			case OrganTypes.FruitWing: return FixedFruit(agent.Color, ColorUtils.LaminaExposure);
			case OrganTypes.FlowerPadel:
				//Read at senescence / shed point: the authored curve ends where the petal falls.
				return Flowers.FlowerPhenology.WireColor(Season, agent.Adulcy(Timestep), 0f,
					MathF.Min(1f, agent.Senescence(Timestep) / PetalShed), ColorUtils.LaminaExposure, agent.Stress);
			default: return Foliage(in agent, ColorUtils.LaminaExposure, ref HasGrownBlade, ref GrownBlade);
		}
	}

	static RGB8 FixedFruit(Vector3 srgb, float exposure)
		=> ColorUtils.AppearanceToWire(ColorUtils.SrgbToLinear(Vector3.Clamp(srgb, Vector3.Zero, Vector3.One)), exposure);

	/// <summary>A blade or its petiole on the leaf phenology.</summary>
	RGB8 Foliage(in AboveGroundAgent agent, float exposure, ref bool hasGrown, ref RGB8 grown)
	{
		var senescence = agent.Senescence(Timestep);
		//The leaf's own path, chosen when it was committed: a drought-killed leaf dries, one
		//resorbed in autumn or retired for carbon with water yellows first. Tree autumn colour
		//and monocarpic ripening are resorption too.
		var path = senescence > 0f ? agent.Stress : 0f;
		if (Ripening > senescence) senescence = Ripening;
		var adulcy = agent.Adulcy(Timestep);
		if (senescence > 0f || adulcy < 1f)
			return Species.LeafPhenology.WireColor(Season, adulcy, 0f, senescence, exposure, path);

		//Most of a canopy is full-grown and healthy, and all of that is one colour this frame.
		if (!hasGrown)
		{
			grown = Species.LeafPhenology.WireColor(Season, 1f, 0f, 0f, exposure);
			hasGrown = true;
		}
		return grown;
	}

	/// <summary>Colour of a stem, a meristem, a petiole or any part of an inflorescence axis.</summary>
	public RGB8 Cylinder(OrganTypes organ, int index, in AboveGroundAgent agent)
	{
		var ageHours = (Timestep - agent.BirthTime) * HoursPerTick;
		switch (organ)
		{
			case OrganTypes.Petiole:
				return Foliage(in agent, ColorUtils.RoundOrganExposure, ref HasGrownPetiole, ref GrownPetiole);

			case OrganTypes.FlowerStem:
			case OrganTypes.FlowerMeristem:
			case OrganTypes.FlowerPedicel:
			{
				var dying = FloralDying(in agent);
				if (Flowers.ScapeColor.HasValue)
					return dying > 0f ? Stems.WireDrying(Flowers.ScapeColor.Value, dying) : ScapeWire;
				return Stems.Wire(Stems.Maturity(0f, ageHours), 0f, dying);
			}

			case OrganTypes.FlowerBud:
			{
				var dying = FloralDying(in agent);
				var open = OpenFlower != null && OpenFlower[index];
				var own = open ? OpenCentre : ClosedBud;
				if (own.HasValue)
					return dying > 0f ? Stems.WireDrying(own.Value, dying) : open ? CentreWire : BudWire;
				//No colour of its own: a calyx is young stem tissue until the flower opens.
				return Stems.Wire(open ? Stems.Maturity(0f, ageHours) : 0f, 0f, dying);
			}

			default:
				//Stem and meristem. A rhizome is old from birth: scale leaves over a brown axis.
				if (agent.IsRizome) return Stems.Wire(1f, 1f, 0f);
				//A growing tip is fresh tissue however long it has been growing: only its wood
				//ratio hardens it, which is what turns a tree's resting bud twig-brown.
				if (organ == OrganTypes.Meristem) return Stems.Wire(Stems.Maturity(agent.WoodRatio(), 0f), 0f, Ripening);
				return Stems.Wire(Stems.Maturity(agent.WoodRatio(), ageHours), Stems.Ageing(agent.Radius, ageHours), Ripening);
		}
	}

	/// <summary>
	/// How dry a floral organ is. A spent herbaceous flower raises the wood factor of its bud,
	/// pedicel and scape to one before they are removed (FlowerHelper.receit), which is exactly
	/// the drying of a spent inflorescence; a tree's flower axis is shed green.
	/// </summary>
	readonly float FloralDying(in AboveGroundAgent agent)
		=> Herbaceous ? MathF.Max(agent.WoodRatio(), Ripening) : 0f;
}
