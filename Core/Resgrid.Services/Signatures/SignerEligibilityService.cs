using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Services;

namespace Resgrid.Services.Signatures
{
	/// <summary>
	/// The evaluator eligibility resolver (S3 evaluator mode, plan §13 T4 item 2): an active member qualifies by a valid
	/// certification of a listed type — valid as the department's own certification rules say (status, expiry, pending
	/// verification) — or by membership of a listed personnel role. Deleted, disabled and hidden members never qualify.
	/// </summary>
	public class SignerEligibilityService : ISignerEligibilityService
	{
		private readonly ICertificationService _certifications;
		private readonly IPersonnelRolesService _roles;
		private readonly IDepartmentsService _departments;

		public SignerEligibilityService(ICertificationService certifications, IPersonnelRolesService roles, IDepartmentsService departments)
		{
			_certifications = certifications;
			_roles = roles;
			_departments = departments;
		}

		public async Task<bool> IsEligibleAsync(int departmentId, string userId, SignerEligibility eligibility)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId) || eligibility == null || eligibility.IsEmpty)
				return false;

			// S4: an excluded member (an approval's submitter) never qualifies, whatever else the rule names.
			if (eligibility.Excludes(userId))
				return false;

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>();
			if (!active.Any(id => string.Equals(id, userId, StringComparison.OrdinalIgnoreCase)))
				return false;

			// S4 approval chains: named approvers (and their delegates or escalation targets), and the department's active admins.
			if (eligibility.Names(userId))
				return true;
			if (eligibility.DepartmentAdmins)
			{
				var admins = await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>();
				if (admins.Any(a => a != null && string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)))
					return true;
			}

			var roleIds = (eligibility.PersonnelRoleIds ?? new List<int>()).Where(x => x > 0).ToHashSet();
			if (roleIds.Count > 0)
			{
				var roles = await _roles.GetRolesForUserAsync(userId, departmentId) ?? new List<PersonnelRole>();
				if (roles.Any(r => r != null && r.DepartmentId == departmentId && roleIds.Contains(r.PersonnelRoleId)))
					return true;
			}

			var typeIds = (eligibility.CertificationTypeIds ?? new List<int>()).Where(x => x > 0).ToHashSet();
			if (typeIds.Count == 0)
				return false;

			var records = (await _certifications.GetCertificationsByUserIdAsync(userId) ?? new List<PersonnelCertification>())
				.Where(c => c != null && c.DepartmentId == departmentId && c.DepartmentCertificationTypeId.HasValue && typeIds.Contains(c.DepartmentCertificationTypeId.Value)).ToList();
			if (records.Count == 0)
				return false;

			var settings = await _certifications.GetCertificationSettingsAsync(departmentId);
			var today = DateTime.UtcNow;
			foreach (var record in records)
			{
				var type = await _certifications.GetCertificationTypeByIdAsync(record.DepartmentCertificationTypeId.Value);
				if (type == null || type.DepartmentId != departmentId)
					continue;
				if (CertificationRequirementEvaluator.IsRecordValid(record, type, today, false, settings?.TreatPendingVerificationAsValid ?? false))
					return true;
			}

			return false;
		}

		public async Task<List<string>> GetEligibleUserIdsAsync(int departmentId, SignerEligibility eligibility)
		{
			var eligible = new List<string>();
			if (departmentId <= 0 || eligibility == null || eligibility.IsEmpty)
				return eligible;

			foreach (var userId in (await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>()).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
				if (await IsEligibleAsync(departmentId, userId, eligibility))
					eligible.Add(userId);
			return eligible;
		}
	}
}
