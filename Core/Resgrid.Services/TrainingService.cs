using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Baseline trainings (protocols-training-knowledge-plan §13 T0): content, the server-scored quiz with attempt history,
	/// archive / copy / delete, the officer report and the notifier. Assignment (who and when) is ITrainingAssignmentService.
	/// </summary>
	public class TrainingService : ITrainingService
	{
		/// <summary>The widest reminder lead (days) the notifier looks ahead for.</summary>
		public const int MaxReminderLeadDays = TrainingDueDates.MaxReminderLeadDays;

		public const string QuestionFlaggedTitle = "Training Question Flagged";

		private readonly ITrainingRepository _trainingRepository;
		private readonly ITrainingAttachmentRepository _trainingAttachmentRepository;
		private readonly ITrainingQuestionRepository _trainingQuestionRepository;
		private readonly ITrainingUserRepository _trainingUserRepository;
		private readonly ICommunicationService _communicationService;
		private readonly IDepartmentsService _departmentService;
		private readonly IDepartmentSettingsService _departmentSettingsService;
		private readonly Lazy<ISearchProjectionService> _searchProjections;
		private readonly ITrainingQuizAttemptsRepository _attemptsRepository;
		private readonly ITrainingAudiencesRepository _audiencesRepository;
		private readonly ITrainingCompletionHistoriesRepository _historiesRepository;
		private readonly ITrainingQuestionFlagsRepository _flagsRepository;
		private readonly IMemberLeaveService _leaveService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentGroupsService _departmentGroupsService;

		public TrainingService(ITrainingRepository trainingRepository, ITrainingAttachmentRepository trainingAttachmentRepository,
			ITrainingUserRepository trainingUserRepository, ITrainingQuestionRepository trainingQuestionRepository, ICommunicationService communicationService, IDepartmentsService departmentService,
			IDepartmentSettingsService departmentSettingsService, Lazy<ISearchProjectionService> searchProjections = null, ITrainingQuizAttemptsRepository attemptsRepository = null,
			ITrainingAudiencesRepository audiencesRepository = null, ITrainingCompletionHistoriesRepository historiesRepository = null, ITrainingQuestionFlagsRepository flagsRepository = null,
			IMemberLeaveService leaveService = null, IEventAggregator eventAggregator = null, IDepartmentGroupsService departmentGroupsService = null)
		{
			_searchProjections = searchProjections;
			_trainingRepository = trainingRepository;
			_trainingAttachmentRepository = trainingAttachmentRepository;
			_trainingUserRepository = trainingUserRepository;
			_trainingQuestionRepository = trainingQuestionRepository;
			_communicationService = communicationService;
			_departmentService = departmentService;
			_departmentSettingsService = departmentSettingsService;
			_attemptsRepository = attemptsRepository;
			_audiencesRepository = audiencesRepository;
			_historiesRepository = historiesRepository;
			_flagsRepository = flagsRepository;
			_leaveService = leaveService;
			_eventAggregator = eventAggregator;
			_departmentGroupsService = departmentGroupsService;
		}

		#region Reads

		public async Task<List<Training>> GetAllTrainingsForDepartmentAsync(int departmentId)
		{
			var list = (await _trainingRepository.GetTrainingsByDepartmentIdAsync(departmentId))?.ToList() ?? new List<Training>();

			// One query for every training's question count, not one question read per training (T-D22).
			var counts = await _trainingRepository.GetQuestionCountsForDepartmentAsync(departmentId) ?? new Dictionary<int, int>();
			foreach (var item in list)
			{
				item.QuestionCount = counts.TryGetValue(item.TrainingId, out var count) ? count : 0;
				item.Users ??= new List<TrainingUser>();
			}

			return list;
		}

		public async Task<Training> GetTrainingByIdAsync(int trainingId)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);

			if (training == null)
				return null;

			training.Users ??= new List<TrainingUser>();
			training.Questions = (await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(training.TrainingId) ?? Enumerable.Empty<TrainingQuestion>()).ToList();
			training.QuestionCount = training.Questions.Count;
			// Metadata only: the bytes stream from the one download endpoint (T-D22).
			training.Attachments = await _trainingAttachmentRepository.GetAttachmentMetadataByTrainingIdAsync(trainingId) ?? new List<TrainingAttachment>();
			training.Audiences = _audiencesRepository == null ? new List<TrainingAudience>() : await _audiencesRepository.GetForTrainingAsync(training.DepartmentId, trainingId) ?? new List<TrainingAudience>();

			return training;
		}

		public async Task<Training> GetTrainingRowByIdAsync(int trainingId)
		{
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
			if (training != null)
				training.Users ??= new List<TrainingUser>();
			return training;
		}

		public Task<TrainingAttachment> GetTrainingAttachmentByIdAsync(int trainingAttachmentId)
		{
			return _trainingAttachmentRepository.GetAttachmentMetadataByIdAsync(trainingAttachmentId);
		}

		public Task<TrainingAttachment> GetTrainingAttachmentWithDataAsync(int trainingAttachmentId)
		{
			return _trainingAttachmentRepository.GetAttachmentWithDataAsync(trainingAttachmentId);
		}

		#endregion Reads

		#region Save, archive, copy, delete

		public async Task<Training> SaveAsync(Training training, CancellationToken cancellationToken = default(CancellationToken))
		{
			var isNew = training.TrainingId <= 0;
			var now = DateTime.UtcNow;

			// Rich text keeps the allow-listed embeds (YouTube, Vimeo, Google Docs/Sheets, Microsoft Stream/Teams), no other frame.
			training.Description = StringHelpers.SanitizeHtmlWithEmbedsInString(training.Description);
			training.TrainingText = StringHelpers.SanitizeHtmlWithEmbedsInString(training.TrainingText);

			// Server-side bounds (T-D23): the browser's range check is not a guarantee.
			if (double.IsNaN(training.MinimumScore) || training.MinimumScore < 0)
				training.MinimumScore = 0;
			if (training.MinimumScore > 100)
				training.MinimumScore = 100;
			if (training.DueDays.HasValue && training.DueDays.Value <= 0)
				training.DueDays = null;
			if (training.RepeatMonths.HasValue && training.RepeatMonths.Value <= 0)
				training.RepeatMonths = null;
			if (training.QuizAttemptLimit.HasValue && training.QuizAttemptLimit.Value <= 0)
				training.QuizAttemptLimit = null;
			if (training.OverdueRepeatDays.HasValue && training.OverdueRepeatDays.Value <= 0)
				training.OverdueRepeatDays = null;
			training.ReminderLeadDaysCsv = TrainingDueDates.NormalizeLeadDaysCsv(training.ReminderLeadDaysCsv);
			if (!training.OpenToAllMembers)
				training.AllowSelfEnroll = false;

			if (isNew && training.CreatedOn == default)
				training.CreatedOn = now;
			if (!isNew)
				training.UpdatedOn = now;

			// One simple repeat: the first run is one interval after the start (or creation); clearing the interval stops it.
			if (!training.RepeatMonths.HasValue)
				training.NextRepeatOn = null;
			else if (!training.NextRepeatOn.HasValue)
				training.NextRepeatOn = TrainingDueDates.NextRepeatOn(training.RepeatMonths, training.StartsOn ?? (isNew ? now : training.CreatedOn));

			// Questions are saved separately from the training row, so on an update reconcile them with what is stored
			// before writing anything (#180): posted questions that carry their id are updated in place, removed ones are
			// deleted. Re-adding every question as a new row duplicated the quiz on each edit and inflated the score divisor.
			List<TrainingQuestion> removedQuestions = null;
			if (!isNew && training.Questions != null)
				removedQuestions = await ReconcileQuestionsAsync(training);

			var questions = training.Questions?.ToList();
			var newAttachments = training.Attachments?.Where(a => a != null && a.TrainingAttachmentId <= 0 && a.Data != null).ToList();
			var users = training.Users;
			var audiences = training.Audiences;

			// The training row alone (firstLevelOnly): its assignees are written row by row elsewhere (T-D14), so a quiz result
			// that lands during an edit is never overwritten by the copy read at the start of the request.
			var saved = await _trainingRepository.SaveOrUpdateAsync(training, cancellationToken, true);

			if (removedQuestions != null && removedQuestions.Count > 0)
			{
				// TrainingQuestionAnswers cascade on delete. Attempts keep their own snapshot, so nothing else references these ids.
				foreach (var removedQuestion in removedQuestions)
					await _trainingQuestionRepository.DeleteAsync(removedQuestion, cancellationToken);

				if (_flagsRepository != null)
					await _flagsRepository.DeleteForQuestionsAsync(saved.DepartmentId, saved.TrainingId, removedQuestions.Select(q => q.TrainingQuestionId), cancellationToken);
			}

			if (questions != null && questions.Any())
			{
				for (int i = 0; i < questions.Count; i++)
				{
					questions[i].TrainingId = saved.TrainingId;
					if (!Enum.IsDefined(typeof(TrainingQuestionTypes), questions[i].Type))
						questions[i].Type = (int)TrainingQuestionTypes.SingleChoice;
					questions[i] = await _trainingQuestionRepository.SaveOrUpdateAsync(questions[i], cancellationToken);
				}

				saved.Questions = questions;
			}

			if (newAttachments != null && newAttachments.Any())
			{
				foreach (var attachment in newAttachments)
				{
					attachment.TrainingId = saved.TrainingId;
					await _trainingAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken);
					attachment.Data = null;
				}
			}

			saved.Users = users;
			saved.Audiences = audiences;

			// T-D18: the workflow triggers fire. The payload carries the training's own fields, not its assignees or bytes.
			var payload = EventPayload(saved);
			if (isNew)
				_eventAggregator?.SendMessage(new TrainingAddedEvent { DepartmentId = saved.DepartmentId, Training = payload });
			else
				_eventAggregator?.SendMessage(new TrainingUpdatedEvent { DepartmentId = saved.DepartmentId, Training = payload });

			if (_searchProjections != null) await _searchProjections.Value.ProjectTrainingAsync(saved, cancellationToken);
			return saved;
		}

		/// <summary>The training's own fields for workflow payloads: no assignees, questions (answer key) or attachment bytes.</summary>
		public static Training EventPayload(Training training)
		{
			if (training == null)
				return null;

			return new Training
			{
				TrainingId = training.TrainingId,
				DepartmentId = training.DepartmentId,
				CreatedByUserId = training.CreatedByUserId,
				Name = training.Name,
				Description = training.Description,
				TrainingText = training.TrainingText,
				MinimumScore = training.MinimumScore,
				CreatedOn = training.CreatedOn,
				ToBeCompletedBy = training.ToBeCompletedBy,
				Notified = training.Notified,
				OpenToAllMembers = training.OpenToAllMembers,
				AllowSelfEnroll = training.AllowSelfEnroll,
				StartsOn = training.StartsOn,
				DueDays = training.DueDays,
				RepeatMonths = training.RepeatMonths,
				Hours = training.Hours,
				CeHours = training.CeHours,
				CeProvider = training.CeProvider,
				CeCourseNumber = training.CeCourseNumber,
				CategoryId = training.CategoryId,
				UpdatedOn = training.UpdatedOn,
				UpdatedByUserId = training.UpdatedByUserId
			};
		}

		/// <summary>
		/// Matches the questions on an existing training against the stored ones and returns the stored questions that are
		/// no longer present (to delete). Question/answer ids that do not belong to this training (or question) are cleared
		/// so they are inserted as new rows instead of overwriting someone else's. The repository only removes an updated
		/// question's dropped answers when at least one stored answer id survives, so a question whose answers were all
		/// removed or replaced is recreated rather than updated.
		/// </summary>
		private async Task<List<TrainingQuestion>> ReconcileQuestionsAsync(Training training)
		{
			var storedQuestions = (await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(training.TrainingId))?.ToList();

			// The question read swallows errors and returns null; saving without it would duplicate every question again.
			if (storedQuestions == null)
				throw new InvalidOperationException($"Unable to load the existing questions for training {training.TrainingId}.");

			var keptQuestionIds = new HashSet<int>();

			foreach (var question in training.Questions)
			{
				var storedQuestion = question.TrainingQuestionId > 0 && !keptQuestionIds.Contains(question.TrainingQuestionId)
					? storedQuestions.FirstOrDefault(x => x.TrainingQuestionId == question.TrainingQuestionId)
					: null;

				if (storedQuestion == null)
				{
					ClearQuestionIds(question);
					continue;
				}

				var storedAnswerIds = new HashSet<int>(storedQuestion.Answers?.Select(x => x.TrainingQuestionAnswerId) ?? Enumerable.Empty<int>());
				var keptAnswerIds = new HashSet<int>();

				if (question.Answers != null)
				{
					foreach (var answer in question.Answers)
					{
						if (answer.TrainingQuestionAnswerId > 0 && storedAnswerIds.Contains(answer.TrainingQuestionAnswerId) && keptAnswerIds.Add(answer.TrainingQuestionAnswerId))
							continue;

						answer.TrainingQuestionAnswerId = 0;
					}
				}

				if (storedAnswerIds.Count > 0 && keptAnswerIds.Count == 0)
				{
					ClearQuestionIds(question);
					continue;
				}

				keptQuestionIds.Add(question.TrainingQuestionId);
			}

			return storedQuestions.Where(x => !keptQuestionIds.Contains(x.TrainingQuestionId)).ToList();
		}

		private static void ClearQuestionIds(TrainingQuestion question)
		{
			question.TrainingQuestionId = 0;

			if (question.Answers != null)
			{
				foreach (var answer in question.Answers)
				{
					answer.TrainingQuestionAnswerId = 0;
					answer.TrainingQuestionId = 0;
				}
			}
		}

		public async Task<bool> RemoveAttachmentAsync(int departmentId, int trainingId, int trainingAttachmentId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var attachment = await _trainingAttachmentRepository.GetAttachmentMetadataByIdAsync(trainingAttachmentId);
			if (attachment == null || attachment.TrainingId != trainingId)
				return false;

			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return false;

			return await _trainingAttachmentRepository.DeleteAsync(attachment, cancellationToken);
		}

		public async Task<bool> ArchiveTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingRowByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return false;
			if (training.IsArchived)
				return true;

			var now = DateTime.UtcNow;
			if (!await _trainingRepository.SetArchivedAsync(departmentId, trainingId, now, userId, cancellationToken))
				return false;

			training.ArchivedOn = now;
			training.ArchivedByUserId = userId;
			Audit(departmentId, userId, AuditLogTypes.TrainingArchived, null, JsonConvert.SerializeObject(new { training.TrainingId, training.Name, ArchivedOn = now }));
			if (_searchProjections != null) await _searchProjections.Value.ProjectTrainingAsync(training, cancellationToken);
			return true;
		}

		public async Task<bool> RestoreTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingRowByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return false;
			if (!training.IsArchived)
				return true;

			if (!await _trainingRepository.SetArchivedAsync(departmentId, trainingId, null, userId, cancellationToken))
				return false;

			var archivedOn = training.ArchivedOn;
			training.ArchivedOn = null;
			training.ArchivedByUserId = null;
			Audit(departmentId, userId, AuditLogTypes.TrainingRestored, JsonConvert.SerializeObject(new { training.TrainingId, training.Name, ArchivedOn = archivedOn }), null);
			if (_searchProjections != null) await _searchProjections.Value.ProjectTrainingAsync(training, cancellationToken);
			return true;
		}

		public async Task<TrainingDeleteOutcomes> DeleteTrainingAsync(int departmentId, int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingRowByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return TrainingDeleteOutcomes.NotFound;

			// T-D21: completion history is never destroyed by one click. A training somebody completed is archived instead.
			if (await _trainingRepository.CountCompletionsAsync(trainingId) > 0)
				return TrainingDeleteOutcomes.HasCompletions;

			// Assignees, questions, answers, attachments, attempts, audiences and flags cascade from the training row.
			training.Users = null;
			if (!await _trainingRepository.DeleteAsync(training, cancellationToken))
				return TrainingDeleteOutcomes.NotFound;

			Audit(departmentId, userId, AuditLogTypes.TrainingDeleted, JsonConvert.SerializeObject(new { training.TrainingId, training.Name, training.CreatedByUserId, training.CreatedOn }), null);
			if (_searchProjections != null)
				await _searchProjections.Value.RemoveAsync(training.DepartmentId, SearchEntityTypes.Training, training.TrainingId.ToString(), cancellationToken);
			return TrainingDeleteOutcomes.Deleted;
		}

		public async Task<Training> CopyTrainingAsync(int departmentId, int trainingId, string userId, string name = null, CancellationToken cancellationToken = default(CancellationToken))
		{
			var source = await GetTrainingByIdAsync(trainingId);
			if (source == null || source.DepartmentId != departmentId)
				return null;

			var now = DateTime.UtcNow;
			var copy = new Training
			{
				DepartmentId = departmentId,
				CreatedByUserId = userId,
				CreatedOn = now,
				Name = string.IsNullOrWhiteSpace(name) ? source.Name : name.Trim(),
				Description = source.Description,
				TrainingText = source.TrainingText,
				MinimumScore = source.MinimumScore,
				ToBeCompletedBy = source.ToBeCompletedBy.HasValue && source.ToBeCompletedBy.Value.Date >= now.Date ? source.ToBeCompletedBy : null,
				OpenToAllMembers = source.OpenToAllMembers,
				AllowSelfEnroll = source.AllowSelfEnroll,
				StartsOn = source.StartsOn.HasValue && source.StartsOn.Value > now ? source.StartsOn : null,
				DueDays = source.DueDays,
				ClosedToNewAssignees = false,
				RepeatMonths = source.RepeatMonths,
				QuizAttemptLimit = source.QuizAttemptLimit,
				OneQuestionPerPage = source.OneQuestionPerPage,
				ReminderLeadDaysCsv = source.ReminderLeadDaysCsv,
				SendOverdueNotices = source.SendOverdueNotices,
				OverdueRepeatDays = source.OverdueRepeatDays,
				ReminderMessage = source.ReminderMessage,
				Hours = source.Hours,
				CeHours = source.CeHours,
				CeProvider = source.CeProvider,
				CeCourseNumber = source.CeCourseNumber,
				CategoryId = source.CategoryId,
				Questions = new Collection<TrainingQuestion>()
			};

			foreach (var question in source.Questions ?? Enumerable.Empty<TrainingQuestion>())
			{
				copy.Questions.Add(new TrainingQuestion
				{
					Question = question.Question,
					Type = question.Type,
					Answers = (question.Answers ?? Enumerable.Empty<TrainingQuestionAnswer>()).OrderBy(a => a.TrainingQuestionAnswerId)
						.Select(a => new TrainingQuestionAnswer { Answer = a.Answer, Correct = a.Correct }).ToList()
				});
			}

			// Content, quiz, attachments and settings. Not the assignees or the audience: the copy is assigned on its own.
			var saved = await SaveAsync(copy, cancellationToken);
			await _trainingAttachmentRepository.CopyAttachmentsAsync(source.TrainingId, saved.TrainingId, cancellationToken);

			Audit(departmentId, userId, AuditLogTypes.TrainingCopied, null, JsonConvert.SerializeObject(new { FromTrainingId = source.TrainingId, saved.TrainingId, saved.Name }));
			return saved;
		}

		#endregion Save, archive, copy, delete

		#region Taking a training

		private static TrainingUser FindAssignment(Training training, string userId, bool includeRemoved = false)
		{
			return training?.Users?.FirstOrDefault(x => x != null && (includeRemoved || !x.RemovedOn.HasValue) &&
				string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase));
		}

		public async Task<TrainingUser> SetTrainingAsViewedAsync(int trainingId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingRowByIdAsync(trainingId);
			var assignment = FindAssignment(training, userId);
			var now = DateTime.UtcNow;

			if (assignment == null || training.IsArchived || !training.HasStarted(now))
				return assignment;

			var questions = await _trainingQuestionRepository.GetTrainingQuestionsByTrainingIdAsync(trainingId);
			var completeWithoutQuiz = questions == null || !questions.Any();

			await _trainingUserRepository.MarkViewedAsync(assignment.TrainingUserId, now, completeWithoutQuiz, cancellationToken);

			assignment.Viewed = true;
			assignment.ViewedOn ??= now;
			if (completeWithoutQuiz)
			{
				assignment.Complete = true;
				assignment.CompletedOn ??= now;
			}

			assignment.Training = training;
			return assignment;
		}

		public async Task<TrainingQuizSubmission> RecordQuizAttemptAsync(int departmentId, int trainingId, string userId, IReadOnlyDictionary<int, List<int>> answers,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			var training = await GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.NotFound };

			var now = DateTime.UtcNow;
			if (training.IsArchived || !training.HasStarted(now))
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.NotStarted };

			var assignment = FindAssignment(training, userId);
			if (assignment == null)
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.NotAssigned };

			if (training.Questions == null || training.Questions.Count == 0)
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.NoQuiz };

			if (assignment.Complete)
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.AlreadyPassed };

			var limit = training.QuizAttemptLimit.GetValueOrDefault();
			if (limit > 0 && assignment.AttemptCount >= limit)
				return new TrainingQuizSubmission { Status = TrainingQuizSubmissionStatuses.AttemptLimitReached, AttemptsLeft = 0 };

			var result = TrainingQuizScorer.Score(training, answers ?? new Dictionary<int, List<int>>());
			var attemptCount = assignment.AttemptCount + 1;

			TrainingQuizAttempt attempt = null;
			if (_attemptsRepository != null)
			{
				attempt = new TrainingQuizAttempt
				{
					DepartmentId = departmentId,
					TrainingId = trainingId,
					UserId = assignment.UserId,
					AttemptNumber = await _attemptsRepository.GetLastAttemptNumberAsync(departmentId, trainingId, assignment.UserId) + 1,
					Cycle = assignment.Cycle,
					SubmittedOn = now,
					QuizRevisionHash = result.QuizRevisionHash,
					Snapshot = JsonConvert.SerializeObject(result.Snapshot),
					QuestionCount = result.QuestionCount,
					CorrectCount = result.CorrectCount,
					Score = result.Score,
					MinimumScore = training.MinimumScore,
					Passed = result.Passed
				};
				attempt = await _attemptsRepository.InsertAsync(attempt, cancellationToken, true);
			}

			await _trainingUserRepository.RecordQuizResultAsync(assignment.TrainingUserId, result.Score, result.Passed, attemptCount, now, cancellationToken);

			return new TrainingQuizSubmission
			{
				Status = TrainingQuizSubmissionStatuses.Recorded,
				Attempt = attempt,
				Result = result,
				AttemptsLeft = limit > 0 ? Math.Max(0, limit - attemptCount) : (int?)null
			};
		}

		public async Task<List<TrainingQuizAttempt>> GetAttemptsForTrainingAsync(int departmentId, int trainingId)
		{
			if (_attemptsRepository == null)
				return new List<TrainingQuizAttempt>();
			return await _attemptsRepository.GetForTrainingAsync(departmentId, trainingId) ?? new List<TrainingQuizAttempt>();
		}

		public async Task<List<TrainingQuizAttempt>> GetAttemptsForMemberAsync(int departmentId, int trainingId, string userId)
		{
			if (_attemptsRepository == null)
				return new List<TrainingQuizAttempt>();
			return await _attemptsRepository.GetForMemberAsync(departmentId, trainingId, userId) ?? new List<TrainingQuizAttempt>();
		}

		#endregion Taking a training

		#region Flags

		public async Task<TrainingQuestionFlag> FlagQuestionAsync(int departmentId, int trainingId, int trainingQuestionId, string userId, string comment,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_flagsRepository == null)
				return null;

			var training = await GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId || training.Questions?.Any(q => q.TrainingQuestionId == trainingQuestionId) != true)
				return null;

			var text = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
			if (text != null && text.Length > 1000)
				text = text.Substring(0, 1000);

			var flag = await _flagsRepository.InsertAsync(new TrainingQuestionFlag
			{
				DepartmentId = departmentId,
				TrainingId = trainingId,
				TrainingQuestionId = trainingQuestionId,
				UserId = userId,
				Comment = text,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);

			// Feedback to the author: a notice to whoever created the training (English, as every outbound message).
			if (!string.IsNullOrWhiteSpace(training.CreatedByUserId) && _communicationService != null && ConfigHelper.CanTransmit(departmentId))
			{
				try
				{
					var department = await _departmentService.GetDepartmentByIdAsync(departmentId, false);
					var number = await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(departmentId);
					await _communicationService.SendNotificationAsync(training.CreatedByUserId, departmentId,
						string.Format("A question in training ({0}) was flagged for review", training.Name), number, department, QuestionFlaggedTitle);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Question flag notice failed for training {trainingId}");
				}
			}

			return flag;
		}

		public async Task<List<TrainingQuestionFlag>> GetQuestionFlagsAsync(int departmentId, int trainingId, bool includeResolved)
		{
			if (_flagsRepository == null)
				return new List<TrainingQuestionFlag>();
			return await _flagsRepository.GetForTrainingAsync(departmentId, trainingId, includeResolved) ?? new List<TrainingQuestionFlag>();
		}

		public async Task<bool> ResolveQuestionFlagAsync(int departmentId, int trainingId, int trainingQuestionFlagId, string userId,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (_flagsRepository == null)
				return false;

			var flags = await _flagsRepository.GetForTrainingAsync(departmentId, trainingId, false) ?? new List<TrainingQuestionFlag>();
			if (flags.All(f => f.TrainingQuestionFlagId != trainingQuestionFlagId))
				return false;

			return await _flagsRepository.ResolveAsync(departmentId, trainingQuestionFlagId, userId, DateTime.UtcNow, cancellationToken);
		}

		#endregion Flags

		#region Report

		private async Task<Dictionary<string, List<MemberLeavePeriod>>> LeavesAsync(int departmentId)
		{
			if (_leaveService == null)
				return new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase);
			return await _leaveService.GetByMemberAsync(departmentId) ?? new Dictionary<string, List<MemberLeavePeriod>>(StringComparer.OrdinalIgnoreCase);
		}

		private static List<MemberLeavePeriod> LeavesOf(Dictionary<string, List<MemberLeavePeriod>> leaves, string userId)
		{
			return userId != null && leaves != null && leaves.TryGetValue(userId, out var list) ? list : null;
		}

		public async Task<TrainingReport> GetTrainingReportAsync(int departmentId, int trainingId, DateTime utcNow)
		{
			var training = await GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != departmentId)
				return null;

			var report = new TrainingReport { Training = training };
			var department = await _departmentService.GetDepartmentByIdAsync(departmentId, false);
			var leaves = await LeavesAsync(departmentId);
			var active = await _departmentService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var names = (await _departmentService.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);

			var groupNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			if (_departmentGroupsService != null)
			{
				var groups = (await _departmentGroupsService.GetAllGroupsForDepartmentAsync(departmentId) ?? new List<DepartmentGroup>()).ToDictionary(g => g.DepartmentGroupId, g => g.Name);
				foreach (var pair in await _departmentGroupsService.GetGroupIdsForAllUsersInDepartmentAsync(departmentId) ?? new Dictionary<string, int>())
					if (groups.TryGetValue(pair.Value, out var groupName))
						groupNames[pair.Key] = groupName;
			}

			report.Attempts = await GetAttemptsForTrainingAsync(departmentId, trainingId);
			report.History = _historiesRepository == null ? new List<TrainingCompletionHistory>() : await _historiesRepository.GetForTrainingAsync(departmentId, trainingId) ?? new List<TrainingCompletionHistory>();
			report.Flags = await GetQuestionFlagsAsync(departmentId, trainingId, false);

			foreach (var assignment in training.Users ?? Enumerable.Empty<TrainingUser>())
			{
				var memberLeaves = LeavesOf(leaves, assignment.UserId);
				var due = TrainingDueDates.EffectiveDueUtc(training, assignment, department, memberLeaves);
				var onLeave = TrainingDueDates.IsOnLeave(utcNow, memberLeaves);
				var memberAttempts = report.Attempts.Where(a => string.Equals(a.UserId, assignment.UserId, StringComparison.OrdinalIgnoreCase)).ToList();

				report.Rows.Add(new TrainingReportRow
				{
					Assignment = assignment,
					UserId = assignment.UserId,
					Name = names.TryGetValue(assignment.UserId ?? string.Empty, out var name) ? name : assignment.UserId,
					GroupName = groupNames.TryGetValue(assignment.UserId ?? string.Empty, out var group) ? group : null,
					IsActiveMember = active.Contains(assignment.UserId ?? string.Empty),
					State = TrainingDueDates.StateOf(training, assignment, due, onLeave, utcNow),
					DueUtc = due,
					OnLeave = onLeave,
					AttemptCount = memberAttempts.Count,
					LastAttempt = memberAttempts.OrderByDescending(a => a.AttemptNumber).FirstOrDefault()
				});
			}

			report.Rows = report.Rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

			var tracked = report.Rows.Where(r => r.IsActiveMember && r.State != TrainingAssignmentStates.Removed && r.State != TrainingAssignmentStates.Exempt).ToList();
			report.CompleteCount = tracked.Count(r => r.State == TrainingAssignmentStates.Complete);
			report.OverdueCount = tracked.Count(r => r.State == TrainingAssignmentStates.Overdue);
			report.ExemptCount = report.Rows.Count(r => r.IsActiveMember && r.State == TrainingAssignmentStates.Exempt);
			report.CompletionPercent = tracked.Count == 0 ? 0 : Math.Round(report.CompleteCount * 100d / tracked.Count, 1);

			return report;
		}

		public async Task<TrainingManagerSummary> GetManagerSummaryAsync(int departmentId, IEnumerable<Training> trainings, DateTime utcNow)
		{
			var summary = new TrainingManagerSummary();
			var list = (trainings ?? Enumerable.Empty<Training>()).Where(t => t != null && t.DepartmentId == departmentId && !t.IsArchived).ToList();
			if (list.Count == 0)
				return summary;

			var department = await _departmentService.GetDepartmentByIdAsync(departmentId, false);
			var leaves = await LeavesAsync(departmentId);
			var active = await _departmentService.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int tracked = 0, complete = 0;

			foreach (var training in list)
			{
				foreach (var assignment in training.Users ?? Enumerable.Empty<TrainingUser>())
				{
					if (assignment == null || assignment.RemovedOn.HasValue || !active.Contains(assignment.UserId ?? string.Empty) || assignment.IsExemptOn(utcNow))
						continue;

					tracked++;
					if (assignment.Complete)
					{
						complete++;
						continue;
					}

					var memberLeaves = LeavesOf(leaves, assignment.UserId);
					var state = TrainingDueDates.StateOf(training, assignment, TrainingDueDates.EffectiveDueUtc(training, assignment, department, memberLeaves),
						TrainingDueDates.IsOnLeave(utcNow, memberLeaves), utcNow);
					var due = TrainingDueDates.EffectiveDueUtc(training, assignment, department, memberLeaves);

					if (state == TrainingAssignmentStates.Overdue)
						summary.Overdue++;
					else if (state != TrainingAssignmentStates.Scheduled && state != TrainingAssignmentStates.OnLeave && TrainingDueDates.IsDueWithin(due, utcNow, TimeSpan.FromDays(7)))
						summary.DueThisWeek++;
				}
			}

			summary.CompletionPercent = tracked == 0 ? 0 : Math.Round(complete * 100d / tracked, 1);
			return summary;
		}

		#endregion Report

		#region Notifier

		public async Task<List<Training>> GetTrainingsToNotifyAsync(DateTime currentTime)
		{
			var trainingsToNotify = new List<Training>();

			// One query over every department for the trainings that may owe a notice (never announced, inside a reminder lead,
			// or overdue), instead of loading every training of every department and testing a 10:00 window (T-D19).
			var ids = await _trainingRepository.GetTrainingIdsWithNoticeWorkAsync(currentTime, MaxReminderLeadDays) ?? new List<int>();

			foreach (var trainingId in ids.Distinct())
			{
				// The repository answers null for a read that failed as well as for a row that is gone; either way it is left
				// for a later run, never marked with nobody told.
				var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);
				if (training == null)
					continue;

				training.Users ??= new List<TrainingUser>();
				trainingsToNotify.Add(training);
			}

			return trainingsToNotify;
		}

		public async Task<int> SendTrainingNoticesAsync(Training training, DateTime utcNow, IReadOnlyCollection<string> onlyUserIds = null,
			CancellationToken cancellationToken = default(CancellationToken))
		{
			if (training?.Users == null || training.Users.Count == 0)
				return 0;

			var only = onlyUserIds == null ? null : new HashSet<string>(onlyUserIds.Where(x => !string.IsNullOrWhiteSpace(x)), StringComparer.OrdinalIgnoreCase);
			var department = await _departmentService.GetDepartmentByIdAsync(training.DepartmentId, false);
			var active = await _departmentService.GetActiveMemberUserIdsAsync(training.DepartmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var leaves = await LeavesAsync(training.DepartmentId);
			var canTransmit = ConfigHelper.CanTransmit(training.DepartmentId);
			string departmentNumber = null;
			var sent = 0;
			var overdueNow = 0;

			foreach (var assignment in training.Users.ToList())
			{
				if (assignment == null || string.IsNullOrWhiteSpace(assignment.UserId) || (only != null && !only.Contains(assignment.UserId)))
					continue;

				// Rows from before T0 carry no due moment; the training's fixed date is written on them once.
				if (!assignment.DueOn.HasValue && training.ToBeCompletedBy.HasValue && !assignment.RemovedOn.HasValue)
				{
					assignment.DueOn = TrainingDueDates.FixedDueUtc(training, department);
					await _trainingUserRepository.UpdateColumnsAsync(assignment, new[] { "DueOn" }, cancellationToken);
				}

				var memberLeaves = LeavesOf(leaves, assignment.UserId);
				var due = TrainingDueDates.EffectiveDueUtc(training, assignment, department, memberLeaves);
				var localDue = due.HasValue ? DateTime.SpecifyKind(due.Value, DateTimeKind.Utc).TimeConverter(department).ToShortDateString() : null;
				var plan = TrainingNoticePlanner.PlanAssignment(training, assignment, due, active.Contains(assignment.UserId), TrainingDueDates.IsOnLeave(utcNow, memberLeaves), localDue, utcNow);

				if (plan.MarkNotifiedOnly)
				{
					assignment.NotifiedOn = utcNow;
					await _trainingUserRepository.UpdateColumnsAsync(assignment, new[] { "NotifiedOn" }, cancellationToken);
					continue;
				}

				if (plan.Kind == TrainingNoticeKinds.None)
					continue;

				if (canTransmit)
				{
					departmentNumber ??= await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(training.DepartmentId);
					await _communicationService.SendNotificationAsync(assignment.UserId, training.DepartmentId, plan.Message, departmentNumber, department, plan.Title);
					sent++;
				}

				// Stamped whether or not the department may transmit, so re-enabling it does not release a backlog.
				switch (plan.Kind)
				{
					case TrainingNoticeKinds.NewTraining:
						assignment.NotifiedOn = utcNow;
						await _trainingUserRepository.UpdateColumnsAsync(assignment, new[] { "NotifiedOn" }, cancellationToken);
						break;
					case TrainingNoticeKinds.DueReminder:
						assignment.RemindedOn = utcNow;
						assignment.LastReminderLeadDays = plan.LeadDays;
						await _trainingUserRepository.UpdateColumnsAsync(assignment, new[] { "RemindedOn", "LastReminderLeadDays" }, cancellationToken);
						break;
					case TrainingNoticeKinds.Overdue:
						assignment.OverdueNotifiedOn = utcNow;
						await _trainingUserRepository.UpdateColumnsAsync(assignment, new[] { "OverdueNotifiedOn" }, cancellationToken);
						overdueNow++;
						break;
				}
			}

			// The training officer is told too: one digest to the training's creator per run that found members overdue.
			if (overdueNow > 0 && canTransmit && !string.IsNullOrWhiteSpace(training.CreatedByUserId) && active.Contains(training.CreatedByUserId))
			{
				departmentNumber ??= await _departmentSettingsService.GetTextToCallNumberForDepartmentAsync(training.DepartmentId);
				await _communicationService.SendNotificationAsync(training.CreatedByUserId, training.DepartmentId, TrainingNoticePlanner.CreatorOverdueMessage(training, overdueNow),
					departmentNumber, department, TrainingNoticePlanner.OverdueTitle);
				sent++;
			}

			return sent;
		}

		public async Task<Training> MarkAsNotifiedAsync(int trainingId, CancellationToken cancellationToken = default(CancellationToken))
		{
			// The training row alone: loading the attachments and saving the whole tree back would rewrite every assignee row
			// from a copy read moments earlier, undoing a quiz result recorded in between.
			var training = await _trainingRepository.GetTrainingByTrainingIdAsync(trainingId);

			if (training == null)
				return null;

			training.Notified = DateTime.UtcNow;

			return await _trainingRepository.SaveOrUpdateAsync(training, cancellationToken, true);
		}

		#endregion Notifier

		#region Member assignments

		public async Task<List<TrainingUser>> GetTrainingUsersForUserAsync(string userId)
		{
			var users = (await _trainingUserRepository.GetAllByUserIdAsync(userId))?.ToList() ?? new List<TrainingUser>();

			// The GDPR export names each training (T-D23): the training row's own fields, never other members' rows.
			var trainings = new Dictionary<int, Training>();
			foreach (var trainingId in users.Select(u => u.TrainingId).Distinct())
			{
				var row = await _trainingRepository.GetByIdAsync(trainingId);
				if (row != null)
					trainings[trainingId] = new Training { TrainingId = row.TrainingId, DepartmentId = row.DepartmentId, Name = row.Name, CreatedOn = row.CreatedOn, ToBeCompletedBy = row.ToBeCompletedBy, Hours = row.Hours };
			}

			foreach (var user in users)
				if (trainings.TryGetValue(user.TrainingId, out var training))
					user.Training = training;

			return users;
		}

		public async Task<List<TrainingAssignmentStatus>> GetAssignmentsForUserAsync(int departmentId, string userId, DateTime utcNow)
		{
			var result = new List<TrainingAssignmentStatus>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return result;

			var trainings = (await _trainingRepository.GetTrainingsByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<Training>())
				.Where(t => t != null && t.DepartmentId == departmentId && !t.IsArchived).ToList();
			var mine = trainings.Select(t => new { Training = t, Assignment = FindAssignment(t, userId) }).Where(x => x.Assignment != null).ToList();
			if (mine.Count == 0)
				return result;

			var counts = await _trainingRepository.GetQuestionCountsForDepartmentAsync(departmentId) ?? new Dictionary<int, int>();
			var department = await _departmentService.GetDepartmentByIdAsync(departmentId, false);
			var leaves = _leaveService == null ? null : await _leaveService.GetForMemberAsync(departmentId, userId);
			var onLeave = TrainingDueDates.IsOnLeave(utcNow, leaves);

			foreach (var item in mine)
			{
				item.Training.QuestionCount = counts.TryGetValue(item.Training.TrainingId, out var count) ? count : 0;
				var due = TrainingDueDates.EffectiveDueUtc(item.Training, item.Assignment, department, leaves);
				result.Add(new TrainingAssignmentStatus
				{
					Training = item.Training,
					Assignment = item.Assignment,
					DueUtc = due,
					State = TrainingDueDates.StateOf(item.Training, item.Assignment, due, onLeave, utcNow),
					QuizFailed = !item.Assignment.Complete && item.Assignment.Passed == false
				});
			}

			return result
				.OrderBy(x => x.DueUtc.HasValue ? 0 : 1)
				.ThenBy(x => x.DueUtc)
				.ThenBy(x => x.Training.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}

		public async Task<List<TrainingAssignmentStatus>> GetOutstandingTrainingsForUserAsync(int departmentId, string userId)
		{
			var outstanding = new[] { TrainingAssignmentStates.NotStarted, TrainingAssignmentStates.InProgress, TrainingAssignmentStates.Failed, TrainingAssignmentStates.Overdue, TrainingAssignmentStates.OnLeave };
			return (await GetAssignmentsForUserAsync(departmentId, userId, DateTime.UtcNow)).Where(x => outstanding.Contains(x.State)).ToList();
		}

		#endregion Member assignments

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_eventAggregator?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true
			});
		}
	}
}
