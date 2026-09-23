using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Services.Invoicing;

namespace Resgrid.Services.Workforce
{
	/// <summary>
	/// The California CRD pay data report wizard (plan E3/E5): create → build employee snapshots → aggregate → validate
	/// → freeze and export → attest externally. Everything a person could be identified or priced by (demographic
	/// code, earnings, hourly rates, employer identity, remarks, export bytes) rides ADP catalog 28; the engine decrypts
	/// through the <c>pay-data-reporting</c> workload purpose, users through their grant. A frozen run is immutable —
	/// a correction supersedes it. Resgrid never files: the artifacts and worksheet are what the officer carries to the
	/// CRD portal, and "certified" is only ever an observation the officer records.
	/// </summary>
	public class CaPayDataReportingService : ICaPayDataReportingService
	{
		private static readonly HashSet<int> RemindedToday = new HashSet<int>();

		private readonly IPayDataReportRunRepository _runs;
		private readonly IPayDataReportEmployeeSnapshotRepository _snapshots;
		private readonly IPayDataReportRowRepository _rows;
		private readonly IPayDataExportArtifactRepository _artifacts;
		private readonly IWorkforceEmployerProfileRepository _employers;
		private readonly IWorkforceAffiliatedEntityRepository _affiliates;
		private readonly IWorkforceEstablishmentRepository _establishments;
		private readonly IWorkforceLaborContractorRepository _contractors;
		private readonly IWorkforceWorkerRepository _workers;
		private readonly IWorkforceEmploymentRepository _employments;
		private readonly IWorkforceJobAssignmentRepository _assignments;
		private readonly IWorkforceWorkEntryRepository _workEntries;
		private readonly IWorkforceAnnualPayFactRepository _annualFacts;
		private readonly IPayDataReportingDemographicRepository _demographics;
		private readonly IUserProfileService _userProfileService;
		private readonly IDepartmentsService _departmentsService;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IDepartmentSettingsService> _departmentSettings;
		private readonly IEventAggregator _eventAggregator;
		private readonly WorkforceProtectionSeam _seam;

		public CaPayDataReportingService(IPayDataReportRunRepository runs, IPayDataReportEmployeeSnapshotRepository snapshots, IPayDataReportRowRepository rows, IPayDataExportArtifactRepository artifacts,
			IWorkforceEmployerProfileRepository employers, IWorkforceAffiliatedEntityRepository affiliates, IWorkforceEstablishmentRepository establishments, IWorkforceLaborContractorRepository contractors,
			IWorkforceWorkerRepository workers, IWorkforceEmploymentRepository employments, IWorkforceJobAssignmentRepository assignments, IWorkforceWorkEntryRepository workEntries, IWorkforceAnnualPayFactRepository annualFacts,
			IPayDataReportingDemographicRepository demographics, IUserProfileService userProfileService, IDepartmentsService departmentsService, Lazy<ICommunicationService> communication, Lazy<IDepartmentSettingsService> departmentSettings,
			IEventAggregator eventAggregator, Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_runs = runs;
			_snapshots = snapshots;
			_rows = rows;
			_artifacts = artifacts;
			_employers = employers;
			_affiliates = affiliates;
			_establishments = establishments;
			_contractors = contractors;
			_workers = workers;
			_employments = employments;
			_assignments = assignments;
			_workEntries = workEntries;
			_annualFacts = annualFacts;
			_demographics = demographics;
			_userProfileService = userProfileService;
			_departmentsService = departmentsService;
			_communication = communication;
			_departmentSettings = departmentSettings;
			_eventAggregator = eventAggregator;
			_seam = new WorkforceProtectionSeam(protectedWrite, protectedRead, grant);
		}

		#region Reads

		public async Task<List<PayDataReportRun>> GetRunsAsync(int departmentId, int? reportingYear = null) =>
			(await _runs.GetForDepartmentAsync(departmentId, reportingYear))?.Where(r => !r.IsDeleted).OrderByDescending(r => r.ReportingYear).ThenByDescending(r => r.AddedOn).ToList() ?? new List<PayDataReportRun>();

		public async Task<PayDataReportRun> GetRunAsync(string runId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(runId)) return null;
			var run = await _runs.GetByIdForDepartmentAsync(runId, departmentId);
			if (run == null || run.IsDeleted) return null;
			await _seam.ResolveForReadAsync(new[] { run }, departmentId, WorkforceProtectedFields.ReportRun);
			return run;
		}

