using System;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.EmailProvider
{
	/// <summary>
	/// Outbound-boundary net for email (ADP plan section 7.5, queue side). Every message leaving
	/// the platform passes through <see cref="IEmailSender"/>, so wrapping it covers every sender —
	/// dispatch, notifications, reports, exports, workers — without a per-caller list.
	///
	/// Notification paths are supposed to call <c>IProtectedProjectionService</c> first, which
	/// produces a properly worded safe message. This catches the ones that did not: it scrubs the
	/// ciphertext, drops an enveloped attachment, and logs enough to find the missed path. It never
	/// blocks the send — a degraded dispatch email is bad, an undelivered one is dangerous.
	/// </summary>
	public class ProtectedEmailSenderDecorator : IEmailSender
	{
		private readonly IEmailSender _inner;

		public ProtectedEmailSenderDecorator(IEmailSender inner)
		{
			_inner = inner;
		}

		public async Task<bool> SendEmail(MailMessage email)
		{
			try
			{
				Sanitize(email);
			}
			catch (Exception ex)
			{
				// The net must never be the reason a message fails to send.
				Logging.LogException(ex, "ProtectedEmailSenderDecorator failed while sanitizing an outbound email");
			}

			return await _inner.SendEmail(email);
		}

		public async Task<bool> Send(Email email)
		{
			try
			{
				Sanitize(email);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "ProtectedEmailSenderDecorator failed while sanitizing an outbound email");
			}

			return await _inner.Send(email);
		}

		/// <summary>
		/// Composition only — the message it builds is sanitized when it is actually sent, so
		/// scrubbing here as well would only double the work.
		/// </summary>
		public MailMessage CreateMailMessageFromEmail(Email email) => _inner.CreateMailMessageFromEmail(email);

		private static void Sanitize(MailMessage email)
		{
			if (email == null)
				return;

			var scrubbed = 0;

			email.Subject = ProtectedOutboundGuard.Scrub(email.Subject, out var subjectCount);
			scrubbed += subjectCount;

			email.Body = ProtectedOutboundGuard.Scrub(email.Body, out var bodyCount);
			scrubbed += bodyCount;

			// An attachment whose bytes are still an envelope is unreadable to the recipient and is
			// raw ciphertext sitting in their mailbox. There is nothing to redact inside a file.
			var enveloped = email.Attachments
				.Where(a => a?.ContentStream != null && StartsWithBinaryEnvelope(a.ContentStream))
				.ToList();

			foreach (var attachment in enveloped)
			{
				email.Attachments.Remove(attachment);
				attachment.Dispose();
			}

			Report(scrubbed, enveloped.Count, email.To?.Count ?? 0);
		}

		private static void Sanitize(Email email)
		{
			if (email == null)
				return;

			var scrubbed = 0;

			email.Subject = ProtectedOutboundGuard.Scrub(email.Subject, out var subjectCount);
			scrubbed += subjectCount;

			email.HtmlBody = ProtectedOutboundGuard.Scrub(email.HtmlBody, out var htmlCount);
			scrubbed += htmlCount;

			email.TextBody = ProtectedOutboundGuard.Scrub(email.TextBody, out var textCount);
			scrubbed += textCount;

			Report(scrubbed, 0, email.To?.Count ?? 0);
		}

		private static void Report(int scrubbed, int droppedAttachments, int recipients)
		{
			if (scrubbed == 0 && droppedAttachments == 0)
				return;

			// Recipients and content stay out of the log; that a send path skipped its safe
			// projection is the finding, and the counts are enough to locate it.
			Logging.LogError($"ADP outbound net scrubbed an email before sending: {scrubbed} enveloped value(s) in subject/body, " +
				$"{droppedAttachments} enveloped attachment(s) dropped, {recipients} recipient(s). " +
				"A notification path is missing its protected projection.");
		}

		private static bool StartsWithBinaryEnvelope(System.IO.Stream stream)
		{
			if (!stream.CanSeek || !stream.CanRead)
				return false;

			var prefix = System.Text.Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix);
			var buffer = new byte[prefix.Length];

			var position = stream.Position;
			try
			{
				stream.Position = 0;
				var read = stream.Read(buffer, 0, buffer.Length);
				if (read < buffer.Length)
					return false;

				for (var i = 0; i < prefix.Length; i++)
				{
					if (buffer[i] != prefix[i])
						return false;
				}

				return true;
			}
			finally
			{
				stream.Position = position;
			}
		}
	}
}
