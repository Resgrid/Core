using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Department report exports (RMS plan section 5.6): the template contract a department authors, the file
	/// an agency receives, and the way a Workflow step and the schedule sweep (worker 45) get at it.
	/// </summary>
	[TestFixture]
	public class RecordsExportServiceTests
	{
		private const int Dept = 41;
		private FakeRmsStore _store;
		private List<RmsExportTemplate> _templates;
		private List<RmsExportRun> _runs;
		private Mock<IRmsExportTemplatesRepository> _templatesRepo;
		private Mock<IRmsExportRunsRepository> _runsRepo;
		private Mock<IRecordsService> _records;
		private Mock<IIncidentReportsService> _incidents;
		private Mock<IRecordsAuthorizationService> _authorization;
		private PassthroughRecordsProtection _protection;
		private Mock<IPdfProvider> _pdf;
		private List<RmsOperationalRecord> _finalized;
		private RecordsExportService _service;
		private bool _restricted;

		[SetUp]
		public void SetUp()
		{
			_store = new FakeRmsStore();
			_templates = new List<RmsExportTemplate>();
			_runs = new List<RmsExportRun>();
			_finalized = new List<RmsOperationalRecord>();
			_restricted = true;

			_templatesRepo = new Mock<IRmsExportTemplatesRepository>();
			_templatesRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExportTemplate>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExportTemplate t, CancellationToken c, bool f) => { _templates.Add(t); return t; });
			_templatesRepo.Setup(r => r.UpdateAsync(It.IsAny<RmsExportTemplate>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExportTemplate t, CancellationToken c, bool f) => { _templates.RemoveAll(x => x.RmsExportTemplateId == t.RmsExportTemplateId); _templates.Add(t); return t; });
			_templatesRepo.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _templates.FirstOrDefault(t => t.RmsExportTemplateId == id && t.DeletedOn == null));
			_templatesRepo.Setup(r => r.GetByKeyAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string key) => _templates.FirstOrDefault(t => t.TemplateKey == key && t.DeletedOn == null));
			_templatesRepo.Setup(r => r.GetForDepartmentAsync(Dept)).ReturnsAsync(() => _templates.Where(t => t.DeletedOn == null).ToList());
			_templatesRepo.Setup(r => r.GetDueAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync((DateTime now, int take) => _templates.Where(t => t.IsEnabled && t.ScheduleKind != 0 && t.NextRunOn <= now && t.DeletedOn == null).ToList());

			_runsRepo = new Mock<IRmsExportRunsRepository>();
			_runsRepo.Setup(r => r.InsertAsync(It.IsAny<RmsExportRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>())).ReturnsAsync((RmsExportRun run, CancellationToken c, bool f) => { _runs.Add(run); return run; });
			_runsRepo.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _runs.FirstOrDefault(x => x.RmsExportRunId == id));
			_runsRepo.Setup(r => r.GetWithDataAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _runs.FirstOrDefault(x => x.RmsExportRunId == id));
			_runsRepo.Setup(r => r.GetForTemplateAsync(Dept, It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((int d, string id, int take) => _runs.Where(x => x.TemplateId == id).ToList());
			_runsRepo.Setup(r => r.DeleteExpiredAsync(Dept, It.IsAny<DateTime>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);

			_records = new Mock<IRecordsService>();
			_records.Setup(r => r.GetAsync(Dept, It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((int d, string id, bool rev) => Aggregate(id));
			_incidents = new Mock<IIncidentReportsService>();
			_store.RecordsRepo.Setup(r => r.GetFinalizedSinceAsync(Dept, It.IsAny<DateTime>())).ReturnsAsync((int d, DateTime since) => _finalized.Where(r => r.FinalizedOn >= since).ToList());
			_store.RecordsRepo.Setup(r => r.GetByIdForDepartmentAsync(Dept, It.IsAny<string>())).ReturnsAsync((int d, string id) => _finalized.FirstOrDefault(r => r.RmsOperationalRecordId == id));
			var incidentsRepo = new Mock<IRmsIncidentReportsRepository>();
			incidentsRepo.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsIncidentReportQuery>())).ReturnsAsync(new List<RmsIncidentReport>());

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, PermissionTypes.ManageRecordReports)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, PermissionTypes.ExportRecords)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, PermissionTypes.ViewRestrictedRecords)).ReturnsAsync(() => _restricted);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);

			_protection = new PassthroughRecordsProtection();
			_pdf = new Mock<IPdfProvider>();
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>(), "Letter")).Returns(Encoding.ASCII.GetBytes("%PDF-export"));
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = Dept, Name = "Test FD", TimeZone = "Eastern Standard Time" });
			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupByIdAsync(12, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 12, Name = "Station 1" });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "Pat", LastName = id });

			_service = new RecordsExportService(_templatesRepo.Object, _runsRepo.Object, _records.Object, _incidents.Object, _store.RecordsRepo.Object, incidentsRepo.Object,
				_authorization.Object, _protection, new DomainEventOutboxService(_store.OutboxRepo.Object, Mock.Of<IEventAggregator>()), _store.AuditsRepo.Object,
				departments.Object, groups.Object, profiles.Object, _pdf.Object, _store.UnitOfWork.Object);
		}

		private RmsOperationalRecord Seed(string id, DateTime finalizedOn, string narrative = "=Engine 5 responded, \"quoted\"")
		{
			var record = new RmsOperationalRecord
			{
				RmsOperationalRecordId = id, DepartmentId = Dept, DefinitionKey = RmsDefinitionKeys.Training, RecordType = (int)RmsOperationalRecordType.Training, RecordNumber = "TRN-" + id,
				State = (int)RmsRecordState.Finalized, FinalizedOn = finalizedOn, StartedOn = finalizedOn.AddHours(-3), EndedOn = finalizedOn.AddHours(-1), StationGroupId = 12, AuthorUserId = "author", CreatedOn = finalizedOn.AddHours(-4), CurrentRevisionId = "rev-" + id
			};
			_finalized.Add(record);
			_store.Details.Add(new RmsOperationalRecordDetail { RmsOperationalRecordDetailId = "det-" + id, DepartmentId = Dept, RecordId = id, Narrative = narrative, Course = "Ropes", CaseNumber = "case-" + id, CallNumber = "C-1" });
			return record;
		}

		private RecordAggregate Aggregate(string id)
		{
			var record = _finalized.FirstOrDefault(r => r.RmsOperationalRecordId == id);
			if (record == null) return null;
			return new RecordAggregate
			{
				Record = record,
				Details = _store.Details.FirstOrDefault(d => d.RecordId == id),
				Participants = new List<RmsRecordParticipant> { new RmsRecordParticipant { UserId = "p1", DisplayNameSnapshot = "Pat One" } },
				Units = new List<RmsRecordUnitResponse> { new RmsRecordUnitResponse { UnitId = 5, UnitNameSnapshot = "Engine 5", Dispatched = record.StartedOn } },
				Protection = new ProtectedReadResult()
			};
		}

		private static RmsExportTemplate Template(params string[] columns)
		{
			return new RmsExportTemplate
			{
				TemplateKey = "state-runs", Name = "State runs", Format = (int)RmsExportFormat.Csv, Scope = (int)RmsExportScope.Window,
				ColumnsJson = JsonConvert.SerializeObject(columns.Length == 0 ? RecordsExportFieldCatalog.DefaultColumns : columns), IncludeHeader = true, Delimiter = ",",
				ScheduleKind = (int)RmsExportScheduleKind.Daily, ScheduleHourLocal = 6, IsEnabled = true
			};
		}

		[Test]
		public async Task Validation_rejects_unknown_columns_bad_keys_and_unacknowledged_sensitive_columns()
		{
			var bad = Template("record.number", "details.narrative", "nope.column");
			bad.TemplateKey = "Not A Key";
			var result = await _service.ValidateAsync(Dept, "admin", bad);

			result.IsValid.Should().BeFalse();
			result.Errors.Should().Contain(e => e.Contains("nope.column"));
			result.Errors.Should().Contain(e => e.Contains("key must be"));
			result.Errors.Should().Contain(e => e.Contains("Include narrative"));
			result.Errors.Should().Contain(e => e.Contains("Acknowledge"));

			var restrictedOnly = Template("record.number", "details.case_number");
			restrictedOnly.IncludeRestricted = true;
			restrictedOnly.EgressAcknowledgedOn = DateTime.UtcNow;
			_restricted = false;
			(await _service.ValidateAsync(Dept, "clerk", restrictedOnly)).Errors.Should().Contain(e => e.Contains("restricted-records grant"));
		}

		[Test]
		public async Task Save_normalizes_the_template_records_the_acknowledgement_and_schedules_the_next_run()
		{
			var template = Template("record.number", "details.narrative", "record.finalized_on");
			template.TemplateKey = " State-Runs ";
			template.IncludeNarrative = true;

			var saved = await _service.SaveAsync(Dept, "admin", template, acknowledgeEgress: true);

			saved.TemplateKey.Should().Be("state-runs");
			saved.EgressAcknowledgedOn.Should().NotBeNull();
			saved.EgressAcknowledgedByUserId.Should().Be("admin");
			saved.NextRunOn.Should().NotBeNull().And.BeAfter(DateTime.UtcNow);
			saved.RowVersion.Should().Be(1);
			_templates.Should().ContainSingle();
			_store.Audits.Should().ContainSingle(a => a.Purpose == "Export template created");

			// Widening a template past what was acknowledged drops the acknowledgement unless it is given again.
			saved.IncludeNarrative = false;
			saved.ColumnsJson = JsonConvert.SerializeObject(new[] { "record.number" });
			var narrowed = await _service.SaveAsync(Dept, "admin", saved, acknowledgeEgress: false);
			narrowed.EgressAcknowledgedOn.Should().BeNull();
		}

		[Test]
		public void Next_run_lands_on_the_department_local_hour_strictly_after_now()
		{
			var weekly = new RmsExportTemplate { ScheduleKind = (int)RmsExportScheduleKind.Weekly, ScheduleHourLocal = 6, ScheduleDayOfWeek = 1 };
			var now = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc); // a Monday, after 06:00 UTC
			var next = RecordsExportService.ComputeNextRun(weekly, now, null);
			next.Should().Be(new DateTime(2026, 9, 14, 6, 0, 0, DateTimeKind.Utc), "the same weekday later today has passed, so it is next week");

			var monthly = new RmsExportTemplate { ScheduleKind = (int)RmsExportScheduleKind.Monthly, ScheduleHourLocal = 2, ScheduleDayOfMonth = 1 };
			RecordsExportService.ComputeNextRun(monthly, now, null).Should().Be(new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc));

			RecordsExportService.ComputeNextRun(new RmsExportTemplate { ScheduleKind = (int)RmsExportScheduleKind.None }, now, null).Should().BeNull();
			RecordsExportService.ScheduleWindow(weekly, now).Should().Be((now.AddDays(-7), now));
		}

		[Test]
		public async Task Window_render_produces_a_guarded_csv_and_audits_every_record()
		{
			var now = DateTime.UtcNow;
			Seed("a", now.AddHours(-2));
			Seed("b", now.AddHours(-30), "Nothing to report");
			Seed("old", now.AddDays(-9));
			var template = Template("record.number", "record.type", "record.station_group_name", "record.author_name", "participants.names", "units.names", "details.narrative", "details.course");
			template.IncludeNarrative = true; template.EgressAcknowledgedOn = now; template.RmsExportTemplateId = "t1"; template.DepartmentId = Dept; template.WindowDays = 2;
			_templates.Add(template);

			var run = await _service.RenderAsync(Dept, template, new RecordsExportRequest { Trigger = RmsExportTrigger.Manual, ActingUserId = "admin", Purpose = "Manual export" });

			run.RecordCount.Should().Be(2, "the record finalized nine days ago is outside the two-day window");
			run.ContentType.Should().Be("text/csv");
			run.FileName.Should().StartWith("state-runs-").And.EndWith(".csv");
			run.Checksum.Should().Be(RecordSnapshotSerializer.Checksum(run.Data));
			var csv = Encoding.UTF8.GetString(run.Data).TrimStart('﻿');
			var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
			lines[0].Should().Be("record.number,record.type,record.station_group_name,record.author_name,participants.names,units.names,details.narrative,details.course");
			lines.Should().Contain(l => l.StartsWith("TRN-a,Training,Station 1,Pat author,Pat One,Engine 5,\"'=Engine 5 responded, \"\"quoted\"\"\",Ropes"), "a leading = is neutralized and quotes are doubled");
			_runs.Should().ContainSingle(r => r.RmsExportRunId == run.RmsExportRunId);
			_store.Audits.Where(a => a.Action == (int)RmsAccessAuditAction.Export).Select(a => a.RecordId).Should().BeEquivalentTo(new[] { "a", "b" });
			_protection.Writes.Should().Contain("export-run", "the stored bytes pass the ADP seam");
		}

		[Test]
		public async Task Restricted_columns_are_withheld_from_a_caller_without_the_grant_and_the_run_says_so()
		{
			var now = DateTime.UtcNow;
			Seed("a", now.AddHours(-2));
			var template = Template("record.number", "details.case_number");
			template.IncludeRestricted = true; template.EgressAcknowledgedOn = now; template.RmsExportTemplateId = "t2"; template.DepartmentId = Dept; template.Format = (int)RmsExportFormat.Json;
			_restricted = false;

			var run = await _service.RenderAsync(Dept, template, new RecordsExportRequest { ActingUserId = "clerk" });

			run.Redacted.Should().BeTrue();
			var json = JObject.Parse(Encoding.UTF8.GetString(run.Data));
			json["rows"][0]["details.case_number"].Value<string>().Should().Be(ProtectedDataEnvelope.RedactionValue);
			json["rows"][0]["record.number"].Value<string>().Should().Be("TRN-a");
			JObject.Parse(run.RedactedFieldsJson)["withheld_columns"].Values<string>().Should().Contain("details.case_number");
		}

		[Test]
		public async Task Workflow_resolution_renders_the_triggering_record_or_re_renders_a_scheduled_window()
		{
			var now = DateTime.UtcNow;
			Seed("a", now.AddHours(-2));
			var perRecord = Template("record.number", "record.kind");
			perRecord.Scope = (int)RmsExportScope.TriggeringRecord; perRecord.ScheduleKind = 0; perRecord.RmsExportTemplateId = "t3"; perRecord.DepartmentId = Dept; perRecord.Format = (int)RmsExportFormat.Pdf;
			_templates.Add(perRecord);

			var single = await _service.ResolveForWorkflowAsync(Dept, "t3", "a", RmsRecordKind.Operational, null, "wf-run-1");
			single.RecordCount.Should().Be(1);
			single.ContentType.Should().Be("application/pdf");
			single.WorkflowRunId.Should().Be("wf-run-1");
			_runs.Should().Contain(r => r.RmsExportRunId == single.RmsExportRunId, "a per-record render is stored for the run history");

			var scheduled = Template("record.number"); scheduled.RmsExportTemplateId = "t4"; scheduled.DepartmentId = Dept; scheduled.NextRunOn = now.AddMinutes(-5);
			_templates.Add(scheduled);
			var sweep = await _service.RunDueSchedulesAsync();
			sweep.RunsRendered.Should().Be(1);
			var stored = _runs.Single(r => r.TemplateId == "t4");
			_store.Outbox.Should().ContainSingle(o => o.EventName == "RecordExportScheduled");
			var payload = JObject.Parse(_store.Outbox.Single(o => o.EventName == "RecordExportScheduled").PayloadJson);
			payload["export"]["run_id"].Value<string>().Should().Be(stored.RmsExportRunId);
			payload["export"]["record_count"].Value<int>().Should().Be(1);
			payload["protection"]["is_redacted"].Value<bool>().Should().BeFalse();
			scheduled.NextRunOn.Should().BeAfter(now);
			scheduled.LastRunOn.Should().NotBeNull();

			var carried = await _service.ResolveForWorkflowAsync(Dept, "t4", null, null, stored.RmsExportRunId, "wf-run-2");
			carried.RmsExportRunId.Should().Be(stored.RmsExportRunId, "the step carries the scheduled run it was told about");
			carried.WindowStart.Should().Be(stored.WindowStart);
			carried.Data.Should().Equal(stored.Data, "re-rendering the same window gives the same file");
			_runs.Count(r => r.TemplateId == "t4").Should().Be(1, "a workflow re-render is not stored again");

			Func<Task> missing = () => _service.ResolveForWorkflowAsync(Dept, "t3", null, null, null, "wf-run-3");
			await missing.Should().ThrowAsync<InvalidOperationException>();
		}

		[Test]
		public void Csv_cells_are_rfc4180_quoted_and_formula_guarded()
		{
			RecordsExportRenderer.Cell("plain", ",").Should().Be("plain");
			RecordsExportRenderer.Cell("a,b", ",").Should().Be("\"a,b\"");
			RecordsExportRenderer.Cell("=SUM(A1)", ",").Should().Be("'=SUM(A1)");
			RecordsExportRenderer.Cell("+1", ";").Should().Be("'+1");
			RecordsExportRenderer.Cell("line\nbreak", ",").Should().Be("\"line\nbreak\"");
			RecordsExportRenderer.Cell(null, ",").Should().Be(string.Empty);
		}
	}
}
