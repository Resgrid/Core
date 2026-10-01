using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The authoritative-context half of Protected Data Grant checking (passkey plan sections 8.2-8.3). The grant service
	/// proves a token is genuine, current and scoped; this proves it belongs to the caller presenting it. Every protected
	/// read, write, reveal and sensitive command needs both.
	///
	/// A version 1 grant is bound by user only, as before (bounded legacy reading), and is refused on a shared session. A
	/// version 2 grant must also match the validated session exactly: session id, client application, authentication
	/// generation and lock version. It must not outlive its verification by more than the department's current window. A
	/// passkey, Responder approval or provider step-up grant also names a credential whose revocation state
	/// <see cref="CheckAsync"/> checks; the synchronous <see cref="Check"/> cannot, so it refuses them.
	/// </summary>
	public static class ProtectedGrantBinding
	{
		/// <summary>
		/// The full binding, including the revocation state of the credential behind a passkey, approval or provider step-up
		/// grant (plan section 8.2). Without a credential-state service those grants fail closed.
		/// </summary>
		public static async Task<ProtectedGrantBindingOutcome> CheckAsync(ProtectedDataGrant grant, string callerUserId,
			ProtectedGrantSessionContext callerSession, int? policyWindowMinutes, IMfaCredentialStateService credentialStates,
			CancellationToken cancellationToken = default)
		{
			var outcome = CheckContext(grant, callerUserId, callerSession, policyWindowMinutes);
			if (outcome != ProtectedGrantBindingOutcome.Bound || !NamesCredential(grant))
				return outcome;

			if (credentialStates == null)
				return ProtectedGrantBindingOutcome.MethodUnverifiable;

			try
			{
				return await credentialStates.IsCurrentAsync(grant, cancellationToken)
					? ProtectedGrantBindingOutcome.Bound
					: ProtectedGrantBindingOutcome.MethodUnverifiable;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// A revocation that cannot be checked is not proof that none happened.
				Framework.Logging.LogException(ex, "Protected Data Grant credential state could not be read; the grant was refused.");
				return ProtectedGrantBindingOutcome.MethodUnverifiable;
			}
		}

		/// <summary>
		/// The binding without credential state: TOTP and exempt grants are bound; a passkey, approval or provider step-up
		/// grant is refused, because this overload cannot check its credential's revocation. Use <see cref="CheckAsync"/>.
		/// </summary>
		public static ProtectedGrantBindingOutcome Check(ProtectedDataGrant grant, string callerUserId,
			ProtectedGrantSessionContext callerSession, int? policyWindowMinutes = null)
		{
			var outcome = CheckContext(grant, callerUserId, callerSession, policyWindowMinutes);
			return outcome == ProtectedGrantBindingOutcome.Bound && NamesCredential(grant) ? ProtectedGrantBindingOutcome.MethodUnverifiable : outcome;
		}

		/// <summary>A version 2 grant whose second factor was a credential with its own revocation state.</summary>
		private static bool NamesCredential(ProtectedDataGrant grant) =>
			grant.Version >= 2 && grant.MfaMethod != ProtectedDataGrantMfaMethods.Totp && grant.MfaMethod != ProtectedDataGrantMfaMethods.None;

		private static ProtectedGrantBindingOutcome CheckContext(ProtectedDataGrant grant, string callerUserId,
			ProtectedGrantSessionContext callerSession, int? policyWindowMinutes)
		{
			if (grant == null || string.IsNullOrWhiteSpace(callerUserId) ||
				!string.Equals(grant.UserId, callerUserId, StringComparison.OrdinalIgnoreCase))
				return ProtectedGrantBindingOutcome.UserMismatch;

			// A shared session's grants must prove they were issued since its last lock (plan section 12.5.3). A version 1
			// grant carries no session or lock version, so it cannot, and a shared caller refuses it.
			if (grant.Version < 2)
				return callerSession?.SessionLockVersion != null ? ProtectedGrantBindingOutcome.SessionLocked : ProtectedGrantBindingOutcome.Bound;

			if (callerSession == null || string.IsNullOrWhiteSpace(callerSession.SessionId) ||
				!string.Equals(grant.SessionId, callerSession.SessionId, StringComparison.Ordinal))
				return ProtectedGrantBindingOutcome.SessionMismatch;

			if (grant.ClientApp != callerSession.ClientApplication)
				return ProtectedGrantBindingOutcome.ClientMismatch;

			if (grant.AuthenticationGeneration != callerSession.AuthenticationGeneration)
				return ProtectedGrantBindingOutcome.GenerationMismatch;

			if (grant.SessionLockVersion != callerSession.SessionLockVersion)
				return ProtectedGrantBindingOutcome.SessionLocked;

			// TOTP evidence is revoked by advancing the authentication generation (checked above), and an exempt grant has
			// no evidence. A grant naming a credential is checked for that credential's revocation by the caller.
			if (!ProtectedDataGrantMfaMethods.IsKnown(grant.MfaMethod))
				return ProtectedGrantBindingOutcome.MethodUnverifiable;

			if (policyWindowMinutes.HasValue)
			{
				var anchor = grant.StepUpExempt ? grant.IssuedAtUtc : grant.MfaAtUtc;
				var skew = TimeSpan.FromSeconds(Math.Max(0, Config.DataProtectionConfig.GrantClockSkewSeconds));
				if (grant.ExpiresOnUtc > anchor.AddMinutes(EffectiveWindowMinutes(policyWindowMinutes.Value)).Add(skew))
					return ProtectedGrantBindingOutcome.WindowExceeded;
			}

			return ProtectedGrantBindingOutcome.Bound;
		}

		/// <summary>The department step-up window as issuers apply it: the policy value or the default, clamped to 1..max.</summary>
		public static int EffectiveWindowMinutes(int policyWindowMinutes)
		{
			var window = policyWindowMinutes > 0 ? policyWindowMinutes : Config.DataProtectionConfig.StepUpWindowDefaultMinutes;
			return Math.Min(Math.Max(1, window), Math.Max(1, Config.DataProtectionConfig.StepUpMaximumMinutes));
		}

		/// <summary>
		/// The validated session of the caller, when the grant context describes the same attended user; otherwise null,
		/// which makes every version 2 grant fail closed.
		/// </summary>
		public static ProtectedGrantSessionContext SessionFor(IProtectedGrantContext context, string userId)
		{
			if (context == null || context.IsWorkloadCaller || string.IsNullOrWhiteSpace(userId) ||
				!string.Equals(context.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return null;

			return context.Session;
		}

		/// <summary>The value-free error code for a binding failure (workbook section 7.5); null when bound.</summary>
		public static string ErrorCode(ProtectedGrantBindingOutcome outcome) => outcome switch
		{
			ProtectedGrantBindingOutcome.Bound => null,
			ProtectedGrantBindingOutcome.UserMismatch => "protected_access_denied",
			ProtectedGrantBindingOutcome.SessionMismatch => "grant_session_mismatch",
			ProtectedGrantBindingOutcome.ClientMismatch => "grant_client_mismatch",
			ProtectedGrantBindingOutcome.SessionLocked => "grant_session_locked",
			_ => "grant_revoked"
		};

		/// <summary>The value-free error code for a validation failure, shared by the application-tier readers.</summary>
		public static string ErrorCode(ProtectedDataGrantValidationOutcome outcome) => outcome switch
		{
			ProtectedDataGrantValidationOutcome.Valid => null,
			ProtectedDataGrantValidationOutcome.Expired => "grant_expired",
			ProtectedDataGrantValidationOutcome.EpochRevoked => "grant_revoked",
			ProtectedDataGrantValidationOutcome.VersionUnsupported => "grant_version_unsupported",
			_ => "step_up_required"
		};
	}
}
