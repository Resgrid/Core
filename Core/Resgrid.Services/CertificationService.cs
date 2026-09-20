using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Certification catalog, records, role qualification and the expiry sweep (Workforce &amp; Business Operations plan,
	/// Phase D; decision 40 extends this service rather than adding a second one). The legacy members keep their
	/// behaviour for the Profile/Types/Reports pages; the D4 members audit every mutation (decision 27) and publish the
	/// D7 workflow events. Role membership is reached through Lazy&lt;IPersonnelRolesService&gt; because that service
	/// calls back into the evaluator here.
	/// </summary>
	public partial class CertificationService : ICertificationService
	{
		private readonly IDepartmentCertificationTypeRepository _departmentCertificationTypeRepository;
		private readonly IPersonnelCertificationRepository _personnelCertificationRepository;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly IPersonnelRoleCertificationRequirementRepository _requirements;
		private readonly IDepartmentCertificationSettingsRepository _settings;
		private readonly IPersonnelCertificationCreditsRepository _credits;
		private readonly IUnitCertificationRepository _unitRecords;
		private readonly IEventAggregator _eventAggregator;
		private readonly Lazy<IPersonnelRolesService> _roles;
		private readonly Lazy<IUnitsService> _units;
		private readonly Lazy<IUserProfileService> _profiles;
		private readonly Lazy<IDepartmentsService> _departments;
		private readonly Lazy<IDepartmentSettingsService> _departmentSettings;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IProtectedReadService> _protectedRead;
		/// <summary>The caller's Protected Data Grant (request-bound in the web hosts, workload elsewhere); a grant holder reads decrypted values.</summary>
		private readonly IProtectedGrantContext _grant;

		public const string SystemUserId = "system";
		private static readonly Regex CodeCleaner = new Regex("[^A-Za-z0-9._-]+", RegexOptions.Compiled);

		public CertificationService(IDepartmentCertificationTypeRepository departmentCertificationTypeRepository,
			IPersonnelCertificationRepository personnelCertificationRepository,
			Lazy<IProtectedWriteService> protectedWriteService,
			IPersonnelRoleCertificationRequirementRepository requirements,
			IDepartmentCertificationSettingsRepository settings,
			IPersonnelCertificationCreditsRepository credits,
			IUnitCertificationRepository unitRecords,
			IEventAggregator eventAggregator,
			Lazy<IPersonnelRolesService> roles,
			Lazy<IUnitsService> units,
			Lazy<IUserProfileService> profiles,
			Lazy<IDepartmentsService> departments,
			Lazy<IDepartmentSettingsService> departmentSettings,
			Lazy<ICommunicationService> communication,
			Lazy<IProtectedReadService> protectedRead = null,
			IProtectedGrantContext grant = null,
			Lazy<ISearchProjectionService> searchProjections = null)
		{
			_grant = grant;
			_departmentCertificationTypeRepository = departmentCertificationTypeRepository;
			_personnelCertificationRepository = personnelCertificationRepository;
			_protectedWriteService = protectedWriteService;
			_searchProjections = searchProjections;
			_requirements = requirements;
			_settings = settings;
			_credits = credits;
			_unitRecords = unitRecords;
			_eventAggregator = eventAggregator;
			_roles = roles;
			_units = units;
			_profiles = profiles;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_communication = communication;
			_protectedRead = protectedRead;
		}

		#region Legacy surface

		public async Task<List<DepartmentCertificationType>> GetAllCertificationTypesByDepartmentAsync(int departmentId)
		{
			var items = await _departmentCertificationTypeRepository.GetAllByDepartmentIdAsync(departmentId);
			return items != null && items.Any() ? items.ToList() : new List<DepartmentCertificationType>();
		}

		public async Task<DepartmentCertificationType> GetCertificationTypeByIdAsync(int certificationTypeId)
		{
			return await _departmentCertificationTypeRepository.GetByIdAsync(certificationTypeId);
		}

		public async Task<bool> DeleteCertificationTypeByIdAsync(int certificationTypeId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var type = await GetCertificationTypeByIdAsync(certificationTypeId);
			if (type == null || type.IsDeleted)
				return false;

			if (await _requirements.CountByTypeIdAsync(certificationTypeId) > 0
				|| await _personnelCertificationRepository.CountByTypeIdAsync(certificationTypeId) > 0
				|| await _unitRecords.CountByTypeIdAsync(certificationTypeId) > 0)
				throw new InvalidOperationException("certifications_type_in_use");

			var before = Snapshot(type);
			type.IsDeleted = true;
			type.IsActive = false;
			type.EditedOn = DateTime.UtcNow;
			await _departmentCertificationTypeRepository.SaveOrUpdateAsync(type, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectCertificationTypeAsync(type, cancellationToken);
			Audit(type.DepartmentId, null, AuditLogTypes.CertificationTypeRemoved, before, Snapshot(type));
			return true;
		}

		public async Task<DepartmentCertificationType> SaveNewCertificationTypeAsync(string certificationType, int departmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await SaveCertificationTypeAsync(new DepartmentCertificationType
			{
				DepartmentId = departmentId,
				Type = certificationType,
				Category = (int)CertificationCategories.Other,
				AppliesTo = (int)CertificationAppliesTo.Person,
				IsActive = true
			}, null, cancellationToken);
		}

		public async Task<bool> DoesCertificationTypeAlreadyExistAsync(int departmentId, string certificationTypeText)
		{
			var categories = await _departmentCertificationTypeRepository.GetAllByDepartmentIdAsync(departmentId);
			if (categories == null)
				return false;
			var text = (certificationTypeText ?? string.Empty).Trim();
			return categories.Any(x => !x.IsDeleted && (x.Type == text || string.Equals(x.Code, ToCode(text), StringComparison.OrdinalIgnoreCase)));
		}

		public async Task<List<PersonnelCertification>> GetCertificationsByUserIdAsync(string userId)
		{
			var items = await _personnelCertificationRepository.GetCertificationsByUserAsync(userId);
			return items != null && items.Any() ? items.Where(c => !c.IsDeleted).ToList() : new List<PersonnelCertification>();
		}

		public async Task<List<string>> GetDepartmentCertificationTypesAsync(int departmentId)
		{
			return (from doc in await GetAllCertificationTypesByDepartmentAsync(departmentId) where !doc.IsDeleted select doc.Type).Distinct().ToList();
		}

		public async Task<PersonnelCertification> SaveCertificationAsync(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The stored row backs REDACTED-sentinel restoration: an admin who edits a member's
			// certification without a grant posts back placeholders, and those must not be written
			// over the real values. Fetched before the save, while the id still identifies the
			// stored row rather than the incoming one.
			PersonnelCertification existing = null;
			if (certification != null && certification.PersonnelCertificationId > 0)
				existing = await _personnelCertificationRepository.GetByIdAsync(certification.PersonnelCertificationId);

			var isExistingRow = certification != null && certification.PersonnelCertificationId > 0;
			DepartmentCertificationType type = null;
			if (certification != null && certification.IsTyped)
			{
				type = await GetCertificationTypeByIdAsync(certification.DepartmentCertificationTypeId.Value);
				if (type == null || type.IsDeleted || type.DepartmentId != certification.DepartmentId)
					throw new InvalidOperationException("certifications_type_not_found");
				if (type.IsUnitScoped)
					throw new InvalidOperationException("certifications_type_scope");
				// The catalog name is the legacy match key for reports and untyped readers.
				if (string.IsNullOrWhiteSpace(certification.Type))
					certification.Type = type.Type;
				if (!isExistingRow && type.RequiresVerification && certification.Status == (int)PersonnelCertificationStatuses.Active)
					certification.Status = (int)PersonnelCertificationStatuses.PendingVerification;
			}
			if (existing != null)
			{
				// Routing metadata the edit form does not carry survives the save (Expired included: only a future expiry
				// below returns a lapsed record to service).
				certification.IsDeleted = existing.IsDeleted;
				if (certification.Status == 0 && existing.Status != 0)
					certification.Status = existing.Status;
				certification.StatusChangedOn ??= existing.StatusChangedOn;
				certification.StatusChangedByUserId ??= existing.StatusChangedByUserId;
				certification.StatusReason ??= existing.StatusReason;
				// A future expiry on an expired row returns it to service the same way RenewCertificationAsync does: a type
				// that requires sign-off goes back through PendingVerification, never straight to Active.
				if (existing.Status == (int)PersonnelCertificationStatuses.Expired && certification.ExpiresOn.HasValue && certification.ExpiresOn.Value.Date >= DateTime.UtcNow.Date)
				{
					certification.Status = type?.RequiresVerification == true ? (int)PersonnelCertificationStatuses.PendingVerification : (int)PersonnelCertificationStatuses.Active;
					certification.StatusChangedOn = DateTime.UtcNow;
					certification.StatusReason = null;
				}
				certification.VerifiedByUserId ??= existing.VerifiedByUserId;
				certification.VerifiedOn ??= existing.VerifiedOn;
				certification.IsProtected = existing.IsProtected;
			}

			// ADP write safety net (plan 4.2/19.2). The AAD row key is the identity pk, so a NEW row
			// must be inserted before it can be enveloped, and that insert is a transient plaintext
			// write. An UPDATE already has its id, so it is enveloped BEFORE the save and no
			// plaintext ever reaches the table - which is the common path here, since a member
			// edits this data far more often than they first fill it in. Fails closed either way.
			if (isExistingRow)
			{
				var preSaveWrite = await _protectedWriteService.Value.PrepareCertificationWriteAsync(
					certification.DepartmentId, certification, existing, null, null, workloadCaller: true, cancellationToken);
				if (!preSaveWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({preSaveWrite.Reason}); certification {certification.PersonnelCertificationId} was NOT saved.");
			}

			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(certification, cancellationToken);

			if (!isExistingRow)
			{
				var protectedWrite = await _protectedWriteService.Value.PrepareCertificationWriteAsync(
					saved.DepartmentId, saved, existing, null, null, workloadCaller: true, cancellationToken);
				if (!protectedWrite.Success)
					throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); certification {saved.PersonnelCertificationId} has transient plaintext pending re-encryption.");
				if (protectedWrite.Changed)
					saved = await _personnelCertificationRepository.SaveOrUpdateAsync(saved, cancellationToken);
			}

			Audit(saved.DepartmentId, null, isExistingRow ? AuditLogTypes.CertificationUpdated : AuditLogTypes.CertificationAdded, existing == null ? null : Snapshot(existing), Snapshot(saved));
			if (!isExistingRow)
				_eventAggregator.SendMessage(new CertificationAddedEvent { DepartmentId = saved.DepartmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type });
			return saved;
		}

		public async Task<PersonnelCertification> GetCertificationByIdAsync(int certificationId)
		{
			return await _personnelCertificationRepository.GetByIdAsync(certificationId);
		}

		public async Task<bool> DeleteCertification(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (certification == null)
				return false;
			await _credits.DeleteByCertificationIdAsync(certification.PersonnelCertificationId, cancellationToken);
			var deleted = await _personnelCertificationRepository.DeleteAsync(certification, cancellationToken);
			if (deleted)
				Audit(certification.DepartmentId, null, AuditLogTypes.CertificationRemoved, Snapshot(certification), null);
			return deleted;
		}

		public async Task<bool> DeleteAllCertificationsForUser(string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var certs = await _personnelCertificationRepository.GetCertificationsByUserAsync(userId);
			foreach (var cert in certs ?? Enumerable.Empty<PersonnelCertification>())
			{
				await _credits.DeleteByCertificationIdAsync(cert.PersonnelCertificationId, cancellationToken);
				await _personnelCertificationRepository.DeleteAsync(cert, cancellationToken);
			}
			return true;
		}

		#endregion

		#region Types

		public async Task<List<DepartmentCertificationType>> GetActiveCertificationTypesAsync(int departmentId, CertificationAppliesTo? appliesTo = null)
		{
			var items = await _departmentCertificationTypeRepository.GetActiveForDepartmentAsync(departmentId, appliesTo.HasValue ? (int)appliesTo.Value : (int?)null);
			return items?.ToList() ?? new List<DepartmentCertificationType>();
		}

		public Task<DepartmentCertificationType> GetCertificationTypeByCodeAsync(int departmentId, string code)
			=> _departmentCertificationTypeRepository.GetByCodeAsync(departmentId, (code ?? string.Empty).Trim());

		public async Task<DepartmentCertificationType> SaveCertificationTypeAsync(DepartmentCertificationType type, string userId, CancellationToken cancellationToken = default)
		{
			if (type == null || type.DepartmentId <= 0)
				throw new ArgumentException("A certification type needs a department.", nameof(type));
			type.Type = (type.Type ?? string.Empty).Trim();
			if (type.Type.Length == 0)
				throw new InvalidOperationException("certifications_type_name_required");
			if (type.Type.Length > 100)
				type.Type = type.Type.Substring(0, 100);
			type.Code = ToCode(string.IsNullOrWhiteSpace(type.Code) ? type.Type : type.Code);
			if (!Enum.IsDefined(typeof(CertificationCategories), type.Category))
				type.Category = (int)CertificationCategories.Other;
			if (!Enum.IsDefined(typeof(CertificationAppliesTo), type.AppliesTo))
				type.AppliesTo = (int)CertificationAppliesTo.Person;
			if (type.NeverExpires)
				type.DefaultValidityMonths = null;
			if (type.DefaultValidityMonths.HasValue && (type.DefaultValidityMonths.Value <= 0 || type.DefaultValidityMonths.Value > 600))
				throw new InvalidOperationException("certifications_type_validity_invalid");
			if (type.IsUnitScoped)
			{
				// Units do not train and nobody signs them off (plan D1.1).
				type.RenewalCreditHoursRequired = null;
				type.RequiresVerification = false;
			}
			if (type.RenewalCreditHoursRequired.HasValue && type.RenewalCreditHoursRequired.Value < 0)
				throw new InvalidOperationException("certifications_type_credits_invalid");

			DepartmentCertificationType existing = null;
			if (type.DepartmentCertificationTypeId > 0)
			{
				existing = await GetCertificationTypeByIdAsync(type.DepartmentCertificationTypeId);
				if (existing == null || existing.IsDeleted || existing.DepartmentId != type.DepartmentId)
					throw new InvalidOperationException("certifications_type_not_found");
			}

			var clash = await _departmentCertificationTypeRepository.GetByCodeAsync(type.DepartmentId, type.Code);
			if (clash != null && (existing == null || clash.DepartmentCertificationTypeId != existing.DepartmentCertificationTypeId))
				throw new InvalidOperationException("certifications_type_code_taken");

			var now = DateTime.UtcNow;
			if (existing != null)
			{
				if (!string.Equals(existing.Code, type.Code, StringComparison.Ordinal) && await _requirements.CountByTypeIdAsync(existing.DepartmentCertificationTypeId) > 0)
					throw new InvalidOperationException("certifications_type_code_locked");
				if (existing.AppliesTo != type.AppliesTo && (await _personnelCertificationRepository.CountByTypeIdAsync(existing.DepartmentCertificationTypeId) > 0 || await _unitRecords.CountByTypeIdAsync(existing.DepartmentCertificationTypeId) > 0))
					throw new InvalidOperationException("certifications_type_scope_locked");
				type.AddedOn = existing.AddedOn;
				type.AddedByUserId = existing.AddedByUserId;
				type.IsDeleted = false;
				type.EditedOn = now;
				type.EditedByUserId = userId;
			}
			else
			{
				type.AddedOn = now;
				type.AddedByUserId = userId;
				type.IsDeleted = false;
			}

			var saved = await _departmentCertificationTypeRepository.SaveOrUpdateAsync(type, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectCertificationTypeAsync(type, cancellationToken);
			Audit(saved.DepartmentId, userId, existing == null ? AuditLogTypes.CertificationTypeAdded : AuditLogTypes.CertificationTypeEdited, existing == null ? null : Snapshot(existing), Snapshot(saved));
			return saved;
		}

		public async Task<DepartmentCertificationType> CreateCertificationTypeFromTemplateAsync(int departmentId, string templateId, string userId, CancellationToken cancellationToken = default)
		{
			var template = CertificationTypeTemplateCatalog.GetById(templateId);
			if (template == null)
				throw new InvalidOperationException("certifications_template_not_found");
			return await SaveCertificationTypeAsync(CertificationTypeTemplateCatalog.ToDepartmentType(template, departmentId), userId, cancellationToken);
		}

		/// <summary>Stable code from a display name: letters, digits, dots, dashes; upper-case; at most 50 characters.</summary>
		public static string ToCode(string value)
		{
			var cleaned = CodeCleaner.Replace((value ?? string.Empty).Trim().Replace(' ', '-'), "-").Trim('-').ToUpperInvariant();
			while (cleaned.Contains("--")) cleaned = cleaned.Replace("--", "-");
			if (cleaned.Length == 0) cleaned = "TYPE";
			return cleaned.Length > 50 ? cleaned.Substring(0, 50).TrimEnd('-') : cleaned;
		}

		#endregion

		#region Personnel records

		public async Task<List<PersonnelCertification>> GetCertificationsForDepartmentAsync(int departmentId, IEnumerable<string> userIds = null)
		{
			var rows = await _personnelCertificationRepository.GetForDepartmentAsync(departmentId, userIds);
			return rows?.ToList() ?? new List<PersonnelCertification>();
		}

		private async Task<(PersonnelCertification Record, DepartmentCertificationType Type)> LoadRecordAsync(int certificationId, int departmentId)
		{
			var record = await _personnelCertificationRepository.GetByIdAsync(certificationId);
			if (record == null || record.IsDeleted || record.DepartmentId != departmentId)
				throw new InvalidOperationException("certifications_record_not_found");
			var type = record.IsTyped ? await GetCertificationTypeByIdAsync(record.DepartmentCertificationTypeId.Value) : null;
			return (record, type);
		}

		public async Task<PersonnelCertification> SetCertificationStatusAsync(int certificationId, int departmentId, PersonnelCertificationStatuses status, string reason, string userId, CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			if (status == PersonnelCertificationStatuses.Expired)
				throw new InvalidOperationException("certifications_status_invalid");
			var before = Snapshot(record);
			var oldStatus = record.Status;
			record.Status = (int)status;
			record.StatusChangedOn = DateTime.UtcNow;
			record.StatusChangedByUserId = userId;
			record.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
			if (status == PersonnelCertificationStatuses.Active && record.VerifiedOn == null && type?.RequiresVerification == true)
			{
				record.VerifiedOn = record.StatusChangedOn;
				record.VerifiedByUserId = userId;
			}
			await ProtectRecordBeforeSaveAsync(record, departmentId, cancellationToken);
			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationStatusChanged, before, Snapshot(saved));
			_eventAggregator.SendMessage(new CertificationStatusChangedEvent { DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type, OldStatus = oldStatus, NewStatus = saved.Status, Reason = record.StatusReason, ChangedByUserId = userId });
			return saved;
		}

		public async Task<PersonnelCertification> VerifyCertificationAsync(int certificationId, int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			if (record.Status != (int)PersonnelCertificationStatuses.PendingVerification)
				throw new InvalidOperationException("certifications_not_pending");
			var before = Snapshot(record);
			var oldStatus = record.Status;
			record.Status = (int)PersonnelCertificationStatuses.Active;
			record.VerifiedOn = DateTime.UtcNow;
			record.VerifiedByUserId = userId;
			record.StatusChangedOn = record.VerifiedOn;
			record.StatusChangedByUserId = userId;
			record.StatusReason = null;
			await ProtectRecordBeforeSaveAsync(record, departmentId, cancellationToken);
			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationVerified, before, Snapshot(saved));
			_eventAggregator.SendMessage(new CertificationStatusChangedEvent { DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type, OldStatus = oldStatus, NewStatus = saved.Status, Reason = "verified", ChangedByUserId = userId });
			return saved;
		}

		public async Task<PersonnelCertification> RenewCertificationAsync(int certificationId, int departmentId, DateTime? newExpiresOn, string newNumber, string userId, CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			if (type != null && !type.NeverExpires && !newExpiresOn.HasValue)
				throw new InvalidOperationException("certifications_expiry_required");
			if (newExpiresOn.HasValue && record.ExpiresOn.HasValue && newExpiresOn.Value.Date <= record.ExpiresOn.Value.Date && record.Status != (int)PersonnelCertificationStatuses.Expired)
				throw new InvalidOperationException("certifications_expiry_not_later");
			var existing = JsonConvert.DeserializeObject<PersonnelCertification>(JsonConvert.SerializeObject(record));
			var before = Snapshot(record);
			var previous = record.ExpiresOn;
			record.ExpiresOn = newExpiresOn;
			record.RecievedOn = DateTime.UtcNow.Date;
			if (!string.IsNullOrWhiteSpace(newNumber))
				record.Number = newNumber.Trim();
			if (record.Status == (int)PersonnelCertificationStatuses.Expired)
			{
				record.Status = type?.RequiresVerification == true ? (int)PersonnelCertificationStatuses.PendingVerification : (int)PersonnelCertificationStatuses.Active;
				record.StatusChangedOn = DateTime.UtcNow;
				record.StatusChangedByUserId = userId;
				record.StatusReason = null;
			}
			var write = await _protectedWriteService.Value.PrepareCertificationWriteAsync(departmentId, record, existing, null, null, workloadCaller: true, cancellationToken);
			if (!write.Success)
				throw new InvalidOperationException($"Protected write blocked ({write.Reason}); certification {certificationId} was NOT renewed.");
			var saved = await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationUpdated, before, Snapshot(saved));
			_eventAggregator.SendMessage(new CertificationRenewedEvent { DepartmentId = departmentId, Certification = saved, TypeCode = type?.Code, TypeName = type?.Type, PreviousExpiresOn = previous });
			return saved;
		}

		/// <summary>
		/// Catalog-6/28 write seam for an existing personnel record whose cataloged text (now including StatusReason) may have
		/// changed: the row is enveloped before the save so no plaintext reaches the table. A pristine copy is not needed —
		/// the record came straight from the repository, so untouched columns still hold their envelopes.
		/// </summary>
		private async Task ProtectRecordBeforeSaveAsync(PersonnelCertification record, int departmentId, CancellationToken cancellationToken)
		{
			if (_protectedWriteService?.Value == null) return;
			var write = await _protectedWriteService.Value.PrepareCertificationWriteAsync(departmentId, record, null, null, null, workloadCaller: true, cancellationToken);
			if (write != null && !write.Success)
				throw new InvalidOperationException($"Protected write blocked ({write.Reason}); certification {record.PersonnelCertificationId} was NOT saved.");
		}

		public async Task<bool> SoftDeleteCertificationAsync(int certificationId, int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var (record, type) = await LoadRecordAsync(certificationId, departmentId);
			var before = Snapshot(record);
			record.IsDeleted = true;
			await _personnelCertificationRepository.SaveOrUpdateAsync(record, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationRemoved, before, Snapshot(record));
			_eventAggregator.SendMessage(new CertificationRemovedEvent { DepartmentId = departmentId, Certification = record, TypeCode = type?.Code, TypeName = type?.Type, RemovedByUserId = userId });
			return true;
		}

		#endregion

		#region Credits

		public async Task<List<PersonnelCertificationCredit>> GetCertificationCreditsAsync(int certificationId)
		{
			var rows = (await _credits.GetByCertificationIdAsync(certificationId))?.ToList() ?? new List<PersonnelCertificationCredit>();
			if (rows.Count > 0)
				await ResolveReadAsync(rows, c => c.PersonnelCertificationCreditId > 0 ? c.PersonnelCertificationCreditId.ToString() : null, CertificationProtectedFields.Credit, rows[0].DepartmentId);
			return rows;
		}

		public async Task<PersonnelCertificationCredit> GetCertificationCreditByIdAsync(int creditId, bool includeData = false)
		{
			var credit = includeData ? await _credits.GetByIdWithDataAsync(creditId) : await _credits.GetByIdAsync(creditId);
			if (credit == null)
				return null;
			if (!includeData)
				credit.Data = null;
			await ResolveReadAsync(new[] { credit }, c => c.PersonnelCertificationCreditId.ToString(), CertificationProtectedFields.Credit, credit.DepartmentId);
			if (includeData && credit.Data != null && _protectedRead != null)
				await ResolveBinaryReadAsync(credit.DepartmentId, CertificationProtectedFields.CreditDataFieldId, credit.PersonnelCertificationCreditId.ToString(), credit.Data, bytes => credit.Data = bytes);
			return credit;
		}

		public async Task<PersonnelCertificationCredit> AddCertificationCreditAsync(PersonnelCertificationCredit credit, string userId, CancellationToken cancellationToken = default)
		{
			if (credit == null || credit.PersonnelCertificationId <= 0)
				throw new ArgumentException("A credit needs a certification.", nameof(credit));
			var (record, type) = await LoadRecordAsync(credit.PersonnelCertificationId, credit.DepartmentId);
			if (type?.IsUnitScoped == true)
				throw new InvalidOperationException("certifications_type_scope");
			if (credit.Hours <= 0 || credit.Hours > 9999)
				throw new InvalidOperationException("certifications_credit_hours_invalid");
			credit.PersonnelCertificationCreditId = 0;
			credit.DepartmentId = record.DepartmentId;
			credit.AddedOn = DateTime.UtcNow;
			credit.AddedByUserId = userId;
			if (credit.CreditDate == default)
				credit.CreditDate = credit.AddedOn.Date;
			credit.IsProtected = false;
			credit.ProtectedCatalogVersion = null;
			var data = credit.Data;
			credit.Data = null;
			var saved = await InsertProtectedAsync(_credits, credit, c => c.PersonnelCertificationCreditId > 0 ? c.PersonnelCertificationCreditId.ToString() : null, CertificationProtectedFields.Credit, MarkProtected, credit.DepartmentId, cancellationToken);
			if (data != null && data.Length > 0)
			{
				saved.Data = data;
				await ProtectBinaryAsync(saved.DepartmentId, CertificationProtectedFields.CreditDataFieldId, saved.PersonnelCertificationCreditId.ToString(), saved.Data, bytes => saved.Data = bytes, () => MarkProtected(saved), cancellationToken);
				saved = await _credits.SaveOrUpdateAsync(saved, cancellationToken);
			}
			Audit(saved.DepartmentId, userId, AuditLogTypes.CertificationCreditAdded, null, Snapshot(new { saved.PersonnelCertificationCreditId, saved.PersonnelCertificationId, saved.CreditDate, saved.Hours, saved.Category }));
			_eventAggregator.SendMessage(new CertificationCreditAddedEvent
			{
				DepartmentId = saved.DepartmentId, Certification = record, TypeCode = type?.Code, TypeName = type?.Type, PersonnelCertificationCreditId = saved.PersonnelCertificationCreditId,
				CreditDate = saved.CreditDate, Hours = saved.Hours, Category = saved.Category, AddedByUserId = userId
			});
			return saved;
		}

		public async Task<bool> DeleteCertificationCreditAsync(int creditId, int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var credit = await _credits.GetByIdAsync(creditId);
			if (credit == null || credit.DepartmentId != departmentId)
				throw new InvalidOperationException("certifications_credit_not_found");
			await _credits.DeleteAsync(credit, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.CertificationCreditRemoved, Snapshot(new { credit.PersonnelCertificationCreditId, credit.PersonnelCertificationId, credit.CreditDate, credit.Hours, credit.Category }), null);
			return true;
		}

		public Task<IReadOnlyDictionary<int, decimal>> GetCertificationCreditTotalsAsync(IEnumerable<int> certificationIds)
			=> _credits.GetHourTotalsAsync(certificationIds);

		#endregion

		#region Unit records

		public async Task<List<UnitCertification>> GetUnitCertificationsAsync(int unitId)
		{
			var rows = (await _unitRecords.GetByUnitIdAsync(unitId))?.ToList() ?? new List<UnitCertification>();
			if (rows.Count > 0)
				await ResolveReadAsync(rows, u => u.UnitCertificationId.ToString(), CertificationProtectedFields.Unit, rows[0].DepartmentId);
			return rows;
		}

		public async Task<List<UnitCertification>> GetUnitCertificationsForDepartmentAsync(int departmentId)
		{
			var rows = (await _unitRecords.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<UnitCertification>();
			await ResolveReadAsync(rows, u => u.UnitCertificationId.ToString(), CertificationProtectedFields.Unit, departmentId);
			return rows;
		}

		public async Task<UnitCertification> GetUnitCertificationByIdAsync(int unitCertificationId, bool includeData = false)
		{
			var row = includeData ? await _unitRecords.GetByIdWithDataAsync(unitCertificationId) : await _unitRecords.GetByIdAsync(unitCertificationId);
			if (row == null || row.IsDeleted)
				return null;
			if (!includeData)
				row.Data = null;
			await ResolveReadAsync(new[] { row }, u => u.UnitCertificationId.ToString(), CertificationProtectedFields.Unit, row.DepartmentId);
			if (includeData && row.Data != null && _protectedRead != null)
				await ResolveBinaryReadAsync(row.DepartmentId, CertificationProtectedFields.UnitDataFieldId, row.UnitCertificationId.ToString(), row.Data, bytes => row.Data = bytes);
			return row;
		}

		public async Task<UnitCertification> SaveUnitCertificationAsync(UnitCertification certification, string userId, CancellationToken cancellationToken = default)
		{
			if (certification == null || certification.UnitId <= 0 || certification.DepartmentId <= 0)
				throw new ArgumentException("A unit certification needs a unit and a department.", nameof(certification));
			var type = await GetCertificationTypeByIdAsync(certification.DepartmentCertificationTypeId);
			if (type == null || type.IsDeleted || type.DepartmentId != certification.DepartmentId)
				throw new InvalidOperationException("certifications_type_not_found");
			if (!type.IsUnitScoped)
				throw new InvalidOperationException("certifications_type_scope");
			var unit = await _units.Value.GetUnitByIdAsync(certification.UnitId);
			if (unit == null || unit.DepartmentId != certification.DepartmentId)
				throw new InvalidOperationException("certifications_unit_not_found");
			if (!Enum.IsDefined(typeof(UnitCertificationStatuses), certification.Status))
				certification.Status = (int)UnitCertificationStatuses.Active;

			UnitCertification existing = null;
			if (certification.UnitCertificationId > 0)
			{
				existing = await _unitRecords.GetByIdWithDataAsync(certification.UnitCertificationId);
				if (existing == null || existing.IsDeleted || existing.DepartmentId != certification.DepartmentId)
					throw new InvalidOperationException("certifications_record_not_found");
			}

			var now = DateTime.UtcNow;
			var incomingData = certification.Data;
			if (existing != null)
			{
				certification.AddedOn = existing.AddedOn;
				certification.AddedByUserId = existing.AddedByUserId;
				certification.EditedOn = now;
				certification.EditedByUserId = userId;
				certification.IsProtected = existing.IsProtected;
				certification.ProtectedCatalogVersion = existing.ProtectedCatalogVersion;
				certification.StatusChangedOn ??= existing.StatusChangedOn;
				certification.StatusChangedByUserId ??= existing.StatusChangedByUserId;
				certification.StatusReason ??= existing.StatusReason;
				if (existing.Status == (int)UnitCertificationStatuses.Expired && certification.ExpiresOn.HasValue && certification.ExpiresOn.Value.Date >= now.Date && certification.Status == (int)UnitCertificationStatuses.Expired)
					certification.Status = (int)UnitCertificationStatuses.Active;
				// No new file posted: keep the stored one (already enveloped when protected).
				if (incomingData == null || incomingData.Length == 0)
				{
					certification.Data = existing.Data;
					certification.FileName ??= existing.FileName;
					certification.FileType ??= existing.FileType;
					certification.FileSize ??= existing.FileSize;
					incomingData = null;
				}
			}
			else
			{
				certification.UnitCertificationId = 0;
				certification.AddedOn = now;
				certification.AddedByUserId = userId;
				certification.IsProtected = false;
				certification.ProtectedCatalogVersion = null;
			}
			certification.IsDeleted = false;
			if (incomingData != null && incomingData.Length > 0)
			{
				certification.FileSize = incomingData.Length;
				certification.Data = null;
			}

			var saved = await SaveProtectedAsync(_unitRecords, certification, existing, u => u.UnitCertificationId > 0 ? u.UnitCertificationId.ToString() : null, CertificationProtectedFields.Unit, MarkProtected, certification.DepartmentId, cancellationToken);
			if (incomingData != null && incomingData.Length > 0)
			{
				saved.Data = incomingData;
				await ProtectBinaryAsync(saved.DepartmentId, CertificationProtectedFields.UnitDataFieldId, saved.UnitCertificationId.ToString(), saved.Data, bytes => saved.Data = bytes, () => MarkProtected(saved), cancellationToken);
				saved = await _unitRecords.SaveOrUpdateAsync(saved, cancellationToken);
			}
			else if (existing != null && existing.Data != null)
				saved.Data = existing.Data;

			Audit(saved.DepartmentId, userId, existing == null ? AuditLogTypes.UnitCertificationAdded : AuditLogTypes.UnitCertificationUpdated, existing == null ? null : Snapshot(existing), Snapshot(saved));
			if (existing == null)
				_eventAggregator.SendMessage(new UnitCertificationAddedEvent { DepartmentId = saved.DepartmentId, Certification = WithoutBytes(saved), UnitName = unit.Name, TypeCode = type.Code, TypeName = type.Type });
			else if (existing.Status != saved.Status)
				_eventAggregator.SendMessage(new UnitCertificationStatusChangedEvent { DepartmentId = saved.DepartmentId, Certification = WithoutBytes(saved), UnitName = unit.Name, TypeCode = type.Code, TypeName = type.Type, OldStatus = existing.Status, NewStatus = saved.Status, Reason = saved.StatusReason, ChangedByUserId = userId });
			return WithoutBytes(saved);
		}

		public async Task<UnitCertification> SetUnitCertificationStatusAsync(int unitCertificationId, int departmentId, UnitCertificationStatuses status, string reason, string userId, CancellationToken cancellationToken = default)
		{
			var row = await _unitRecords.GetByIdWithDataAsync(unitCertificationId);
			if (row == null || row.IsDeleted || row.DepartmentId != departmentId)
				throw new InvalidOperationException("certifications_record_not_found");
			if (status == UnitCertificationStatuses.Expired)
				throw new InvalidOperationException("certifications_status_invalid");
			var before = Snapshot(row);
			var pristine = row.CloneJson();
			var oldStatus = row.Status;
			row.Status = (int)status;
			row.StatusChangedOn = DateTime.UtcNow;
			row.StatusChangedByUserId = userId;
			row.StatusReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
			// StatusReason is a catalog field: the reason is enveloped before it reaches the table, the file bytes stay as stored.
			var saved = await SaveProtectedAsync(_unitRecords, row, pristine, u => u.UnitCertificationId.ToString(), CertificationProtectedFields.Unit, MarkProtected, departmentId, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.UnitCertificationStatusChanged, before, Snapshot(saved));
			var (unitName, type) = await UnitEventContextAsync(saved);
			_eventAggregator.SendMessage(new UnitCertificationStatusChangedEvent { DepartmentId = departmentId, Certification = WithoutBytes(saved), UnitName = unitName, TypeCode = type?.Code, TypeName = type?.Type, OldStatus = oldStatus, NewStatus = saved.Status, Reason = saved.StatusReason, ChangedByUserId = userId });
			return WithoutBytes(saved);
		}

		public async Task<bool> DeleteUnitCertificationAsync(int unitCertificationId, int departmentId, string userId, CancellationToken cancellationToken = default)
		{
			var row = await _unitRecords.GetByIdWithDataAsync(unitCertificationId);
			if (row == null || row.IsDeleted || row.DepartmentId != departmentId)
				throw new InvalidOperationException("certifications_record_not_found");
			var before = Snapshot(row);
			row.IsDeleted = true;
			row.EditedOn = DateTime.UtcNow;
			row.EditedByUserId = userId;
			await _unitRecords.SaveOrUpdateAsync(row, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.UnitCertificationRemoved, before, Snapshot(row));
			var (unitName, type) = await UnitEventContextAsync(row);
			_eventAggregator.SendMessage(new UnitCertificationRemovedEvent { DepartmentId = departmentId, Certification = WithoutBytes(row), UnitName = unitName, TypeCode = type?.Code, TypeName = type?.Type, RemovedByUserId = userId });
			return true;
		}

		/// <summary>Unit name and catalog type for a unit-certification event; lookup failures leave them blank rather than failing the mutation.</summary>
		private async Task<(string UnitName, DepartmentCertificationType Type)> UnitEventContextAsync(UnitCertification row)
		{
			string unitName = null; DepartmentCertificationType type = null;
			try { unitName = (await _units.Value.GetUnitByIdAsync(row.UnitId))?.Name; } catch (Exception ex) { Logging.LogException(ex, "Unit name could not be read for a certification event."); }
			try { type = await GetCertificationTypeByIdAsync(row.DepartmentCertificationTypeId); } catch (Exception ex) { Logging.LogException(ex, "Certification type could not be read for a unit certification event."); }
			return (unitName, type);
		}

		#endregion

		#region Requirements and settings

		public async Task<List<PersonnelRoleCertificationRequirement>> GetRoleRequirementsAsync(int roleId)
			=> (await _requirements.GetByRoleIdAsync(roleId))?.ToList() ?? new List<PersonnelRoleCertificationRequirement>();

		public async Task<List<PersonnelRoleCertificationRequirement>> GetAllRoleRequirementsAsync(int departmentId)
			=> (await _requirements.GetAllForDepartmentAsync(departmentId))?.ToList() ?? new List<PersonnelRoleCertificationRequirement>();

		public async Task<List<PersonnelRoleCertificationRequirement>> SaveRoleRequirementsAsync(int departmentId, int roleId, List<PersonnelRoleCertificationRequirement> requirements, string userId, CancellationToken cancellationToken = default)
		{
			var role = await _roles.Value.GetRoleByIdAsync(roleId);
			if (role == null || role.DepartmentId != departmentId)
				throw new InvalidOperationException("certifications_role_not_found");

			var incoming = (requirements ?? new List<PersonnelRoleCertificationRequirement>()).Where(r => r != null && r.DepartmentCertificationTypeId > 0).ToList();
			var types = (await GetActiveCertificationTypesAsync(departmentId)).ToDictionary(t => t.DepartmentCertificationTypeId);
			foreach (var req in incoming)
			{
				if (!types.TryGetValue(req.DepartmentCertificationTypeId, out var type))
					throw new InvalidOperationException("certifications_type_not_found");
				if (type.IsUnitScoped)
					throw new InvalidOperationException("certifications_type_scope");
				if (req.GraceDaysOverride.HasValue && (req.GraceDaysOverride.Value < 0 || req.GraceDaysOverride.Value > 3650))
					throw new InvalidOperationException("certifications_grace_invalid");
			}
			// One row per type: the last one wins.
			incoming = incoming.GroupBy(r => r.DepartmentCertificationTypeId).Select(g => g.Last()).ToList();

			var existing = await GetRoleRequirementsAsync(roleId);
			var before = Snapshot(existing.Select(Requirement).ToList());
			var now = DateTime.UtcNow;
			var kept = new List<PersonnelRoleCertificationRequirement>();
			foreach (var req in incoming)
			{
				var match = existing.FirstOrDefault(e => e.DepartmentCertificationTypeId == req.DepartmentCertificationTypeId);
				var row = match ?? new PersonnelRoleCertificationRequirement { PersonnelRoleId = roleId, DepartmentId = departmentId, DepartmentCertificationTypeId = req.DepartmentCertificationTypeId, AddedOn = now, AddedByUserId = userId };
				row.IsMandatory = req.IsMandatory;
				row.AnyOfGroup = req.AnyOfGroup;
				row.AllowTrainee = req.AllowTrainee;
				row.GraceDaysOverride = req.GraceDaysOverride;
				kept.Add(await _requirements.SaveOrUpdateAsync(row, cancellationToken));
			}
			foreach (var gone in existing.Where(e => incoming.All(i => i.DepartmentCertificationTypeId != e.DepartmentCertificationTypeId)))
				await _requirements.DeleteAsync(gone, cancellationToken);

			Audit(departmentId, userId, AuditLogTypes.RoleCertificationRequirementChanged, before, Snapshot(new { roleId, role.Name, requirements = kept.Select(Requirement).ToList() }));
			return kept;
		}

		private static object Requirement(PersonnelRoleCertificationRequirement r)
			=> new { r.PersonnelRoleCertificationRequirementId, r.PersonnelRoleId, r.DepartmentCertificationTypeId, r.IsMandatory, r.AnyOfGroup, r.AllowTrainee, r.GraceDaysOverride };

		public async Task<DepartmentCertificationSettings> GetCertificationSettingsAsync(int departmentId)
			=> await _settings.GetAsync(departmentId) ?? new DepartmentCertificationSettings { DepartmentId = departmentId };

		public async Task<DepartmentCertificationSettings> SaveCertificationSettingsAsync(DepartmentCertificationSettings settings, string userId, CancellationToken cancellationToken = default)
		{
			if (settings == null || settings.DepartmentId <= 0)
				throw new ArgumentException("Settings need a department.", nameof(settings));
			if (!Enum.IsDefined(typeof(CertificationEnforcementModes), settings.EnforcementMode))
				throw new InvalidOperationException("certifications_enforcement_invalid");
			if (settings.RoleRemovalGraceDays < 0 || settings.RoleRemovalGraceDays > 3650)
				throw new InvalidOperationException("certifications_grace_invalid");
			var leadDays = DepartmentCertificationSettings.ParseLeadDays(settings.NotifyLeadDaysCsv);
			if (leadDays.Any(d => d > Config.CertificationConfig.MaxLeadDays))
				throw new InvalidOperationException("certifications_lead_days_invalid");
			settings.NotifyLeadDaysCsv = string.Join(",", leadDays);
			var existing = await _settings.GetAsync(settings.DepartmentId);
			settings.UpdatedOn = DateTime.UtcNow;
			settings.UpdatedByUserId = userId;
			var saved = await _settings.SaveAsync(settings, cancellationToken);
			Audit(settings.DepartmentId, userId, AuditLogTypes.DepartmentCertificationSettingsChanged, existing == null ? null : Snapshot(existing), Snapshot(saved));
			return saved;
		}

		#endregion

		#region Evaluation

		private async Task<(List<PersonnelRoleCertificationRequirement> Requirements, Dictionary<int, DepartmentCertificationType> Types, DepartmentCertificationSettings Settings)> LoadEvaluationContextAsync(int departmentId, int roleId)
		{
			var requirements = await GetRoleRequirementsAsync(roleId);
			var types = (await GetAllCertificationTypesByDepartmentAsync(departmentId)).Where(t => !t.IsDeleted).ToDictionary(t => t.DepartmentCertificationTypeId);
			var settings = await GetCertificationSettingsAsync(departmentId);
			return (requirements, types, settings);
		}

		public async Task<List<RoleCertificationEvaluation>> EvaluateRoleRequirementsAsync(int departmentId, int roleId, DateTime? onDate = null)
		{
			var (requirements, types, settings) = await LoadEvaluationContextAsync(departmentId, roleId);
			var members = (await _roles.Value.GetAllMembersOfRoleAsync(roleId))?.Select(m => m.UserId).Distinct().ToList() ?? new List<string>();
			var date = onDate ?? DateTime.UtcNow;
			if (requirements.Count == 0)
				return members.Select(m => new RoleCertificationEvaluation { PersonnelRoleId = roleId, UserId = m, Qualified = true }).ToList();
			var records = members.Count == 0 ? new List<PersonnelCertification>() : await GetCertificationsForDepartmentAsync(departmentId, members);
			var byUser = records.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => (IReadOnlyList<PersonnelCertification>)g.ToList());
			return members.Select(m => CertificationRequirementEvaluator.Evaluate(roleId, m, requirements, byUser.TryGetValue(m, out var mine) ? mine : Array.Empty<PersonnelCertification>(), types, settings, date)).ToList();
		}

		public async Task<RoleCertificationEvaluation> EvaluateUserForRoleAsync(int departmentId, int roleId, string userId, DateTime? onDate = null)
		{
			var (requirements, types, settings) = await LoadEvaluationContextAsync(departmentId, roleId);
			if (requirements.Count == 0)
				return new RoleCertificationEvaluation { PersonnelRoleId = roleId, UserId = userId, Qualified = true };
			var records = await GetCertificationsForDepartmentAsync(departmentId, new[] { userId });
			return CertificationRequirementEvaluator.Evaluate(roleId, userId, requirements, records, types, settings, onDate ?? DateTime.UtcNow);
		}

		public async Task<List<string>> GetQualifiedPersonnelForRoleAsync(int departmentId, int roleId)
			=> (await EvaluateRoleRequirementsAsync(departmentId, roleId)).Where(e => e.Qualified).Select(e => e.UserId).ToList();

		public async Task<List<string>> GetUsersWithValidCertificationAsync(int departmentId, IEnumerable<string> typeCodes, bool allOf = true)
		{
			var codes = (typeCodes ?? Array.Empty<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			if (codes.Count == 0)
				return new List<string>();
			var types = (await _departmentCertificationTypeRepository.GetByCodesAsync(departmentId, codes))?.ToList() ?? new List<DepartmentCertificationType>();
			if (types.Count == 0 || (allOf && types.Count < codes.Count))
				return new List<string>();
			var settings = await GetCertificationSettingsAsync(departmentId);
			var records = (await _personnelCertificationRepository.GetByTypeIdsAsync(departmentId, types.Select(t => t.DepartmentCertificationTypeId)))?.ToList() ?? new List<PersonnelCertification>();
			var today = DateTime.UtcNow;
			var byType = types.ToDictionary(t => t.DepartmentCertificationTypeId);
			var valid = records.Where(r => byType.TryGetValue(r.DepartmentCertificationTypeId ?? 0, out var t) && CertificationRequirementEvaluator.IsRecordValid(r, t, today, false, settings.TreatPendingVerificationAsValid))
				.GroupBy(r => r.UserId).ToDictionary(g => g.Key, g => g.Select(r => r.DepartmentCertificationTypeId.Value).Distinct().Count());
			return valid.Where(kv => allOf ? kv.Value >= types.Count : kv.Value > 0).Select(kv => kv.Key).OrderBy(u => u).ToList();
		}

		public async Task<CertificationExpiryDashboard> GetExpiryDashboardAsync(int departmentId, DateTime? onDate = null)
		{
			var today = (onDate ?? DateTime.UtcNow).Date;
			var types = (await GetAllCertificationTypesByDepartmentAsync(departmentId)).Where(t => !t.IsDeleted).ToList();
			var byType = types.ToDictionary(t => t.DepartmentCertificationTypeId);
			var dashboard = new CertificationExpiryDashboard
			{
				PersonTypes = types.Where(t => !t.IsUnitScoped && t.IsActive).OrderBy(t => t.Category).ThenBy(t => t.Type).ToList(),
				UnitTypes = types.Where(t => t.IsUnitScoped && t.IsActive).OrderBy(t => t.Category).ThenBy(t => t.Type).ToList()
			};
			var leadDays = (await GetCertificationSettingsAsync(departmentId)).GetNotifyLeadDays();
			var horizon = leadDays.Count > 0 ? leadDays.Max() : 60;

			var people = (await GetCertificationsForDepartmentAsync(departmentId)).Where(r => r.IsTyped && byType.ContainsKey(r.DepartmentCertificationTypeId.Value)).ToList();
			// One cached department name list instead of a profile read per member; a member missing from it (a fresh
			// account, a stale list) still resolves through the profile.
			var departmentNames = (await _departments.Value.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.Where(n => !string.IsNullOrWhiteSpace(n.UserId)).GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.First().Name?.Trim(), StringComparer.OrdinalIgnoreCase);
			var names = new Dictionary<string, string>();
			foreach (var userId in people.Select(p => p.UserId).Distinct())
				names[userId] = departmentNames.TryGetValue(userId, out var known) && !string.IsNullOrWhiteSpace(known) ? known : await DisplayNameAsync(userId);
			foreach (var group in people.GroupBy(r => new { r.UserId, TypeId = r.DepartmentCertificationTypeId.Value }))
			{
				var type = byType[group.Key.TypeId];
				// One cell per person × type: the most favourable live record.
				var best = group.OrderBy(r => r.Status == (int)PersonnelCertificationStatuses.Active ? 0 : r.Status == (int)PersonnelCertificationStatuses.Trainee || r.Status == (int)PersonnelCertificationStatuses.PendingVerification ? 1 : 2)
					.ThenByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).First();
				dashboard.PersonCells.Add(Cell(group.Key.UserId, names[group.Key.UserId], type, best.PersonnelCertificationId, best.Status, best.ExpiresOn, today));
			}

			var units = (await _unitRecords.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<UnitCertification>();
			var unitNames = new Dictionary<int, string>();
			foreach (var group in units.Where(u => byType.ContainsKey(u.DepartmentCertificationTypeId)).GroupBy(u => new { u.UnitId, u.DepartmentCertificationTypeId }))
			{
				if (!unitNames.TryGetValue(group.Key.UnitId, out var unitName))
				{
					var unit = await _units.Value.GetUnitByIdAsync(group.Key.UnitId);
					unitName = unit?.Name ?? $"Unit {group.Key.UnitId}";
					unitNames[group.Key.UnitId] = unitName;
				}
				var type = byType[group.Key.DepartmentCertificationTypeId];
				var best = group.OrderBy(u => u.Status == (int)UnitCertificationStatuses.Active ? 0 : 1).ThenByDescending(u => u.ExpiresOn ?? DateTime.MaxValue).First();
				dashboard.UnitCells.Add(Cell(group.Key.UnitId.ToString(), unitName, type, best.UnitCertificationId, best.Status, best.ExpiresOn, today));
			}

			dashboard.Horizon = horizon;
			dashboard.RecountTotals();
			return dashboard;
		}

		private static CertificationDashboardCell Cell(string subjectId, string subjectName, DepartmentCertificationType type, int recordId, int status, DateTime? expiresOn, DateTime today)
			=> new CertificationDashboardCell
			{
				SubjectId = subjectId, SubjectName = subjectName, DepartmentCertificationTypeId = type.DepartmentCertificationTypeId, TypeCode = type.Code, TypeName = type.Type, Category = type.Category,
				RecordId = recordId, Status = status, ExpiresOn = expiresOn, NeverExpires = type.NeverExpires,
				DaysUntilExpiry = CertificationRequirementEvaluator.DaysUntilExpiry(expiresOn, type.NeverExpires, today)
			};

		#endregion

		#region Helpers

		private async Task<string> DisplayNameAsync(string userId)
		{
			try
			{
				var profile = await _profiles.Value.GetProfileByUserIdAsync(userId);
				var name = profile?.FullName?.AsFirstNameLastName;
				return string.IsNullOrWhiteSpace(name) ? userId : name;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return userId;
			}
		}

		/// <summary>A copy for callers without the file bytes; the stored row (and any live reference to it) keeps them.</summary>
		private static UnitCertification WithoutBytes(UnitCertification row)
		{
			var copy = JsonConvert.DeserializeObject<UnitCertification>(JsonConvert.SerializeObject(row, new JsonSerializerSettings { ContractResolver = new SkipDataResolver() }));
			copy.Data = null;
			return copy;
		}

		private sealed class SkipDataResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
		{
			protected override Newtonsoft.Json.Serialization.JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
			{
				var property = base.CreateProperty(member, memberSerialization);
				if (member.Name == nameof(UnitCertification.Data) || member.Name == "IdValue") property.ShouldSerialize = _ => false;
				return property;
			}
		}

		private static string Snapshot(object value)
		{
			if (value == null) return null;
			switch (value)
			{
				case PersonnelCertification c:
					return JsonConvert.SerializeObject(new { c.PersonnelCertificationId, c.DepartmentId, c.UserId, c.DepartmentCertificationTypeId, c.Type, c.Status, c.StatusReason, c.ExpiresOn, c.RecievedOn, c.VerifiedOn, c.VerifiedByUserId, c.IsDeleted, c.IsProtected });
				case UnitCertification u:
					return JsonConvert.SerializeObject(new { u.UnitCertificationId, u.DepartmentId, u.UnitId, u.DepartmentCertificationTypeId, u.Status, u.StatusReason, u.IssuedOn, u.ExpiresOn, u.FileName, u.FileSize, u.IsDeleted, u.IsProtected });
				case DepartmentCertificationType t:
					return JsonConvert.SerializeObject(new { t.DepartmentCertificationTypeId, t.DepartmentId, t.Type, t.Code, t.Category, t.AppliesTo, t.IssuingAuthority, t.DefaultValidityMonths, t.NeverExpires, t.RenewalCreditHoursRequired, t.RequiresVerification, t.IsActive, t.IsDeleted });
				default:
					return JsonConvert.SerializeObject(value);
			}
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after, bool successful = true)
		{
			_eventAggregator.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId ?? SystemUserId,
				Type = type,
				Successful = successful,
				Before = before,
				After = after,
				ServerName = Environment.MachineName
			});
		}

		#endregion
	}
}
