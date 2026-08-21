using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	public interface IPasswordRecoveryService
	{
		Task<PasswordRecoveryIssueResult> IssueAsync(string userId, string email, string ipAddress,
			long authenticationGeneration, string securityStamp,
			CancellationToken cancellationToken = default);
		/// <summary>
		/// Resolves a recovery token. Always returns a result; a missing, unreadable, or expired token comes
		/// back as <see cref="PasswordRecoveryLookupResult.Found"/> = false rather than a null request.
		/// </summary>
		Task<PasswordRecoveryLookupResult> GetAsync(string token, CancellationToken cancellationToken = default);
		Task<bool> TryConsumeAsync(string token, CancellationToken cancellationToken = default);
		Task RemoveAsync(string token, CancellationToken cancellationToken = default);
	}
}
