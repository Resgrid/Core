using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Certification catalog, personnel and unit records, role qualification and the expiry sweep. Phase D of the
	/// Workforce &amp; Business Operations plan extends this one service (decision 40); the legacy members keep their
	/// signatures so the Profile/Types/Reports surfaces and the RMS evidence adapters keep working unchanged.
	/// Every write audits (decision 27); the plan's error codes are thrown as InvalidOperationException("certifications_*").
	/// </summary>
	public interface ICertificationService
	{
		// ---- Legacy surface (kept) --------------------------------------------------------------------------------

		/// <summary>Every type row of the department, soft-deleted ones included (legacy callers filter by hand).</summary>
		Task<List<DepartmentCertificationType>> GetAllCertificationTypesByDepartmentAsync(int departmentId);
		Task<DepartmentCertificationType> GetCertificationTypeByIdAsync(int certificationTypeId);
		/// <summary>Soft delete (Phase D); refused with certifications_type_in_use while a requirement or a live record references the type.</summary>
		Task<bool> DeleteCertificationTypeByIdAsync(int certificationTypeId, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>Creates a person-scoped type from its display name; Code derives from the name.</summary>
		Task<DepartmentCertificationType> SaveNewCertificationTypeAsync(string certificationType, int departmentId, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>Full legacy read of a member's records (bytes included). Soft-deleted rows are excluded.</summary>
		Task<List<PersonnelCertification>> GetCertificationsByUserIdAsync(string userId);
		Task<List<string>> GetDepartmentCertificationTypesAsync(int departmentId);
		/// <summary>Creates or updates a record (ADP catalog 6 seam). New typed records of a RequiresVerification type start PendingVerification; raises trigger 87 on create.</summary>
		Task<PersonnelCertification> SaveCertificationAsync(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken));
		Task<PersonnelCertification> GetCertificationByIdAsync(int certificationId);
		/// <summary>Hard delete kept for the legacy Profile page and purge paths; Phase D surfaces use <see cref="SoftDeleteCertificationAsync"/>.</summary>
		Task<bool> DeleteCertification(PersonnelCertification certification, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> DeleteAllCertificationsForUser(string userId, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> DoesCertificationTypeAlreadyExistAsync(int departmentId, string certificationTypeText);

		// ---- Types (plan D4) -------------------------------------------------------------------------------------

		/// <summary>Active, non-deleted types, optionally narrowed to one scope.</summary>
		Task<List<DepartmentCertificationType>> GetActiveCertificationTypesAsync(int departmentId, CertificationAppliesTo? appliesTo = null);
		Task<DepartmentCertificationType> GetCertificationTypeByCodeAsync(int departmentId, string code);
		/// <summary>
		/// Creates or updates a typed catalog row. Code is unique per department and immutable once a requirement references it;
		/// AppliesTo is immutable once records of the type exist; unit-scoped types cannot carry credits or verification.
		/// </summary>
		Task<DepartmentCertificationType> SaveCertificationTypeAsync(DepartmentCertificationType type, string userId, CancellationToken cancellationToken = default);
		/// <summary>Projects a <see cref="Certifications.CertificationTypeTemplateCatalog"/> template into a new department type.</summary>
		Task<DepartmentCertificationType> CreateCertificationTypeFromTemplateAsync(int departmentId, string templateId, string userId, CancellationToken cancellationToken = default);

		// ---- Personnel records (plan D4) ---------------------------------------------------------------------------

		/// <summary>Non-deleted records of the department without file bytes; optionally narrowed to some members.</summary>
		Task<List<PersonnelCertification>> GetCertificationsForDepartmentAsync(int departmentId, IEnumerable<string> userIds = null);
		/// <summary>Suspend / revoke / reinstate / mark trainee with a reason: audits Before/After and raises trigger 91.</summary>
		Task<PersonnelCertification> SetCertificationStatusAsync(int certificationId, int departmentId, PersonnelCertificationStatuses status, string reason, string userId, CancellationToken cancellationToken = default);
		/// <summary>Supervisor sign-off: PendingVerification → Active, stamps the verifier; raises trigger 91.</summary>
		Task<PersonnelCertification> VerifyCertificationAsync(int certificationId, int departmentId, string userId, CancellationToken cancellationToken = default);
		/// <summary>Extends the expiry (and optionally the number) of a record, reactivating an expired one; raises trigger 88.</summary>
		Task<PersonnelCertification> RenewCertificationAsync(int certificationId, int departmentId, DateTime? newExpiresOn, string newNumber, string userId, CancellationToken cancellationToken = default);
		/// <summary>Soft delete with audit; credits stay for history.</summary>
		Task<bool> SoftDeleteCertificationAsync(int certificationId, int departmentId, string userId, CancellationToken cancellationToken = default);

		// ---- Credits (plan D1.4) ----------------------------------------------------------------------------------

		Task<List<PersonnelCertificationCredit>> GetCertificationCreditsAsync(int certificationId);
		Task<PersonnelCertificationCredit> GetCertificationCreditByIdAsync(int creditId, bool includeData = false);
		Task<PersonnelCertificationCredit> AddCertificationCreditAsync(PersonnelCertificationCredit credit, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteCertificationCreditAsync(int creditId, int departmentId, string userId, CancellationToken cancellationToken = default);
		Task<IReadOnlyDictionary<int, decimal>> GetCertificationCreditTotalsAsync(IEnumerable<int> certificationIds);

		// ---- Unit records (plan D1.8) --------------------------------------------------------------------------------

		Task<List<UnitCertification>> GetUnitCertificationsAsync(int unitId);
		Task<List<UnitCertification>> GetUnitCertificationsForDepartmentAsync(int departmentId);
		Task<UnitCertification> GetUnitCertificationByIdAsync(int unitCertificationId, bool includeData = false);
		/// <summary>Creates or updates a unit record; the type must be unit-scoped (certifications_type_scope).</summary>
		Task<UnitCertification> SaveUnitCertificationAsync(UnitCertification certification, string userId, CancellationToken cancellationToken = default);
		Task<UnitCertification> SetUnitCertificationStatusAsync(int unitCertificationId, int departmentId, UnitCertificationStatuses status, string reason, string userId, CancellationToken cancellationToken = default);
		Task<bool> DeleteUnitCertificationAsync(int unitCertificationId, int departmentId, string userId, CancellationToken cancellationToken = default);

		// ---- Role requirements and settings (plan D1.5 / D1.6) ------------------------------------------------------------

		Task<List<PersonnelRoleCertificationRequirement>> GetRoleRequirementsAsync(int roleId);
		Task<List<PersonnelRoleCertificationRequirement>> GetAllRoleRequirementsAsync(int departmentId);
		/// <summary>Replaces a role's requirement set; every type must be person-scoped and belong to the department.</summary>
		Task<List<PersonnelRoleCertificationRequirement>> SaveRoleRequirementsAsync(int departmentId, int roleId, List<PersonnelRoleCertificationRequirement> requirements, string userId, CancellationToken cancellationToken = default);
		/// <summary>The department's settings, or the defaults (EnforcementMode Off) when it never saved any.</summary>
		Task<DepartmentCertificationSettings> GetCertificationSettingsAsync(int departmentId);
		Task<DepartmentCertificationSettings> SaveCertificationSettingsAsync(DepartmentCertificationSettings settings, string userId, CancellationToken cancellationToken = default);

		// ---- Evaluation (plan D1.7 / D4) ------------------------------------------------------------------------------

		/// <summary>Every current member of the role, evaluated with the D1.7 rule.</summary>
		Task<List<RoleCertificationEvaluation>> EvaluateRoleRequirementsAsync(int departmentId, int roleId, DateTime? onDate = null);
		/// <summary>One member against one role (used before a role add).</summary>
		Task<RoleCertificationEvaluation> EvaluateUserForRoleAsync(int departmentId, int roleId, string userId, DateTime? onDate = null);
		/// <summary>Members qualified for the role today (all mandatory requirements satisfied), members with no requirements included.</summary>
		Task<List<string>> GetQualifiedPersonnelForRoleAsync(int departmentId, int roleId);
		/// <summary>Members holding a valid record of every (or any) listed type code.</summary>
		Task<List<string>> GetUsersWithValidCertificationAsync(int departmentId, IEnumerable<string> typeCodes, bool allOf = true);
		/// <summary>Person × type and unit × type matrices with expiry colouring inputs (plan D9).</summary>
		Task<CertificationExpiryDashboard> GetExpiryDashboardAsync(int departmentId, DateTime? onDate = null);

		// ---- Worker (plan D5) --------------------------------------------------------------------------------------------

		/// <summary>Departments the sweep has anything to do for: typed personnel records, unit records or role requirements.</summary>
		Task<List<int>> GetDepartmentsForSweepAsync();
		/// <summary>
		/// Claims <paramref name="localToday"/> as the department's sweep day; false when that day was already claimed. The
		/// worker claims before it runs, so the expiring notifications for a local day are sent once even when the hourly
		/// tick repeats, drifts past the configured hour or overlaps.
		/// </summary>
		Task<bool> TryClaimSweepDayAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default);
		/// <summary>Returns a claim after a failed sweep so the next tick of the same local day retries it.</summary>
		Task ReleaseSweepDayAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default);
		/// <summary>Runs the expire, expiring, unit, enforcement and digest passes for one department for its local date.</summary>
		Task<CertificationSweepResult> RunExpirySweepAsync(int departmentId, DateTime localToday, CancellationToken cancellationToken = default);
	}
}

namespace Resgrid.Model
{
	/// <summary>Counts from one department's nightly sweep (plan D5).</summary>
	public sealed class CertificationSweepResult
	{
		public int DepartmentId { get; set; }
		public int Expired { get; set; }
		public int ExpiringNotified { get; set; }
		public int UnitsExpired { get; set; }
		public int UnitsExpiringNotified { get; set; }
		public int InGrace { get; set; }
		public int Removed { get; set; }
		public bool DigestSent { get; set; }
		public List<string> ExpiredNames { get; set; } = new List<string>();
		public List<string> ExpiringNames { get; set; } = new List<string>();
		public List<string> InGraceNames { get; set; } = new List<string>();
		public List<string> RemovedNames { get; set; } = new List<string>();
	}
}
