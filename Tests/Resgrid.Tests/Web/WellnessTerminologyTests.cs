using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Web.Areas.User.Models.Departments;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Department terminology for member wellness (decided 2026-11-26): the wellness section, the wellness officers, the designated officers
	/// and the peer support team are department-renamable. Role terms carry a plural and a word for one person through every screen
	/// (wellness pages, Admin Assist's help, the call screen's button); "Wellness" itself only inside the wellness pages and their help.
	/// </summary>
	[TestFixture]
	public class WellnessTerminologyTests
	{
		private CultureInfo _uiCulture;
		private CultureInfo _culture;

		[SetUp]
		public void SetUp()
		{
			_uiCulture = CultureInfo.CurrentUICulture;
			_culture = CultureInfo.CurrentCulture;
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
		}

		[TearDown]
		public void TearDown()
		{
			CultureInfo.CurrentUICulture = _uiCulture;
			CultureInfo.CurrentCulture = _culture;
		}

		private static DepartmentTerminologyConfig Config(params (string Key, string Text, string Singular)[] terms)
		{
			var config = new DepartmentTerminologyConfig();
			foreach (var (key, text, singular) in terms)
				config.Set(key, text, singular);
			return config;
		}

		private static async Task<IHttpContextAccessor> PreparedRequest(DepartmentTerminologyConfig config)
		{
			var request = DepartmentTerminologyFactory.CreateForRequest(config);
			await request.Terms.PrepareAsync();
			return request.Accessor;
		}

		private static IStringLocalizer Localizer(string resource, IHttpContextAccessor accessor, params (string Name, string Value)[] strings)
		{
			var inner = new Mock<IStringLocalizer>();
			inner.Setup(l => l[It.IsAny<string>()]).Returns((string name) =>
			{
				var found = strings.FirstOrDefault(s => s.Name == name);
				return found.Name == null ? new LocalizedString(name, name, true) : new LocalizedString(name, found.Value, false);
			});
			return new DepartmentTermStringLocalizer(inner.Object, resource, accessor);
		}

		[Test]
		public void The_wellness_terms_are_in_the_catalog_with_their_words()
		{
			DepartmentTerms.Find(DepartmentTerms.Wellness).Category.Should().Be(DepartmentTermCategories.Module);
			DepartmentTerms.Find(DepartmentTerms.Wellness).HasSingular.Should().BeFalse();

			var officers = DepartmentTerms.Find(DepartmentTerms.WellnessOfficers);
			officers.Category.Should().Be(DepartmentTermCategories.Role);
			officers.DefaultName.Should().Be("Wellness officers");
			officers.DefaultSingular.Should().Be("Wellness officer");
			DepartmentTerms.Find(DepartmentTerms.DesignatedOfficers).DefaultSingular.Should().Be("Designated officer");
			DepartmentTerms.Find(DepartmentTerms.PeerSupportTeam).HasSingular.Should().BeFalse("a team has no word for one of it here");

			DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.Role).Should().OnlyContain(t => t.ReplacesWords && t.Key.StartsWith(DepartmentTerms.RolePrefix));
			DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.RecordType).Should().OnlyContain(t => !t.ReplacesWords);
		}

		[Test]
		public async Task Renamed_wellness_roles_reach_the_wellness_pages_admin_assist_and_the_call_screen_button()
		{
			var accessor = await PreparedRequest(Config(
				(DepartmentTerms.WellnessOfficers, "Safety officers", "Safety officer"),
				(DepartmentTerms.DesignatedOfficers, "Infection control officers", "Infection control officer"),
				(DepartmentTerms.PeerSupportTeam, "CISM team", null)));

			Localizer("Wellness", accessor, ("WellnessOfficerRoleLabel", "Wellness officer role"))["WellnessOfficerRoleLabel"].Value.Should().Be("Safety officer role");
			Localizer("Wellness", accessor, ("ReportIntro", "Counts per member for the wellness officers."))["ReportIntro"].Value
				.Should().Be("Counts per member for the safety officers.");
			Localizer("Wellness", accessor, ("RemindPeerTeamLabel", "Let the peer support team know before the anniversary"))["RemindPeerTeamLabel"].Value
				.Should().Be("Let the CISM team know before the anniversary");
			Localizer("MemberHealth", accessor, ("OpenCasesHeader", "Open exposure cases (designated officer)"))["OpenCasesHeader"].Value
				.Should().Be("Open exposure cases (infection control officer)");
			Localizer("AdminAssist", accessor, ("Field.WellnessSettings.WellnessOfficerRoleId", "Wellness settings / Wellness officer role"))["Field.WellnessSettings.WellnessOfficerRoleId"].Value
				.Should().Be("Wellness settings / Safety officer role", "Admin Assist's help carries the department's word");
			Localizer("AdminAssist", accessor, ("Area.people", "People"))["Area.people"].Value.Should().Be("People");
		}

		[Test]
		public async Task A_renamed_wellness_section_is_renamed_on_its_own_pages_only()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Wellness, "Health and Safety", null)));

			Localizer("Wellness", accessor, ("SettingsTitle", "Wellness settings"))["SettingsTitle"].Value.Should().Be("Health and Safety settings");
			Localizer("Wellness", accessor, ("WellnessOfficerRoleLabel", "Wellness officer role"))["WellnessOfficerRoleLabel"].Value
				.Should().Be("Health and Safety officer role", "a role the department did not rename follows the section's name");
			Localizer("AdminAssist", accessor, ("Setting.WellnessSettings", "Wellness settings"))["Setting.WellnessSettings"].Value.Should().Be("Health and Safety settings");
			Localizer("Call", accessor, ("WellnessCheckType", "Wellness check"))["WellnessCheckType"].Value
				.Should().Be("Wellness check", "a welfare check on a call is not the wellness section");
		}

		[Test]
		public async Task Role_names_resolve_for_headings_and_the_scope_is_everywhere()
		{
			var terms = DepartmentTerminologyFactory.Create(Config((DepartmentTerms.PeerSupportTeam, "CISM team", null)));
			(await terms.NameAsync(DepartmentTerms.PeerSupportTeam, "Peer support team")).Should().Be("CISM team");
			(await terms.NameAsync(DepartmentTerms.WellnessOfficers, "Wellness officers")).Should().Be("Wellness officers");
			terms.DefaultName(DepartmentTerms.WellnessOfficers).Should().Be("Wellness officers");
			terms.DefaultSingular(DepartmentTerms.DesignatedOfficers).Should().Be("Designated officer");

			DepartmentTermScopes.IsEverywhere(DepartmentTerms.WellnessOfficers).Should().BeTrue();
			DepartmentTermScopes.IsEverywhere(DepartmentTerms.PeerSupportTeam).Should().BeTrue();
			DepartmentTermScopes.IsEverywhere(DepartmentTerms.Wellness).Should().BeFalse();

			var rules = await terms.ClientRulesAsync();
			rules.Rules.Should().ContainSingle(r => r.Key == DepartmentTerms.PeerSupportTeam && r.Everywhere && r.Text == "CISM team");
		}

		[Test]
		public void The_terminology_page_offers_the_roles_with_a_word_for_one_person()
		{
			var view = TerminologyView.From(Config((DepartmentTerms.WellnessOfficers, "Safety officers", "Safety officer")));
			var row = view.Terms.Single(t => t.Key == DepartmentTerms.WellnessOfficers);
			row.Category.Should().Be(DepartmentTermCategories.Role);
			row.HasSingular.Should().BeTrue();
			row.Text.Should().Be("Safety officers");
			row.Singular.Should().Be("Safety officer");
			view.Terms.Single(t => t.Key == DepartmentTerms.PeerSupportTeam).HasSingular.Should().BeFalse();
		}
	}
}
