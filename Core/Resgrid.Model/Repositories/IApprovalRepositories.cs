using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Approvals;

namespace Resgrid.Model.Repositories
{
	/// <summary>Approval chain definitions (e-Signature S4, M0413).</summary>
	public interface IApprovalChainsRepository : IRepository<ApprovalChain>
	{
		Task<List<ApprovalChain>> GetForDepartmentAsync(int departmentId);

		/// <summary>The department's active chain for a subject kind (the newest when several were left active).</summary>
		Task<ApprovalChain> GetActiveForKindAsync(int departmentId, int subjectKind);
	}

	public interface IApprovalChainStepsRepository : IRepository<ApprovalChainStep>
	{
		Task<List<ApprovalChainStep>> GetForChainAsync(int departmentId, int approvalChainId);

		Task<int> DeleteForChainAsync(int departmentId, int approvalChainId, CancellationToken cancellationToken = default);
	}

	public interface IApprovalRequestsRepository : IRepository<ApprovalRequest>
	{
		Task<ApprovalRequest> GetForDepartmentAsync(int departmentId, int approvalRequestId);

		/// <summary>A subject's requests, newest first.</summary>
		Task<List<ApprovalRequest>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId);

		Task<ApprovalRequest> GetBySignatureRequestAsync(int departmentId, int signatureRequestId);

		/// <summary>The department's requests in a status (newest first, at most <paramref name="take"/>).</summary>
		Task<List<ApprovalRequest>> GetByStatusAsync(int departmentId, int status, int take);

		/// <summary>
		/// One conditional update from <paramref name="fromStatus"/> to <paramref name="toStatus"/>: exactly one caller wins when two
		/// paths decide the same request (the completion hook and the worker).
		/// </summary>
		Task<bool> ClaimStatusAsync(int approvalRequestId, int fromStatus, int toStatus, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 97: pending requests whose signature request is no longer open (completed, declined, expired, voided elsewhere).</summary>
		Task<List<ApprovalRequest>> GetPendingWithClosedSignatureAsync(int take);
	}

	public interface IApprovalRequestStepsRepository : IRepository<ApprovalRequestStep>
	{
		Task<List<ApprovalRequestStep>> GetForRequestAsync(int departmentId, int approvalRequestId);

		/// <summary>
		/// Worker 97: steps of pending requests whose S3 step's turn has started and was never announced (TurnNotifiedOn is null and the
		/// signer is unsigned). A due-work query across departments, never a sweep.
		/// </summary>
		Task<List<ApprovalRequestStep>> GetUnannouncedTurnsAsync(int take);

		/// <summary>Worker 97: steps of pending requests whose escalation time has come and that have not escalated.</summary>
		Task<List<ApprovalRequestStep>> GetDueEscalationsAsync(DateTime utcNow, int take);

		/// <summary>Claims the announcement once and stores the escalation time (null: never).</summary>
		Task<bool> ClaimAnnouncementAsync(int approvalRequestStepId, DateTime utcNow, DateTime? escalateDueOn, CancellationToken cancellationToken = default);

		/// <summary>Claims the escalation once.</summary>
		Task<bool> ClaimEscalationAsync(int approvalRequestStepId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface IApprovalDelegatesRepository : IRepository<ApprovalDelegate>
	{
		Task<List<ApprovalDelegate>> GetForDepartmentAsync(int departmentId);
	}

	public interface IMutualAidAgreementsRepository : IRepository<MutualAidAgreement>
	{
		Task<MutualAidAgreement> GetForDepartmentAsync(int departmentId, int mutualAidAgreementId);

		Task<List<MutualAidAgreement>> GetAllForDepartmentAsync(int departmentId);
	}
}
