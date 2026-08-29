using System.Net.Mail;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.EmailProvider;
using Resgrid.Providers.NumberProvider;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The queue-side outbound net (ADP plan 7.5). Worker output never passes through the HTTP
	/// response filter, and it is the one direction that cannot be taken back: an email is
	/// delivered, an SMS reaches a carrier, a push lands on a lock screen. These pin that an
	/// envelope which slipped past its projection is scrubbed on the way out, and that the send is
	/// never blocked — a degraded dispatch beats a missing one.
	/// </summary>
	[TestFixture]
	public class ProtectedOutboundGuardTests : TestBase
	{
		private const string Envelope = "rgdp:1:2:c29tZS1jaXBoZXJ0ZXh0";

		[Test]
		public void An_envelope_in_text_is_replaced_with_the_placeholder()
		{
			var scrubbed = ProtectedOutboundGuard.Scrub($"Dispatch: {Envelope} at {Envelope}", out var count);

			count.Should().Be(2);
			scrubbed.Should().NotContain("rgdp:");
			scrubbed.Should().Be($"Dispatch: {ProtectedDataEnvelope.RedactionValue} at {ProtectedDataEnvelope.RedactionValue}");
		}

		[Test]
		public void Ordinary_text_is_left_exactly_as_it_was()
		{
			// Matching loosely on "rgdp:" would scrub a support email that merely discusses the
			// envelope format, which would be its own kind of bug.
			const string message = "Engine 1 responding to 26-45, staging at the corner.";

			ProtectedOutboundGuard.Scrub(message, out var count).Should().Be(message);
			count.Should().Be(0);

			ProtectedOutboundGuard.Scrub("The rgdp: prefix marks a protected value.", out var prose)
				.Should().Be("The rgdp: prefix marks a protected value.");
			prose.Should().Be(0);
		}

		[Test]
		public void Null_and_empty_text_pass_through()
		{
			ProtectedOutboundGuard.Scrub(null, out var nullCount).Should().BeNull();
			nullCount.Should().Be(0);

			ProtectedOutboundGuard.Scrub(string.Empty, out var emptyCount).Should().BeEmpty();
			emptyCount.Should().Be(0);
		}

		[Test]
		public void A_push_title_and_subtitle_are_scrubbed_but_the_push_still_goes()
		{
			var inner = new RecordingPushService();
			var decorator = new ProtectedPushServiceDecorator(inner);
			var message = new Resgrid.Model.Messages.StandardPushMessage
			{
				Title = Envelope,
				SubTitle = $"Nature: {Envelope}",
				DepartmentId = 7
			};

			decorator.PushNotification(message, "user-1").Wait();

			inner.Sent.Should().Be(1, "the notification must still reach the responder");
			message.Title.Should().Be(ProtectedDataEnvelope.RedactionValue);
			message.SubTitle.Should().Be($"Nature: {ProtectedDataEnvelope.RedactionValue}");
		}

		[Test]
		public void An_email_subject_and_body_are_scrubbed_but_the_email_still_sends()
		{
			var inner = new RecordingEmailSender();
			var decorator = new ProtectedEmailSenderDecorator(inner);
			var mail = new MailMessage
			{
				Subject = $"Call {Envelope}",
				Body = $"Nature of call: {Envelope}"
			};
			mail.To.Add("someone@example.org");

			decorator.SendEmail(mail).Wait();

			inner.Sent.Should().Be(1);
			mail.Subject.Should().NotContain("rgdp:");
			mail.Body.Should().NotContain("rgdp:");
		}

		[Test]
		public void An_enveloped_attachment_is_dropped_rather_than_delivered_as_ciphertext()
		{
			var inner = new RecordingEmailSender();
			var decorator = new ProtectedEmailSenderDecorator(inner);
			var mail = new MailMessage { Subject = "Report", Body = "Attached." };
			mail.To.Add("someone@example.org");

			var payload = System.Text.Encoding.ASCII.GetBytes(ProtectedDataEnvelope.BinaryPrefix + "1:2:xyz");
			mail.Attachments.Add(new Attachment(new System.IO.MemoryStream(payload), "report.pdf"));

			decorator.SendEmail(mail).Wait();

			mail.Attachments.Count.Should().Be(0, "there is nothing to redact inside a file");
			inner.Sent.Should().Be(1, "the message itself still goes");
		}

		[Test]
		public void A_plain_attachment_is_left_alone()
		{
			var inner = new RecordingEmailSender();
			var decorator = new ProtectedEmailSenderDecorator(inner);
			var mail = new MailMessage { Subject = "Report", Body = "Attached." };
			mail.To.Add("someone@example.org");
			mail.Attachments.Add(new Attachment(
				new System.IO.MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 real report")), "report.pdf"));

			decorator.SendEmail(mail).Wait();

			mail.Attachments.Count.Should().Be(1);
		}

		[Test]
		public void Every_outbound_channel_is_wrapped_in_the_container()
		{
			// The net is only a net if it is actually registered. This also proves the decorated
			// graphs still resolve — the voice decorator depends on the projection service.
			Resolve<IEmailSender>().Should().BeOfType<ProtectedEmailSenderDecorator>();
			Resolve<ITextMessageProvider>().Should().BeOfType<ProtectedTextMessageProviderDecorator>();
			Resolve<IOutboundVoiceProvider>().Should().BeOfType<ProtectedOutboundVoiceProviderDecorator>();
			Resolve<IPushService>().Should().BeOfType<ProtectedPushServiceDecorator>();
		}

		private sealed class RecordingEmailSender : IEmailSender
		{
			public int Sent { get; private set; }

			public System.Threading.Tasks.Task<bool> SendEmail(MailMessage email)
			{
				Sent++;
				return System.Threading.Tasks.Task.FromResult(true);
			}

			public System.Threading.Tasks.Task<bool> Send(Email email)
			{
				Sent++;
				return System.Threading.Tasks.Task.FromResult(true);
			}

			public MailMessage CreateMailMessageFromEmail(Email email) => new MailMessage();
		}

		private sealed class RecordingPushService : IPushService
		{
			public int Sent { get; private set; }

			private System.Threading.Tasks.Task<bool> Record()
			{
				Sent++;
				return System.Threading.Tasks.Task.FromResult(true);
			}

			public System.Threading.Tasks.Task<bool> PushMessage(Resgrid.Model.Messages.StandardPushMessage message, string userId, UserProfile profile = null) => Record();
			public System.Threading.Tasks.Task<bool> PushCall(Resgrid.Model.Messages.StandardPushCall call, string userId, UserProfile profile = null, DepartmentCallPriority priority = null) => Record();
			public System.Threading.Tasks.Task<bool> Register(PushUri pushUri) => Record();
			public System.Threading.Tasks.Task<bool> UnRegister(PushUri pushUri) => Record();
			public void UnRegisterNotificationOnly(PushUri pushUri) { }
			public System.Threading.Tasks.Task<bool> PushNotification(Resgrid.Model.Messages.StandardPushMessage message, string userId, UserProfile profile = null) => Record();
			public System.Threading.Tasks.Task<bool> PushICNotification(Resgrid.Model.Messages.StandardPushMessage message, string userId, UserProfile profile = null) => Record();
			public System.Threading.Tasks.Task<bool> RegisterUnit(PushUri pushUri) => Record();
			public System.Threading.Tasks.Task<bool> UnRegisterUnit(PushUri pushUri) => Record();
			public System.Threading.Tasks.Task<bool> PushChat(Resgrid.Model.Messages.StandardPushMessage message, string userId, UserProfile profile = null) => Record();
			public System.Threading.Tasks.Task<bool> PushCallUnit(Resgrid.Model.Messages.StandardPushCall call, int unitId, DepartmentCallPriority priority = null) => Record();
			public System.Threading.Tasks.Task<bool> PushChatMessage(Resgrid.Model.Messages.StandardPushMessage message, string userId, string eventCode, int unreadCount, bool includeIncidentCommandApp, UserProfile profile = null) => Record();
			public System.Threading.Tasks.Task<bool> PushChatMessageUnit(Resgrid.Model.Messages.StandardPushMessage message, int unitId, string eventCode, int unreadCount) => Record();
		}
	}
}
