using System;
using System.Text.RegularExpressions;

namespace Agro;

/// <summary>
/// Selects obstacles by name for the leaf area index measurement: a case-insensitive regular
/// expression, with a case-insensitive substring match as the fallback when the pattern does not
/// parse or a match times out.
/// </summary>
/// <remarks>
/// The fallback exists because the filter arrives from a text box and a run must not die on a
/// stray bracket; the mode and the parser's message travel with the measurement so the reader can
/// see which rule actually selected the obstacles.
/// </remarks>
public sealed class ObstacleNameFilter
{
	public const string RegexMode = "regex";
	public const string SubstringMode = "substring";
	static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

	public string Text { get; }
	/// <summary>Which rule is in force: <see cref="RegexMode"/> or <see cref="SubstringMode"/>.</summary>
	public string Mode { get; private set; }
	/// <summary>Why the regular expression was not used, or null when it was.</summary>
	public string? Error { get; private set; }
	Regex? Pattern;

	public ObstacleNameFilter(string text)
	{
		Text = text.Trim();
		Error = Validate(Text);
		if (Error == null)
		{
			Pattern = new Regex(Text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
			Mode = RegexMode;
		}
		else
			Mode = SubstringMode;
	}

	/// <summary>The parser's message when <paramref name="text"/> is not a valid pattern, else null.</summary>
	public static string? Validate(string text)
	{
		try
		{
			_ = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, MatchTimeout);
			return null;
		}
		catch (ArgumentException e)
		{
			return e.Message;
		}
	}

	public bool IsMatch(string name)
	{
		if (Pattern != null)
		{
			try { return Pattern.IsMatch(name); }
			catch (RegexMatchTimeoutException)
			{
				//Once is enough: a pattern that times out on one name will on the next, and the
				//substring rule answers the same question for the rest of the list.
				Pattern = null;
				Mode = SubstringMode;
				Error = $"The pattern took longer than {MatchTimeout.TotalMilliseconds} ms to match \"{name}\".";
			}
		}
		return name.Contains(Text, StringComparison.OrdinalIgnoreCase);
	}
}
