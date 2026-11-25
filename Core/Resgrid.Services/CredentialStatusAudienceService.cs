using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Messages audiences by credential status (§13a.3, LMS add-on): the active members whose record of one certification type is expired,
	/// expires within N days, or who never held it. Each record is a cycle (T1), so a member renewed into a later cycle is neither expired
	/// nor expiring. Answers only while the LMS gate is open; the compose page asks <see cref="IsAvailableAsync"/> once per request.
	/// </summary>
	public class CredentialStatusAudienceService : ICredentialStatusAudienceService
	{
		public const int MaxWithinDays = 365;

		private readonly ICertificationService _certifications;
		private readonly IDepartmentsService _departments;
		private readonly ILmsAccessService _access;

		public CredentialStatusAudienceService(ICertificationService certifications, IDepartmentsService departments, ILmsAccessService access)
		{
			_certifications = certifications;
			_departments = departments;
			_access = access;
		}

		public async Task<bool> IsAvailableAsync(int departmentId)
		{
			try
			{
				return departmentId > 0 && await _access.IsFlagEnabledAsync(departmentId) && await _access.CanAuthorAndManageAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return false;
			}
		}

		public async Task<List<string>> ResolveAsync(int departmentId, int departmentCertificationTypeId, CredentialAudienceStatuses status, int withinDays, DateTime utcNow)
		{
			var type = await _certifications.GetCertificationTypeByIdAsync(departmentCertificationTypeId);
			if (type == null || type.DepartmentId != departmentId || type.IsDeleted || type.AppliesTo != (int)CertificationAppliesTo.Person)
				return new List<string>();

			var active = await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var records = (await _certifications.GetCertificationsForDepartmentAsync(departmentId) ?? new List<PersonnelCertification>())
				.Where(r => r != null && !r.IsDeleted && r.DepartmentCertificationTypeId == departmentCertificationTypeId && r.UserId != null)
				.GroupBy(r => r.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

			var today = utcNow.Date;
			var horizon = today.AddDays(Math.Clamp(withinDays, 0, MaxWithinDays));
			return active.Where(userId => Matches(records.TryGetValue(userId, out var mine) ? mine : null, type, status, today, horizon))
				.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList();
		}

		/// <summary>One member's records of the type against the asked status (pure, so it can be tested).</summary>
		public static bool Matches(IReadOnlyCollection<PersonnelCertification> records, DepartmentCertificationType type, CredentialAudienceStatuses status, DateTime today, DateTime horizon)
		{
			var mine = (records ?? Array.Empty<PersonnelCertification>()).Where(r => r != null && !r.IsDeleted).ToList();
			if (status == CredentialAudienceStatuses.Missing)
				return mine.Count == 0;
			if (mine.Count == 0 || type.NeverExpires)
				return false;

			bool IsLive(PersonnelCertification r) => r.Status == (int)PersonnelCertificationStatuses.Active || r.Status == (int)PersonnelCertificationStatuses.PendingVerification ||
				r.Status == (int)PersonnelCertificationStatuses.Trainee;
			bool ValidOn(PersonnelCertification r, DateTime day) => IsLive(r) && (!r.ExpiresOn.HasValue || r.ExpiresOn.Value.Date >= day) && (!r.RecievedOn.HasValue || r.RecievedOn.Value.Date <= day);

			if (status == CredentialAudienceStatuses.Expired)
			{
				// Expired: no cycle is valid today, and the newest cycle ended (by status or date).
				if (mine.Any(r => ValidOn(r, today)))
					return false;
				var newest = mine.OrderByDescending(r => r.ExpiresOn ?? DateTime.MaxValue).ThenByDescending(r => r.PersonnelCertificationId).First();
				return newest.Status == (int)PersonnelCertificationStatuses.Expired || (newest.ExpiresOn.HasValue && newest.ExpiresOn.Value.Date < today);
			}

			// Expiring: a cycle valid today ends within the window and no later cycle carries the member past it.
			var valid = mine.Where(r => ValidOn(r, today)).ToList();
			if (valid.Count == 0 || valid.Any(r => !r.ExpiresOn.HasValue))
				return false;
			var lastDay = mine.Where(IsLive).Max(r => r.ExpiresOn ?? DateTime.MaxValue).Date;
			return lastDay >= today && lastDay <= horizon;
		}
	}
}
