using System.Collections.Generic;

namespace Resgrid.Model
{
	public class CallEmail
	{
		public string MessageId { get; set; }
		/// <summary>Sender address when the inbound path knows it; used for the AI dispatch sender allowlist.</summary>
		public string From { get; set; }
		public string Subject { get; set; }
		public string Body { get; set; }
		public string TextBody { get; set; }
		public string DispatchAudioFileName { get; set; }
		public byte[] DispatchAudio { get; set; }

		/// <summary>
		/// The email's Date header (UTC) when it had a readable one. Orders CAD updates that arrive out of order
		/// (cad-email-call-updates-plan.md D9): an email older than one already applied to the call only adds a note.
		/// </summary>
		public System.DateTime? SentOn { get; set; }

		/// <summary>
		/// Reads an RFC 5322 Date header (Postmark passes it as text) into UTC; null when it is missing or unreadable.
		/// </summary>
		public static System.DateTime? ParseSentOn(string dateHeader)
		{
			if (string.IsNullOrWhiteSpace(dateHeader))
				return null;

			if (MimeKit.Utils.DateUtils.TryParse(dateHeader.Trim(), out System.DateTimeOffset parsed))
				return parsed.UtcDateTime;

			if (System.DateTimeOffset.TryParse(dateHeader.Trim(), System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.AssumeUniversal, out parsed))
				return parsed.UtcDateTime;

			return null;
		}
	}
}