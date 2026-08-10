using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Chatbot.NLU.Providers;
using Resgrid.Chatbot.Services;
using Resgrid.Model;

namespace Resgrid.Tests.Chatbot
{
	/// <summary>
	/// Keyword-classifier routing for the incident-command (ICS) questions an Incident Commander asks
	/// on a command board, plus the ordering regressions that matter: the incident patterns sit ahead
	/// of the department-wide unit/personnel/responder patterns, so this fixture pins BOTH that the
	/// incident phrasings bind and that the older phrasings still keep their original intent.
	/// </summary>
	[TestFixture]
	public class IncidentCommandIntentClassifierTests
	{
		private KeywordIntentClassifier _classifier;

		[SetUp]
		public void Setup()
		{
			_classifier = new KeywordIntentClassifier();
		}

		[TestCase("PAR", "incident_par")]
		[TestCase("par check", "incident_par")]
		[TestCase("accountability", "incident_par")]
		[TestCase("give me a PAR", "incident_par")]
		[TestCase("who's overdue?", "incident_par")]
		[TestCase("who is not accounted for", "incident_par")]
		[TestCase("span of control", "incident_span_of_control")]
		[TestCase("which lanes are over staffed", "incident_span_of_control")]
		[TestCase("who is working Division A", "incident_resources")]
		[TestCase("what's in staging?", "incident_resources")]
		[TestCase("what resources do I have on scene", "incident_resources")]
		[TestCase("who is unassigned", "incident_resources")]
		[TestCase("objectives", "incident_objectives")]
		[TestCase("what objectives are still open", "incident_objectives")]
		[TestCase("what's my next benchmark", "incident_objectives")]
		[TestCase("needs", "incident_needs")]
		[TestCase("what needs are unfilled", "incident_needs")]
		[TestCase("what did we order", "incident_needs")]
		[TestCase("what am I waiting on", "incident_needs")]
		[TestCase("ics roles", "incident_roles")]
		[TestCase("who is the safety officer", "incident_roles")]
		[TestCase("do we have a staging area manager?", "incident_roles")]
		[TestCase("which positions are unfilled", "incident_roles")]
		[TestCase("incident log", "incident_timeline")]
		[TestCase("what happened in the last 30 minutes", "incident_timeline")]
		[TestCase("show me the last 5 log entries", "incident_timeline")]
		[TestCase("timers", "incident_timers")]
		[TestCase("what timers are due", "incident_timers")]
		[TestCase("when is my next PAR", "incident_timers")]
		[TestCase("briefing", "incident_briefing")]
		[TestCase("give me a transfer of command briefing", "incident_briefing")]
		[TestCase("ics-201", "incident_briefing")]
		[TestCase("what am I missing", "incident_checklist")]
		[TestCase("checklist for a structure fire", "incident_checklist")]
		[TestCase("what should I be doing", "incident_checklist")]
		[TestCase("incident weather", "incident_weather")]
		[TestCase("what is the wind doing", "incident_weather")]
		[TestCase("wind direction", "incident_weather")]
		[TestCase("incident notes", "incident_notes")]
		[TestCase("incident status", "incident_status")]
		[TestCase("size-up", "incident_status")]
		[TestCase("sitrep", "incident_status")]
		[TestCase("where do we stand", "incident_status")]
		public async Task IncidentQuestions_ClassifyToTheIncidentIntent(string text, string expected)
		{
			var result = await _classifier.ClassifyAsync(text);

			result.IntentName.Should().Be(expected);
			result.Confidence.Should().Be(1.0);
		}

		/// <summary>
		/// The incident block is evaluated before the department-wide query patterns, so these are the
		/// phrasings most at risk of being swallowed by it. They must keep their original meaning.
		/// </summary>
		[TestCase("who's available?", "who_available")]
		[TestCase("what units are available?", "units_available")]
		[TestCase("who's on scene at the fire", "call_responders")]
		[TestCase("who is responding", "call_responders")]
		[TestCase("who got dispatched", "call_dispatched")]
		[TestCase("who is John Smith", "personnel_lookup")]
		[TestCase("where is Engine 1", "personnel_lookup")]
		[TestCase("calls", "list_calls")]
		[TestCase("units", "list_units")]
		[TestCase("my status?", "my_status")]
		[TestCase("weather", "weather_alert")]
		[TestCase("help", "help")]
		public async Task ExistingIntents_AreNotSwallowedByTheIncidentPatterns(string text, string expected)
		{
			var result = await _classifier.ClassifyAsync(text);

			result.IntentName.Should().Be(expected);
		}

