using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Providers.EmailProvider;

namespace Resgrid.Tests.Providers
{
	[TestFixture]
	public class TransactionalEmailTemplateTests
	{
		private const string CallUrlPath = "/User/Dispatch/CallExportEx?query=";
		private const string LoginUrlPath = "/Account/LogOn";
		private const string AudioUrl = "https://audio.resgrid.test/dispatch/42";

		private static PostmarkTemplateProvider CreateProvider(ICollection<Email> sentEmails)
		{
			var senderMock = new Mock<IEmailSender>();
			senderMock
				.Setup(sender => sender.Send(It.IsAny<Email>()))
				.Callback<Email>(sentEmails.Add)
				.ReturnsAsync(true);

			return new PostmarkTemplateProvider(senderMock.Object);
		}

		private static CommunicationTestEmailContent CommunicationTestContent()
		{
			return new CommunicationTestEmailContent
			{
				Subject = "Communication test",
				Preheader = "Confirm this communication test",
				Greeting = "Hello",
				Intro = "This is a communication test.",
				Disclaimer = "This is not an emergency.",
				DepartmentLabel = "Department:",
				DepartmentName = "Example Fire",
				TestLabel = "Test:",
				TestName = "September test",
				Action = "Please confirm receipt.",
				ButtonText = "Confirm receipt",
				ConfirmUrl = "https://app.resgrid.test/confirm/42",
				TroubleText = "If the button does not work, copy the URL.",
				Signoff = "Thanks,",
				TeamName = "The Resgrid Team",
				TextBody = "Communication test\nhttps://app.resgrid.test/confirm/42"
			};
		}

		[Test]
		public async Task SendCallMail_WithAudio_RendersClickableAudioLinkAndFallback()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendCallMail("member@example.com", "New call", "Structure fire",
				"High", "Reported structure fire", "12-A", "100 Main St", "2026-09-02 07:12",
				42, "user-1", "38.9399,-119.9772", AudioUrl, null);

			// Assert
			result.Should().BeTrue();
			var html = sentEmails.Single().HtmlBody;
			html.Should().Contain($"href=\"{AudioUrl}\"",
				"the optional audio action must remain clickable inside its template section");
			Regex.Matches(html, Regex.Escape(AudioUrl)).Count.Should().BeGreaterThanOrEqualTo(2,
				"the audio URL must also be available when an email client suppresses the button");
		}

		[Test]
		public async Task SendCallMail_WithoutAudio_OmitsAudioActionAndFallback()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendCallMail("member@example.com", "New call", "Structure fire",
				"High", "Reported structure fire", "12-A", "100 Main St", "2026-09-02 07:12",
				42, "user-1", "38.9399,-119.9772", null, null);

