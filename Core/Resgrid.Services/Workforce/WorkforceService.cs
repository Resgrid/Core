using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
	/// Employer identity, affiliates, establishments, labor contractors, workers, employment periods, job
	/// assignments, work entries and annual pay facts (Workforce &amp; Business Operations plan, E3
	/// <c>IWorkforceService</c> / <c>IWorkforceImportService</c>). Identifiers, addresses, external worker keys,
	/// approved payroll cost and annual earnings ride the ADP seam (catalog 28); overlapping employment periods
	/// and job assignments are refused; imports dry-run before anything commits. Callers authorize (75/76/77).
	/// </summary>
	public class WorkforceService : IWorkforceService
	{
		private readonly IWorkforceEmployerProfileRepository _employers;
		private readonly IWorkforceAffiliatedEntityRepository _affiliates;
		private readonly IWorkforceEstablishmentRepository _establishments;
		private readonly IWorkforceLaborContractorRepository _contractors;
		private readonly IWorkforceWorkerRepository _workers;
		private readonly IWorkforceEmploymentRepository _employments;
		private readonly IWorkforceJobAssignmentRepository _assignments;
		private readonly IWorkforceWorkEntryRepository _workEntries;
		private readonly IWorkforceAnnualPayFactRepository _annualFacts;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;
		private readonly WorkforceProtectionSeam _seam;

		public WorkforceService(IWorkforceEmployerProfileRepository employers, IWorkforceAffiliatedEntityRepository affiliates, IWorkforceEstablishmentRepository establishments,
			IWorkforceLaborContractorRepository contractors, IWorkforceWorkerRepository workers, IWorkforceEmploymentRepository employments, IWorkforceJobAssignmentRepository assignments,
			IWorkforceWorkEntryRepository workEntries, IWorkforceAnnualPayFactRepository annualFacts, IUserProfileService userProfileService, IEventAggregator eventAggregator,
			Lazy<IProtectedWriteService> protectedWrite = null, Lazy<IProtectedReadService> protectedRead = null, IProtectedGrantContext grant = null)
		{
			_employers = employers;
			_affiliates = affiliates;
			_establishments = establishments;
			_contractors = contractors;
			_workers = workers;
			_employments = employments;
			_assignments = assignments;
			_workEntries = workEntries;
			_annualFacts = annualFacts;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_seam = new WorkforceProtectionSeam(protectedWrite, protectedRead, grant);
		}

		#region Employer, affiliates, establishments, contractors

		public async Task<WorkforceEmployerProfile> GetEmployerProfileAsync(int departmentId)
		{
			var profile = await _employers.GetActiveForDepartmentAsync(departmentId);
			if (profile == null) return null;
			await _seam.ResolveForReadAsync(new[] { profile }, departmentId, WorkforceProtectedFields.Employer);
			return profile;
		}

		public async Task<WorkforceEmployerProfile> SaveEmployerProfileAsync(WorkforceEmployerProfile profile, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (profile == null) throw new ArgumentNullException(nameof(profile));
			if (string.IsNullOrWhiteSpace(profile.LegalName)) throw new InvalidOperationException("workforce_employer_name_required");
			if (!string.IsNullOrWhiteSpace(profile.Naics) && (profile.Naics.Trim().Length != 6 || !profile.Naics.Trim().All(char.IsDigit))) throw new InvalidOperationException("workforce_naics_invalid");
			if (!Enum.IsDefined(typeof(CaliforniaPayDataCoverageStatuses), profile.CoverageStatus)) throw new InvalidOperationException("workforce_coverage_invalid");
			var existing = await _employers.GetActiveForDepartmentAsync(profile.DepartmentId);
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceEmployerProfile { DepartmentId = profile.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.LegalName = profile.LegalName.Trim();
			target.Naics = Trim(profile.Naics);
			target.IsIntegratedEnterprise = profile.IsIntegratedEnterprise;
			target.CoverageStatus = profile.CoverageStatus;
			target.UsEmployeeCount = profile.UsEmployeeCount;
			target.CaliforniaEmployeeCount = profile.CaliforniaEmployeeCount;
			target.EffectiveOn = profile.EffectiveOn;
			target.ExpiresOn = profile.ExpiresOn;
			target.IsActive = true;
			foreach (var accessor in WorkforceProtectedFields.Employer) Keep(existing, target, profile, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_employers, target, existing, profile.DepartmentId, WorkforceProtectedFields.Employer, cancellationToken);
			Audit(profile.DepartmentId, userId, AuditLogTypes.WorkforceEmployerProfileChanged, ipAddress, userAgent, before, saved);
			return await GetEmployerProfileAsync(profile.DepartmentId);
		}

		public async Task<List<WorkforceAffiliatedEntity>> GetAffiliatesAsync(int departmentId)
		{
			var rows = (await _affiliates.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceAffiliatedEntity>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.Affiliate);
			return rows;
		}

		public async Task<WorkforceAffiliatedEntity> SaveAffiliateAsync(WorkforceAffiliatedEntity entity, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (entity == null) throw new ArgumentNullException(nameof(entity));
			if (string.IsNullOrWhiteSpace(entity.LegalName)) throw new InvalidOperationException("workforce_affiliate_name_required");
			var existing = string.IsNullOrWhiteSpace(entity.WorkforceAffiliatedEntityId) ? null : await _affiliates.GetByIdForDepartmentAsync(entity.WorkforceAffiliatedEntityId, entity.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceAffiliatedEntity { DepartmentId = entity.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.WorkforceEmployerProfileId = Trim(entity.WorkforceEmployerProfileId) ?? (await _employers.GetActiveForDepartmentAsync(entity.DepartmentId))?.WorkforceEmployerProfileId;
			target.LegalName = entity.LegalName.Trim();
			target.EffectiveOn = entity.EffectiveOn; target.ExpiresOn = entity.ExpiresOn;
			foreach (var accessor in WorkforceProtectedFields.Affiliate) Keep(existing, target, entity, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_affiliates, target, existing, entity.DepartmentId, WorkforceProtectedFields.Affiliate, cancellationToken);
			Audit(entity.DepartmentId, userId, AuditLogTypes.WorkforceEmployerProfileChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public Task<bool> DeleteAffiliateAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SoftDeleteAsync(_affiliates, () => _affiliates.GetByIdForDepartmentAsync(id, departmentId), r => r.IsDeleted, (r, v) => r.IsDeleted = v, (r, on, by) => { r.EditedOn = on; r.EditedByUserId = by; }, departmentId, userId, ipAddress, userAgent, AuditLogTypes.WorkforceEmployerProfileChanged, cancellationToken);

		public async Task<List<WorkforceEstablishment>> GetEstablishmentsAsync(int departmentId)
		{
			var rows = (await _establishments.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceEstablishment>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.Establishment);
			return rows;
		}

		public async Task<WorkforceEstablishment> GetEstablishmentAsync(string id, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(id)) return null;
			var row = await _establishments.GetByIdForDepartmentAsync(id, departmentId);
			if (row == null || row.IsDeleted) return null;
			await _seam.ResolveForReadAsync(new[] { row }, departmentId, WorkforceProtectedFields.Establishment);
			return row;
		}

		public async Task<WorkforceEstablishment> SaveEstablishmentAsync(WorkforceEstablishment establishment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (establishment == null) throw new ArgumentNullException(nameof(establishment));
			if (string.IsNullOrWhiteSpace(establishment.Code) || string.IsNullOrWhiteSpace(establishment.Name)) throw new InvalidOperationException("workforce_establishment_code_required");
			if (!string.IsNullOrWhiteSpace(establishment.Naics) && (establishment.Naics.Trim().Length != 6 || !establishment.Naics.Trim().All(char.IsDigit))) throw new InvalidOperationException("workforce_naics_invalid");
			if (establishment.ActiveTo.HasValue && establishment.ActiveFrom.HasValue && establishment.ActiveTo < establishment.ActiveFrom) throw new InvalidOperationException("workforce_dates_invalid");
			var all = (await _establishments.GetForDepartmentAsync(establishment.DepartmentId))?.ToList() ?? new List<WorkforceEstablishment>();
			if (all.Any(e => e.WorkforceEstablishmentId != establishment.WorkforceEstablishmentId && string.Equals(e.Code, establishment.Code.Trim(), StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("workforce_establishment_code_duplicate");
			var existing = string.IsNullOrWhiteSpace(establishment.WorkforceEstablishmentId) ? null : await _establishments.GetByIdForDepartmentAsync(establishment.WorkforceEstablishmentId, establishment.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceEstablishment { DepartmentId = establishment.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.WorkforceAffiliatedEntityId = Trim(establishment.WorkforceAffiliatedEntityId);
			target.Code = establishment.Code.Trim(); target.Name = establishment.Name.Trim();
			target.City = Trim(establishment.City); target.StateCode = Trim(establishment.StateCode)?.ToUpperInvariant(); target.PostalCode = Trim(establishment.PostalCode);
			target.Naics = Trim(establishment.Naics); target.MajorActivity = Trim(establishment.MajorActivity);
			target.IsHeadquarters = establishment.IsHeadquarters; target.WasFiledPriorYear = establishment.WasFiledPriorYear;
			target.ActiveFrom = establishment.ActiveFrom; target.ActiveTo = establishment.ActiveTo; target.TimeZoneId = Trim(establishment.TimeZoneId);
			foreach (var accessor in WorkforceProtectedFields.Establishment) Keep(existing, target, establishment, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_establishments, target, existing, establishment.DepartmentId, WorkforceProtectedFields.Establishment, cancellationToken);
			Audit(establishment.DepartmentId, userId, AuditLogTypes.WorkforceEstablishmentChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public Task<bool> DeleteEstablishmentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SoftDeleteAsync(_establishments, () => _establishments.GetByIdForDepartmentAsync(id, departmentId), r => r.IsDeleted, (r, v) => r.IsDeleted = v, (r, on, by) => { r.EditedOn = on; r.EditedByUserId = by; }, departmentId, userId, ipAddress, userAgent, AuditLogTypes.WorkforceEstablishmentChanged, cancellationToken);

		public async Task<List<WorkforceLaborContractor>> GetLaborContractorsAsync(int departmentId)
		{
			var rows = (await _contractors.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceLaborContractor>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.Contractor);
			return rows;
		}

		public async Task<WorkforceLaborContractor> SaveLaborContractorAsync(WorkforceLaborContractor contractor, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (contractor == null) throw new ArgumentNullException(nameof(contractor));
			if (string.IsNullOrWhiteSpace(contractor.LegalName)) throw new InvalidOperationException("workforce_contractor_name_required");
			if (contractor.RelationshipEndOn.HasValue && contractor.RelationshipStartOn.HasValue && contractor.RelationshipEndOn < contractor.RelationshipStartOn) throw new InvalidOperationException("workforce_dates_invalid");
			var existing = string.IsNullOrWhiteSpace(contractor.WorkforceLaborContractorId) ? null : await _contractors.GetByIdForDepartmentAsync(contractor.WorkforceLaborContractorId, contractor.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceLaborContractor { DepartmentId = contractor.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.LegalName = contractor.LegalName.Trim(); target.OwnershipName = Trim(contractor.OwnershipName); target.Dba = Trim(contractor.Dba);
			target.IdentifierType = Trim(contractor.IdentifierType) ?? "FEIN";
			target.RelationshipStartOn = contractor.RelationshipStartOn; target.RelationshipEndOn = contractor.RelationshipEndOn;
			target.Provenance = Trim(contractor.Provenance); target.IsActive = contractor.IsActive;
			foreach (var accessor in WorkforceProtectedFields.Contractor) Keep(existing, target, contractor, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_contractors, target, existing, contractor.DepartmentId, WorkforceProtectedFields.Contractor, cancellationToken);
			Audit(contractor.DepartmentId, userId, AuditLogTypes.WorkforceEmployerProfileChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public Task<bool> DeleteLaborContractorAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SoftDeleteAsync(_contractors, () => _contractors.GetByIdForDepartmentAsync(id, departmentId), r => r.IsDeleted, (r, v) => r.IsDeleted = v, (r, on, by) => { r.EditedOn = on; r.EditedByUserId = by; }, departmentId, userId, ipAddress, userAgent, AuditLogTypes.WorkforceEmployerProfileChanged, cancellationToken);

		#endregion

		#region Workers, employments, assignments

		public async Task<List<WorkforceWorker>> GetWorkersAsync(int departmentId)
		{
			var rows = (await _workers.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceWorker>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.Worker);
			await NameWorkersAsync(rows);
			return rows;
		}

		public async Task<WorkforceWorker> GetWorkerAsync(string id, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(id)) return null;
			var row = await _workers.GetByIdForDepartmentAsync(id, departmentId);
			if (row == null || row.IsDeleted) return null;
			await _seam.ResolveForReadAsync(new[] { row }, departmentId, WorkforceProtectedFields.Worker);
			await NameWorkersAsync(new[] { row });
			return row;
		}

		public async Task<WorkforceWorker> GetOrCreateWorkerForUserAsync(int departmentId, string userId, string actorUserId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("A user id is required.", nameof(userId));
			var existing = await _workers.GetByUserIdAsync(departmentId, userId);
			if (existing != null) return existing;
			var created = await _workers.SaveOrUpdateAsync(new WorkforceWorker { DepartmentId = departmentId, UserId = userId, AddedOn = DateTime.UtcNow, AddedByUserId = actorUserId }, cancellationToken);
			return created;
		}

		public async Task<WorkforceWorker> SaveWorkerAsync(WorkforceWorker worker, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (worker == null) throw new ArgumentNullException(nameof(worker));
			if (string.IsNullOrWhiteSpace(worker.UserId) && string.IsNullOrWhiteSpace(worker.ExternalWorkerKey) && string.IsNullOrWhiteSpace(worker.WorkforceWorkerId)) throw new InvalidOperationException("workforce_worker_identity_required");
			var existing = string.IsNullOrWhiteSpace(worker.WorkforceWorkerId) ? null : await _workers.GetByIdForDepartmentAsync(worker.WorkforceWorkerId, worker.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			if (existing == null && !string.IsNullOrWhiteSpace(worker.UserId) && await _workers.GetByUserIdAsync(worker.DepartmentId, worker.UserId) != null) throw new InvalidOperationException("workforce_worker_duplicate");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceWorker { DepartmentId = worker.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.UserId = Trim(worker.UserId) ?? target.UserId;
			target.IsActive = worker.IsActive;
			foreach (var accessor in WorkforceProtectedFields.Worker) Keep(existing, target, worker, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_workers, target, existing, worker.DepartmentId, WorkforceProtectedFields.Worker, cancellationToken);
			Audit(worker.DepartmentId, userId, AuditLogTypes.WorkforceEmploymentChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<List<WorkforceEmployment>> GetEmploymentsAsync(int departmentId)
		{
			var rows = (await _employments.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceEmployment>();
			await LoadAssignmentsAsync(rows);
			return rows;
		}

		public async Task<List<WorkforceEmployment>> GetEmploymentsForWorkerAsync(string workerId, int departmentId)
		{
			var rows = (await _employments.GetByWorkerAsync(workerId))?.Where(e => e.DepartmentId == departmentId).ToList() ?? new List<WorkforceEmployment>();
			await LoadAssignmentsAsync(rows);
			return rows;
		}

		public async Task<WorkforceEmployment> GetEmploymentAsync(string id, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(id)) return null;
			var row = await _employments.GetByIdForDepartmentAsync(id, departmentId);
			if (row == null || row.IsDeleted) return null;
			await LoadAssignmentsAsync(new[] { row });
			return row;
		}

		private async Task LoadAssignmentsAsync(IReadOnlyList<WorkforceEmployment> rows)
		{
			if (rows.Count == 0) return;
			var assignments = (await _assignments.GetByEmploymentsAsync(rows.Select(r => r.WorkforceEmploymentId)))?.ToList() ?? new List<WorkforceJobAssignment>();
			foreach (var row in rows) row.Assignments = assignments.Where(a => a.WorkforceEmploymentId == row.WorkforceEmploymentId).OrderBy(a => a.EffectiveOn).ToList();
		}

		public async Task<WorkforceEmployment> SaveEmploymentAsync(WorkforceEmployment employment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (employment == null) throw new ArgumentNullException(nameof(employment));
			if (string.IsNullOrWhiteSpace(employment.WorkforceWorkerId)) throw new InvalidOperationException("workforce_worker_required");
			if (!Enum.IsDefined(typeof(WorkerKinds), employment.WorkerKind)) throw new InvalidOperationException("workforce_worker_kind_invalid");
			if (employment.EndOn.HasValue && employment.EndOn < employment.StartOn) throw new InvalidOperationException("workforce_dates_invalid");
			if (employment.WorkerKind == (int)WorkerKinds.LaborContractorEmployee && string.IsNullOrWhiteSpace(employment.WorkforceLaborContractorId)) throw new InvalidOperationException("workforce_contractor_required");
			var worker = await _workers.GetByIdForDepartmentAsync(employment.WorkforceWorkerId, employment.DepartmentId);
			if (worker == null || worker.IsDeleted) throw new InvalidOperationException("workforce_worker_not_found");
			var siblings = (await _employments.GetByWorkerAsync(employment.WorkforceWorkerId))?.Where(e => e.WorkforceEmploymentId != employment.WorkforceEmploymentId).ToList() ?? new List<WorkforceEmployment>();
			if (siblings.Any(s => s.Overlaps(employment))) throw new InvalidOperationException("workforce_employment_overlap");
			if (!string.IsNullOrWhiteSpace(employment.DefaultEstablishmentId) && (await _establishments.GetByIdForDepartmentAsync(employment.DefaultEstablishmentId, employment.DepartmentId)) == null) throw new InvalidOperationException("workforce_establishment_not_found");
			var existing = string.IsNullOrWhiteSpace(employment.WorkforceEmploymentId) ? null : await _employments.GetByIdForDepartmentAsync(employment.WorkforceEmploymentId, employment.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("workforce_not_found");
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceEmployment { DepartmentId = employment.DepartmentId, WorkforceWorkerId = employment.WorkforceWorkerId, AddedOn = now, AddedByUserId = userId };
			target.WorkforceAffiliatedEntityId = Trim(employment.WorkforceAffiliatedEntityId);
			target.WorkforceLaborContractorId = Trim(employment.WorkforceLaborContractorId);
			target.WorkerKind = employment.WorkerKind; target.StartOn = employment.StartOn.Date; target.EndOn = employment.EndOn?.Date;
			target.EmploymentType = Enum.IsDefined(typeof(EmploymentTypes), employment.EmploymentType) ? employment.EmploymentType : 0;
			target.ExemptionStatus = Enum.IsDefined(typeof(ExemptionStatuses), employment.ExemptionStatus) ? employment.ExemptionStatus : 0;
			target.DefaultEstablishmentId = Trim(employment.DefaultEstablishmentId);
			target.CaliforniaEmployeeBasis = Enum.IsDefined(typeof(CaliforniaEmployeeBases), employment.CaliforniaEmployeeBasis) ? employment.CaliforniaEmployeeBasis : 0;
			target.PersonnelRoleId = employment.PersonnelRoleId;
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _employments.SaveOrUpdateAsync(target, cancellationToken);
			Audit(employment.DepartmentId, userId, AuditLogTypes.WorkforceEmploymentChanged, ipAddress, userAgent, before, saved);
			return await GetEmploymentAsync(saved.WorkforceEmploymentId, employment.DepartmentId);
		}

		public Task<bool> DeleteEmploymentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SoftDeleteAsync(_employments, () => _employments.GetByIdForDepartmentAsync(id, departmentId), r => r.IsDeleted, (r, v) => r.IsDeleted = v, (r, on, by) => { r.EditedOn = on; r.EditedByUserId = by; }, departmentId, userId, ipAddress, userAgent, AuditLogTypes.WorkforceEmploymentChanged, cancellationToken);

		public async Task<WorkforceJobAssignment> SaveJobAssignmentAsync(WorkforceJobAssignment assignment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (assignment == null) throw new ArgumentNullException(nameof(assignment));
			var employment = await _employments.GetByIdForDepartmentAsync(assignment.WorkforceEmploymentId ?? string.Empty, assignment.DepartmentId);
			if (employment == null || employment.IsDeleted) throw new InvalidOperationException("workforce_employment_not_found");
			if (assignment.ExpiresOn.HasValue && assignment.ExpiresOn < assignment.EffectiveOn) throw new InvalidOperationException("workforce_dates_invalid");
			if (!string.IsNullOrWhiteSpace(assignment.WorkforceEstablishmentId) && (await _establishments.GetByIdForDepartmentAsync(assignment.WorkforceEstablishmentId, assignment.DepartmentId)) == null) throw new InvalidOperationException("workforce_establishment_not_found");
			if (!string.IsNullOrWhiteSpace(assignment.JobCategoryCode))
			{
				var profile = CaPayDataSchemaProfile.Get(assignment.CaPayDataProfileCode) ?? CaPayDataSchemaProfile.Current;
				if (profile.JobCategories.All(j => j.Code != assignment.JobCategoryCode.Trim())) throw new InvalidOperationException("workforce_job_category_invalid");
				assignment.CaPayDataProfileCode = profile.Code;
			}
			if (!Enum.IsDefined(typeof(WorkModes), assignment.WorkMode)) throw new InvalidOperationException("workforce_work_mode_invalid");
			var siblings = (await _assignments.GetByEmploymentAsync(employment.WorkforceEmploymentId))?.Where(a => a.WorkforceJobAssignmentId != assignment.WorkforceJobAssignmentId).ToList() ?? new List<WorkforceJobAssignment>();
			if (siblings.Any(s => s.Overlaps(assignment))) throw new InvalidOperationException("workforce_assignment_overlap");
			var existing = string.IsNullOrWhiteSpace(assignment.WorkforceJobAssignmentId) ? null : await _assignments.GetByIdForDepartmentAsync(assignment.WorkforceJobAssignmentId, assignment.DepartmentId);
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceJobAssignment { DepartmentId = assignment.DepartmentId, WorkforceEmploymentId = employment.WorkforceEmploymentId, AddedOn = now, AddedByUserId = userId };
			target.EffectiveOn = assignment.EffectiveOn.Date; target.ExpiresOn = assignment.ExpiresOn?.Date;
			target.WorkforceEstablishmentId = Trim(assignment.WorkforceEstablishmentId) ?? employment.DefaultEstablishmentId;
			target.JobTitle = Trim(assignment.JobTitle); target.SocCode = Trim(assignment.SocCode); target.SocVersion = Trim(assignment.SocVersion);
			target.CaPayDataProfileCode = Trim(assignment.CaPayDataProfileCode); target.JobCategoryCode = Trim(assignment.JobCategoryCode);
			target.CalOesMarsAuthorityProfileCode = Trim(assignment.CalOesMarsAuthorityProfileCode); target.CalOesMarsClassificationCode = Trim(assignment.CalOesMarsClassificationCode);
			target.MappingProvenance = Trim(assignment.MappingProvenance);
			target.WorkMode = assignment.WorkMode; target.WorkCountry = Trim(assignment.WorkCountry)?.ToUpperInvariant(); target.WorkSubdivision = Trim(assignment.WorkSubdivision)?.ToUpperInvariant();
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _assignments.SaveOrUpdateAsync(target, cancellationToken);
			Audit(assignment.DepartmentId, userId, AuditLogTypes.WorkforceEmploymentChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public Task<bool> DeleteJobAssignmentAsync(string id, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default) =>
			SoftDeleteAsync(_assignments, () => _assignments.GetByIdForDepartmentAsync(id, departmentId), r => r.IsDeleted, (r, v) => r.IsDeleted = v, (r, on, by) => { r.EditedOn = on; r.EditedByUserId = by; }, departmentId, userId, ipAddress, userAgent, AuditLogTypes.WorkforceEmploymentChanged, cancellationToken);

		#endregion

		#region Work entries and annual facts

		public async Task<List<WorkforceWorkEntry>> GetWorkEntriesAsync(int departmentId, DateTime from, DateTime to)
		{
			var rows = (await _workEntries.GetForDepartmentInWindowAsync(departmentId, from, to))?.ToList() ?? new List<WorkforceWorkEntry>();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.WorkEntry);
			return rows;
		}

		public async Task<WorkforceWorkEntry> SaveWorkEntryAsync(WorkforceWorkEntry entry, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (entry == null) throw new ArgumentNullException(nameof(entry));
			if (string.IsNullOrWhiteSpace(entry.WorkforceWorkerId)) throw new InvalidOperationException("workforce_worker_required");
			if (entry.Hours < 0 || entry.Hours > 24) throw new InvalidOperationException("workforce_hours_invalid");
			if (!Enum.IsDefined(typeof(WorkHoursTypes), entry.HoursType)) throw new InvalidOperationException("workforce_hours_type_invalid");
			var worker = await _workers.GetByIdForDepartmentAsync(entry.WorkforceWorkerId, entry.DepartmentId);
			if (worker == null || worker.IsDeleted) throw new InvalidOperationException("workforce_worker_not_found");
			if (string.IsNullOrWhiteSpace(entry.WorkforceEmploymentId))
				entry.WorkforceEmploymentId = (await _employments.GetByWorkerAsync(worker.WorkforceWorkerId))?.FirstOrDefault(e => !e.IsDeleted && e.Covers(entry.WorkDate, entry.WorkDate))?.WorkforceEmploymentId;
			var existing = string.IsNullOrWhiteSpace(entry.WorkforceWorkEntryId) ? null : await _workEntries.GetByIdForDepartmentAsync(entry.WorkforceWorkEntryId, entry.DepartmentId);
			if (existing == null && !string.IsNullOrWhiteSpace(entry.ExternalSource) && !string.IsNullOrWhiteSpace(entry.ExternalId)) existing = await _workEntries.GetByExternalIdAsync(entry.DepartmentId, entry.ExternalSource, entry.ExternalId);
			var before = existing == null ? null : Snapshot(existing);
			var now = DateTime.UtcNow;
			var target = existing ?? new WorkforceWorkEntry { DepartmentId = entry.DepartmentId, WorkforceWorkerId = worker.WorkforceWorkerId, AddedOn = now, AddedByUserId = userId };
			target.WorkforceEmploymentId = entry.WorkforceEmploymentId; target.WorkDate = entry.WorkDate.Date; target.StartTime = entry.StartTime; target.EndTime = entry.EndTime;
			target.Hours = entry.Hours; target.HoursType = entry.HoursType; target.WorkforceEstablishmentId = Trim(entry.WorkforceEstablishmentId);
			target.WorkCountry = Trim(entry.WorkCountry)?.ToUpperInvariant(); target.WorkSubdivision = Trim(entry.WorkSubdivision)?.ToUpperInvariant(); target.WorkMode = Enum.IsDefined(typeof(WorkModes), entry.WorkMode) ? entry.WorkMode : 0;
			target.CallId = entry.CallId; target.DeploymentId = Trim(entry.DeploymentId); target.DeploymentTimeReportId = Trim(entry.DeploymentTimeReportId);
			target.ExternalSource = Trim(entry.ExternalSource); target.ExternalId = Trim(entry.ExternalId); target.ImportBatchId = Trim(entry.ImportBatchId);
			target.IsApproved = entry.IsApproved; target.IsReconciled = entry.IsReconciled;
			foreach (var accessor in WorkforceProtectedFields.WorkEntry) Keep(existing, target, entry, accessor.Value);
			if (existing != null) { target.RowVersion = existing.RowVersion + 1; target.EditedOn = now; target.EditedByUserId = userId; }
			var saved = await _seam.SaveAsync(_workEntries, target, existing, entry.DepartmentId, WorkforceProtectedFields.WorkEntry, cancellationToken);
			Audit(entry.DepartmentId, userId, AuditLogTypes.WorkforceCompensationChanged, ipAddress, userAgent, before, saved);
			return saved;
		}

		public async Task<List<WorkforceAnnualPayFact>> GetAnnualPayFactsAsync(int departmentId, int reportingYear, PayDataReportTypes reportType)
		{
			var rows = (await _annualFacts.GetForYearAsync(departmentId, reportingYear, (int)reportType))?.ToList() ?? new List<WorkforceAnnualPayFact>();
			// Only the current version per employment / allocation.
			rows = rows.GroupBy(f => (f.WorkforceEmploymentId, f.ClientAllocationKey ?? string.Empty)).Select(g => g.OrderByDescending(f => f.Version).First()).ToList();
			await _seam.ResolveForReadAsync(rows, departmentId, WorkforceProtectedFields.AnnualFact);
			return rows;
		}

		public async Task<WorkforceAnnualPayFact> SaveAnnualPayFactAsync(WorkforceAnnualPayFact fact, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (fact == null) throw new ArgumentNullException(nameof(fact));
			var employment = await _employments.GetByIdForDepartmentAsync(fact.WorkforceEmploymentId ?? string.Empty, fact.DepartmentId);
			if (employment == null || employment.IsDeleted) throw new InvalidOperationException("workforce_employment_not_found");
			if (fact.ReportingYear < 2020 || fact.ReportingYear > 2100) throw new InvalidOperationException("workforce_year_invalid");
			if (!Enum.IsDefined(typeof(PayDataReportTypes), fact.ReportType)) throw new InvalidOperationException("workforce_report_type_invalid");
			Normalize(fact);
			var current = (await _annualFacts.GetByEmploymentAsync(employment.WorkforceEmploymentId))?.Where(f => !f.IsDeleted && f.ReportingYear == fact.ReportingYear && f.ReportType == fact.ReportType && (f.ClientAllocationKey ?? string.Empty) == (fact.ClientAllocationKey ?? string.Empty)).OrderByDescending(f => f.Version).FirstOrDefault();
			var now = DateTime.UtcNow;
			// Corrections version: the stored fact is immutable, the new row supersedes it.
			var target = new WorkforceAnnualPayFact
			{
				DepartmentId = fact.DepartmentId, WorkforceEmploymentId = employment.WorkforceEmploymentId, ReportingYear = fact.ReportingYear, ReportType = fact.ReportType, ClientAllocationKey = Trim(fact.ClientAllocationKey),
				EarningsSource = fact.EarningsSource, ActualWorkedHours = fact.ActualWorkedHours, PaidLeaveHours = fact.PaidLeaveHours, ReportableHours = fact.ReportableHours, DaysWorked = fact.DaysWorked, WeeksWorked = fact.WeeksWorked,
				ExemptProxyMethod = fact.ExemptProxyMethod, ProxyAverageHoursPerDay = fact.ProxyAverageHoursPerDay, ClientAllocatedHours = fact.ClientAllocatedHours, ClientAllocatedWeeks = fact.ClientAllocatedWeeks,
				Source = Trim(fact.Source) ?? "manual", ImportBatchId = Trim(fact.ImportBatchId), SourceChecksum = Trim(fact.SourceChecksum), IsReconciled = fact.IsReconciled, IsApproved = fact.IsApproved,
				Version = (current?.Version ?? 0) + 1, SupersedesFactId = current?.WorkforceAnnualPayFactId, AddedOn = now, AddedByUserId = userId,
				W2Box5 = fact.W2Box5, W2Box1 = fact.W2Box1, EarningsUsed = fact.EarningsUsed, ClientAllocatedEarnings = fact.ClientAllocatedEarnings
			};
			var saved = await _seam.SaveAsync(_annualFacts, target, null, fact.DepartmentId, WorkforceProtectedFields.AnnualFact, cancellationToken);
			Audit(fact.DepartmentId, userId, AuditLogTypes.WorkforceAnnualPayFactImported, ipAddress, userAgent, current == null ? null : Snapshot(current), saved);
			return saved;
		}

		/// <summary>E1 rules: Box 5 is the earnings; Box 1 only when Box 5 is absent (remarked); reportable hours = worked + paid leave, or the exempt proxy days × average hours.</summary>
		public static void Normalize(WorkforceAnnualPayFact fact)
		{
			if (fact.ReportType == (int)PayDataReportTypes.LaborContractorEmployee)
			{
				fact.EarningsUsedValue = fact.ClientAllocatedEarningsValue;
				fact.EarningsSource = (int)EarningsSources.ClientAllocated;
				fact.ReportableHours = fact.ClientAllocatedHours ?? fact.ReportableHours;
				fact.WeeksWorked = fact.ClientAllocatedWeeks ?? fact.WeeksWorked;
				return;
			}
			if (fact.W2Box5Value.HasValue) { fact.EarningsUsedValue = fact.W2Box5Value; fact.EarningsSource = (int)EarningsSources.W2Box5; }
			else if (fact.W2Box1Value.HasValue) { fact.EarningsUsedValue = fact.W2Box1Value; fact.EarningsSource = (int)EarningsSources.W2Box1Fallback; }
			if (fact.ExemptProxyMethod == (int)ExemptProxyMethods.DaysTimesAverageHours && fact.DaysWorked.HasValue && fact.ProxyAverageHoursPerDay.HasValue)
				fact.ReportableHours = fact.DaysWorked.Value * fact.ProxyAverageHoursPerDay.Value;
			else if (fact.ActualWorkedHours.HasValue || fact.PaidLeaveHours.HasValue)
				fact.ReportableHours = (fact.ActualWorkedHours ?? 0) + (fact.PaidLeaveHours ?? 0);
		}

		public async Task<WorkforceImportResult> ImportAnnualPayFactsAsync(int departmentId, string csv, bool dryRun, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var result = new WorkforceImportResult { DryRun = dryRun, ImportBatchId = Guid.NewGuid().ToString() };
			if (string.IsNullOrWhiteSpace(csv)) { result.Issues.Add(new WorkforceImportIssue { Line = 0, Code = "empty", IsError = true }); return result; }
			var workers = (await _workers.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceWorker>();
			await _seam.ResolveForWorkloadAsync(workers, departmentId, WorkforceProtectedFields.ReportingWorkloadPurpose, WorkforceProtectedFields.Worker);
			var employments = (await _employments.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<WorkforceEmployment>();
			var rows = new List<WorkforceAnnualPayFact>();
			var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			using var reader = new StringReader(csv);
			string line; var number = 0; string[] header = null;
			while ((line = reader.ReadLine()) != null)
			{
				number++;
				if (string.IsNullOrWhiteSpace(line)) continue;
				var cells = ParseCsvLine(line);
				if (header == null) { header = cells.Select(c => c.Trim().ToLowerInvariant()).ToArray(); continue; }
				result.Total++;
				string Cell(string name) { var i = Array.IndexOf(header, name.ToLowerInvariant()); return i >= 0 && i < cells.Count ? cells[i].Trim() : null; }
				var key = Cell("ExternalWorkerKey"); var user = Cell("UserId");
				var worker = !string.IsNullOrWhiteSpace(user) ? workers.FirstOrDefault(w => string.Equals(w.UserId, user, StringComparison.OrdinalIgnoreCase)) : workers.FirstOrDefault(w => string.Equals(w.ExternalWorkerKey, key, StringComparison.OrdinalIgnoreCase));
				if (worker == null) { result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "worker_not_found", Detail = user ?? key, IsError = true }); continue; }
				if (!int.TryParse(Cell("ReportingYear"), out var year)) { result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "year_invalid", IsError = true }); continue; }
				var typeText = Cell("ReportType"); var type = string.Equals(typeText, "LaborContractorEmployee", StringComparison.OrdinalIgnoreCase) || typeText == "1" ? PayDataReportTypes.LaborContractorEmployee : PayDataReportTypes.PayrollEmployee;
				var employment = employments.Where(e => e.WorkforceWorkerId == worker.WorkforceWorkerId && !e.IsDeleted && e.Covers(new DateTime(year, 1, 1), new DateTime(year, 12, 31))).OrderByDescending(e => e.StartOn).FirstOrDefault();
				if (employment == null) { result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "employment_not_found", Detail = $"{user ?? key} {year}", IsError = true }); continue; }
				var allocation = Cell("ClientAllocationKey");
				var dedupe = $"{employment.WorkforceEmploymentId}|{year}|{(int)type}|{allocation}";
				if (!seen.Add(dedupe)) { result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "duplicate_row", Detail = dedupe, IsError = true }); continue; }
				var fact = new WorkforceAnnualPayFact
				{
					DepartmentId = departmentId, WorkforceEmploymentId = employment.WorkforceEmploymentId, ReportingYear = year, ReportType = (int)type, ClientAllocationKey = Trim(allocation),
					W2Box5Value = Dec(Cell("W2Box5")), W2Box1Value = Dec(Cell("W2Box1")), ActualWorkedHours = Dec(Cell("ActualWorkedHours")), PaidLeaveHours = Dec(Cell("PaidLeaveHours")),
					DaysWorked = int.TryParse(Cell("DaysWorked"), out var days) ? days : null, WeeksWorked = Dec(Cell("WeeksWorked")),
					ExemptProxyMethod = string.Equals(Cell("ExemptProxyMethod"), "DaysTimesAverageHours", StringComparison.OrdinalIgnoreCase) ? (int)ExemptProxyMethods.DaysTimesAverageHours : string.IsNullOrWhiteSpace(Cell("ExemptProxyMethod")) ? 0 : (int)ExemptProxyMethods.ActualPlusPaidLeave,
					ProxyAverageHoursPerDay = Dec(Cell("ProxyAverageHoursPerDay")), ClientAllocatedEarningsValue = Dec(Cell("ClientAllocatedEarnings")), ClientAllocatedHours = Dec(Cell("ClientAllocatedHours")), ClientAllocatedWeeks = Dec(Cell("ClientAllocatedWeeks")),
					Source = "import", ImportBatchId = result.ImportBatchId, SourceChecksum = PayDataAggregator.Sha256(System.Text.Encoding.UTF8.GetBytes(line)), IsReconciled = true, IsApproved = false
				};
				Normalize(fact);
				if (!fact.EarningsUsedValue.HasValue) result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "earnings_missing", Detail = user ?? key, IsError = true });
				if (!(fact.ReportableHours > 0)) result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "hours_missing", Detail = user ?? key, IsError = true });
				if (fact.EarningsSource == (int)EarningsSources.W2Box1Fallback) result.Issues.Add(new WorkforceImportIssue { Line = number, Code = "box1_fallback", Detail = user ?? key, IsError = false });
				rows.Add(fact);
			}
			if (result.HasErrors || dryRun) { result.Skipped = result.Total; return result; }
			foreach (var fact in rows)
			{
				var current = (await _annualFacts.GetByEmploymentAsync(fact.WorkforceEmploymentId))?.Any(f => !f.IsDeleted && f.ReportingYear == fact.ReportingYear && f.ReportType == fact.ReportType && (f.ClientAllocationKey ?? string.Empty) == (fact.ClientAllocationKey ?? string.Empty)) == true;
				await SaveAnnualPayFactAsync(fact, userId, ipAddress, userAgent, cancellationToken);
				if (current) result.Updated++; else result.Created++;
			}
			return result;
		}

		private static decimal? Dec(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

		public static List<string> ParseCsvLine(string line)
		{
			var cells = new List<string>(); var current = new System.Text.StringBuilder(); var quoted = false;
			for (var i = 0; i < line.Length; i++)
			{
				var c = line[i];
				if (quoted) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; } else quoted = false; } else current.Append(c); }
				else if (c == '"') quoted = true;
				else if (c == ',') { cells.Add(current.ToString()); current.Clear(); }
				else current.Append(c);
			}
			cells.Add(current.ToString());
			return cells;
		}

		#endregion

		#region Helpers

		private async Task NameWorkersAsync(IReadOnlyList<WorkforceWorker> rows)
		{
			var userIds = rows.Where(r => !string.IsNullOrWhiteSpace(r.UserId)).Select(r => r.UserId).Distinct().ToList();
			var profiles = userIds.Count == 0 ? new List<UserProfile>() : (await _userProfileService.GetSelectedUserProfilesAsync(userIds))?.ToList() ?? new List<UserProfile>();
			foreach (var row in rows)
			{
				var profile = string.IsNullOrWhiteSpace(row.UserId) ? null : profiles.FirstOrDefault(p => string.Equals(p.UserId, row.UserId, StringComparison.OrdinalIgnoreCase));
				row.DisplayName = profile?.FullName.AsFirstNameLastName ?? (WorkforceProtectionSeam.IsUnavailable(row.DisplayLabel) ? ProtectedDataEnvelope.RedactionValue : row.DisplayLabel) ?? row.UserId ?? row.WorkforceWorkerId;
			}
		}

		/// <summary>A REDACTED value posted back from an unrevealed page keeps the stored envelope; anything else is the new value.</summary>
		private static void Keep<T>(T existing, T target, T input, (Func<T, string> Get, Action<T, string> Set) accessor)
		{
			var value = accessor.Get(input);
			if (existing != null && value == ProtectedDataEnvelope.RedactionValue) { accessor.Set(target, accessor.Get(existing)); return; }
			accessor.Set(target, string.IsNullOrWhiteSpace(value) ? null : value.Trim());
		}

		private async Task<bool> SoftDeleteAsync<T>(IRepository<T> repository, Func<Task<T>> load, Func<T, bool> deleted, Action<T, bool> setDeleted, Action<T, DateTime, string> stamp, int departmentId, string userId, string ipAddress, string userAgent, AuditLogTypes type, CancellationToken cancellationToken) where T : class, IEntity
		{
			var row = await load();
			if (row == null || deleted(row)) return false;
			var before = Snapshot(row);
			setDeleted(row, true); stamp(row, DateTime.UtcNow, userId);
			await repository.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, type, ipAddress, userAgent, before, row);
			return true;
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		/// <summary>Decision 27: audit snapshots carry ids, versions, codes and dates — every protected value is replaced by a field marker.</summary>
		internal static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			switch (clone)
			{
				case WorkforceEmployerProfile r: foreach (var a in WorkforceProtectedFields.Employer) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceAffiliatedEntity r: foreach (var a in WorkforceProtectedFields.Affiliate) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceEstablishment r: foreach (var a in WorkforceProtectedFields.Establishment) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceLaborContractor r: foreach (var a in WorkforceProtectedFields.Contractor) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceWorker r: foreach (var a in WorkforceProtectedFields.Worker) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceEmployment r: r.Assignments = null; break;
				case WorkforceWorkEntry r: foreach (var a in WorkforceProtectedFields.WorkEntry) a.Value.Set(r, Marker(a.Value.Get(r))); break;
				case WorkforceAnnualPayFact r: foreach (var a in WorkforceProtectedFields.AnnualFact) a.Value.Set(r, Marker(a.Value.Get(r))); break;
			}
			return clone.CloneJsonToString();
		}

		internal static string Marker(string value) => string.IsNullOrEmpty(value) ? null : "[protected]";

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
