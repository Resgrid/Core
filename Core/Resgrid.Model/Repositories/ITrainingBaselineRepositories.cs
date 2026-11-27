using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Quiz attempts (R-T7). Every read starts at the department.</summary>
	public interface ITrainingQuizAttemptsRepository : IRepository<TrainingQuizAttempt>
	{
		Task<List<TrainingQuizAttempt>> GetForTrainingAsync(int departmentId, int trainingId);

		Task<List<TrainingQuizAttempt>> GetForMemberAsync(int departmentId, int trainingId, string userId);

		Task<List<TrainingQuizAttempt>> GetAllForMemberAsync(int departmentId, string userId);

		/// <summary>The highest attempt number the member has, across cycles (0 when none).</summary>
		Task<int> GetLastAttemptNumberAsync(int departmentId, int trainingId, string userId);
	}

	/// <summary>Dynamic assignment rules of trainings (T-D20).</summary>
	public interface ITrainingAudiencesRepository : IRepository<TrainingAudience>
	{
		Task<List<TrainingAudience>> GetForTrainingAsync(int departmentId, int trainingId);

		/// <summary>Rules of every training of the department that is not archived and not closed to new assignees.</summary>
		Task<List<TrainingAudience>> GetForOpenTrainingsAsync(int departmentId);

		/// <summary>Replaces a training's rules in one transaction.</summary>
		Task ReplaceForTrainingAsync(int departmentId, int trainingId, IReadOnlyCollection<TrainingAudience> rows, CancellationToken cancellationToken = default);
	}

	/// <summary>Completions moved off live assignment rows (repeat, material edit, reset).</summary>
	public interface ITrainingCompletionHistoriesRepository : IRepository<TrainingCompletionHistory>
	{
		Task<List<TrainingCompletionHistory>> GetForTrainingAsync(int departmentId, int trainingId);

		Task<List<TrainingCompletionHistory>> GetForMemberAsync(int departmentId, string userId);
	}

	/// <summary>"Flag this question" feedback.</summary>
	public interface ITrainingQuestionFlagsRepository : IRepository<TrainingQuestionFlag>
	{
		Task<List<TrainingQuestionFlag>> GetForTrainingAsync(int departmentId, int trainingId, bool includeResolved);

		Task<bool> ResolveAsync(int departmentId, int trainingQuestionFlagId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> DeleteForQuestionsAsync(int departmentId, int trainingId, IEnumerable<int> questionIds, CancellationToken cancellationToken = default);
	}

	/// <summary>The membership-change queue worker 83 drains.</summary>
	public interface IContentAudienceChangesRepository : IRepository<ContentAudienceChange>
	{
		/// <summary>The oldest unprocessed changes, at most <paramref name="take"/>.</summary>
		Task<List<ContentAudienceChange>> GetPendingAsync(int take);

		Task<int> MarkProcessedAsync(IEnumerable<int> ids, DateTime utcNow, CancellationToken cancellationToken = default);

		Task<int> PurgeProcessedAsync(DateTime processedBeforeUtc, CancellationToken cancellationToken = default);
	}

	/// <summary>Member leave of absence periods (R-C2).</summary>
	public interface IMemberLeavePeriodsRepository : IRepository<MemberLeavePeriod>
	{
		Task<List<MemberLeavePeriod>> GetForDepartmentAsync(int departmentId);

		Task<List<MemberLeavePeriod>> GetForMemberAsync(int departmentId, string userId);

		Task<bool> DeleteForDepartmentAsync(int departmentId, int memberLeavePeriodId, CancellationToken cancellationToken = default);
	}

	/// <summary>Items members pinned to My Training.</summary>
	public interface IMemberPinnedItemsRepository : IRepository<MemberPinnedItem>
	{
		Task<List<MemberPinnedItem>> GetForMemberAsync(int departmentId, string userId);

		Task<int> DeleteForMemberAsync(int departmentId, string userId, int kind, string refId, CancellationToken cancellationToken = default);
	}
}
