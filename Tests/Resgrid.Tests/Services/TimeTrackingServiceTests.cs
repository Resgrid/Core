using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Invoicing;

namespace Resgrid.Tests.Services
{
	/// <summary>Workforce &amp; Business Operations plan Phase C (deployment core): DTR numbering, prefill, validation, lifecycle, expenses and export.</summary>
	[TestFixture]
	public class TimeTrackingServiceTests
	{
		private const int DeptId = 7;
		private const string Crew = "alice";
		private const string Approver = "chief";
		private static readonly DateTime Day = new DateTime(2026, 9, 21);

		private Mock<IDeploymentRepository> _deployments;
		private Mock<IDeploymentPersonnelRepository> _personnel;
		private Mock<IDeploymentUnitRepository> _units;
		private Mock<IDeploymentEquipmentRepository> _equipment;
		private Mock<IDeploymentTimeReportRepository> _reports;
		private Mock<IDeploymentTimeEntryRepository> _entries;
		private Mock<IDeploymentExpenseRepository> _expenses;
		private Mock<IDeploymentAttachmentRepository> _attachments;
		private Mock<ITimeReportNumberSequenceRepository> _sequence;
		private Mock<IDeploymentService> _deploymentService;
		private Mock<IDepartmentsService> _departments;
		private Mock<IUserProfileService> _profiles;
		private Mock<IEventAggregator> _events;
		private Mock<IPdfProvider> _pdf;
		private List<AuditEvent> _audits;
		private List<DeploymentTimeReport> _storedReports;
		private List<DeploymentTimeEntry> _storedEntries;
		private List<DeploymentExpense> _storedExpenses;
		private List<DeploymentAttachment> _storedAttachments;
		private Deployment _deployment;
		private int _nextNumber;
		private TimeTrackingService _service;

