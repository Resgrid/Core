using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Read-only NFIRS rendering and crosswalk (RMS-3, plan section 4.3): every value comes from something the
	/// department already holds, each field names its NERIS equivalent, a NERIS report's coverage is reported
	/// per field, the source-Call rule is enforced, another department's Call is not found, and no source
	/// outage turns into an exception.
	/// </summary>
	[TestFixture]
	public class RecordsNfirsLegacyServiceTests
	{
		private const int Dept = 42;
		private static readonly DateTime LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

		private Mock<ICallsService> _calls;
		private Mock<IUnitsService> _units;
		private Mock<IIncidentReportingService> _reporting;
		private Mock<IIncidentReportsService> _incidents;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<INerisProfileService> _neris;
		private Mock<IProtectedReadService> _protectedReads;
		private Call _call;
		private RecordsNfirsLegacyService _service;

		[SetUp]
		public void SetUp()
		{
			_call = new Call { CallId = 77, DepartmentId = Dept, Number = "2026-000123", IncidentNumber = "INC-9", Name = "Structure fire", Type = "Fire", Address = "1 Main St", NatureOfCall = "Smoke showing", LoggedOn = LoggedOn };
			_calls = new Mock<ICallsService>();
			_calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(() => _call);
			_calls.Setup(c => c.GetCallByIdAsync(78, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 78, DepartmentId = 99 });
			_units = new Mock<IUnitsService>();
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, 77)).ReturnsAsync(new List<UnitState>
			{
				new UnitState { UnitId = 5, State = (int)UnitStateTypes.Responding, Timestamp = LoggedOn.AddMinutes(2) },
				new UnitState { UnitId = 6, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(11) },
				new UnitState { UnitId = 5, State = (int)UnitStateTypes.OnScene, Timestamp = LoggedOn.AddMinutes(9) }
			});
			_reporting = new Mock<IIncidentReportingService>();
			_reporting.Setup(r => r.GetIncidentTimesReportAsync(Dept, 77)).ReturnsAsync(new IncidentTimesReport { CallId = 77, LastBenchmarkCompletedOn = LoggedOn.AddMinutes(40), CommandClosedOn = LoggedOn.AddMinutes(90), MutualAidResourceCount = 2 });
			_incidents = new Mock<IIncidentReportsService>();
			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.CanReadSourceCallAsync("officer", Dept, It.IsAny<Call>())).ReturnsAsync(true);
			_neris = new Mock<INerisProfileService>();
			_neris.Setup(n => n.GetProfileAsync(Dept)).ReturnsAsync(new RmsNerisProfile { DepartmentId = Dept, NerisEntityId = "FD24027000" });
			_neris.Setup(n => n.ResolveCrosswalkAsync(Dept, "incident_type", NerisCrosswalkSources.CallType, "Fire")).ReturnsAsync("FIRE||STRUCTURE_FIRE||RESIDENTIAL");
			_protectedReads = new Mock<IProtectedReadService>();
			_protectedReads.Setup(p => p.ResolveForReadAsync(Dept, It.IsAny<Call>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
			_service = new RecordsNfirsLegacyService(_calls.Object, _units.Object, _reporting.Object, _incidents.Object, _authorization.Object, _neris.Object, _protectedReads.Object);
		}

		private NfirsLegacyField Field(NfirsLegacyRendering r, string name) => r.Fields.Single(f => f.Name == name);

		[Test]
		public async Task Every_value_is_read_from_an_existing_source_and_names_its_neris_equivalent()
		{
			var r = await _service.RenderAsync(Dept, "officer", 77);

			r.ReadOnly.Should().BeTrue();
			r.CallNumber.Should().Be("2026-000123");
			Field(r, "FDID").Value.Should().Be("FD24027000");
			Field(r, "FDID").SourceSystem.Should().Be("NerisProfile");
			Field(r, "IncidentNumber").Value.Should().Be("INC-9", "the incident number is preferred over the call number, as the CSV export does");
			Field(r, "IncidentNumber").NerisFactKey.Should().Be(NerisFactKeys.IncidentNumber);
			Field(r, "AlarmDateTime").Value.Should().Be("2026-09-01T08:00:00Z");
			Field(r, "IncidentTypeCode").Value.Should().Be("Fire → FIRE||STRUCTURE_FIRE||RESIDENTIAL");
			Field(r, "IncidentTypeCode").SourceSystem.Should().Be("Crosswalk");
			Field(r, "ArrivalDateTime").Value.Should().Be("2026-09-01T08:09:00Z", "the earliest on-scene state is the arrival");
			Field(r, "ArrivalDateTime").NerisFactKey.Should().Be(NerisFactKeys.UnitTime(5, "on_scene"));
			Field(r, "ControlledDateTime").Value.Should().Be("2026-09-01T08:40:00Z");
			Field(r, "ControlledDateTime").SourceSystem.Should().Be("IncidentCommand");
			Field(r, "LastUnitClearedDateTime").Value.Should().Be("2026-09-01T09:30:00Z", "command close is the proxy when dispatch never closed the call");
			Field(r, "LastUnitClearedDateTime").SourceSystem.Should().Be("IncidentCommand");
			Field(r, "AidGivenOrReceived").Value.Should().Be("Received (2)");
			Field(r, "LocationAddress").NerisFactKey.Should().Be(NerisFactKeys.Location);
			Field(r, "IncidentName").NerisFactKey.Should().BeNull("NERIS has no incident name; it is department-only");
			Field(r, "ActionsTaken").Status.Should().Be(NfirsLegacyFieldStatus.NotCaptured);
			Field(r, "PropertyUse").Status.Should().Be(NfirsLegacyFieldStatus.NotCaptured);
			r.Fields.Should().OnlyContain(f => f.NerisPopulated == null, "no NERIS report exists for the call");
			r.IncidentReportId.Should().BeNull();

			r.Summary.TotalFields.Should().Be(r.Fields.Count);
			r.Summary.NotCaptured.Should().Be(3);
			r.Summary.RequiredMissing.Should().Be(0);
			r.Summary.CrosswalkedToNeris.Should().Be(r.Fields.Count(f => f.NerisFactKey != null || f.NerisSection != null));
		}

		[Test]
		public async Task Dispatch_close_wins_over_command_close_and_an_unmapped_type_is_missing()
		{
			_call.ClosedOn = LoggedOn.AddMinutes(75);
			_call.Type = "Odd";

			var r = await _service.RenderAsync(Dept, "officer", 77);

			Field(r, "LastUnitClearedDateTime").Value.Should().Be("2026-09-01T09:15:00Z");
			Field(r, "LastUnitClearedDateTime").SourceSystem.Should().Be("Calls");
			var type = Field(r, "IncidentTypeCode");
			type.Status.Should().Be(NfirsLegacyFieldStatus.Missing, "an unmapped local type is a crosswalk gap the department must close");
			type.Value.Should().Be("Odd");
			r.Summary.RequiredMissing.Should().Be(1);
		}

		[Test]
		public async Task An_existing_neris_report_reports_per_field_coverage()
		{
			_incidents.Setup(i => i.GetForCallAsync(Dept, 77)).ReturnsAsync(new IncidentReportAggregate
			{
				Report = new RmsIncidentReport { RmsIncidentReportId = "rep-1", DepartmentId = Dept, CallId = 77, RecordNumber = "INC-2026-0001", State = (int)RmsRecordState.Finalized, ReportingEntityId = "FD24027000" },
				Facts = new List<RmsSourceFact>
				{
					new RmsSourceFact { FactKey = NerisFactKeys.CallCreate, SourceValue = "2026-09-01T08:00:00Z" },
					new RmsSourceFact { FactKey = NerisFactKeys.IncidentNumber, SourceValue = "INC-9", CurrentValue = "INC-9" },
					new RmsSourceFact { FactKey = NerisFactKeys.Location, SourceValue = "1 Main St", CurrentValue = "" }
				},
				Types = new List<RmsIncidentType> { new RmsIncidentType { TypeCode = "FIRE||STRUCTURE_FIRE||RESIDENTIAL" } },
				Units = new List<RmsUnitResponse> { new RmsUnitResponse { UnitId = 5, OnSceneOn = LoggedOn.AddMinutes(9), StationGroupIdSnapshot = 3 } },
				Narrative = new RmsNarrative { Narrative = "Officer narrative" }
			});

			var r = await _service.RenderAsync(Dept, "officer", 77);

			r.IncidentReportId.Should().Be("rep-1");
			r.IncidentReportNumber.Should().Be("INC-2026-0001");
			r.IncidentReportState.Should().Be("Finalized");
			Field(r, "FDID").NerisPopulated.Should().BeTrue();
			Field(r, "AlarmDateTime").NerisPopulated.Should().BeTrue();
			Field(r, "IncidentNumber").NerisPopulated.Should().BeTrue();
			Field(r, "LocationAddress").NerisPopulated.Should().BeTrue("an empty correction falls back to the source value");
			Field(r, "IncidentTypeCode").NerisPopulated.Should().BeTrue();
			Field(r, "ArrivalDateTime").NerisPopulated.Should().BeTrue();
			Field(r, "Station").NerisPopulated.Should().BeTrue();
			Field(r, "NatureOfCall").NerisPopulated.Should().BeTrue();
			Field(r, "LastUnitClearedDateTime").NerisPopulated.Should().BeFalse();
			Field(r, "AidGivenOrReceived").NerisPopulated.Should().BeFalse();
			Field(r, "ActionsTaken").NerisPopulated.Should().BeFalse();
			Field(r, "IncidentName").NerisPopulated.Should().BeNull("there is nothing in NERIS to be populated");
			r.Summary.CrosswalkedAndPopulated.Should().Be(r.Fields.Count(f => f.NerisPopulated == true));
		}

		[Test]
		public async Task Another_departments_call_is_not_found_and_an_unauthorized_viewer_is_refused()
		{
			(await _service.RenderAsync(Dept, "officer", 78)).Should().BeNull();
			(await _service.RenderAsync(Dept, "officer", 0)).Should().BeNull();
			_authorization.Verify(a => a.CanReadSourceCallAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<Call>()), Times.Never, "a foreign call is not found before any authorization question is asked");

			_authorization.Setup(a => a.CanReadSourceCallAsync("stranger", Dept, It.IsAny<Call>())).ReturnsAsync(false);
			Func<Task> render = () => _service.RenderAsync(Dept, "stranger", 77);
			await render.Should().ThrowAsync<UnauthorizedAccessException>();
			_units.Verify(u => u.GetUnitStatesForCallAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never, "nothing is read for an unauthorized viewer");
		}

		[Test]
		public async Task Source_outages_degrade_into_notes_rather_than_failures()
		{
			_units.Setup(u => u.GetUnitStatesForCallAsync(Dept, 77)).ThrowsAsync(new TimeoutException());
			_reporting.Setup(r => r.GetIncidentTimesReportAsync(Dept, 77)).ThrowsAsync(new TimeoutException());
			_incidents.Setup(i => i.GetForCallAsync(Dept, 77)).ThrowsAsync(new TimeoutException());
			_neris.Setup(n => n.GetProfileAsync(Dept)).ThrowsAsync(new TimeoutException());
			_neris.Setup(n => n.ResolveCrosswalkAsync(Dept, "incident_type", NerisCrosswalkSources.CallType, "Fire")).ThrowsAsync(new TimeoutException());

			var r = await _service.RenderAsync(Dept, "officer", 77);

			r.Notes.Should().HaveCount(5);
			Field(r, "FDID").Status.Should().Be(NfirsLegacyFieldStatus.Missing);
			Field(r, "ArrivalDateTime").Status.Should().Be(NfirsLegacyFieldStatus.Missing);
			Field(r, "AlarmDateTime").Status.Should().Be(NfirsLegacyFieldStatus.Populated, "the Call itself was readable");
		}

		[Test]
		public void The_service_exposes_no_write_path()
		{
			typeof(IRecordsNfirsLegacyService).GetMethods().Select(m => m.Name).Should().Equal("RenderAsync");
		}
	}
}
