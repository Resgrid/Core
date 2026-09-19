using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Role → certification-type requirement rows (Workforce &amp; Business Operations plan, Phase D3; M0213).</summary>
	public interface IPersonnelRoleCertificationRequirementRepository : IRepository<PersonnelRoleCertificationRequirement>
	{
		Task<IEnumerable<PersonnelRoleCertificationRequirement>> GetByRoleIdAsync(int personnelRoleId);
		Task<IEnumerable<PersonnelRoleCertificationRequirement>> GetAllForDepartmentAsync(int departmentId);
		Task<int> CountByTypeIdAsync(int departmentCertificationTypeId);
		Task<int> DeleteByRoleIdAsync(int personnelRoleId, CancellationToken cancellationToken = default);
		/// <summary>Departments with at least one requirement row (worker 34 scope).</summary>
		Task<IEnumerable<int>> GetDepartmentIdsAsync();
	}

	/// <summary>One settings row per department, keyed by DepartmentId (M0213). Save is an upsert.</summary>
	public interface IDepartmentCertificationSettingsRepository
	{
		Task<DepartmentCertificationSettings> GetAsync(int departmentId);
		Task<DepartmentCertificationSettings> SaveAsync(DepartmentCertificationSettings settings, CancellationToken cancellationToken = default);
	}

	/// <summary>Continuing-education credit entries (M0214).</summary>
	public interface IPersonnelCertificationCreditsRepository : IRepository<PersonnelCertificationCredit>
	{
		/// <summary>Credits for one record, newest first, without the file bytes.</summary>
		Task<IEnumerable<PersonnelCertificationCredit>> GetByCertificationIdAsync(int personnelCertificationId);
		Task<PersonnelCertificationCredit> GetByIdWithDataAsync(int personnelCertificationCreditId);
		/// <summary>Sum of hours per certification id for the ids given.</summary>
		Task<IReadOnlyDictionary<int, decimal>> GetHourTotalsAsync(IEnumerable<int> personnelCertificationIds);
		Task<int> DeleteByCertificationIdAsync(int personnelCertificationId, CancellationToken cancellationToken = default);
	}

	/// <summary>Unit-scoped certification records (plan D1.8; M0213). Reads exclude the file bytes unless asked.</summary>
	public interface IUnitCertificationRepository : IRepository<UnitCertification>
	{
		Task<IEnumerable<UnitCertification>> GetByUnitIdAsync(int unitId);
		Task<IEnumerable<UnitCertification>> GetForDepartmentAsync(int departmentId);
		Task<UnitCertification> GetByIdWithDataAsync(int unitCertificationId);
		/// <summary>Live, non-deleted records with an expiry on or before the date (department-wide), no bytes.</summary>
		Task<IEnumerable<UnitCertification>> GetExpiringAsync(int departmentId, DateTime onOrBefore);
		Task<int> CountByTypeIdAsync(int departmentCertificationTypeId);
		/// <summary>Departments holding at least one non-deleted unit record (worker 34 scope).</summary>
		Task<IEnumerable<int>> GetDepartmentIdsAsync();
	}
}
