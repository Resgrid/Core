using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using static Resgrid.Tests.Rms.RmsPreventionHarness;

namespace Resgrid.Tests.Rms
{
	/// <summary>RMS-5 inspection programs, code sets, inspections, violations, re-inspection and the daily sweep (RMS plan section 4.3).</summary>
	[TestFixture]
	public class RecordsInspectionsServiceTests
	{
		private RmsPreventionHarness _h;
		private RmsOccupancy _occupancy;
		private RmsCodeSet _codeSet;
		private RmsCodeSection _exits;
		private RmsCodeSection _extinguishers;
		private RmsInspectionProgram _program;

		[SetUp]
		public async Task SetUp()
		{
			_h = new RmsPreventionHarness();
			_occupancy = _h.SeedOccupancy(occupancyType: 3);
			_codeSet = await _h.InspectionsService.SaveCodeSetAsync(Dept, Admin, new RmsCodeSet { Name = "IFC", Edition = "2021", IsActive = true });
			_exits = await _h.InspectionsService.SaveCodeSectionAsync(Dept, Admin, new RmsCodeSection { RmsCodeSetId = _codeSet.RmsCodeSetId, SectionNumber = "1031.2", Title = "Means of egress", DefaultSeverity = (int)RmsViolationSeverity.Critical, DefaultCorrectionDays = 1 });
			_extinguishers = await _h.InspectionsService.SaveCodeSectionAsync(Dept, Admin, new RmsCodeSection { RmsCodeSetId = _codeSet.RmsCodeSetId, SectionNumber = "906.1", Title = "Portable extinguishers", DefaultSeverity = (int)RmsViolationSeverity.Moderate, DefaultCorrectionDays = 30 });
			_program = await _h.InspectionsService.SaveProgramAsync(Dept, Admin, new RmsInspectionProgram { Name = "Annual life safety", FrequencyMonths = 12, OccupancyTypesCsv = "3,5", RmsCodeSetId = _codeSet.RmsCodeSetId, IsActive = true }, new List<RmsInspectionChecklistItem>
			{
				new RmsInspectionChecklistItem { Key = "exits", Text = "Exits unobstructed", RmsCodeSectionId = _exits.RmsCodeSectionId, Required = true },
				new RmsInspectionChecklistItem { Key = "ext", Text = "Extinguishers tagged", RmsCodeSectionId = _extinguishers.RmsCodeSectionId, Required = true },
				new RmsInspectionChecklistItem { Key = "signs", Text = "Address visible from street", Required = false }
			});
		}

		[Test]
		public async Task Completing_an_inspection_opens_violations_from_failed_items_and_raises_trigger_161_without_the_notes()
		{
			var inspection = await _h.InspectionsService.ScheduleAsync(Dept, Admin, _occupancy.RmsOccupancyId, _program.RmsInspectionProgramId, DateTime.UtcNow, Admin);
			inspection.InspectionNumber.Should().Be($"INSP-{DateTime.UtcNow.Year}-0001");
			await _h.InspectionsService.StartAsync(Dept, Admin, inspection.RmsInspectionId);

			var aggregate = await _h.InspectionsService.CompleteAsync(Dept, Admin, inspection.RmsInspectionId, new List<RmsInspectionItemResult>
			{
				new RmsInspectionItemResult { Key = "exits", Passed = false, Note = "Rear exit chained shut" },
				new RmsInspectionItemResult { Key = "ext", Passed = true },
				new RmsInspectionItemResult { Key = "signs", Passed = false }
			}, "Occupant cooperative; manager present.", "J. Manager");

			aggregate.Inspection.Result.Should().Be((int)RmsInspectionResult.Fail);
			aggregate.Inspection.State.Should().Be((int)RmsInspectionState.ReinspectionRequired);
			aggregate.Inspection.Notes.Should().Be("Occupant cooperative; manager present.", "plaintext restored after the seal");
			aggregate.Violations.Should().HaveCount(2);
			var exits = aggregate.Violations.Single(v => v.ChecklistItemKey == "exits");
			exits.Severity.Should().Be((int)RmsViolationSeverity.Critical);
			exits.RmsCodeSectionId.Should().Be(_exits.RmsCodeSectionId);
			exits.DueOn.Should().BeCloseTo(DateTime.UtcNow.AddDays(1), TimeSpan.FromMinutes(1));
			exits.Description.Should().Be("Rear exit chained shut");
			aggregate.Violations.Single(v => v.ChecklistItemKey == "signs").Severity.Should().Be((int)RmsViolationSeverity.Moderate, "no code section: the default severity applies");
			_occupancy.LastInspectedOn.Should().NotBeNull();

			var evt = _h.Outbox.Enqueued.Should().ContainSingle().Which.Envelope;
			evt.Trigger.Should().Be(WorkflowTriggerEventType.RecordInspectionCompleted);
			var payload = JObject.FromObject(evt.Payload);
			payload["inspection"]["violation_count"].Value<int>().Should().Be(2);
			payload["inspection"]["critical_violation_count"].Value<int>().Should().Be(1);
			payload["inspection"]["result"].Value<string>().Should().Be("Fail");
			payload.ToString().Should().NotContain("Occupant cooperative").And.NotContain("J. Manager");
			payload["record"]["kind"].Value<string>().Should().Be("Prevention");
			_h.Outbox.Dispatched.Should().HaveCount(1);
		}

