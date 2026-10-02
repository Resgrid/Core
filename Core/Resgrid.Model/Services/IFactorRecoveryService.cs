using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Restricted factor recovery (passkey plan sections 5.4 and 6.3). A login transaction (a verified first factor) and a
	/// recovery code open it; the secret it returns permits only status, replacement setup, completion and cancellation,
	/// and grants no ordinary or ADP access. This service owns the transaction's state; verifying the recovery code and the
	/// replacement authenticator is the caller's job.
	/// </summary>
	public interface IFactorRecoveryService
	{
		/// <summary>Recovery starts from a login transaction, so it follows that gate.</summary>
		bool IsEnabled { get; }

		/// <summary>Opens a recovery for the login transaction whose recovery code the caller has just spent; the secret is returned once.</summary>
		Task<FactorRecoveryStart> BeginAsync(MfaLoginTransaction login, CancellationToken cancellationToken = default);

		/// <summary>The pending recovery for a secret, for this client, unexpired, under its attempt limit, with the account's generation unchanged.</summary>
		Task<FactorRecoveryResult> OpenAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default);

		Task RecordFailedAttemptAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default);

		/// <summary>Completes the recovery once; the caller then commits the replacement.</summary>
		Task<bool> TryCompleteAsync(FactorRecoveryTransaction transaction, CancellationToken cancellationToken = default);

		/// <summary>Ends a pending recovery; the recovery code that opened it stays spent.</summary>
		Task<FactorRecoveryOutcome> CancelAsync(string secret, UserSessionClientApplication client, CancellationToken cancellationToken = default);
	}
}
