using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	/// <summary>
	/// Builds the spoken dispatch prompt for outbound voice calls. Shared between the
	/// Twilio voice webhook (which plays the prompt) and the call broadcast worker
	/// (which pre-warms the TTS audio while recipients' phones are still ringing).
	/// The TTS cache key is a hash of the exact chunk text, so both sides must produce
	/// byte-identical text and identical chunk boundaries for the pre-warm to count.
	/// </summary>
	public static class DispatchVoicePromptBuilder
	{
		// Ordinal wording for re-dispatches ("Second alarm"). Index = dispatch count.
		private static readonly string[] AlarmOrdinals =
		{
			null, null, "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth"
		};

		// Trailing address segments that carry no value over the phone. Country names
		// and state/zip segments are stripped from the END only, so freeform locations
		// ("Building 5, Floor 3, Room 10") are never touched.
		private static readonly HashSet<string> CountryNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"USA", "U.S.A.", "US", "U.S.", "United States", "United States of America", "Canada"
		};

		private static readonly HashSet<string> StateNames = new(StringComparer.OrdinalIgnoreCase)
		{
			"Alabama", "Alaska", "Arizona", "Arkansas", "California", "Colorado", "Connecticut", "Delaware",
			"Florida", "Georgia", "Hawaii", "Idaho", "Illinois", "Indiana", "Iowa", "Kansas", "Kentucky",
			"Louisiana", "Maine", "Maryland", "Massachusetts", "Michigan", "Minnesota", "Mississippi",
			"Missouri", "Montana", "Nebraska", "Nevada", "New Hampshire", "New Jersey", "New Mexico",
			"New York", "North Carolina", "North Dakota", "Ohio", "Oklahoma", "Oregon", "Pennsylvania",
			"Rhode Island", "South Carolina", "South Dakota", "Tennessee", "Texas", "Utah", "Vermont",
			"Virginia", "Washington", "West Virginia", "Wisconsin", "Wyoming", "District of Columbia",
			"Puerto Rico", "Guam",
			// Canadian provinces/territories
			"Alberta", "British Columbia", "Manitoba", "New Brunswick", "Newfoundland and Labrador",
			"Nova Scotia", "Ontario", "Prince Edward Island", "Quebec", "Saskatchewan",
			"Northwest Territories", "Nunavut", "Yukon"
		};

		// Two-letter state codes match case-sensitively: CAD feeds emit them upper-case,
		// and a case-insensitive match would eat street segments like "La" or "In".
		private static readonly HashSet<string> StateAbbreviations = new(StringComparer.Ordinal)
		{
			"AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA", "HI", "ID", "IL", "IN", "IA",
			"KS", "KY", "LA", "ME", "MD", "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
			"NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC", "SD", "TN", "TX", "UT", "VT",
			"VA", "WA", "WV", "WI", "WY", "DC", "PR", "GU",
			// Canadian provinces/territories
			"AB", "BC", "MB", "NB", "NL", "NS", "ON", "PE", "QC", "SK", "NT", "NU", "YT"
		};

		private static readonly Regex PostalCodeRegex = new(@"^(\d{5}(-\d{4})?|[A-Za-z]\d[A-Za-z]\s?\d[A-Za-z]\d)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

		public static string BuildDispatchPrompt(Call call, string address)
		{
			// Periods between the segments give the TTS engine sentence boundaries
			// (Piper inserts 0.35s of silence per sentence), which keeps the alarm
			// intro, nature, address and priority audibly separated.
			var nature = StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall);
			var intro = BuildAlarmIntro(call.DispatchCount);
			var spokenAddress = TrimAddressForSpeech(address);

			string prompt;
			if (!String.IsNullOrWhiteSpace(spokenAddress))
			{
				// "Address" for street-style values (leading house number), "Location" for
				// freeform values ("Building 5, Floor 3", "Bottom of Bucks Canyon").
				var placeLabel = char.IsDigit(spokenAddress.TrimStart()[0]) ? "Address" : "Location";
				prompt = $"{intro}, {call.Name}. Nature, {nature}. {placeLabel}, {spokenAddress}. Priority, {call.GetPriorityText()}";
			}
			else
			{
				prompt = $"{intro}, {call.Name}. Nature, {nature}. Priority, {call.GetPriorityText()}";
			}

			return prompt.EndsWith(".", StringComparison.Ordinal) || prompt.EndsWith("!", StringComparison.Ordinal) || prompt.EndsWith("?", StringComparison.Ordinal)
				? prompt
				: $"{prompt}.";
		}

		/// <summary>
		/// "New call" for a first dispatch, "Second alarm" / "Third alarm" / ... for
		/// re-dispatches. DispatchCount is 0 for departments whose dispatch path never
		/// increments it, and 1 after the first send — both mean a first dispatch.
		/// </summary>
		private static string BuildAlarmIntro(int dispatchCount)
		{
			if (dispatchCount <= 1)
				return "New call";

			return dispatchCount < AlarmOrdinals.Length
				? $"{AlarmOrdinals[dispatchCount]} alarm"
				: $"Alarm {dispatchCount}";
		}

		/// <summary>
		/// Drops trailing state, postal code and country segments from a comma-separated
		/// postal address so the spoken prompt stays short ("123 Main St, Springfield, WA
		/// 98111, USA" becomes "123 Main St, Springfield"). Trimming walks from the end
		/// and stops at the first segment that isn't a state/zip/country, so freeform
		/// locations pass through untouched. At least one segment is always kept.
		/// </summary>
		public static string TrimAddressForSpeech(string address)
		{
			if (String.IsNullOrWhiteSpace(address))
				return address;

			var segments = address.Split(',')
				.Select(segment => segment.Trim())
				.Where(segment => segment.Length > 0)
				.ToList();

			while (segments.Count > 1 && IsDroppableTrailingSegment(segments[^1]))
			{
				segments.RemoveAt(segments.Count - 1);
			}

			return string.Join(", ", segments);
		}

		private static bool IsDroppableTrailingSegment(string segment)
		{
			if (CountryNames.Contains(segment) || PostalCodeRegex.IsMatch(segment))
				return true;

			// "WA", "Washington", "WA 98111" or "Washington 98111".
			var lastSpaceIndex = segment.LastIndexOf(' ');
			var head = lastSpaceIndex > 0 ? segment[..lastSpaceIndex].Trim() : segment;
			var tail = lastSpaceIndex > 0 ? segment[(lastSpaceIndex + 1)..] : null;

			if (tail != null && !PostalCodeRegex.IsMatch(tail))
				return StateAbbreviations.Contains(segment) || StateNames.Contains(segment);

			return StateAbbreviations.Contains(head) || StateNames.Contains(head);
		}

		public static async Task<string> ResolveDispatchAddressAsync(Call call, IGeoLocationProvider geoLocationProvider, CancellationToken cancellationToken = default)
		{
			var address = call.Address;

			if (String.IsNullOrWhiteSpace(address) && !string.IsNullOrWhiteSpace(call.GeoLocationData) && call.GeoLocationData.Length > 1)
			{
				try
				{
					string[] points = call.GeoLocationData.Split(char.Parse(","));

					// Bound the reverse-geocode: it's an external HTTP call with no timeout of
					// its own, and the webhook caller runs inside a Twilio request whose total
					// budget is 15s. On timeout the catch swallows and the dispatch is spoken
					// without an address. TryParse with InvariantCulture: malformed coordinates
					// skip the lookup instead of throwing, and a comma-decimal server culture
					// can't silently misread "47.606" as 47606.
					if (points != null && points.Length == 2
						&& double.TryParse(points[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
						&& double.TryParse(points[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
						address = await geoLocationProvider.GetAproxAddressFromLatLong(latitude, longitude)
							.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
				}
				catch
				{
				}
			}

			return String.IsNullOrWhiteSpace(address) ? call.Address : address;
		}

		public static IEnumerable<string> ChunkText(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				yield break;

			var normalized = Regex.Replace(text, @"\s+", " ").Trim();
			var maxLength = TtsConfig.MaxTextLength > 0 ? TtsConfig.MaxTextLength : 1000;

			if (normalized.Length <= maxLength)
			{
				yield return normalized;
				yield break;
			}

			var sentences = Regex.Split(normalized, @"(?<=[\.\!\?])\s+")
				.Where(sentence => !string.IsNullOrWhiteSpace(sentence));
			var builder = new StringBuilder();

			foreach (var sentence in sentences)
			{
				var trimmed = sentence.Trim();

				if (trimmed.Length > maxLength)
				{
					foreach (var fragment in ChunkLongSentence(trimmed, maxLength))
					{
						if (builder.Length > 0)
						{
							yield return builder.ToString();
							builder.Clear();
						}

						yield return fragment;
					}

					continue;
				}

				if (builder.Length == 0)
				{
					builder.Append(trimmed);
					continue;
				}

				if (builder.Length + 1 + trimmed.Length <= maxLength)
				{
					builder.Append(' ').Append(trimmed);
					continue;
				}

				yield return builder.ToString();
				builder.Clear();
				builder.Append(trimmed);
			}

			if (builder.Length > 0)
			{
				yield return builder.ToString();
			}
		}

		private static IEnumerable<string> ChunkLongSentence(string sentence, int maxLength)
		{
			var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			var builder = new StringBuilder();

			foreach (var word in words)
			{
				if (word.Length > maxLength)
				{
					if (builder.Length > 0)
					{
						yield return builder.ToString();
						builder.Clear();
					}

					for (var index = 0; index < word.Length; index += maxLength)
					{
						yield return word.Substring(index, Math.Min(maxLength, word.Length - index));
					}

					continue;
				}

				if (builder.Length == 0)
				{
					builder.Append(word);
					continue;
				}

				if (builder.Length + 1 + word.Length <= maxLength)
				{
					builder.Append(' ').Append(word);
					continue;
				}

				yield return builder.ToString();
				builder.Clear();
				builder.Append(word);
			}

			if (builder.Length > 0)
			{
				yield return builder.ToString();
			}
		}
	}
}