		public async Task<List<PayDataReportEmployeeSnapshot>> GetSnapshotsAsync(string runId, int departmentId)
		{
			var run = await RequireRunAsync(runId, departmentId);
			var rows = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.OrderBy(s => s.WorkforceEstablishmentId).ThenBy(s => s.JobCategoryCode).ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.EmployeeSnapshot);
			await NameSnapshotsAsync(rows, departmentId);
			return rows;
		}

		public async Task<List<PayDataReportRow>> GetRowsAsync(string runId, int departmentId)
		{
			var run = await RequireRunAsync(runId, departmentId);
			var rows = (await _rows.GetByRunAsync(run.PayDataReportRunId))?.OrderBy(r => r.SortOrder).ToList() ?? new List<PayDataReportRow>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.ReportRow);
			return rows;
		}

		private async Task NameSnapshotsAsync(IReadOnlyList<PayDataReportEmployeeSnapshot> rows, int departmentId)
		{
			if (rows.Count == 0) return;
			var workers = (await _workers.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceWorker>();
			var userIds = workers.Where(w => !string.IsNullOrWhiteSpace(w.UserId)).Select(w => w.UserId).Distinct().ToList();
			var profiles = userIds.Count == 0 ? new List<UserProfile>() : (await _userProfileService.GetSelectedUserProfilesAsync(userIds))?.ToList() ?? new List<UserProfile>();
			foreach (var row in rows)
			{
				var worker = workers.FirstOrDefault(w => w.WorkforceWorkerId == row.WorkforceWorkerId);
				var profile = worker?.UserId == null ? null : profiles.FirstOrDefault(p => string.Equals(p.UserId, worker.UserId, StringComparison.OrdinalIgnoreCase));
				row.WorkerDisplayName = profile?.FullName.AsFirstNameLastName ?? (worker != null && !WorkforceProtectionSeam.IsUnavailable(worker.DisplayLabel) ? worker.DisplayLabel : null) ?? worker?.UserId ?? row.WorkforceWorkerId;
			}
		}

		#endregion

		#region Lifecycle

		public async Task<PayDataReportRun> CreateRunAsync(int departmentId, int reportingYear, PayDataReportTypes reportType, DateTime snapshotStart, DateTime snapshotEnd, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var profile = CaPayDataSchemaProfile.ForYear(reportingYear) ?? throw new InvalidOperationException("paydata_profile_unavailable");
			if (!profile.IsSnapshotInWindow(snapshotStart, snapshotEnd)) throw new InvalidOperationException("paydata_snapshot_window_invalid");
			var employer = await _employers.GetActiveForDepartmentAsync(departmentId);
			var run = new PayDataReportRun
			{
				DepartmentId = departmentId, ReportType = (int)reportType, ReportingYear = reportingYear, SchemaProfileCode = profile.Code, SchemaProfileHash = ProfileHash(profile),
				SnapshotStart = snapshotStart.Date, SnapshotEnd = snapshotEnd.Date, Status = (int)PayDataReportRunStatuses.Draft, AddedOn = DateTime.UtcNow, AddedByUserId = userId,
				EmployerSnapshotJson = await EmployerSnapshotAsync(employer, departmentId)
			};
			var saved = await _seam.SaveAsync(_runs, run, null, departmentId, WorkforceProtectedFields.ReportRun, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCreated, ipAddress, userAgent, null, saved);
			return await GetRunAsync(saved.PayDataReportRunId, departmentId);
		}

		private async Task<string> EmployerSnapshotAsync(WorkforceEmployerProfile employer, int departmentId)
		{
			if (employer == null) return null;
			await _seam.ResolveForWorkloadAsync(new[] { employer }, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Employer);
			var affiliates = (await _affiliates.GetForDepartmentAsync(departmentId))?.Where(a => !a.IsDeleted).ToList() ?? new List<WorkforceAffiliatedEntity>();
			await _seam.ResolveForWorkloadAsync(affiliates, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Affiliate);
			return JsonConvert.SerializeObject(new
			{
				employer.WorkforceEmployerProfileId, employer.RowVersion, employer.LegalName, employer.Fein, employer.Sein, employer.SosNumber, employer.Naics, employer.EddAddress, employer.HeadquartersAddress, employer.IsIntegratedEnterprise,
				employer.FilingContactName, employer.FilingContactEmail, employer.FilingContactPhone, employer.CoverageStatus, employer.UsEmployeeCount, employer.CaliforniaEmployeeCount,
				Affiliates = affiliates.Select(a => new { a.WorkforceAffiliatedEntityId, a.LegalName, a.Fein, a.Sein, a.SosNumber, a.HeadquartersAddress }).ToList()
			});
		}

		public async Task<PayDataReportRun> BuildEmployeeSnapshotsAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? throw new InvalidOperationException("paydata_profile_unavailable");
			var before = Snapshot(run);
			await _rows.DeleteByRunAsync(run.PayDataReportRunId, cancellationToken);
			await _snapshots.DeleteByRunAsync(run.PayDataReportRunId, cancellationToken);

			var yearStart = new DateTime(run.ReportingYear, 1, 1); var yearEnd = new DateTime(run.ReportingYear, 12, 31);
			var kind = run.ReportType == (int)PayDataReportTypes.LaborContractorEmployee ? WorkerKinds.LaborContractorEmployee : WorkerKinds.PayrollEmployee;
			var employments = (await _employments.GetActiveInWindowAsync(departmentId, run.SnapshotStart, run.SnapshotEnd))?.Where(e => !e.IsDeleted && e.WorkerKind == (int)kind).ToList() ?? new List<WorkforceEmployment>();
			var establishments = (await _establishments.GetForDepartmentAsync(departmentId))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEstablishment>();
			var assignments = employments.Count == 0 ? new List<WorkforceJobAssignment>() : (await _assignments.GetByEmploymentsAsync(employments.Select(e => e.WorkforceEmploymentId)))?.Where(a => !a.IsDeleted).ToList() ?? new List<WorkforceJobAssignment>();
			var facts = (await _annualFacts.GetForYearAsync(departmentId, run.ReportingYear, run.ReportType))?.Where(f => !f.IsDeleted).GroupBy(f => (f.WorkforceEmploymentId, f.ClientAllocationKey ?? string.Empty)).Select(g => g.OrderByDescending(f => f.Version).First()).ToList() ?? new List<WorkforceAnnualPayFact>();
			var factsResolved = await _seam.ResolveForWorkloadAsync(facts, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.AnnualFact);
			// Demographic responses are collected after the snapshot period: the current response counts, not the one that existed on the snapshot date.
			var demographics = (await _demographics.GetCurrentForDepartmentAsync(departmentId, DateTime.UtcNow.Date))?.Where(d => !d.IsDeleted).GroupBy(d => d.WorkforceWorkerId).Select(g => g.OrderByDescending(d => d.Version).First()).ToList() ?? new List<PayDataReportingDemographic>();
			var demographicsResolved = await _seam.ResolveForWorkloadAsync(demographics, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Demographic);
			var workEntries = (await _workEntries.GetForDepartmentInWindowAsync(departmentId, yearStart, yearEnd))?.Where(w => !w.IsDeleted).ToList() ?? new List<WorkforceWorkEntry>();

			var snapshots = new List<PayDataReportEmployeeSnapshot>();
			foreach (var employment in employments)
			{
				// Snapshot-period assignment governs job category and work mode; the establishment is the one with the most hours in the year when work entries exist.
				var assignment = assignments.Where(a => a.WorkforceEmploymentId == employment.WorkforceEmploymentId && a.EffectiveOn.Date <= run.SnapshotEnd && (!a.ExpiresOn.HasValue || a.ExpiresOn.Value.Date >= run.SnapshotStart)).OrderByDescending(a => a.EffectiveOn).FirstOrDefault();
				var hoursByEstablishment = workEntries.Where(w => w.WorkforceEmploymentId == employment.WorkforceEmploymentId && !string.IsNullOrWhiteSpace(w.WorkforceEstablishmentId)).GroupBy(w => w.WorkforceEstablishmentId).OrderByDescending(g => g.Sum(w => w.Hours)).FirstOrDefault();
				var establishmentId = hoursByEstablishment?.Key ?? assignment?.WorkforceEstablishmentId ?? employment.DefaultEstablishmentId;
				var establishment = establishments.FirstOrDefault(e => e.WorkforceEstablishmentId == establishmentId);
				var inCalifornia = employment.CaliforniaEmployeeBasis != (int)CaliforniaEmployeeBases.NotCalifornia || establishment?.IsCalifornia == true || assignment?.WorkMode == (int)WorkModes.RemoteWithinCalifornia
					|| string.Equals(assignment?.WorkSubdivision, "CA", StringComparison.OrdinalIgnoreCase) || workEntries.Any(w => w.WorkforceEmploymentId == employment.WorkforceEmploymentId && string.Equals(w.WorkSubdivision, "CA", StringComparison.OrdinalIgnoreCase));
				if (!inCalifornia) continue;

				var exceptions = new List<string>();
				var demographic = demographics.FirstOrDefault(d => d.WorkforceWorkerId == employment.WorkforceWorkerId);
				string demographicCode = null;
				if (demographic == null || !demographicsResolved || WorkforceProtectionSeam.IsUnavailable(demographic.SexCode) || WorkforceProtectionSeam.IsUnavailable(demographic.RaceEthnicityCodes)) exceptions.Add(PayDataValidationCodes.DemographicMissing);
				else
				{
					demographicCode = profile.DemographicCode(demographic.HispanicLatino, (demographic.RaceEthnicityCodes ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList(), demographic.SexCode);
					if (demographicCode == null) exceptions.Add(PayDataValidationCodes.DemographicMissing);
					if (demographic.CollectionSource == (int)DemographicCollectionSources.ObserverPerception) exceptions.Add(PayDataValidationCodes.ObserverPerceptionUsed);
				}
				var fact = facts.FirstOrDefault(f => f.WorkforceEmploymentId == employment.WorkforceEmploymentId);
				decimal? earnings = null; decimal hours = 0m; decimal weeks = 0m; var earningsSource = 0;
				if (fact == null || !factsResolved || WorkforceProtectionSeam.IsUnavailable(fact.EarningsUsed)) exceptions.Add(PayDataValidationCodes.AnnualFactMissing);
				else
				{
					if (!fact.IsApproved) exceptions.Add(PayDataValidationCodes.AnnualFactUnapproved);
					earnings = fact.EarningsUsedValue; earningsSource = fact.EarningsSource; hours = fact.ReportableHours ?? 0m; weeks = fact.WeeksWorked ?? 0m;
					if (!earnings.HasValue) exceptions.Add(PayDataValidationCodes.EarningsMissing);
					if (fact.EarningsSource == (int)EarningsSources.W2Box1Fallback) exceptions.Add(PayDataValidationCodes.EarningsBox1Fallback);
					if (hours <= 0) exceptions.Add(PayDataValidationCodes.HoursZero);
					if (weeks <= 0) exceptions.Add(PayDataValidationCodes.WeeksMissing);
					if (fact.ExemptProxyMethod == (int)ExemptProxyMethods.DaysTimesAverageHours) exceptions.Add(PayDataValidationCodes.ExemptProxyUsed);
				}
				if (establishment == null) exceptions.Add(PayDataValidationCodes.EstablishmentMissing);
				if (assignment == null || string.IsNullOrWhiteSpace(assignment.JobCategoryCode)) exceptions.Add(PayDataValidationCodes.JobCategoryMissing);
				else if (profile.JobCategories.All(j => j.Code != assignment.JobCategoryCode)) exceptions.Add(PayDataValidationCodes.JobCategoryUnknown);
				if (assignment == null) exceptions.Add(PayDataValidationCodes.WorkModeUnresolved);
				if (run.ReportType == (int)PayDataReportTypes.LaborContractorEmployee && string.IsNullOrWhiteSpace(employment.WorkforceLaborContractorId)) exceptions.Add(PayDataValidationCodes.ContractorIdentityMissing);
				var overlapping = assignments.Count(a => a.WorkforceEmploymentId == employment.WorkforceEmploymentId && a.EffectiveOn.Date <= run.SnapshotEnd && (!a.ExpiresOn.HasValue || a.ExpiresOn.Value.Date >= run.SnapshotStart));
				if (overlapping > 1 && assignments.Where(a => a.WorkforceEmploymentId == employment.WorkforceEmploymentId).Any(a => assignments.Any(o => o != a && o.WorkforceEmploymentId == a.WorkforceEmploymentId && a.Overlaps(o)))) exceptions.Add(PayDataValidationCodes.AssignmentOverlap);

				var rate = PayDataAggregator.HourlyRate(earnings, hours);
				snapshots.Add(new PayDataReportEmployeeSnapshot
				{
					PayDataReportRunId = run.PayDataReportRunId, DepartmentId = departmentId, WorkforceWorkerId = employment.WorkforceWorkerId, WorkforceEmploymentId = employment.WorkforceEmploymentId,
					WorkforceEstablishmentId = establishment?.WorkforceEstablishmentId, WorkforceLaborContractorId = employment.WorkforceLaborContractorId, JobCategoryCode = assignment?.JobCategoryCode,
					DemographicCode = demographicCode, PayBandCode = earnings.HasValue ? profile.PayBandFor(earnings.Value)?.Code : null,
					ExemptionCode = employment.ExemptionStatus == (int)ExemptionStatuses.Exempt ? "Exempt" : employment.ExemptionStatus == (int)ExemptionStatuses.NonExempt ? "NonExempt" : null,
					EmploymentTypeCode = employment.EmploymentType == (int)EmploymentTypes.Unknown ? null : ((EmploymentTypes)employment.EmploymentType).ToString(),
					WorkMode = assignment?.WorkMode ?? (int)WorkModes.NonRemote, AnnualEarningsValue = earnings, EarningsSource = earningsSource, AnnualHours = hours, AnnualWeeks = weeks,
					HourlyRateValue = rate.HasValue ? Math.Round(rate.Value, 4, MidpointRounding.AwayFromZero) : null, IsIncluded = true, ExceptionCodesCsv = exceptions.Count == 0 ? null : string.Join(",", exceptions.Distinct()),
					SourceVersions = JsonConvert.SerializeObject(new { Employment = new { employment.WorkforceEmploymentId, employment.RowVersion }, Assignment = assignment == null ? null : new { assignment.WorkforceJobAssignmentId, assignment.RowVersion }, Fact = fact == null ? null : new { fact.WorkforceAnnualPayFactId, fact.Version }, Demographic = demographic == null ? null : new { demographic.PayDataReportingDemographicId, demographic.Version } }),
					AddedOn = DateTime.UtcNow, AddedByUserId = userId
				});
			}
			foreach (var snapshot in snapshots) await _seam.SaveAsync(_snapshots, snapshot, null, departmentId, WorkforceProtectedFields.EmployeeSnapshot, cancellationToken);
			run.EmployeeCount = snapshots.Count; run.RowCount = 0; run.SourceCutoff = DateTime.UtcNow; run.Status = (int)PayDataReportRunStatuses.Draft; run.ValidationSummaryJson = null;
			run.ExceptionCount = snapshots.Count(s => s.ExceptionCodes.Any(IsBlocking)); run.WarningCount = snapshots.Count(s => s.ExceptionCodes.Any(c => !IsBlocking(c)));
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCreated, ipAddress, userAgent, before, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		public static bool IsBlocking(string code) => code is PayDataValidationCodes.AnnualFactMissing or PayDataValidationCodes.AnnualFactUnapproved or PayDataValidationCodes.EarningsMissing or PayDataValidationCodes.HoursZero
			or PayDataValidationCodes.WeeksMissing or PayDataValidationCodes.DemographicMissing or PayDataValidationCodes.JobCategoryMissing or PayDataValidationCodes.JobCategoryUnknown or PayDataValidationCodes.WorkModeUnresolved
			or PayDataValidationCodes.EstablishmentMissing or PayDataValidationCodes.ContractorIdentityMissing or PayDataValidationCodes.AssignmentOverlap;

		public async Task<PayDataReportEmployeeSnapshot> OverrideSnapshotAsync(string runId, string snapshotId, int departmentId, bool include, string jobCategoryCode, int? workMode, string reason, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			if (string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("paydata_reason_required");
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? throw new InvalidOperationException("paydata_profile_unavailable");
			var snapshot = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.FirstOrDefault(s => s.PayDataReportEmployeeSnapshotId == snapshotId) ?? throw new InvalidOperationException("paydata_snapshot_not_found");
			var before = snapshot.CloneJson(); foreach (var a in WorkforceProtectedFields.EmployeeSnapshot) a.Value.Set(before, WorkforceService.Marker(a.Value.Get(before)));
			var codes = snapshot.ExceptionCodes.ToList();
			if (!string.IsNullOrWhiteSpace(jobCategoryCode))
			{
				if (profile.JobCategories.All(j => j.Code != jobCategoryCode.Trim())) throw new InvalidOperationException("workforce_job_category_invalid");
				snapshot.JobCategoryCode = jobCategoryCode.Trim();
				codes.Remove(PayDataValidationCodes.JobCategoryMissing); codes.Remove(PayDataValidationCodes.JobCategoryUnknown);
			}
			if (workMode.HasValue)
			{
				if (!Enum.IsDefined(typeof(WorkModes), workMode.Value)) throw new InvalidOperationException("workforce_work_mode_invalid");
				snapshot.WorkMode = workMode.Value;
				codes.Remove(PayDataValidationCodes.WorkModeUnresolved);
			}
			snapshot.IsIncluded = include;
			if (!codes.Contains(PayDataValidationCodes.ManualOverride)) codes.Add(PayDataValidationCodes.ManualOverride);
			snapshot.ExceptionCodesCsv = string.Join(",", codes);
			snapshot.OverrideReason = reason.Trim(); snapshot.OverrideByUserId = userId;
			// The envelope is bound to the row; the protected columns are unchanged, so a plain update keeps them.
			await _snapshots.SaveOrUpdateAsync(snapshot, cancellationToken);
			await _rows.DeleteByRunAsync(run.PayDataReportRunId, cancellationToken);
			var all = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			run.RowCount = 0; run.Status = (int)PayDataReportRunStatuses.Draft; run.ValidationSummaryJson = null;
			run.ExceptionCount = all.Count(s => s.IsIncluded && s.ExceptionCodes.Any(IsBlocking)); run.WarningCount = all.Count(s => s.IsIncluded && s.ExceptionCodes.Any(c => !IsBlocking(c)));
			await TouchAsync(run, userId, cancellationToken);
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.PayDataReportCreated, ipAddress, userAgent);
			var after = snapshot.CloneJson(); foreach (var a in WorkforceProtectedFields.EmployeeSnapshot) a.Value.Set(after, WorkforceService.Marker(a.Value.Get(after)));
			audit.Before = before.CloneJsonToString(); audit.After = after.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(audit);
			await _seam.ResolveForReadAsync(new[] { snapshot }, departmentId, WorkforceProtectedFields.EmployeeSnapshot);
			return snapshot;
		}

		public async Task<PayDataReportRun> AggregateRowsAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? throw new InvalidOperationException("paydata_profile_unavailable");
			var before = Snapshot(run);
			var snapshots = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			if (snapshots.Count == 0) throw new InvalidOperationException("paydata_no_snapshots");
			if (!await _seam.ResolveForWorkloadAsync(snapshots, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.EmployeeSnapshot)) throw new InvalidOperationException("paydata_workload_denied");
			await _rows.DeleteByRunAsync(run.PayDataReportRunId, cancellationToken);
			var rows = PayDataAggregator.Aggregate(snapshots, profile);
			foreach (var row in rows) await _seam.SaveAsync(_rows, row, null, departmentId, WorkforceProtectedFields.ReportRow, cancellationToken);
			run.RowCount = rows.Count; run.Status = (int)PayDataReportRunStatuses.Draft; run.ValidationSummaryJson = null;
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCreated, ipAddress, userAgent, before, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		public async Task<PayDataReportRun> SaveRemarksAsync(string runId, int departmentId, string runRemarks, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			var existing = run.CloneJson();
			var before = Snapshot(run);
			if (runRemarks != ProtectedDataEnvelope.RedactionValue)
			{
				if (!string.IsNullOrEmpty(runRemarks) && runRemarks.Length > 500) throw new InvalidOperationException("paydata_remarks_too_long");
				run.RunRemarks = string.IsNullOrWhiteSpace(runRemarks) ? null : runRemarks.Trim();
			}
			run.RowVersion++; run.EditedOn = DateTime.UtcNow; run.EditedByUserId = userId;
			await _seam.SaveAsync(_runs, run, existing, departmentId, WorkforceProtectedFields.ReportRun, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCreated, ipAddress, userAgent, before, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		public async Task<PayDataValidationResult> ValidateRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			var before = Snapshot(run);
			var result = await ValidateCoreAsync(run, departmentId);
			run.ValidationSummaryJson = JsonConvert.SerializeObject(new { result.ValidatedOn, Errors = result.Errors.Select(e => new { e.Code, e.Scope, e.SubjectId }).ToList(), Warnings = result.Warnings.Select(w => new { w.Code, w.Scope, w.SubjectId }).ToList() });
			run.ExceptionCount = result.Errors.Count; run.WarningCount = result.Warnings.Count;
			run.Status = result.CanFreeze ? (int)PayDataReportRunStatuses.Validated : (int)PayDataReportRunStatuses.Draft;
			run.ReviewedByUserId = userId; run.ReviewedOn = result.ValidatedOn;
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportValidated, ipAddress, userAgent, before, run);
			return result;
		}

		private async Task<PayDataValidationResult> ValidateCoreAsync(PayDataReportRun run, int departmentId)
		{
			var result = new PayDataValidationResult { RunId = run.PayDataReportRunId, ValidatedOn = DateTime.UtcNow };
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode);
			if (profile == null) { result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.ProfileStale, Scope = "run", IsBlocking = true }); return result; }
			if (!string.Equals(run.SchemaProfileHash, ProfileHash(profile), StringComparison.Ordinal)) result.Warnings.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.ProfileStale, Scope = "run" });
			if (!profile.IsSnapshotInWindow(run.SnapshotStart, run.SnapshotEnd)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.SnapshotOutsideWindow, Scope = "run", IsBlocking = true });

			var employer = await _employers.GetActiveForDepartmentAsync(departmentId);
			if (employer == null) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EmployerIdentityMissing, Scope = "employer", IsBlocking = true });
			else
			{
				await _seam.ResolveForWorkloadAsync(new[] { employer }, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Employer);
				if (string.IsNullOrWhiteSpace(employer.LegalName) || Missing(employer.Fein) || Missing(employer.Sein) || Missing(employer.EddAddress)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EmployerIdentityMissing, Scope = "employer", SubjectId = employer.WorkforceEmployerProfileId, IsBlocking = true });
				if (employer.CoverageStatus == (int)CaliforniaPayDataCoverageStatuses.Unknown) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.CoverageUndeclared, Scope = "employer", IsBlocking = true });
				else if (employer.CoverageStatus == (int)CaliforniaPayDataCoverageStatuses.NotCovered) result.Warnings.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.CoverageUndeclared, Scope = "employer", Detail = "declared not covered" });
			}

			var snapshots = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			var included = snapshots.Where(s => s.IsIncluded).ToList();
			if (included.Count == 0) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.NoEmployees, Scope = "run", IsBlocking = true });
			foreach (var snapshot in included)
				foreach (var code in snapshot.ExceptionCodes)
					(IsBlocking(code) ? result.Errors : result.Warnings).Add(new PayDataValidationIssue { Code = code, Scope = "employee", SubjectId = snapshot.PayDataReportEmployeeSnapshotId, IsBlocking = IsBlocking(code) });

			var establishments = (await _establishments.GetForDepartmentAsync(departmentId))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEstablishment>();
			await _seam.ResolveForWorkloadAsync(establishments, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Establishment);
			foreach (var id in included.Select(s => s.WorkforceEstablishmentId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
			{
				var establishment = establishments.FirstOrDefault(e => e.WorkforceEstablishmentId == id);
				if (establishment == null) { result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EstablishmentMissing, Scope = "establishment", SubjectId = id, IsBlocking = true }); continue; }
				if (string.IsNullOrWhiteSpace(establishment.Naics)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EstablishmentNaicsMissing, Scope = "establishment", SubjectId = id, IsBlocking = true, Detail = establishment.Code });
				if (Missing(establishment.PhysicalAddress) || string.IsNullOrWhiteSpace(establishment.City) || string.IsNullOrWhiteSpace(establishment.StateCode) || string.IsNullOrWhiteSpace(establishment.PostalCode)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.EstablishmentAddressMissing, Scope = "establishment", SubjectId = id, IsBlocking = true, Detail = establishment.Code });
			}
			if (run.ReportType == (int)PayDataReportTypes.LaborContractorEmployee)
			{
				var contractors = (await _contractors.GetForDepartmentAsync(departmentId))?.Where(c => !c.IsDeleted).ToList() ?? new List<WorkforceLaborContractor>();
				await _seam.ResolveForWorkloadAsync(contractors, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Contractor);
				foreach (var id in included.Select(s => s.WorkforceLaborContractorId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct())
				{
					var contractor = contractors.FirstOrDefault(c => c.WorkforceLaborContractorId == id);
					if (contractor == null || string.IsNullOrWhiteSpace(contractor.LegalName) || Missing(contractor.Fein)) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.ContractorIdentityMissing, Scope = "contractor", SubjectId = id, IsBlocking = true });
				}
			}

			var rows = (await _rows.GetByRunAsync(run.PayDataReportRunId))?.OrderBy(r => r.SortOrder).ToList() ?? new List<PayDataReportRow>();
			if (rows.Count == 0 && included.Count > 0) result.Errors.Add(new PayDataValidationIssue { Code = "rows_not_aggregated", Scope = "run", IsBlocking = true });
			else if (rows.Count > 0)
			{
				await _seam.ResolveForWorkloadAsync(rows, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.ReportRow);
				await _seam.ResolveForWorkloadAsync(snapshots, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.EmployeeSnapshot);
				PayDataAggregator.ValidateRows(rows, snapshots, profile, result);
				var columns = PayDataAggregator.Columns(profile, run.ReportType);
				var exports = BuildExportContext(establishments, included);
				long bytes = 0;
				foreach (var row in rows)
				{
					var cells = PayDataAggregator.Cells(row, profile, run.ReportType, exports.Establishments.TryGetValue(row.WorkforceEstablishmentId ?? string.Empty, out var e) ? e : null, exports.Contractors.TryGetValue(row.WorkforceLaborContractorId ?? string.Empty, out var c) ? c : null);
					PayDataAggregator.ValidateCells(columns, cells, row.PayDataReportRowId, result);
					bytes += cells.Sum(v => (v?.Length ?? 0) + 3);
				}
				if (bytes > profile.MaxFileBytes) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.FileTooLarge, Scope = "run", IsBlocking = true });
			}
			if (!string.IsNullOrWhiteSpace(run.RunRemarks) && !WorkforceProtectionSeam.IsUnavailable(run.RunRemarks) && run.RunRemarks.Length > 500) result.Errors.Add(new PayDataValidationIssue { Code = PayDataValidationCodes.FieldTooLong, Scope = "run", IsBlocking = true, Detail = "Clarifying Remarks" });
			return result;
		}

		private static bool Missing(string value) => string.IsNullOrWhiteSpace(value) || WorkforceProtectionSeam.IsUnavailable(value);

		private sealed class ExportContext
		{
			public Dictionary<string, PayDataAggregator.ExportEstablishment> Establishments { get; } = new Dictionary<string, PayDataAggregator.ExportEstablishment>(StringComparer.OrdinalIgnoreCase);
			public Dictionary<string, PayDataAggregator.ExportContractor> Contractors { get; } = new Dictionary<string, PayDataAggregator.ExportContractor>(StringComparer.OrdinalIgnoreCase);
		}

		private static ExportContext BuildExportContext(List<WorkforceEstablishment> establishments, List<PayDataReportEmployeeSnapshot> included, List<WorkforceLaborContractor> contractors = null)
		{
			var context = new ExportContext();
			foreach (var establishment in establishments)
				context.Establishments[establishment.WorkforceEstablishmentId] = new PayDataAggregator.ExportEstablishment
				{
					Id = establishment.WorkforceEstablishmentId, Name = establishment.Name, Address = establishment.PhysicalAddress, City = establishment.City, State = establishment.StateCode, Zip = establishment.PostalCode, Naics = establishment.Naics, MajorActivity = establishment.MajorActivity,
					TotalEmployees = included.Count(s => s.WorkforceEstablishmentId == establishment.WorkforceEstablishmentId), FiledPriorYear = establishment.WasFiledPriorYear, IsHeadquarters = establishment.IsHeadquarters
				};
			foreach (var contractor in contractors ?? new List<WorkforceLaborContractor>())
				context.Contractors[contractor.WorkforceLaborContractorId] = new PayDataAggregator.ExportContractor { Id = contractor.WorkforceLaborContractorId, Name = contractor.LegalName, Fein = contractor.Fein };
			return context;
		}

		public async Task<PayDataReportRun> FreezeAndExportAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireEditableAsync(runId, departmentId);
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? throw new InvalidOperationException("paydata_profile_unavailable");
			var validation = await ValidateCoreAsync(run, departmentId);
			if (!validation.CanFreeze) throw new InvalidOperationException("paydata_validation_failed");
			var before = Snapshot(run);
			var snapshots = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.Where(s => s.IsIncluded).ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			var rows = (await _rows.GetByRunAsync(run.PayDataReportRunId))?.OrderBy(r => r.SortOrder).ToList() ?? new List<PayDataReportRow>();
			if (!await _seam.ResolveForWorkloadAsync(rows, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.ReportRow)) throw new InvalidOperationException("paydata_workload_denied");
			var establishments = (await _establishments.GetForDepartmentAsync(departmentId))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEstablishment>();
			await _seam.ResolveForWorkloadAsync(establishments, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Establishment);
			var contractors = run.ReportType == (int)PayDataReportTypes.LaborContractorEmployee ? (await _contractors.GetForDepartmentAsync(departmentId))?.Where(c => !c.IsDeleted).ToList() ?? new List<WorkforceLaborContractor>() : new List<WorkforceLaborContractor>();
			if (contractors.Count > 0) await _seam.ResolveForWorkloadAsync(contractors, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Contractor);
			var context = BuildExportContext(establishments, snapshots, contractors);
			var columns = PayDataAggregator.Columns(profile, run.ReportType);
			var cells = rows.Select(row => (IReadOnlyList<string>)PayDataAggregator.Cells(row, profile, run.ReportType, context.Establishments.TryGetValue(row.WorkforceEstablishmentId ?? string.Empty, out var e) ? e : null, context.Contractors.TryGetValue(row.WorkforceLaborContractorId ?? string.Empty, out var c) ? c : null)).ToList();
			var now = DateTime.UtcNow;
			var stem = $"CRD-PayData-{run.ReportingYear}-{(run.ReportType == (int)PayDataReportTypes.LaborContractorEmployee ? "LaborContractor" : "Payroll")}-{run.PayDataReportRunId.Substring(0, Math.Min(8, run.PayDataReportRunId.Length))}";
			var csv = PayDataAggregator.RenderCsv(columns, cells);
			var xlsx = PayDataAggregator.RenderXlsx(columns, cells);
			var csvArtifact = await _seam.SaveArtifactAsync(_artifacts, new PayDataExportArtifact { PayDataReportRunId = run.PayDataReportRunId, DepartmentId = departmentId, SchemaProfileCode = profile.Code, SchemaProfileHash = run.SchemaProfileHash, Format = (int)PayDataExportFormats.Csv, FileName = stem + ".csv", Checksum = PayDataAggregator.Sha256(csv), Data = csv, Size = csv.Length, CreatedOn = now, ExpiresOn = now.AddDays(Math.Max(1, Config.WorkforceConfig.ExportArtifactRetentionDays)), ExportedByUserId = userId }, departmentId, cancellationToken);
			await _seam.SaveArtifactAsync(_artifacts, new PayDataExportArtifact { PayDataReportRunId = run.PayDataReportRunId, DepartmentId = departmentId, SchemaProfileCode = profile.Code, SchemaProfileHash = run.SchemaProfileHash, Format = (int)PayDataExportFormats.Xlsx, FileName = stem + ".xlsx", Checksum = PayDataAggregator.Sha256(xlsx), Data = xlsx, Size = xlsx.Length, CreatedOn = now, ExpiresOn = now.AddDays(Math.Max(1, Config.WorkforceConfig.ExportArtifactRetentionDays)), ExportedByUserId = userId }, departmentId, cancellationToken);
			run.Status = (int)PayDataReportRunStatuses.Exported; run.FrozenByUserId = userId; run.FrozenOn = now; run.ExportedByUserId = userId; run.ExportedOn = now; run.CertifiedArtifactChecksum = csvArtifact.Checksum;
			run.ExceptionCount = 0; run.WarningCount = validation.Warnings.Count;
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportFrozen, ipAddress, userAgent, before, run);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportExported, ipAddress, userAgent, null, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		public async Task<List<PayDataExportArtifact>> GetArtifactsAsync(string runId, int departmentId)
		{
			var run = await RequireRunAsync(runId, departmentId);
			// Listings never carry the bytes (and never mutate the stored row).
			return (await _artifacts.GetByRunAsync(run.PayDataReportRunId))?.OrderBy(a => a.Format).Select(a => { var copy = a.CloneJson(); copy.Data = null; return copy; }).ToList() ?? new List<PayDataExportArtifact>();
		}

		public async Task<PayDataExportArtifact> DownloadArtifactAsync(string artifactId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var artifact = await _artifacts.GetByIdForDepartmentAsync(artifactId ?? string.Empty, departmentId) ?? throw new InvalidOperationException("paydata_artifact_not_found");
			if (!artifact.IsAvailable || artifact.Data == null) throw new InvalidOperationException("paydata_artifact_unavailable");
			await _seam.ResolveArtifactForReadAsync(artifact, departmentId);
			if (artifact.Data == null) throw new InvalidOperationException("paydata_artifact_unavailable");
			var data = artifact.Data;
			artifact.DownloadCount++; artifact.LastDownloadedOn = DateTime.UtcNow; artifact.LastDownloadedByUserId = userId;
			// Counters only: the stored envelope stays as it is.
			var stored = await _artifacts.GetByIdForDepartmentAsync(artifact.PayDataExportArtifactId, departmentId);
			stored.DownloadCount = artifact.DownloadCount; stored.LastDownloadedOn = artifact.LastDownloadedOn; stored.LastDownloadedByUserId = userId;
			await _artifacts.SaveOrUpdateAsync(stored, cancellationToken);
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.PayDataReportExported, ipAddress, userAgent);
			audit.After = JsonConvert.SerializeObject(new { artifact.PayDataExportArtifactId, artifact.PayDataReportRunId, artifact.FileName, artifact.Checksum, artifact.DownloadCount, Action = "download" });
			_eventAggregator.SendMessage<AuditEvent>(audit);
			artifact.Data = data;
			return artifact;
		}

		public async Task<PayDataPortalWorksheet> GetWorksheetAsync(string runId, int departmentId)
		{
			var run = await GetRunAsync(runId, departmentId) ?? throw new InvalidOperationException("paydata_run_not_found");
			var profile = CaPayDataSchemaProfile.Get(run.SchemaProfileCode) ?? CaPayDataSchemaProfile.Current;
			var employer = await _employers.GetActiveForDepartmentAsync(departmentId);
			if (employer != null) await _seam.ResolveForReadAsync(new[] { employer }, departmentId, WorkforceProtectedFields.Employer);
			var affiliates = (await _affiliates.GetForDepartmentAsync(departmentId))?.Where(a => !a.IsDeleted).ToList() ?? new List<WorkforceAffiliatedEntity>();
			await _seam.ResolveForReadAsync(affiliates, departmentId, WorkforceProtectedFields.Affiliate);
			var establishments = (await _establishments.GetForDepartmentAsync(departmentId))?.Where(e => !e.IsDeleted).ToList() ?? new List<WorkforceEstablishment>();
			await _seam.ResolveForReadAsync(establishments, departmentId, WorkforceProtectedFields.Establishment);
			var snapshots = (await _snapshots.GetByRunAsync(run.PayDataReportRunId))?.Where(s => s.IsIncluded).ToList() ?? new List<PayDataReportEmployeeSnapshot>();
			var worksheet = new PayDataPortalWorksheet
			{
				RunId = run.PayDataReportRunId, ProfileCode = run.SchemaProfileCode, ReportType = run.ReportType, ReportingYear = run.ReportingYear, SnapshotStart = run.SnapshotStart, SnapshotEnd = run.SnapshotEnd,
				EmployerLegalName = employer?.LegalName, EmployerFein = employer?.Fein, EmployerSein = employer?.Sein, EmployerSosNumber = employer?.SosNumber, EmployerNaics = employer?.Naics, EddAddress = employer?.EddAddress, HeadquartersAddress = employer?.HeadquartersAddress,
				IsIntegratedEnterprise = employer?.IsIntegratedEnterprise ?? false, FilingContactName = employer?.FilingContactName, FilingContactEmail = employer?.FilingContactEmail, FilingContactPhone = employer?.FilingContactPhone,
				UsEmployeeCount = employer?.UsEmployeeCount, CaliforniaEmployeeCount = employer?.CaliforniaEmployeeCount, SnapshotEmployeeCount = snapshots.Count, RunRemarks = run.RunRemarks, DueDate = profile.DueDate
			};
			foreach (var establishment in establishments.Where(e => snapshots.Any(s => s.WorkforceEstablishmentId == e.WorkforceEstablishmentId)).OrderBy(e => e.Code))
				worksheet.Establishments.Add(new PayDataWorksheetEstablishment { Code = establishment.Code, Name = establishment.Name, Address = establishment.PhysicalAddress, City = establishment.City, State = establishment.StateCode, Zip = establishment.PostalCode, Naics = establishment.Naics, MajorActivity = establishment.MajorActivity, IsHeadquarters = establishment.IsHeadquarters, WasFiledPriorYear = establishment.WasFiledPriorYear, EmployeeCount = snapshots.Count(s => s.WorkforceEstablishmentId == establishment.WorkforceEstablishmentId) });
			foreach (var affiliate in affiliates.OrderBy(a => a.LegalName))
				worksheet.Affiliates.Add(new PayDataWorksheetAffiliate { LegalName = affiliate.LegalName, Fein = affiliate.Fein, Sein = affiliate.Sein, SosNumber = affiliate.SosNumber, HeadquartersAddress = affiliate.HeadquartersAddress });
			return worksheet;
		}

		public async Task<PayDataReportRun> MarkCertifiedExternallyAsync(string runId, int departmentId, string certificationReference, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireRunAsync(runId, departmentId);
			if (run.Status != (int)PayDataReportRunStatuses.Exported) throw new InvalidOperationException("paydata_run_not_exported");
			if (string.IsNullOrWhiteSpace(certificationReference)) throw new InvalidOperationException("paydata_certification_reference_required");
			var before = Snapshot(run);
			run.Status = (int)PayDataReportRunStatuses.CertifiedExternally; run.CertifiedByUserId = userId; run.CertifiedOn = DateTime.UtcNow; run.CertificationReference = certificationReference.Trim();
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportMarkedCertified, ipAddress, userAgent, before, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		public async Task<PayDataReportRun> CreateCorrectionAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireRunAsync(runId, departmentId);
			// Exported or certified runs can be corrected; a run already marked Correction can take a new correction only once the previous one was voided.
			if (run.Status != (int)PayDataReportRunStatuses.Exported && run.Status != (int)PayDataReportRunStatuses.CertifiedExternally && run.Status != (int)PayDataReportRunStatuses.Correction) throw new InvalidOperationException("paydata_run_not_frozen");
			var open = (await GetRunsAsync(departmentId, run.ReportingYear)).Any(r => r.SupersedesRunId == run.PayDataReportRunId && r.Status != (int)PayDataReportRunStatuses.Void);
			if (open) throw new InvalidOperationException("paydata_correction_exists");
			var before = Snapshot(run);
			var employer = await _employers.GetActiveForDepartmentAsync(departmentId);
			var correction = new PayDataReportRun
			{
				DepartmentId = departmentId, ReportType = run.ReportType, ReportingYear = run.ReportingYear, SchemaProfileCode = run.SchemaProfileCode, SchemaProfileHash = run.SchemaProfileHash, SnapshotStart = run.SnapshotStart, SnapshotEnd = run.SnapshotEnd,
				Status = (int)PayDataReportRunStatuses.Draft, SupersedesRunId = run.PayDataReportRunId, AddedOn = DateTime.UtcNow, AddedByUserId = userId, EmployerSnapshotJson = await EmployerSnapshotAsync(employer, departmentId)
			};
			var saved = await _seam.SaveAsync(_runs, correction, null, departmentId, WorkforceProtectedFields.ReportRun, cancellationToken);
			// The superseded run stays immutable and is marked as corrected.
			run.Status = (int)PayDataReportRunStatuses.Correction;
			await TouchAsync(run, userId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCorrected, ipAddress, userAgent, before, saved);
			return await GetRunAsync(saved.PayDataReportRunId, departmentId);
		}

		public async Task<PayDataReportRun> VoidRunAsync(string runId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var run = await RequireRunAsync(runId, departmentId);
			if (run.Status == (int)PayDataReportRunStatuses.CertifiedExternally || run.Status == (int)PayDataReportRunStatuses.Correction) throw new InvalidOperationException("paydata_run_certified");
			if (run.Status == (int)PayDataReportRunStatuses.Void) return await GetRunAsync(run.PayDataReportRunId, departmentId);
			var before = Snapshot(run);
			run.Status = (int)PayDataReportRunStatuses.Void;
			await TouchAsync(run, userId, cancellationToken);
			foreach (var artifact in (await _artifacts.GetByRunAsync(run.PayDataReportRunId))?.Where(a => !a.PurgedOn.HasValue) ?? Enumerable.Empty<PayDataExportArtifact>())
			{ artifact.Data = null; artifact.PurgedOn = DateTime.UtcNow; await _artifacts.SaveOrUpdateAsync(artifact, cancellationToken); }
			Audit(departmentId, userId, AuditLogTypes.PayDataReportCorrected, ipAddress, userAgent, before, run);
			return await GetRunAsync(run.PayDataReportRunId, departmentId);
		}

		#endregion

		#region Readiness and worker 49

		public async Task<PayDataReadiness> GetReadinessAsync(int departmentId, int reportingYear)
		{
			var profile = CaPayDataSchemaProfile.ForYear(reportingYear);
			var employer = await _employers.GetActiveForDepartmentAsync(departmentId);
			var runs = await GetRunsAsync(departmentId, reportingYear);
			var yearEnd = new DateTime(reportingYear, 12, 31);
			var employments = (await _employments.GetActiveInWindowAsync(departmentId, yearEnd, yearEnd))?.Where(e => !e.IsDeleted && (e.WorkerKind == (int)WorkerKinds.PayrollEmployee || e.WorkerKind == (int)WorkerKinds.LaborContractorEmployee)).ToList() ?? new List<WorkforceEmployment>();
			var workers = employments.Select(e => e.WorkforceWorkerId).Distinct().ToList();
			var responses = (await _demographics.GetCurrentForDepartmentAsync(departmentId, DateTime.UtcNow.Date))?.Where(d => !d.IsDeleted).Select(d => d.WorkforceWorkerId).Distinct().ToList() ?? new List<string>();
			var facts = (await _annualFacts.GetForYearAsync(departmentId, reportingYear, (int)PayDataReportTypes.PayrollEmployee))?.Where(f => !f.IsDeleted).Select(f => f.WorkforceEmploymentId).Distinct().ToList() ?? new List<string>();
			var contractorFacts = (await _annualFacts.GetForYearAsync(departmentId, reportingYear, (int)PayDataReportTypes.LaborContractorEmployee))?.Where(f => !f.IsDeleted).Select(f => f.WorkforceEmploymentId).Distinct().ToList() ?? new List<string>();
			var active = runs.Where(r => r.Status != (int)PayDataReportRunStatuses.Void).ToList();
			return new PayDataReadiness
			{
				ReportingYear = reportingYear, ProfileCode = profile?.Code, ProfileAvailable = profile != null, DueDate = profile?.DueDate ?? CaPayDataSchemaProfile.SecondWednesdayOfMay(reportingYear + 1),
				CoverageStatus = employer?.CoverageStatus ?? (int)CaliforniaPayDataCoverageStatuses.Unknown,
				OpenRuns = active.Count(r => r.IsEditable), UnresolvedExceptions = active.Where(r => r.IsEditable).Sum(r => r.ExceptionCount),
				DemographicsMissing = workers.Count(w => !responses.Contains(w)),
				AnnualFactsMissing = employments.Count(e => e.WorkerKind == (int)WorkerKinds.PayrollEmployee ? !facts.Contains(e.WorkforceEmploymentId) : !contractorFacts.Contains(e.WorkforceEmploymentId)),
				HasFrozenRun = active.Any(r => r.IsFrozen), HasCertifiedRun = active.Any(r => r.Status == (int)PayDataReportRunStatuses.CertifiedExternally || (r.Status == (int)PayDataReportRunStatuses.Correction && r.CertifiedOn.HasValue))
			};
		}

		public async Task<int> RunReadinessSweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			if (!Config.WorkforceConfig.ReadinessReminderEnabled || _communication?.Value == null) return 0;
			if (asOfUtc.Month < Config.WorkforceConfig.FilingSeasonStartMonth || asOfUtc.Month > Config.WorkforceConfig.FilingSeasonEndMonth) return 0;
			var reportingYear = asOfUtc.Year - 1;
			var departments = ((await _employers.GetDepartmentsWithActiveProfilesAsync())?.ToList() ?? new List<int>()).Concat((await _runs.GetDepartmentsWithRunsAsync(reportingYear))?.ToList() ?? new List<int>()).Distinct().ToList();
			var notified = 0;
			var dayKey = asOfUtc.Date.GetHashCode();
			foreach (var departmentId in departments)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (departmentEnabled != null && !await departmentEnabled(departmentId)) continue;
				var key = HashCode.Combine(dayKey, departmentId);
				lock (RemindedToday) { if (RemindedToday.Contains(key)) continue; }
				string message;
				try
				{
					var readiness = await GetReadinessAsync(departmentId, reportingYear);
					if (readiness.CoverageStatus == (int)CaliforniaPayDataCoverageStatuses.NotCovered || (readiness.HasCertifiedRun && readiness.OpenRuns == 0)) continue;
					if (readiness.DueDate.Date < asOfUtc.Date.AddDays(-30)) continue;
					// Value-free: dates, states and counts only (plan E5) — never a name, code, earnings or rate.
					var lines = new List<string> { $"Reporting year {reportingYear} is due {readiness.DueDate:yyyy-MM-dd} ({Math.Max(0, (readiness.DueDate.Date - asOfUtc.Date).Days)} days)." };
					if (readiness.CoverageStatus == (int)CaliforniaPayDataCoverageStatuses.Unknown) lines.Add("Coverage status is not declared.");
					if (!readiness.ProfileAvailable) lines.Add("No reviewed CRD schema profile covers this year.");
					lines.Add(readiness.HasFrozenRun ? "A report has been exported but not marked certified." : readiness.OpenRuns > 0 ? $"{readiness.OpenRuns} open run(s), {readiness.UnresolvedExceptions} unresolved exception(s)." : "No report run has been started.");
					if (readiness.DemographicsMissing > 0) lines.Add($"{readiness.DemographicsMissing} worker(s) have no demographic response.");
					if (readiness.AnnualFactsMissing > 0) lines.Add($"{readiness.AnnualFactsMissing} employment(s) have no annual pay fact.");
					message = "California pay data reporting: " + string.Join(" ", lines);
				}
				catch (Exception ex) { Logging.LogException(ex, $"Pay data readiness sweep failed for department {departmentId}."); continue; }
				lock (RemindedToday) { RemindedToday.Add(key); if (RemindedToday.Count > 50_000) RemindedToday.Clear(); }
				await NotifyAdminsAsync(departmentId, message);
				notified++;
			}
			return notified;
		}

		private async Task NotifyAdminsAsync(int departmentId, string message)
		{
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				var number = _departmentSettings?.Value == null ? null : await _departmentSettings.Value.GetTextToCallNumberForDepartmentAsync(departmentId);
				// Permission 77 defaults to department administrators; the digest goes to them (a narrower assignment still includes admins).
				foreach (var admin in await _departmentsService.GetActiveAdminsForDepartmentAsync(departmentId))
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, number, department, "Pay Data Reporting");
			}
			catch (Exception ex) { Logging.LogException(ex, $"Pay data readiness digest could not be sent for department {departmentId}."); }
		}

		public async Task<int> PurgeExpiredArtifactsAsync(DateTime asOfUtc, CancellationToken cancellationToken = default)
		{
			var purged = 0;
			foreach (var artifact in (await _artifacts.GetExpiredUnpurgedAsync(asOfUtc))?.ToList() ?? new List<PayDataExportArtifact>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				artifact.Data = null; artifact.PurgedOn = asOfUtc;
				await _artifacts.SaveOrUpdateAsync(artifact, cancellationToken);
				purged++;
			}
			return purged;
		}

		#endregion

		#region Helpers

		/// <summary>Stable digest of the reviewed profile (codes, bands and column headers) so a run records exactly which schema it was built with.</summary>
		public static string ProfileHash(CaPayDataSchemaProfile profile)
		{
			var text = new StringBuilder(profile.Code).Append('|').Append(profile.ReportingYear).Append('|')
				.Append(string.Join(",", profile.JobCategories.Select(j => j.Code))).Append('|').Append(string.Join(",", profile.PayBands.Select(b => b.Code + ":" + b.Minimum.ToString(CultureInfo.InvariantCulture) + "-" + (b.Maximum?.ToString(CultureInfo.InvariantCulture) ?? "")))).Append('|')
				.Append(string.Join(",", profile.RaceEthnicities.Select(r => r.Code))).Append('|').Append(string.Join(",", profile.Sexes.Select(s => s.Code))).Append('|')
				.Append(string.Join(";", profile.PayrollColumns.Select(c => c.Header))).Append('|').Append(string.Join(";", profile.LaborContractorColumns.Select(c => c.Header)));
			return PayDataAggregator.Sha256(Encoding.UTF8.GetBytes(text.ToString()));
		}

		private async Task<PayDataReportRun> RequireRunAsync(string runId, int departmentId)
		{
			var run = string.IsNullOrWhiteSpace(runId) ? null : await _runs.GetByIdForDepartmentAsync(runId, departmentId);
			if (run == null || run.IsDeleted) throw new InvalidOperationException("paydata_run_not_found");
			return run;
		}

		private async Task<PayDataReportRun> RequireEditableAsync(string runId, int departmentId)
		{
			var run = await RequireRunAsync(runId, departmentId);
			if (!run.IsEditable) throw new InvalidOperationException("paydata_run_frozen");
			return run;
		}

		private async Task TouchAsync(PayDataReportRun run, string userId, CancellationToken cancellationToken)
		{
			run.RowVersion++; run.EditedOn = DateTime.UtcNow; run.EditedByUserId = userId;
			await _runs.SaveOrUpdateAsync(run, cancellationToken);
		}

		internal static string Snapshot(PayDataReportRun run)
		{
			var clone = run.CloneJson();
			foreach (var a in WorkforceProtectedFields.ReportRun) a.Value.Set(clone, WorkforceService.Marker(a.Value.Get(clone)));
			clone.ValidationSummaryJson = null;
			return clone.CloneJsonToString();
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, PayDataReportRun after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before; audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
