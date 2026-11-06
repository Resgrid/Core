using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IPasswordRecoveryService
	{
		/// <summary>
		/// Issues a link for a signed-out forgot-password request (SessionSecurityConfig.PublicResetLinkLifetimeMinutes). Counts
		/// against both the requesting address's and the email's hourly limits, for unknown accounts (null userId) too.
		/// </summary>
		Task<PasswordRecoveryIssueResult> IssueAsync(string userId, string email, string ipAddress,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default);
		/// <summary>
		/// Issues a link a department administrator sends a member (SessionSecurityConfig.AdministratorResetLinkLifetimeMinutes).
		/// Counts against the member's hourly limit only: the administrator is signed in, authorized and MFA-verified, so their
		/// sends neither use up nor are refused by the signed-out request limit of the network they work from.
		/// </summary>
		Task<PasswordRecoveryIssueResult> IssueForAdministratorAsync(string userId, string email,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default);
		/// <summary>
		/// Resolves a recovery token. Always returns a result; a missing, unreadable, or expired token comes
		/// back as <see cref="PasswordRecoveryLookupResult.Found"/> = false rather than a null request.
		/// </summary>
		Task<PasswordRecoveryLookupResult> GetAsync(string token, CancellationToken cancellationToken = default);
		Task<bool> TryConsumeAsync(string token, CancellationToken cancellationToken = default);
		/// <summary>
		/// Clears the single-use marker set by <see cref="TryConsumeAsync"/> so the grant can be presented
		/// again within its original lifetime. Used when the work the grant authorized did not complete.
		/// </summary>
		Task ReleaseAsync(string token, CancellationToken cancellationToken = default);
		Task RemoveAsync(string token, CancellationToken cancellationToken = default);
	}
}