		[Test]
		public async Task Required_items_must_be_inspected_and_a_clean_inspection_passes()
		{
			var inspection = await _h.InspectionsService.ScheduleAsync(Dept, Admin, _occupancy.RmsOccupancyId, _program.RmsInspectionProgramId, DateTime.UtcNow, null);
			Func<Task> incomplete = () => _h.InspectionsService.CompleteAsync(Dept, Admin, inspection.RmsInspectionId, new List<RmsInspectionItemResult> { new RmsInspectionItemResult { Key = "exits", Passed = true } }, null, null);
			(await incomplete.Should().ThrowAsync<ArgumentException>()).WithMessage("*ext*");

			var aggregate = await _h.InspectionsService.CompleteAsync(Dept, Admin, inspection.RmsInspectionId, new List<RmsInspectionItemResult> { new RmsInspectionItemResult { Key = "exits", Passed = true }, new RmsInspectionItemResult { Key = "ext", Passed = true } }, null, null);
			aggregate.Inspection.Result.Should().Be((int)RmsInspectionResult.Pass);
			aggregate.Inspection.State.Should().Be((int)RmsInspectionState.Completed);
			aggregate.Violations.Should().BeEmpty();
			await _h.InspectionsService.CloseAsync(Dept, Admin, inspection.RmsInspectionId);
			inspection.State.Should().Be((int)RmsInspectionState.Closed);
		}

		[Test]
		public async Task Reinspection_follows_the_violations_and_closing_waits_for_verification()
		{
			var inspection = await _h.InspectionsService.ScheduleAsync(Dept, Admin, _occupancy.RmsOccupancyId, _program.RmsInspectionProgramId, DateTime.UtcNow, null);
			await _h.InspectionsService.CompleteAsync(Dept, Admin, inspection.RmsInspectionId, new List<RmsInspectionItemResult> { new RmsInspectionItemResult { Key = "exits", Passed = false }, new RmsInspectionItemResult { Key = "ext", Passed = true } }, null, null);
			var violation = _h.Violations.Rows.Single();

			Func<Task> close = () => _h.InspectionsService.CloseAsync(Dept, Admin, inspection.RmsInspectionId);
			await close.Should().ThrowAsync<InvalidOperationException>();

			var child = await _h.InspectionsService.ScheduleReinspectionAsync(Dept, Admin, inspection.RmsInspectionId, DateTime.UtcNow.AddDays(1));
			child.ParentInspectionId.Should().Be(inspection.RmsInspectionId);
			violation.ReinspectionId.Should().Be(child.RmsInspectionId);

			await _h.InspectionsService.TransitionViolationAsync(Dept, Admin, violation.RmsViolationId, RmsViolationState.Corrected, null);
			Func<Task> waivedWithoutReason = () => _h.InspectionsService.TransitionViolationAsync(Dept, Admin, violation.RmsViolationId, RmsViolationState.Waived, null);
			await waivedWithoutReason.Should().ThrowAsync<InvalidOperationException>("Corrected cannot go to Waived");
			await _h.InspectionsService.TransitionViolationAsync(Dept, Admin, violation.RmsViolationId, RmsViolationState.Verified, null);
			violation.VerifiedByUserId.Should().Be(Admin);
			await _h.InspectionsService.CloseAsync(Dept, Admin, inspection.RmsInspectionId);
			await _h.InspectionsService.IssueNoticeAsync(Dept, Admin, inspection.RmsInspectionId, "NOV-77");
			inspection.NoticeReference.Should().Be("NOV-77");
		}

