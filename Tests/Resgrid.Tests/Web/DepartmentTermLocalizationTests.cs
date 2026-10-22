using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Moq;
using NUnit.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// The web app's localized strings carry the department's terminology: a renamed module's words reach its buttons, headings,
	/// messages and exports, while data, protected phrases and other modules' everyday words keep theirs.
	/// </summary>
	[TestFixture]
	public class DepartmentTermLocalizationTests
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

		private static DepartmentTerminologyConfig Config(params (string Key, string Text)[] terms)
		{
			var config = new DepartmentTerminologyConfig();
			foreach (var (key, text) in terms)
				config.Set(key, text);
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
			inner.Setup(l => l.GetAllStrings(It.IsAny<bool>())).Returns(strings.Select(s => new LocalizedString(s.Name, s.Value, false)));
			return new DepartmentTermStringLocalizer(inner.Object, resource, accessor);
		}

		[Test]
		public async Task A_renamed_module_reaches_its_buttons_and_headings()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Units, "Teams")));
			var localizer = Localizer("Units", accessor, ("NewUnitHeader", "New Unit"), ("UnitStaffing", "Unit Staffing"), ("NoUnitsInDepartment", "There are no units in your department."));

			localizer["NewUnitHeader"].Value.Should().Be("New Team");
			localizer["UnitStaffing"].Value.Should().Be("Team Staffing");
			localizer["NoUnitsInDepartment"].Value.Should().Be("There are no teams in your department.");
			localizer.GetAllStrings(true).Select(s => s.Value).Should().Contain("Team Staffing");
		}

		[Test]
		public async Task Values_formatted_into_a_string_are_never_rewritten()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Units, "Teams")));
			var localizer = Localizer("Units", accessor, ("DeleteUnitWarning", "Delete unit {0}?"));

			localizer["DeleteUnitWarning", "Unit 5"].Value.Should().Be("Delete team Unit 5?");
		}

		[Test]
		public async Task Everyday_words_are_renamed_only_inside_their_own_module()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Notes, "Journal"), (DepartmentTerms.Logs, "Diary")));

			Localizer("Call", accessor, ("CallNotes", "Call Notes"))["CallNotes"].Value.Should().Be("Call Notes", "a call's notes are not the Notes module");
			Localizer("Note", accessor, ("NewNote", "New Note"))["NewNote"].Value.Should().Be("New Journal");
			Localizer("Common", accessor, ("NotesModule", "Notes"), ("Notes", "Notes"))["NotesModule"].Value.Should().Be("Journal", "the module's own name string is renamed wherever it is used");
			Localizer("Common", accessor, ("Notes", "Notes"))["Notes"].Value.Should().Be("Notes");
			Localizer("Security", accessor, ("AuditLogsButton", "Audit Logs"))["AuditLogsButton"].Value.Should().Be("Audit Logs");
			Localizer("Logs", accessor, ("NewLog", "New Log"), ("LogIn", "Log In"))["NewLog"].Value.Should().Be("New Diary");
			Localizer("Logs", accessor, ("LogIn", "Log In"))["LogIn"].Value.Should().Be("Log In");
		}

		[Test]
		public async Task Strings_where_a_word_means_something_else_keep_it()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Units, "Teams"), (DepartmentTerms.Calls, "Incidents")));

			Localizer("Inventory", accessor, ("HelpFieldAmount", "How many units, in the item's unit of measure."))["HelpFieldAmount"].Value
				.Should().Be("How many units, in the item's unit of measure.");
			Localizer("Checklists", accessor, ("Units", "Units"))["Units"].Value.Should().Be("Units", "a checklist answer's units are units of measure");
			Localizer("CustomStatuses", accessor, ("StaffingOnCall", "On Call"))["StaffingOnCall"].Value.Should().Be("On Call");
			Localizer("Inventory", accessor, ("IssueToUnit", "Issue to unit"))["IssueToUnit"].Value.Should().Be("Issue to team");
		}

		[Test]
		public async Task The_item_word_for_personnel_stays_generic_outside_the_module()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Personnel, "Members")));

			Localizer("Reports", accessor, ("PersonnelHours", "Personnel hours per person"))["PersonnelHours"].Value.Should().Be("Members hours per person");
			Localizer("Person", accessor, ("AddPerson", "Add Person"))["AddPerson"].Value.Should().Be("Add Member");
		}

		[Test]
		public async Task Missing_strings_suppressed_pages_and_requests_without_terms_pass_through()
		{
			var accessor = await PreparedRequest(Config((DepartmentTerms.Units, "Teams")));
			Localizer("Units", accessor)["NoSuchKey"].ResourceNotFound.Should().BeTrue();

			var suppressed = DepartmentTerminologyFactory.CreateForRequest(Config((DepartmentTerms.Units, "Teams")));
			suppressed.Terms.Suppress();
			(await suppressed.Terms.PrepareAsync()).IsEmpty.Should().BeTrue();
			Localizer("Units", suppressed.Accessor, ("NewUnitHeader", "New Unit"))["NewUnitHeader"].Value.Should().Be("New Unit");

			Localizer("Units", Mock.Of<IHttpContextAccessor>(), ("NewUnitHeader", "New Unit"))["NewUnitHeader"].Value.Should().Be("New Unit");

			var nothing = await PreparedRequest(new DepartmentTerminologyConfig());
			Localizer("Units", nothing, ("NewUnitHeader", "New Unit"))["NewUnitHeader"].Value.Should().Be("New Unit");
		}

		[Test]
		public async Task Unrenamed_record_types_follow_renamed_modules()
		{
			var request = DepartmentTerminologyFactory.CreateForRequest(Config((DepartmentTerms.Units, "Teams"), (DepartmentTerms.Trainings, "Courses")));

			(await request.Terms.RecordTypeAsync((int)RmsOperationalRecordType.UnitActivity)).Should().Be("Team activity");
			(await request.Terms.RecordTypeAsync((int)RmsOperationalRecordType.Training)).Should().Be("Training", "a training record is not a Trainings course");
			(await request.Terms.SingularAsync(DepartmentTerms.Units)).Should().Be("Team");
			(await request.Terms.SingularAsync(DepartmentTerms.Calls)).Should().Be("Call");
		}

		[Test]
		public async Task Code_written_english_follows_the_terms_in_every_language()
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es");
			var request = DepartmentTerminologyFactory.CreateForRequest(Config((DepartmentTerms.Units, "Equipos"), (DepartmentTerms.Contacts, "Clientes")));
			await request.Terms.PrepareAsync();

			request.Context.WithDepartmentTerms("All Units").Should().Be("All Equipos");
			request.Context.WithDepartmentTerms("All Contacts").Should().Be("All Contacts", "Contacts is renamed inside its module only");
			request.Context.WithDepartmentTerms("All Contacts", "Contacts").Should().Be("All Clientes");
			request.Terms.Apply("Unit with that name already exists.").Should().Be("Equipo with that name already exists.");
			new DefaultHttpContext().WithDepartmentTerms("All Units").Should().Be("All Units");
		}

		[Test]
		public async Task Page_scripts_get_the_english_rules_with_their_scope()
		{
			var request = DepartmentTerminologyFactory.CreateForRequest(Config((DepartmentTerms.Units, "Teams"), (DepartmentTerms.Logs, "Diaries"), (DepartmentTerms.Personnel, "Members")));

			var rules = await request.Terms.ClientRulesAsync();

			rules.Rules.Should().ContainSingle(r => r.Key == DepartmentTerms.Units && r.Text == "Teams" && r.Everywhere && r.Forms.Contains("Units"));
			rules.Rules.Should().ContainSingle(r => r.Key == DepartmentTerms.Units && r.Text == "Team" && r.Forms.Contains("Unit"));
			rules.Rules.Should().OnlyContain(r => r.Key != DepartmentTerms.Logs || !r.Everywhere);
			rules.Rules.Should().ContainSingle(r => r.Key == DepartmentTerms.Personnel && r.Text == "Member" && !r.Everywhere, "\"person\" is generic outside Personnel");
			rules.Phrases.Should().Contain("log in");
			rules.Patterns.Should().NotBeEmpty();
			(await DepartmentTerminologyFactory.Create().ClientRulesAsync()).Should().BeNull();
		}

		[Test]
		public void Every_item_word_is_translated_in_every_language()
		{
			DepartmentTerminology.LocalizedSingularKeys.Should().BeEquivalentTo(DepartmentTerms.All.Where(t => t.HasSingular).Select(t => t.Key));

			var resources = new ResourceManager(typeof(Common));
			foreach (var culture in new[] { "ar", "de", "el", "en", "es", "fr", "it", "pl", "sv", "uk" }.Select(CultureInfo.GetCultureInfo))
			{
				foreach (var name in DepartmentTerminology.SingularResourceNames)
				{
					var set = resources.GetResourceSet(culture, true, false);
					set.Should().NotBeNull(culture.Name);
					set.GetString(name).Should().NotBeNullOrWhiteSpace(culture.Name + " " + name);
				}
			}
		}

		[Test]
		public void Excluded_and_scoped_strings_name_real_resource_keys()
		{
			// The exclusion list names strings by "Resource.Key"; a renamed key would silently stop protecting it.
			foreach (var (type, key) in new (System.Type, string)[]
			{
				(typeof(Resgrid.Localization.Areas.User.Inventory.Inventory), "HelpFieldAmount"),
				(typeof(Resgrid.Localization.Areas.User.Inventory.Inventory), "UnitOfMeasure"),
				(typeof(Resgrid.Localization.Areas.User.Checklists.Checklists), "Units"),
				(typeof(Resgrid.Localization.Areas.User.CustomStatuses.CustomStatuses), "StaffingOnCall"),
				(typeof(Resgrid.Localization.Areas.User.Workforce.Workforce), "UnitPrice"),
				(typeof(Resgrid.Localization.Areas.User.Mapping.MapVisualizations), "OptionDistanceUnit"),
				(typeof(Resgrid.Localization.Areas.User.WorkOrders.WorkOrders), "MeterUnit")
			})
				new ResourceManager(type).GetString(key, CultureInfo.GetCultureInfo("en")).Should().NotBeNullOrWhiteSpace(type.Name + "." + key);
		}
	}
}
