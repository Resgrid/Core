using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Approvals;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// e-Signature S4: approval chains with thresholds (plan §23 S4) on the S3 multi-signer request, never a fork of it. A submission
	/// resolves the department's chain for the subject's amount into ordered (and parallel) member steps at L2, each open to the
	/// members the step names (a role, a named member and their delegate while on leave, a group's admins, a certifying official or the
	/// department admins) minus the submitter. Every approval is a signature bound to the subject's content hash: a change voids the
	/// approval (the gate compares hashes) and a re-submission after the change starts a new chain. A rejection needs a reason. A
	/// completed chain seals the subject's document through S1 (fails closed: "sealing pending"). Stalled steps escalate after N hours
	/// (worker 97). Workflow events 236-239 and notifications 73-74 carry no contact details.
	/// </summary>
	public interface IApprovalChainService
	{
		/// <summary>The approval kinds whose owning module has a registered source.</summary>
		IReadOnlyList<SignatureSubjectKinds> AvailableKinds { get; }

		/// <summary>Whether the kind's owning module (and its add-on) is active for the department (its own access service; fails closed).</summary>
		Task<bool> IsKindAvailableAsync(int departmentId, SignatureSubjectKinds kind);

		Task<List<ApprovalChainDefinition>> GetChainsAsync(int departmentId);

		Task<ApprovalChainDefinition> GetChainAsync(int departmentId, int approvalChainId);

		Task<ApprovalChainDefinition> GetActiveChainAsync(int departmentId, SignatureSubjectKinds kind);

		/// <summary>Saves a chain (steps replaced). Activating it deactivates the kind's other chains. Audited.</summary>
		Task<(ApprovalRefusals Refusal, ApprovalChainDefinition Definition)> SaveChainAsync(ApprovalChainDefinition definition, string userId, CancellationToken cancellationToken = default);

		Task<bool> DeactivateChainAsync(int departmentId, int approvalChainId, string userId, CancellationToken cancellationToken = default);

		Task<List<ApprovalDelegate>> GetDelegatesAsync(int departmentId);

		Task<ApprovalDelegate> SaveDelegateAsync(int departmentId, string userId, string delegateUserId, DateTime? startsOnUtc, DateTime? endsOnUtc, string actingUserId,
			CancellationToken cancellationToken = default);

		Task<bool> RemoveDelegateAsync(int departmentId, int approvalDelegateId, string actingUserId, CancellationToken cancellationToken = default);

		/// <summary>Submits (or re-submits after a change) a subject to its kind's active chain.</summary>
		Task<ApprovalResult> SubmitAsync(ApprovalSubmitInput input, CancellationToken cancellationToken = default);

		/// <summary>The member approves their step (L2: a fresh step-up or a content-bound passkey), bound to the submitted content hash.</summary>
		Task<ApprovalResult> ApproveAsync(int departmentId, int approvalRequestId, SignatureProof proof, bool requirePasskey, CancellationToken cancellationToken = default);

		/// <summary>The member rejects their step with a reason: the request is rejected and the subject goes back to its owner.</summary>
		Task<ApprovalResult> RejectAsync(int departmentId, int approvalRequestId, string userId, string reason, CancellationToken cancellationToken = default);

		/// <summary>The submitter (or a department admin) withdraws a pending request.</summary>
		Task<ApprovalResult> WithdrawAsync(int departmentId, int approvalRequestId, string userId, bool isDepartmentAdmin, CancellationToken cancellationToken = default);

		Task<ApprovalRequest> GetRequestAsync(int departmentId, int approvalRequestId);

		/// <summary>One request with its steps and what the member may do (null when it does not exist).</summary>
		Task<ApprovalView> GetViewAsync(int departmentId, int approvalRequestId, string userId, bool isDepartmentAdmin);

		/// <summary>A subject's approval state (the module page's panel and the approvals tab).</summary>
		Task<ApprovalSubjectStatus> GetSubjectStatusAsync(int departmentId, SignatureSubjectKinds kind, string subjectId, string userId, bool isDepartmentAdmin);

		/// <summary>The requests waiting for the member now ("My approvals").</summary>
		Task<List<ApprovalInboxItem>> GetInboxAsync(int departmentId, string userId);

		/// <summary>The department's requests in a status, newest first.</summary>
		Task<List<ApprovalRequest>> GetRequestsAsync(int departmentId, ApprovalRequestStatuses status, int take);

		/// <summary>Subjects of a kind that could be submitted now (the approvals "Submit" list).</summary>
		Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, SignatureSubjectKinds kind, int take);

		/// <summary>The S3 request completed (called by the approval kinds' signature source, once per signer; idempotent).</summary>
		Task HandleSignatureCompletedAsync(SignatureRequestRecord request, CancellationToken cancellationToken = default);

		// --- Worker 97 (due-work queries only; no department sweep, no billing gate) ---

		/// <summary>Announces steps whose turn started (delegating away approvers first) and schedules their escalation.</summary>
		Task<int> AnnounceTurnsAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>Escalates steps whose time has come: their escalation target may approve too and is told.</summary>
		Task<int> EscalateDueAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);

		/// <summary>Brings pending requests whose signature request closed elsewhere (completed, declined, expired, voided) to their state.</summary>
		Task<int> SyncClosedAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// "Approve before …": what a module asks before acting (send an invoice, approve a requisition or a plan, hand a MARS package to
	/// the portal). Required only where the department has an active chain for the kind and the owning module is available; approved
	/// only when the latest request is granted and the subject still hashes to what was approved.
	/// </summary>
	public interface IApprovalGateService
	{
		Task<ApprovalGateResult> CheckAsync(int departmentId, SignatureSubjectKinds kind, string subjectId);
	}

	/// <summary>
	/// One approval kind's module adapter: reads the subject through its module's repositories (never its services, to keep the gate
	/// free of cycles), computes the canonical content hash, reports whether the owning module and add-on are active, and applies the
	/// decision back where the module keeps one.
	/// </summary>
	public interface IApprovalSubjectSource
	{
		SignatureSubjectKinds Kind { get; }

		/// <summary>The owning module (and add-on) is active for the department, through its own access service. Fails closed.</summary>
		Task<bool> IsAvailableAsync(int departmentId);

		/// <summary>The subject now; null when it does not exist in the department.</summary>
		Task<ApprovalSubjectInfo> DescribeAsync(int departmentId, string subjectId);

		/// <summary>Subjects that could be submitted now, newest first.</summary>
		Task<List<ApprovalSubjectInfo>> GetCandidatesAsync(int departmentId, int take);

		/// <summary>The chain approved the subject: apply it (move the requisition to Approved). Throws when the module refuses.</summary>
		Task OnApprovedAsync(ApprovalRequest approval, string approverUserId, CancellationToken cancellationToken = default);

		/// <summary>The chain rejected the subject: hand it back to its owner (the requisition back to draft). Non-fatal.</summary>
		Task OnRejectedAsync(ApprovalRequest approval, string userId, string reason, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Member notifications for approvals (EventTypes 73 ApprovalAwaiting, 74 ApprovalDecided): push + email with the request's link
	/// and the push code "AP:{approvalRequestId}", active members only. The text names the subject and its content code, never anyone's
	/// contact details. Outbound text is English, like the rest of Resgrid's outbound messages.
	/// </summary>
	public interface IApprovalNotificationService
	{
		Task<int> NotifyAwaitingAsync(ApprovalRequest request, IReadOnlyCollection<string> userIds, bool escalated, CancellationToken cancellationToken = default);

		Task<int> NotifyDecidedAsync(ApprovalRequest request, CancellationToken cancellationToken = default);
	}

	/// <summary>Mutual-aid agreement documents (S4): drafted in the department, approved by its chain, sealed on completion.</summary>
	public interface IMutualAidAgreementService
	{
		Task<List<MutualAidAgreement>> GetAgreementsAsync(int departmentId);

		Task<MutualAidAgreement> GetAgreementAsync(int departmentId, int mutualAidAgreementId);

		/// <summary>Saves a draft (an agreement in approval or approved cannot be edited; withdraw or copy it). Audited.</summary>
		Task<MutualAidAgreement> SaveAsync(MutualAidAgreement agreement, string userId, CancellationToken cancellationToken = default);

		Task<bool> DeleteAsync(int departmentId, int mutualAidAgreementId, string userId, CancellationToken cancellationToken = default);
	}
}