		[Test]
		public async Task The_sweep_generates_due_inspections_once_and_emits_overdue_violations_exactly_once()
		{
			var other = _h.SeedOccupancy("Office", "9 Main St", occupancyType: 1);
			var now = DateTime.UtcNow;
			(await _h.InspectionsService.GenerateDueInspectionsAsync(Dept, now)).Should().Be(1, "only the type-3 occupancy matches the program; it was never inspected");
			(await _h.InspectionsService.GenerateDueInspectionsAsync(Dept, now)).Should().Be(0, "an open inspection blocks a second");
			var generated = _h.Inspections.Rows.Single();
			generated.RmsOccupancyId.Should().Be(_occupancy.RmsOccupancyId);
			generated.InspectionNumber.Should().StartWith("INSP-");

			await _h.InspectionsService.CompleteAsync(Dept, Admin, generated.RmsInspectionId, new List<RmsInspectionItemResult> { new RmsInspectionItemResult { Key = "exits", Passed = false }, new RmsInspectionItemResult { Key = "ext", Passed = true } }, null, null);
			(await _h.InspectionsService.GenerateDueInspectionsAsync(Dept, now)).Should().Be(0, "ReinspectionRequired still counts as open");
			(await _h.InspectionsService.GenerateDueInspectionsAsync(Dept, now.AddMonths(13))).Should().Be(0, "a re-inspection is the path, not a new cycle, while it stays open");

			var violation = _h.Violations.Rows.Single();
			(await _h.InspectionsService.EmitOverdueViolationsAsync(Dept, now)).Should().Be(0, "due tomorrow");
			(await _h.InspectionsService.EmitOverdueViolationsAsync(Dept, now.AddDays(3))).Should().Be(1);
			(await _h.InspectionsService.EmitOverdueViolationsAsync(Dept, now.AddDays(4))).Should().Be(0, "emitted once");
			violation.OverdueEmittedOn.Should().NotBeNull();
			var evt = _h.Outbox.Enqueued.Last().Envelope;
			evt.Trigger.Should().Be(WorkflowTriggerEventType.RecordViolationOverdue);
			var payload = JObject.FromObject(evt.Payload);
			payload["violation"]["code_section_number"].Value<string>().Should().Be("1031.2");
			payload["violation"]["days_overdue"].Value<int>().Should().BeGreaterThanOrEqualTo(1);
			payload["violation"]["severity"].Value<string>().Should().Be("Critical");
			payload.ToString().Should().NotContain("Description");

			_h.DisabledFlags.Add(FeatureFlagKeys.RecordsPreventionInspections);
			(await _h.InspectionsService.GenerateDueInspectionsAsync(Dept, now.AddYears(2))).Should().Be(0, "a disabled module never sweeps");
		}

		[Test]
		public async Task Code_sections_import_from_csv_and_skip_duplicates_and_headers()
		{
			var created = await _h.InspectionsService.ImportCodeSectionsAsync(Dept, Admin, _codeSet.RmsCodeSetId,
				"section,title,text,severity,days\n1031.2,Means of egress,\"Exits shall be, at all times, unobstructed\",4,1\n315.3,Storage,,2,14\n\n907.8,\"Alarm testing\",Annual,3,30\n");
			created.Should().Be(2, "1031.2 already exists");
			var sections = await _h.InspectionsService.GetCodeSectionsAsync(Dept, Member, _codeSet.RmsCodeSetId);
			sections.Select(s => s.SectionNumber).Should().BeEquivalentTo(new[] { "1031.2", "906.1", "315.3", "907.8" });
			sections.Single(s => s.SectionNumber == "907.8").Title.Should().Be("Alarm testing");
			sections.Single(s => s.SectionNumber == "315.3").DefaultCorrectionDays.Should().Be(14);
			RecordsInspectionsService_CsvSplitPin();
		}

		private static void RecordsInspectionsService_CsvSplitPin()
		{
			Resgrid.Services.Records.RecordsInspectionsService.CsvSplit("a,\"b,c\",\"d\"\"e\"").Should().Equal("a", "b,c", "d\"e");
		}

		[Test]
		public async Task Only_prevention_administrators_change_inspection_data()
		{
			Func<Task> schedule = () => _h.InspectionsService.ScheduleAsync(Dept, Member, _occupancy.RmsOccupancyId, _program.RmsInspectionProgramId, DateTime.UtcNow, null);
			await schedule.Should().ThrowAsync<UnauthorizedAccessException>();
			(await _h.InspectionsService.GetProgramsAsync(Dept, Member, false)).Should().ContainSingle(p => p.Name == "Annual life safety");
			(await _h.InspectionsService.ListAsync(Dept, Member, new RmsInspectionQuery())).Should().BeEmpty();
		}
	}
}
