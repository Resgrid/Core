using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Security;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <inheritdoc cref="IAdpStepUpService"/>
	public sealed class AdpStepUpService : IAdpStepUpService
	{
		private static readonly string[] Scopes = { ProtectedDataGrantScopes.Read, ProtectedDataGrantScopes.Write };

		private readonly IProtectedDataGrantService _grants;
		private readonly IDepartmentDataProtectionService _protection;
		private readonly IMfaPolicyService _policy;
		private readonly IMfaEvidenceService _evidence;
		private readonly IPasskeyService _passkeys;
		private readonly IMfaApprovalService _approvals;
		private readonly ISsoBrokerService _broker;
		private readonly IDepartmentSsoService _departmentSso;
		private readonly IMfaCredentialStateService _credentials;
		private readonly IMfaActivityService _activity;
		private readonly IAdpAuditRepository _audit;
		private readonly IPasskeyFeatureGates _gates;
		private readonly TimeProvider _time;

		public AdpStepUpService(IProtectedDataGrantService grants, IDepartmentDataProtectionService protection, IMfaPolicyService policy,
			IMfaEvidenceService evidence, IPasskeyService passkeys, IMfaApprovalService approvals, ISsoBrokerService broker,
			IDepartmentSsoService departmentSso, IMfaCredentialStateService credentials, IMfaActivityService activity, IAdpAuditRepository audit,
			IPasskeyFeatureGates gates, TimeProvider time)
		{
			_grants = grants;
			_protection = protection;
			_policy = policy;
			_evidence = evidence;
			_passkeys = passkeys;
			_approvals = approvals;
			_broker = broker;
			_departmentSso = departmentSso;
			_credentials = credentials;
			_activity = activity;
			_audit = audit;
			_gates = gates;
			_time = time;
		}

		private DateTime Now => _time.GetUtcNow().UtcDateTime;

		/// <summary>A passkey, approval or provider step-up grant is version 2, bound to a tracked session.</summary>
		private bool CanIssueVersionTwo(AdpStepUpCaller caller) =>
			_gates.EmitGrantV2 && !string.IsNullOrWhiteSpace(caller?.Session?.SessionId);

		public async Task<MfaMethodChoice> GetMethodChoiceAsync(AdpStepUpCaller caller, bool totpEnrolled, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			var versionTwo = CanIssueVersionTwo(caller);
			var passkey = versionTwo && await _passkeys.HasActiveForClientAsync(caller.UserId, caller.Client, cancellationToken);
			var approval = versionTwo && await _approvals.IsAvailableAsync(caller.UserId, caller.Client, cancellationToken);
			var federated = versionTwo && await _departmentSso.IsFederatedMfaAvailableAsync(caller.DepartmentId, caller.UserId, cancellationToken);
			return await _policy.GetMethodChoiceAsync(caller.UserId, totpEnrolled, caller.DepartmentId, MfaMethodScope.Adp, passkey, federated, approval,
				cancellationToken);
		}

		// ── Passkey ───────────────────────────────────────────────────────────────────

		public async Task<PasskeyCeremonyStart> BeginPasskeyAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			if (!CanIssueVersionTwo(caller))
				return PasskeyCeremonyStart.Of(caller.Session == null ? PasskeyOutcome.SessionRequired : PasskeyOutcome.Unavailable);
			if (!await _policy.IsMethodAcceptedAsync(caller.DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.Passkey, cancellationToken))
				return PasskeyCeremonyStart.Of(PasskeyOutcome.Unavailable);

			return await _passkeys.BeginAssertionAsync(caller.ToPasskeyCaller(), AuthenticationChallengePurpose.AdpStepUp, cancellationToken);
		}

		public async Task<AdpGrantIssue> CompletePasskeyAsync(AdpStepUpCaller caller, string requestId, string credentialJson,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			var refusal = await RefuseBeforeVerifyingAsync(caller, MfaEvidenceMethod.Passkey, cancellationToken);
			if (refusal != null)
				return refusal;

			var assertion = await _passkeys.CompleteAssertionAsync(caller.ToPasskeyCaller(), AuthenticationChallengePurpose.AdpStepUp, requestId,
				credentialJson, cancellationToken);
			if (!assertion.Succeeded)
			{
				if (assertion.Outcome is PasskeyOutcome.VerificationFailed or PasskeyOutcome.NotRegisteredForClient)
					await RecordDeniedAsync(caller, MfaEvidenceMethod.Passkey, cancellationToken);
				await AuditVerifyAsync(caller, false);
				return AdpGrantIssue.Of(assertion.Outcome == PasskeyOutcome.ServiceUnavailable ? AdpGrantOutcome.ServiceUnavailable : AdpGrantOutcome.VerificationFailed,
					PasskeyOutcomes.ErrorCode(assertion.Outcome));
			}

			await AuditVerifyAsync(caller, true);
			return await IssueAsync(caller, MfaEvidenceMethod.Passkey, assertion.VerifiedOnUtc, UserPasskey.FactorReferenceFor(assertion.Passkey.UserPasskeyId),
				cancellationToken);
		}

		// ── TOTP ──────────────────────────────────────────────────────────────────────

		public Task<AdpGrantIssue> IssueForTotpAsync(AdpStepUpCaller caller, DateTime verifiedOnUtc, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			return IssueAsync(caller, MfaEvidenceMethod.Totp, verifiedOnUtc, null, cancellationToken);
		}

		// ── Responder approval ────────────────────────────────────────────────────────

		public async Task<MfaApprovalStart> RequestApprovalAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			if (caller.Session == null)
				return MfaApprovalStart.Of(MfaApprovalOutcome.SessionRequired);
			if (!CanIssueVersionTwo(caller) ||
				!await _policy.IsMethodAcceptedAsync(caller.DepartmentId, MfaMethodScope.Adp, MfaEvidenceMethod.PasskeyApproval, cancellationToken))
				return MfaApprovalStart.Of(MfaApprovalOutcome.Unavailable);

			return await _approvals.RequestAsync(new MfaApprovalRequester
			{
				UserId = caller.UserId,
				Kind = MfaApprovalRequesterKind.Session,
				RequesterId = caller.Session.SessionId,
				ClientApplication = caller.Client,
				AuthenticationGeneration = caller.Session.AuthenticationGeneration,
				DepartmentId = caller.DepartmentId,
				Purpose = MfaApprovalPurpose.Adp,
				SharedMode = caller.Session.SharedMode,
				LockVersion = caller.Session.SessionLockVersion,
				IpAddress = caller.IpAddress,
				UserName = caller.UserName,
				AuditSystem = caller.AuditSystem
			}, cancellationToken);
		}

		public async Task<AdpGrantIssue> CompleteApprovalAsync(AdpStepUpCaller caller, string approvalRequestId, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			var refusal = await RefuseBeforeVerifyingAsync(caller, MfaEvidenceMethod.PasskeyApproval, cancellationToken);
			if (refusal != null)
				return refusal;

			// The request must be this session's, for this department's protected data, at the lock version it was made at.
			var found = await _approvals.GetForRequesterAsync(approvalRequestId, MfaApprovalRequesterKind.Session, caller.Session.SessionId, cancellationToken);
			if (found.Succeeded && (found.Request.RequestPurpose != MfaApprovalPurpose.Adp || found.Request.DepartmentId != caller.DepartmentId ||
					found.Request.LockVersion != caller.Session.SessionLockVersion))
				return AdpGrantIssue.Of(AdpGrantOutcome.ApprovalUnavailable);

			var consumed = await _approvals.ConsumeAsync(approvalRequestId, MfaApprovalRequesterKind.Session, caller.Session.SessionId, caller.UserId,
				caller.Session.AuthenticationGeneration, cancellationToken);
			if (!consumed.Succeeded)
				return consumed.Outcome switch
				{
					MfaApprovalOutcome.Pending => AdpGrantIssue.Of(AdpGrantOutcome.ApprovalPending),
					MfaApprovalOutcome.ServiceUnavailable => AdpGrantIssue.Of(AdpGrantOutcome.ServiceUnavailable),
					_ => AdpGrantIssue.Of(AdpGrantOutcome.ApprovalUnavailable, MfaApprovalOutcomes.ErrorCode(consumed.Outcome))
				};

			var approval = consumed.Request;
			await AuditVerifyAsync(caller, true);
			return await IssueAsync(caller, MfaEvidenceMethod.PasskeyApproval, approval.DecidedOnUtc ?? Now,
				MfaApprovalRequest.FactorReferenceFor(approval.ApproverPasskeyId, approval.ApproverSessionId), cancellationToken);
		}

		// ── Provider step-up ──────────────────────────────────────────────────────────

		public async Task<AdpGrantIssue> CompleteFederatedAsync(AdpStepUpCaller caller, string ssoTransactionId, string code, string codeVerifier,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			var refusal = await RefuseBeforeVerifyingAsync(caller, MfaEvidenceMethod.Federated, cancellationToken);
			if (refusal != null)
				return refusal;

			var redeemed = await _broker.RedeemAsync(ssoTransactionId, code, codeVerifier, caller.Client, cancellationToken, SsoTransactionPurpose.AdpStepUp);
			var transaction = redeemed.Transaction;
			var config = redeemed.Succeeded ? await _departmentSso.GetTestedFederatedMfaConfigAsync(caller.DepartmentId, cancellationToken) : null;
			if (!redeemed.Succeeded || transaction.DepartmentId != caller.DepartmentId || !FederatedMfaMapping.Satisfies(transaction, config) ||
				!string.Equals(transaction.SessionId, caller.Session.SessionId, StringComparison.Ordinal) ||
				// Begun before this shared session's last lock: nothing from before a lock counts after it (plan section 12.5.3).
				(caller.Session.SessionLockedOnUtc != null && transaction.CreatedOnUtc <= caller.Session.SessionLockedOnUtc.Value) ||
				!string.Equals(transaction.ExpectedUserId, caller.UserId, StringComparison.OrdinalIgnoreCase) ||
				!string.Equals(transaction.UserId, caller.UserId, StringComparison.OrdinalIgnoreCase) ||
				transaction.AuthenticationGeneration != caller.Session.AuthenticationGeneration)
			{
				if (redeemed.Succeeded)
					await RecordDeniedAsync(caller, MfaEvidenceMethod.Federated, cancellationToken);
				await AuditVerifyAsync(caller, false);
				return redeemed.Outcome == SsoBrokerOutcome.ServiceUnavailable
					? AdpGrantIssue.Of(AdpGrantOutcome.ServiceUnavailable)
					: AdpGrantIssue.Of(AdpGrantOutcome.VerificationFailed, redeemed.Succeeded ? "federated_mfa_not_satisfied" : SsoBrokerOutcomes.ErrorCode(redeemed.Outcome));
			}

			await AuditVerifyAsync(caller, true);
			return await IssueAsync(caller, MfaEvidenceMethod.Federated, transaction.AuthenticatedOnUtc ?? Now,
				FederatedMfaMapping.FactorReferenceFor(config.DepartmentSsoConfigId, config.FederatedMfaMappingVersion), cancellationToken);
		}

		// ── Exemption ─────────────────────────────────────────────────────────────────

		public async Task<AdpGrantIssue> IssueExemptAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);

			// The exemption answer and the epoch the grant is stamped with come from one policy snapshot, so a revocation
			// between two reads cannot mint a grant carrying the epoch that revoked it.
			var decision = await _protection.GetStepUpDecisionForClientAsync(caller.DepartmentId, caller.Client);
			if (decision.StepUpRequired)
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);
			if (!_grants.CanIssueGrants)
				return AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured);

			var window = ProtectedGrantBinding.EffectiveWindowMinutes(decision.StepUpWindowMinutes);
			var request = ProtectedGrantIssueRequests.ForSession(new ProtectedDataGrantIssueRequest
			{
				UserId = caller.UserId,
				DepartmentId = caller.DepartmentId,
				SessionId = caller.Session?.SessionId ?? caller.LegacySessionId,
				ClientApp = (int)caller.Client,
				PolicyEpoch = decision.PolicyEpoch,
				WindowMinutes = window,
				Scopes = Scopes,
				MfaAtUtc = Now,
				StepUpExempt = true
			}, caller.Session, ProtectedDataGrantMfaMethods.None, _gates.EmitGrantV2);
			if (request.Version == 2)
				request.NotAfterUtc = caller.Session.SessionExpiresOnUtc;

			return await SignAsync(caller, request, window, "step-up-exempt", ProtectedDataGrantMfaMethods.None, decision.PolicyEpoch);
		}

		// ── Shared orchestration ──────────────────────────────────────────────────────

		/// <summary>
		/// What stops a passkey, approval or provider step-up before any factor is checked: no signing material, no tracked
		/// session, version 2 not yet emitted, or a method the department or deployment does not accept for protected data.
		/// </summary>
		private async Task<AdpGrantIssue> RefuseBeforeVerifyingAsync(AdpStepUpCaller caller, MfaEvidenceMethod method, CancellationToken cancellationToken)
		{
			if (!_grants.CanIssueGrants)
				return AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured);
			if (caller.Session == null || string.IsNullOrWhiteSpace(caller.Session.SessionId))
				return AdpGrantIssue.Of(AdpGrantOutcome.SessionRequired);
			if (!CanIssueVersionTwo(caller) || !await _policy.IsMethodAcceptedAsync(caller.DepartmentId, MfaMethodScope.Adp, method, cancellationToken))
				return AdpGrantIssue.Of(AdpGrantOutcome.MethodNotAllowed);
			return null;
		}

		/// <summary>
		/// Issues a grant for a verification that has just succeeded (plan sections 8.1 and 9.2): the method must still be
		/// accepted and its credential current; the verification is recorded as <c>AdpStepUp</c> evidence for this department
		/// (it happened, whether or not a grant can be signed); the expiry runs from the verification, never from issuance,
		/// and never past the session's end.
		/// </summary>
		// ── Recent sign-in or unlock evidence (plan section 9.1) ─────────────────────────────────────────────────────────

		/// <summary>
		/// A grant from this session's own recent second factor, without a new prompt (plan section 9.1): the latest real verification
		/// for this session, account generation and client, when the department accepts its method for protected data now and
		/// accepts reusing it, and its credential still counts. Sign-in evidence needs <c>AcceptRecentLoginMfaForAdp</c>; shared
		/// unlock evidence needs <c>AcceptRecentUnlockMfaForAdp</c> at the session's current lock; protected-data evidence counts only
		/// in the department it was for. The grant expires from the original verification, never from now.
		/// </summary>
		public async Task<AdpGrantIssue> IssueFromRecentEvidenceAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(caller);
			if (!_grants.CanIssueGrants)
				return AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured);
			if (caller.Session == null || string.IsNullOrWhiteSpace(caller.EvidenceKey))
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);

			var evidence = await _evidence.GetLatestSecondFactorAsync(caller.UserId, caller.EvidenceKey, caller.Session.AuthenticationGeneration,
				cancellationToken);
			if (evidence == null || evidence.Kind != (int)MfaEvidenceKind.SecondFactor || evidence.ClientApplication != (int)caller.Client ||
				evidence.AuthenticationGeneration != caller.Session.AuthenticationGeneration)
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);

			var reusable = (MfaEvidencePurpose)evidence.Purpose switch
			{
				MfaEvidencePurpose.Login => (await _departmentSso.GetSecurityPolicyForDepartmentAsync(caller.DepartmentId, cancellationToken)
					?? new DepartmentSecurityPolicy()).AcceptRecentLoginMfaForAdp,
				// The unlock switch and the lock are the policy service's and the evidence service's to decide.
				MfaEvidencePurpose.SharedUnlock => true,
				MfaEvidencePurpose.AdpStepUp => evidence.DepartmentId == caller.DepartmentId,
				_ => false
			};
			if (!reusable || !await _policy.IsEvidenceAcceptedAsync(caller.DepartmentId, MfaMethodScope.Adp, evidence, cancellationToken))
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);

			return await IssueAsync(caller, (MfaEvidenceMethod)evidence.Method, evidence.VerifiedOnUtc, evidence.FactorReference, cancellationToken,
				reused: true);
		}

		private async Task<AdpGrantIssue> IssueAsync(AdpStepUpCaller caller, MfaEvidenceMethod method, DateTime verifiedOnUtc, string factorReference,
			CancellationToken cancellationToken, bool reused = false)
		{
			var versionTwo = CanIssueVersionTwo(caller);
			var grantMethod = GrantMethodFor(method);
			if (grantMethod == null)
				return AdpGrantIssue.Of(AdpGrantOutcome.MethodNotAllowed);
			if (method != MfaEvidenceMethod.Totp)
			{
				if (caller.Session == null)
					return AdpGrantIssue.Of(AdpGrantOutcome.SessionRequired);
				if (!versionTwo || !await _policy.IsMethodAcceptedAsync(caller.DepartmentId, MfaMethodScope.Adp, method, cancellationToken))
					return AdpGrantIssue.Of(AdpGrantOutcome.MethodNotAllowed);
			}

			// The credential the verification used must still count, at the state version the grant will carry.
			MfaCredentialSnapshot credential = null;
			if (method != MfaEvidenceMethod.Totp)
			{
				credential = await _credentials.ResolveAsync(caller.UserId, caller.DepartmentId, caller.Client, method, factorReference,
					caller.Session.AuthenticationGeneration, cancellationToken);
				if (credential == null)
					return AdpGrantIssue.Of(AdpGrantOutcome.CredentialRevoked);
			}

			verifiedOnUtc = DateTime.SpecifyKind(verifiedOnUtc, DateTimeKind.Utc);
			// A reused verification is already this session's evidence; only a new one is recorded.
			if (!reused)
				await RecordEvidenceAsync(caller, method, verifiedOnUtc, factorReference, cancellationToken);

			if (!_grants.CanIssueGrants)
				return AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured);

			var policy = await _protection.GetPolicyByDepartmentIdAsync(caller.DepartmentId);
			var window = ProtectedGrantBinding.EffectiveWindowMinutes(policy?.StepUpWindowMinutes ?? 0);
			var now = Now;

			// Expiry: the earliest of verification plus the window and the session's end. Nothing already expired is issued,
			// and nothing verified in the future beyond the allowed skew is believed.
			var skew = TimeSpan.FromSeconds(Math.Max(0, DataProtectionConfig.GrantClockSkewSeconds));
			var expiry = verifiedOnUtc.AddMinutes(window);
			if (versionTwo && caller.Session?.SessionExpiresOnUtc is DateTime sessionEnd && sessionEnd < expiry)
				expiry = sessionEnd;
			if (verifiedOnUtc > now.Add(skew) || expiry <= now)
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);

			var request = ProtectedGrantIssueRequests.ForSession(new ProtectedDataGrantIssueRequest
			{
				UserId = caller.UserId,
				DepartmentId = caller.DepartmentId,
				SessionId = caller.Session?.SessionId ?? caller.LegacySessionId,
				ClientApp = (int)caller.Client,
				PolicyEpoch = policy?.PolicyEpoch ?? 0,
				WindowMinutes = window,
				Scopes = Scopes,
				MfaAtUtc = verifiedOnUtc
			}, caller.Session, grantMethod, versionTwo);

			if (request.Version == 2)
			{
				request.MfaCredentialId = credential?.CredentialId;
				request.MfaStateVersion = credential?.StateVersion;
				request.NotAfterUtc = caller.Session.SessionExpiresOnUtc;
			}

			return await SignAsync(caller, request, window, reused ? "mfa-reused" : "mfa-verified", grantMethod, policy?.PolicyEpoch ?? 0);
		}

		private async Task<AdpGrantIssue> SignAsync(AdpStepUpCaller caller, ProtectedDataGrantIssueRequest request, int window, string auditOutcome,
			string grantMethod, long policyEpoch)
		{
			ProtectedDataGrantIssueResult issued;
			try
			{
				issued = _grants.IssueGrant(request);
			}
			catch (ArgumentException)
			{
				// The verification or the session ran out between the checks and signing.
				return AdpGrantIssue.Of(AdpGrantOutcome.StepUpRequired);
			}
			catch (InvalidOperationException ex)
			{
				Logging.LogException(ex, "A Protected Data Grant could not be signed.");
				return AdpGrantIssue.Of(AdpGrantOutcome.NotConfigured);
			}

			await _audit.AppendAsync(new AdpAuditEvent
			{
				DepartmentId = caller.DepartmentId,
				Layer = "identity",
				Operation = "grant-issued",
				Outcome = auditOutcome,
				ActorId = caller.UserId,
				CorrelationId = issued.GrantId,
				ResourceId = grantMethod,
				PolicyEpoch = policyEpoch
			});

			return new AdpGrantIssue
			{
				Outcome = AdpGrantOutcome.Issued,
				GrantId = issued.GrantId,
				Token = issued.Token,
				ExpiresOnUtc = issued.ExpiresOnUtc,
				WindowMinutes = window
			};
		}

		private static string GrantMethodFor(MfaEvidenceMethod method) => method switch
		{
			MfaEvidenceMethod.Totp => ProtectedDataGrantMfaMethods.Totp,
			MfaEvidenceMethod.Passkey => ProtectedDataGrantMfaMethods.Passkey,
			MfaEvidenceMethod.PasskeyApproval => ProtectedDataGrantMfaMethods.PasskeyApproval,
			MfaEvidenceMethod.Federated => ProtectedDataGrantMfaMethods.Federated,
			_ => null
		};

		/// <summary>
		/// Records the ADP step-up as evidence on the caller's session (purpose <c>AdpStepUp</c>, plan section 5.3), for later
		/// reuse under section 9.1. The grant is this call's product, so a failure here is logged, not returned.
		/// </summary>
		private async Task RecordEvidenceAsync(AdpStepUpCaller caller, MfaEvidenceMethod method, DateTime verifiedOnUtc, string factorReference,
			CancellationToken cancellationToken)
		{
			var sessionKey = caller.EvidenceKey;
			if (sessionKey == null)
				return;

			try
			{
				await _evidence.RecordAsync(caller.UserId, sessionKey, caller.Client, MfaEvidenceKind.SecondFactor, method, MfaEvidencePurpose.AdpStepUp,
					verifiedOnUtc, caller.Session?.AuthenticationGeneration ?? caller.AccountAuthenticationGeneration, caller.DepartmentId, factorReference,
					cancellationToken);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException))
			{
				Logging.LogException(ex, "Failed to record ADP step-up evidence.");
			}
		}

		private Task RecordDeniedAsync(AdpStepUpCaller caller, MfaEvidenceMethod method, CancellationToken cancellationToken) =>
			_activity.RecordAsync(new MfaActivityEntry
			{
				UserId = caller.UserId, Method = method, Purpose = MfaEvidencePurpose.AdpStepUp, Successful = false, ClientApplication = caller.Client,
				SharedMode = caller.Session?.SharedMode == true, DepartmentId = caller.DepartmentId, SessionId = caller.Session?.SessionId
			}, cancellationToken);

		private Task AuditVerifyAsync(AdpStepUpCaller caller, bool verified) =>
			_audit.AppendAsync(new AdpAuditEvent
			{
				DepartmentId = caller.DepartmentId, Layer = "identity", Operation = "mfa-verify", Outcome = verified ? "verified" : "denied", ActorId = caller.UserId
			});
	}
}
