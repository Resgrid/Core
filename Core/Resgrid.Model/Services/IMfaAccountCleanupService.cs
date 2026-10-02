using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// What account deletion does to sign-in factors (passkey plan section 6.4): passkeys are revoked (their rows stay as
	/// tombstones, so a credential can never be registered again), evidence is retired, pending challenges and approval
	/// requests end, and queued notices and recovery transactions are removed. The authenticator key, recovery codes and
	/// TOTP state are removed with the identity itself.
	/// </summary>
	public interface IMfaAccountCleanupService
	{
		Task RemoveForDeletedAccountAsync(string userId, string actorUserId, CancellationToken cancellationToken = default);
	}
}
