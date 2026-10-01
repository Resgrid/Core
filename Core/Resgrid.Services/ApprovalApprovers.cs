using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;

namespace Resgrid.Services
{
	/// <summary>
	/// Who may approve, and whether a past approval still counts (passkey plan section 7.9). An approver is a personal
	/// Responder session of the same user under the current generation (never a shared one), with an active Responder
	/// passkey that has approval on. An approval stops counting as soon as its passkey is revoked or has approval turned
	/// off, or its Responder session ends, however that happened, because every read checks both.
	/// </summary>
	public static class ApprovalApprovers
	{
		public static bool IsEligibleSession(UserSession session, string userId, long authenticationGeneration, DateTime utcNow) =>
			session != null && string.Equals(session.UserId, userId, StringComparison.OrdinalIgnoreCase) &&
			session.ClientApplication == (int)UserSessionClientApplication.Responder && !session.SharedMode && session.ApprovalsDisabledOnUtc == null &&
			session.State == (int)UserSessionState.Active && session.RevokedOn == null && session.ExpiresOn > utcNow &&
			session.AuthenticationGeneration == authenticationGeneration;

		public static bool IsApprovingPasskey(UserPasskey passkey, string userId) =>
			passkey != null && passkey.IsActive && passkey.ApprovalEnabled &&
			passkey.ClientApplication == (int)UserSessionClientApplication.Responder &&
			string.Equals(passkey.UserId, userId, StringComparison.OrdinalIgnoreCase);

		/// <summary>Whether the passkey and Responder session an approval's factor reference names still count.</summary>
		public static async Task<bool> IsValidAsync(IUserPasskeyRepository passkeys, IUserSessionsRepository sessions, string userId,
			string factorReference, long authenticationGeneration, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!MfaApprovalRequest.TryParseFactorReference(factorReference, out var passkeyId, out var sessionId))
				return false;

			if (!IsApprovingPasskey(await passkeys.GetAsync(passkeyId, cancellationToken), userId))
				return false;

			return IsEligibleSession(await sessions.GetByIdAsync(sessionId), userId, authenticationGeneration, utcNow);
		}
	}
}
