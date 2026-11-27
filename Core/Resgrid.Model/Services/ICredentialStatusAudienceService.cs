using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model
{
	/// <summary>A credential-status message audience (§13a.3, LMS): members by their record of one certification type. Persisted nowhere.</summary>
	public enum CredentialAudienceStatuses
	{
		/// <summary>The member's newest record of the type has expired and no live record replaces it.</summary>
		Expired = 0,
		/// <summary>A live record expires within the window and no later cycle replaces it.</summary>
		ExpiringWithin = 1,
		/// <summary>The member has never held the type.</summary>
		Missing = 2
	}
}

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Messages → Compose audience "members whose certification X is expired / expiring within N days / missing" (T3, LMS add-on). Only
	/// active members are ever returned. A baseline screen (Messages) asks it, so it carries no LMS course type; it answers only while
	/// the LMS gate is open (<see cref="IsAvailableAsync"/>), and the compose page asks that once per request.
	/// </summary>
	public interface ICredentialStatusAudienceService
	{
		/// <summary>The LMS flag and add-on are on for the department (one billing read).</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>The active members matching the status for one person-scoped certification type, as of <paramref name="utcNow"/>.</summary>
		Task<List<string>> ResolveAsync(int departmentId, int departmentCertificationTypeId, CredentialAudienceStatuses status, int withinDays, DateTime utcNow);
	}
}
