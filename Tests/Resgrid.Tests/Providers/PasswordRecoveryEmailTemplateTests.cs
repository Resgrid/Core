using System.Net;
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
	public class PasswordRecoveryEmailTemplateTests
	{
		private const string ResetUrl =
			"https://app.resgrid.test/Account/ResetPassword#token=abcdefghijklmnopqrstuvwxyz0123456789_-ABCD";
		private const string UserName = "bsmith";
		private const string ExpiresOn = "2026-09-02 16:12:00Z";

		private static async Task<Email> Render(bool isSsoManaged = false)
		{
			// Arrange
			Email sent = null;
			var senderMock = new Mock<IEmailSender>();
			senderMock
				.Setup(sender => sender.Send(It.IsAny<Email>()))
				.Callback<Email>(email => sent = email)
				.ReturnsAsync(true);
			var provider = new PostmarkTemplateProvider(senderMock.Object);

			// Act
			var result = await provider.SendPasswordRecoveryMail("Brandon", "brandon@example.com",
				"Example Fire", isSsoManaged ? null : ResetUrl, "192.0.2.1", "Test browser",
				"2026-09-02 14:12:00Z", isSsoManaged, isSsoManaged ? null : UserName,
				isSsoManaged ? null : "2 hours", isSsoManaged ? null : ExpiresOn);

			// Assert
			result.Should().BeTrue();
			sent.Should().NotBeNull();
			return sent;
		}

		[Test]
		public async Task SendPasswordRecoveryMail_WithResetLink_ProvidesUntrackedClickableFallbacks()
		{
			// Arrange
			const bool isSsoManaged = false;

			// Act
			var sent = await Render(isSsoManaged);
			var decodedHtml = WebUtility.HtmlDecode(sent.HtmlBody);

			// Assert
			Regex.Matches(decodedHtml, Regex.Escape($"href=\"{ResetUrl}\"")).Count
				.Should().Be(2, "both the button and fallback link must open the recovery page");
			Regex.Matches(sent.HtmlBody, "data-pm-no-track").Count
				.Should().Be(2, "tracking redirects can discard the recovery token stored in the URL fragment");
			sent.HtmlBody.Should().Contain("copy and paste this link",
				"a recipient needs a recovery path when their email client suppresses the button");
			sent.TextBody.Should().Contain(ResetUrl,
				"plain-text email clients must receive the complete recovery link");
		}

		[Test]
		public async Task SendPasswordRecoveryMail_ForSsoManagedAccount_DoesNotIncludeResetLink()
		{
			// Arrange
			const bool isSsoManaged = true;

			// Act
			var sent = await Render(isSsoManaged);

			// Assert
			sent.HtmlBody.Should().NotContain("Choose a new password");
			sent.HtmlBody.Should().NotContain("data-pm-no-track");
			sent.HtmlBody.Should().NotContain("expires in", "no link was sent, so nothing expires");
			sent.TextBody.Should().NotContain("Choose a new password");
			sent.TextBody.Should().Contain("linked via SSO");
		}

		[Test]
		public async Task SendPasswordRecoveryMail_WithResetLink_SaysHowLongTheLinkWorksAndNamesTheUsername()
		{
			// Act
			var sent = await Render();
			var html = WebUtility.HtmlDecode(sent.HtmlBody);

			// Assert
			html.Should().Contain("This link can only be used once and expires in 2 hours.",
				"a recipient who opens the email later must know whether the link still works");
			html.Should().Contain($"It stops working at {ExpiresOn} (UTC).");
			html.Should().Contain($"Your username is <strong>{UserName}</strong>",
				"the sign-in page asks for the username, which the person resetting may also have forgotten");
			sent.TextBody.Should().Contain("This link can only be used once and expires in 2 hours.");
			sent.TextBody.Should().Contain($"It stops working at {ExpiresOn} (UTC).");
			sent.TextBody.Should().Contain($"Your username is {UserName}.");
			html.Should().NotContain("{{", "every template field is filled");
		}

		[Test]
		public async Task SendPasswordRecoveryMail_WithoutExpiryDetails_StillSaysTheLinkIsShortLived()
		{
			// Arrange
			Email sent = null;
			var senderMock = new Mock<IEmailSender>();
			senderMock.Setup(sender => sender.Send(It.IsAny<Email>())).Callback<Email>(email => sent = email).ReturnsAsync(true);
			var provider = new PostmarkTemplateProvider(senderMock.Object);

			// Act
			(await provider.SendPasswordRecoveryMail("Brandon", "brandon@example.com", "Example Fire", ResetUrl, "192.0.2.1",
				"Test browser", "2026-09-02 14:12:00Z", false, null, null, null)).Should().BeTrue();

			// Assert
			sent.HtmlBody.Should().Contain("stops working after a short time");
			sent.HtmlBody.Should().NotContain("It stops working at");
			sent.HtmlBody.Should().NotContain("Your username is");
			sent.TextBody.Should().NotContain("Your username is");
		}

		[Test]
		public async Task SendPasswordRecoveryMail_WithoutRequiredResetLink_DoesNotSendUnusableEmail()
		{
			// Arrange
			var senderMock = new Mock<IEmailSender>();
			var provider = new PostmarkTemplateProvider(senderMock.Object);

			// Act
			var result = await provider.SendPasswordRecoveryMail("Brandon", "brandon@example.com",
				"Example Fire", null, "192.0.2.1", "Test browser", "2026-09-02 14:12:00Z", false, UserName, "2 hours", ExpiresOn);

			// Assert
			result.Should().BeFalse();
			senderMock.Verify(sender => sender.Send(It.IsAny<Email>()), Times.Never);
		}

		[Test]
		public async Task SendPasswordRecoveryMail_WithSpecialCharacters_EncodesTemplateValuesExactlyOnce()
		{
			// Arrange
			const string resetUrl =
				"https://app.resgrid.test/Account/ResetPassword?token=abc123&returnUrl=%2FUser%2FHome";
			Email sent = null;
			var senderMock = new Mock<IEmailSender>();
			senderMock
				.Setup(sender => sender.Send(It.IsAny<Email>()))
				.Callback<Email>(email => sent = email)
				.ReturnsAsync(true);
			var provider = new PostmarkTemplateProvider(senderMock.Object);

			// Act
			var result = await provider.SendPasswordRecoveryMail("Brandon & <Team>", "brandon@example.com",
				"Example & <Fire>", resetUrl, "192.0.2.1 & proxy", "Browser <Beta> & Co",
				"2026-09-02 14:12 UTC & verified", false, "b&o<n>", "2 hours", ExpiresOn);

			// Assert
			result.Should().BeTrue();
			sent.Should().NotBeNull();
			sent.HtmlBody.Should().Contain("Brandon &amp; &lt;Team&gt;");
			sent.HtmlBody.Should().Contain("Example &amp; &lt;Fire&gt;");
			sent.HtmlBody.Should().Contain("192.0.2.1 &amp; proxy");
			sent.HtmlBody.Should().Contain("Browser &lt;Beta&gt; &amp; Co");
			sent.HtmlBody.Should().Contain("2026-09-02 14:12 UTC &amp; verified");
			sent.HtmlBody.Should().Contain("b&amp;o&lt;n&gt;");
			sent.HtmlBody.Should().NotContain("&amp;amp;");
			WebUtility.HtmlDecode(sent.HtmlBody).Should().Contain($"href=\"{resetUrl}\"");
		}
	}
}
