using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Security;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Responder approval (passkey plan section 7.9; workbook section 7.4): another app asks, the user approves in their own
	/// Responder with Responder's passkey after typing the number shown on the requesting screen, and the requester
	/// completes through its own login transaction or session. Nothing is issued to Responder.
	/// </summary>
	public interface IMfaApprovalService
	{
		/// <summary>Whether the approval gate is on.</summary>
		bool IsEnabled { get; }

		/// <summary>
		/// Whether <paramref name="requestingClient"/> could ask this user for approval now: the gate is on, the client is not
		/// Responder, and the user has an eligible Responder (an active personal Responder session and an active Responder
		/// passkey with approval on). Department policy is the caller's check.
		/// </summary>
		Task<bool> IsAvailableAsync(string userId, UserSessionClientApplication requestingClient, CancellationToken cancellationToken = default);

		// ── Requester ─────────────────────────────────────────────────────────────

		/// <summary>Creates the request, replacing the user's pending one, and notifies their Responder. The number is returned once.</summary>
		Task<MfaApprovalStart> RequestAsync(MfaApprovalRequester requester, CancellationToken cancellationToken = default);

		/// <summary>The request, only for the requester that made it, with its state as it reads now.</summary>
		Task<MfaApprovalResult> GetForRequesterAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId,
			CancellationToken cancellationToken = default);

		Task<MfaApprovalOutcome> CancelAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Uses an approved request once, for the requester that made it, under the account's current generation and while
		/// the approving passkey and Responder session are still valid. The requester records the evidence.
		/// </summary>
		Task<MfaApprovalResult> ConsumeAsync(string approvalRequestId, MfaApprovalRequesterKind requesterKind, string requesterId, string userId,
			long authenticationGeneration, CancellationToken cancellationToken = default);

		/// <summary>
		/// Stops approval requests on one of the user's Responder installations, or all of them with a null
		/// <paramref name="installationId"/>, which also turns approval off on every Responder passkey (plan section 6.5).
		/// Pending requests end, and approvals from a stopped installation no longer count. Returns how many installations and
		/// passkeys stopped.
		/// </summary>
		Task<(int Installations, int Passkeys)> DisableInstallationsAsync(string userId, string installationId, SharedSessionRequestInfo request,
			CancellationToken cancellationToken = default);

		/// <summary>Whether an approval's passkey and Responder session (its factor reference) still count.</summary>
		Task<bool> IsApproverValidAsync(string userId, string factorReference, long authenticationGeneration, CancellationToken cancellationToken = default);

		// ── Approver (Responder) ──────────────────────────────────────────────────

		/// <summary>The user's pending request for an eligible Responder session to review, or null.</summary>
		Task<MfaApprovalResult> GetPendingForApproverAsync(PasskeyCaller approver, CancellationToken cancellationToken = default);

		/// <summary>Assertion options for the approver's approval-enabled Responder passkeys, bound to the request.</summary>
		Task<(MfaApprovalOutcome Outcome, PasskeyCeremonyStart Ceremony)> BeginApprovalAsync(PasskeyCaller approver, string approvalRequestId,
			CancellationToken cancellationToken = default);

		/// <summary>Checks the number, verifies the passkey assertion, and approves in one compare-and-set.</summary>
		Task<(MfaApprovalResult Result, PasskeyOutcome Passkey)> ApproveAsync(PasskeyCaller approver, string approvalRequestId, string matchNumber,
			string requestId, string credentialJson, CancellationToken cancellationToken = default);

		/// <summary>Denies the request; "not me" also ends the requester's sign-in and suspends approval requests for 15 minutes.</summary>
		Task<MfaApprovalResult> DenyAsync(PasskeyCaller approver, string approvalRequestId, MfaApprovalEndReason reason,
			CancellationToken cancellationToken = default);
	}
}
