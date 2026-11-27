using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	// Training T4 part 2 (round 5C, M0381-M0384): skill sheets, evaluations, task books and the skill notification digest.

	public interface ILmsSkillSheetsRepository : IRepository<LmsSkillSheet>
	{
		Task<List<LmsSkillSheet>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		Task<LmsSkillSheet> GetByIdForDepartmentAsync(int departmentId, int lmsSkillSheetId);

		Task<List<LmsSkillSheet>> GetByIdsAsync(int departmentId, IEnumerable<int> lmsSkillSheetIds);

		/// <summary>Writes the row when its RowVersion is still <paramref name="expectedRowVersion"/> (then bumps it); false when it moved on.</summary>
		Task<bool> TryUpdateAsync(LmsSkillSheet sheet, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsSkillSheetVersionsRepository : IRepository<LmsSkillSheetVersion>
	{
		Task<List<LmsSkillSheetVersion>> GetForSheetAsync(int departmentId, int lmsSkillSheetId);

		Task<LmsSkillSheetVersion> GetByIdForDepartmentAsync(int departmentId, int lmsSkillSheetVersionId);

		Task<bool> TryUpdateAsync(LmsSkillSheetVersion version, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsSkillEvaluationsRepository : IRepository<LmsSkillEvaluation>
	{
		Task<LmsSkillEvaluation> GetByIdForDepartmentAsync(int departmentId, int lmsSkillEvaluationId);

		Task<LmsSkillEvaluation> GetByIdempotencyKeyAsync(int departmentId, string idempotencyKey);

		/// <summary>Evaluations in the department, newest first (list columns only: no answers).</summary>
		Task<List<LmsSkillEvaluation>> GetForDepartmentAsync(int departmentId, DateTime? fromUtc, DateTime? toUtc, int? lmsSkillSheetId, int? status, int take);

		/// <summary>Open (in progress or awaiting signature) evaluations the member evaluates.</summary>
		Task<List<LmsSkillEvaluation>> GetOpenForEvaluatorAsync(int departmentId, string evaluatorUserId);

		/// <summary>Evaluations a member was evaluated on (included), newest first.</summary>
		Task<List<LmsSkillEvaluation>> GetForMemberAsync(int departmentId, string userId, int? lmsSkillSheetId);

		Task<List<LmsSkillEvaluation>> GetForTaskBookAsync(int departmentId, int lmsTaskBookId);

		Task<List<LmsSkillEvaluation>> GetForClassSessionAsync(int departmentId, string classSessionRef);

		Task<LmsSkillEvaluation> GetBySignatureRequestAsync(int departmentId, int signatureRequestId);

		/// <summary>Completed (passed or failed) evaluations of sheets in a window with their answers (the rating reports).</summary>
		Task<List<LmsSkillEvaluation>> GetCompletedForReportAsync(int departmentId, DateTime fromUtc, DateTime toUtc, int? lmsSkillSheetId);

		Task<bool> TryUpdateAsync(LmsSkillEvaluation evaluation, long expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Inserts an evaluation an idempotency key may already hold (an offline replay racing the first delivery); false on the unique index.</summary>
		Task<bool> TryInsertAsync(LmsSkillEvaluation evaluation, CancellationToken cancellationToken = default);
	}

	public interface ILmsSkillEvaluationMembersRepository : IRepository<LmsSkillEvaluationMember>
	{
		Task<List<LmsSkillEvaluationMember>> GetForEvaluationAsync(int departmentId, int lmsSkillEvaluationId);

		Task<List<LmsSkillEvaluationMember>> GetForEvaluationsAsync(int departmentId, IEnumerable<int> lmsSkillEvaluationIds);
	}

	public interface ILmsSkillEvaluationFilesRepository : IRepository<LmsSkillEvaluationFile>
	{
		/// <summary>The evidence of an evaluation without the bytes.</summary>
		Task<List<LmsSkillEvaluationFile>> GetForEvaluationAsync(int departmentId, int lmsSkillEvaluationId);

		/// <summary>One file with its bytes.</summary>
		Task<LmsSkillEvaluationFile> GetWithDataAsync(int departmentId, int lmsSkillEvaluationFileId);
	}

	public interface ILmsTaskBookDefinitionsRepository : IRepository<LmsTaskBookDefinition>
	{
		Task<List<LmsTaskBookDefinition>> GetForDepartmentAsync(int departmentId, bool includeDeleted = false);

		Task<LmsTaskBookDefinition> GetByIdForDepartmentAsync(int departmentId, int lmsTaskBookDefinitionId);

		Task<bool> TryUpdateAsync(LmsTaskBookDefinition definition, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsTaskBooksRepository : IRepository<LmsTaskBook>
	{
		Task<LmsTaskBook> GetByIdForDepartmentAsync(int departmentId, int lmsTaskBookId);

		Task<List<LmsTaskBook>> GetForDepartmentAsync(int departmentId, bool openOnly);

		Task<List<LmsTaskBook>> GetForMemberAsync(int departmentId, string userId);

		Task<LmsTaskBook> GetBySignatureRequestAsync(int departmentId, int signatureRequestId);

		/// <summary>Worker 96's due-work query: open task books past their validity, across departments, oldest first.</summary>
		Task<List<LmsTaskBook>> GetExpiredOpenAsync(DateTime utcNow, int take);

		Task<bool> TryUpdateAsync(LmsTaskBook book, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	public interface ILmsSkillNotificationsRepository : IRepository<LmsSkillNotification>
	{
		/// <summary>Worker 96's due-work query (no department sweep): unsent notices due by now, unclaimed or with a stale claim.</summary>
		Task<List<LmsSkillNotification>> GetDueAsync(DateTime utcNow, DateTime staleClaimBefore, int take);

		/// <summary>Claims a notice (conditional on it being unsent and unclaimed or stale); false when another worker has it.</summary>
		Task<bool> ClaimAsync(int lmsSkillNotificationId, DateTime utcNow, DateTime staleClaimBefore, CancellationToken cancellationToken = default);

		Task MarkSentAsync(IEnumerable<int> lmsSkillNotificationIds, DateTime utcNow, bool skipped, CancellationToken cancellationToken = default);

		Task<List<LmsSkillNotification>> GetForUserAsync(int departmentId, string userId, int take);
	}
}
