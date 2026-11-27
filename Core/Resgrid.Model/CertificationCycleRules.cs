using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>
	/// Certification cycles (Training T1/T3): each personnel certification record of a type is one cycle, so a member renewed into a
	/// new record holds an earlier and a later cycle side by side. Baseline rule shared by the nightly expiry sweep and the LMS cycle
	/// sweep: an earlier cycle that a later live cycle replaces is not "expiring" or "expired" news.
	/// </summary>
	public static class CertificationCycleRules
	{
		/// <summary>
		/// True when the same member holds another live record of the same type that starts after <paramref name="record"/> started
		/// and runs past its expiry (or never expires): the member has renewed into a later cycle.
		/// </summary>
		public static bool IsSupersededByLaterCycle(PersonnelCertification record, IEnumerable<PersonnelCertification> sameMemberRecords)
		{
			if (record == null || !record.DepartmentCertificationTypeId.HasValue || !record.ExpiresOn.HasValue)
				return false;

			return (sameMemberRecords ?? Enumerable.Empty<PersonnelCertification>()).Any(other =>
				other != null && other.PersonnelCertificationId != record.PersonnelCertificationId && !other.IsDeleted &&
				other.DepartmentCertificationTypeId == record.DepartmentCertificationTypeId &&
				string.Equals(other.UserId, record.UserId, StringComparison.OrdinalIgnoreCase) &&
				(other.Status == (int)PersonnelCertificationStatuses.Active || other.Status == (int)PersonnelCertificationStatuses.PendingVerification ||
				 other.Status == (int)PersonnelCertificationStatuses.Trainee) &&
				(other.RecievedOn ?? DateTime.MinValue).Date > (record.RecievedOn ?? DateTime.MinValue).Date &&
				(!other.ExpiresOn.HasValue || other.ExpiresOn.Value.Date > record.ExpiresOn.Value.Date));
		}
	}
}
