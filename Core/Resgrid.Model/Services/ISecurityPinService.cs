using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Manages the per-user 4-digit security PIN used as a step-up (2FA-style) check for
	/// dangerous/department-wide chatbot and SMS actions. PINs are stored AES-encrypted on the
	/// user profile. A user can opt in personally (UserProfile.SecurityPinEnabled) or a
	/// department can force PIN usage for all members (DepartmentSettingTypes.ForceChatbotSecurityPin).
	/// </summary>
	public interface ISecurityPinService
	{
		/// <summary>
		/// True when the user must supply their security PIN for dangerous actions: either the
		/// department forces PIN usage or the user opted in on their profile.
		/// </summary>
		Task<bool> IsPinRequiredAsync(string userId, int departmentId);

		/// <summary>True when the user has a security PIN set on their profile.</summary>
		Task<bool> HasPinAsync(string userId);

		/// <summary>Validates the supplied PIN against the user's stored (encrypted) PIN.</summary>
		Task<bool> ValidatePinAsync(string userId, string pin);

		/// <summary>Gets the user's decrypted PIN (for own-profile display), or null when none is set.</summary>
		Task<string> GetPinAsync(string userId);

		/// <summary>
		/// Generates and saves a random PIN for the user when they don't have one yet.
		/// Returns the profile (with a PIN guaranteed set) or null when no profile exists.
		/// </summary>
		Task<UserProfile> EnsurePinAsync(string userId, int departmentId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Generates a random PIN for every member of the department that doesn't have one yet.
		/// Used when a department enables the ForceChatbotSecurityPin setting.
		/// </summary>
		Task EnsurePinsForDepartmentAsync(int departmentId, CancellationToken cancellationToken = default);
	}
}
