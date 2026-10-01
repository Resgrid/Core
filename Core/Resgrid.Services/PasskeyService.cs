using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IPasskeyService"/>
	public sealed class PasskeyService : IPasskeyService
	{
		/// <summary>An attestation or assertion response is a few kilobytes; anything far larger is refused unread.</summary>
		internal const int MaxCredentialJsonLength = 64 * 1024;

		private readonly IUserPasskeyRepository _passkeys;
		private readonly IPasskeyProvider _provider;
		private readonly IRelyingPartyRegistry _registry;
		private readonly IPasskeyFeatureGates _gates;
		private readonly IAuthenticationChallengeService _challenges;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _policy;
		private readonly IUserSessionsRepository _sessions;
		private readonly IUserSessionService _userSessions;
		private readonly ISystemAuditsService _audits;
		private readonly IMfaApprovalRequestRepository _approvals;
		private readonly ISecurityNoticeService _notices;
		private readonly TimeProvider _time;

		public PasskeyService(IUserPasskeyRepository passkeys, IPasskeyProvider provider, IRelyingPartyRegistry registry, IPasskeyFeatureGates gates,
			IAuthenticationChallengeService challenges, IMfaEvidenceService evidence, IMfaPolicyService policy, IUserSessionsRepository sessions,
			IUserSessionService userSessions, ISystemAuditsService audits, IMfaApprovalRequestRepository approvals, ISecurityNoticeService notices,
			TimeProvider time)
		{
			_notices = notices;
			_approvals = approvals;
			_passkeys = passkeys;
			_provider = provider;
			_registry = registry;
			_gates = gates;
			_challenges = challenges;
			_evidence = evidence;
			_policy = policy;
			_sessions = sessions;
			_userSessions = userSessions;
			_audits = audits;
			_time = time;
		}

		private static TimeSpan FirstFactorWindow => TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.FirstFactorReauthWindowMinutes));
		private static TimeSpan SecondFactorWindow => TimeSpan.FromMinutes(Math.Max(1, TwoFactorConfig.SensitiveOperationWindowMinutes));
		private static int MaxPerClient => Math.Max(1, PasskeyConfig.MaxActiveCredentialsPerClient);

		// ── Registration (plan section 6.1) ───────────────────────────────────────────

		public bool IsRegistrationAvailable(UserSessionClientApplication client) =>
			_gates.RegistrationEnabled && _registry.Get(client) != null && _provider.IsAvailableFor(client);

		public async Task<PasskeyCeremonyStart> BeginRegistrationAsync(PasskeyCaller caller, bool totpEnrolled, int recoveryCodesRemaining,
			CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyCeremonyStart.Of(PasskeyOutcome.SessionRequired);
			if (!IsRegistrationAvailable(caller.ClientApplication))
				return PasskeyCeremonyStart.Of(PasskeyOutcome.Unavailable);

			// The first release requires TOTP and usable recovery before a passkey, so a passkey is never the only factor.
			if (!totpEnrolled || recoveryCodesRemaining <= 0)
				return PasskeyCeremonyStart.Of(PasskeyOutcome.EnrollmentRequired);

			try
			{
				var readiness = await CheckEnrollmentEvidenceAsync(caller, _time.GetUtcNow().UtcDateTime, cancellationToken);
				if (readiness != PasskeyOutcome.Succeeded)
					return PasskeyCeremonyStart.Of(readiness);

				var party = _registry.Get(caller.ClientApplication);
				var mine = BoundTo(await _passkeys.GetActiveForUserAsync(caller.UserId, cancellationToken), caller.ClientApplication, party.RpId);
				if (mine.Count >= MaxPerClient)
					return PasskeyCeremonyStart.Of(PasskeyOutcome.LimitReached);

				// One stable, random handle per account and RP; never the user id, name or e-mail (plan section 5.1).
				var userHandle = await _passkeys.GetUserHandleAsync(caller.UserId, party.RpId, cancellationToken) ?? RandomNumberGenerator.GetBytes(32);
				var accountName = string.IsNullOrWhiteSpace(caller.UserName) ? caller.UserId : caller.UserName;
				var options = _provider.CreateRegistrationOptions(caller.ClientApplication, userHandle, accountName, accountName,
					mine.Select(p => p.CredentialId).ToList(), preferRoaming: caller.SharedMode);

				var challenge = await _challenges.CreateAsync(Binding(caller, AuthenticationChallengePurpose.PasskeyRegistration, null),
					party.RpId, options, cancellationToken);
				return challenge == null
					? PasskeyCeremonyStart.Of(PasskeyOutcome.TooManyRequests)
					: new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = challenge.AuthenticationChallengeId, OptionsJson = options };
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Passkey registration could not be started.");
				return PasskeyCeremonyStart.Of(PasskeyOutcome.ServiceUnavailable);
			}
		}

		public async Task<PasskeyRegistrationResult> CompleteRegistrationAsync(PasskeyCaller caller, string requestId, string credentialJson,
			string displayName, CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyRegistrationResult.Of(PasskeyOutcome.SessionRequired);
			if (!IsRegistrationAvailable(caller.ClientApplication))
				return PasskeyRegistrationResult.Of(PasskeyOutcome.Unavailable);
			if (!IsCeremonyInput(requestId, credentialJson))
				return PasskeyRegistrationResult.Of(PasskeyOutcome.InvalidRequest);

			var name = NormalizeDisplayName(displayName);
			if (name != null && name.Length > MaxDisplayNameLength)
				return PasskeyRegistrationResult.Of(PasskeyOutcome.InvalidRequest);

			try
			{
				var lookup = await _challenges.GetForCompletionAsync(requestId,
					Binding(caller, AuthenticationChallengePurpose.PasskeyRegistration, null), cancellationToken);
				if (!lookup.IsUsable)
					return PasskeyRegistrationResult.Of(Map(lookup.Outcome));

				var challenge = lookup.Challenge;
				var party = _registry.Get(caller.ClientApplication);
				if (!string.Equals(challenge.RpId, party.RpId, StringComparison.Ordinal))
					return PasskeyRegistrationResult.Of(PasskeyOutcome.ChallengeExpired);

				// The evidence that allowed the ceremony to start must still be current (same generation, not revoked).
				var readiness = await CheckEnrollmentEvidenceAsync(caller, challenge.CreatedOnUtc, cancellationToken);
				if (readiness != PasskeyOutcome.Succeeded)
					return PasskeyRegistrationResult.Of(readiness);

				var verification = await _provider.VerifyRegistrationAsync(caller.ClientApplication, challenge.OptionsJson, credentialJson, cancellationToken);
				if (!verification.Succeeded)
				{
					await _challenges.RecordFailedAttemptAsync(challenge.AuthenticationChallengeId, cancellationToken);
					return PasskeyRegistrationResult.Of(PasskeyOutcome.VerificationFailed);
				}

				// Spend the request before the credential exists, so one ceremony can never register twice.
				if (!await _challenges.TryConsumeAsync(challenge.AuthenticationChallengeId, cancellationToken))
					return PasskeyRegistrationResult.Of(PasskeyOutcome.ChallengeConsumed);

				if (BoundTo(await _passkeys.GetActiveForUserAsync(caller.UserId, cancellationToken), caller.ClientApplication, party.RpId).Count >= MaxPerClient)
					return PasskeyRegistrationResult.Of(PasskeyOutcome.LimitReached);

				var now = _time.GetUtcNow().UtcDateTime;
				var session = await TryGetSessionAsync(caller.SessionId);
				var passkey = new UserPasskey
				{
					UserPasskeyId = Guid.NewGuid().ToString(),
					UserId = caller.UserId,
					ClientApplication = (int)caller.ClientApplication,
					RpId = party.RpId,
					CredentialId = verification.CredentialId,
					CredentialIdHash = SHA256.HashData(verification.CredentialId),
					PublicKey = verification.PublicKey,
					Algorithm = verification.Algorithm,
					UserHandle = verification.UserHandle,
					SignCount = verification.SignCount,
					IsBackupEligible = verification.IsBackupEligible,
					IsBackedUp = verification.IsBackedUp,
					Transports = Truncate(string.Join(",", verification.Transports ?? Array.Empty<string>()), 128),
					Aaguid = verification.Aaguid == Guid.Empty ? null : verification.Aaguid.ToString(),
					AttestationFormat = Truncate(verification.AttestationFormat, 32),
					DisplayName = name ?? DefaultDisplayName(caller.ClientApplication, now),
					CreatedOnUtc = now,
					RegistrationPlatform = Truncate(session?.OperatingSystem ?? session?.DeviceType, 128),
					RegistrationInstallation = Truncate(session?.DeviceName, 256),
					RegistrationUserAgentFamily = Truncate(session?.Browser, 128),
					RegistrationAttachment = verification.Attachment,
					RegisteredInSharedMode = caller.SharedMode,
					StateVersion = 1
				};

				// The (RP, credential id) key is unique across users, so a credential already registered anywhere is refused.
				if (!await _passkeys.TryInsertAsync(passkey, cancellationToken))
					return PasskeyRegistrationResult.Of(PasskeyOutcome.VerificationFailed);

				await AuditAsync(caller, SystemAuditTypes.PasskeyRegistered,
					$"Passkey {passkey.UserPasskeyId} registered for {ClientLabel(caller.ClientApplication)}.", cancellationToken);
				await NoticeAsync(caller, SecurityNoticeKind.PasskeyRegistered, cancellationToken);
				// A passkey created on a shared installation may sit in its common profile (plan sections 6.5 and 12.5.2).
				if (caller.SharedMode)
					await NoticeAsync(caller, SecurityNoticeKind.SharedInstallationFactor, cancellationToken);
				return new PasskeyRegistrationResult { Outcome = PasskeyOutcome.Succeeded, Passkey = passkey };
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Passkey registration could not be completed.");
				return PasskeyRegistrationResult.Of(PasskeyOutcome.ServiceUnavailable);
			}
		}

		// ── Inventory and revocation (plan sections 6.1 and 6.5) ──────────────────────

		public Task<IReadOnlyList<UserPasskey>> GetActiveForUserAsync(string userId, CancellationToken cancellationToken = default) =>
			_passkeys.GetActiveForUserAsync(userId, cancellationToken);

		public async Task<bool> HasActiveForClientAsync(string userId, UserSessionClientApplication client, CancellationToken cancellationToken = default)
		{
			var party = _registry.Get(client);
			return party != null && !string.IsNullOrWhiteSpace(userId) &&
				BoundTo(await _passkeys.GetActiveForUserAsync(userId, cancellationToken), client, party.RpId).Count > 0;
		}

		public Task<int> CountActiveForUserAsync(string userId, CancellationToken cancellationToken = default) =>
			_passkeys.CountActiveForUserAsync(userId, cancellationToken);

		public async Task<PasskeyOutcome> RenameAsync(PasskeyCaller caller, string userPasskeyId, string displayName,
			CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyOutcome.SessionRequired;

			var name = NormalizeDisplayName(displayName);
			if (string.IsNullOrEmpty(name) || name.Length > MaxDisplayNameLength || string.IsNullOrWhiteSpace(userPasskeyId))
				return PasskeyOutcome.InvalidRequest;

			if (!await _passkeys.TryRenameAsync(userPasskeyId, caller.UserId, name, cancellationToken))
				return PasskeyOutcome.NotFound;

			await AuditAsync(caller, SystemAuditTypes.PasskeyRenamed, $"Passkey {userPasskeyId} renamed.", cancellationToken);
			return PasskeyOutcome.Succeeded;
		}

		public async Task<PasskeyRevocationResult> RevokeAsync(PasskeyCaller caller, string userPasskeyId, CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyRevocationResult.Of(PasskeyOutcome.SessionRequired);
			if (string.IsNullOrWhiteSpace(userPasskeyId))
				return PasskeyRevocationResult.Of(PasskeyOutcome.InvalidRequest);

			var now = _time.GetUtcNow().UtcDateTime;
			if (!await HasRecentAccountMfaAsync(caller, now, cancellationToken))
				return PasskeyRevocationResult.Of(PasskeyOutcome.StepUpRequired);

			var passkey = await _passkeys.GetAsync(userPasskeyId, cancellationToken);
			if (passkey == null || !passkey.IsActive || !SameUser(passkey.UserId, caller.UserId))
				return PasskeyRevocationResult.Of(PasskeyOutcome.NotFound);

			if (!await _passkeys.TryRevokeAsync(userPasskeyId, caller.UserId, PasskeyRevocationReason.RemovedByUser, caller.UserId, now, cancellationToken))
				return PasskeyRevocationResult.Of(PasskeyOutcome.NotFound);

			await RetireDerivedStateAsync(caller.UserId, new[] { userPasskeyId }, cancellationToken);
			var (ended, currentEnded) = await EndLoginSessionsAsync(caller, new[] { userPasskeyId }, cancellationToken);
			await AuditAsync(caller, SystemAuditTypes.PasskeyRevoked,
				$"Passkey {userPasskeyId} ({ClientLabel((UserSessionClientApplication)passkey.ClientApplication)}) removed; " +
				$"{ended} session(s) that signed in with it ended.", cancellationToken);
			await NoticeAsync(caller, SecurityNoticeKind.PasskeyRemoved, cancellationToken);
			return new PasskeyRevocationResult { Outcome = PasskeyOutcome.Succeeded, Revoked = 1, SessionsEnded = ended, CurrentSessionEnded = currentEnded };
		}

		public async Task<PasskeyRevocationResult> RevokeAllForClientAsync(PasskeyCaller caller, UserSessionClientApplication client,
			CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyRevocationResult.Of(PasskeyOutcome.SessionRequired);

			var now = _time.GetUtcNow().UtcDateTime;
			if (!await HasRecentAccountMfaAsync(caller, now, cancellationToken))
				return PasskeyRevocationResult.Of(PasskeyOutcome.StepUpRequired);

			var revoked = await _passkeys.RevokeAllForClientAsync(caller.UserId, (int)client, PasskeyRevocationReason.RemovedAllForClient,
				caller.UserId, now, cancellationToken);
			if (revoked.Count == 0)
				return new PasskeyRevocationResult { Outcome = PasskeyOutcome.Succeeded };

			await RetireDerivedStateAsync(caller.UserId, revoked, cancellationToken);
			var (ended, currentEnded) = await EndLoginSessionsAsync(caller, revoked, cancellationToken);
			await AuditAsync(caller, SystemAuditTypes.PasskeyRevoked,
				$"All {revoked.Count} passkey(s) for {ClientLabel(client)} removed: {string.Join(", ", revoked)}; " +
				$"{ended} session(s) that signed in with them ended.", cancellationToken);
			await NoticeAsync(caller, SecurityNoticeKind.PasskeyRemoved, cancellationToken);
			return new PasskeyRevocationResult { Outcome = PasskeyOutcome.Succeeded, Revoked = revoked.Count, SessionsEnded = ended, CurrentSessionEnded = currentEnded };
		}

		public async Task<PasskeyOutcome> SetApprovalEnabledAsync(PasskeyCaller caller, string userPasskeyId, bool enabled,
			CancellationToken cancellationToken = default)
		{
			if (caller?.SessionKey == null)
				return PasskeyOutcome.SessionRequired;
			if (string.IsNullOrWhiteSpace(userPasskeyId))
				return PasskeyOutcome.InvalidRequest;

			// Turning approval off is always allowed; turning it on needs the approval feature.
			if (enabled && !_gates.ResponderApprovalEnabled)
				return PasskeyOutcome.Unavailable;

			// Turning it on needs a fresh assertion with that very passkey in this Responder session (plan section 7.9
			// eligibility); turning it off, any accepted account factor.
			var now = _time.GetUtcNow().UtcDateTime;
			if (enabled ? !await HasFreshAssertionWithAsync(caller, userPasskeyId, now, cancellationToken)
					: !await HasRecentAccountMfaAsync(caller, now, cancellationToken))
				return PasskeyOutcome.StepUpRequired;

			var passkey = await _passkeys.GetAsync(userPasskeyId, cancellationToken);
			if (passkey == null || !passkey.IsActive || !SameUser(passkey.UserId, caller.UserId))
				return PasskeyOutcome.NotFound;

			// Only a Responder passkey can approve another app's request (plan section 7.9).
			if (passkey.ClientApplication != (int)UserSessionClientApplication.Responder)
				return PasskeyOutcome.InvalidRequest;

			if (!await _passkeys.TrySetApprovalEnabledAsync(userPasskeyId, caller.UserId, enabled, cancellationToken))
				return PasskeyOutcome.NotFound;

			// Turning approval off retires what this passkey approved, as removing it would (plan section 7.9 revocation):
			// pending requests end, sign-ins it approved end, and its approval evidence stops counting on the next read.
			if (!enabled)
			{
				await CancelPendingApprovalsAsync(caller.UserId, cancellationToken);
				await EndLoginSessionsAsync(caller, new[] { userPasskeyId }, cancellationToken, approvalsOnly: true);
			}

			await AuditAsync(caller, SystemAuditTypes.PasskeyApprovalChanged,
				$"Passkey {userPasskeyId} {(enabled ? "may now approve" : "no longer approves")} other apps' requests.", cancellationToken);
			await NoticeAsync(caller, enabled ? SecurityNoticeKind.ApprovalTurnedOn : SecurityNoticeKind.ApprovalTurnedOff, cancellationToken);
			return PasskeyOutcome.Succeeded;
		}

		// ── Assertions for step-up (plan sections 7.6 and 11) ─────────────────────────

		public async Task<PasskeyCeremonyStart> BeginAssertionAsync(PasskeyCaller caller, AuthenticationChallengePurpose purpose,
			CancellationToken cancellationToken = default)
		{
			if (caller?.ChallengeParentId == null)
				return PasskeyCeremonyStart.Of(PasskeyOutcome.SessionRequired);
			if (!ParentFits(caller, purpose))
				return PasskeyCeremonyStart.Of(PasskeyOutcome.InvalidRequest);
			if (!IsAssertionAvailable(caller.ClientApplication, purpose))
				return PasskeyCeremonyStart.Of(PasskeyOutcome.Unavailable);

			try
			{
				var party = _registry.Get(caller.ClientApplication);
				var mine = BoundTo(await _passkeys.GetActiveForUserAsync(caller.UserId, cancellationToken), caller.ClientApplication, party.RpId)
					.Where(p => purpose != AuthenticationChallengePurpose.ApprovalResponse || p.ApprovalEnabled)
					.ToList();
				if (mine.Count == 0)
					return PasskeyCeremonyStart.Of(PasskeyOutcome.NotRegisteredForClient);

				var options = _provider.CreateAssertionOptions(caller.ClientApplication, mine.Select(p => p.CredentialId).ToList());
				var challenge = await _challenges.CreateAsync(Binding(caller, purpose, caller.DepartmentId), party.RpId, options, cancellationToken);
				return challenge == null
					? PasskeyCeremonyStart.Of(PasskeyOutcome.TooManyRequests)
					: new PasskeyCeremonyStart { Outcome = PasskeyOutcome.Succeeded, RequestId = challenge.AuthenticationChallengeId, OptionsJson = options };
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Passkey verification could not be started.");
				return PasskeyCeremonyStart.Of(PasskeyOutcome.ServiceUnavailable);
			}
		}

		public async Task<PasskeyAssertionResult> CompleteAssertionAsync(PasskeyCaller caller, AuthenticationChallengePurpose purpose, string requestId,
			string credentialJson, CancellationToken cancellationToken = default)
		{
			if (caller?.ChallengeParentId == null)
				return PasskeyAssertionResult.Of(PasskeyOutcome.SessionRequired);
			if (!ParentFits(caller, purpose))
				return PasskeyAssertionResult.Of(PasskeyOutcome.InvalidRequest);
			if (!IsAssertionAvailable(caller.ClientApplication, purpose))
				return PasskeyAssertionResult.Of(PasskeyOutcome.Unavailable);
			if (!IsCeremonyInput(requestId, credentialJson))
				return PasskeyAssertionResult.Of(PasskeyOutcome.InvalidRequest);

			try
			{
				var lookup = await _challenges.GetForCompletionAsync(requestId, Binding(caller, purpose, caller.DepartmentId), cancellationToken);
				if (!lookup.IsUsable)
					return PasskeyAssertionResult.Of(Map(lookup.Outcome));

				var challenge = lookup.Challenge;
				var party = _registry.Get(caller.ClientApplication);
				if (!string.Equals(challenge.RpId, party.RpId, StringComparison.Ordinal))
					return PasskeyAssertionResult.Of(PasskeyOutcome.ChallengeExpired);

				// The library verifies against whatever key it is handed, so the credential is loaded here: it must be this
				// user's, active, and bound to the client asking (plan section 3 item 14).
				var credentialId = _provider.ReadCredentialId(credentialJson);
				var stored = credentialId == null ? null : await _passkeys.GetActiveByCredentialAsync(party.RpId, SHA256.HashData(credentialId), cancellationToken);
				if (stored == null || !SameUser(stored.UserId, caller.UserId) || stored.ClientApplication != (int)caller.ClientApplication
					|| stored.CredentialId == null || !stored.CredentialId.AsSpan().SequenceEqual(credentialId)
					|| (purpose == AuthenticationChallengePurpose.ApprovalResponse && !stored.ApprovalEnabled))
				{
					await _challenges.RecordFailedAttemptAsync(challenge.AuthenticationChallengeId, cancellationToken);
					return PasskeyAssertionResult.Of(PasskeyOutcome.NotRegisteredForClient);
				}

				var verification = await _provider.VerifyAssertionAsync(caller.ClientApplication, challenge.OptionsJson, credentialJson,
					new PasskeyAssertionCredential
					{
						CredentialId = stored.CredentialId,
						PublicKey = stored.PublicKey,
						UserHandle = stored.UserHandle,
						SignCount = stored.SignCount
					}, cancellationToken);
				if (!verification.Succeeded)
				{
					await _challenges.RecordFailedAttemptAsync(challenge.AuthenticationChallengeId, cancellationToken);
					return PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed);
				}

				// The library accepts a replayed assertion; the single-use request is what refuses it.
				if (!await _challenges.TryConsumeAsync(challenge.AuthenticationChallengeId, cancellationToken))
					return PasskeyAssertionResult.Of(PasskeyOutcome.ChallengeConsumed);

				// Recorded only if the counter is still the one verified against and the passkey was not revoked meanwhile.
				var now = _time.GetUtcNow().UtcDateTime;
				var session = caller.SessionId == null ? null : await TryGetSessionAsync(caller.SessionId);
				if (!await _passkeys.TryRecordUseAsync(stored.UserPasskeyId, stored.SignCount, verification.SignCount, verification.IsBackedUp,
						(int)caller.ClientApplication, Truncate(session?.DeviceName, 256), caller.SharedMode, now, cancellationToken))
					return PasskeyAssertionResult.Of(PasskeyOutcome.VerificationFailed);

				return new PasskeyAssertionResult { Outcome = PasskeyOutcome.Succeeded, Passkey = stored, VerifiedOnUtc = now };
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Passkey verification could not be completed.");
				return PasskeyAssertionResult.Of(PasskeyOutcome.ServiceUnavailable);
			}
		}

		// ── Rules ─────────────────────────────────────────────────────────────────────

		private static int MaxDisplayNameLength => Math.Max(1, PasskeyConfig.MaxDisplayNameLength);

		/// <summary>
		/// A new credential needs a password or SSO verification for this session within five minutes, and an accepted
		/// second factor within five minutes (plan section 6.1 item 1), both under the user's current generation.
		/// </summary>
		private async Task<PasskeyOutcome> CheckEnrollmentEvidenceAsync(PasskeyCaller caller, DateTime asOfUtc, CancellationToken cancellationToken)
		{
			var firstFactor = await _evidence.GetLatestFirstFactorAsync(caller.UserId, caller.SessionKey, caller.AuthenticationGeneration, cancellationToken);
			if (!MfaEvidenceService.IsFresh(firstFactor, FirstFactorWindow, asOfUtc))
				return PasskeyOutcome.ReauthenticationRequired;

			return await HasRecentAccountMfaAsync(caller, asOfUtc, cancellationToken) ? PasskeyOutcome.Succeeded : PasskeyOutcome.StepUpRequired;
		}

		/// <summary>
		/// Account factor management accepts TOTP or a passkey bound to the calling client, verified for this session within
		/// five minutes; never Responder approval or provider step-up (plan section 7.6 row 14).
		/// </summary>
		private async Task<bool> HasRecentAccountMfaAsync(PasskeyCaller caller, DateTime asOfUtc, CancellationToken cancellationToken)
		{
			var latest = await _evidence.GetLatestSecondFactorAsync(caller.UserId, caller.SessionKey, caller.AuthenticationGeneration, cancellationToken);
			if (!MfaEvidenceService.IsFresh(latest, SecondFactorWindow, asOfUtc))
				return false;

			var method = (MfaEvidenceMethod)latest.Method;
			if (method == MfaEvidenceMethod.Passkey && latest.ClientApplication != (int)caller.ClientApplication)
				return false;

			return (method == MfaEvidenceMethod.Totp || method == MfaEvidenceMethod.Passkey) &&
				await _policy.IsMethodAcceptedAsync(null, MfaMethodScope.Account, method, cancellationToken);
		}

		/// <summary>The session's latest second factor is an assertion with this passkey, bound to the calling client, within five minutes.</summary>
		private async Task<bool> HasFreshAssertionWithAsync(PasskeyCaller caller, string userPasskeyId, DateTime asOfUtc, CancellationToken cancellationToken)
		{
			var latest = await _evidence.GetLatestSecondFactorAsync(caller.UserId, caller.SessionKey, caller.AuthenticationGeneration, cancellationToken);
			return MfaEvidenceService.IsFresh(latest, SecondFactorWindow, asOfUtc) && latest.Method == (int)MfaEvidenceMethod.Passkey &&
				latest.ClientApplication == (int)caller.ClientApplication &&
				string.Equals(latest.FactorReference, UserPasskey.FactorReferenceFor(userPasskeyId), StringComparison.Ordinal);
		}

		/// <summary>
		/// A removed passkey stops counting at once: the evidence it produced is revoked and the user's pending ceremonies
		/// are canceled (plan section 6.1 item 8). A failure here never undoes the revocation.
		/// </summary>
		private async Task RetireDerivedStateAsync(string userId, IEnumerable<string> userPasskeyIds, CancellationToken cancellationToken)
		{
			try
			{
				foreach (var id in userPasskeyIds)
					await _evidence.RevokeForFactorAsync(userId, UserPasskey.FactorReferenceFor(id), cancellationToken);
				await _challenges.CancelPendingForUserAsync(userId, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Evidence or challenges derived from a removed passkey could not be retired.");
			}

			// Approvals it made stop counting on the next evidence read; requests still waiting for it end now.
			await CancelPendingApprovalsAsync(userId, cancellationToken);
		}

		/// <summary>Pending Responder approval requests end when an approving passkey goes (plan section 7.9 revocation).</summary>
		private async Task CancelPendingApprovalsAsync(string userId, CancellationToken cancellationToken)
		{
			try
			{
				await _approvals.CancelPendingForUserAsync(userId, MfaApprovalEndReason.ApproverRevoked, _time.GetUtcNow().UtcDateTime, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Pending approval requests for a removed approver could not be ended.");
			}
		}

		/// <summary>
		/// Ends the caller's active sessions whose sign-in was verified with one of the removed passkeys (plan section 6.1
		/// item 8). Sessions that used another factor are untouched. A failure here never undoes the revocation.
		/// </summary>
		private async Task<(int Ended, bool CurrentEnded)> EndLoginSessionsAsync(PasskeyCaller caller, IReadOnlyCollection<string> userPasskeyIds,
			CancellationToken cancellationToken, bool approvalsOnly = false)
		{
			try
			{
				// A sign-in verified with the passkey itself, or approved with it from Responder (plan section 7.9).
				var references = approvalsOnly
					? new HashSet<string>(StringComparer.Ordinal)
					: new HashSet<string>(userPasskeyIds.Select(UserPasskey.FactorReferenceFor), StringComparer.Ordinal);
				var approvalPrefixes = userPasskeyIds.Select(MfaApprovalRequest.FactorReferencePrefixFor).ToList();
				bool UsedIt(string reference) => reference != null &&
					(references.Contains(reference) || approvalPrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.Ordinal)));

				var sessions = await _sessions.GetActiveByUserAsync(caller.UserId, _time.GetUtcNow().UtcDateTime);
				var ended = 0;
				var currentEnded = false;
				foreach (var session in sessions.Where(s => UsedIt(s.LoginMfaFactorReference)))
				{
					var result = await _userSessions.RevokeSessionAsync(caller.UserId, caller.UserId, session.UserSessionId,
						UserSessionRevocationReason.MfaChanged, cancellationToken);
					if (result.RevokedSessionCount > 0)
					{
						ended += result.RevokedSessionCount;
						currentEnded |= string.Equals(session.UserSessionId, caller.SessionId, StringComparison.Ordinal);
					}
				}

				return (ended, currentEnded);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Sessions that signed in with a removed passkey could not be ended.");
				return (0, false);
			}
		}

		private bool IsAssertionAvailable(UserSessionClientApplication client, AuthenticationChallengePurpose purpose)
		{
			var gateOn = purpose switch
			{
				AuthenticationChallengePurpose.LoginSecondFactor => _gates.LoginAcceptanceEnabled,
				AuthenticationChallengePurpose.SensitiveOperation => _gates.LoginAcceptanceEnabled || _gates.AdpAcceptanceEnabled,
				AuthenticationChallengePurpose.AdpStepUp => _gates.AdpAcceptanceEnabled,
				// Unlocking a shared session follows the sign-in passkey rules (plan section 12.5.3).
				AuthenticationChallengePurpose.SharedDeviceUnlock => _gates.LoginAcceptanceEnabled,
				// Only a Responder passkey with approval on answers another app's request (plan section 7.9).
				AuthenticationChallengePurpose.ApprovalResponse => _gates.ResponderApprovalEnabled && client == UserSessionClientApplication.Responder,
				_ => false
			};
			return gateOn && _registry.Get(client) != null && _provider.IsAvailableFor(client);
		}

		/// <summary>
		/// A login second factor is bound to its login transaction, an approval response to its approval request (from the
		/// approver's own session); every other ceremony to a signed-in session.
		/// </summary>
		private static bool ParentFits(PasskeyCaller caller, AuthenticationChallengePurpose purpose) => purpose switch
		{
			AuthenticationChallengePurpose.LoginSecondFactor => caller.ChallengeParentKind == AuthenticationChallengeParentKind.LoginTransaction,
			AuthenticationChallengePurpose.ApprovalResponse => caller.ChallengeParentKind == AuthenticationChallengeParentKind.ApprovalRequest &&
				!string.IsNullOrWhiteSpace(caller.SessionId),
			_ => caller.ChallengeParentKind == AuthenticationChallengeParentKind.Session
		};

		private static AuthenticationChallengeBinding Binding(PasskeyCaller caller, AuthenticationChallengePurpose purpose, int? departmentId) => new()
		{
			UserId = caller.UserId,
			Purpose = purpose,
			ClientApplication = caller.ClientApplication,
			ParentKind = caller.ChallengeParentKind,
			ParentId = caller.ChallengeParentId,
			DepartmentId = departmentId,
			AuthenticationGeneration = caller.AuthenticationGeneration,
			LockVersion = caller.SessionLockVersion
		};

		/// <summary>The caller's active passkeys for the client's current RP; credentials from a retired RP never count.</summary>
		private static List<UserPasskey> BoundTo(IEnumerable<UserPasskey> passkeys, UserSessionClientApplication client, string rpId) =>
			(passkeys ?? Enumerable.Empty<UserPasskey>())
				.Where(p => p.IsActive && p.ClientApplication == (int)client && string.Equals(p.RpId, rpId, StringComparison.Ordinal))
				.ToList();

		private static PasskeyOutcome Map(AuthenticationChallengeOutcome outcome) => outcome switch
		{
			AuthenticationChallengeOutcome.AlreadyUsed => PasskeyOutcome.ChallengeConsumed,
			AuthenticationChallengeOutcome.TooManyAttempts => PasskeyOutcome.TooManyAttempts,
			AuthenticationChallengeOutcome.Unavailable => PasskeyOutcome.ServiceUnavailable,
			// Not found, expired, stale and a binding mismatch look the same: the ceremony has to start again.
			_ => PasskeyOutcome.ChallengeExpired
		};

		private static bool IsCeremonyInput(string requestId, string credentialJson) =>
			!string.IsNullOrWhiteSpace(requestId) && requestId.Length <= 64 &&
			!string.IsNullOrWhiteSpace(credentialJson) && credentialJson.Length <= MaxCredentialJsonLength;

		private static bool SameUser(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// A display name is user text: control and invisible formatting characters (including bidirectional overrides)
		/// are removed and whitespace collapsed. It is escaped again wherever it is shown.
		/// </summary>
		internal static string NormalizeDisplayName(string displayName)
		{
			if (displayName == null)
				return null;

			var builder = new StringBuilder(displayName.Length);
			var pendingSpace = false;
			foreach (var ch in displayName)
			{
				var category = char.GetUnicodeCategory(ch);
				if (category == UnicodeCategory.Format)
					continue;

				if (category is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator || char.IsWhiteSpace(ch))
				{
					pendingSpace = builder.Length > 0;
					continue;
				}

				if (pendingSpace)
					builder.Append(' ');
				pendingSpace = false;
				builder.Append(ch);
			}

			return builder.Length == 0 ? null : builder.ToString();
		}

		internal static string ClientLabel(UserSessionClientApplication client) => client switch
		{
			UserSessionClientApplication.Web => "Resgrid Web",
			UserSessionClientApplication.Responder => "Responder",
			UserSessionClientApplication.Unit => "Unit",
			UserSessionClientApplication.Dispatch => "Dispatch",
			UserSessionClientApplication.Command => "IC",
			_ => client.ToString()
		};

		private static string DefaultDisplayName(UserSessionClientApplication client, DateTime createdOnUtc) =>
			$"{ClientLabel(client)} passkey ({createdOnUtc:yyyy-MM-dd})";

		private static string Truncate(string value, int length) =>
			string.IsNullOrWhiteSpace(value) ? null : value.Length <= length ? value : value[..length];

		/// <summary>Server-observed context from the session record; labels only, so a failed read leaves them empty.</summary>
		private async Task<UserSession> TryGetSessionAsync(string sessionId)
		{
			try
			{
				return await _sessions.GetByIdAsync(sessionId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Session context for a passkey record could not be read.");
				return null;
			}
		}

		/// <summary>A security notice to the account holder (plan section 6.4), naming the app and installation the change came from.</summary>
		private async Task NoticeAsync(PasskeyCaller caller, SecurityNoticeKind kind, CancellationToken cancellationToken)
		{
			var session = caller.SessionId == null ? null : await TryGetSessionAsync(caller.SessionId);
			await _notices.QueueAsync(new SecurityNoticeRequest
			{
				UserId = caller.UserId,
				Kind = kind,
				ClientApplication = caller.ClientApplication,
				InstallationLabel = session?.DeviceName,
				Region = SecurityNotices.Region(session?.LastRegion, session?.LastCountry)
			}, cancellationToken);
		}

		private async Task AuditAsync(PasskeyCaller caller, SystemAuditTypes type, string data, CancellationToken cancellationToken)
		{
			try
			{
				await _audits.SaveSystemAuditAsync(new SystemAudit
				{
					System = (int)caller.AuditSystem,
					Type = (int)type,
					UserId = caller.UserId,
					Username = caller.UserName,
					Successful = true,
					IpAddress = caller.IpAddress,
					ServerName = Environment.MachineName,
					Data = data
				}, cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				// The change already committed; a lost audit row is logged loudly rather than undoing it.
				Logging.LogException(ex, $"Passkey audit ({type}) could not be saved.");
			}
		}
	}
}
