using System;
using System.IO;
using System.Text;
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
	/// Department masthead in the operational email templates (RMS plan section 4.10.1). The plan's contract is
	/// two-sided: with the opt-in and a logo, the Call, Message and ReportDelivery emails carry the department
	/// masthead; without either, the email is today's output byte-for-byte. The baselines under
	/// Providers/Fixtures/EmailBaseline were captured from the templates before the branding sections were
	/// added, so the equality tests are a real regression check and not a self-referential one. Re-capture
	/// them with the explicit <see cref="Regenerate_baselines"/> test only for a deliberate template change.
	/// </summary>
	[TestFixture]
	public class DepartmentEmailBrandingTemplateTests
	{
		private const string LogoUrl = "https://app.example/User/Department/PublicMasthead?key=0123456789abcdef0123456789abcdef0123456789abcdef";

		private static DepartmentEmailBranding Branded(string displayName = "Springfield Fire", string website = "https://www.springfieldfire.example/")
		{
			return new DepartmentEmailBranding { DepartmentId = 4, Enabled = true, DisplayName = displayName, LogoUrl = LogoUrl, Website = website };
		}

		private static async Task<Email> RenderAsync(Func<PostmarkTemplateProvider, Task<bool>> send)
		{
			Email sent = null;
			var sender = new Mock<IEmailSender>();
			sender.Setup(x => x.Send(It.IsAny<Email>())).Callback<Email>(x => sent = x).ReturnsAsync(true);

			var result = await send(new PostmarkTemplateProvider(sender.Object));

			result.Should().BeTrue("the template should render and hand off to the sender");
			sent.Should().NotBeNull();
			return sent;
		}

		private static Task<Email> CallAsync(DepartmentEmailBranding branding)
		{
			return RenderAsync(p => p.SendCallMail("member@example.com", "P1 Structure Fire", "Structure Fire", "High", "Smoke showing from <b>rear</b>",
				"https://maps.example/1", "100 Main St", "2026-09-03 10:00 UTC", 77, "user-1", "39.7,-89.6", "https://audio.example/x", branding));
		}

		private static Task<Email> MessageAsync(DepartmentEmailBranding branding)
		{
			return RenderAsync(p => p.SendMessageMail("member@example.com", "Drill tonight", "Drill tonight", "<p>Bring gear &amp; radios</p>",
				"chief@example.com", "Chief Wiggum", "2026-09-03 10:00 UTC", 55, branding));
		}

		private static Task<Email> ReportAsync(DepartmentEmailBranding branding)
		{
			return RenderAsync(p => p.SendReportDeliveryMail("member@example.com", "Weekly activity", "Attached.", "2026-09-03 10:00 UTC",
				"Weekly activity", "weekly.pdf", new byte[] { 1, 2, 3, 4 }, "https://app.example/report/1", branding));
		}

		/// <summary>The call export link carries an encrypted token that is not stable across runs; nothing else varies.</summary>
		private static string Stable(string html)
		{
			return Regex.Replace(html, @"query=[^""&]*", "query=TOKEN");
		}

		/// <summary>Line endings are a checkout artifact (git may normalize the fixture), not part of the contract.</summary>
		private static string Norm(string html)
		{
			return html?.Replace("\r\n", "\n");
		}

		private static string BaselineDirectory()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Tests", "Resgrid.Tests", "Providers", "Fixtures", "EmailBaseline");
		}

		private static string Baseline(string name)
		{
			var path = Path.Combine(BaselineDirectory(), name + ".html");
			System.IO.File.Exists(path).Should().BeTrue($"the pre-branding baseline {name}.html should be checked in");
			return Norm(System.IO.File.ReadAllText(path, Encoding.UTF8));
		}

		[Test, Explicit("Rewrites the byte-for-byte baselines from the current templates; only for a deliberate template change.")]
		public async Task Regenerate_baselines()
		{
			Directory.CreateDirectory(BaselineDirectory());
			var utf8 = new UTF8Encoding(false);

			System.IO.File.WriteAllText(Path.Combine(BaselineDirectory(), "Call.html"), Stable((await CallAsync(null)).HtmlBody), utf8);
			System.IO.File.WriteAllText(Path.Combine(BaselineDirectory(), "Message.html"), (await MessageAsync(null)).HtmlBody, utf8);
			System.IO.File.WriteAllText(Path.Combine(BaselineDirectory(), "ReportDelivery.html"), (await ReportAsync(null)).HtmlBody, utf8);
		}

		// ── Fallback: byte-for-byte ────────────────────────────────────────────────────────────────

		[Test]
		public async Task Call_without_branding_is_todays_output_byte_for_byte()
		{
			Norm(Stable((await CallAsync(null)).HtmlBody)).Should().Be(Baseline("Call"));
		}

		[Test]
		public async Task Call_with_the_opt_in_but_no_logo_is_todays_output_byte_for_byte()
		{
			var disabled = new DepartmentEmailBranding { DepartmentId = 4, Enabled = false, DisplayName = "Springfield Fire", Website = "https://www.springfieldfire.example/" };
			Norm(Stable((await CallAsync(disabled)).HtmlBody)).Should().Be(Baseline("Call"));

			// Enabled without a masthead URL is the "toggle on, logo removed" race; still Resgrid chrome.
			var noLogo = new DepartmentEmailBranding { DepartmentId = 4, Enabled = true, DisplayName = "Springfield Fire" };
			Norm(Stable((await CallAsync(noLogo)).HtmlBody)).Should().Be(Baseline("Call"));
		}

		[Test]
		public async Task Message_without_branding_is_todays_output_byte_for_byte()
		{
			Norm((await MessageAsync(null)).HtmlBody).Should().Be(Baseline("Message"));
			Norm((await MessageAsync(DepartmentEmailBranding.Disabled(4, "Springfield Fire"))).HtmlBody).Should().Be(Baseline("Message"));
		}

		[Test]
		public async Task Report_delivery_without_branding_is_todays_output_byte_for_byte()
		{
			Norm((await ReportAsync(null)).HtmlBody).Should().Be(Baseline("ReportDelivery"));
			Norm((await ReportAsync(DepartmentEmailBranding.Disabled(4, "Springfield Fire"))).HtmlBody).Should().Be(Baseline("ReportDelivery"));
		}

		// ── Branded: department masthead, Resgrid footer ───────────────────────────────────────────

		[Test]
		public async Task Call_with_branding_swaps_the_masthead_and_keeps_the_resgrid_footer()
		{
			var html = (await CallAsync(Branded())).HtmlBody;

			html.Should().Contain($"<img src=\"{LogoUrl}\"", "the masthead rendition is referenced by its anonymous URL");
			html.Should().Contain("class=\"email-masthead_logo\"");
			html.Should().Contain("alt=\"Springfield Fire\"");
			html.Should().Contain("<a href=\"https://www.springfieldfire.example/\" class=\"email-masthead_name\"", "the masthead links to the department website");
			html.Should().NotContain("<a href=\"https://resgrid.com\" class=\"email-masthead_name\"", "the Resgrid text masthead is replaced, not duplicated");
			html.Should().Contain("Powered by Resgrid", "the service-provider line stays with department branding");
			html.Should().Contain("Resgrid, LLC. All rights reserved.", "the legal identity in the footer never changes");
			html.Should().Contain("<h1>Structure Fire</h1>", "the body is untouched");
		}

		[Test]
		public async Task Message_and_report_delivery_with_branding_gain_the_department_masthead()
		{
			var message = (await MessageAsync(Branded())).HtmlBody;
			message.Should().Contain($"<img src=\"{LogoUrl}\"");
			message.Should().Contain("class=\"email-masthead\"");
			message.Should().Contain("Powered by Resgrid");
			message.Should().Contain("By Chief Wiggum at 2026-09-03 10:00 UTC");

			var report = (await ReportAsync(Branded())).HtmlBody;
			report.Should().Contain($"<img src=\"{LogoUrl}\"");
			report.Should().Contain("Springfield Fire");
			report.Should().Contain("Powered by Resgrid");
			report.Should().Contain("View your Scheduled Report Deliveries");
		}

		[Test]
		public async Task Branding_without_a_website_links_the_masthead_to_the_app()
		{
			var html = (await CallAsync(Branded(website: null))).HtmlBody;

			html.Should().Contain($"<a href=\"{Resgrid.Config.SystemBehaviorConfig.ResgridBaseUrl}\" class=\"email-masthead_name\"");
		}

		[Test]
		public async Task Department_values_are_html_encoded_in_the_masthead()
		{
			var html = (await CallAsync(Branded(displayName: "Smith & <Sons> Fire", website: "https://x.example/?a=1&b=2"))).HtmlBody;

			html.Should().Contain("alt=\"Smith &amp; &lt;Sons&gt; Fire\"");
			html.Should().Contain("href=\"https://x.example/?a=1&amp;b=2\"");
			html.Should().NotContain("<Sons>");
		}

		// ── Account and service templates never take department branding ──────────────────────────

		[TestCase("Welcome.html")]
		[TestCase("Invitation.html")]
		[TestCase("PasswordRecovery.html")]
		[TestCase("PasswordChangedByAdministrator.html")]
		[TestCase("Receipt.html")]
		[TestCase("Cancelled.html")]
		[TestCase("ChargeFailed.html")]
		[TestCase("DeleteDepartment.html")]
		[TestCase("DepartmentLinkCreated.html")]
		[TestCase("TroubleAlert.html")]
		[TestCase("CommunicationTest.html")]
		public void Account_and_service_templates_carry_no_department_branding_sections(string template)
		{
			var assembly = typeof(PostmarkTemplateProvider).Assembly;
			using var resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Template." + template);
			resource.Should().NotBeNull();
			using var reader = new StreamReader(resource);
			var text = reader.ReadToEnd();

			text.Should().NotContain("department_branding");
			text.Should().NotContain("department_logo_url");
			text.Should().NotContain("resgrid_branding");
		}

		[TestCase("Call.html")]
		[TestCase("Message.html")]
		[TestCase("ReportDelivery.html")]
		public void Operational_templates_carry_the_department_branding_section(string template)
		{
			var assembly = typeof(PostmarkTemplateProvider).Assembly;
			using var resource = assembly.GetManifestResourceStream(assembly.GetName().Name + ".Template." + template);
			using var reader = new StreamReader(resource);
			var text = reader.ReadToEnd();

			text.Should().Contain("{{#department_branding}}");
			text.Should().Contain("{{department_logo_url}}");
			text.Should().Contain("{{department_display_name}}");
			text.Should().Contain("{{department_website}}");
		}
	}
}
