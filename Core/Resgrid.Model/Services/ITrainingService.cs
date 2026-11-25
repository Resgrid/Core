using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ITrainingService
	{
		/// <summary>
		/// Every training of the department with its assignees and question counts (one question query for the list, T-D22),
		/// archived ones included. Callers filter with <see cref="TrainingManagementScope"/>.
		/// </summary>
		Task<List<Training>> GetAllTrainingsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Saves a training's own row, its questions and new attachments. Assignees are never written here (T-D14): they are
		/// added, removed and updated row by row through <see cref="ITrainingAssignmentService"/>. Publishes TrainingAdded or
		/// TrainingUpdated (T-D18).
		/// </summary>
		Task<Training> SaveAsync(Training training, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// A training by id with its assignees, questions, audience rules and attachment metadata. Attachment bytes are never
		/// loaded here (T-D22); <see cref="GetTrainingAttachmentWithDataAsync"/> streams one.
		/// </summary>
		Task<Training> GetTrainingByIdAsync(int trainingId);

		/// <summary>The training row with its assignees only (no questions, attachments or audiences).</summary>
		Task<Training> GetTrainingRowByIdAsync(int trainingId);

		/// <summary>One attachment's metadata (no bytes).</summary>
		Task<TrainingAttachment> GetTrainingAttachmentByIdAsync(int trainingAttachmentId);

		/// <summary>One attachment with its bytes: the one authorized download path.</summary>
		Task<TrainingAttachment> GetTrainingAttachmentWithDataAsync(int trainingAttachmentId);

		/// <summary>Removes one attachment of the department's training (T-D15).</summary>
		Task<bool> RemoveAttachmentAsync(int departmentId, int trainingId, int trainingAttachmentId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Marks the member's assignment viewed (ViewedOn written once, T-D13); a training without a quiz completes on view.
		/// Null when the member is not assigned.
		/// </summary>
		Task<TrainingUser> SetTrainingAsViewedAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Scores and records a quiz submission on the server (R-T11): the attempt with its snapshot and revision hash (R-T6,
		/// R-T7), then the assignment's score (always, T-D1) and pass result (apart from completion, T-D2). Posted ids are
		/// validated against the training's own questions and options (T-D4, T-D11). The attempt limit is enforced.
		/// </summary>
		Task<TrainingQuizSubmission> RecordQuizAttemptAsync(int departmentId, int trainingId, string userId, IReadOnlyDictionary<int, List<int>> answers,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<TrainingQuizAttempt>> GetAttemptsForTrainingAsync(int departmentId, int trainingId);

		Task<List<TrainingQuizAttempt>> GetAttemptsForMemberAsync(int departmentId, int trainingId, string userId);

		/// <summary>Archives a training (T-D21): hidden from members and lists, kept for reports, restorable, audited.</summary>
		Task<bool> ArchiveTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> RestoreTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Deletes a training outright only when nobody completed it; otherwise refuses (the caller archives).</summary>
		Task<TrainingDeleteOutcomes> DeleteTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Copies a training (content, quiz, attachments and settings; not its assignees or audience). Audited.</summary>
		Task<Training> CopyTrainingAsync(int departmentId, int trainingId, string userId, string name = null, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The officer's report: per-assignee state, attempts, completion history and open question flags.</summary>
		Task<TrainingReport> GetTrainingReportAsync(int departmentId, int trainingId, DateTime utcNow);

		/// <summary>KPI tiles for the trainings a manager sees (overdue, due this week, completion %).</summary>
		Task<TrainingManagerSummary> GetManagerSummaryAsync(int departmentId, IEnumerable<Training> trainings, DateTime utcNow);

		/// <summary>A member flags a question as wrong; the author is told. Null when the question is not the training's.</summary>
		Task<TrainingQuestionFlag> FlagQuestionAsync(int departmentId, int trainingId, int trainingQuestionId, string userId, string comment,
			CancellationToken cancellationToken = default(CancellationToken));

		Task<List<TrainingQuestionFlag>> GetQuestionFlagsAsync(int departmentId, int trainingId, bool includeResolved);

		Task<bool> ResolveQuestionFlagAsync(int departmentId, int trainingId, int trainingQuestionFlagId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Trainings with notifier work on this run (one query over every department for the ids, then each training with
		/// its assignees). Replaces the old load-everything sweep and its 10:00 window (T-D19).
		/// </summary>
		Task<List<Training>> GetTrainingsToNotifyAsync(DateTime currentTime);

		/// <summary>
		/// Sends the notices one training owes on this run (new assignment, due reminders, overdue to the member and a digest
		/// to the creator) and stamps each assignee row. <paramref name="onlyUserIds"/> limits it to those members (an edit
		/// that just added them). Returns the number of notices sent.
		/// </summary>
		Task<int> SendTrainingNoticesAsync(Training training, DateTime utcNow, IReadOnlyCollection<string> onlyUserIds = null,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Stamps the training row's Notified (first announcement done). Writes the row alone.</summary>
		Task<Training> MarkAsNotifiedAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>The member's assignment rows, each with its training row (the GDPR export names the training, T-D23).</summary>
		Task<List<TrainingUser>> GetTrainingUsersForUserAsync(string userId);

		/// <summary>
		/// The department's trainings assigned to the member that are still outstanding: not complete, not exempt, not
		/// removed, started and not archived. Soonest due first (no due date last). Due moments come from
		/// TrainingDueDates with the member's leave applied.
		/// </summary>
		Task<List<TrainingAssignmentStatus>> GetOutstandingTrainingsForUserAsync(int departmentId, string userId);

		/// <summary>Every assignment of the member in the department with its state (My Training, the v4 API).</summary>
		Task<List<TrainingAssignmentStatus>> GetAssignmentsForUserAsync(int departmentId, string userId, DateTime utcNow);
	}
}
