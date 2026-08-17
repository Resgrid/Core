using System;
using System.Collections.Generic;
using System.IO;
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
	/// <summary>
	/// A communication test proves a department can reach its people, and the proof is the recipient
	/// clicking through. An email that arrives as a wall of plain text with a bare URL is a worse
	/// proof than one that looks like every other Resgrid email and carries a button, so these pin
	/// that the test email goes out through the shared HTML template with the confirm link wired to
	/// a real anchor.
	/// </summary>
	[TestFixture]
	public class CommunicationTestEmailTemplateTests
	{
		/// <summary>Every placeholder the template is allowed to contain, mirroring the model the provider builds.</summary>
		private static readonly string[] ExpectedPlaceholders =
		{
			"preheader", "greeting", "intro", "disclaimer", "department_label", "department_name",
			"test_label", "test_name", "action", "button_text", "confirm_url", "trouble_text",
			"signoff", "team_name"
		};

		private static CommunicationTestEmailContent SampleContent()
		{
			// Distinct sentinels rather than realistic copy: a field the template forgets to place
			// renders as nothing, and a sentinel makes that omission visible.
			return new CommunicationTestEmailContent
			{
				Subject = "SUBJECT-SENTINEL",
				Preheader = "PREHEADER-SENTINEL",
				Greeting = "GREETING-SENTINEL",
				Intro = "INTRO-SENTINEL",
				Disclaimer = "DISCLAIMER-SENTINEL",
				Action = "ACTION-SENTINEL",
				ButtonText = "BUTTON-SENTINEL",
				ConfirmUrl = "https://confirm/link",
				TroubleText = "TROUBLE-SENTINEL",
				Signoff = "SIGNOFF-SENTINEL",
				TeamName = "TEAM-SENTINEL",
				DepartmentLabel = "DEPARTMENTLABEL-SENTINEL",
				DepartmentName = "DEPARTMENTNAME-SENTINEL",
				TestLabel = "TESTLABEL-SENTINEL",
				TestName = "TESTNAME-SENTINEL"
			};
		}

		private static string ReadTemplate()
		{
			var assembly = typeof(PostmarkTemplateProvider).Assembly;
			using (var resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Template.CommunicationTest.html"))
			{
				resource.Should().NotBeNull("the communication test template should be embedded in the email provider assembly");

				using (var reader = new StreamReader(resource))
					return reader.ReadToEnd();
			}
		}

		/// <summary>Sends through the provider with a fake sender and hands back the email it would have transmitted.</summary>
		private static async Task<Email> Render(CommunicationTestEmailContent content)
		{
			Email sent = null;

			var senderMock = new Mock<IEmailSender>();
			senderMock
				.Setup(x => x.Send(It.IsAny<Email>()))
				.Callback<Email>(x => sent = x)
				.ReturnsAsync(true);

			var provider = new PostmarkTemplateProvider(senderMock.Object);

			var result = await provider.SendCommunicationTestMail("member@example.com", content);

			result.Should().BeTrue("the template should render and hand off to the sender");
			sent.Should().NotBeNull();

			return sent;
		}

		[Test]
		public async Task the_email_should_be_sent_as_html_using_the_shared_resgrid_template()
		{
			var sent = await Render(SampleContent());

			sent.To.Should().Contain("member@example.com");
			sent.Subject.Should().Be("SUBJECT-SENTINEL");

			// The masthead and footer are what make it read as a Resgrid email rather than a raw note.
			sent.HtmlBody.Should().Contain("class=\"email-masthead_name\"");
			sent.HtmlBody.Should().Contain("Resgrid, LLC. All rights reserved.");
		}

		[Test]
		public async Task the_confirm_url_should_be_a_clickable_button_and_a_pasteable_fallback()
		{
			var sent = await Render(SampleContent());

			sent.HtmlBody.Should().Contain("<a href=\"https://confirm/link\" class=\"button button--green\"",
				"the recipient confirms by clicking, so the URL has to be a real anchor");
			sent.HtmlBody.Should().Contain("BUTTON-SENTINEL", "the button needs its localized label");

			// Some clients strip buttons, so the raw URL stays in the sub copy under it.
			Regex.Matches(sent.HtmlBody, Regex.Escape("https://confirm/link")).Count
				.Should().BeGreaterThanOrEqualTo(2, "the URL should also appear as copy-and-paste text");
		}

		[Test]
		public async Task every_piece_of_localized_wording_should_reach_the_rendered_email()
		{
			var content = SampleContent();
			var sent = await Render(content);

			var expected = new[]
			{
				content.Preheader, content.Greeting, content.Intro, content.Disclaimer, content.Action,
				content.ButtonText, content.TroubleText, content.Signoff, content.TeamName,
				content.DepartmentLabel, content.DepartmentName, content.TestLabel, content.TestName
			};

			foreach (var value in expected)
				sent.HtmlBody.Should().Contain(value, $"'{value}' is composed in the recipient's language and must survive rendering");

			sent.HtmlBody.Should().NotContain("{{", "an unresolved placeholder means a field nobody filled");
		}

		[Test]
		public void the_template_should_not_reference_a_placeholder_the_provider_never_fills()
		{
			// An unfilled placeholder renders as nothing rather than failing, so the mismatch has to
			// be caught here instead of by looking at the output.
			var placeholders = Regex.Matches(ReadTemplate(), @"\{\{\{?([#/^]?)([A-Za-z0-9_]+)\}?\}\}")
				.Cast<System.Text.RegularExpressions.Match>()
				.Select(x => x.Groups[2].Value)
				.Distinct()
				.ToList();

			placeholders.Should().BeEquivalentTo((IEnumerable<string>)ExpectedPlaceholders);
		}
	}
}