		[Test]
		public async Task LaneQuestion_CarriesTheLaneName()
		{
			var result = await _classifier.ClassifyAsync("who is working Division A");

			result.Parameters.Should().ContainKey("laneName");
			result.Parameters["laneName"].Should().Be("Division A");
		}

		[Test]
		public async Task UnassignedQuestion_MarksTheResourcePool()
		{
			var result = await _classifier.ClassifyAsync("who is unassigned");

			result.Parameters["laneName"].Should().Be("unassigned");
		}

		[Test]
		public async Task RoleQuestion_CarriesTheRoleAsked()
		{
			var result = await _classifier.ClassifyAsync("who is the safety officer");

			result.Parameters.Should().ContainKey("roleQuery");
			result.Parameters["roleQuery"].Should().Be("safety officer");
		}

		[Test]
		public async Task TimelineWindow_IsNormalizedToMinutes()
		{
			var minutes = await _classifier.ClassifyAsync("what happened in the last 30 minutes");
			minutes.Parameters["minutes"].Should().Be("30");

			var hours = await _classifier.ClassifyAsync("what happened in the last 2 hours");
			hours.Parameters["minutes"].Should().Be("120");

			var count = await _classifier.ClassifyAsync("show me the last 5 log entries");
			count.Parameters["count"].Should().Be("5");
		}

		[Test]
		public async Task ChecklistQuestion_CarriesTheNamedIncidentType()
		{
			var result = await _classifier.ClassifyAsync("checklist for a structure fire");

			result.Parameters["incidentType"].Should().Be("structure fire");
		}

		[Test]
		public async Task ScopedQuestion_CarriesTheCallReference()
		{
			var result = await _classifier.ClassifyAsync("PAR for 26-1");

			result.IntentName.Should().Be("incident_par");
			result.Parameters["callRef"].Should().Be("26-1");
		}
	}

	/// <summary>
	/// The ICS knowledge table: inferring an incident family from the call, and resolving one the
	/// commander named outright.
	/// </summary>
	[TestFixture]
	public class IcsPlaybookTests
	{
		[TestCase("Structure Fire", "Smoke showing from the second floor", IncidentPlaybookType.StructureFire)]
		[TestCase("Brush Fire", "Grass fire moving uphill", IncidentPlaybookType.Wildland)]
		[TestCase("MVA", "Two vehicles, one pin in", IncidentPlaybookType.VehicleAccident)]
		[TestCase("Medical", "Chest pain", IncidentPlaybookType.Ems)]
		[TestCase("MCI", "Bus accident with multiple patients", IncidentPlaybookType.MassCasualty)]
		[TestCase("HazMat", "Chemical spill at the plant", IncidentPlaybookType.HazMat)]
		[TestCase("Flood", "Storm damage across the valley", IncidentPlaybookType.NaturalDisaster)]
		[TestCase("Search", "Missing person, overdue hiker", IncidentPlaybookType.SearchAndRescue)]
		[TestCase("Technical Rescue", "Trench collapse", IncidentPlaybookType.TechnicalRescue)]
		[TestCase("Water Rescue", "Swift water, person in the water", IncidentPlaybookType.WaterRescue)]
		[TestCase("Shooting", "Active shooter reported", IncidentPlaybookType.ActiveThreat)]
		public void Infer_PicksTheIncidentFamilyFromTheCall(string type, string nature, IncidentPlaybookType expected)
		{
			var playbook = IcsPlaybooks.Infer(new Call { Type = type, NatureOfCall = nature, Name = type });

			playbook.Type.Should().Be(expected);
		}

		[Test]
		public void Infer_FallsBackToTheGeneralPlaybook_WhenTheCallSaysNothingUseful()
		{
			IcsPlaybooks.Infer(new Call { Type = "Other", Name = "Assist", NatureOfCall = "Public assist" })
				.Type.Should().Be(IncidentPlaybookType.General);

			IcsPlaybooks.Infer(null).Type.Should().Be(IncidentPlaybookType.General);
		}

		[Test]
		public void Infer_PrefersTheLongerKeyword()
		{
			// "vehicle fire" must beat a bare "fire" so a car fire isn't handled as a structure fire.
			IcsPlaybooks.Infer(new Call { Type = "Vehicle Fire", Name = "Vehicle Fire" })
				.Type.Should().Be(IncidentPlaybookType.VehicleAccident);
		}

