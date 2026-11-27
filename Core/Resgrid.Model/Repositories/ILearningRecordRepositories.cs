using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>The learning-record ledger (§12.2). Every read starts at the department; list reads never load evidence bytes.</summary>
	public interface ILearningRecordsRepository : IRepository<LearningRecord>
	{
		Task<LearningRecord> GetByIdForDepartmentAsync(int departmentId, int learningRecordId);

		Task<List<LearningRecord>> GetByIdsForDepartmentAsync(int departmentId, IEnumerable<int> learningRecordIds);

		/// <summary>Every record of one source instance (all members and components, voided ones included), oldest first.</summary>
		Task<List<LearningRecord>> GetBySourceAsync(int departmentId, int sourceKind, string sourceRefId);

		/// <summary>The source ref ids among <paramref name="sourceRefIds"/> that already have a record that is not voided (backfill skip).</summary>
		Task<HashSet<string>> GetExistingSourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds);

		/// <summary>The records (not voided) of the listed source instances of one kind.</summary>
		Task<List<LearningRecord>> GetBySourceRefIdsAsync(int departmentId, int sourceKind, IEnumerable<string> sourceRefIds);

		Task<List<LearningRecord>> GetBySessionKeyAsync(int departmentId, string sessionKey);

		Task<List<LearningRecord>> QueryAsync(LearningRecordQuery query);

		Task<int> CountAsync(LearningRecordQuery query);

		/// <summary>The member's records (not voided) whose CompletedOn falls in the window, for the possible-duplicate check.</summary>
		Task<List<LearningRecord>> GetForMemberInWindowAsync(int departmentId, string userId, DateTime fromUtc, DateTime toUtc);

		/// <summary>
		/// Inserts the record unless a record that is not voided already holds its idempotency key
		/// (DepartmentId, SourceKind, SourceRefId, UserId, ComponentKey); returns the stored row and whether it was inserted.
		/// A concurrent insert that loses the unique index race returns the winner.
		/// </summary>
		Task<(LearningRecord Record, bool Inserted)> InsertIfAbsentAsync(LearningRecord record, CancellationToken cancellationToken = default);

		/// <summary>Marks a record voided (only from Pending or Verified); returns false when it was already voided or not found.</summary>
		Task<bool> VoidAsync(int departmentId, int learningRecordId, string reason, string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Writes the named columns of an existing record (status changes, review, duplicate resolution, category).</summary>
		Task<int> UpdateColumnsAsync(LearningRecord record, IReadOnlyCollection<string> columns, CancellationToken cancellationToken = default);

		/// <summary>Sets the category of the listed records that are not voided; returns the count changed.</summary>
		Task<int> SetCategoryAsync(int departmentId, IEnumerable<int> learningRecordIds, int? categoryId, CancellationToken cancellationToken = default);

		/// <summary>Uncategorized records (not voided) grouped by title and course code, in the window.</summary>
		Task<List<LearningRecordGroup>> GetUncategorizedGroupsAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);

		/// <summary>Records (not voided) with an unresolved possible duplicate in the window.</summary>
		Task<int> CountUnresolvedDuplicatesAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc);

		/// <summary>LMS: a record by its certificate verification code (any department), or null.</summary>
		Task<LearningRecord> GetByVerificationCodeAsync(string verificationCode);

		/// <summary>Round 9B (worker 85 digest): departments with member submissions waiting for a reviewer, at most <paramref name="take"/>.</summary>
		Task<List<int>> GetDepartmentIdsAwaitingReviewAsync(int take);
	}

	/// <summary>Evidence files of external submissions.</summary>
	public interface ILearningRecordFilesRepository : IRepository<LearningRecordFile>
	{
		/// <summary>The file with its bytes, or null when it is not the department's.</summary>
		Task<LearningRecordFile> GetWithDataAsync(int departmentId, int learningRecordFileId);

		/// <summary>Round 9B: the evidence of these submissions without the bytes (linked rows, and the T1 single file by its id), oldest first.</summary>
		Task<List<LearningRecordFile>> GetForRecordsAsync(int departmentId, IEnumerable<int> learningRecordIds);

		/// <summary>Round 9B: a member's evidence files without the bytes (GDPR export).</summary>
		Task<List<LearningRecordFile>> GetForUserAsync(int departmentId, string userId);

		/// <summary>Round 9B: links uploaded files (still unlinked, the department's) to their submission.</summary>
		Task<int> LinkToRecordAsync(int departmentId, IEnumerable<int> learningRecordFileIds, int learningRecordId, CancellationToken cancellationToken = default);
	}

	/// <summary>Round 9B: the review history of member submissions (M0409).</summary>
	public interface ILearningRecordReviewEventsRepository : IRepository<LearningRecordReviewEvent>
	{
		/// <summary>One submission's steps, oldest first.</summary>
		Task<List<LearningRecordReviewEvent>> GetForRecordAsync(int departmentId, int learningRecordId);

		/// <summary>A member's steps on their submissions (GDPR export), oldest first.</summary>
		Task<List<LearningRecordReviewEvent>> GetForUserAsync(int departmentId, string userId);
	}

	/// <summary>Round 9B: the daily reviewer digest claims (M0410).</summary>
	public interface ITrainingReviewDigestsRepository : IRepository<TrainingReviewDigest>
	{
		/// <summary>Claims the department's digest for a local day: true for the first caller, false when it was already sent (or claimed).</summary>
		Task<bool> TryClaimAsync(int departmentId, DateTime digestDate, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Records how many reviewers and submissions the claimed digest covered.</summary>
		Task<int> SaveCountsAsync(int departmentId, DateTime digestDate, int reviewers, int submissions, CancellationToken cancellationToken = default);
	}

	public interface ITrainingCategoriesRepository : IRepository<TrainingCategory>
	{
		Task<List<TrainingCategory>> GetForDepartmentAsync(int departmentId, bool includeDeleted);
	}

	public interface ICategoryCreditMappingsRepository : IRepository<CategoryCreditMapping>
	{
		Task<List<CategoryCreditMapping>> GetForDepartmentAsync(int departmentId);

		Task<List<CategoryCreditMapping>> GetForCertificationTypeAsync(int departmentId, int departmentCertificationTypeId);
	}

	public interface ILearningRecordBackfillsRepository : IRepository<LearningRecordBackfill>
	{
		Task<LearningRecordBackfill> GetLatestForDepartmentAsync(int departmentId);

		/// <summary>Queued jobs, and running ones whose worker stopped (started before <paramref name="staleBeforeUtc"/>), oldest first.</summary>
		Task<List<LearningRecordBackfill>> GetRunnableAsync(DateTime staleBeforeUtc, int take);

		/// <summary>Claims a runnable job for this run (state Running, StartedOn now); false when another worker took it.</summary>
		Task<bool> ClaimAsync(int learningRecordBackfillId, int expectedState, DateTime? expectedStartedOn, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> SaveProgressAsync(LearningRecordBackfill backfill, CancellationToken cancellationToken = default);
	}

	/// <summary>LMS (dark): custom certificate templates.</summary>
	public interface ILmsCertificateTemplatesRepository : IRepository<LmsCertificateTemplate>
	{
		Task<List<LmsCertificateTemplate>> GetForDepartmentAsync(int departmentId);
	}
}
