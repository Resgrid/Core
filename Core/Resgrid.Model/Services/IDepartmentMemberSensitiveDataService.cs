using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Department-scoped sensitive personnel attributes (ADP plan section 5.1). These live here
	/// rather than on <see cref="UserProfile"/> for a structural reason: a profile row is GLOBAL to
	/// the user and shared across every department they belong to, so it cannot be encrypted with
	/// any one department's key. This table is keyed (DepartmentId, UserId), which is what makes
	/// per-department protection possible at all.
	/// </summary>
	public interface IDepartmentMemberSensitiveDataService
	{
		/// <summary>The row for one member of one department, or null when none has been created.</summary>
		Task<DepartmentMemberSensitiveData> GetByDepartmentAndUserAsync(int departmentId, string userId);

		/// <summary>
		/// Creates or updates the member's row. The cataloged columns are enveloped by the write
		/// safety net before the row is persisted, so callers pass plaintext and never deal with
		/// envelopes themselves.
		/// </summary>
		/// <summary>
		/// Removes a member's department-scoped row outright. Used when an account is deleted: this
		/// row is now the ONLY copy of their identification number and address for this department,
		/// so leaving it behind would retain personal data the deletion is supposed to remove.
		/// </summary>
		Task<bool> DeleteForMemberAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default);

		Task<DepartmentMemberSensitiveData> SaveAsync(DepartmentMemberSensitiveData data,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Stamps each profile's <see cref="UserProfile.IdentificationNumber"/> with the value this
		/// department holds for that member, resolved through the protected-read pipeline. The
		/// number is department-issued, so the profile's own (global, legacy) column is never the
		/// answer once a department row exists — a member with no row for this department simply has
		/// no number here. One query and one resolve for the whole list.
		/// </summary>
		/// <summary>
		/// Every member's department-scoped row for one department, keyed by user id and already put
		/// through the protected read pipeline — so a protected department hands back the REDACTED
		/// placeholder wherever the caller has no grant, never ciphertext.
		/// </summary>
		Task<IReadOnlyDictionary<string, DepartmentMemberSensitiveData>> GetResolvedForDepartmentAsync(
			int departmentId, string grantToken, string actingUserId);

		Task ApplyIdentificationNumbersAsync(int departmentId, IEnumerable<UserProfile> profiles,
			string grantToken, string actingUserId);
	}
}