		[TestCase("structure fire", IncidentPlaybookType.StructureFire)]
		[TestCase("mci", IncidentPlaybookType.MassCasualty)]
		[TestCase("wildland", IncidentPlaybookType.Wildland)]
		[TestCase("hazmat", IncidentPlaybookType.HazMat)]
		public void Resolve_MatchesAnExplicitlyNamedType(string text, IncidentPlaybookType expected)
		{
			IcsPlaybooks.Resolve(text).Type.Should().Be(expected);
		}

		[Test]
		public void Resolve_ReturnsNull_SoCallersCanFallBackToInference()
		{
			IcsPlaybooks.Resolve("something else entirely").Should().BeNull();
			IcsPlaybooks.Resolve(null).Should().BeNull();
		}

		[Test]
		public void ChecklistFor_AppendsTheUniversalItemsToTheTypeSpecificOnes()
		{
			var playbook = IcsPlaybooks.Get(IncidentPlaybookType.StructureFire);
			var checklist = IcsPlaybooks.ChecklistFor(playbook);

			checklist.Should().Contain(item => item.Contains("RIT"));
			checklist.Should().Contain(item => item.Contains("Span of control"));
			checklist.Count.Should().Be(playbook.Checklist.Count + IcsPlaybooks.General.Checklist.Count);
		}

		[Test]
		public void KeyRolesFor_IncludesTheUniversalPositionsWithoutDuplicating()
		{
			var roles = IcsPlaybooks.KeyRolesFor(IcsPlaybooks.Get(IncidentPlaybookType.StructureFire));

			roles.Should().Contain(IncidentRoleType.SafetyOfficer);
			roles.Should().Contain(IncidentRoleType.IncidentCommander);
			roles.Should().OnlyHaveUniqueItems();
		}

		[Test]
		public void EveryPlaybook_CarriesGuidanceAndPrompts()
		{
			foreach (var playbook in IcsPlaybooks.All)
			{
				playbook.DisplayName.Should().NotBeNullOrWhiteSpace();
				playbook.Checklist.Should().NotBeEmpty();
				playbook.Benchmarks.Should().NotBeEmpty();
				playbook.KeyRoles.Should().NotBeEmpty();
				playbook.SuggestedQuestions.Should().NotBeEmpty();
			}
		}
	}

	/// <summary>Radio shorthand for ICS positions must resolve; ordinary names must not.</summary>
	[TestFixture]
	public class IncidentRoleVocabularyTests
	{
		[TestCase("safety", IncidentRoleType.SafetyOfficer)]
		[TestCase("safety officer", IncidentRoleType.SafetyOfficer)]
		[TestCase("ops", IncidentRoleType.OperationsSectionChief)]
		[TestCase("operations section chief", IncidentRoleType.OperationsSectionChief)]
		[TestCase("staging manager", IncidentRoleType.StagingAreaManager)]
		[TestCase("pio", IncidentRoleType.PublicInformationOfficer)]
		[TestCase("ic", IncidentRoleType.IncidentCommander)]
		[TestCase("air ops", IncidentRoleType.AirOperationsBranchDirector)]
		[TestCase("decon", IncidentRoleType.DeconOfficer)]
		public void Resolve_MapsRadioShorthandToThePosition(string text, IncidentRoleType expected)
		{
			IncidentRoleVocabulary.Resolve(text).Should().Be(expected);
		}

		[Test]
		public void Resolve_DoesNotMatchInsideAnotherWord()
		{
			// "ic" must not match inside "medic", and "ops" must not match inside "logistics".
			IncidentRoleVocabulary.Resolve("medic").Should().BeNull();
			IncidentRoleVocabulary.Resolve("Jordan Rivera").Should().BeNull();
		}

		[Test]
		public void Resolve_PrefersTheLongerPosition()
		{
			IncidentRoleVocabulary.Resolve("medical branch director").Should().Be(IncidentRoleType.MedicalBranchDirector);
			IncidentRoleVocabulary.Resolve("deputy incident commander").Should().Be(IncidentRoleType.DeputyIncidentCommander);
		}

		[TestCase("rit")]
		[TestCase("ric")]
		[TestCase("rapid intervention team")]
		public void IsRapidIntervention_IsRecognizedSeparately_BecauseItIsALaneNotAPosition(string text)
		{
			IncidentRoleVocabulary.IsRapidIntervention(text).Should().BeTrue();
			IncidentRoleVocabulary.Resolve(text).Should().BeNull();
		}

		[Test]
		public void DisplayName_IsReadable()
		{
			IncidentRoleVocabulary.DisplayName(IncidentRoleType.HazMatGroupSupervisor).Should().Be("HazMat Group Supervisor");
		}
	}
}
