using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.CostRecovery;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase C-M3 (Cal OES MARS): readiness, F-42 / expense projection from
	/// the deployment, order, fill, roster, DTR and expense facts, the checklist, expected reimbursement, the no-store
	/// handoff, the observation state machine (submission, return with a new revision, approval), redispatch,
	/// MARS-invoice reconciliation that never touches a Phase B invoice, and worker 32's value-minimized digest
	/// (plan C11 acceptance 1, 4, 5, 6, 7, 8).
	/// </summary>
	[TestFixture]
	public class CalOesMarsServiceTests
	{
		private const int DeptId = 7;
		private const string User = "manager";
		private static readonly DateTime Dispatch = new DateTime(2026, 8, 1, 6, 0, 0, DateTimeKind.Utc);

		private List<CalOesMarsAgencyProfile> _agencies;
		private List<CalOesMarsResourceProfile> _resources;
		private List<CalOesMarsRateProfile> _rateProfiles;
		private List<CalOesMarsRateLine> _rateLines;
		private List<CalOesMarsAdministrativeRateInput> _inputs;
		private List<CalOesMarsAgreementSnapshot> _agreements;
		private List<CalOesMarsWorkItem> _items;
		private List<CalOesMarsReimbursementLine> _lines;
		private List<AuditEvent> _audits;
		private List<string> _notifications;
		private Deployment _deployment;
		private DeploymentExternalContext _context;
		private List<DeploymentAttachment> _attachments;
		private List<DeploymentTimeEntry> _entries;
		private List<DeploymentTimeReport> _reports;
		private List<DeploymentExpense> _expenses;
		private Mock<IDeploymentService> _deployments;
		private Mock<ICompensationCostService> _compensation;
		private CalOesMarsService _service;

		[SetUp]
		public void SetUp()
		{
			_agencies = new List<CalOesMarsAgencyProfile>(); _resources = new List<CalOesMarsResourceProfile>(); _rateProfiles = new List<CalOesMarsRateProfile>(); _rateLines = new List<CalOesMarsRateLine>();
			_inputs = new List<CalOesMarsAdministrativeRateInput>(); _agreements = new List<CalOesMarsAgreementSnapshot>(); _items = new List<CalOesMarsWorkItem>(); _lines = new List<CalOesMarsReimbursementLine>();
			_audits = new List<AuditEvent>(); _notifications = new List<string>();

			var agencies = Repo<ICalOesMarsAgencyProfileRepository, CalOesMarsAgencyProfile>(_agencies, a => a.CalOesMarsAgencyProfileId, (a, id) => a.CalOesMarsAgencyProfileId = id);
			agencies.Setup(r => r.GetByDepartmentAsync(DeptId)).ReturnsAsync(() => _agencies.FirstOrDefault(a => !a.IsDeleted));
			agencies.Setup(r => r.GetAllAsync()).ReturnsAsync(() => _agencies.ToList());
			var resources = Repo<ICalOesMarsResourceProfileRepository, CalOesMarsResourceProfile>(_resources, r => r.CalOesMarsResourceProfileId, (r, id) => r.CalOesMarsResourceProfileId = id);
			resources.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _resources.FirstOrDefault(x => x.CalOesMarsResourceProfileId == id));
			resources.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _resources.Where(x => !x.IsDeleted).ToList());
			resources.Setup(r => r.GetByUnitIdsAsync(DeptId, It.IsAny<IEnumerable<int>>())).ReturnsAsync((int _, IEnumerable<int> ids) => _resources.Where(x => !x.IsDeleted && x.UnitId.HasValue && ids.Contains(x.UnitId.Value)).ToList());
			var rateProfiles = Repo<ICalOesMarsRateProfileRepository, CalOesMarsRateProfile>(_rateProfiles, p => p.CalOesMarsRateProfileId, (p, id) => p.CalOesMarsRateProfileId = id);
			rateProfiles.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _rateProfiles.FirstOrDefault(x => x.CalOesMarsRateProfileId == id));
			rateProfiles.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<int?>())).ReturnsAsync((int _, int? year) => _rateProfiles.Where(x => !x.IsDeleted && (!year.HasValue || x.SubmissionYear == year)).ToList());
			rateProfiles.Setup(r => r.GetEffectiveAsync(DeptId, It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime asOf) => _rateProfiles.Where(x => !x.IsDeleted && x.IsCurrent(asOf)).ToList());
			var rateLines = Repo<ICalOesMarsRateLineRepository, CalOesMarsRateLine>(_rateLines, l => l.CalOesMarsRateLineId, (l, id) => l.CalOesMarsRateLineId = id);
			rateLines.Setup(r => r.GetByProfileAsync(It.IsAny<string>())).ReturnsAsync((string id) => _rateLines.Where(x => x.CalOesMarsRateProfileId == id && !x.IsDeleted).ToList());
			rateLines.Setup(r => r.GetByProfilesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => _rateLines.Where(x => ids.Contains(x.CalOesMarsRateProfileId) && !x.IsDeleted).ToList());
			var inputs = Repo<ICalOesMarsAdministrativeRateInputRepository, CalOesMarsAdministrativeRateInput>(_inputs, i => i.CalOesMarsAdministrativeRateInputId, (i, id) => i.CalOesMarsAdministrativeRateInputId = id);
			inputs.Setup(r => r.GetByProfileAsync(It.IsAny<string>())).ReturnsAsync((string id) => _inputs.Where(x => x.CalOesMarsRateProfileId == id && !x.IsDeleted).ToList());
			var agreements = Repo<ICalOesMarsAgreementSnapshotRepository, CalOesMarsAgreementSnapshot>(_agreements, a => a.CalOesMarsAgreementSnapshotId, (a, id) => a.CalOesMarsAgreementSnapshotId = id);
			agreements.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _agreements.FirstOrDefault(x => x.CalOesMarsAgreementSnapshotId == id));
			agreements.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _agreements.Where(x => !x.IsDeleted).ToList());
			var items = Repo<ICalOesMarsWorkItemRepository, CalOesMarsWorkItem>(_items, w => w.CalOesMarsWorkItemId, (w, id) => w.CalOesMarsWorkItemId = id);
			items.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _items.FirstOrDefault(x => x.CalOesMarsWorkItemId == id));
			items.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _items.Where(x => x.DeploymentId == id && !x.IsDeleted).ToList());
			items.Setup(r => r.GetByExternalIdAsync(DeptId, It.IsAny<string>())).ReturnsAsync((int _, string id) => _items.Where(x => !x.IsDeleted && (x.MarsRecordId == id || x.MarsInvoiceId == id)).ToList());
			items.Setup(r => r.GetActionQueueAsync(DeptId, It.IsAny<int?>())).ReturnsAsync((int _, int? type) => _items.Where(x => !x.IsDeleted && x.LocalState != (int)CalOesMarsLocalStates.Closed && (!type.HasValue || x.RecordType == type)).ToList());
			items.Setup(r => r.GetByAgreementSnapshotAsync(DeptId, It.IsAny<string>())).ReturnsAsync((int _, string id) => _items.Where(x => !x.IsDeleted && x.AgreementSnapshotId == id).ToList());
			items.Setup(r => r.GetDepartmentsWithOpenItemsAsync()).ReturnsAsync(() => _items.Where(x => !x.IsDeleted && x.LocalState != (int)CalOesMarsLocalStates.Closed).Select(x => x.DepartmentId).Distinct().ToList());
			var lines = Repo<ICalOesMarsReimbursementLineRepository, CalOesMarsReimbursementLine>(_lines, l => l.CalOesMarsReimbursementLineId, (l, id) => l.CalOesMarsReimbursementLineId = id);
			lines.Setup(r => r.GetByWorkItemAsync(It.IsAny<string>())).ReturnsAsync((string id) => _lines.Where(x => x.CalOesMarsWorkItemId == id).ToList());
			lines.Setup(r => r.DeleteByWorkItemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string id, CancellationToken _) => { _lines.RemoveAll(x => x.CalOesMarsWorkItemId == id); return true; });

			// The immutable deployment facts: an engine on an RMS order with two requests, a roster of two, DTR hours and two expenses.
			_deployment = new Deployment
			{
				DeploymentId = "dep-1", DepartmentId = DeptId, Name = "Ridge Fire engine", FinanceMode = (int)DeploymentFinanceModes.CostRecovery, Status = (int)DeploymentStatuses.Active, RmsExternalOrderId = "order-1", IncidentNumber = "CA-LNU-001234", StartOn = Dispatch, AddedOn = Dispatch,
				Units = { new DeploymentUnit { DeploymentUnitId = "du-1", DeploymentId = "dep-1", DepartmentId = DeptId, UnitId = 31, UnitName = "E-31" } },
				Personnel =
				{
					new DeploymentPersonnel { DeploymentPersonnelId = "dp-1", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "alice", CertificationCode = "Captain", DisplayName = "Alice Captain", RmsExternalOrderFillId = "fill-e12" },
					new DeploymentPersonnel { DeploymentPersonnelId = "dp-2", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "bob", CertificationCode = "Firefighter", DisplayName = "Bob Firefighter", RmsExternalOrderFillId = "fill-e12" },
					new DeploymentPersonnel { DeploymentPersonnelId = "dp-3", DeploymentId = "dep-1", DepartmentId = DeptId, UserId = "carol", CertificationCode = "Firefighter", DisplayName = "Carol Overhead", RmsExternalOrderFillId = "fill-o3" }
				}
			};
			_context = new DeploymentExternalContext
			{
				Order = new RmsExternalOrder { RmsExternalOrderId = "order-1", DepartmentId = DeptId, OrderNumber = "CA-LNU-001234-O1", IncidentName = "Ridge Fire", IncidentNumber = "CA-LNU-001234", RequestingAgency = "LNU" },
				Fills =
				{
					new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-e12", RmsExternalOrderId = "order-1", RequestNumber = "E-12", ResourceKind = "Engine", ResourceType = "Type 3 Engine", MobilizedOn = Dispatch, CheckedInOn = Dispatch.AddHours(4), ReleasedOn = Dispatch.AddHours(46) },
					new RmsExternalOrderFill { RmsExternalOrderFillId = "fill-o3", RmsExternalOrderId = "order-1", RequestNumber = "O-3", ResourceKind = "Overhead", Position = "DIVS", MobilizedOn = Dispatch }
				}
			};
			_attachments = new List<DeploymentAttachment> { new DeploymentAttachment { DeploymentAttachmentId = 5, DeploymentId = "dep-1", DepartmentId = DeptId, AttachmentType = (int)DeploymentAttachmentTypes.PaperF42, Name = "Paper F-42", FileName = "f42.pdf", Data = new byte[] { 1, 2, 3 } } };
			_reports = new List<DeploymentTimeReport> { new DeploymentTimeReport { DeploymentTimeReportId = "dtr-1", DeploymentId = "dep-1", DepartmentId = DeptId, Status = (int)DeploymentTimeReportStatuses.Approved, ReportDate = Dispatch.Date }, new DeploymentTimeReport { DeploymentTimeReportId = "dtr-void", DeploymentId = "dep-1", DepartmentId = DeptId, Status = (int)DeploymentTimeReportStatuses.Void, ReportDate = Dispatch.Date } };
			_entries = new List<DeploymentTimeEntry>
			{
				new DeploymentTimeEntry { DeploymentTimeEntryId = "te-1", DeploymentTimeReportId = "dtr-1", DeploymentId = "dep-1", SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, DeploymentPersonnelId = "dp-1", StartTime = Dispatch, EndTime = Dispatch.AddHours(14) },
				new DeploymentTimeEntry { DeploymentTimeEntryId = "te-2", DeploymentTimeReportId = "dtr-1", DeploymentId = "dep-1", SubjectType = (int)DeploymentTimeSubjectTypes.Unit, DeploymentUnitId = "du-1", StartTime = Dispatch, EndTime = Dispatch.AddHours(14), MileageKm = 160.9m },
				new DeploymentTimeEntry { DeploymentTimeEntryId = "te-void", DeploymentTimeReportId = "dtr-void", DeploymentId = "dep-1", SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, DeploymentPersonnelId = "dp-1", StartTime = Dispatch, EndTime = Dispatch.AddHours(99) }
			};
			_expenses = new List<DeploymentExpense>
			{
				new DeploymentExpense { DeploymentExpenseId = "ex-1", DeploymentId = "dep-1", DepartmentId = DeptId, ExpenseDate = Dispatch.Date, ExpenseType = (int)DeploymentExpenseTypes.PerDiemMeal, City = "Napa", Amount = 18.5m, ReceiptAttachmentId = 9 },
				new DeploymentExpense { DeploymentExpenseId = "ex-2", DeploymentId = "dep-1", DepartmentId = DeptId, ExpenseDate = Dispatch.Date, ExpenseType = (int)DeploymentExpenseTypes.Accommodation, City = "Napa", Amount = 140 }
			};
			_deployments = new Mock<IDeploymentService>();
			_compensation = new Mock<ICompensationCostService>();
			_deployments.Setup(d => d.GetDeploymentByIdAsync("dep-1", DeptId)).ReturnsAsync(() => _deployment);
			_deployments.Setup(d => d.GetExternalContextAsync("dep-1", DeptId, It.IsAny<string>())).ReturnsAsync(() => _context);
			_deployments.Setup(d => d.GetAttachmentsAsync("dep-1", DeptId)).ReturnsAsync(() => _attachments.ToList());
			_deployments.Setup(d => d.GetAttachmentAsync(It.IsAny<int>(), DeptId, It.IsAny<bool>())).ReturnsAsync((int id, int _, bool __) => _attachments.FirstOrDefault(a => a.DeploymentAttachmentId == id));
			_deployments.Setup(d => d.IsRosteredAsync("dep-1", DeptId, It.IsAny<string>())).ReturnsAsync((string _, int __, string user) => _deployment.Personnel.Any(p => p.UserId == user));
			_deployments.Setup(d => d.CanFieldMemberSeeAsync("dep-1", DeptId, It.IsAny<string>())).ReturnsAsync((string _, int __, string user) => _deployment.Personnel.Any(p => p.UserId == user));
			_deployments.Setup(d => d.GetCostRecoveryDeploymentsReleasedBeforeAsync(DeptId, It.IsAny<DateTime>())).ReturnsAsync(() => new List<Deployment> { _deployment });
			var timeTracking = new Mock<ITimeTrackingService>();
			timeTracking.Setup(t => t.GetTimeReportsAsync("dep-1", DeptId)).ReturnsAsync(() => _reports.ToList());
			timeTracking.Setup(t => t.GetExpensesAsync("dep-1", DeptId)).ReturnsAsync(() => _expenses.ToList());
			var entries = new Mock<IDeploymentTimeEntryRepository>();
			entries.Setup(e => e.GetByDeploymentAsync("dep-1")).ReturnsAsync(() => _entries.ToList());
			var units = new Mock<IUnitsService>();
			units.Setup(u => u.GetUnitsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 31, DepartmentId = DeptId, Name = "E-31", Type = "Type 3 Engine", PlateNumber = "1ABC234", VIN = "VIN31" } });
			units.Setup(u => u.GetUnitByIdAsync(31)).ReturnsAsync(new Unit { UnitId = 31, DepartmentId = DeptId, Name = "E-31" });
			var profiles = new Mock<IUserProfileService>();
			profiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync(new List<UserProfile>());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Test", TimeZone = "UTC" });
			departments.Setup(d => d.GetAllAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), DeptId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.Callback<string, int, string, string, Department, string, UserProfile, bool>((_, __, message, ___, ____, _____, ______, _______) => _notifications.Add(message)).ReturnsAsync(true);

			_service = new CalOesMarsService(agencies.Object, resources.Object, rateProfiles.Object, rateLines.Object, inputs.Object, agreements.Object, items.Object, lines.Object,
				_deployments.Object, timeTracking.Object, entries.Object, units.Object, profiles.Object, departments.Object, events.Object, null,
				new CalOesMarsReimbursementCalculator(), new ManualCalOesMarsGateway(), new Lazy<ICommunicationService>(() => communication.Object), null, new Lazy<ICompensationCostService>(() => _compensation.Object));
		}

		private static Mock<TRepo> Repo<TRepo, T>(List<T> store, Func<T, string> id, Action<T, string> setId) where TRepo : class, IRepository<T> where T : class, IEntity
		{
			var mock = new Mock<TRepo>();
			mock.Setup(r => r.SaveOrUpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((T entity, CancellationToken _, bool __) => { if (string.IsNullOrWhiteSpace(id(entity))) setId(entity, Guid.NewGuid().ToString()); store.RemoveAll(x => id(x) == id(entity)); store.Add(entity); return entity; });
			return mock;
		}

		private async Task SeedReadyDepartmentAsync()
		{
			await _service.SaveAgencyProfileAsync(new CalOesMarsAgencyProfile { DepartmentId = DeptId, AgencyName = "Test County Fire", MacsDesignator = "xtc", FeinReference = "12-3456789", UeiReference = "ABC123DEF456", FiscalSupplierReference = "0001234567" }, User, null, null);
			await _service.MarkAgencyVerifiedAsync(DeptId, User, null, null);
			var salary = await _service.SaveRateProfileAsync(new CalOesMarsRateProfile { DepartmentId = DeptId, SubmissionYear = 2026, SubmissionType = (int)CalOesMarsSubmissionTypes.SalarySurvey, EffectiveOn = new DateTime(2026, 1, 1), ExpiresOn = new DateTime(2026, 12, 31), AdministrativeRateMethod = (int)CalOesMarsAdministrativeRateMethods.DeMinimis, AdministrativeRateValue = 10 }, User, null, null);
			await _service.SaveRateLinesAsync(salary.CalOesMarsRateProfileId, DeptId, new List<CalOesMarsRateLine>
			{
				new CalOesMarsRateLine { LineKind = (int)CalOesMarsRateLineKinds.SalarySurvey, ClassificationCode = "Captain", StraightRate = 60, OvertimeRate = 90, OvertimeEligible = true, PortalToPortalEligible = true },
				new CalOesMarsRateLine { LineKind = (int)CalOesMarsRateLineKinds.SalarySurvey, ClassificationCode = "Firefighter", StraightRate = 40, OvertimeEligible = true, PortalToPortalEligible = true }
			}, User, null, null);
			await _service.SetRateProfileStatusAsync(salary.CalOesMarsRateProfileId, DeptId, CalOesMarsRateProfileStatuses.Reviewed, null, User, null, null);
			var letter = await _service.SaveRateProfileAsync(new CalOesMarsRateProfile { DepartmentId = DeptId, SubmissionYear = 2026, SubmissionType = (int)CalOesMarsSubmissionTypes.RateLetter, EffectiveOn = new DateTime(2026, 1, 1) }, User, null, null);
			await _service.SaveRateLinesAsync(letter.CalOesMarsRateProfileId, DeptId, new List<CalOesMarsRateLine>
			{
				new CalOesMarsRateLine { LineKind = (int)CalOesMarsRateLineKinds.OfficialApparatus, ResourceCode = "Type 3 Engine", Basis = (int)CalOesMarsRateBases.Hourly, StraightRate = 85, Authority = (int)CalOesMarsRateAuthorities.CalOesRateLetter }
			}, User, null, null);
			await _service.SaveAgreementAsync(new CalOesMarsAgreementSnapshot { DepartmentId = DeptId, DocumentKind = (int)CalOesMarsDocumentKinds.Mou, CompensationMethod = (int)CalOesMarsCompensationMethods.ActualHours, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterEightHoursPerDay, StartOn = new DateTime(2026, 1, 1) }, User, null, null);
			await _service.BuildResourceInventoryF5DraftAsync(DeptId, new[] { 31 }, User, null, null);
		}

		[Test]
		public async Task Readiness_blocks_without_agency_salary_survey_or_agreement_and_clears_once_they_exist()
		{
			var empty = await _service.GetAgencyReadinessAsync(DeptId, Dispatch);
			empty.IsReady.Should().BeFalse();
			empty.Items.Where(i => i.Severity == (int)CalOesMarsReadinessSeverities.Blocker).Select(i => i.MessageKey).Should().BeEquivalentTo(new[] { "ReadinessAgencyMissing", "ReadinessSalaryMissing", "ReadinessAgreementMissing" });
			empty.AuthorityProfileCode.Should().Be(CalOesMarsAuthorityProfile.CurrentCode);

			await SeedReadyDepartmentAsync();
			var ready = await _service.GetAgencyReadinessAsync(DeptId, Dispatch);
			ready.IsReady.Should().BeTrue();
			ready.Items.Should().NotContain(i => i.Severity == (int)CalOesMarsReadinessSeverities.Blocker);
			ready.Items.Select(i => i.MessageKey).Should().Contain("ReadinessRateUnsigned", "the survey is reviewed but not signed");
			ready.Agency.MacsDesignator.Should().Be("XTC", "the designator is upper-cased");
			ready.ResourceProfiles.Should().Be(1);
			_resources.Single().Should().Match<CalOesMarsResourceProfile>(r => r.UnitId == 31 && r.LicensePlate == "1ABC234" && r.Vin == "VIN31" && r.ResourceType == "Type 3 Engine");
			// Changing an identifier clears the verification.
			await _service.SaveAgencyProfileAsync(new CalOesMarsAgencyProfile { DepartmentId = DeptId, AgencyName = "Test County Fire", MacsDesignator = "XTC", FeinReference = "99-0000000" }, User, null, null);
			(await _service.GetAgencyProfileAsync(DeptId)).VerifiedOn.Should().BeNull();
		}

		[Test]
		public async Task F42_draft_projects_the_fill_roster_and_dtr_facts_then_the_checklist_gates_the_handoff_and_observation()
		{
			await SeedReadyDepartmentAsync();
			await FluentActions.Awaiting(() => _service.BuildF42DraftAsync("dep-1", DeptId, null, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_fill_required");

			var item = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			item.RecordType.Should().Be((int)CalOesMarsRecordTypes.F42);
			item.LocalState.Should().Be((int)CalOesMarsLocalStates.Draft);
			item.RmsExternalOrderFillId.Should().Be("fill-e12");
			item.AgreementSnapshotId.Should().NotBeNullOrWhiteSpace("the agreement is selected as of initial dispatch");
			item.RateProfileVersion.Should().Contain(":");
			var s = CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(item.SnapshotJson);
			s.MacsDesignator.Should().Be("XTC");
			s.IncidentNumber.Should().Be("CA-LNU-001234");
			s.OrderNumber.Should().Be("CA-LNU-001234-O1");
			s.RequestNumber.Should().Be("E-12");
			s.ResourceType.Should().Be("Type 3 Engine");
			s.DispatchedOn.Should().Be(Dispatch);
			s.CommittedOn.Should().Be(Dispatch.AddHours(4));
			s.ReleasedOn.Should().Be(Dispatch.AddHours(46));
			s.ReturnedOn.Should().BeNull("release is not return");
			s.Personnel.Select(p => p.DeploymentPersonnelId).Should().BeEquivalentTo(new[] { "dp-1", "dp-2" }, "the roster is filtered to the request's fill");
			s.Personnel.Single(p => p.DeploymentPersonnelId == "dp-1").ActualHours.Should().ContainSingle().Which.Hours.Should().Be(14, "void reports never feed the F-42");
			s.Vehicles.Should().ContainSingle().Which.Should().Match<CalOesMarsF42Vehicle>(v => v.Kind == "Apparatus" && v.Designator == "E-31" && v.ResourceCode == "Type 3 Engine" && v.LicensePlate == "1ABC234" && v.CommittedHours == 14 && v.Miles == 100);
			s.AttachmentIds.Should().Equal(5);
			s.SourceTimeReportIds.Should().Equal("dtr-1");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.CalOesMarsWorkItemPrepared);

			// Unsigned: the checklist blocks and the handoff refuses.
			var first = await _service.ValidateForPortalAsync(item.CalOesMarsWorkItemId, DeptId, User, null, null);
			first.IsReadyForPortal.Should().BeFalse();
			first.Errors.Select(e => e.Code).Should().Contain(new[] { CalOesMarsValidationCodes.RespondingSignatureMissing, CalOesMarsValidationCodes.IncidentAuthorizationMissing });
			(await _service.GetWorkItemAsync(item.CalOesMarsWorkItemId, DeptId)).LocalState.Should().Be((int)CalOesMarsLocalStates.NeedsReview);
			await FluentActions.Awaiting(() => _service.OpenPortalHandoffAsync(item.CalOesMarsWorkItemId, DeptId, true, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_not_ready");
			await FluentActions.Awaiting(() => _service.RecordExternalSubmissionAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1" }, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_not_ready");

			// Sign, set the return, run the checklist: ready. Opening the handoff never changes the state.
			await _service.SaveF42SnapshotAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { ReturnedOn = Dispatch.AddHours(48), RespondingSignerName = "Chief Jones", IncidentAuthorizerName = "AREP Smith", Comments = "Relieved by E-32" }, User, null, null);
			var second = await _service.ValidateForPortalAsync(item.CalOesMarsWorkItemId, DeptId, User, null, null);
			second.Errors.Should().BeEmpty();
			second.Warnings.Select(w => w.Code).Should().NotContain(CalOesMarsValidationCodes.VehicleNotInInventory);
			(await _service.GetWorkItemAsync(item.CalOesMarsWorkItemId, DeptId)).LocalState.Should().Be((int)CalOesMarsLocalStates.ReadyForPortal);
			await FluentActions.Awaiting(() => _service.OpenPortalHandoffAsync(item.CalOesMarsWorkItemId, DeptId, false, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_attestation_required");
			var manifest = await _service.OpenPortalHandoffAsync(item.CalOesMarsWorkItemId, DeptId, true, User, null, null);
			manifest.Fields.Select(f => f.Box).Should().Equal(CalOesMarsAuthorityProfile.Current.F42Boxes.Select(b => b.Id));
			manifest.Fields.Single(f => f.Box == "request").Value.Should().Be("E-12");
			manifest.NotAnImportFile.Should().BeTrue();
			manifest.Checksum.Should().NotBeNullOrWhiteSpace();
			(await _service.GetWorkItemAsync(item.CalOesMarsWorkItemId, DeptId)).LocalState.Should().Be((int)CalOesMarsLocalStates.ReadyForPortal, "opening the handoff is not a submission");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.CalOesMarsWorkItemOpenedForHandoff);

			// Expected reimbursement is an estimate from the effective profile; the packet carries the paper F-42.
			var calc = await _service.CalculateExpectedReimbursementAsync(item.CalOesMarsWorkItemId, DeptId, User, null, null);
			calc.Lines.Should().Contain(l => l.LineKind == (int)CalOesMarsLineKinds.Personnel && l.SubjectId == "dp-1" && l.Quantity == 8 && l.Rate == 60);
			calc.Lines.Should().Contain(l => l.LineKind == (int)CalOesMarsLineKinds.Personnel && l.SubjectId == "dp-1" && l.Quantity == 6 && l.Rate == 90);
			calc.Lines.Should().Contain(l => l.LineKind == (int)CalOesMarsLineKinds.Apparatus && l.ExpectedAmount == 14 * 85);
			calc.Lines.Should().Contain(l => l.LineKind == (int)CalOesMarsLineKinds.Administrative);
			calc.Exceptions.Should().Contain(e => e.Code == CalOesMarsExceptionCodes.NoActualHours && e.Detail.Contains("Bob"));
			(await _service.GetWorkItemAsync(item.CalOesMarsWorkItemId, DeptId)).ExpectedTotal.Should().Be(calc.ExpectedTotal);
			_lines.Should().HaveCount(calc.Lines.Count);
			using (var zip = new ZipArchive(new MemoryStream(await _service.BuildEvidencePacketAsync(item.CalOesMarsWorkItemId, DeptId, User)), ZipArchiveMode.Read))
			{
				zip.Entries.Select(e => e.FullName).Should().Contain(new[] { "README.txt", "manifest.json", "snapshot.json", "record.html", "supporting/f42.pdf" });
				using var reader = new StreamReader(zip.GetEntry("README.txt").Open());
				(await reader.ReadToEndAsync()).Should().Contain("NOT an accepted MARS import file");
			}

			// Observed submitted, returned (new revision carries the comment; the old one closes), resubmitted and approved.
			var submitted = await _service.RecordExternalSubmissionAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1001", ExternalStatus = "Cal OES Review" }, User, null, null);
			submitted.LocalState.Should().Be((int)CalOesMarsLocalStates.SubmittedExternal);
			submitted.MarsRecordId.Should().Be("F42-1001");
			submitted.IsLocallyEditable.Should().BeFalse();
			await FluentActions.Awaiting(() => _service.SaveF42SnapshotAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot(), User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_work_item_external");
			await FluentActions.Awaiting(() => _service.RecordExternalStatusAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalStatus = "Rejected" }, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_status_unknown");
			var revision = await _service.RecordExternalStatusAsync(item.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalStatus = "Agency Review", Comment = "Box 14 missing the AREP title" }, User, null, null);
			revision.CalOesMarsWorkItemId.Should().NotBe(item.CalOesMarsWorkItemId);
			revision.SupersedesWorkItemId.Should().Be(item.CalOesMarsWorkItemId);
			revision.LocalState.Should().Be((int)CalOesMarsLocalStates.ReturnedForAgencyReview);
			revision.CorrectionComment.Should().Be("Box 14 missing the AREP title");
			revision.MarsRecordId.Should().Be("F42-1001");
			revision.Lines.Should().HaveCount(calc.Lines.Count, "the estimate travels with the revision");
			(await _service.GetWorkItemAsync(item.CalOesMarsWorkItemId, DeptId)).LocalState.Should().Be((int)CalOesMarsLocalStates.Closed);
			await _service.SaveF42SnapshotAsync(revision.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { RespondingSignerName = "Chief Jones", IncidentAuthorizerName = "AREP Smith, Agency Rep", ReturnedOn = Dispatch.AddHours(48) }, User, null, null);
			(await _service.ValidateForPortalAsync(revision.CalOesMarsWorkItemId, DeptId, User, null, null)).IsReadyForPortal.Should().BeTrue();
			await _service.RecordExternalSubmissionAsync(revision.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1001" }, User, null, null);
			var approved = await _service.RecordExternalStatusAsync(revision.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalStatus = "Approved" }, User, null, null);
			approved.LocalState.Should().Be((int)CalOesMarsLocalStates.Approved);
			approved.ApprovedOn.Should().NotBeNull();

			// The queue: managers see everything open, a rostered member only their own deployment's items, and never an invoice.
			(await _service.GetActionQueueAsync(DeptId, "alice", true)).Select(q => q.WorkItem.CalOesMarsWorkItemId).Should().Contain(revision.CalOesMarsWorkItemId);
			(await _service.GetActionQueueAsync(DeptId, "alice", false)).Should().OnlyContain(q => q.IsMine);
			(await _service.GetActionQueueAsync(DeptId, "stranger", false)).Should().BeEmpty();
			// A member seated on the deployed unit (the Unit app's tablet) is not on the roster but opens the items; the queue lists them by the same rule.
			_deployments.Setup(d => d.CanFieldMemberSeeAsync("dep-1", DeptId, "tablet")).ReturnsAsync(true);
			(await _service.IsRosteredForWorkItemAsync(revision.CalOesMarsWorkItemId, DeptId, "tablet")).Should().BeTrue();
			var tabletQueue = await _service.GetActionQueueAsync(DeptId, "tablet", false);
			tabletQueue.Select(q => q.WorkItem.CalOesMarsWorkItemId).Should().Contain(revision.CalOesMarsWorkItemId);
			tabletQueue.Should().OnlyContain(q => q.IsMine && q.WorkItem.RecordType != (int)CalOesMarsRecordTypes.GeneratedInvoice);
			(await _service.IsRosteredForWorkItemAsync(revision.CalOesMarsWorkItemId, DeptId, "bob")).Should().BeTrue();
			(await _service.IsRosteredForWorkItemAsync(revision.CalOesMarsWorkItemId, DeptId, "stranger")).Should().BeFalse();
		}

		[Test]
		public async Task Closed_work_items_still_protect_their_agreement_snapshot()
		{
			await SeedReadyDepartmentAsync();
			var item = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			var agreement = await _service.GetAgreementAsync(item.AgreementSnapshotId, DeptId);
			agreement.Should().NotBeNull();
			// The claim was submitted, paid and closed; it left the action queue but its terms are still the ones on record.
			_items.Single(w => w.CalOesMarsWorkItemId == item.CalOesMarsWorkItemId).LocalState = (int)CalOesMarsLocalStates.Closed;

			await FluentActions.Awaiting(() => _service.DeleteAgreementAsync(agreement.CalOesMarsAgreementSnapshotId, DeptId, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_agreement_in_use");
			var edit = new CalOesMarsAgreementSnapshot { CalOesMarsAgreementSnapshotId = agreement.CalOesMarsAgreementSnapshotId, DepartmentId = DeptId, DocumentKind = agreement.DocumentKind, CompensationMethod = agreement.CompensationMethod, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterTwelveHoursPerDay, StartOn = agreement.StartOn };
			var saved = await _service.SaveAgreementAsync(edit, User, null, null);
			saved.CalOesMarsAgreementSnapshotId.Should().NotBe(item.AgreementSnapshotId, "an edit to a referenced snapshot becomes a new version");
			var superseded = await _service.GetAgreementAsync(item.AgreementSnapshotId, DeptId);
			superseded.OvertimeMethod.Should().Be((int)CalOesMarsOvertimeMethods.AfterEightHoursPerDay, "the closed claim's terms are unchanged");
			// The revision starts on the same day, so the prior snapshot covers no dispatch date the revision does and a new claim selects the revision.
			superseded.CoversDate(Dispatch).Should().BeFalse();
			(await _service.SelectAgreementAsync(DeptId, null, Dispatch)).CalOesMarsAgreementSnapshotId.Should().Be(saved.CalOesMarsAgreementSnapshotId);

			// An amendment effective later closes the prior revision the day before it and keeps it for the earlier dispatch dates.
			_items.Add(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "wi-ext", DepartmentId = DeptId, DeploymentId = "dep-1", RecordType = (int)CalOesMarsRecordTypes.F42, AgreementSnapshotId = saved.CalOesMarsAgreementSnapshotId, LocalState = (int)CalOesMarsLocalStates.Closed, MarsRecordId = "F42-9", AddedOn = Dispatch });
			var amendment = new CalOesMarsAgreementSnapshot { CalOesMarsAgreementSnapshotId = saved.CalOesMarsAgreementSnapshotId, DepartmentId = DeptId, DocumentKind = saved.DocumentKind, CompensationMethod = saved.CompensationMethod, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterEightHoursPerDay, StartOn = Dispatch.Date.AddDays(10) };
			var amended = await _service.SaveAgreementAsync(amendment, User, null, null);
			amended.CalOesMarsAgreementSnapshotId.Should().NotBe(saved.CalOesMarsAgreementSnapshotId);
			(await _service.GetAgreementAsync(saved.CalOesMarsAgreementSnapshotId, DeptId)).EndOn.Should().Be(Dispatch.Date.AddDays(9));
			(await _service.SelectAgreementAsync(DeptId, null, Dispatch)).CalOesMarsAgreementSnapshotId.Should().Be(saved.CalOesMarsAgreementSnapshotId);
			(await _service.SelectAgreementAsync(DeptId, null, Dispatch.AddDays(10))).CalOesMarsAgreementSnapshotId.Should().Be(amended.CalOesMarsAgreementSnapshotId);
		}

		[Test]
		public async Task An_open_ended_referenced_agreement_revised_without_a_start_is_effective_today_and_keeps_the_prior_for_earlier_dates()
		{
			var openEnded = await _service.SaveAgreementAsync(new CalOesMarsAgreementSnapshot { DepartmentId = DeptId, DocumentKind = (int)CalOesMarsDocumentKinds.Mou, CompensationMethod = (int)CalOesMarsCompensationMethods.ActualHours, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterEightHoursPerDay }, User, null, null);
			openEnded.StartOn.Should().BeNull("an ordinary agreement keeps the open-ended start it was given");
			_items.Add(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "wi-open", DepartmentId = DeptId, DeploymentId = "dep-1", RecordType = (int)CalOesMarsRecordTypes.F42, AgreementSnapshotId = openEnded.CalOesMarsAgreementSnapshotId, LocalState = (int)CalOesMarsLocalStates.Closed, MarsRecordId = "F42-1", AddedOn = Dispatch });

			var today = DateTime.UtcNow.Date;
			var revision = await _service.SaveAgreementAsync(new CalOesMarsAgreementSnapshot { CalOesMarsAgreementSnapshotId = openEnded.CalOesMarsAgreementSnapshotId, DepartmentId = DeptId, DocumentKind = openEnded.DocumentKind, CompensationMethod = openEnded.CompensationMethod, OvertimeMethod = (int)CalOesMarsOvertimeMethods.AfterTwelveHoursPerDay }, User, null, null);

			revision.CalOesMarsAgreementSnapshotId.Should().NotBe(openEnded.CalOesMarsAgreementSnapshotId);
			revision.StartOn.Should().Be(today, "a referenced revision without a submitted start is effective today, matching the day the prior snapshot closes");
			(await _service.GetAgreementAsync(openEnded.CalOesMarsAgreementSnapshotId, DeptId)).EndOn.Should().Be(today.AddDays(-1));
			// Yesterday's dispatch still selects the prior terms; today's selects the revision.
			(await _service.SelectAgreementAsync(DeptId, null, today.AddDays(-1))).CalOesMarsAgreementSnapshotId.Should().Be(openEnded.CalOesMarsAgreementSnapshotId);
			(await _service.SelectAgreementAsync(DeptId, null, today)).CalOesMarsAgreementSnapshotId.Should().Be(revision.CalOesMarsAgreementSnapshotId);

			// An end before the effective start is rejected rather than saved as an empty range.
			_items.Add(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "wi-rev", DepartmentId = DeptId, DeploymentId = "dep-1", RecordType = (int)CalOesMarsRecordTypes.F42, AgreementSnapshotId = revision.CalOesMarsAgreementSnapshotId, LocalState = (int)CalOesMarsLocalStates.Closed, MarsRecordId = "F42-2", AddedOn = Dispatch });
			await FluentActions.Awaiting(() => _service.SaveAgreementAsync(new CalOesMarsAgreementSnapshot { CalOesMarsAgreementSnapshotId = revision.CalOesMarsAgreementSnapshotId, DepartmentId = DeptId, DocumentKind = revision.DocumentKind, CompensationMethod = revision.CompensationMethod, OvertimeMethod = revision.OvertimeMethod, EndOn = today.AddDays(-2) }, User, null, null))
				.Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_dates_invalid");
		}

		[Test]
		public async Task Redispatch_closes_the_first_interval_and_opens_a_superseding_f42()
		{
			await SeedReadyDepartmentAsync();
			var first = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			await _service.SaveF42SnapshotAsync(first.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { RespondingSignerName = "Chief", IncidentAuthorizerName = "AREP" }, User, null, null);
			await _service.ValidateForPortalAsync(first.CalOesMarsWorkItemId, DeptId, User, null, null);
			await _service.RecordExternalSubmissionAsync(first.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1" }, User, null, null);

			// The same request is rebuilt after the resource is sent on to a new incident: a new item supersedes the first,
			// whose interval closes at its release time (release is not return, but the redispatch ends the first commitment).
			var second = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			second.CalOesMarsWorkItemId.Should().NotBe(first.CalOesMarsWorkItemId);
			second.SupersedesWorkItemId.Should().Be(first.CalOesMarsWorkItemId);
			CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(second.SnapshotJson).IsRedispatch.Should().BeTrue();
			CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>((await _service.GetWorkItemAsync(first.CalOesMarsWorkItemId, DeptId)).SnapshotJson).ReturnedOn.Should().Be(Dispatch.AddHours(46));
			// Rebuilding a still-local draft reuses it and keeps the locally authored boxes.
			await _service.SaveF42SnapshotAsync(second.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { Comments = "Second interval" }, User, null, null);
			var rebuilt = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			rebuilt.CalOesMarsWorkItemId.Should().Be(second.CalOesMarsWorkItemId);
			CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(rebuilt.SnapshotJson).Comments.Should().Be("Second interval");
			// The overhead request gets its own F-42 with the overhead position and its own roster member.
			var overhead = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-o3", User, null, null);
			var s = CalOesMarsService.Deserialize<CalOesMarsF42Snapshot>(overhead.SnapshotJson);
			s.OverheadPosition.Should().Be("DIVS");
			s.Personnel.Select(p => p.DeploymentPersonnelId).Should().Equal("dp-3");
		}

		[Test]
		public async Task Expense_claim_links_to_a_submitted_f42_or_takes_the_travel_only_path()
		{
			await SeedReadyDepartmentAsync();
			var f42 = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			var claim = await _service.BuildExpenseClaimDraftAsync("dep-1", DeptId, f42.CalOesMarsWorkItemId, User, null, null);
			var s = CalOesMarsService.Deserialize<CalOesMarsExpenseClaimSnapshot>(claim.SnapshotJson);
			s.F42WorkItemId.Should().Be(f42.CalOesMarsWorkItemId);
			s.TravelOnly.Should().BeFalse();
			s.Lines.Select(l => l.Category).Should().Equal("Meal", "Lodging");
			s.Lines[1].ReceiptAttachmentId.Should().BeNull();

			var blocked = await _service.ValidateForPortalAsync(claim.CalOesMarsWorkItemId, DeptId, User, null, null);
			blocked.Errors.Select(e => e.Code).Should().Contain(new[] { CalOesMarsValidationCodes.ExpenseReceiptMissing, CalOesMarsValidationCodes.ExpenseF42NotSubmitted, CalOesMarsValidationCodes.ExpenseSignatureMissing });

			_expenses[1].ReceiptAttachmentId = 10;
			await _service.SaveF42SnapshotAsync(f42.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { RespondingSignerName = "Chief", IncidentAuthorizerName = "AREP" }, User, null, null);
			await _service.ValidateForPortalAsync(f42.CalOesMarsWorkItemId, DeptId, User, null, null);
			await _service.RecordExternalSubmissionAsync(f42.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1" }, User, null, null);
			var rebuilt = await _service.BuildExpenseClaimDraftAsync("dep-1", DeptId, f42.CalOesMarsWorkItemId, User, null, null);
			rebuilt.CalOesMarsWorkItemId.Should().Be(claim.CalOesMarsWorkItemId, "a local claim for the same F-42 is refreshed, not duplicated");
			await _service.SaveExpenseSnapshotAsync(claim.CalOesMarsWorkItemId, DeptId, new CalOesMarsExpenseClaimSnapshot { SignerName = "A. Captain", ApproverName = "Chief" }, User, null, null);
			var ready = await _service.ValidateForPortalAsync(claim.CalOesMarsWorkItemId, DeptId, User, null, null);
			ready.Errors.Should().BeEmpty();
			var calc = await _service.CalculateExpectedReimbursementAsync(claim.CalOesMarsWorkItemId, DeptId, User, null, null);
			calc.ExpectedTotal.Should().Be(18.5m);
			calc.UncertainTotal.Should().Be(140, "lodging without pre-approval");

			var travel = await _service.BuildExpenseClaimDraftAsync("dep-1", DeptId, null, User, null, null);
			CalOesMarsService.Deserialize<CalOesMarsExpenseClaimSnapshot>(travel.SnapshotJson).TravelOnly.Should().BeTrue();
			travel.CalOesMarsWorkItemId.Should().NotBe(claim.CalOesMarsWorkItemId);
		}

		[Test]
		public async Task Mars_invoice_is_a_work_item_reconciled_against_expected_lines_and_paid_only_from_an_observed_payment()
		{
			await SeedReadyDepartmentAsync();
			var f42 = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			await _service.SaveF42SnapshotAsync(f42.CalOesMarsWorkItemId, DeptId, new CalOesMarsF42Snapshot { RespondingSignerName = "Chief", IncidentAuthorizerName = "AREP", ReturnedOn = Dispatch.AddHours(48) }, User, null, null);
			await _service.ValidateForPortalAsync(f42.CalOesMarsWorkItemId, DeptId, User, null, null);
			var calc = await _service.CalculateExpectedReimbursementAsync(f42.CalOesMarsWorkItemId, DeptId, User, null, null);
			await FluentActions.Awaiting(() => _service.RecordMarsInvoiceAsync(DeptId, "dep-1", new CalOesMarsInvoiceObservation { MarsInvoiceId = "INV-9", InvoicedTotal = 100, CoveredWorkItemIds = { f42.CalOesMarsWorkItemId } }, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_work_item_not_external");
			await _service.RecordExternalSubmissionAsync(f42.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalId = "F42-1" }, User, null, null);
			await _service.RecordExternalStatusAsync(f42.CalOesMarsWorkItemId, DeptId, new CalOesMarsExternalObservation { ExternalStatus = "Approved" }, User, null, null);

			var invoice = await _service.RecordMarsInvoiceAsync(DeptId, "dep-1", new CalOesMarsInvoiceObservation { MarsInvoiceId = "INV-9", InvoiceDate = Dispatch.AddDays(30), InvoicedTotal = calc.ExpectedTotal - 50, PayingEntity = "Cal OES", CoveredWorkItemIds = { f42.CalOesMarsWorkItemId } }, User, null, null);
			invoice.RecordType.Should().Be((int)CalOesMarsRecordTypes.GeneratedInvoice);
			invoice.LocalState.Should().Be((int)CalOesMarsLocalStates.PendingLocalAgencyApproval);
			invoice.MarsInvoiceId.Should().Be("INV-9");
			invoice.ExpectedTotal.Should().Be(calc.ExpectedTotal);
			await FluentActions.Awaiting(() => _service.RecordMarsInvoiceAsync(DeptId, "dep-1", new CalOesMarsInvoiceObservation { MarsInvoiceId = "INV-9", InvoicedTotal = 1 }, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_invoice_duplicate");
			var reconciliation = await _service.GetInvoiceReconciliationAsync(invoice.CalOesMarsWorkItemId, DeptId);
			reconciliation.CoveredItems.Select(c => c.CalOesMarsWorkItemId).Should().Equal(f42.CalOesMarsWorkItemId);
			reconciliation.Variance.Should().Be(-50);

			// A payment cannot be recorded before the local decision; a rejection needs a comment; approval routes to the paying entity.
			await FluentActions.Awaiting(() => _service.RecordPaymentAsync(invoice.CalOesMarsWorkItemId, DeptId, new CalOesMarsPaymentObservation { PaidTotal = 1, PaidOn = Dispatch.AddDays(60) }, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_invoice_not_approved");
			await FluentActions.Awaiting(() => _service.ApproveOrRejectObservedInvoiceAsync(invoice.CalOesMarsWorkItemId, DeptId, false, "Fire Chief", null, User, null, null)).Should().ThrowAsync<InvalidOperationException>().WithMessage("calmars_rejection_comment_required");
			var approved = await _service.ApproveOrRejectObservedInvoiceAsync(invoice.CalOesMarsWorkItemId, DeptId, true, "Fire Chief", "Matches expected within tolerance", User, null, null);
			approved.LocalState.Should().Be((int)CalOesMarsLocalStates.PendingPayingEntityApproval);
			approved.ApprovedByUserId.Should().Be(User);
			_audits.Should().Contain(a => a.Type == AuditLogTypes.CalOesMarsInvoiceApproved);

			var paid = await _service.RecordPaymentAsync(invoice.CalOesMarsWorkItemId, DeptId, new CalOesMarsPaymentObservation { PaidTotal = calc.ExpectedTotal - 50, PaidOn = Dispatch.AddDays(60), PaymentReference = "EFT-77" }, User, null, null);
			paid.LocalState.Should().Be((int)CalOesMarsLocalStates.Paid);
			paid.PaymentReference.Should().Be("EFT-77");
			(await _service.GetWorkItemAsync(f42.CalOesMarsWorkItemId, DeptId)).LocalState.Should().Be((int)CalOesMarsLocalStates.Paid, "the covered F-42 follows the observed payment");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.CalOesMarsPaymentReconciled);
			(await _service.CloseWorkItemAsync(invoice.CalOesMarsWorkItemId, DeptId, User, null, null)).LocalState.Should().Be((int)CalOesMarsLocalStates.Closed);
			// Decision 36: no Phase B service is even a dependency of this service — a MARS invoice never becomes a customer invoice.
			typeof(CalOesMarsService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Should().NotContain(new[] { typeof(IInvoicingService), typeof(IInvoicePaymentsService) });
		}

		[Test]
		public async Task Salary_survey_draft_writes_classification_means_from_phase_e_and_never_an_individual()
		{
			_compensation.Setup(c => c.GetClassificationRateAggregateAsync(DeptId, It.IsAny<DateTime>(), It.IsAny<string>()))
				.ReturnsAsync(new List<(string, int, decimal, decimal)> { ("Captain", 3, 61.3333m, 2m), ("Firefighter", 1, 40m, 0m), ("Dog Handler", 2, 30m, 0m) });
			var survey = await _service.SaveRateProfileAsync(new CalOesMarsRateProfile { DepartmentId = DeptId, SubmissionYear = 2026, SubmissionType = (int)CalOesMarsSubmissionTypes.SalarySurvey, EffectiveOn = new DateTime(2026, 1, 1) }, User, null, null);
			await _service.SaveRateLinesAsync(survey.CalOesMarsRateProfileId, DeptId, new List<CalOesMarsRateLine> { new CalOesMarsRateLine { LineKind = (int)CalOesMarsRateLineKinds.SalarySurvey, ClassificationCode = "Captain", StraightRate = 50, OvertimeRate = 75, IncludesWorkersComp = true, OvertimeEligible = true } }, User, null, null);

			var draft = await _service.BuildSalarySurveyDraftAsync(survey.CalOesMarsRateProfileId, DeptId, new DateTime(2026, 6, 1), User, null, null);
			draft.IsReady.Should().BeTrue();
			draft.LinesWritten.Should().Be(2);
			draft.EmployeesIncluded.Should().Be(4);
			draft.UnknownClassifications.Should().Equal("Dog Handler");
			draft.Classifications.Single(c => c.ClassificationCode == "Captain").StraightRate.Should().Be(61.33m);
			draft.Classifications.Single(c => c.ClassificationCode == "Captain").OvertimeRate.Should().Be(94m, "1.5 × 61.3333 + the $2 per-overtime-hour adder");
			draft.Classifications.Single(c => c.ClassificationCode == "Firefighter").SingleEmployee.Should().BeTrue();

			var profile = await _service.GetRateProfileAsync(survey.CalOesMarsRateProfileId, DeptId);
			var captain = profile.Lines.Single(l => l.ClassificationCode == "Captain");
			captain.StraightRate.Should().Be(61.33m); captain.IncludesWorkersComp.Should().BeTrue("existing line flags survive; only the rates are replaced");
			captain.SourceInputVersions.Should().Contain("workforce-aggregate").And.Contain("\"Employees\":3");
			profile.Lines.Single(l => l.ClassificationCode == "Firefighter").OvertimeRate.Should().Be(60m);
			profile.Lines.Should().NotContain(l => l.ClassificationCode == "Dog Handler");
			_audits.Should().Contain(a => a.Type == AuditLogTypes.CalOesMarsRateDraftBuilt && a.After.Contains("\"LinesWritten\":2") && !a.After.Contains("61.33"), "the audit carries counts, never rates");

			var admin = await _service.SaveRateProfileAsync(new CalOesMarsRateProfile { DepartmentId = DeptId, SubmissionYear = 2026, SubmissionType = (int)CalOesMarsSubmissionTypes.AdministrativeRate, EffectiveOn = new DateTime(2026, 1, 1) }, User, null, null);
			(await _service.BuildSalarySurveyDraftAsync(admin.CalOesMarsRateProfileId, DeptId, new DateTime(2026, 6, 1), User, null, null)).Blockers.Should().Equal("profile_type");
		}

		[Test]
		public async Task Reminder_sweep_sends_one_value_minimized_digest_per_department_per_day()
		{
			await SeedReadyDepartmentAsync();
			_agreements.Single().EndOn = Dispatch.AddDays(10);
			_deployment.Status = (int)DeploymentStatuses.Completed; _deployment.StatusChangedOn = Dispatch.AddDays(-30);
			var f42 = await _service.BuildF42DraftAsync("dep-1", DeptId, "fill-e12", User, null, null);
			await _service.CalculateExpectedReimbursementAsync(f42.CalOesMarsWorkItemId, DeptId, User, null, null);
			var asOf = new DateTime(2031, 3, 5, 12, 0, 0, DateTimeKind.Utc);

			(await _service.RunReminderSweepAsync(asOf)).Should().Be(1);
			_notifications.Should().ContainSingle();
			var digest = _notifications.Single();
			digest.Should().StartWith("Cal OES MARS:");
			digest.Should().Contain("Ridge Fire engine: released").And.Contain("without a ready or submitted F-42");
			digest.Should().Contain("No Salary Survey covers 2031-03-05");
			digest.Should().NotContain("$").And.NotContain(f42.ExpectedTotal?.ToString("N2") ?? "n/a", "digests carry no amounts");
			(await _service.RunReminderSweepAsync(asOf)).Should().Be(0, "one digest per department per day");
			(await _service.RunReminderSweepAsync(asOf, _ => Task.FromResult(false))).Should().Be(0, "a lapsed entitlement is skipped");
		}
	}
}
