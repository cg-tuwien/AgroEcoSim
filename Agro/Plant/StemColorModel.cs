using System.Numerics;
using System.Runtime.CompilerServices;

namespace Agro;

/// <summary>
/// The colour of a stem through its life: four authored colours and the three scales that
/// say when an organ passes from one to the next.
/// </summary>
/// <remarks>
/// <para>
/// A young shoot is green because its epidermis is still transparent over a chlorenchyma
/// cortex - paler and yellower than the blade, since a stem holds a third to two thirds of
/// the leaf's chlorophyll. What happens next depends on the habit. A woody shoot forms its
/// first periderm within the season: tannin-filled cork replaces the epidermis and gives the
/// species its twig colour. With girth the outer bark builds up and the twig colour gives way
/// to the bark colour. A herbaceous stem forms no periderm: it hardens to a duller green,
/// often flushed red, its persistent base browns under old leaf bases, and when it dies it
/// bleaches to straw. See <c>Agro/ORGAN_COLORS.md</c> for the sources.
/// </para>
/// <para>
/// The colour is <c>Young -> Mature</c> by maturity, <c>-> Old</c> by ageing, and
/// <c>-> Dead</c> by dying, interpolated in OKLab. All of it is tabulated once per species as
/// wire bytes, so the export of a tree with several hundred thousand stem segments is one
/// table lookup per segment.
/// </para>
/// </remarks>
public sealed class StemColorModel
{
	const int StageSteps = 128;
	const int DyingSteps = 32;

	/// <summary>Fresh extension growth, sRGB from 0 to 1.</summary>
	public Vector3 Young { get; }
	/// <summary>A hardened herbaceous stem, or a woody shoot's first periderm - its twig colour.</summary>
	public Vector3 Mature { get; }
	/// <summary>Mature bark of a woody axis; the caudex or persistent base of a herb; every rhizome.</summary>
	public Vector3 Old { get; }
	/// <summary>A dead stem still standing: straw.</summary>
	public Vector3 Dead { get; }

	/// <summary>Age at which a stem that does not lignify is fully <see cref="Mature"/>, in hours.
	/// Zero leaves maturity to the wood ratio alone, which is what a woody species wants.</summary>
	public float MatureAgeHours { get; }
	/// <summary>Age at which a stem is fully <see cref="Old"/>, in hours; zero switches ageing by time off.</summary>
	public float OldAgeHours { get; }
	/// <summary>Radius at which a woody axis carries its mature bark, in metres; zero switches
	/// ageing by girth off. The change starts at a quarter of it.</summary>
	public float OldRadius { get; }

	readonly Vector3 DeadLab;
	/// <summary>Wire bytes over (stage, dying), three per entry, for a round organ.</summary>
	readonly byte[] Table = new byte[StageSteps * DyingSteps * 3];

	/// <param name="young">sRGB bytes, like the keys of the leaf phenology.</param>
	public StemColorModel(Vector3 young, Vector3 mature, Vector3 old, Vector3 dead,
		float matureAgeDays = 0f, float oldAgeDays = 0f, float oldRadius = 0f)
	{
		Young = young / 255f; Mature = mature / 255f; Old = old / 255f; Dead = dead / 255f;
		MatureAgeHours = matureAgeDays * 24f;
		OldAgeHours = oldAgeDays * 24f;
		OldRadius = oldRadius;

		var youngLab = ColorUtils.OkLabFromSrgb(Young);
		var matureLab = ColorUtils.OkLabFromSrgb(Mature);
		var oldLab = ColorUtils.OkLabFromSrgb(Old);
		DeadLab = ColorUtils.OkLabFromSrgb(Dead);
		for (int d = 0; d < DyingSteps; ++d)
		{
			var dying = d / (float)(DyingSteps - 1);
			for (int s = 0; s < StageSteps; ++s)
			{
				var stage = 2f * s / (StageSteps - 1);
				var alive = stage <= 1f
					? Vector3.Lerp(youngLab, matureLab, stage)
					: Vector3.Lerp(matureLab, oldLab, stage - 1f);
				var wire = ColorUtils.OklabToWire(Vector3.Lerp(alive, DeadLab, dying), ColorUtils.RoundOrganExposure);
				var o = (d * StageSteps + s) * 3;
				Table[o] = wire.Red; Table[o + 1] = wire.Green; Table[o + 2] = wire.Blue;
			}
		}
	}

	/// <summary>How far a stem has hardened, from 0 to 1: its wood ratio, or its age where the
	/// species does not lignify.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Maturity(float woodRatio, float ageHours)
	{
		var byAge = MatureAgeHours > 0f ? ageHours / MatureAgeHours : 0f;
		return Math.Clamp(MathF.Max(woodRatio, byAge), 0f, 1f);
	}

	/// <summary>How far a mature stem has aged towards <see cref="Old"/>, from 0 to 1. Girth
	/// drives bark - it is monotone and tapers from the base of an axis to its tip - and time
	/// drives the browning of a herbaceous base.</summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float Ageing(float radius, float ageHours)
	{
		var byGirth = OldRadius > 0f ? ColorUtils.SmoothStep(0.25f * OldRadius, OldRadius, radius) : 0f;
		var byAge = OldAgeHours > MatureAgeHours ? ColorUtils.SmoothStep(MatureAgeHours, OldAgeHours, ageHours) : 0f;
		return MathF.Max(byGirth, byAge);
	}

	/// <summary>The wire colour of a stem segment.</summary>
	/// <param name="maturity">0 fresh, 1 hardened: <see cref="Maturity"/>.</param>
	/// <param name="ageing">0 mature, 1 old: <see cref="Ageing"/>.</param>
	/// <param name="dying">0 alive, 1 dead and dry.</param>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public RGB8 Wire(float maturity, float ageing, float dying)
	{
		//Ageing only counts on what has matured: a thick shoot that is still green is not bark.
		var stage = Math.Clamp(maturity + maturity * ageing, 0f, 2f);
		var s = (int)(stage * (0.5f * (StageSteps - 1)) + 0.5f);
		var d = (int)(Math.Clamp(dying, 0f, 1f) * (DyingSteps - 1) + 0.5f);
		var o = (d * StageSteps + s) * 3;
		return new RGB8(Table[o], Table[o + 1], Table[o + 2]);
	}

	/// <summary>The wire colour of an organ whose own colour is <paramref name="srgb"/> while it
	/// lives - a scape, a bud, a flower's centre - drying to <see cref="Dead"/>.</summary>
	public RGB8 WireDrying(Vector3 srgb, float dying)
		=> ColorUtils.OklabToWire(
			Vector3.Lerp(ColorUtils.OkLabFromSrgb(srgb), DeadLab, Math.Clamp(dying, 0f, 1f)),
			ColorUtils.RoundOrganExposure);

	/// <summary>A herbaceous stem of no particular species: fresh green, a duller hardened
	/// green after a month, a brown base after most of a season, straw when dead.</summary>
	public static readonly StemColorModel Herbaceous = new(
		new(121, 176, 51), new(86, 140, 44), new(124, 105, 55), new(183, 154, 93),
		matureAgeDays: 30f, oldAgeDays: 200f);

	/// <summary>A woody axis of no particular species: green shoot, brown twig, grey-brown bark.</summary>
	public static readonly StemColorModel Woody = new(
		new(112, 154, 42), new(126, 94, 50), new(110, 105, 98), new(154, 134, 106),
		oldRadius: 0.03f);
}
