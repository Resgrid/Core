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
		public static string BuildDispatchPrompt(Call call, string address)
		{
			// Periods between the segments give the TTS engine sentence boundaries
			// (Piper inserts 0.35s of silence per sentence), which keeps the priority,
			// address and nature audibly separated instead of running together.
			var nature = StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall);
			var prompt = !String.IsNullOrWhiteSpace(address)
				? string.Format("{0}, Priority {1}. Address {2}. Nature {3}", call.Name, call.GetPriorityText(), address, nature)
				: string.Format("{0}, Priority {1}. Nature {2}", call.Name, call.GetPriorityText(), nature);

			return prompt.EndsWith(".", StringComparison.Ordinal) || prompt.EndsWith("!", StringComparison.Ordinal) || prompt.EndsWith("?", StringComparison.Ordinal)
				? prompt
				: $"{prompt}.";
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
