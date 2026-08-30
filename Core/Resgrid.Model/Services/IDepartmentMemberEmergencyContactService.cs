using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// A member's emergency contacts, scoped to a department (ADP plan section 5.1). A member may
	/// have several per department, and the values may legitimately differ between departments.
	/// Cataloged columns are enveloped by the write safety net, so callers pass and receive
	/// plaintext (or the REDACTED placeholder when no grant is held) and never handle envelopes.
	/// </summary>
	public interface IDepartmentMemberEmergencyContactService
	{
		/// <summary>The member's contacts for one department, primary first. Never null.</summary>
		Task<List<DepartmentMemberEmergencyContact>> GetAllForMemberAsync(int departmentId, string userId);

		/// <summary>Creates or updates one contact.</summary>
		Task<DepartmentMemberEmergencyContact> SaveAsync(DepartmentMemberEmergencyContact contact,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Soft-deletes one contact. Scoped by department and user so a caller cannot remove another
		/// member's row by id alone.
		/// </summary>
		/// <summary>
		/// Removes every emergency contact a member holds in one department. Used when an account is
		/// deleted — these rows carry the contact's name, phone and email, which is third-party
		/// personal data that must not outlive the member's account.
		/// </summary>
		Task<int> DeleteAllForMemberAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default);

		Task<bool> DeleteAsync(int departmentMemberEmergencyContactId, int departmentId, string userId,
			string deletingUserId, CancellationToken cancellationToken = default);
	}
}
