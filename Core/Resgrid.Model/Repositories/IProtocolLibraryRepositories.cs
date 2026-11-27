using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Signatures (S0). Every read starts at the department.</summary>
	public interface ISignatureRecordsRepository : IRepository<SignatureRecord>
	{
		Task<List<SignatureRecord>> GetForSubjectAsync(int departmentId, int subjectKind, string subjectId);

		Task<List<SignatureRecord>> GetForSubjectsAsync(int departmentId, int subjectKind, IEnumerable<string> subjectIds);

		Task<SignatureRecord> GetByIdForDepartmentAsync(int departmentId, int signatureRecordId);
	}

	/// <summary>Library categories (P1).</summary>
	public interface IProtocolCategoriesRepository : IRepository<ProtocolCategory>
	{
		Task<List<ProtocolCategory>> GetForDepartmentAsync(int departmentId);

		Task<ProtocolCategory> GetByIdForDepartmentAsync(int departmentId, int protocolCategoryId);
	}

	/// <summary>Protocol versions (P1). Header reads never select the content columns.</summary>
	public interface IProtocolVersionsRepository : IRepository<ProtocolVersion>
	{
		/// <summary>The protocol's versions, newest first; without content unless <paramref name="withContent"/>.</summary>
		Task<List<ProtocolVersion>> GetForProtocolAsync(int departmentId, int protocolId, bool withContent = false);

		Task<ProtocolVersion> GetByIdForDepartmentAsync(int departmentId, int protocolVersionId);

		/// <summary>The given versions of the department (content included only when asked).</summary>
		Task<List<ProtocolVersion>> GetByIdsAsync(int departmentId, IEnumerable<int> protocolVersionIds, bool withContent = false);

		/// <summary>Every version header of the department (library tiles, review queue).</summary>
		Task<List<ProtocolVersion>> GetHeadersForDepartmentAsync(int departmentId);

		/// <summary>
		/// Writes the version only if its RowVersion is still <paramref name="expectedRowVersion"/>, and bumps it. False when
		/// another save got there first (R-P8): nothing is written.
		/// </summary>
		Task<bool> TryUpdateAsync(ProtocolVersion version, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Approved versions whose effective date has come (worker 84), oldest first, across departments.</summary>
		Task<List<ProtocolVersion>> GetDueActivationsAsync(DateTime utcNow, int take);

		/// <summary>Published versions past their expiry (worker 84), oldest first, across departments.</summary>
		Task<List<ProtocolVersion>> GetDueExpirationsAsync(DateTime utcNow, int take);

		/// <summary>The department's superseded or retired versions that are not on hold, not in force and cited by no call.</summary>
		Task<List<ProtocolVersion>> GetPurgeCandidatesAsync(int departmentId, DateTime endedBeforeUtc, int take);

		/// <summary>Removes a version with its approvals and comments (retention purge only, never a user action).</summary>
		Task<int> PurgeAsync(int departmentId, int protocolVersionId, CancellationToken cancellationToken = default);
	}

	public interface IProtocolVersionApprovalsRepository : IRepository<ProtocolVersionApproval>
	{
		Task<List<ProtocolVersionApproval>> GetForVersionAsync(int departmentId, int protocolVersionId);

		Task<List<ProtocolVersionApproval>> GetForVersionsAsync(int departmentId, IEnumerable<int> protocolVersionIds);

		/// <summary>The department's open (pending) steps.</summary>
		Task<List<ProtocolVersionApproval>> GetPendingForDepartmentAsync(int departmentId);

		/// <summary>Records a decision only while the step is still pending (two approvers deciding at once: one wins).</summary>
		Task<bool> TryDecideAsync(ProtocolVersionApproval approval, CancellationToken cancellationToken = default);

		/// <summary>Marks a submission's still-pending steps withdrawn (a rejection or a withdrawal sent the version back).</summary>
		Task<int> WithdrawPendingAsync(int departmentId, int protocolVersionId, int round, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface IProtocolAudiencesRepository : IRepository<ProtocolAudience>
	{
		Task<List<ProtocolAudience>> GetForProtocolAsync(int departmentId, int protocolId);

		Task<List<ProtocolAudience>> GetForDepartmentAsync(int departmentId);

		/// <summary>Replaces the protocol's rules in one transaction.</summary>
		Task ReplaceForProtocolAsync(int departmentId, int protocolId, IReadOnlyCollection<ProtocolAudience> rows, CancellationToken cancellationToken = default);
	}

	public interface IProtocolCommentsRepository : IRepository<ProtocolComment>
	{
		Task<List<ProtocolComment>> GetForProtocolAsync(int departmentId, int protocolId);

		Task<ProtocolComment> GetByIdForDepartmentAsync(int departmentId, int protocolCommentId);

		/// <summary>Open (unresolved) top-level review comments on a version.</summary>
		Task<int> CountOpenReviewCommentsAsync(int departmentId, int protocolVersionId);
	}

	/// <summary>A call that cites a protocol (the detail page's Usage tab).</summary>
	public sealed class ProtocolCallUsage
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string Name { get; set; }
		public DateTime LoggedOn { get; set; }
		public int? ProtocolVersionId { get; set; }
		public DateTime? RemovedOn { get; set; }
	}

	/// <summary>A protocol whose review reminder is due (worker 84).</summary>
	public sealed class ProtocolReviewDue
	{
		public int DispatchProtocolId { get; set; }
		public int DepartmentId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public string OwnerUserId { get; set; }
		public DateTime NextReviewOn { get; set; }
		public int ReviewReminderStage { get; set; }
	}

	/// <summary>Library queries on DispatchProtocols that the generic repository does not have (P1).</summary>
	public interface IProtocolLibraryRepository
	{
		/// <summary>Bumps the protocol's RowVersion from <paramref name="expected"/>; false when another save changed it first.</summary>
		Task<bool> TryBumpRowVersionAsync(int departmentId, int protocolId, long expected, CancellationToken cancellationToken = default);

		/// <summary>
		/// Published, live protocols whose next review is within 30 days (or past) and whose reminder for that stage has not been
		/// sent, across departments. Only departments with due work are ever read (no sweep).
		/// </summary>
		Task<List<ProtocolReviewDue>> GetReviewRemindersDueAsync(DateTime utcNow, int take);

		/// <summary>Records the reminder stage sent, only if NextReviewOn is still the one the reminder was for.</summary>
		Task<bool> MarkReviewReminderAsync(int departmentId, int protocolId, DateTime nextReviewOn, int stage, CancellationToken cancellationToken = default);

		/// <summary>Departments with a Protocol Library Settings row (setting 134) that have superseded or retired versions.</summary>
		Task<List<int>> GetDepartmentsWithRetentionWorkAsync(int take);

		/// <summary>The most recent calls citing the protocol.</summary>
		Task<List<ProtocolCallUsage>> GetRecentCallsAsync(int departmentId, int protocolId, int take);

		/// <summary>Calls by protocol version for the department (versions that are cited stay forever).</summary>
		Task<Dictionary<int, int>> GetCallCountsByVersionAsync(int departmentId, int protocolId);
	}
}
