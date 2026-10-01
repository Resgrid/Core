using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IMfaAccountCleanupService"/>
	public sealed class MfaAccountCleanupService : IMfaAccountCleanupService
	{
		private readonly IUserPasskeyRepository _passkeys;
		private readonly IUserSessionMfaEvidenceRepository _evidence;
		private readonly IAuthenticationChallengeRepository _challenges;
		private readonly IMfaApprovalRequestRepository _approvals;
		private readonly ISecurityNoticeRepository _notices;
		private readonly IFactorRecoveryTransactionRepository _recoveries;
		private readonly IMfaActivityRepository _activity;
		private readonly TimeProvider _time;

		public MfaAccountCleanupService(IUserPasskeyRepository passkeys, IUserSessionMfaEvidenceRepository evidence,
			IAuthenticationChallengeRepository challenges, IMfaApprovalRequestRepository approvals, ISecurityNoticeRepository notices,
			IFactorRecoveryTransactionRepository recoveries, IMfaActivityRepository activity, TimeProvider time)
		{
			_activity = activity;
			_passkeys = passkeys;
			_evidence = evidence;
			_challenges = challenges;
			_approvals = approvals;
			_notices = notices;
			_recoveries = recoveries;
			_time = time;
		}

		public async Task RemoveForDeletedAccountAsync(string userId, string actorUserId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return;

			var now = _time.GetUtcNow().UtcDateTime;

			// Each step stands alone: one failing never leaves the others undone.
			await StepAsync("revoke passkeys", async () =>
			{
				foreach (var passkey in await _passkeys.GetActiveForUserAsync(userId, cancellationToken))
					await _passkeys.TryRevokeAsync(passkey.UserPasskeyId, userId, PasskeyRevocationReason.AccountDeactivated, actorUserId, now, cancellationToken);
			});
			await StepAsync("retire evidence", () => _evidence.RevokeForUserAsync(userId, now, cancellationToken));
			await StepAsync("cancel challenges", () => _challenges.CancelPendingForUserAsync(userId, cancellationToken));
			await StepAsync("cancel approval requests", () => _approvals.CancelPendingForUserAsync(userId, MfaApprovalEndReason.ApproverRevoked, now, cancellationToken));
			await StepAsync("remove notices", () => _notices.DeleteForUserAsync(userId, cancellationToken));
			await StepAsync("remove recovery transactions", () => _recoveries.DeleteForUserAsync(userId, cancellationToken));
			await StepAsync("remove MFA activity", () => _activity.DeleteForUserAsync(userId, cancellationToken));
		}

		private static async Task StepAsync(string step, Func<Task> action)
		{
			try
			{
				await action();
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, $"Account deletion could not {step}.");
			}
		}
	}
}
