using System.Globalization;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;

namespace Resgrid.Tests.Models
{
	/// <summary>
	/// Carrying a department's terminology into Resgrid's own text (setting 118): a renamed module's name and item word flow into
	/// buttons, headings and messages, in the case of the text they replace, without touching phrases that mean something else.
	/// </summary>
	[TestFixture]
	public class DepartmentTermSubstitutionTests
	{
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		private static DepartmentTermSubstitution EnglishSubstitution(DepartmentTerminologyConfig config)
		{
			var forms = DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.Module)
				.Select(t => new DepartmentTermWordForms(t.Key, new[] { t.DefaultName }, t.HasSingular ? new[] { t.DefaultSingular } : new string[0]));
			return DepartmentTermSubstitution.Create(config, forms, English);
		}

		private static DepartmentTermSubstitution Renamed(string key, string name, string singular = null)
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(key, name, singular);
			return EnglishSubstitution(config);
		}

		[TestCase("New Unit", "New Team")]
		[TestCase("Unit Staffing", "Team Staffing")]
		[TestCase("Units", "Teams")]
		[TestCase("Add a new unit", "Add a new team")]
		[TestCase("No units in this group", "No teams in this group")]
		[TestCase("SET UNIT STATUS", "SET TEAM STATUS")]
		[TestCase("The unit's crew", "The team's crew")]
		[TestCase("Unit-level settings", "Team-level settings")]
		public void A_renamed_module_flows_into_buttons_headings_and_messages(string shipped, string expected)
		{
			Renamed(DepartmentTerms.Units, "Teams").Apply(shipped).Should().Be(expected);
		}

		[Test]
		public void Words_that_only_contain_the_term_are_left_alone()
		{
			var substitution = Renamed(DepartmentTerms.Calls, "Incidents");

			substitution.Apply("Callback").Should().Be("Callback");
			substitution.Apply("Recall the call").Should().Be("Recall the incident");
			substitution.Apply("Calling").Should().Be("Calling");
		}

		[Test]
		public void English_articles_follow_the_department_word()
		{
			Renamed(DepartmentTerms.Units, "Apparatus").Apply("Select a unit").Should().Be("Select an apparatus");
			Renamed(DepartmentTerms.Units, "Apparatus").Apply("A unit is required.").Should().Be("An apparatus is required.");
			Renamed(DepartmentTerms.Units, "EMS Units").Apply("Add a unit").Should().Be("Add an EMS unit", "acronyms keep their case and read letter by letter");
			Renamed(DepartmentTerms.Calls, "Incidents").Apply("Close an call").Should().Be("Close an incident");
			Renamed(DepartmentTerms.Units, "Teams").Apply("an unit").Should().Be("a team");
		}

		[TestCase("Unit Price")]
		[TestCase("Unit of Measure")]
		[TestCase("Average unit cost")]
		[TestCase("Install the Unit app on each tablet")]
		[TestCase("the Responder, Unit, IC and Dispatch apps")]
		[TestCase("The Unit and Responder apps")]
		public void Phrases_where_the_word_means_something_else_keep_it(string shipped)
		{
			Renamed(DepartmentTerms.Units, "Teams").Apply(shipped).Should().Be(shipped);
		}

		[Test]
		public void A_protected_phrase_does_not_shield_the_rest_of_the_text()
		{
			Renamed(DepartmentTerms.Units, "Teams").Apply("Unit price per unit").Should().Be("Unit price per team");
			Renamed(DepartmentTerms.Logs, "Journals").Apply("Log In to add a log").Should().Be("Log In to add a journal");
			Renamed(DepartmentTerms.Calls, "Incidents").Apply("The call sign of the unit on the call").Should().Be("The call sign of the unit on the incident");
		}

		[Test]
		public void The_on_call_staffing_level_keeps_its_name()
		{
			var substitution = Renamed(DepartmentTerms.Calls, "Incidents");

			substitution.Apply("On Call").Should().Be("On Call");
			substitution.Apply("Units on Call").Should().Be("Units on Incident");
		}

		[Test]
		public void A_saved_item_word_wins_over_the_guess()
		{
			var substitution = Renamed(DepartmentTerms.Personnel, "Crew", "Crew Member");

			substitution.Apply("Personnel Staffing").Should().Be("Crew Staffing");
			substitution.Apply("Add Person").Should().Be("Add Crew Member");
		}

		[Test]
		public void Only_the_item_word_can_be_renamed()
		{
			var substitution = Renamed(DepartmentTerms.Units, null, "Rig");

			substitution.Apply("New Unit").Should().Be("New Rig");
			substitution.Apply("Units").Should().Be("Units");
		}

		[Test]
		public void Multi_word_terms_match_as_a_whole()
		{
			var substitution = Renamed(DepartmentTerms.WorkOrders, "Jobs");

			substitution.Apply("New work order").Should().Be("New job");
			substitution.Apply("Work Orders").Should().Be("Jobs");
			substitution.Apply("Work order\nhistory").Should().Be("Job\nhistory");
			substitution.Apply("Work hours").Should().Be("Work hours");
		}

		[Test]
		public void A_department_word_containing_the_shipped_word_is_not_replaced_again()
		{
			Renamed(DepartmentTerms.Units, "Unit Teams").Apply("New Unit in Units").Should().Be("New Unit Team in Unit Teams");
		}

		[Test]
		public void Several_terms_are_replaced_in_one_pass()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Units, "Calls");
			config.Set(DepartmentTerms.Calls, "Incidents");

			EnglishSubstitution(config).Apply("Units on the call").Should().Be("Calls on the incident", "a replaced word is not replaced a second time");
		}

		[Test]
		public void The_scope_can_keep_a_term_out_of_a_string()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Notes, "Journal");
			config.Set(DepartmentTerms.Units, "Teams");
			var substitution = EnglishSubstitution(config);

			substitution.Apply("Unit notes", match => match.Key != DepartmentTerms.Notes).Should().Be("Team notes");
			substitution.Apply("Unit notes").Should().Be("Team journal");
		}

		[Test]
		public void Nothing_renamed_changes_nothing()
		{
			var substitution = EnglishSubstitution(new DepartmentTerminologyConfig());

			substitution.IsEmpty.Should().BeTrue();
			substitution.Apply("New Unit").Should().Be("New Unit");
			DepartmentTermSubstitution.None.Apply("New Unit").Should().Be("New Unit");
			Renamed(DepartmentTerms.Units, "Teams").Apply(null).Should().BeNull();
		}

		[Test]
		public void Record_types_are_never_substituted_into_text()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Work), "Shift On Duty");

			EnglishSubstitution(config).Apply("Work hours").Should().Be("Work hours", "record types are named where a type is shown, not in sentences");
		}

		[Test]
		public void Translated_forms_are_matched_in_their_language()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Units, "Equipos");
			var forms = new[] { new DepartmentTermWordForms(DepartmentTerms.Units, new[] { "Unidades" }, new[] { "Unidad" }) };
			var substitution = DepartmentTermSubstitution.Create(config, forms, CultureInfo.GetCultureInfo("es"));

			substitution.Apply("Nueva unidad").Should().Be("Nueva equipo");
			substitution.Apply("Unidades").Should().Be("Equipos");
			substitution.Apply("la aplicación Unit").Should().Be("la aplicación Unit", "an English word in a translated string is a product name");
			substitution.Apply("a unidades").Should().Be("a equipos", "English articles are only adjusted in English");
		}

		[TestCase("Fire Apparatus", "unit", "fire apparatus")]
		[TestCase("EMS Teams", "units", "EMS teams")]
		[TestCase("iPads", "Units", "iPads")]
		[TestCase("teams", "Units", "Teams")]
		[TestCase("Teams", "UNITS", "TEAMS")]
		public void Case_follows_the_replaced_word(string departmentWord, string source, string expected)
		{
			DepartmentTermSubstitution.MatchCase(source, departmentWord, English).Should().Be(expected);
		}

		[TestCase("Teams", "Team")]
		[TestCase("Companies", "Company")]
		[TestCase("Fire Trucks", "Fire Truck")]
		[TestCase("Boxes", "Box")]
		[TestCase("Dispatches", "Dispatch")]
		[TestCase("Classes", "Class")]
		[TestCase("Responses", "Response")]
		[TestCase("Apparatus", "Apparatus")]
		[TestCase("Staff", "Staff")]
		[TestCase("EMS", "EMS")]
		[TestCase("Bus", "Bus")]
		[TestCase("  Crews ", "Crew")]
		public void The_item_word_is_guessed_from_a_regular_plural(string name, string expected)
		{
			DepartmentTerms.DeriveSingular(name).Should().Be(expected);
		}

		[Test]
		public void Only_modules_with_an_item_word_keep_one()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Units, "Teams");
			config.Set(DepartmentTerms.Calendar, "Schedule", "Event");
			config.Set(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run), "Incident", "Ignored");

			config.SingularFor(DepartmentTerms.Units).Should().Be("Team", "guessed from the name");
			config.SavedSingularFor(DepartmentTerms.Units).Should().BeNull();
			config.SingularFor(DepartmentTerms.Calendar).Should().BeNull("Calendar has no item word");
			config.SingularFor(DepartmentTerms.RecordTypeKey(RmsDefinitionKeys.Run)).Should().BeNull();
			config.SingularFor(DepartmentTerms.Calls).Should().BeNull("an unrenamed term keeps Resgrid's words");
			DepartmentTerms.Find(DepartmentTerms.Calendar).HasSingular.Should().BeFalse();
			DepartmentTerms.All.Where(t => t.Category == DepartmentTermCategories.RecordType).Should().OnlyContain(t => !t.HasSingular);
		}

		[Test]
		public void An_item_word_saves_with_or_without_a_name_and_round_trips()
		{
			var config = new DepartmentTerminologyConfig();
			config.Set(DepartmentTerms.Units, "Apparatus", "Rig");
			config.Set(DepartmentTerms.Personnel, " ", "Member");
			config.Set(DepartmentTerms.Calls, "Incidents", new string('x', DepartmentTerms.MaxLength + 1));

			var copy = ObjectSerialization.Deserialize<DepartmentTerminologyConfig>(ObjectSerialization.Serialize(config));

			copy.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");
			copy.SingularFor(DepartmentTerms.Units).Should().Be("Rig");
			copy.TextFor(DepartmentTerms.Personnel).Should().BeNull();
			copy.SingularFor(DepartmentTerms.Personnel).Should().Be("Member");
			copy.SingularFor(DepartmentTerms.Calls).Should().Be("Incident", "an item word over the limit is dropped and guessed instead");
			copy.HasAny().Should().BeTrue();
			new DepartmentTerminologyConfig().HasAny().Should().BeFalse();

			config.Set(DepartmentTerms.Units, "", "");
			config.Terms.Should().NotContain(t => t.Key == DepartmentTerms.Units, "an emptied row goes back to Resgrid's words");
		}
	}
}
