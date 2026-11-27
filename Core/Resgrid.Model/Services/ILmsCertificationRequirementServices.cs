using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Round 9A (LMS add-on): certifications backed by training. Refusals are InvalidOperationException("lms_cert_req_*") codes the pages show
	// as text. Callers check the LMS gate first; worker 94 queries for departments with active sets and asks the gate once per department
	// with due work (R-L9).

	/// <summary>
	/// The issuance engine and its ledger: every automatic issue or renewal (an LMS course or path completion, a completed requirement set,
	/// a cycle's renewal hours) goes through here, gets its expiry from the type's validity rule on department-local dates, is recorded with
	/// the record's previous state and the learning records behind it, audited, told to the member (push + email, EventTypes 77) and raised
	/// as a workflow event (275/276). A void of a learning record behind it re-evaluates it: another valid completion keeps it, otherwise the
	/// record returns to its earlier state (or a record the engine created is withdrawn), audited, told to the member and raised (277).
	/// </summary>
	public interface ILmsCertificationIssuanceService
	{
		Task<LmsCertificationIssueResult> IssueOrRenewAsync(LmsCertificationIssueRequest request, CancellationToken cancellationToken = default);

		/// <summary>Ledgers an issuance another engine made itself (the cycle auto-renew's next cycle); the row is returned with its id.</summary>
		Task<LmsCertificationIssuance> RecordAsync(LmsCertificationIssuance issuance, CancellationToken cancellationToken = default);

		/// <summary>
		/// A member's learning records were voided: each live issuance whose basis holds one of them is re-evaluated (kept with a new basis, its
		/// expiry adjusted, rolled back, unwound under a later issuance, or left for review when the record was changed by hand since).
		/// </summary>
		Task<LmsCertificationVoidResult> OnRecordsVoidedAsync(int departmentId, string userId, IReadOnlyCollection<int> voidedRecordIds, string actorUserId,
			CancellationToken cancellationToken = default);

		/// <summary>The issuances of one record, oldest first (the certification record page).</summary>
		Task<List<LmsCertificationIssuance>> GetForCertificationAsync(int departmentId, int personnelCertificationId);
	}

	/// <summary>
	/// Certification requirement sets (department-defined per certification type): the editor, member progress ("3 of 4 done") for My Training,
	/// the certification record page and v4, the officers' "close to lapsing" list, automatic issue or renewal when the last item completes
	/// within the cycle window, and worker 94's daily step (catch-up issue/renew and "requirements due" notices, EventTypes 78, trigger 278).
	/// </summary>
	public interface ILmsCertificationRequirementService
	{
		/// <summary>Every person-scoped type with its set (or none), item names and holder counts (officers' list).</summary>
		Task<List<LmsCertificationRequirementSetRow>> GetSetRowsAsync(int departmentId, DateTime utcNow);

		/// <summary>The type's set with its items; null when the type has none.</summary>
		Task<LmsCertificationRequirementSet> GetSetAsync(int departmentId, int certificationTypeId);

		/// <summary>Creates or replaces the type's set (every item validated, targets in the department). Audited.</summary>
		Task<LmsCertificationRequirementSet> SaveSetAsync(int departmentId, LmsCertificationRequirementSetInput input, string userId, CancellationToken cancellationToken = default);

		Task<bool> DeleteSetAsync(int departmentId, int certificationTypeId, string userId, CancellationToken cancellationToken = default);

		/// <summary>What the editor offers: the department's courses, trainings, training categories and learning paths (kind, id, name).</summary>
		Task<List<(int Kind, int Id, string Name)>> GetTargetOptionsAsync(int departmentId);

		/// <summary>The name of an item's target (course, training, category, path or skill code) for display.</summary>
		Task<Dictionary<int, string>> GetItemNamesAsync(int departmentId, IEnumerable<LmsCertificationRequirementItem> items);

		/// <summary>A member's progress on every active set that concerns them (they hold the type, or the set issues to non-holders and they started).</summary>
		Task<List<LmsCertificationProgress>> GetMemberProgressAsync(int departmentId, string userId, DateTime utcNow);

		/// <summary>The officers' list for one type: holders and members with progress, closest to lapsing first.</summary>
		Task<List<LmsCertificationProgress>> GetTypeProgressAsync(int departmentId, int certificationTypeId, DateTime utcNow);

		/// <summary>
		/// Issues or renews for these members every set they have now completed (the gate was already checked). Called after a completion
		/// (the learning record observer) and by the worker.
		/// </summary>
		Task<LmsCertificationRequirementRunResult> EvaluateMembersAsync(int departmentId, IReadOnlyCollection<string> userIds, string actorUserId, DateTime utcNow,
			CancellationToken cancellationToken = default);

		/// <summary>Worker 94: departments with active sets, once per department-local day, the gate once per department.</summary>
		Task<LmsCertificationRequirementRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>One department's catch-up issue/renew and due notices (the gate was already asked).</summary>
		Task<LmsCertificationRequirementRunResult> RunDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
