using System;

namespace Agro;

/// <summary>
/// The reporting periods of a run: 7-day blocks counted from the local start date, local calendar
/// years, and the whole run.
/// </summary>
/// <remarks>
/// <para>
/// One helper for every component that aggregates by period - the obstacle light meter and the
/// tray vegetation survey - so none of them computes dates on its own and the choice of period can
/// be changed in one place. Weeks are counted from the start date rather than ISO weeks because
/// that is what the study asks for: "the value on 1 Feb, 8 Feb, 15 Feb ...".
/// </para>
/// <para>
/// Calendar years do not nest in 7-day blocks (a week straddles 1 January unless the start date
/// happens to line up), so an annual record cannot be derived from weekly ones. Every component
/// therefore folds into a weekly and an annual bucket at once and derives the total from the
/// years.
/// </para>
/// </remarks>
public sealed class PeriodCalendar
{
	/// <summary>Local date of the first simulated day.</summary>
	public DateTime Start { get; }
	/// <summary>Number of local dates the run can touch, the horizon included.</summary>
	public int Days { get; }
	/// <summary>Last local date of the run.</summary>
	public DateTime End => Start.AddDays(Days - 1);
	public int WeekCount => (Days + 6) / 7;
	public int YearCount => End.Year - Start.Year + 1;

	public const string WeekRule = "Week i covers the 7 local dates from start + 7i; the last week is clipped to the run.";
	public const string YearRule = "Local calendar years; the first and last are clipped to the run.";

	public PeriodCalendar(DateTime startLocalDate, int days)
	{
		Start = startLocalDate.Date;
		Days = Math.Max(1, days);
	}

	/// <summary>
	/// The local date a step is credited to: the date of its midpoint. Dating by the start put a
	/// 24-hour step that begins at 23:00 after a daylight-saving shift entirely on the wrong day.
	/// </summary>
	public static DateTime StepDate(DateTime localStepStart, float hoursPerTick)
		=> localStepStart.AddHours(hoursPerTick * 0.5).Date;

	public int WeekIndex(DateTime localDate) => Math.Max(0, (localDate.Date - Start).Days / 7);
	public int YearIndex(DateTime localDate) => Math.Max(0, localDate.Year - Start.Year);

	public (DateTime Start, DateTime End) Week(int index)
	{
		var start = Start.AddDays(7L * index);
		var end = start.AddDays(6);
		return (start, end > End ? End : end);
	}

	public (int Year, DateTime Start, DateTime End) Year(int index)
	{
		var year = Start.Year + index;
		var start = index == 0 ? Start : new DateTime(year, 1, 1);
		var end = year == End.Year ? End : new DateTime(year, 12, 31);
		return (year, start, end);
	}
}
