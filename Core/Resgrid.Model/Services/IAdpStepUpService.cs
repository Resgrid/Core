using System;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// The one orchestration every Protected Data Grant goes through (passkey plan sections 8.1 and 9.2), on Web and the API
	/// alike: check the caller's session, verify or accept the factor, resolve the department's current policy and method
	/// switches, compute the absolute expiry from the verification time, record the evidence, and sign. A method other than
	/// TOTP needs a version 2 grant bound to a tracked session. Missing signing material is an unavailable operation, never
	/// a token-less success.
	/// </summary>
	public interface IAdpStepUpService
	{
		/// <summary>The methods the caller can use for this department's protected data now, and which to show first.</summary>
		Task<MfaMethodChoice> GetMethodChoiceAsync(AdpStepUpCaller caller, bool totpEnrolled, CancellationToken cancellationToken = default);

		/// <summary>Assertion options limited to the caller's passkeys for its own client, bound to its session.</summary>
		Task<PasskeyCeremonyStart> BeginPasskeyAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default);

		/// <summary>Verifies the assertion, records <c>AdpStepUp</c> evidence and issues a <c>passkey</c> grant.</summary>
		Task<AdpGrantIssue> CompletePasskeyAsync(AdpStepUpCaller caller, string requestId, string credentialJson,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Issues a <c>totp</c> grant for an authenticator code the caller has just verified (the code check belongs to the
		/// Identity surface that shares the account lockout), and records the evidence.
		/// </summary>
		Task<AdpGrantIssue> IssueForTotpAsync(AdpStepUpCaller caller, DateTime verifiedOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Asks the user's Responder to approve access to this department's protected data (plan section 7.9).</summary>
		Task<MfaApprovalStart> RequestApprovalAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default);

		/// <summary>Uses an approved ADP request once and issues a <c>passkey_approval</c> grant from its decision time.</summary>
		Task<AdpGrantIssue> CompleteApprovalAsync(AdpStepUpCaller caller, string approvalRequestId, CancellationToken cancellationToken = default);

		/// <summary>Redeems a brokered <c>adp_step_up</c> round trip and issues a <c>federated</c> grant (plan section 7.8).</summary>
		Task<AdpGrantIssue> CompleteFederatedAsync(AdpStepUpCaller caller, string ssoTransactionId, string code, string codeVerifier,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// A grant without a second factor for a client the department exempted from the prompt (plan section 3.3). It is
		/// never proof of MFA; <see cref="AdpGrantOutcome.StepUpRequired"/> for any other client.
		/// </summary>
		Task<AdpGrantIssue> IssueExemptAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default);

		/// <summary>
		/// A grant from this session's own recent second factor, with no new prompt (plan section 9.1): sign-in evidence where the
		/// department accepts reusing it, shared-unlock evidence likewise, or earlier protected-data evidence for this department.
		/// It expires from the original verification. <see cref="AdpGrantOutcome.StepUpRequired"/> when nothing qualifies.
		/// </summary>
		Task<AdpGrantIssue> IssueFromRecentEvidenceAsync(AdpStepUpCaller caller, CancellationToken cancellationToken = default);
	}
}
