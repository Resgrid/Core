using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// The run reports an incident report author sees for the call: only Run/Callback records on that call that the author
	/// may view, never the report being written, voided, cancelled or deleted ones; each one shown is written to its access
	/// audit; content comes through the Records aggregate so protected text stays withheld.
	/// </summary>
	[TestFixture]
	public class RecordCallReportsServiceTests
	{
		private const int Dept = 3;
		private const int CallId = 55;
		private static readonly DateTime T0 = new DateTime(2026, 10, 1, 14, 0, 0, DateTimeKind.Utc);

		private Mock<IRmsOperationalRecordsRepository> _records;
		private Mock<IRecordsService> _recordsService;
		private Mock<IRecordsAuthorizationService> _authorization;
		private RecordCallReportsService _service;
		private List<RmsOperationalRecord> _rows;

		[SetUp]
		public void SetUp()
		{
			_rows = new List<RmsOperationalRecord>
			{
				Row("r1", RmsDefinitionKeys.Run, RmsRecordState.Finalized, T0.AddHours(1)),
				Row("r2", RmsDefinitionKeys.Callback, RmsRecordState.Draft, T0.AddHours(2)),
				Row("r3", RmsDefinitionKeys.Run, RmsRecordState.Voided, T0.AddHours(3)),
				Row("r4", RmsDefinitionKeys.Training, RmsRecordState.Finalized, T0.AddHours(4)),
				Row("mine", RmsDefinitionKeys.Run, RmsRecordState.Draft, T0.AddHours(5)),
				Row("hidden", RmsDefinitionKeys.Run, RmsRecordState.Finalized, T0.AddHours(6))
			};
			_records = new Mock<IRmsOperationalRecordsRepository>();
			_records.Setup(r => r.GetByCallAsync(Dept, CallId)).ReturnsAsync(() => _rows);

			_recordsService = new Mock<IRecordsService>();
			_recordsService.Setup(r => r.GetAsync(Dept, It.IsAny<string>(), false)).ReturnsAsync((int d, string id, bool rev) => Aggregate(_rows.Single(x => x.RmsOperationalRecordId == id)));

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync("author", Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanUserViewRecordAsync("author", It.IsAny<string>(), Dept)).ReturnsAsync((string u, string id, int d) => id != "hidden");

			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName> { new PersonName { UserId = "writer", FirstName = "Wren", LastName = "Writer" } });
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetAllGroupsForDepartmentAsync(Dept)).ReturnsAsync(new List<DepartmentGroup> { new DepartmentGroup { DepartmentGroupId = 2, Name = "Station 2" } });

			_service = new RecordCallReportsService(_records.Object, _recordsService.Object, _authorization.Object, departments.Object, groups.Object);
		}

		private static RmsOperationalRecord Row(string id, string definition, RmsRecordState state, DateTime modified) => new RmsOperationalRecord
		{
			RmsOperationalRecordId = id, DepartmentId = Dept, CallId = CallId, DefinitionKey = definition, State = (int)state, ModifiedOn = modified,
			AuthorUserId = "writer", StationGroupId = 2, DraftReference = "D-" + id, StartedOn = T0, EndedOn = T0.AddHours(1)
		};

		private static RecordAggregate Aggregate(RmsOperationalRecord record) => new RecordAggregate
		{
			Record = record,
			Details = new RmsOperationalRecordDetail { Narrative = "<p>Arrived to <strong>light smoke</strong>.</p><ul><li>Checked attic</li></ul>", Location = "1 Main St", InitialReport = ProtectedDataEnvelope.RedactionValue },
			Units = new List<RmsRecordUnitResponse> { new RmsRecordUnitResponse { UnitId = 5, UnitNameSnapshot = "Engine 5", Dispatched = T0.AddMinutes(1), OnScene = T0.AddMinutes(9), Released = T0.AddMinutes(40) } },
			Participants = new List<RmsRecordParticipant>
			{
				new RmsRecordParticipant { UserId = "a", DisplayNameSnapshot = "Ann Able", UnitId = 5, Role = "Officer" },
				new RmsRecordParticipant { UserId = "b", DisplayNameSnapshot = "Ben Baker", UnitId = 5 }
			},
			Protection = new ProtectedReadResult { RedactedFields = new List<string> { "InitialReport" } }
		};

		[Test]
		public async Task Lists_the_calls_visible_run_and_callback_reports_except_the_one_being_written()
		{
			var reports = await _service.GetForCallAsync(Dept, "author", CallId, "mine", "Looked up while writing report X");

			reports.Select(r => r.RecordId).Should().Equal("r2", "r1");
			_recordsService.Verify(r => r.RecordAccessAsync(Dept, "author", "r1", It.IsAny<string>(), RmsAccessAuditAction.Read, "Looked up while writing report X", null, RmsOriginClient.Web), Times.Once);
			_recordsService.Verify(r => r.RecordAccessAsync(Dept, "author", "hidden", It.IsAny<string>(), It.IsAny<RmsAccessAuditAction>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RmsOriginClient>()), Times.Never);
		}

		[Test]
		public async Task A_report_carries_its_units_crew_and_plain_narrative_and_withholds_protected_text()
		{
			var report = (await _service.GetForCallAsync(Dept, "author", CallId, null, "p")).Single(r => r.RecordId == "r1");

			report.IsFinal.Should().BeTrue();
			report.TypeName.Should().Be("Run");
			report.AuthorName.Should().Be("Wren Writer");
			report.StationName.Should().Be("Station 2");
			report.Narrative.Should().Be("Arrived to light smoke.\n- Checked attic");
			report.InitialReport.Should().BeNull("a redacted value is left out, never shown");
			report.ContentWithheld.Should().BeTrue();
			var unit = report.Units.Single();
			unit.Crew.Should().Be(2);
			unit.ReleasedOn.Should().Be(T0.AddMinutes(40));
			report.Personnel.Select(p => p.Name).Should().Equal("Ann Able", "Ben Baker");
		}

		[Test]
		public async Task An_inactive_member_sees_nothing()
		{
			(await _service.GetForCallAsync(Dept, "stranger", CallId, null, "p")).Should().BeEmpty();
			_records.Verify(r => r.GetByCallAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
		}

		[Test]
		public void Plain_text_narrative_keeps_paragraphs_and_list_items()
		{
			RecordNarrativeFormatter.ToPlainText("<p>One</p><p>Two<br>Three</p>").Should().Be("One\nTwo\nThree");
			RecordNarrativeFormatter.ToPlainText("No markup").Should().Be("No markup");
			RecordNarrativeFormatter.ToPlainText("<script>alert(1)</script><p>Safe</p>").Should().Be("Safe");
		}
	}
}
