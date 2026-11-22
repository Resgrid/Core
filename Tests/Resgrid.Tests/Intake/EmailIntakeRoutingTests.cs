using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Moq;
using NUnit.Framework;
using PostmarkDotNet;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Controllers;

namespace Resgrid.Tests.Intake
{
	/// <summary>
	/// Email intake: a recipient on the dispatch domain that is an Email intake channel's address becomes a pending call through
	/// intake (IntakeMessages is the idempotency claim; no InboundEmailReceipt, no dispatch lookup); any other address keeps the
	/// dispatch path.
	/// </summary>
	[TestFixture]
	public class EmailIntakeRoutingTests
	{
		private Mock<IIntakeService> _intake;
		private Mock<IInboundEmailReceiptsService> _receipts;
		private Mock<IDepartmentSettingsService> _settings;
		private Mock<IEmailSender> _sender;
		private EmailController _controller;
		private IntakeEnvelope _received;

		[SetUp]
		public void SetUp()
		{
			_received = null;
			_intake = new Mock<IIntakeService>();
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.Callback((IntakeEnvelope e, CancellationToken _) => _received = e)
				.ReturnsAsync((IntakeEnvelope e, CancellationToken _) => e.ChannelAddress == "hospital"
					? new IntakeResult { State = IntakeMessageStates.CallCreated, CallId = 900, Acknowledgement = "Received by Springfield Fire." }
					: new IntakeResult { State = IntakeMessageStates.Refused, Reason = "NoChannel" });
			_receipts = new Mock<IInboundEmailReceiptsService>();
			_settings = new Mock<IDepartmentSettingsService>();
			_sender = new Mock<IEmailSender>();
			_sender.Setup(s => s.SendEmail(It.IsAny<MailMessage>())).ReturnsAsync(true);

			_controller = new EmailController(_settings.Object, Mock.Of<INumbersService>(), Mock.Of<ILimitsService>(), Mock.Of<ICallsService>(), Mock.Of<IQueueService>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<IUserProfileService>(), Mock.Of<ITextCommandService>(), Mock.Of<IActionLogsService>(), Mock.Of<IUserStateService>(),
				Mock.Of<ICommunicationService>(), Mock.Of<IDistributionListsService>(), Mock.Of<IUsersService>(), Mock.Of<IEmailService>(), Mock.Of<IDepartmentGroupsService>(),
				Mock.Of<IMessageService>(), Mock.Of<IFileService>(), Mock.Of<IUnitsService>(), Mock.Of<IGeoLocationProvider>(), Mock.Of<ICallDispatchStatusService>(),
				Mock.Of<IDispatchRecommendationService>(), Mock.Of<IFeatureToggleService>(), Mock.Of<IAiDispatchAdminService>(), _receipts.Object, _intake.Object, _sender.Object, Mock.Of<IDispatcherEmailCallService>());
		}

		private static PostmarkInboundMessage Email(string localPart, string from = "nurse@mercy.example", List<PostmarkDotNet.Header> headers = null) => new PostmarkInboundMessage
		{
			MessageID = "pm-1",
			Subject = "Transfer request",
			TextBody = "Patient in room 4 needs transport to St. Luke's.",
			FromFull = new FromFull { Email = from, Name = "Dana Nurse" },
			ToFull = new List<ToFull> { new ToFull { Email = localPart + "@" + Resgrid.Config.InboundEmailConfig.DispatchDomain, Name = "Intake" } },
			Attachments = new List<PostmarkDotNet.Attachment>(),
			Headers = headers ?? new List<PostmarkDotNet.Header>()
		};

		private static int? Status(ActionResult result) => (result as IStatusCodeActionResult)?.StatusCode;

		[Test]
		public async Task an_intake_address_creates_a_pending_call_without_the_dispatch_path()
		{
			var result = await _controller.Receive(Email("hospital"), CancellationToken.None);

			Status(result).Should().Be(200);
			_received.ChannelType.Should().Be(IntakeChannelTypes.Email);
			_received.ChannelAddress.Should().Be("hospital");
			_received.ProviderMessageId.Should().Be("pm-1");
			_received.Sender.Should().Be("nurse@mercy.example");
			_received.SenderName.Should().Be("Dana Nurse");
			_received.Subject.Should().Be("Transfer request");
			_received.Body.Should().Contain("room 4");
			_receipts.Verify(r => r.ClaimAsync(It.IsAny<string>(), It.IsAny<InboundEmailKinds>(), It.IsAny<CancellationToken>()), Times.Never);
			_settings.Verify(s => s.GetDepartmentIdForDispatchEmailAsync(It.IsAny<string>()), Times.Never);
		}