			// Assert
			result.Should().BeTrue();
			var html = sentEmails.Single().HtmlBody;
			html.Should().NotContain("Listen To Dispatch Audio");
			html.Should().NotContain("or audio",
				"an email without dispatch audio should not show an incomplete fallback instruction");
		}

		[Test]
		public async Task SendTroubleAlertMail_WithDetails_RendersCompleteAlert()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendTroubleAlertMail("member@example.com", "Engine 1",
				"38.9399,-119.9772", "Alex Smith, Jamie Lee", "100 Main St", "101 Main St",
				"2026-09-02 07:12", "Structure fire");

			// Assert
			result.Should().BeTrue();
			var html = sentEmails.Single().HtmlBody;
			html.Should().Contain("A trouble alert was issued for Engine 1 on 2026-09-02 07:12");
			html.Should().Contain("Alex Smith, Jamie Lee");
		}

		[Test]
		public async Task ImplementedTemplates_WithCompleteModels_UseStandardResgridChrome()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			await provider.SendDeleteDepartmentEmail("Requester", "Example Fire", new DateTime(2026, 9, 5),
				"Recipient", "member@example.com");
			await provider.SendCallMail("member@example.com", "New call", "Structure fire", "High",
				"Reported structure fire", "12-A", "100 Main St", "2026-09-02 07:12", 42, "user-1",
				"38.9399,-119.9772", AudioUrl, null);
			await provider.SendTroubleAlertMail("member@example.com", "Engine 1", "38.9399,-119.9772",
				"Alex Smith", "100 Main St", "101 Main St", "2026-09-02 07:12", "Structure fire");
			await provider.SendCancellationReciept("Recipient", "member@example.com", "2026-09-30",
				"Example Fire");
			await provider.SendChargeFailed("Recipient", "member@example.com", "2026-09-30", "Example Fire",
				"Standard");
			await provider.SendInviteMail("invite-code", "Example Fire", "member@example.com", "Chief Smith",
				"chief@example.com");
			await provider.SendMessageMail("member@example.com", "Resgrid message from Chief Smith",
				"September drill", "Please review the drill plan.", "chief@example.com", "Chief Smith",
				"2026-09-02 07:12 UTC", 42, null);
			await provider.SendPasswordRecoveryMail("Recipient", "member@example.com", "Example Fire",
				"https://app.resgrid.test/reset#token", "192.0.2.1", "Test browser", "2026-09-02 07:12 UTC",
				false);
			await provider.SendPasswordChangedByAdministratorMail("Recipient", "recipient",
				"member@example.com", "Example Fire");
			await provider.SendPaymentReciept("Example Fire", "Recipient", "2026-09-02", "$20.00",
				"member@example.com", "Card", "txn-42", "Standard", "September 1 to September 30",
				"2026-10-01", 42);
			await provider.SendWelcomeMail("Recipient", "Example Fire", "recipient", "member@example.com", 42);
			await provider.SendNewDepartmentLinkMail("Recipient", "Example Fire", "Calls",
				"member@example.com", 42);
			await provider.SendReportDeliveryMail("member@example.com", "Scheduled report",
				"Your report is attached.", "2026-09-02 07:12 UTC", "Weekly activity", "weekly.pdf",
				new byte[] { 1, 2, 3 }, "https://app.resgrid.test/report/42", null);
			await provider.SendCommunicationTestMail("member@example.com", CommunicationTestContent());

			// Assert
			sentEmails.Should().HaveCount(14);
			foreach (var email in sentEmails)
			{
				email.HtmlBody.Should().Contain("<html lang=\"en\"",
					$"'{email.Subject}' should identify its language for assistive technology");
				email.HtmlBody.Should().Contain("class=\"preheader\"",
					$"'{email.Subject}' should provide consistent inbox preview text");
				email.HtmlBody.Should().Contain("class=\"email-masthead_name\"",
					$"'{email.Subject}' should use the standard Resgrid masthead");
				email.HtmlBody.Should().Contain("Resgrid, LLC. All rights reserved.",
					$"'{email.Subject}' should use the standard Resgrid footer");
				email.HtmlBody.Should().NotContain("https://example.com");
				email.HtmlBody.Should().NotContain("href=\"\"",
					$"'{email.Subject}' should not contain an unusable link");
				email.HtmlBody.Should().NotContain("{{",
					$"'{email.Subject}' should not expose an unresolved template field");

				foreach (System.Text.RegularExpressions.Match link in Regex.Matches(email.HtmlBody, "href=\\\"(?<url>[^\\\"]+)\\\""))
				{
					var url = System.Net.WebUtility.HtmlDecode(link.Groups["url"].Value);
					Uri.TryCreate(url, UriKind.Absolute, out _).Should().BeTrue(
						$"'{email.Subject}' should not contain the malformed link '{url}'");
				}
			}
		}

		[Test]
		public async Task PrimaryActionEmails_WithCompleteModels_ProvidePasteableUrls()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			await provider.SendCallMail("member@example.com", "New call", "Structure fire", "High",
				"Reported structure fire", "12-A", "100 Main St", "2026-09-02 07:12", 42, "user-1",
				"38.9399,-119.9772", null, null);
			await provider.SendCancellationReciept("Recipient", "member@example.com", "2026-09-30",
				"Example Fire");
			await provider.SendChargeFailed("Recipient", "member@example.com", "2026-09-30", "Example Fire",
				"Standard");
			await provider.SendInviteMail("invite-code", "Example Fire", "member@example.com", "Chief Smith",
				"chief@example.com");
			await provider.SendMessageMail("member@example.com", "Resgrid message from Chief Smith",
				"September drill", "Please review the drill plan.", "chief@example.com", "Chief Smith",
				"2026-09-02 07:12 UTC", 42, null);
			await provider.SendPasswordChangedByAdministratorMail("Recipient", "recipient",
				"member@example.com", "Example Fire");
			await provider.SendPaymentReciept("Example Fire", "Recipient", "2026-09-02", "$20.00",
				"member@example.com", "Card", "txn-42", "Standard", "September 1 to September 30",
				"2026-10-01", 42);
			await provider.SendWelcomeMail("Recipient", "Example Fire", "recipient", "member@example.com", 42);
			await provider.SendNewDepartmentLinkMail("Recipient", "Example Fire", "Calls",
				"member@example.com", 42);

			// Assert
			foreach (var email in sentEmails)
			{
				email.HtmlBody.Should().Contain("copy and paste",
					$"'{email.Subject}' needs a usable path when a client suppresses its button");
			}

			var callEmail = sentEmails.Single(email => email.Subject.StartsWith("New Call:"));
			callEmail.HtmlBody.Should().Contain(CallUrlPath);
			var welcomeEmail = sentEmails.Single(email => email.Subject.StartsWith("Welcome,"));
			welcomeEmail.HtmlBody.Should().Contain($"<p class=\"sub\">{Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl}{LoginUrlPath}</p>");
		}

		[Test]
		public async Task SendPaymentReciept_WithPaymentId_BuildsValidInvoiceUrl()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendPaymentReciept("Example Fire", "Recipient", "2026-09-02",
				"$20.00", "member@example.com", "Card", "txn-42", "Standard",
				"September 1 to September 30", "2026-10-01", 42);

			// Assert
			result.Should().BeTrue();
			var expectedUrl = $"{Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl}/User/Subscription/ViewInvoice?paymentId=42";
			sentEmails.Single().HtmlBody.Should().Contain($"href=\"{expectedUrl}\"");
		}

		[Test]
		public async Task SendReportDeliveryMail_WithoutLiveUrl_OmitsEmptyLinkAndStillSendsAttachment()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendReportDeliveryMail("member@example.com", "Scheduled report",
				"Your report is attached.", "2026-09-02 07:12 UTC", "Weekly activity", "weekly.pdf",
				new byte[] { 1, 2, 3 }, null, null);

			// Assert
			result.Should().BeTrue();
			var email = sentEmails.Single();
			email.HtmlBody.Should().Contain("weekly.pdf");
			email.HtmlBody.Should().NotContain("View live report");
			email.HtmlBody.Should().NotContain("href=\"\"");
		}

		[Test]
		public async Task MessageAndReportBodies_WithPlainTextLineBreaks_PreserveWhitespace()
		{
			// Arrange
			const string body = "<p>First line</p><p>  Indented second line</p>";
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			await provider.SendMessageMail("member@example.com", "New message", "Drill details", body,
				"chief@example.com", "Chief Smith", "2026-09-02 07:12 UTC", 42, null);
			await provider.SendReportDeliveryMail("member@example.com", "Scheduled report", body,
				"2026-09-02 07:12 UTC", "Weekly activity", "weekly.pdf", new byte[] { 1, 2, 3 }, null, null);

			// Assert
			sentEmails.Should().HaveCount(2);
			foreach (var email in sentEmails)
			{
				email.HtmlBody.Should().Contain("<p style=\"white-space: pre-wrap;\">",
					$"'{email.Subject}' should display the converted plain-text whitespace and line breaks");
				email.HtmlBody.Should().Contain("\r\nFirst line\r\n  Indented second line");
			}
		}

		[TestCase(null)]
		[TestCase("")]
		public async Task SendInviteMail_WithoutInviteCode_DoesNotSendUnusableAction(string inviteCode)
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);

			// Act
			var result = await provider.SendInviteMail(inviteCode, "Example Fire", "member@example.com",
				"Chief Smith", "chief@example.com");

			// Assert
			result.Should().BeFalse();
			sentEmails.Should().BeEmpty();
		}

		[Test]
		public async Task SendCommunicationTestMail_WithoutConfirmUrl_DoesNotSendUnusableAction()
		{
			// Arrange
			var sentEmails = new List<Email>();
			var provider = CreateProvider(sentEmails);
			var content = CommunicationTestContent();
			content.ConfirmUrl = null;

			// Act
			var result = await provider.SendCommunicationTestMail("member@example.com", content);

			// Assert
			result.Should().BeFalse();
			sentEmails.Should().BeEmpty();
		}
	}
}
