using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Framework
{
	/// <summary>
	/// Prepares outbound SMS content for deliverability and cost. US carriers (A2P 10DLC) filter messages that
	/// contain public/unbranded links (bit.ly, tinyurl, unknown domains), so links whose host is not on the supplied
	/// allow-list are removed. Common non-GSM-7 characters are normalized so a stray smart-quote/em-dash doesn't flip
	/// the whole message into costlier UCS-2 encoding (67 vs 153 chars per segment), and the body is capped so we
	/// never pay for an excessively long message (or get rejected for length - Twilio error 21617 at 1600 chars).
	/// Pure (no config dependency): callers pass the allow-list + max length.
	/// </summary>
	public static class SmsContentHelper
	{
		private const string TruncationSuffix = "... (open Resgrid for the full message)";

		// Only scheme-qualified (http/https) or www-prefixed links are treated as URLs. Matching bare "domain.tld"
		// would mangle ordinary text like "U.S.", "9-1-1", or "400 Olive St." that appears in real messages.
		private static readonly Regex UrlRegex = new Regex(
			@"(?:https?://|www\.)\S+",
			RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static readonly Regex MultiSpaceRegex = new Regex(@"[ \t]{2,}", RegexOptions.Compiled);

		/// <summary>Strip-disallowed-URLs, normalize, then truncate. Returns a carrier-safe, cost-bounded body.</summary>
		public static string PrepareForSms(string message, int maxLength, IEnumerable<string> allowedUrlDomains)
		{
			if (string.IsNullOrEmpty(message))
				return message;

			message = StripDisallowedUrls(message, allowedUrlDomains);
			message = NormalizeForGsm(message);
			message = Truncate(message, maxLength);
			return message;
		}

		/// <summary>Removes any URL whose host is not on the allow-list (host or a subdomain of an allowed domain).</summary>
		public static string StripDisallowedUrls(string message, IEnumerable<string> allowedUrlDomains)
		{
			if (string.IsNullOrEmpty(message))
				return message;

			var allowed = (allowedUrlDomains ?? Enumerable.Empty<string>())
				.Where(d => !string.IsNullOrWhiteSpace(d))
				.Select(d => d.Trim().TrimStart('.').ToLowerInvariant())
				.ToList();

			var stripped = false;
			var result = UrlRegex.Replace(message, match =>
			{
				if (IsAllowedUrl(match.Value, allowed))
					return match.Value;

				stripped = true;
				return string.Empty;
			});

			if (!stripped)
				return message;

			// Collapse whitespace / dangling separators left where a link was removed.
			result = MultiSpaceRegex.Replace(result, " ");
			result = result.Replace(" .", ".").Replace(" ,", ",").Replace("( )", "").Replace("()", "");
			return result.Trim();
		}

		private static bool IsAllowedUrl(string token, List<string> allowedDomains)
		{
			var host = ExtractHost(token);
			if (string.IsNullOrEmpty(host))
				return false;

			return allowedDomains.Any(d =>
				host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
				host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
		}

		private static string ExtractHost(string token)
		{
			var s = token.Trim();

			var scheme = s.IndexOf("://", StringComparison.Ordinal);
			if (scheme >= 0)
				s = s.Substring(scheme + 3);

			// Drop anything from the first path/query/fragment separator onward.
			var sep = s.IndexOfAny(new[] { '/', '?', '#', '\\' });
			if (sep >= 0)
				s = s.Substring(0, sep);

			var at = s.IndexOf('@');         // strip userinfo
			if (at >= 0) s = s.Substring(at + 1);

			var colon = s.IndexOf(':');      // strip port
			if (colon >= 0) s = s.Substring(0, colon);

			return s.Trim().Trim('.').ToLowerInvariant();
		}

		/// <summary>Replaces the most common copy-paste non-GSM-7 characters so the body stays single-byte (cheaper).</summary>
		public static string NormalizeForGsm(string message)
		{
			if (string.IsNullOrEmpty(message))
				return message;

			return message
				.Replace('‘', '\'').Replace('’', '\'')   // curly single quotes / smart apostrophe
				.Replace('“', '"').Replace('”', '"')     // curly double quotes
				.Replace('–', '-').Replace('—', '-')     // en dash / em dash
				.Replace("…", "...")                          // horizontal ellipsis
				.Replace(' ', ' ');                           // non-breaking space
		}

		/// <summary>Truncates to <paramref name="maxLength"/> with a clear suffix when over the limit.</summary>
		public static string Truncate(string message, int maxLength)
		{
			if (maxLength <= 0 || string.IsNullOrEmpty(message) || message.Length <= maxLength)
				return message;

			if (maxLength <= TruncationSuffix.Length)
				return message.Substring(0, maxLength);

			return message.Substring(0, maxLength - TruncationSuffix.Length) + TruncationSuffix;
		}
	}
}