		[Test]
		public async Task the_acknowledgement_is_an_auto_reply_from_do_not_reply()
		{
			MailMessage sent = null;
			_sender.Setup(s => s.SendEmail(It.IsAny<MailMessage>())).Callback((MailMessage m) => sent = new MailMessage
			{
				From = m.From, Subject = m.Subject, Body = m.Body
			}.Also(copy => { copy.To.Add(m.To[0]); copy.Headers.Add("Auto-Submitted", m.Headers["Auto-Submitted"]); })).ReturnsAsync(true);

			await _controller.Receive(Email("hospital"), CancellationToken.None);

			sent.Should().NotBeNull();
			sent.To[0].Address.Should().Be("nurse@mercy.example");
			sent.From.Address.Should().Be("do-not-reply@resgrid.com");
			sent.Headers["Auto-Submitted"].Should().Be("auto-replied");
			sent.Body.Should().Be("Received by Springfield Fire.");
			// The incoming subject ("Transfer request", which may name a patient or an incident) is never repeated back.
			sent.Subject.Should().Be(EmailController.IntakeAcknowledgementSubject);
			sent.Subject.Should().NotContain("Transfer");
		}

		[Test]
		public async Task a_redelivery_while_the_first_is_still_being_processed_is_conflict_so_postmark_retries()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, Reason = IntakeResultReasons.DuplicateInProgress });

			Status(await _controller.Receive(Email("hospital"), CancellationToken.None)).Should().Be(409);
			_sender.Verify(s => s.SendEmail(It.IsAny<MailMessage>()), Times.Never);
		}

		[Test]
		public async Task a_redelivery_of_a_finished_email_is_acknowledged()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Duplicate, CallId = 900, Reason = IntakeResultReasons.Duplicate });

			Status(await _controller.Receive(Email("hospital"), CancellationToken.None)).Should().Be(200);
		}

		[Test]
		public async Task automated_senders_and_auto_replies_get_no_acknowledgement()
		{
			await _controller.Receive(Email("hospital", from: "no-reply@cad.example"), CancellationToken.None);
			await _controller.Receive(Email("hospital", headers: new List<PostmarkDotNet.Header> { new PostmarkDotNet.Header { Name = "Auto-Submitted", Value = "auto-replied" } }), CancellationToken.None);

			_sender.Verify(s => s.SendEmail(It.IsAny<MailMessage>()), Times.Never);
		}

		[Test]
		public async Task any_other_address_continues_to_the_dispatch_path()
		{
			var result = await _controller.Receive(Email("springfieldfire"), CancellationToken.None);

			_settings.Verify(s => s.GetDepartmentIdForDispatchEmailAsync("springfieldfire"), Times.Once);
			Status(result).Should().NotBe(200, "no department dispatches from that address in this fixture");
		}

		[Test]
		public async Task a_failed_intake_asks_postmark_to_redeliver()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.Failed });

			Status(await _controller.Receive(Email("hospital"), CancellationToken.None)).Should().Be(400);
		}

		[Test]
		public async Task a_duplicate_or_refused_intake_email_is_acknowledged_to_postmark()
		{
			_intake.Setup(i => i.ReceiveAsync(It.IsAny<IntakeEnvelope>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new IntakeResult { State = IntakeMessageStates.SenderNotAllowed });

			Status(await _controller.Receive(Email("hospital"), CancellationToken.None)).Should().Be(200);
			_sender.Verify(s => s.SendEmail(It.IsAny<MailMessage>()), Times.Never);
		}
	}

	internal static class MailMessageTestExtensions
	{
		public static MailMessage Also(this MailMessage message, System.Action<MailMessage> action)
		{
			action(message);
			return message;
		}
	}
}
