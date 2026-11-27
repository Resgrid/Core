using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	/// <summary>Per-protocol LMS learning settings (LmsProtocolLearnings, M0387), unique per department and protocol.</summary>
	public interface ILmsProtocolLearningsRepository : IRepository<LmsProtocolLearning>
	{
		Task<LmsProtocolLearning> GetForProtocolAsync(int departmentId, int dispatchProtocolId);

		Task<List<LmsProtocolLearning>> GetForDepartmentAsync(int departmentId);

		/// <summary>Worker 86's due-work query: departments with a micro-learning protocol whose weekly clock has run out (or never ran).</summary>
		Task<List<int>> GetMicroLearningDueDepartmentIdsAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default);
	}

	/// <summary>Protocol acknowledgment quiz attempts (LmsProtocolQuizAttempts, M0387).</summary>
	public interface ILmsProtocolQuizAttemptsRepository : IRepository<LmsProtocolQuizAttempt>
	{
		Task<LmsProtocolQuizAttempt> GetByIdForDepartmentAsync(int departmentId, int lmsProtocolQuizAttemptId);

		/// <summary>The member's attempts on one version, newest first.</summary>
		Task<List<LmsProtocolQuizAttempt>> GetForUserVersionAsync(int departmentId, string userId, int protocolVersionId);

		/// <summary>Every attempt of the member in the department (the GDPR export), newest first.</summary>
		Task<List<LmsProtocolQuizAttempt>> GetForUserAsync(int departmentId, string userId);

		/// <summary>Writes the attempt when its stored RowVersion is still <paramref name="expectedRowVersion"/>.</summary>
		Task<bool> TryUpdateAsync(LmsProtocolQuizAttempt attempt, long expectedRowVersion, CancellationToken cancellationToken = default);
	}

	/// <summary>Weekly micro-learning questions and the per-department clock (LmsMicroLearningItems, LmsMicroLearningRuns; M0387).</summary>
	public interface ILmsMicroLearningRepository : IRepository<LmsMicroLearningItem>
	{
		/// <summary>
		/// Claims the department's weekly run: moves its clock to <paramref name="nextRunUtc"/> only while it is still due (or creates it).
		/// False when another worker claimed it first.
		/// </summary>
		Task<bool> TryClaimRunAsync(int departmentId, DateTime utcNow, DateTime nextRunUtc, string weekKey, CancellationToken cancellationToken = default);

		Task<LmsMicroLearningRun> GetRunAsync(int departmentId);

		/// <summary>Inserts a member's question for the week; false when the member already has one for that week (unique index).</summary>
		Task<bool> TryInsertItemAsync(LmsMicroLearningItem item, CancellationToken cancellationToken = default);

		Task<LmsMicroLearningItem> GetItemAsync(int departmentId, int lmsMicroLearningItemId);

		/// <summary>The member's questions, newest first (<paramref name="take"/> at most; 0 = all, for the GDPR export).</summary>
		Task<List<LmsMicroLearningItem>> GetForUserAsync(int departmentId, string userId, int take);

		/// <summary>The question ids the department's members were asked since <paramref name="sinceUtc"/>, by member (no repeats while others remain).</summary>
		Task<List<LmsMicroLearningItem>> GetRecentForDepartmentAsync(int departmentId, DateTime sinceUtc);

		/// <summary>Records the answer only when the item is still unanswered; false when it was answered already.</summary>
		Task<bool> TryAnswerAsync(LmsMicroLearningItem item, CancellationToken cancellationToken = default);
	}
}
