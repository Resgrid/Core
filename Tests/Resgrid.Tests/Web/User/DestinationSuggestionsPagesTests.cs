using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Every web page that chooses a call's destination shows the rule-based suggestions: New Call (suggest and fill), Update
	/// Call and Add Archived Call (suggest only, the call form's own fields), and the recurring call form (suggest only, its own
	/// fields named through the partial). The Units page's single-unit Set Status modal sends the unit, so its destination list
	/// is ranked for it; the multi-unit modal does not.
	/// </summary>
	[TestFixture]
	public class DestinationSuggestionsPagesTests
	{
		private static string WebProject()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			Assert.That(directory, Is.Not.Null, "the repository root should be locatable from the test directory");
			return Path.Combine(directory!.FullName, "Web", "Resgrid.Web");
		}

		private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(WebProject(), Path.Combine(parts)));

		private static string Script(string name) => Read("wwwroot", "js", "app", "internal", "dispatch", name);

		[Test]
		public void Add_archived_call_shows_the_suggestions_without_filling()
		{
			var view = Read("Areas", "User", "Views", "Dispatch", "AddArchivedCall.cshtml");

			view.Should().Contain("<partial name=\"_DestinationSuggestions\" />");
			view.Should().NotContain("DestinationSuggestionsFill");
			view.IndexOf("resgrid.dispatch.destinations.js", System.StringComparison.Ordinal).Should()
				.BeGreaterThan(-1).And.BeLessThan(view.IndexOf("resgrid.dispatch.addArchivedCall.js", System.StringComparison.Ordinal));
		}

		[Test]
		public void Add_archived_call_refreshes_the_suggestions_when_its_script_moves_the_location()
		{
			var script = Script("resgrid.dispatch.addArchivedCall.js");

			script.Should().Contain("resgrid.dispatch.destinations.schedule()");
			// Pin placement (map clicks, lookups, the location POI), dragging, the browser's location, and type/priority/template.
			Regex.Matches(script, @"scheduleDestinations\(\);").Count.Should().BeGreaterThanOrEqualTo(4);
		}

		[Test]
		public void The_recurring_call_form_shows_the_suggestions_from_its_own_fields_without_filling()
		{
			var view = Read("Areas", "User", "Views", "RecurringCalls", "Edit.cshtml");

			view.Should().Contain("~/Areas/User/Views/Dispatch/_DestinationSuggestions.cshtml");
			view.Should().NotContain("DestinationSuggestionsFill");
			view.Should().Contain("{ \"DestinationSuggestions.Destination\", \"#DestinationPoiId\" }");
			view.Should().Contain("{ \"DestinationSuggestions.Coordinates\", \"#Coordinates\" }");
			view.Should().Contain("{ \"DestinationSuggestions.Address\", \"#Address\" }");
			view.Should().Contain("{ \"DestinationSuggestions.Priority\", \"#Priority\" }");
			view.Should().Contain("{ \"DestinationSuggestions.Type\", \"#Type\" }");
			view.Should().Contain("{ \"DestinationSuggestions.Free\", \".recurring-destination-free\" }");
			view.Should().Contain("resgrid.dispatch.destinations.js");

			// The ids the mapping names are the form's own.
			view.Should().Contain("asp-for=\"DestinationPoiId\"");
			view.Should().Contain("asp-for=\"Coordinates\"");
			view.Should().Contain("asp-for=\"Address\"");
			view.Should().Contain("asp-for=\"Priority\"");
			view.Should().Contain("asp-for=\"Type\"");
			view.Should().Contain("recurring-destination-free");
		}

		[Test]
		public void New_call_still_fills_and_update_call_still_only_suggests()
		{
			Read("Areas", "User", "Views", "Dispatch", "NewCall.cshtml").Should().Contain("{ \"DestinationSuggestionsFill\", true }");
			Read("Areas", "User", "Views", "Dispatch", "UpdateCall.cshtml").Should().Contain("<partial name=\"_DestinationSuggestions\" />");
		}

		[TestCase("Destination", "destination")]
		[TestCase("Latitude", "latitude")]
		[TestCase("Longitude", "longitude")]
		[TestCase("Coordinates", "coordinates")]
		[TestCase("Address", "address")]
		[TestCase("Priority", "priority")]
		[TestCase("Type", "type")]
		[TestCase("TypeId", "type-id")]
		[TestCase("Free", "free")]
		[TestCase("Unit", "unit")]
		[TestCase("UnitId", "unit-id")]
		[TestCase("CallId", "call-id")]
		public void Each_field_a_page_names_reaches_the_script(string viewDataKey, string attribute)
		{
			var partial = Read("Areas", "User", "Views", "Dispatch", "_DestinationSuggestions.cshtml");
			var script = Script("resgrid.dispatch.destinations.js");

			partial.Should().Contain($"data-{attribute}=\"@field(\"{viewDataKey}\")\"");
			script.Should().Contain($"attr('{attribute}'");
		}

		[Test]
		public void The_script_sends_the_call_type_id_unit_and_call_when_the_page_has_them()
		{
			var script = Script("resgrid.dispatch.destinations.js");

			script.Should().Contain("query.callTypeId = current.callTypeId");
			script.Should().Contain("query.unitId = current.unitId");
			script.Should().Contain("query.callId = current.callId");
		}

		[Test]
		public void Only_the_single_unit_status_modal_sends_its_unit()
		{
			var script = Read("wwwroot", "js", "app", "internal", "units", "resgrid.units.index.js");

			script.Should().Contain("'&unitId=' + encodeURIComponent($('#setUnitStateUnitId').val()");
			foreach (Match request in Regex.Matches(script, @"GetUnitStatusDestinationHtmlForDropdown\?[^;]*"))
			{
				if (request.Value.Contains("getSelectedCheckboxDataValue"))
					request.Value.Should().NotContain("unitId");
			}
		}
	}
}
