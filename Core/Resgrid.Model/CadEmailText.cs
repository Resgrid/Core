using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>What a CAD email asks for besides its content: nothing, closing the call, or cancelling it.</summary>
	public enum CadEmailCloseKinds
	{
		None = 0,
		Close = 1,
		Cancel = 2
	}

	/// <summary>
	/// Pure text rules of CAD email updates (cad-email-call-updates-plan.md §4.2, §4.8): reading the CAD call ID, spotting a close
	/// or cancel phrase, normalising IDs and values for comparison, and the value hashes kept in the update ledger.
	/// </summary>
	public static class CadEmailText
	{
		/// <summary>Patterns get this long to match, so a pattern that backtracks badly cannot hold up an inbound email.</summary>
		public static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(100);

		private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);
		private static readonly Regex Tags = new Regex(@"<[^>]*>", RegexOptions.Compiled);
		private static readonly Regex BreakTags = new Regex(@"<\s*(br|/p|/div|/tr|/li|/h[1-6])\s*/?\s*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

		/// <summary>
		/// The CAD call ID an email carries under <paramref name="config"/>, normalised, or null. <paramref name="formatKey"/> is the
		/// format's own key (<see cref="CallEmailParseResult.MatchKey"/>) for <see cref="CadCallIdSources.FromFormat"/>.
		/// </summary>
		public static string ExtractCallId(EmailCallUpdateConfig config, CallEmail email, string formatKey)
		{
			if (config == null)
				return null;

			switch (config.GetIdSource())
			{
				case CadCallIdSources.FromFormat:
					return NormalizeId(formatKey);
				case CadCallIdSources.Label:
					return NormalizeId(SearchAreas(config, email).Select(text => AfterLabel(text, config.IdLabel)).FirstOrDefault(v => v != null));
				case CadCallIdSources.Pattern:
					return NormalizeId(SearchAreas(config, email).Select(text => FirstGroup(text, config.IdPattern)).FirstOrDefault(v => v != null));
				default:
					return null;
			}
		}

		/// <summary>Whether the email is the CAD closing or cancelling the call (cancel phrases are checked first).</summary>
		public static CadEmailCloseKinds DetectClose(EmailCallUpdateConfig config, CallEmail email)
		{
			if (config == null || email == null)
				return CadEmailCloseKinds.None;

			var cancel = config.GetCancelPhrases();
			var close = config.GetClosePhrases();
			if (cancel.Count == 0 && close.Count == 0)
				return CadEmailCloseKinds.None;

			var texts = new[] { email.Subject, BodyText(email) };
			if (cancel.Any(p => texts.Any(t => ContainsPhrase(t, p))))
				return CadEmailCloseKinds.Cancel;
			if (close.Any(p => texts.Any(t => ContainsPhrase(t, p))))
				return CadEmailCloseKinds.Close;

			return CadEmailCloseKinds.None;
		}

		/// <summary>An ID as compared and stored: trimmed, inner whitespace collapsed, null when empty or longer than 100 characters.</summary>
		public static string NormalizeId(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			var normalized = Whitespace.Replace(id.Trim(), " ");
			return normalized.Length > CallEmailUpdate.MaxCadCallIdLength ? null : normalized;
		}

		/// <summary>True when two IDs are the same CAD call (normalised, case ignored).</summary>
		public static bool SameId(string a, string b)
		{
			var x = NormalizeId(a);
			var y = NormalizeId(b);
			return x != null && y != null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>A value as compared for "did it change": trimmed, inner whitespace collapsed, case ignored by <see cref="SameValue"/>.</summary>
		public static string NormalizeValue(string value)
		{
			return string.IsNullOrWhiteSpace(value) ? string.Empty : Whitespace.Replace(value.Trim(), " ");
		}

		public static bool SameValue(string a, string b)
		{
			return string.Equals(NormalizeValue(a), NormalizeValue(b), StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>SHA-256 (lower-case hex) of a normalised, upper-cased value: the ledger's record of what the CAD set.</summary>
		public static string Hash(string value)
		{
			using (var sha = SHA256.Create())
			{
				var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(NormalizeValue(value).ToUpperInvariant()));
				var sb = new StringBuilder(bytes.Length * 2);
				foreach (var b in bytes)
					sb.Append(b.ToString("x2"));
				return sb.ToString();
			}
		}

		/// <summary>The email's body as plain text: the text body, else the HTML body with tags removed.</summary>
		public static string BodyText(CallEmail email)
		{
			if (email == null)
				return null;

			if (!string.IsNullOrWhiteSpace(email.TextBody))
				return email.TextBody;

			return HtmlToText(email.Body);
		}

		/// <summary>HTML as plain text, line breaks kept where block elements end.</summary>
		public static string HtmlToText(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return html;

			var withBreaks = BreakTags.Replace(html, "\n");
			return WebUtility.HtmlDecode(Tags.Replace(withBreaks, string.Empty));
		}

		/// <summary>
		/// The first word after <paramref name="label"/> on the same line, ignoring case; separators such as ":", "#" or "-" right
		/// after the label are skipped. Null when the label is not there or nothing follows it.
		/// </summary>
		public static string AfterLabel(string text, string label)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(label))
				return null;

			var trimmedLabel = label.Trim();
			var index = text.IndexOf(trimmedLabel, StringComparison.OrdinalIgnoreCase);
			while (index >= 0)
			{
				var rest = text.Substring(index + trimmedLabel.Length);
				var lineEnd = rest.IndexOfAny(new[] { '\r', '\n' });
				var line = (lineEnd >= 0 ? rest.Substring(0, lineEnd) : rest).TrimStart(' ', '\t', ':', '#', '-', '=', '.');
				var word = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
				if (!string.IsNullOrWhiteSpace(word))
					return word.Trim().TrimEnd(',', ';', '.');

				index = text.IndexOf(trimmedLabel, index + trimmedLabel.Length, StringComparison.OrdinalIgnoreCase);
			}

			return null;
		}

		/// <summary>The first capture group of <paramref name="pattern"/> in <paramref name="text"/>; null on no match, a bad pattern or a timeout.</summary>
		public static string FirstGroup(string text, string pattern)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(pattern))
				return null;

			try
			{
				var match = Regex.Match(text, pattern.Trim(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
				if (!match.Success)
					return null;

				return match.Groups.Count > 1 && match.Groups[1].Success ? match.Groups[1].Value : null;
			}
			catch (RegexMatchTimeoutException)
			{
				return null;
			}
			catch (ArgumentException)
			{
				return null;
			}
		}

		/// <summary>Whole-phrase, case-insensitive: "closed" does not match inside "disclosed".</summary>
		public static bool ContainsPhrase(string text, string phrase)
		{
			if (string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(phrase))
				return false;

			var pattern = @"(?<![\p{L}\p{N}])" + Regex.Escape(phrase.Trim()) + @"(?![\p{L}\p{N}])";
			try
			{
				return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, PatternTimeout);
			}
			catch (RegexMatchTimeoutException)
			{
				return false;
			}
		}

		private static IEnumerable<string> SearchAreas(EmailCallUpdateConfig config, CallEmail email)
		{
			if (email == null)
				yield break;

			switch (config.GetIdSearchIn())
			{
				case CadCallIdSearchAreas.Subject:
					yield return email.Subject;
					break;
				case CadCallIdSearchAreas.Body:
					yield return BodyText(email);
					break;
				default:
					yield return email.Subject;
					yield return BodyText(email);
					break;
			}
		}
	}
}
