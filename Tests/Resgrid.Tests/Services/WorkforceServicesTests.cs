using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.CostRecovery.CalOesMars;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Workforce;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// Workforce &amp; Business Operations plan Phase E (E7 acceptance): overlapping employment periods are refused;
	/// compensation resolves employee → role default → department default; a deployment cost run prices approved DTR
	/// hours, unit usage and expenses, takes the Cal OES MARS recovery as revenue, freezes immutably and is superseded
	/// by a recalculation; the CRD wizard runs create → snapshots → aggregate → validate → freeze/export → certify and
	/// a correction supersedes a frozen run; demographics stay in their own table and the costing engine never
	/// depends on them.
	/// </summary>
	[TestFixture]
	public class WorkforceServicesTests
	{
		private const int DeptId = 9;
		private const string User = "officer";

		private List<WorkforceEmployerProfile> _employers; private List<WorkforceAffiliatedEntity> _affiliates; private List<WorkforceEstablishment> _establishments; private List<WorkforceLaborContractor> _contractors;
		private List<WorkforceWorker> _workers; private List<WorkforceEmployment> _employments; private List<WorkforceJobAssignment> _assignments; private List<WorkforceWorkEntry> _workEntries; private List<WorkforceAnnualPayFact> _facts;
		private List<EmployeeCompensationProfile> _profiles; private List<EmployeePayComponent> _payComponents; private List<EmployeeCostComponent> _costComponents;
		private List<ResourceCostProfile> _resourceProfiles; private List<ResourceCostComponent> _resourceComponents; private List<ResourceUsageEntry> _usage; private List<FieldCostRun> _runs; private List<FieldCostLine> _lines;
		private List<PayDataReportingDemographic> _demographics; private List<PayDataReportRun> _reportRuns; private List<PayDataReportEmployeeSnapshot> _snapshots; private List<PayDataReportRow> _rows; private List<PayDataExportArtifact> _artifacts;
		private List<AuditEvent> _audits; private List<string> _notifications;
		private Dictionary<string, DepartmentMember> _memberStates;
		private List<Deployment> _deployments; private List<DeploymentPersonnel> _personnel; private List<DeploymentUnit> _units; private List<DeploymentTimeReport> _reports; private List<DeploymentTimeEntry> _entries; private List<DeploymentExpense> _expenses;
		private List<CalOesMarsWorkItem> _marsItems;
		private Mock<IUnitOfWork> _unitOfWork; private Mock<IEmployeePayComponentRepository> _payComponentRepository; private int _commits; private int _discards;
		private WorkforceService _workforce; private CompensationCostService _compensation; private FieldCostingService _costing; private PayDataDemographicsService _demographicsService; private CaPayDataReportingService _reporting;

		[SetUp]
		public void SetUp()
		{
			_employers = new(); _affiliates = new(); _establishments = new(); _contractors = new(); _workers = new(); _employments = new(); _assignments = new(); _workEntries = new(); _facts = new();
			_profiles = new(); _payComponents = new(); _costComponents = new(); _resourceProfiles = new(); _resourceComponents = new(); _usage = new(); _runs = new(); _lines = new();
			_demographics = new(); _reportRuns = new(); _snapshots = new(); _rows = new(); _artifacts = new(); _audits = new(); _notifications = new();
			_deployments = new(); _personnel = new(); _units = new(); _reports = new(); _entries = new(); _expenses = new(); _marsItems = new();

			var employers = Repo<IWorkforceEmployerProfileRepository, WorkforceEmployerProfile>(_employers, e => e.WorkforceEmployerProfileId, (e, v) => e.WorkforceEmployerProfileId = v);
			employers.Setup(r => r.GetActiveForDepartmentAsync(DeptId)).ReturnsAsync(() => _employers.FirstOrDefault(e => !e.IsDeleted && e.IsActive));
			employers.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _employers.Where(e => !e.IsDeleted).ToList());
			employers.Setup(r => r.GetDepartmentsWithActiveProfilesAsync()).ReturnsAsync(() => _employers.Where(e => !e.IsDeleted && e.IsActive).Select(e => e.DepartmentId).Distinct().ToList());
			var affiliates = Repo<IWorkforceAffiliatedEntityRepository, WorkforceAffiliatedEntity>(_affiliates, e => e.WorkforceAffiliatedEntityId, (e, v) => e.WorkforceAffiliatedEntityId = v);
			affiliates.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _affiliates.Where(e => !e.IsDeleted).ToList());
			affiliates.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _affiliates.FirstOrDefault(e => e.WorkforceAffiliatedEntityId == id));
			var establishments = Repo<IWorkforceEstablishmentRepository, WorkforceEstablishment>(_establishments, e => e.WorkforceEstablishmentId, (e, v) => e.WorkforceEstablishmentId = v);
			establishments.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _establishments.Where(e => !e.IsDeleted).ToList());
			establishments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _establishments.FirstOrDefault(e => e.WorkforceEstablishmentId == id));
			var contractors = Repo<IWorkforceLaborContractorRepository, WorkforceLaborContractor>(_contractors, e => e.WorkforceLaborContractorId, (e, v) => e.WorkforceLaborContractorId = v);
			contractors.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _contractors.Where(e => !e.IsDeleted).ToList());
			contractors.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _contractors.FirstOrDefault(e => e.WorkforceLaborContractorId == id));
			var workers = Repo<IWorkforceWorkerRepository, WorkforceWorker>(_workers, e => e.WorkforceWorkerId, (e, v) => e.WorkforceWorkerId = v);
			workers.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _workers.Where(e => !e.IsDeleted).ToList());
			workers.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _workers.FirstOrDefault(e => e.WorkforceWorkerId == id));
			workers.Setup(r => r.GetByUserIdAsync(DeptId, It.IsAny<string>())).ReturnsAsync((int _, string u) => _workers.FirstOrDefault(e => !e.IsDeleted && e.UserId == u));
			var employments = Repo<IWorkforceEmploymentRepository, WorkforceEmployment>(_employments, e => e.WorkforceEmploymentId, (e, v) => e.WorkforceEmploymentId = v);
			employments.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _employments.Where(e => !e.IsDeleted).ToList());
			employments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _employments.FirstOrDefault(e => e.WorkforceEmploymentId == id));
			employments.Setup(r => r.GetByWorkerAsync(It.IsAny<string>())).ReturnsAsync((string w) => _employments.Where(e => !e.IsDeleted && e.WorkforceWorkerId == w).ToList());
			employments.Setup(r => r.GetActiveInWindowAsync(DeptId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime f, DateTime t) => _employments.Where(e => !e.IsDeleted && e.Covers(f, t)).ToList());
			var assignments = Repo<IWorkforceJobAssignmentRepository, WorkforceJobAssignment>(_assignments, e => e.WorkforceJobAssignmentId, (e, v) => e.WorkforceJobAssignmentId = v);
			assignments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _assignments.FirstOrDefault(e => e.WorkforceJobAssignmentId == id));
			assignments.Setup(r => r.GetByEmploymentAsync(It.IsAny<string>())).ReturnsAsync((string e) => _assignments.Where(a => !a.IsDeleted && a.WorkforceEmploymentId == e).ToList());
			assignments.Setup(r => r.GetByEmploymentsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _assignments.Where(a => !a.IsDeleted && set.Contains(a.WorkforceEmploymentId)).ToList(); });
			var workEntries = Repo<IWorkforceWorkEntryRepository, WorkforceWorkEntry>(_workEntries, e => e.WorkforceWorkEntryId, (e, v) => e.WorkforceWorkEntryId = v);
			workEntries.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _workEntries.FirstOrDefault(e => e.WorkforceWorkEntryId == id));
			workEntries.Setup(r => r.GetByExternalIdAsync(DeptId, It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((int _, string s, string id) => _workEntries.FirstOrDefault(e => e.ExternalSource == s && e.ExternalId == id));
			workEntries.Setup(r => r.GetForDepartmentInWindowAsync(DeptId, It.IsAny<DateTime>(), It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime f, DateTime t) => _workEntries.Where(e => !e.IsDeleted && e.WorkDate >= f && e.WorkDate <= t).ToList());
			workEntries.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _workEntries.Where(e => e.DeploymentId == d).ToList());
			workEntries.Setup(r => r.GetByCallAsync(It.IsAny<int>())).ReturnsAsync((int c) => _workEntries.Where(e => e.CallId == c).ToList());
			var facts = Repo<IWorkforceAnnualPayFactRepository, WorkforceAnnualPayFact>(_facts, e => e.WorkforceAnnualPayFactId, (e, v) => e.WorkforceAnnualPayFactId = v);
			facts.Setup(r => r.GetForYearAsync(DeptId, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((int _, int y, int t) => _facts.Where(f => !f.IsDeleted && f.ReportingYear == y && f.ReportType == t).ToList());
			facts.Setup(r => r.GetByEmploymentAsync(It.IsAny<string>())).ReturnsAsync((string e) => _facts.Where(f => f.WorkforceEmploymentId == e).ToList());
			var profiles = Repo<IEmployeeCompensationProfileRepository, EmployeeCompensationProfile>(_profiles, e => e.EmployeeCompensationProfileId, (e, v) => e.EmployeeCompensationProfileId = v);
			profiles.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _profiles.FirstOrDefault(e => e.EmployeeCompensationProfileId == id));
			profiles.Setup(r => r.GetByEmploymentAsync(It.IsAny<string>())).ReturnsAsync((string e) => _profiles.Where(p => p.WorkforceEmploymentId == e).ToList());
			profiles.Setup(r => r.GetByEmploymentsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _profiles.Where(p => set.Contains(p.WorkforceEmploymentId)).ToList(); });
			profiles.Setup(r => r.GetDefaultsForDepartmentAsync(DeptId)).ReturnsAsync(() => _profiles.Where(p => p.Scope != (int)CompensationScopes.Employee).ToList());
			var payComponents = Repo<IEmployeePayComponentRepository, EmployeePayComponent>(_payComponents, e => e.EmployeePayComponentId, (e, v) => e.EmployeePayComponentId = v);
			payComponents.Setup(r => r.GetByProfileAsync(It.IsAny<string>())).ReturnsAsync((string p) => _payComponents.Where(c => c.EmployeeCompensationProfileId == p).ToList());
			payComponents.Setup(r => r.GetByProfilesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _payComponents.Where(c => set.Contains(c.EmployeeCompensationProfileId)).ToList(); });
			var costComponents = Repo<IEmployeeCostComponentRepository, EmployeeCostComponent>(_costComponents, e => e.EmployeeCostComponentId, (e, v) => e.EmployeeCostComponentId = v);
			costComponents.Setup(r => r.GetByProfileAsync(It.IsAny<string>())).ReturnsAsync((string p) => _costComponents.Where(c => c.EmployeeCompensationProfileId == p).ToList());
			costComponents.Setup(r => r.GetByProfilesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _costComponents.Where(c => set.Contains(c.EmployeeCompensationProfileId)).ToList(); });
			var resourceProfiles = Repo<IResourceCostProfileRepository, ResourceCostProfile>(_resourceProfiles, e => e.ResourceCostProfileId, (e, v) => e.ResourceCostProfileId = v);
			resourceProfiles.Setup(r => r.GetForDepartmentAsync(DeptId)).ReturnsAsync(() => _resourceProfiles.ToList());
			resourceProfiles.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _resourceProfiles.FirstOrDefault(e => e.ResourceCostProfileId == id));
			var resourceComponents = Repo<IResourceCostComponentRepository, ResourceCostComponent>(_resourceComponents, e => e.ResourceCostComponentId, (e, v) => e.ResourceCostComponentId = v);
			resourceComponents.Setup(r => r.GetByProfileAsync(It.IsAny<string>())).ReturnsAsync((string p) => _resourceComponents.Where(c => c.ResourceCostProfileId == p).ToList());
			resourceComponents.Setup(r => r.GetByProfilesAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync((IEnumerable<string> ids) => { var set = ids.ToHashSet(); return _resourceComponents.Where(c => set.Contains(c.ResourceCostProfileId)).ToList(); });
			var usage = Repo<IResourceUsageEntryRepository, ResourceUsageEntry>(_usage, e => e.ResourceUsageEntryId, (e, v) => e.ResourceUsageEntryId = v);
			usage.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _usage.FirstOrDefault(e => e.ResourceUsageEntryId == id));
			usage.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _usage.Where(u => u.DeploymentId == d).ToList());
			usage.Setup(r => r.GetByCallAsync(It.IsAny<int>())).ReturnsAsync((int c) => _usage.Where(u => u.CallId == c).ToList());
			var runs = Repo<IFieldCostRunRepository, FieldCostRun>(_runs, e => e.FieldCostRunId, (e, v) => e.FieldCostRunId = v);
			runs.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _runs.FirstOrDefault(e => e.FieldCostRunId == id));
			runs.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string d, int _) => _runs.Where(x => x.DeploymentId == d).ToList());
			runs.Setup(r => r.GetByBidAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string b, int _) => _runs.Where(x => x.BidId == b).ToList());
			runs.Setup(r => r.GetByCallAsync(It.IsAny<int>(), DeptId)).ReturnsAsync((int c, int _) => _runs.Where(x => x.CallId == c).ToList());
			runs.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(() => _runs.ToList());
			var lines = Repo<IFieldCostLineRepository, FieldCostLine>(_lines, e => e.FieldCostLineId, (e, v) => e.FieldCostLineId = v);
			lines.Setup(r => r.GetByRunAsync(It.IsAny<string>())).ReturnsAsync((string run) => _lines.Where(l => l.FieldCostRunId == run).ToList());
			lines.Setup(r => r.DeleteByRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string run, CancellationToken _) => _lines.RemoveAll(l => l.FieldCostRunId == run) > 0);
			var demographics = Repo<IPayDataReportingDemographicRepository, PayDataReportingDemographic>(_demographics, e => e.PayDataReportingDemographicId, (e, v) => e.PayDataReportingDemographicId = v);
			demographics.Setup(r => r.GetCurrentForWorkerAsync(It.IsAny<string>(), It.IsAny<DateTime>())).ReturnsAsync((string w, DateTime d) => _demographics.Where(x => !x.IsDeleted && x.WorkforceWorkerId == w && x.EffectiveOn <= d && (!x.ExpiresOn.HasValue || x.ExpiresOn >= d)).OrderByDescending(x => x.Version).FirstOrDefault());
			demographics.Setup(r => r.GetCurrentForDepartmentAsync(DeptId, It.IsAny<DateTime>())).ReturnsAsync((int _, DateTime d) => _demographics.Where(x => !x.IsDeleted && x.EffectiveOn <= d && (!x.ExpiresOn.HasValue || x.ExpiresOn >= d)).ToList());
			var reportRuns = Repo<IPayDataReportRunRepository, PayDataReportRun>(_reportRuns, e => e.PayDataReportRunId, (e, v) => e.PayDataReportRunId = v);
			reportRuns.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _reportRuns.FirstOrDefault(e => e.PayDataReportRunId == id));
			reportRuns.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<int?>())).ReturnsAsync((int _, int? y) => _reportRuns.Where(x => !y.HasValue || x.ReportingYear == y).ToList());
			reportRuns.Setup(r => r.GetDepartmentsWithRunsAsync(It.IsAny<int>())).ReturnsAsync((int y) => _reportRuns.Where(x => x.ReportingYear == y).Select(x => x.DepartmentId).Distinct().ToList());
			var snapshots = Repo<IPayDataReportEmployeeSnapshotRepository, PayDataReportEmployeeSnapshot>(_snapshots, e => e.PayDataReportEmployeeSnapshotId, (e, v) => e.PayDataReportEmployeeSnapshotId = v);
			snapshots.Setup(r => r.GetByRunAsync(It.IsAny<string>())).ReturnsAsync((string run) => _snapshots.Where(s => s.PayDataReportRunId == run).ToList());
			snapshots.Setup(r => r.DeleteByRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string run, CancellationToken _) => _snapshots.RemoveAll(s => s.PayDataReportRunId == run) > 0);
			var rows = Repo<IPayDataReportRowRepository, PayDataReportRow>(_rows, e => e.PayDataReportRowId, (e, v) => e.PayDataReportRowId = v);
			rows.Setup(r => r.GetByRunAsync(It.IsAny<string>())).ReturnsAsync((string run) => _rows.Where(s => s.PayDataReportRunId == run).ToList());
			rows.Setup(r => r.DeleteByRunAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string run, CancellationToken _) => _rows.RemoveAll(s => s.PayDataReportRunId == run) > 0);
			var artifacts = Repo<IPayDataExportArtifactRepository, PayDataExportArtifact>(_artifacts, e => e.PayDataExportArtifactId, (e, v) => e.PayDataExportArtifactId = v);
			artifacts.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _artifacts.FirstOrDefault(e => e.PayDataExportArtifactId == id));
			artifacts.Setup(r => r.GetByRunAsync(It.IsAny<string>())).ReturnsAsync((string run) => _artifacts.Where(a => a.PayDataReportRunId == run).ToList());
			artifacts.Setup(r => r.GetExpiredUnpurgedAsync(It.IsAny<DateTime>())).ReturnsAsync((DateTime d) => _artifacts.Where(a => !a.PurgedOn.HasValue && a.ExpiresOn <= d).ToList());

			var deployments = new Mock<IDeploymentRepository>();
			deployments.Setup(r => r.GetByIdForDepartmentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string id, int _) => _deployments.FirstOrDefault(d => d.DeploymentId == id));
			var personnel = new Mock<IDeploymentPersonnelRepository>();
			personnel.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _personnel.Where(p => p.DeploymentId == d).ToList());
			var units = new Mock<IDeploymentUnitRepository>();
			units.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _units.Where(p => p.DeploymentId == d).ToList());
			var reports = new Mock<IDeploymentTimeReportRepository>();
			reports.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _reports.Where(p => p.DeploymentId == d).ToList());
			var entries = new Mock<IDeploymentTimeEntryRepository>();
			entries.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _entries.Where(p => p.DeploymentId == d).ToList());
			var expenses = new Mock<IDeploymentExpenseRepository>();
			expenses.Setup(r => r.GetByDeploymentAsync(It.IsAny<string>())).ReturnsAsync((string d) => _expenses.Where(p => p.DeploymentId == d).ToList());
			var invoices = new Mock<IInvoiceRepository>();
			invoices.Setup(r => r.GetForDepartmentAsync(DeptId, It.IsAny<InvoiceListFilter>())).ReturnsAsync(new List<Invoice>());
			var bids = new Mock<IBidRepository>(); var bidLines = new Mock<IBidLineItemRepository>();
			var mars = new Mock<ICalOesMarsService>();
			mars.Setup(m => m.GetWorkItemsForDeploymentAsync(It.IsAny<string>(), DeptId)).ReturnsAsync((string d, int _) => _marsItems.Where(w => w.DeploymentId == d).ToList());
			var unitsService = new Mock<IUnitsService>();
			unitsService.Setup(u => u.GetUnitsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Unit> { new Unit { UnitId = 5, DepartmentId = DeptId, Name = "Engine 1" } });
			unitsService.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = DeptId, Name = "Engine 1" });
			var userProfiles = new Mock<IUserProfileService>();
			userProfiles.Setup(p => p.GetSelectedUserProfilesAsync(It.IsAny<List<string>>())).ReturnsAsync((List<string> ids) => ids.Select(id => new UserProfile { UserId = id, FirstName = "Member", LastName = id }).ToList());
			var departments = new Mock<IDepartmentsService>();
			departments.Setup(d => d.GetDepartmentByIdAsync(DeptId, It.IsAny<bool>())).ReturnsAsync(new Department { DepartmentId = DeptId, Name = "Dept" });
			departments.Setup(d => d.GetAllAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			departments.Setup(d => d.GetActiveAdminsForDepartmentAsync(DeptId)).ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { UserId = "admin" } });
			// Everyone is a current member of the department unless a test gives them another membership state (null = not a member).
			_memberStates = new Dictionary<string, DepartmentMember>(StringComparer.OrdinalIgnoreCase);
			departments.Setup(d => d.GetDepartmentMemberAsync(It.IsAny<string>(), DeptId, true)).ReturnsAsync((string id, int dept, bool _) => _memberStates.TryGetValue(id, out var state) ? state : new DepartmentMember { DepartmentId = dept, UserId = id });
			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), DeptId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.ReturnsAsync((string u, int d, string m, string n, Department dep, string t, UserProfile p, bool ic) => { _notifications.Add(m); return true; });
			var events = new Mock<IEventAggregator>();
			events.Setup(e => e.SendMessage<AuditEvent>(It.IsAny<AuditEvent>())).Callback<AuditEvent>(a => _audits.Add(a));

			_workforce = new WorkforceService(employers.Object, affiliates.Object, establishments.Object, contractors.Object, workers.Object, employments.Object, assignments.Object, workEntries.Object, facts.Object, userProfiles.Object, events.Object, departments.Object);
			_payComponentRepository = payComponents;
			_unitOfWork = TransactionalStores(_profiles, _payComponents, _costComponents);
			_compensation = new CompensationCostService(profiles.Object, payComponents.Object, costComponents.Object, employments.Object, assignments.Object, events.Object, _unitOfWork.Object);
			_costing = new FieldCostingService(resourceProfiles.Object, resourceComponents.Object, usage.Object, runs.Object, lines.Object, workers.Object, employments.Object, workEntries.Object, _compensation,
				bids.Object, bidLines.Object, deployments.Object, personnel.Object, units.Object, reports.Object, entries.Object, expenses.Object, invoices.Object, new Lazy<ICalOesMarsService>(() => mars.Object), unitsService.Object, events.Object);
			_demographicsService = new PayDataDemographicsService(demographics.Object, workers.Object, employments.Object, _workforce, events.Object);
			_reporting = new CaPayDataReportingService(reportRuns.Object, snapshots.Object, rows.Object, artifacts.Object, employers.Object, affiliates.Object, establishments.Object, contractors.Object, workers.Object, employments.Object, assignments.Object,
				workEntries.Object, facts.Object, demographics.Object, userProfiles.Object, departments.Object, new Lazy<ICommunicationService>(() => communication.Object), null, events.Object);
		}

		/// <summary>Stands in for the database transaction: the in-memory stores are restored on discard, kept on commit.</summary>
		private Mock<IUnitOfWork> TransactionalStores(List<EmployeeCompensationProfile> profiles, List<EmployeePayComponent> pay, List<EmployeeCostComponent> cost)
		{
			_commits = 0; _discards = 0;
			(List<EmployeeCompensationProfile> Profiles, List<EmployeePayComponent> Pay, List<EmployeeCostComponent> Cost)? snapshot = null;
			var unitOfWork = new Mock<IUnitOfWork>();
			unitOfWork.Setup(u => u.CreateOrGetConnectionAsync(It.IsAny<CancellationToken>()))
				.Callback(() => snapshot ??= (profiles.ToList(), pay.ToList(), cost.ToList()))
				.ReturnsAsync((System.Data.Common.DbConnection)null);
			unitOfWork.Setup(u => u.CommitChanges()).Callback(() => { _commits++; snapshot = null; });
			unitOfWork.Setup(u => u.DiscardChanges()).Callback(() =>
			{
				_discards++;
				if (snapshot == null) return;
				profiles.Clear(); profiles.AddRange(snapshot.Value.Profiles);
				pay.Clear(); pay.AddRange(snapshot.Value.Pay);
				cost.Clear(); cost.AddRange(snapshot.Value.Cost);
				snapshot = null;
			});
			return unitOfWork;
		}

		private static Mock<TRepo> Repo<TRepo, T>(List<T> store, Func<T, string> id, Action<T, string> setId) where TRepo : class, IRepository<T> where T : class, IEntity
		{
			var mock = new Mock<TRepo>();
			mock.Setup(r => r.SaveOrUpdateAsync(It.IsAny<T>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((T entity, CancellationToken _, bool __) => { if (string.IsNullOrWhiteSpace(id(entity))) setId(entity, Guid.NewGuid().ToString()); store.RemoveAll(x => id(x) == id(entity)); store.Add(entity); return entity; });
			return mock;
		}

		private async Task<(WorkforceWorker Worker, WorkforceEmployment Employment, WorkforceEstablishment Establishment)> SeedWorkerAsync(string userId = "u1", int? roleId = null)
		{
			var establishment = _establishments.FirstOrDefault() ?? await _workforce.SaveEstablishmentAsync(new WorkforceEstablishment { DepartmentId = DeptId, Code = "HQ", Name = "Station 1", PhysicalAddress = "1 Main St", City = "Sacramento", StateCode = "CA", PostalCode = "95814", Naics = "922160", MajorActivity = "Fire protection", IsHeadquarters = true }, User, null, null);
			var worker = await _workforce.GetOrCreateWorkerForUserAsync(DeptId, userId, User);
			var employment = await _workforce.SaveEmploymentAsync(new WorkforceEmployment { DepartmentId = DeptId, WorkforceWorkerId = worker.WorkforceWorkerId, WorkerKind = (int)WorkerKinds.PayrollEmployee, StartOn = new DateTime(2024, 1, 1), EmploymentType = (int)EmploymentTypes.FullTime, ExemptionStatus = (int)ExemptionStatuses.NonExempt, CaliforniaEmployeeBasis = (int)CaliforniaEmployeeBases.Both, DefaultEstablishmentId = establishment.WorkforceEstablishmentId, PersonnelRoleId = roleId }, User, null, null);
			await _workforce.SaveJobAssignmentAsync(new WorkforceJobAssignment { DepartmentId = DeptId, WorkforceEmploymentId = employment.WorkforceEmploymentId, EffectiveOn = new DateTime(2024, 1, 1), JobTitle = "Firefighter", JobCategoryCode = "10", WorkforceEstablishmentId = establishment.WorkforceEstablishmentId, WorkMode = (int)WorkModes.NonRemote, WorkCountry = "US", WorkSubdivision = "CA" }, User, null, null);
			return (worker, employment, establishment);
		}

		[Test]
		public async Task A_worker_row_links_only_a_current_member_of_the_department()
		{
			_memberStates["removed"] = new DepartmentMember { DepartmentId = DeptId, UserId = "removed", IsDeleted = true };
			_memberStates["disabled"] = new DepartmentMember { DepartmentId = DeptId, UserId = "disabled", IsDisabled = true };
			_memberStates["stranger"] = null;
			_memberStates["hidden"] = new DepartmentMember { DepartmentId = DeptId, UserId = "hidden", IsHidden = true };
			foreach (var refused in new[] { "removed", "disabled", "stranger" })
			{
				Func<Task> create = () => _workforce.GetOrCreateWorkerForUserAsync(DeptId, refused, User);
				await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_member_not_found");
				Func<Task> link = () => _workforce.SaveWorkerAsync(new WorkforceWorker { DepartmentId = DeptId, UserId = refused }, User, null, null);
				await link.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_member_not_found");
			}
			_workers.Should().BeEmpty();
			(await _workforce.GetOrCreateWorkerForUserAsync(DeptId, "hidden", User)).UserId.Should().Be("hidden", "a hidden member may still hold a worker row");

			// An existing row keeps working after its member leaves; only a new link is checked.
			var kept = await _workforce.GetOrCreateWorkerForUserAsync(DeptId, "u1", User);
			_memberStates["u1"] = new DepartmentMember { DepartmentId = DeptId, UserId = "u1", IsDisabled = true };
			(await _workforce.GetOrCreateWorkerForUserAsync(DeptId, "u1", User)).WorkforceWorkerId.Should().Be(kept.WorkforceWorkerId);
		}

		[Test]
		public async Task A_departed_members_open_employment_ends_on_the_removal_day_and_leaves_the_current_counts()
		{
			var (worker, employment, _) = await SeedWorkerAsync("leaver");
			var earlier = new WorkforceEmployment { WorkforceEmploymentId = "earlier", DepartmentId = DeptId, WorkforceWorkerId = worker.WorkforceWorkerId, WorkerKind = (int)WorkerKinds.PayrollEmployee, StartOn = new DateTime(2019, 1, 1), EndOn = new DateTime(2021, 12, 31) };
			var removalDay = DateTime.UtcNow.Date;
			var notStarted = new WorkforceEmployment { WorkforceEmploymentId = "not-started", DepartmentId = DeptId, WorkforceWorkerId = worker.WorkforceWorkerId, WorkerKind = (int)WorkerKinds.PayrollEmployee, StartOn = removalDay.AddDays(30) };
			_employments.Add(earlier); _employments.Add(notStarted);
			(await _demographicsService.GetCompletenessAsync(DeptId, removalDay.AddDays(1))).ActiveWorkers.Should().Be(1);
			_audits.Clear();

			(await _workforce.EndEmploymentsForMemberAsync(DeptId, "leaver", removalDay, "chief")).Should().Be(2);

			_employments.Single(e => e.WorkforceEmploymentId == employment.WorkforceEmploymentId).EndOn.Should().Be(removalDay, "end-dated, never deleted: past periods keep the member");
			_employments.Single(e => e.WorkforceEmploymentId == employment.WorkforceEmploymentId).IsDeleted.Should().BeFalse();
			earlier.EndOn.Should().Be(new DateTime(2021, 12, 31), "an employment that already ended is left alone");
			notStarted.IsDeleted.Should().BeTrue("an employment that had not begun is withdrawn rather than ended before its start");
			_audits.Should().HaveCount(2).And.OnlyContain(a => a.Type == AuditLogTypes.WorkforceEmploymentChanged && a.UserId == "chief");
			(await _demographicsService.GetCompletenessAsync(DeptId, removalDay)).ActiveWorkers.Should().Be(1, "the removal day itself is still worked");
			(await _demographicsService.GetCompletenessAsync(DeptId, removalDay.AddDays(1))).ActiveWorkers.Should().Be(0, "current-as-of counts no longer include the member");
			(await _demographicsService.GetCompletenessAsync(DeptId, new DateTime(2024, 6, 1))).ActiveWorkers.Should().Be(1, "a past period still counts them");

			(await _workforce.EndEmploymentsForMemberAsync(DeptId, "leaver", removalDay, "chief")).Should().Be(0, "a second removal pass changes nothing");
			(await _workforce.EndEmploymentsForMemberAsync(DeptId, "no-worker-row", removalDay, "chief")).Should().Be(0);
		}

		[Test]
		public async Task Employment_periods_and_job_assignments_never_overlap()
		{
			var (worker, employment, establishment) = await SeedWorkerAsync();
			Func<Task> overlap = () => _workforce.SaveEmploymentAsync(new WorkforceEmployment { DepartmentId = DeptId, WorkforceWorkerId = worker.WorkforceWorkerId, WorkerKind = (int)WorkerKinds.PayrollEmployee, StartOn = new DateTime(2025, 6, 1) }, User, null, null);
			await overlap.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_employment_overlap");
			Func<Task> assignment = () => _workforce.SaveJobAssignmentAsync(new WorkforceJobAssignment { DepartmentId = DeptId, WorkforceEmploymentId = employment.WorkforceEmploymentId, EffectiveOn = new DateTime(2025, 1, 1), JobCategoryCode = "2", WorkforceEstablishmentId = establishment.WorkforceEstablishmentId }, User, null, null);
			await assignment.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_assignment_overlap");
			Func<Task> unknownCategory = () => _workforce.SaveJobAssignmentAsync(new WorkforceJobAssignment { DepartmentId = DeptId, WorkforceEmploymentId = employment.WorkforceEmploymentId, EffectiveOn = new DateTime(2030, 1, 1), JobCategoryCode = "99" }, User, null, null);
			await unknownCategory.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_job_category_invalid");
			// A closed period followed by a new one is fine.
			employment.EndOn = new DateTime(2025, 12, 31);
			await _workforce.SaveEmploymentAsync(employment, User, null, null);
			var next = await _workforce.SaveEmploymentAsync(new WorkforceEmployment { DepartmentId = DeptId, WorkforceWorkerId = worker.WorkforceWorkerId, WorkerKind = (int)WorkerKinds.PayrollEmployee, StartOn = new DateTime(2026, 1, 1) }, User, null, null);
			next.WorkforceEmploymentId.Should().NotBe(employment.WorkforceEmploymentId);
			(await _workforce.GetEmploymentsForWorkerAsync(worker.WorkforceWorkerId, DeptId)).Should().HaveCount(2);
			_audits.Should().Contain(a => a.Type == AuditLogTypes.WorkforceEmploymentChanged);
		}

		[Test]
		public async Task Saving_a_profile_with_components_commits_both_writes_and_audits_after_commit()
		{
			var saved = await _compensation.SaveProfileWithComponentsAsync(
				new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.DepartmentDefault, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 20m, EffectiveOn = new DateTime(2024, 1, 1) },
				new List<EmployeePayComponent> { new EmployeePayComponent { Category = (int)PayComponentCategories.Ems, Basis = (int)PayComponentBases.PerHour, AmountValue = 4m } },
				new List<EmployeeCostComponent> { new EmployeeCostComponent { Category = (int)CostComponentCategories.EmployerPayrollTax, Basis = (int)CostComponentBases.PercentOfEligiblePay, RateAmountValue = 7.65m } }, User, null, null);

			_profiles.Should().ContainSingle().Which.EmployeeCompensationProfileId.Should().Be(saved.EmployeeCompensationProfileId);
			saved.PayComponents.Should().ContainSingle(); saved.CostComponents.Should().ContainSingle();
			_commits.Should().Be(1); _discards.Should().Be(0);
			_audits.Where(a => a.Type == AuditLogTypes.WorkforceCompensationChanged).Should().HaveCount(2, "the profile save and the component replacement are audited as when saved separately");
		}

		[Test]
		public async Task A_failed_component_write_leaves_no_profile_row_and_no_audit()
		{
			_payComponentRepository.Setup(r => r.SaveOrUpdateAsync(It.IsAny<EmployeePayComponent>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ThrowsAsync(new InvalidOperationException("component insert failed"));

			Func<Task> save = () => _compensation.SaveProfileWithComponentsAsync(
				new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.DepartmentDefault, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 20m, EffectiveOn = new DateTime(2024, 1, 1) },
				new List<EmployeePayComponent> { new EmployeePayComponent { Category = (int)PayComponentCategories.Ems, Basis = (int)PayComponentBases.PerHour, AmountValue = 4m } },
				new List<EmployeeCostComponent>(), User, null, null);

			await save.Should().ThrowAsync<InvalidOperationException>().WithMessage("component insert failed");
			_profiles.Should().BeEmpty("the profile insert rolls back with the failed component write");
			_payComponents.Should().BeEmpty();
			_commits.Should().Be(0); _discards.Should().Be(1);
			_audits.Should().NotContain(a => a.Type == AuditLogTypes.WorkforceCompensationChanged, "nothing was saved, so nothing is audited");
		}

		[Test]
		public async Task Compensation_resolves_employee_then_role_default_then_department_default_and_prices_the_fixture()
		{
			var (_, employment, _) = await SeedWorkerAsync(roleId: 3);
			var department = await _compensation.SaveProfileAsync(new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.DepartmentDefault, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 20m, EffectiveOn = new DateTime(2024, 1, 1) }, User, null, null);
			await _compensation.ApproveProfileAsync(department.EmployeeCompensationProfileId, DeptId, User, null, null);
			var resolved = await _compensation.ResolveProfileAsync(employment.WorkforceEmploymentId, null, DeptId, new DateTime(2026, 6, 1));
			resolved.Profile.Scope.Should().Be((int)CompensationScopes.DepartmentDefault); resolved.IsFallback.Should().BeTrue();

			var role = await _compensation.SaveProfileAsync(new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.RoleDefault, PersonnelRoleId = 3, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 25m, EffectiveOn = new DateTime(2024, 1, 1) }, User, null, null);
			await _compensation.ApproveProfileAsync(role.EmployeeCompensationProfileId, DeptId, User, null, null);
			resolved = await _compensation.ResolveProfileAsync(employment.WorkforceEmploymentId, null, DeptId, new DateTime(2026, 6, 1));
			resolved.Profile.Scope.Should().Be((int)CompensationScopes.RoleDefault, "the employment's personnel role picks the role default");

			var employee = await _compensation.SaveProfileAsync(new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.Employee, WorkforceEmploymentId = employment.WorkforceEmploymentId, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 30m, EffectiveOn = new DateTime(2024, 1, 1), RateMultipliersJson = "{\"Overtime\":1.5}" }, User, null, null);
			await _compensation.SaveComponentsAsync(employee.EmployeeCompensationProfileId, DeptId,
				new List<EmployeePayComponent> { new EmployeePayComponent { Category = (int)PayComponentCategories.Ems, Basis = (int)PayComponentBases.PerHour, AmountValue = 4m, PaidForEachOvertimeHour = true } },
				new List<EmployeeCostComponent> { new EmployeeCostComponent { Category = (int)CostComponentCategories.EmployerPayrollTax, Basis = (int)CostComponentBases.PercentOfEligiblePay, RateAmountValue = 35m } }, User, null, null);
			await _compensation.ApproveProfileAsync(employee.EmployeeCompensationProfileId, DeptId, User, null, null);
			resolved = await _compensation.ResolveProfileAsync(employment.WorkforceEmploymentId, null, DeptId, new DateTime(2026, 6, 1));
			resolved.Profile.Scope.Should().Be((int)CompensationScopes.Employee); resolved.IsFallback.Should().BeFalse();
			resolved.Profile.PayComponents.Should().HaveCount(1); resolved.Profile.CostComponents.Should().HaveCount(1);

			var regular = await _compensation.CalculateLoadedCostAsync(DeptId, new LaborWorkQuantity { WorkforceEmploymentId = employment.WorkforceEmploymentId, PayCode = (int)PayCodes.Regular, Hours = 8 }, null, new DateTime(2026, 6, 1));
			var overtime = await _compensation.CalculateLoadedCostAsync(DeptId, new LaborWorkQuantity { WorkforceEmploymentId = employment.WorkforceEmploymentId, PayCode = (int)PayCodes.Overtime, Hours = 4 }, null, new DateTime(2026, 6, 1));
			(regular.LoadedCost + overtime.LoadedCost).Should().Be(631.8m);
			regular.NeedsReview.Should().BeFalse();

			// Editing a rate removes the approval; the Salary Survey aggregate never returns an individual.
			var edit = employee.CloneJson();
			edit.BaseAmountValue = 31m;
			var edited = await _compensation.SaveProfileAsync(edit, User, null, null);
			edited.IsApproved.Should().BeFalse();
			(await _compensation.GetClassificationRateAggregateAsync(DeptId, new DateTime(2026, 6, 1), null)).Should().BeEmpty("no assignment carries a MARS classification and unapproved profiles never feed the survey");
		}

		[Test]
		public async Task Deployment_cost_run_prices_dtr_hours_usage_and_expenses_takes_mars_recovery_as_revenue_and_freezes_immutably()
		{
			var (worker, employment, _) = await SeedWorkerAsync();
			var profile = await _compensation.SaveProfileAsync(new EmployeeCompensationProfile { DepartmentId = DeptId, Scope = (int)CompensationScopes.Employee, WorkforceEmploymentId = employment.WorkforceEmploymentId, PayBasis = (int)PayBases.Hourly, BaseAmountValue = 30m, EffectiveOn = new DateTime(2024, 1, 1) }, User, null, null);
			await _compensation.ApproveProfileAsync(profile.EmployeeCompensationProfileId, DeptId, User, null, null);
			var engine = await _costing.SaveResourceProfileAsync(new ResourceCostProfile { DepartmentId = DeptId, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = 5, Name = "Engine 1", AcquisitionCost = 60000m, SalvageValue = 12000m, UsefulLifeQuantity = 40000m, AllocationBasis = (int)AllocationBases.Mile, EffectiveOn = new DateTime(2024, 1, 1), IsApproved = true }, User, null, null);
			await _costing.SaveResourceComponentsAsync(engine.ResourceCostProfileId, DeptId, new List<ResourceCostComponent> { new ResourceCostComponent { Category = (int)ResourceCostCategories.FuelEnergy, Basis = (int)ResourceCostBases.PerMile, Rate = 1m, IsApproved = true } }, User, null, null);

			_deployments.Add(new Deployment { DeploymentId = "dep", DepartmentId = DeptId, Name = "Fire", Currency = "USD", StartOn = new DateTime(2026, 8, 1) });
			_personnel.Add(new DeploymentPersonnel { DeploymentPersonnelId = "p1", DeploymentId = "dep", DepartmentId = DeptId, UserId = "u1", CallSign = "FF1" });
			_units.Add(new DeploymentUnit { DeploymentUnitId = "du1", DeploymentId = "dep", DepartmentId = DeptId, UnitId = 5 });
			_reports.Add(new DeploymentTimeReport { DeploymentTimeReportId = "dtr1", DeploymentId = "dep", DepartmentId = DeptId, ReportDate = new DateTime(2026, 8, 1), Status = (int)DeploymentTimeReportStatuses.Approved });
			_reports.Add(new DeploymentTimeReport { DeploymentTimeReportId = "dtr2", DeploymentId = "dep", DepartmentId = DeptId, ReportDate = new DateTime(2026, 8, 2), Status = (int)DeploymentTimeReportStatuses.Draft });
			_entries.Add(new DeploymentTimeEntry { DeploymentTimeEntryId = "e1", DeploymentTimeReportId = "dtr1", DeploymentId = "dep", DepartmentId = DeptId, SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, DeploymentPersonnelId = "p1", EntryType = (int)DeploymentTimeEntryTypes.Deployment, StartTime = new DateTime(2026, 8, 1, 6, 0, 0), EndTime = new DateTime(2026, 8, 1, 18, 0, 0) });
			_entries.Add(new DeploymentTimeEntry { DeploymentTimeEntryId = "e2", DeploymentTimeReportId = "dtr1", DeploymentId = "dep", DepartmentId = DeptId, SubjectType = (int)DeploymentTimeSubjectTypes.Unit, DeploymentUnitId = "du1", EntryType = (int)DeploymentTimeEntryTypes.Deployment, StartTime = new DateTime(2026, 8, 1, 6, 0, 0), EndTime = new DateTime(2026, 8, 1, 18, 0, 0) });
			_entries.Add(new DeploymentTimeEntry { DeploymentTimeEntryId = "e3", DeploymentTimeReportId = "dtr2", DeploymentId = "dep", DepartmentId = DeptId, SubjectType = (int)DeploymentTimeSubjectTypes.Personnel, DeploymentPersonnelId = "p1", EntryType = (int)DeploymentTimeEntryTypes.Deployment, StartTime = new DateTime(2026, 8, 2, 6, 0, 0), EndTime = new DateTime(2026, 8, 2, 18, 0, 0) });
			await _costing.SaveUsageEntryAsync(new ResourceUsageEntry { DepartmentId = DeptId, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = 5, DeploymentId = "dep", UsageDate = new DateTime(2026, 8, 1), Phase = (int)UsagePhases.Incident, StartOdometer = 1000, EndOdometer = 1100, DistanceUnit = "mi", Source = (int)UsageSources.Manual }, User, null, null);
			_expenses.Add(new DeploymentExpense { DeploymentExpenseId = "x1", DeploymentId = "dep", DepartmentId = DeptId, ExpenseDate = new DateTime(2026, 8, 1), ExpenseType = 0, Description = "Meals", Amount = 45m });
			_marsItems.Add(new CalOesMarsWorkItem { CalOesMarsWorkItemId = "w1", DepartmentId = DeptId, DeploymentId = "dep", RecordType = (int)CalOesMarsRecordTypes.F42, ExpectedTotal = 900m, ApprovedTotal = 850m, RowVersion = 2 });

			var run = await _costing.CalculateDeploymentCostAsync("dep", DeptId, new DateTime(2026, 8, 31), RevenueSources.CalOesMarsApproved, User, null, null);
			// 12 DTR hours: 8 regular × $30 + 4 overtime × $45 = $420 (the draft report on the 2nd is excluded).
			run.PersonnelTotal.Should().Be(420m);
			// 100 miles × $1.20 depreciation + 100 miles × $1 fuel.
			run.ResourceTotal.Should().Be(220m);
			run.ExpenseTotal.Should().Be(45m);
			run.TotalLoadedCost.Should().Be(685m);
			run.RevenueSource.Should().Be((int)RevenueSources.CalOesMarsApproved);
			run.RevenueAmount.Should().Be(850m);
			run.ContributionMargin.Should().Be(165m);
			run.BreakEvenRevenue.Should().Be(685m);
			run.Status.Should().Be((int)FieldCostRunStatuses.Draft);
			run.Lines.Where(l => l.Category == (int)FieldCostCategories.Personnel).Should().OnlyContain(l => l.Rate == null, "personnel lines carry no rate in the clear");
			run.Lines.Where(l => l.Category == (int)FieldCostCategories.Personnel).Should().OnlyContain(l => l.ProtectedDetailJson.Contains("\"BaseRate\":30"), "the priced detail rides the protected column");
			run.Lines.Should().Contain(l => l.Category == (int)FieldCostCategories.Resource && l.Component == "Depreciation" && l.Amount == 120m);

			var summary = await _costing.GetFieldCostSummaryAsync(run.FieldCostRunId, DeptId);
			summary.TotalLoadedCost.Should().Be(685m);
			summary.GetType().GetProperty("Lines").Should().BeNull("the mobile summary never carries lines");

			var frozen = await _costing.FreezeCostRunAsync(run.FieldCostRunId, DeptId, User, null, null);
			frozen.IsFrozen.Should().BeTrue();
			Func<Task> delete = () => _costing.DeleteRunAsync(run.FieldCostRunId, DeptId, User, null, null);
			await delete.Should().ThrowAsync<InvalidOperationException>().WithMessage("workforce_run_frozen");

			var again = await _costing.CalculateDeploymentCostAsync("dep", DeptId, new DateTime(2026, 8, 31), RevenueSources.CalOesMarsExpected, User, null, null);
			again.SupersedesRunId.Should().Be(run.FieldCostRunId);
			again.RevenueAmount.Should().Be(900m);
			(await _costing.GetRunAsync(run.FieldCostRunId, DeptId)).Status.Should().Be((int)FieldCostRunStatuses.Frozen, "a frozen run is only marked superseded once its successor freezes");
			await _costing.FreezeCostRunAsync(again.FieldCostRunId, DeptId, User, null, null);
			(await _costing.GetRunAsync(run.FieldCostRunId, DeptId)).Status.Should().Be((int)FieldCostRunStatuses.Superseded);
			_audits.Should().Contain(a => a.Type == AuditLogTypes.FieldCostRunFrozen);
		}

		[Test]
		public async Task Usage_readings_canonicalise_and_conflicting_automatic_and_manual_distances_queue_for_review()
		{
			_deployments.Add(new Deployment { DeploymentId = "dep", DepartmentId = DeptId, Name = "Fire" });
			var manual = await _costing.SaveUsageEntryAsync(new ResourceUsageEntry { DepartmentId = DeptId, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = 5, DeploymentId = "dep", UsageDate = new DateTime(2026, 8, 1), OriginalDistance = 100, DistanceUnit = "km", Source = (int)UsageSources.Manual }, User, null, null);
			manual.CanonicalDistanceMiles.Should().Be(62.14m);
			manual.NeedsReview.Should().BeFalse();
			var gps = await _costing.SaveUsageEntryAsync(new ResourceUsageEntry { DepartmentId = DeptId, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = 5, DeploymentId = "dep", UsageDate = new DateTime(2026, 8, 1), OriginalDistance = 90, DistanceUnit = "mi", Source = (int)UsageSources.Gps }, User, null, null);
			gps.NeedsReview.Should().BeTrue();
			gps.ReviewReason.Should().Be("distance_conflict");
			(await _costing.GetUsageForDeploymentAsync("dep", DeptId)).Should().OnlyContain(u => u.NeedsReview);
		}

		private async Task<PayDataReportRun> SeedReportAsync()
		{
			await _workforce.SaveEmployerProfileAsync(new WorkforceEmployerProfile { DepartmentId = DeptId, LegalName = "Test Fire District", Fein = "12-3456789", Sein = "123-4567-8", EddAddress = "1 Main St, Sacramento CA 95814", Naics = "922160", CoverageStatus = (int)CaliforniaPayDataCoverageStatuses.CoveredPayroll, UsEmployeeCount = 120, CaliforniaEmployeeCount = 120, FilingContactName = "Officer", FilingContactEmail = "officer@example.org" }, User, null, null);
			var (a, ea, _) = await SeedWorkerAsync("u1");
			var (b, eb, _) = await SeedWorkerAsync("u2");
			await _demographicsService.SaveOwnAsync(DeptId, "u1", new PayDataReportingDemographic { HispanicLatino = "No", RaceEthnicityCodes = "B", SexCode = "20" }, null, null);
			await _demographicsService.SaveOwnAsync(DeptId, "u2", new PayDataReportingDemographic { HispanicLatino = "No", RaceEthnicityCodes = "B", SexCode = "20" }, null, null);
			await _workforce.SaveAnnualPayFactAsync(new WorkforceAnnualPayFact { DepartmentId = DeptId, WorkforceEmploymentId = ea.WorkforceEmploymentId, ReportingYear = 2025, ReportType = (int)PayDataReportTypes.PayrollEmployee, W2Box5Value = 52000m, ActualWorkedHours = 2080m, WeeksWorked = 52, IsApproved = true }, User, null, null);
			await _workforce.SaveAnnualPayFactAsync(new WorkforceAnnualPayFact { DepartmentId = DeptId, WorkforceEmploymentId = eb.WorkforceEmploymentId, ReportingYear = 2025, ReportType = (int)PayDataReportTypes.PayrollEmployee, W2Box5Value = 49920m, ActualWorkedHours = 2080m, WeeksWorked = 52, IsApproved = true }, User, null, null);
			return await _reporting.CreateRunAsync(DeptId, 2025, PayDataReportTypes.PayrollEmployee, new DateTime(2025, 10, 1), new DateTime(2025, 10, 31), User, null, null);
		}

		[Test]
		public async Task Report_run_walks_create_snapshots_aggregate_validate_freeze_export_and_certify_and_a_correction_supersedes_it()
		{
			var run = await SeedReportAsync();
			run.Status.Should().Be((int)PayDataReportRunStatuses.Draft);
			run.SchemaProfileCode.Should().Be("CRD-RY2025");
			Func<Task> early = () => _reporting.AggregateRowsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			await early.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_no_snapshots");

			run = await _reporting.BuildEmployeeSnapshotsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			run.EmployeeCount.Should().Be(2); run.ExceptionCount.Should().Be(0);
			var snapshots = await _reporting.GetSnapshotsAsync(run.PayDataReportRunId, DeptId);
			snapshots.Should().OnlyContain(s => s.DemographicCode == "B20" && s.PayBandCode == "6" && s.JobCategoryCode == "10" && s.IsIncluded);
			snapshots.Select(s => s.HourlyRateValue).Should().BeEquivalentTo(new decimal?[] { 25m, 24m });
			snapshots.Should().OnlyContain(s => s.WorkerDisplayName.StartsWith("Member"));

			run = await _reporting.AggregateRowsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			run.RowCount.Should().Be(1);
			var rows = await _reporting.GetRowsAsync(run.PayDataReportRunId, DeptId);
			rows.Single().MeanHourlyRateValue.Should().Be(24.5m); rows.Single().MedianHourlyRateValue.Should().Be(24.5m); rows.Single().EmployeeCount.Should().Be(2);

			var validation = await _reporting.ValidateRunAsync(run.PayDataReportRunId, DeptId, User, null, null);
			validation.Errors.Should().BeEmpty(); validation.CanFreeze.Should().BeTrue();
			(await _reporting.GetRunAsync(run.PayDataReportRunId, DeptId)).Status.Should().Be((int)PayDataReportRunStatuses.Validated);

			run = await _reporting.FreezeAndExportAsync(run.PayDataReportRunId, DeptId, User, null, null);
			run.Status.Should().Be((int)PayDataReportRunStatuses.Exported);
			run.IsFrozen.Should().BeTrue();
			var artifacts = await _reporting.GetArtifactsAsync(run.PayDataReportRunId, DeptId);
			artifacts.Select(a => a.Format).Should().BeEquivalentTo(new[] { (int)PayDataExportFormats.Csv, (int)PayDataExportFormats.Xlsx });
			artifacts.Should().OnlyContain(a => a.Data == null, "listings never carry the bytes");
			var csv = await _reporting.DownloadArtifactAsync(artifacts.Single(a => a.Format == (int)PayDataExportFormats.Csv).PayDataExportArtifactId, DeptId, User, null, null);
			var text = System.Text.Encoding.UTF8.GetString(csv.Data);
			text.Should().StartWith("Establishment Name,Establishment Address");
			text.Should().Contain("Station 1,1 Main St,Sacramento,CA,95814,922160,Fire protection,2,No,Yes,10,B20,6,2,4160,24.50,24.50,2,0,0,");
			csv.Checksum.Should().Be(run.CertifiedArtifactChecksum);
			_audits.Should().Contain(a => a.Type == AuditLogTypes.PayDataReportExported && a.After.Contains("download"));

			var worksheet = await _reporting.GetWorksheetAsync(run.PayDataReportRunId, DeptId);
			worksheet.EmployerLegalName.Should().Be("Test Fire District"); worksheet.SnapshotEmployeeCount.Should().Be(2); worksheet.Establishments.Single().EmployeeCount.Should().Be(2); worksheet.DueDate.Should().Be(new DateTime(2026, 5, 13));

			Func<Task> frozenEdit = () => _reporting.BuildEmployeeSnapshotsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			await frozenEdit.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_run_frozen");

			run = await _reporting.MarkCertifiedExternallyAsync(run.PayDataReportRunId, DeptId, "CRD-2026-000123", User, null, null);
			run.Status.Should().Be((int)PayDataReportRunStatuses.CertifiedExternally);
			Func<Task> voidCertified = () => _reporting.VoidRunAsync(run.PayDataReportRunId, DeptId, User, null, null);
			await voidCertified.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_run_certified");

			var correction = await _reporting.CreateCorrectionAsync(run.PayDataReportRunId, DeptId, User, null, null);
			correction.SupersedesRunId.Should().Be(run.PayDataReportRunId);
			correction.Status.Should().Be((int)PayDataReportRunStatuses.Draft);
			(await _reporting.GetRunAsync(run.PayDataReportRunId, DeptId)).Status.Should().Be((int)PayDataReportRunStatuses.Correction);
			Func<Task> second = () => _reporting.CreateCorrectionAsync(run.PayDataReportRunId, DeptId, User, null, null);
			await second.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_correction_exists");

			var readiness = await _reporting.GetReadinessAsync(DeptId, 2025);
			readiness.HasCertifiedRun.Should().BeTrue(); readiness.OpenRuns.Should().Be(1); readiness.DemographicsMissing.Should().Be(0); readiness.AnnualFactsMissing.Should().Be(0);
		}

		[Test]
		public async Task Validation_blocks_missing_demographics_and_facts_and_overrides_need_a_reason()
		{
			var run = await SeedReportAsync();
			var (c, ec, _) = await SeedWorkerAsync("u3");
			run = await _reporting.BuildEmployeeSnapshotsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			run.EmployeeCount.Should().Be(3); run.ExceptionCount.Should().Be(1, "u3 has neither a demographic response nor an annual fact");
			var snapshot = (await _reporting.GetSnapshotsAsync(run.PayDataReportRunId, DeptId)).Single(s => s.WorkforceWorkerId == c.WorkforceWorkerId);
			snapshot.ExceptionCodes.Should().Contain(PayDataValidationCodes.DemographicMissing).And.Contain(PayDataValidationCodes.AnnualFactMissing);
			await _reporting.AggregateRowsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			var validation = await _reporting.ValidateRunAsync(run.PayDataReportRunId, DeptId, User, null, null);
			validation.CanFreeze.Should().BeFalse();
			validation.Errors.Should().Contain(e => e.Code == PayDataValidationCodes.DemographicMissing && e.SubjectId == snapshot.PayDataReportEmployeeSnapshotId);
			Func<Task> freeze = () => _reporting.FreezeAndExportAsync(run.PayDataReportRunId, DeptId, User, null, null);
			await freeze.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_validation_failed");

			Func<Task> noReason = () => _reporting.OverrideSnapshotAsync(run.PayDataReportRunId, snapshot.PayDataReportEmployeeSnapshotId, DeptId, false, null, null, " ", User, null, null);
			await noReason.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_reason_required");
			var excluded = await _reporting.OverrideSnapshotAsync(run.PayDataReportRunId, snapshot.PayDataReportEmployeeSnapshotId, DeptId, false, null, null, "Left before the snapshot period", User, null, null);
			excluded.IsIncluded.Should().BeFalse();
			excluded.ExceptionCodes.Should().Contain(PayDataValidationCodes.ManualOverride);
			await _reporting.AggregateRowsAsync(run.PayDataReportRunId, DeptId, User, null, null);
			(await _reporting.ValidateRunAsync(run.PayDataReportRunId, DeptId, User, null, null)).CanFreeze.Should().BeTrue("excluded snapshots no longer block");
		}

		[Test]
		public async Task Demographics_live_in_their_own_table_count_only_for_completeness_and_never_reach_the_costing_engine()
		{
			var (worker, _, _) = await SeedWorkerAsync("u1");
			(await _demographicsService.GetOwnAsync(DeptId, "u1")).Should().BeNull();
			var first = await _demographicsService.SaveOwnAsync(DeptId, "u1", new PayDataReportingDemographic { HispanicLatino = "Yes", RaceEthnicityCodes = "B,E", SexCode = "10" }, null, null);
			first.CollectionSource.Should().Be((int)DemographicCollectionSources.SelfIdentified); first.Version.Should().Be(1);
			var second = await _demographicsService.SaveOwnAsync(DeptId, "u1", new PayDataReportingDemographic { DeclinedRaceEthnicity = true, SexCode = "30" }, null, null);
			second.Version.Should().Be(2); second.RaceEthnicityCodes.Should().BeNull(); second.HispanicLatino.Should().BeNull();
			(await _demographicsService.GetOwnAsync(DeptId, "u1")).PayDataReportingDemographicId.Should().Be(second.PayDataReportingDemographicId);
			Func<Task> badRace = () => _demographicsService.SaveOwnAsync(DeptId, "u1", new PayDataReportingDemographic { RaceEthnicityCodes = "Z", SexCode = "10" }, null, null);
			await badRace.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_race_invalid");
			Func<Task> officerNeedsReason = () => _demographicsService.SaveForWorkerAsync(DeptId, worker.WorkforceWorkerId, new PayDataReportingDemographic { CollectionSource = (int)DemographicCollectionSources.ObserverPerception, RaceEthnicityCodes = "C", SexCode = "20", HispanicLatino = "No" }, "", User, null, null);
			await officerNeedsReason.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_reason_required");
			Func<Task> officerNotSelf = () => _demographicsService.SaveForWorkerAsync(DeptId, worker.WorkforceWorkerId, new PayDataReportingDemographic { CollectionSource = (int)DemographicCollectionSources.SelfIdentified, RaceEthnicityCodes = "C", SexCode = "20", HispanicLatino = "No" }, "reason", User, null, null);
			await officerNotSelf.Should().ThrowAsync<InvalidOperationException>().WithMessage("paydata_collection_source_invalid");

			await SeedWorkerAsync("u2");
			var completeness = await _demographicsService.GetCompletenessAsync(DeptId, DateTime.UtcNow);
			completeness.ActiveWorkers.Should().Be(2); completeness.WithResponse.Should().Be(1); completeness.Declined.Should().Be(1); completeness.Missing.Should().Be(1);
			_audits.Where(a => a.Type == AuditLogTypes.PayDataDemographicChanged).Should().OnlyContain(a => !a.After.Contains("\"B,E\"") && !a.After.Contains("\"10\""), "audit snapshots carry markers, never the coded values");

			var costingDependencies = typeof(FieldCostingService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();
			costingDependencies.Should().NotContain(typeof(IPayDataReportingDemographicRepository)).And.NotContain(typeof(IPayDataDemographicsService), "the costing engine has no path to the demographic table");
			typeof(CompensationCostService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).Should().NotContain(typeof(IPayDataReportingDemographicRepository));
		}

		[Test]
		public async Task Annual_fact_import_dry_runs_before_committing_and_versions_corrections()
		{
			var (worker, employment, _) = await SeedWorkerAsync("u1");
			var csv = "UserId,ReportingYear,ReportType,W2Box5,W2Box1,ActualWorkedHours,PaidLeaveHours,WeeksWorked\nu1,2025,PayrollEmployee,52000,50000,2000,80,52\nghost,2025,PayrollEmployee,1,1,1,1,1";
			var dry = await _workforce.ImportAnnualPayFactsAsync(DeptId, csv, true, User, null, null);
			dry.DryRun.Should().BeTrue(); dry.Total.Should().Be(2); dry.HasErrors.Should().BeTrue(); dry.Issues.Should().Contain(i => i.Code == "worker_not_found" && i.Line == 3);
			_facts.Should().BeEmpty("nothing commits while a row is in error");
			var commit = await _workforce.ImportAnnualPayFactsAsync(DeptId, csv, false, User, null, null);
			commit.HasErrors.Should().BeTrue(); _facts.Should().BeEmpty();

			var clean = await _workforce.ImportAnnualPayFactsAsync(DeptId, csv.Split('\n')[0] + "\n" + csv.Split('\n')[1], false, User, null, null);
			clean.Created.Should().Be(1); clean.HasErrors.Should().BeFalse();
			var facts = await _workforce.GetAnnualPayFactsAsync(DeptId, 2025, PayDataReportTypes.PayrollEmployee);
			facts.Single().EarningsUsedValue.Should().Be(52000m); facts.Single().ReportableHours.Should().Be(2080m); facts.Single().IsApproved.Should().BeFalse("imports arrive unapproved");

			var corrected = await _workforce.SaveAnnualPayFactAsync(new WorkforceAnnualPayFact { DepartmentId = DeptId, WorkforceEmploymentId = employment.WorkforceEmploymentId, ReportingYear = 2025, ReportType = (int)PayDataReportTypes.PayrollEmployee, W2Box5Value = 53000m, ActualWorkedHours = 2080m, WeeksWorked = 52, IsApproved = true }, User, null, null);
			corrected.Version.Should().Be(2); corrected.SupersedesFactId.Should().Be(facts.Single().WorkforceAnnualPayFactId);
			(await _workforce.GetAnnualPayFactsAsync(DeptId, 2025, PayDataReportTypes.PayrollEmployee)).Single().EarningsUsedValue.Should().Be(53000m, "only the current version is listed");
			_facts.Should().HaveCount(2, "the superseded fact is kept");
		}

		[Test]
		public async Task Readiness_sweep_sends_one_value_free_digest_per_department_in_season_and_purges_expired_artifacts()
		{
			await SeedReportAsync();
			var sent = await _reporting.RunReadinessSweepAsync(new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc), d => Task.FromResult(true));
			sent.Should().Be(1);
			_notifications.Single().Should().StartWith("California pay data reporting: Reporting year 2025 is due 2026-05-13").And.Contain("1 open run(s)").And.NotContain("52000").And.NotContain("Test Fire District");
			(await _reporting.RunReadinessSweepAsync(new DateTime(2026, 3, 2, 18, 0, 0, DateTimeKind.Utc), d => Task.FromResult(true))).Should().Be(0, "once per department per day");
			(await _reporting.RunReadinessSweepAsync(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc), d => Task.FromResult(true))).Should().Be(0, "outside the filing season");

			_artifacts.Add(new PayDataExportArtifact { PayDataExportArtifactId = "old", PayDataReportRunId = "r", DepartmentId = DeptId, Data = new byte[] { 1 }, ExpiresOn = new DateTime(2026, 1, 1), CreatedOn = new DateTime(2025, 12, 1) });
			(await _reporting.PurgeExpiredArtifactsAsync(new DateTime(2026, 2, 1))).Should().Be(1);
			_artifacts.Single().Data.Should().BeNull(); _artifacts.Single().PurgedOn.Should().NotBeNull();
		}
	}
}
