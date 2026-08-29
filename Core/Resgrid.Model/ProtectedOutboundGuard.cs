using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// The queue-side twin of <see cref="ProtectedEgressScanner"/>: scrubs ADP envelopes out of
	/// text that is about to leave the platform through a carrier — an email body, an SMS, a push
	/// title.
	///
	/// Worker output does not pass through the HTTP response filter, and it is the one direction
	/// where a mistake is irreversible: an email is delivered, an SMS reaches a carrier, and no
	/// amount of later fixing recalls it. Every notification path is supposed to go through
	/// <c>IProtectedProjectionService</c> first, which produces a properly worded safe message;
	/// this only catches the paths that did not, and it deliberately never blocks the send.
	/// A dispatch that arrives degraded still tells a responder something is happening; a dispatch
	/// that never arrives could cost someone their life.
	/// </summary>
	public static class ProtectedOutboundGuard
	{
		/// <summary>
		/// Matches a whole text envelope. Kept deliberately tight — "rgdp:" alone would also match
		/// ordinary prose that happens to mention the format, and scrubbing a support email about
		/// encryption would be its own kind of bug.
		/// </summary>
		private static readonly Regex EnvelopePattern = new(
			@"rgdpb?:\d+:\d+:[A-Za-z0-9+/=_-]+",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>Cheap pre-check so the regex only runs on text that could possibly match.</summary>
		public static bool MightContainEnvelope(string text)
		{
			return !string.IsNullOrEmpty(text) &&
				   text.IndexOf(ProtectedDataEnvelope.Prefix, System.StringComparison.Ordinal) >= 0;
		}

		/// <summary>
		/// Replaces every envelope in <paramref name="text"/> with the placeholder.
		/// <paramref name="replaced"/> reports how many, so the caller can log that a surface
		/// skipped its projection rather than silently papering over it.
		/// </summary>
		public static string Scrub(string text, out int replaced)
		{
			replaced = 0;

			if (!MightContainEnvelope(text))
				return text;

			var count = 0;
			var scrubbed = EnvelopePattern.Replace(text, _ =>
			{
				count++;
				return ProtectedDataEnvelope.RedactionValue;
			});

			replaced = count;
			return scrubbed;
		}
	}
}
