using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services;
using Resgrid.Services.Wellness;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Tests.Wellness
{
	/// <summary>Who the dispatch data places on a call (the pure suggestion rule).</summary>
	[TestFixture]
	public class ResponderExposureSuggestionBuilderTests
	{
		private static readonly DateTime T0 = new DateTime(2026, 11, 20, 14, 0, 0, DateTimeKind.Utc);

		[Test]
		public void Statuses_unit_crews_and_check_ins_place_active_members_with_their_window()
		{
			var statuses = new[]
			{
				new ActionLog { UserId = "a", ActionTypeId = (int)ActionTypes.RespondingToScene, Timestamp = T0 },
				new ActionLog { UserId = "a", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0.AddMinutes(20) },
				new ActionLog { UserId = "b", ActionTypeId = (int)ActionTypes.NotResponding, Timestamp = T0 },
				new ActionLog { UserId = "gone", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0 }
			};
			var unitStates = new[]
			{
				new UnitState { UnitId = 9, Timestamp = T0.AddMinutes(5), Roles = new List<UnitStateRole> { new UnitStateRole { UserId = "C" } } },
				new UnitState { UnitId = 9, Timestamp = T0.AddMinutes(95), Roles = new List<UnitStateRole>() }
			};
			var checkIns = new[] { new CheckInRecord { UserId = "a", Timestamp = T0.AddMinutes(50) } };

			var candidates = ResponderExposureSuggestionBuilder.Build(statuses, unitStates, checkIns, new[] { "a", "b", "c" });

			candidates.Select(c => c.UserId).Should().BeEquivalentTo(new[] { "a", "c" }, "Not Responding places nobody, and only active members are addressed");
			var a = candidates.Single(c => c.UserId == "a");
			a.DurationMinutes.Should().Be(50);
			a.Bases.Should().BeEquivalentTo(new[] { ResponderExposureSuggestionBases.Status, ResponderExposureSuggestionBases.CheckIn });
			var c = candidates.Single(x => x.UserId == "c");
			c.DurationMinutes.Should().Be(90, "the crew shares the unit's response window");
			c.Bases.Should().Contain(new[] { ResponderExposureSuggestionBases.UnitCrew, ResponderExposureSuggestionBases.UnitResponse });
		}

		[Test]
		public void A_status_left_for_days_is_capped_and_a_single_fact_has_no_duration()
		{
			var statuses = new[]
			{
				new ActionLog { UserId = "a", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0 },
				new ActionLog { UserId = "a", ActionTypeId = 0, Timestamp = T0.AddDays(3) },
				new ActionLog { UserId = "b", ActionTypeId = (int)ActionTypes.OnScene, Timestamp = T0 }
			};
			var candidates = ResponderExposureSuggestionBuilder.Build(statuses, null, null, new[] { "a", "b" });
			candidates.Single(c => c.UserId == "a").DurationMinutes.Should().Be(MemberWellnessRules.MaximumSuggestedMinutes);
			candidates.Single(c => c.UserId == "b").DurationMinutes.Should().BeNull();
		}
	}

	/// <summary>The member's export documents and the officer report CSV.</summary>
	[TestFixture]
	public class WellnessDocumentsTests
	{
		[Test]
		public void The_export_prints_withheld_values_as_a_label_and_never_an_envelope_and_encodes_everything()
		{
			var document = new ExposureLogDocument
			{
				MemberName = "Mia <b>Member</b>",
				Entries = new List<RmsResponderExposure>
				{
					new RmsResponderExposure { UserId = "m", Category = (int)ResponderExposureCategories.Carcinogen, Status = (int)ResponderExposureStatuses.Confirmed,
						OccurredOn = new DateTime(2026, 1, 2, 3, 4, 0, DateTimeKind.Utc), Substance = ProtectedDataEnvelope.RedactionValue, PpeWorn = "rgdp:v1:abc", Narrative = "=HYPERLINK(\"x\")" }
				}
			};

			var csv = WellnessDocuments.ExposureLogCsv(document);
			var html = WellnessDocuments.ExposureLogHtml(document);

			csv.Should().NotContain("rgdp:").And.NotContain(ProtectedDataEnvelope.RedactionValue).And.Contain("Withheld (protected)");
			csv.Should().NotContain(",=HYPERLINK", "cells are formula-safe");
			html.Should().NotContain("<b>Member</b>").And.Contain("&lt;b&gt;Member&lt;/b&gt;").And.NotContain("rgdp:");
		}

		[Test]
		public void The_officer_report_has_one_row_per_member_with_counts_only()
		{
			var csv = WellnessDocuments.ReportCsv(new[]
			{
				new MemberExposureSummary { UserId = "m", Total = 3, CriticalIncidents = 1, ByCategory = new Dictionary<int, int> { [1] = 2, [4] = 1 }, ScreeningFlagged = true }
			}, id => "Mia", new Dictionary<string, string>());

			var lines = csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
			lines.Should().HaveCount(2);
			lines[1].Should().Be("\"Mia\",\"3\",\"1\",\"2\",\"0\",\"0\",\"0\",\"0\",,\"Yes\",\"0\"");
		}
	}

	/// <summary>
	/// Exposure, critical-incident and screening content is never indexed by Unified Search, never in the AI corpus and never in a
	/// department-wide export (§13b.5, §16.3). K1 (the knowledge indexer) is built in parallel; until it lands these guards pin that no
	/// search, AI or knowledge source can carry the wellness tables, and they keep holding once a knowledge source-kind enum exists.
	/// </summary>
	[TestFixture]
	public class WellnessExclusionTests
	{
		private static readonly string[] WellnessTypes =
		{
			nameof(RmsResponderExposure), nameof(RmsResponderScreeningFlag), nameof(RmsCallExposureFlag), "RmsResponderExposures", "RmsResponderScreeningFlags",
			// WL1: BBP exposure cases, injuries and duty periods, medical clearances (§13b.5, §16.3).
			nameof(RmsExposureIncident), nameof(RmsMemberInjury), nameof(RmsInjuryDutyPeriod), nameof(RmsMedicalClearance), "RmsExposureIncidents", "RmsMemberInjuries",
			"RmsInjuryDutyPeriods", "RmsMedicalClearances", nameof(IMemberHealthService)
		};

		private static DirectoryInfo Root()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(Path.Combine(root.FullName, "Resgrid.sln"))) root = root.Parent;
			Assert.That(root, Is.Not.Null);
			return root;
		}

		[Test]
		public void Unified_search_indexes_no_wellness_entity()
		{
			SearchEntityTypes.Indexed.Should().NotContain(t => t.IndexOf("Exposure", StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf("Wellness", StringComparison.OrdinalIgnoreCase) >= 0
				|| t.IndexOf("Screening", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		[TestCase("Core/Resgrid.Search")]
		[TestCase("Core/Resgrid.Ai")]
		[TestCase("Core/Resgrid.Llm")]
		[TestCase("Core/Resgrid.AdminAssist")]
		[TestCase("Core/Resgrid.Services/Search")]
		[TestCase("Core/Resgrid.Services/AdminAssist")]
		[TestCase("Core/Resgrid.Services/Records")]
		public void No_search_ai_or_records_export_source_reads_the_wellness_tables(string folder)
		{
			var directory = Path.Combine(Root().FullName, folder.Replace('/', Path.DirectorySeparatorChar));
			if (!Directory.Exists(directory))
				Assert.Inconclusive(folder + " is not in this tree.");
			foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
			{
				var text = System.IO.File.ReadAllText(file);
				foreach (var type in WellnessTypes)
					text.Should().NotContain(type, "{0} must never index, embed or export wellness content (§13b.5, §16.3)", file);
			}
		}

		[Test]
		public void A_knowledge_source_kind_can_never_name_wellness_content()
		{
			// K1 adds the knowledge indexer's source kinds; whatever enum or constant set carries them must not name wellness content.
			var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => (a.GetName().Name ?? string.Empty).StartsWith("Resgrid", StringComparison.Ordinal)).ToList();
			assemblies.Add(typeof(RmsResponderExposure).Assembly);
			assemblies.Add(typeof(MemberWellnessService).Assembly);
			foreach (var type in assemblies.Distinct().SelectMany(a => { try { return a.GetTypes(); } catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); } })
				.Where(t => t.Name.IndexOf("Knowledge", StringComparison.Ordinal) >= 0 && (t.Name.IndexOf("Source", StringComparison.Ordinal) >= 0 || t.Name.IndexOf("Corpus", StringComparison.Ordinal) >= 0)))
			{
				var names = type.IsEnum
					? Enum.GetNames(type)
					: type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral || f.IsInitOnly).Select(f => f.Name + "=" + f.GetValue(null));
				names.Should().NotContain(n => n.IndexOf("Exposure", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Wellness", StringComparison.OrdinalIgnoreCase) >= 0
					|| n.IndexOf("PeerSupport", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Injur", StringComparison.OrdinalIgnoreCase) >= 0
					|| n.IndexOf("Clearance", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("MemberHealth", StringComparison.OrdinalIgnoreCase) >= 0, type.FullName);
			}
		}

		[Test]
		public void The_protected_columns_are_cataloged_and_the_operational_ones_never_are()
		{
			var catalog = new ProtectedFieldCatalog();
			var exposures = catalog.GetForTable("RmsResponderExposures").Select(f => f.ColumnName).ToList();
			exposures.Should().BeEquivalentTo(new[] { "Substance", "Narrative", "PpeWorn", "Decontamination", "HealthDetails" });
			exposures.Should().NotIntersectWith(WellnessProtectedFields.OperationalExposureColumns, "category, date, member and call stay operational (§25.4 item 4c)");
			catalog.GetForTable("RmsResponderScreeningFlags").Select(f => f.ColumnName).Should().BeEquivalentTo(new[] { "PlanNotes" });
			catalog.GetForTable("RmsCallExposureFlags").Should().BeEmpty("a flag on a call is operational");
			catalog.GetForTable("RmsResponderExposures").Should().OnlyContain(f => f.AddedInCatalogVersion == WellnessProtectedFields.CatalogVersion && f.Family == "Personnel");
			WellnessProtectedFields.Exposures.Keys.Should().BeEquivalentTo(catalog.GetForTable("RmsResponderExposures").Select(f => f.FieldId));

			var bindings = AdpTableBindings.V1.Where(b => b.TableName == "RmsResponderExposures" || b.TableName == "RmsResponderScreeningFlags").ToList();
			bindings.Should().HaveCount(2);
			bindings.SelectMany(b => b.Columns).Select(c => c.FieldId).Should().BeEquivalentTo(WellnessProtectedFields.Exposures.Keys.Concat(WellnessProtectedFields.ScreeningFlags.Keys));
		}

		[Test]
		public void Wellness_settings_survive_a_protobuf_round_trip_including_the_default_retention()
		{
			var stored = ObjectSerialization.Serialize(new WellnessSettings { WellnessOfficerRoleId = 4, PeerTeamCertificationTypeId = 9, RetentionYears = 45 });
			var read = ObjectSerialization.Deserialize<WellnessSettings>(stored);
			read.WellnessOfficerRoleId.Should().Be(4);
			read.PeerTeamCertificationTypeId.Should().Be(9);
			read.RetentionYears.Should().Be(45);
			ObjectSerialization.Deserialize<WellnessSettings>(ObjectSerialization.Serialize(new WellnessSettings())).RetentionYears.Should().Be(30);
		}
	}

	/// <summary>Worker 91: every pass starts from its due-work query and a failing pass does not stop the others.</summary>
	[TestFixture]
	public class WellnessLogicTests
	{
		[Test]
		public async Task A_run_does_all_three_passes_and_counts_a_failure_without_stopping()
		{
			var wellness = new Mock<IMemberWellnessService>(MockBehavior.Strict);
			var now = DateTime.UtcNow;
			wellness.Setup(w => w.ProcessPendingFlagsAsync(now, It.IsAny<CancellationToken>())).ReturnsAsync(new WellnessRunResult { FlagsProcessed = 1, SuggestionsCreated = 3 });
			wellness.Setup(w => w.SendAnniversaryRemindersAsync(now, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
			wellness.Setup(w => w.EraseExpiredAsync(now, It.IsAny<CancellationToken>())).ReturnsAsync(2);

			var result = await WellnessLogic.Process(wellness.Object, now);

			result.SuggestionsCreated.Should().Be(3);
			result.EntriesErased.Should().Be(2);
			result.Failures.Should().Be(1);
		}

		[Test]
		public async Task An_idle_department_set_costs_three_queries_and_no_department_sweep()
		{
			var world = new WellnessTestWorld();
			var service = world.Service();

			var result = await WellnessLogic.Process(service, DateTime.UtcNow);

			result.FlagsProcessed.Should().Be(0);
			world.FlagsRepository.Verify(r => r.GetPendingAsync(It.IsAny<int>()), Times.Once);
			world.ExposuresRepository.Verify(r => r.GetDepartmentsWithDueAnniversariesAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
			world.ExposuresRepository.Verify(r => r.GetDepartmentsWithEntriesBeforeAsync(It.IsAny<DateTime>(), It.IsAny<int>()), Times.Once);
			world.Departments.Verify(d => d.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>()), Times.Never);
			world.SettingsService.Verify(s => s.GetWellnessSettingsAsync(It.IsAny<int>()), Times.Never);
		}
	}
}