		[SetUp]
		public void SetUp()
		{
			_deployments = new Mock<IDeploymentRepository>();
			_personnel = new Mock<IDeploymentPersonnelRepository>();
			_units = new Mock<IDeploymentUnitRepository>();
			_equipment = new Mock<IDeploymentEquipmentRepository>();
			_reports = new Mock<IDeploymentTimeReportRepository>();
			_entries = new Mock<IDeploymentTimeEntryRepository>();
			_expenses = new Mock<IDeploymentExpenseRepository>();
			_attachments = new Mock<IDeploymentAttachmentRepository>();
			_sequence = new Mock<ITimeReportNumberSequenceRepository>();
			_deploymentService = new Mock<IDeploymentService>();
			_departments = new Mock<IDepartmentsService>();
			_profiles = new Mock<IUserProfileService>();
			_events = new Mock<IEventAggregator>();
			_pdf = new Mock<IPdfProvider>();
			_audits = new List<AuditEvent>();
			_storedReports = new List<DeploymentTimeReport>();
			_storedEntries = new List<DeploymentTimeEntry>();
			_storedExpenses = new List<DeploymentExpense>();
			_storedAttachments = new List<DeploymentAttachment>();
			_nextNumber = 1;

			_deployment = new Deployment
			{
				DeploymentId = "dep-1", DepartmentId = DeptId, Name = "Ridge Fire", Status = (int)DeploymentStatuses.Active, IncidentNumber = "CA-BTU-1", ResourceOrderNumber = "O-1", RequestNumber = "E-12", CostCode = "CC-1", Currency = "USD", LocalTimeZoneId = "UTC",
				Units = { new DeploymentUnit { DeploymentUnitId = "unit-1", DeploymentId = "dep-1", DepartmentId = DeptId, UnitId = 1, UnitName = "Engine 1" } },
				Personnel =
				{
					new DeploymentPersonnel { DeploymentPersonnelId = "per-1", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = Crew, DeploymentUnitId = "unit-1", DisplayName = "Alice Smith", CertificationCode = "ENGB" },
					new DeploymentPersonnel { DeploymentPersonnelId = "per-2", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "bob", DeploymentUnitId = "unit-1", DisplayName = "Bob Jones", RemovedOn = DateTime.UtcNow }
				},
				Equipment = { new DeploymentEquipment { DeploymentEquipmentId = "eq-1", DeploymentId = "dep-1", DepartmentId = DeptId, FreeTextName = "Pump" } }
			};
			_deploymentService.Setup(s => s.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(() => _deployment);
			_deploymentService.Setup(s => s.SaveAttachmentAsync(It.IsAny<DeploymentAttachment>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DeploymentAttachment a, string _, string __, string ___, CancellationToken ____) => { a.DeploymentAttachmentId = _storedAttachments.Count + 1; _storedAttachments.Add(a); return a; });
			_deployments.Setup(r => r.GetByIdForDepartmentAsync("dep-1", DeptId)).ReturnsAsync(() => _deployment);
			_departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Test County Fire", TimeZone = "UTC" });
			_profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool _) => new UserProfile { UserId = id, FirstName = id, LastName = "Smith" });
			_events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));
			_pdf.Setup(p => p.ConvertHtmlToPdf(It.IsAny<string>())).Returns<string>(html => System.Text.Encoding.UTF8.GetBytes(html));
			_sequence.Setup(s => s.GetNextNumberAsync(DeptId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _nextNumber++);

			_reports.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeReport>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentTimeReport t, CancellationToken _, bool __) => { t.DeploymentTimeReportId ??= Guid.NewGuid().ToString(); _storedReports.RemoveAll(x => x.DeploymentTimeReportId == t.DeploymentTimeReportId); _storedReports.Add(t); return t; });
			_reports.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _storedReports.FirstOrDefault(t => t.DeploymentTimeReportId == id));
			_reports.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedReports.Where(t => t.DeploymentId == id && !t.IsDeleted).OrderBy(t => t.ReportDate).ToList());
			_reports.Setup(r => r.GetByDeploymentAndDateAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync((string id, DateTime date) => _storedReports.FirstOrDefault(t => t.DeploymentId == id && t.ReportDate == date.Date && t.Status != (int)DeploymentTimeReportStatuses.Void));
			_reports.Setup(r => r.GetUnbilledApprovedAsync(DeptId, It.IsAny<string>())).ReturnsAsync((int _, string id) => _storedReports.Where(t => t.Status == (int)DeploymentTimeReportStatuses.Approved && t.InvoiceId == null && (id == null || t.DeploymentId == id)).ToList());
			_entries.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentTimeEntry>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentTimeEntry e, CancellationToken _, bool __) => { e.DeploymentTimeEntryId ??= Guid.NewGuid().ToString(); _storedEntries.Add(e); return e; });
			_entries.Setup(r => r.GetByReportAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedEntries.Where(e => e.DeploymentTimeReportId == id).OrderBy(e => e.SortOrder).ToList());
			_entries.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedEntries.Where(e => e.DeploymentId == id).ToList());
			_entries.Setup(r => r.DeleteByReportAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, CancellationToken _) => _storedEntries.RemoveAll(e => e.DeploymentTimeReportId == id));
			_expenses.Setup(r => r.SaveOrUpdateAsync(It.IsAny<DeploymentExpense>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DeploymentExpense e, CancellationToken _, bool __) => { e.DeploymentExpenseId ??= Guid.NewGuid().ToString(); _storedExpenses.RemoveAll(x => x.DeploymentExpenseId == e.DeploymentExpenseId); _storedExpenses.Add(e); return e; });
			_expenses.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _storedExpenses.FirstOrDefault(e => e.DeploymentExpenseId == id));
			_expenses.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string id) => _storedExpenses.Where(e => e.DeploymentId == id && !e.IsDeleted).ToList());

			_service = new TimeTrackingService(_deployments.Object, _personnel.Object, _units.Object, _equipment.Object, _reports.Object, _entries.Object, _expenses.Object, _attachments.Object, _sequence.Object,
				_deploymentService.Object, _departments.Object, _profiles.Object, new Mock<IUnitsService>().Object, _events.Object, _pdf.Object, null);
		}

		private static DeploymentTimeEntry Entry(string subject, int hourStart, int hourEnd, DeploymentTimeEntryTypes type = DeploymentTimeEntryTypes.Deployment, int unpaidBreak = 30, DateTime? day = null)
		{
			var d = day ?? Day;
			return new DeploymentTimeEntry
			{
				DeploymentPersonnelId = subject.StartsWith("per") ? subject : null, DeploymentUnitId = subject.StartsWith("unit") ? subject : null, DeploymentEquipmentId = subject.StartsWith("eq") ? subject : null,
				EntryType = (int)type, StartTime = d.AddHours(hourStart), EndTime = d.AddHours(hourEnd), UnpaidBreakMinutes = unpaidBreak
			};
		}

		[Test]
		public async Task Creating_a_report_allocates_the_number_copies_identifiers_and_prefills_active_subjects()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);

			report.ReportNumber.Should().Be(1);
			report.Status.Should().Be((int)DeploymentTimeReportStatuses.Draft);
			report.IncidentNumber.Should().Be("CA-BTU-1");
			report.ResourceOrderNumber.Should().Be("O-1");
			report.RequestNumber.Should().Be("E-12");
			report.CostCode.Should().Be("CC-1");
			report.Entries.Select(e => e.SubjectId).Should().BeEquivalentTo(new[] { "per-1", "unit-1", "eq-1" }, "removed members are not prefilled");
			report.Entries.Should().OnlyContain(e => e.EntryType == (int)DeploymentTimeEntryTypes.Deployment && e.EndTime > e.StartTime);
			report.Entries.Single(e => e.SubjectId == "per-1").CertificationCode.Should().Be("ENGB");
			report.Entries.Single(e => e.SubjectId == "unit-1").CrewSizeSnapshot.Should().Be(1);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.TimeReportCreated);

			(await FluentActions.Awaiting(() => _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_date_exists");
			var next = await _service.CreateTimeReportAsync("dep-1", DeptId, Day.AddDays(1), Crew, null, null);
			next.ReportNumber.Should().Be(2, "numbers come from the atomic sequence");
		}

		[Test]
		public async Task Prefill_copies_the_previous_report_span_for_the_same_subject()
		{
			var first = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			await _service.SaveTimeEntriesAsync(first.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry> { Entry("per-1", 6, 18, unpaidBreak: 45), Entry("unit-1", 6, 18) }, Crew, null, null);

			var second = await _service.CreateTimeReportAsync("dep-1", DeptId, Day.AddDays(1), Crew, null, null);
			var alice = second.Entries.Single(e => e.SubjectId == "per-1");
			alice.StartTime.Should().Be(Day.AddDays(1).AddHours(6));
			alice.EndTime.Should().Be(Day.AddDays(1).AddHours(18));
			alice.UnpaidBreakMinutes.Should().Be(45);
			second.Entries.Single(e => e.SubjectId == "eq-1").StartTime.Hour.Should().Be(8, "no prior span falls back to the default day");
		}

		[Test]
		public async Task Validation_flags_overlaps_inverted_spans_foreign_subjects_and_break_and_travel_rules()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			var result = await _service.SaveTimeEntriesAsync(report.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry>
			{
				Entry("per-1", 6, 14), Entry("per-1", 13, 20, DeploymentTimeEntryTypes.Standby), Entry("per-1", 22, 21), Entry("per-9", 6, 8),
				Entry("unit-1", 6, 14, unpaidBreak: 0), Entry("eq-1", 0, 13, DeploymentTimeEntryTypes.Travel, 0)
			}, Crew, null, null);

			result.Validation.IsValid.Should().BeFalse();
			result.Validation.Errors.Select(e => e.Code).Should().Contain(new[] { TimeReportValidation.Overlap, TimeReportValidation.EndBeforeStart, TimeReportValidation.SubjectNotOnRoster });
			result.Validation.Warnings.Select(w => w.Code).Should().Contain(new[] { TimeReportValidation.BreakRule, TimeReportValidation.LongTravel });
			result.Report.Entries.Should().HaveCount(3, "a failed batch leaves the stored prefill untouched");
			_audits.Should().NotContain(a => a.Type == AuditLogTypes.TimeReportUpdated);
		}

		[Test]
		public async Task A_valid_batch_replaces_entries_and_the_subject_id_decides_the_type()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			var standby = Entry("per-1", 14, 20, DeploymentTimeEntryTypes.Standby, 0);
			standby.SubjectType = (int)DeploymentTimeSubjectTypes.Unit;
			standby.DeploymentUnitId = "unit-1";

			var result = await _service.SaveTimeEntriesAsync(report.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry> { Entry("per-1", 6, 14), standby, Entry("unit-1", 6, 20, unpaidBreak: 60) }, Crew, null, null);

			result.Validation.IsValid.Should().BeTrue();
			result.Report.Entries.Should().HaveCount(3);
			var alice = result.Report.Entries.Where(e => e.DeploymentPersonnelId == "per-1").ToList();
			alice.Should().HaveCount(2);
			alice.Should().OnlyContain(e => e.SubjectType == (int)DeploymentTimeSubjectTypes.Personnel && e.DeploymentUnitId == null);
			alice.Sum(e => e.Hours).Should().Be(13.5m);
			result.Report.Entries.Single(e => e.DeploymentUnitId == "unit-1").CrewSizeSnapshot.Should().Be(1);
			result.Report.Entries.Select(e => e.SortOrder).Should().BeEquivalentTo(new[] { 0, 1, 2 });
		}

		[Test]
		public async Task Lifecycle_submit_approve_void_publishes_and_locks()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);

			(await FluentActions.Awaiting(() => _service.ApproveTimeReportAsync(report.DeploymentTimeReportId, DeptId, Approver, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_status_transition_invalid");

			var submitted = await _service.SubmitTimeReportAsync(report.DeploymentTimeReportId, DeptId, Crew, null, null);
			submitted.Validation.IsValid.Should().BeTrue();
			submitted.Report.Status.Should().Be((int)DeploymentTimeReportStatuses.Submitted);
			submitted.Report.SubmittedByUserId.Should().Be(Crew);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.TimeReportSubmitted);

			var approved = await _service.ApproveTimeReportAsync(report.DeploymentTimeReportId, DeptId, Approver, null, null);
			approved.Status.Should().Be((int)DeploymentTimeReportStatuses.Approved);
			approved.ApprovedByUserId.Should().Be(Approver);
			approved.IsEditable.Should().BeFalse();
			(await _service.GetUnbilledApprovedReportsAsync(DeptId)).Should().ContainSingle(r => r.DeploymentTimeReportId == report.DeploymentTimeReportId);

			(await FluentActions.Awaiting(() => _service.SaveTimeEntriesAsync(report.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry>(), Crew, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");

			var voided = await _service.VoidTimeReportAsync(report.DeploymentTimeReportId, DeptId, "duplicate", Approver, null, null);
			voided.Status.Should().Be((int)DeploymentTimeReportStatuses.Void);
			voided.Notes.Should().Contain("duplicate");
			(await FluentActions.Awaiting(() => _service.VoidTimeReportAsync(report.DeploymentTimeReportId, DeptId, null, Approver, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_status_transition_invalid");
			(await _service.GetUnbilledApprovedReportsAsync(DeptId)).Should().BeEmpty();
		}

		[Test]
		public async Task Submit_refuses_a_report_that_fails_validation()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			_storedEntries.Clear();

			var result = await _service.SubmitTimeReportAsync(report.DeploymentTimeReportId, DeptId, Crew, null, null);
			result.Validation.Errors.Should().ContainSingle(e => e.Code == TimeReportValidation.NoEntries);
			result.Report.Status.Should().Be((int)DeploymentTimeReportStatuses.Draft);
		}

		[Test]
		public async Task Signatures_stamp_the_contractor_and_the_customer_signer()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			var signed = await _service.SignTimeReportAsync(report.DeploymentTimeReportId, DeptId, true, "  J. Agency ", Crew, null, null);

			signed.ContractorSignedByUserId.Should().Be(Crew);
			signed.ContractorSignedOn.Should().NotBeNull();
			signed.CustomerSignerName.Should().Be("J. Agency");
			signed.CustomerSignedOn.Should().NotBeNull();

			var html = await _service.RenderTimeReportHtmlAsync(report.DeploymentTimeReportId, DeptId);
			html.Should().Contain("J. Agency").And.Contain("alice Smith").And.Contain("Alice Smith").And.Contain("CA-BTU-1");
			var pdf = await _service.GenerateTimeReportPdfAsync(report.DeploymentTimeReportId, DeptId, Crew, null, null);
			pdf.AttachmentType.Should().Be((int)DeploymentAttachmentTypes.TimeReportPdf);
			pdf.FileName.Should().Be("dtr-1-20260921.pdf");
		}

		[Test]
		public async Task Expenses_default_currency_file_receipts_and_lock_behind_approved_reports()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			var expense = await _service.SaveExpenseAsync(new DeploymentExpense { DeploymentId = "dep-1", DepartmentId = DeptId, DeploymentTimeReportId = report.DeploymentTimeReportId, ExpenseType = (int)DeploymentExpenseTypes.Fuel, Amount = 88.40m, Description = " diesel " },
				new byte[] { 1, 2, 3 }, "receipt.jpg", "image/jpeg", Crew, null, null);

			expense.Currency.Should().Be("USD");
			expense.Description.Should().Be("diesel");
			expense.ReceiptAttachmentId.Should().Be(1);
			_storedAttachments.Single().AttachmentType.Should().Be((int)DeploymentAttachmentTypes.Receipt);
			_audits.Should().ContainSingle(a => a.Type == AuditLogTypes.DeploymentExpenseAdded);

			(await FluentActions.Awaiting(() => _service.SaveExpenseAsync(new DeploymentExpense { DeploymentId = "dep-1", DepartmentId = DeptId, Amount = -1 }, null, null, null, Crew, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("expenses_amount_invalid");
			(await FluentActions.Awaiting(() => _service.SaveExpenseAsync(new DeploymentExpense { DeploymentId = "dep-1", DepartmentId = DeptId, Amount = 1, ExpenseType = 99 }, null, null, null, Crew, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("expenses_type_invalid");

			await _service.SubmitTimeReportAsync(report.DeploymentTimeReportId, DeptId, Crew, null, null);
			await _service.ApproveTimeReportAsync(report.DeploymentTimeReportId, DeptId, Approver, null, null);
			(await FluentActions.Awaiting(() => _service.DeleteExpenseAsync(expense.DeploymentExpenseId, DeptId, Crew, null, null)).Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Be("timereports_locked");
		}

		[Test]
		public async Task Csv_export_lists_every_entry_with_its_report_context()
		{
			var report = await _service.CreateTimeReportAsync("dep-1", DeptId, Day, Crew, null, null);
			await _service.SaveTimeEntriesAsync(report.DeploymentTimeReportId, DeptId, new List<DeploymentTimeEntry> { Entry("per-1", 6, 14) }, Crew, null, null);
			_storedEntries.Single().Notes = "hose, \"long\" lay";
			var csv = await _service.ExportTimeEntriesCsvAsync("dep-1", DeptId);

			var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			lines.Should().HaveCount(2);
			lines[0].Should().StartWith("ReportNumber,ReportDate,ReportStatus");
			lines[1].Should().StartWith("1,2026-09-21,Draft,CA-BTU-1,O-1,E-12,CC-1,Personnel,per-1,Alice Smith,Deployment,");
			lines[1].Should().Contain("\"hose, \"\"long\"\" lay\"");
		}
	}
}
