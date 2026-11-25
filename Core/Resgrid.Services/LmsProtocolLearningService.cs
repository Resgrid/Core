using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The LMS parts of protocol acknowledgment (P2, §17.2), behind the baseline seam <see cref="IProtocolLearningExtensions"/>:
	/// <list type="bullet">
	/// <item>Acknowledgment quizzes drawn from a department-shared question bank linked to the protocol. Draw and scoring run on the
	/// server only; each attempt stores its snapshot with the key (R-T6) and the member receives questions and options (R-T11). The
	/// acknowledgment completes only when a quiz on that very version is passed (a major version asks again). The free baseline
	/// quiz-gated acknowledgment (a linked training) is unchanged; when both are set, both must be passed.</item>
	/// <item>Weekly micro-learning: one scenario question per member per week, drawn from the banks tagged to the department's selected
	/// protocols and sent to each protocol's active, licensed audience. Worker 86 reads only due departments and asks the LMS gate once per
	/// department.</item>
	/// </list>
	/// Settings and new micro-learning need the add-on; a quiz a member already owes stays takeable after a lapse (rule 4).
	/// </summary>
	public class LmsProtocolLearningService : IProtocolLearningExtensions
	{
		public const int DueDepartmentsPerRun = 200;

		private readonly ILmsProtocolLearningsRepository _learnings;
		private readonly ILmsProtocolQuizAttemptsRepository _attempts;
		private readonly ILmsMicroLearningRepository _micro;
		private readonly ILmsQuestionBanksRepository _banks;
		private readonly ILmsQuestionsRepository _questions;
		private readonly ILmsAccessService _access;
		private readonly ILmsSeatService _seats;
		private readonly IProtocolsService _protocols;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolAudiencesRepository _audiences;
		private readonly IContentAudienceService _audienceService;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IUserProfileService _profiles;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IProtocolAcknowledgmentService> _acknowledgments;
		private readonly IEventAggregator _events;

		/// <summary>The draw's randomness (a seam for tests).</summary>
		public Func<Random> RandomFactory { get; set; } = () => new Random();

		public LmsProtocolLearningService(ILmsProtocolLearningsRepository learnings, ILmsProtocolQuizAttemptsRepository attempts, ILmsMicroLearningRepository micro,
			ILmsQuestionBanksRepository banks, ILmsQuestionsRepository questions, ILmsAccessService access, ILmsSeatService seats, IProtocolsService protocols,
			IProtocolVersionsRepository versions, IProtocolAudiencesRepository audiences, IContentAudienceService audienceService, IDepartmentsService departments,
			IDepartmentSettingsService departmentSettings, IUserProfileService profiles, Lazy<ICommunicationService> communication,
			Lazy<IProtocolAcknowledgmentService> acknowledgments, IEventAggregator events)
		{
			_learnings = learnings;
			_attempts = attempts;
			_micro = micro;
			_banks = banks;
			_questions = questions;
			_access = access;
			_seats = seats;
			_protocols = protocols;
			_versions = versions;
			_audiences = audiences;
			_audienceService = audienceService;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_profiles = profiles;
			_communication = communication;
			_acknowledgments = acknowledgments;
			_events = events;
		}

		private static bool ManagesAcknowledgments(ProtocolReader reader) =>
			reader != null && reader.IsCurrentMember && reader.DepartmentId > 0 && (reader.IsDepartmentAdmin || reader.CanManageAcknowledgments || reader.CanApprove || reader.MayAuthor);

		#region Availability and settings

		public Task<bool> IsMicroLearningAvailableAsync(int departmentId) => departmentId <= 0 ? Task.FromResult(false) : _access.CanAuthorAndManageAsync(departmentId);

		public Task<bool> IsBankQuizAvailableAsync(int departmentId) => departmentId <= 0 ? Task.FromResult(false) : _access.CanAuthorAndManageAsync(departmentId);

		public async Task<ProtocolLearningSettings> GetSettingsAsync(int departmentId, int protocolId)
		{
			var row = departmentId <= 0 ? null : await _learnings.GetForProtocolAsync(departmentId, protocolId);
			return row == null || row.DepartmentId != departmentId
				? new ProtocolLearningSettings { ProtocolId = protocolId }
				: new ProtocolLearningSettings
				{
					ProtocolId = protocolId,
					QuestionBankId = row.LmsQuestionBankId,
					QuizRequired = row.QuizRequired,
					QuestionCount = row.QuestionCount,
					PassMark = row.PassMark,
					Tags = row.Tags,
					MicroLearning = row.MicroLearning
				};
		}

		/// <summary>The department's shared banks (a protocol quiz is department content; a private bank is its owner's).</summary>
		private async Task<List<LmsQuestionBank>> SharedBanksAsync(int departmentId) =>
			(await _banks.GetForDepartmentAsync(departmentId) ?? new List<LmsQuestionBank>())
				.Where(b => b != null && b.DepartmentId == departmentId && !b.IsDeleted && b.IsShared).ToList();

		public async Task<List<ProtocolQuestionBankChoice>> GetBankChoicesAsync(int departmentId)
		{
			var choices = new List<ProtocolQuestionBankChoice>();
			if (departmentId <= 0)
				return choices;
			foreach (var bank in (await SharedBanksAsync(departmentId)).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var questions = await _questions.GetForBankAsync(departmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>();
				choices.Add(new ProtocolQuestionBankChoice { Id = bank.LmsQuestionBankId, Name = bank.Name, DrawableQuestions = questions.Count(LmsProtocolLearningRules.IsDrawable) });
			}
			return choices;
		}

		public async Task<List<int>> GetMicroLearningProtocolIdsAsync(int departmentId) =>
			(await _learnings.GetForDepartmentAsync(departmentId) ?? new List<LmsProtocolLearning>())
				.Where(l => l.DepartmentId == departmentId && l.MicroLearning).Select(l => l.DispatchProtocolId).Distinct().ToList();

		public async Task<ProtocolLearningErrors> SaveSettingsAsync(ProtocolReader reader, ProtocolLearningSettings settings, CancellationToken cancellationToken = default)
		{
			if (!ManagesAcknowledgments(reader))
				return ProtocolLearningErrors.NotAuthorized;
			if (settings == null || settings.ProtocolId <= 0)
				return ProtocolLearningErrors.InvalidInput;

			var protocol = await _protocols.GetProtocolHeaderByIdAsync(settings.ProtocolId);
			if (protocol == null || protocol.DepartmentId != reader.DepartmentId || protocol.IsDeleted)
				return ProtocolLearningErrors.NotFound;
			// Authoring LMS settings needs the add-on (rule 3; fails closed).
			if (!await _access.CanAuthorAndManageAsync(reader.DepartmentId))
				return ProtocolLearningErrors.AddonRequired;

			if (settings.QuestionBankId.HasValue)
			{
				var bank = (await SharedBanksAsync(reader.DepartmentId)).FirstOrDefault(b => b.LmsQuestionBankId == settings.QuestionBankId.Value);
				if (bank == null)
					return ProtocolLearningErrors.InvalidInput;
				if (settings.QuizRequired && !(await _questions.GetForBankAsync(reader.DepartmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>()).Any(LmsProtocolLearningRules.IsDrawable))
					return ProtocolLearningErrors.NoQuestions;
			}
			else if (settings.QuizRequired)
			{
				return ProtocolLearningErrors.InvalidInput;
			}

			var row = await _learnings.GetForProtocolAsync(reader.DepartmentId, protocol.DispatchProtocolId);
			var before = row == null ? null : JsonConvert.SerializeObject(Describe(row));
			var now = DateTime.UtcNow;
			row ??= new LmsProtocolLearning { DepartmentId = reader.DepartmentId, DispatchProtocolId = protocol.DispatchProtocolId, CreatedOn = now, CreatedByUserId = reader.UserId };
			row.LmsQuestionBankId = settings.QuestionBankId;
			row.QuizRequired = settings.QuizRequired && settings.QuestionBankId.HasValue;
			row.QuestionCount = LmsProtocolLearningRules.ClampQuestions(settings.QuestionCount);
			row.PassMark = LmsProtocolLearningRules.ClampPassMark(settings.PassMark);
			row.Tags = Clean(settings.Tags, LmsProtocolLearning.TagsMaxLength);
			// A bulletin is a notice, not learning: it never takes part in micro-learning.
			row.MicroLearning = settings.MicroLearning && !protocol.IsBulletin;
			if (row.LmsProtocolLearningId > 0)
			{
				row.UpdatedOn = now;
				row.UpdatedByUserId = reader.UserId;
			}

			await _learnings.SaveOrUpdateAsync(row, cancellationToken, true);
			Audit(reader.DepartmentId, reader.UserId, before, JsonConvert.SerializeObject(Describe(row)));
			return ProtocolLearningErrors.None;
		}

		public async Task<int> ScheduleMicroLearningAsync(ProtocolReader reader, IReadOnlyCollection<int> protocolIds, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			if (!ManagesAcknowledgments(reader) || !await _access.CanAuthorAndManageAsync(reader.DepartmentId))
				return -1;

			// Only the department's published protocols that are not bulletins can be selected.
			var wanted = new HashSet<int>();
			foreach (var id in (protocolIds ?? Array.Empty<int>()).Where(i => i > 0).Distinct())
			{
				var protocol = await _protocols.GetProtocolHeaderByIdAsync(id);
				if (protocol != null && protocol.DepartmentId == reader.DepartmentId && !protocol.IsDeleted && protocol.IsPublished && !protocol.IsBulletin)
					wanted.Add(id);
			}

			var rows = (await _learnings.GetForDepartmentAsync(reader.DepartmentId) ?? new List<LmsProtocolLearning>()).Where(r => r.DepartmentId == reader.DepartmentId).ToList();
			var before = JsonConvert.SerializeObject(rows.Where(r => r.MicroLearning).Select(r => r.DispatchProtocolId).OrderBy(i => i));
			foreach (var row in rows)
			{
				var on = wanted.Contains(row.DispatchProtocolId);
				if (row.MicroLearning == on)
					continue;
				row.MicroLearning = on;
				row.UpdatedOn = utcNow;
				row.UpdatedByUserId = reader.UserId;
				await _learnings.SaveOrUpdateAsync(row, cancellationToken, true);
			}

			foreach (var id in wanted.Where(id => rows.All(r => r.DispatchProtocolId != id)))
				await _learnings.SaveOrUpdateAsync(new LmsProtocolLearning
				{
					DepartmentId = reader.DepartmentId,
					DispatchProtocolId = id,
					MicroLearning = true,
					CreatedOn = utcNow,
					CreatedByUserId = reader.UserId
				}, cancellationToken, true);

			Audit(reader.DepartmentId, reader.UserId, before, JsonConvert.SerializeObject(new { MicroLearningProtocolIds = wanted.OrderBy(i => i) }));
			return wanted.Count;
		}

		private static object Describe(LmsProtocolLearning row) => new
		{
			row.DispatchProtocolId,
			row.LmsQuestionBankId,
			row.QuizRequired,
			row.QuestionCount,
			row.PassMark,
			row.Tags,
			row.MicroLearning
		};

		private static string Clean(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		#endregion Availability and settings

		#region Acknowledgment quizzes

		public async Task<ProtocolBankQuizState> GetBankQuizStateAsync(int departmentId, int protocolId, int protocolVersionId, string userId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return ProtocolBankQuizState.None;
			var row = await _learnings.GetForProtocolAsync(departmentId, protocolId);
			var state = new ProtocolBankQuizState();
			if (row != null && row.DepartmentId == departmentId && row.GatesAcknowledgment)
			{
				state.Configured = true;
				state.QuestionCount = row.QuestionCount;
				state.PassMark = row.PassMark;
			}

			var attempts = (await _attempts.GetForUserVersionAsync(departmentId, userId, protocolVersionId) ?? new List<LmsProtocolQuizAttempt>())
				.Where(a => a.DepartmentId == departmentId && a.ProtocolVersionId == protocolVersionId && string.Equals(a.UserId, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			var scored = attempts.Where(a => a.Status == (int)LmsProtocolQuizAttemptStatuses.Scored).ToList();
			state.Attempts = scored.Count;
			state.Passed = scored.Any(a => a.Passed == true);
			state.BestPercent = scored.Count == 0 ? (decimal?)null : scored.Max(a => a.Percent);
			state.OpenAttemptId = attempts.Where(a => a.Status == (int)LmsProtocolQuizAttemptStatuses.InProgress).Select(a => (int?)a.LmsProtocolQuizAttemptId).FirstOrDefault();
			return state;
		}

		public async Task<ProtocolQuizView> StartBankQuizAsync(ProtocolReader reader, int protocolVersionId, string locale, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId))
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NotAuthorized);

			var version = await _versions.GetByIdForDepartmentAsync(reader.DepartmentId, protocolVersionId);
			var protocol = version == null ? null : await _protocols.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (version == null || protocol == null || protocol.DepartmentId != reader.DepartmentId || version.DepartmentId != reader.DepartmentId)
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NotFound);

			// Only a member who owes the version's acknowledgment takes its quiz (assigned, not withdrawn or superseded).
			var acknowledgment = await _acknowledgments.Value.GetForVersionAndUserAsync(reader.DepartmentId, protocolVersionId, reader.UserId);
			if (!ProtocolAcknowledgmentRules.IsOutstanding(acknowledgment))
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NotAssigned);

			var settings = await _learnings.GetForProtocolAsync(reader.DepartmentId, protocol.DispatchProtocolId);
			if (settings == null || !settings.GatesAcknowledgment)
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NotAssigned);

			var attempts = await _attempts.GetForUserVersionAsync(reader.DepartmentId, reader.UserId, protocolVersionId) ?? new List<LmsProtocolQuizAttempt>();
			if (attempts.Any(a => a.Status == (int)LmsProtocolQuizAttemptStatuses.Scored && a.Passed == true))
				return ProtocolQuizView.Fail(ProtocolLearningErrors.AlreadySubmitted);

			var open = attempts.FirstOrDefault(a => a.Status == (int)LmsProtocolQuizAttemptStatuses.InProgress);
			if (open != null)
				return View(open, protocol, version);

			var bank = (await SharedBanksAsync(reader.DepartmentId)).FirstOrDefault(b => b.LmsQuestionBankId == settings.LmsQuestionBankId.Value);
			if (bank == null)
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NoQuestions);
			var pool = (await _questions.GetForBankAsync(reader.DepartmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>()).Where(LmsProtocolLearningRules.IsDrawable);
			var snapshot = LmsAssessmentEngine.Draw(pool, settings.QuestionCount, settings.Tags, true, settings.PassMark, RandomFactory(), locale);
			if (snapshot.Questions.Count == 0)
				return ProtocolQuizView.Fail(ProtocolLearningErrors.NoQuestions);

			var attempt = new LmsProtocolQuizAttempt
			{
				DepartmentId = reader.DepartmentId,
				DispatchProtocolId = protocol.DispatchProtocolId,
				ProtocolVersionId = version.ProtocolVersionId,
				UserId = reader.UserId,
				LmsQuestionBankId = bank.LmsQuestionBankId,
				AttemptNumber = attempts.Count + 1,
				Status = (int)LmsProtocolQuizAttemptStatuses.InProgress,
				StartedOn = DateTime.UtcNow,
				PassMark = settings.PassMark,
				RevisionHash = LmsAssessmentEngine.RevisionHash(snapshot),
				SnapshotJson = JsonConvert.SerializeObject(snapshot)
			};
			await _attempts.InsertAsync(attempt, cancellationToken, true);
			return View(attempt, protocol, version);
		}

		/// <summary>The learner payload: questions and options in display order, never a key or an explanation (R-T11).</summary>
		public static ProtocolQuizView View(LmsProtocolQuizAttempt attempt, DispatchProtocol protocol, ProtocolVersion version) => new ProtocolQuizView
		{
			AttemptId = attempt.LmsProtocolQuizAttemptId,
			ProtocolId = protocol.DispatchProtocolId,
			ProtocolVersionId = attempt.ProtocolVersionId,
			ProtocolCode = protocol.Code,
			ProtocolName = protocol.Name,
			VersionLabel = version?.Label,
			PassMark = attempt.PassMark,
			Questions = LearnerQuestions(LmsAttemptSnapshot.Parse(attempt.SnapshotJson))
		};

		public static List<ProtocolQuizQuestion> LearnerQuestions(LmsAttemptSnapshot snapshot) =>
			LmsAssessmentEngine.ToLearnerView(snapshot).Select(q => new ProtocolQuizQuestion
			{
				QuestionId = q.QuestionId,
				Kind = q.Kind,
				Text = q.Text,
				ImageUrl = q.ImageUrl,
				Points = q.Points,
				Options = q.Options.Select(o => new ProtocolQuizOption { Key = o.Key, Text = o.Text }).ToList()
			}).ToList();

		private static List<LmsAttemptAnswer> CleanAnswers(LmsAttemptSnapshot snapshot, IEnumerable<ProtocolQuizAnswer> answers)
		{
			var known = new HashSet<int>(snapshot.Questions.Select(q => q.QuestionId));
			return (answers ?? Enumerable.Empty<ProtocolQuizAnswer>()).Where(a => a != null && known.Contains(a.QuestionId))
				.GroupBy(a => a.QuestionId).Select(g => g.First())
				.Select(a => new LmsAttemptAnswer
				{
					QuestionId = a.QuestionId,
					Keys = (a.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct().Take(LmsQuestionBankService.MaxOptions).ToList()
				}).ToList();
		}

		public async Task<ProtocolQuizResult> SubmitBankQuizAsync(ProtocolReader reader, int attemptId, IEnumerable<ProtocolQuizAnswer> answers, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.IsCurrentMember || reader.DepartmentId <= 0 || string.IsNullOrWhiteSpace(reader.UserId))
				return ProtocolQuizResult.Fail(ProtocolLearningErrors.NotAuthorized);

			var attempt = await _attempts.GetByIdForDepartmentAsync(reader.DepartmentId, attemptId);
			if (attempt == null || attempt.DepartmentId != reader.DepartmentId || !string.Equals(attempt.UserId, reader.UserId, StringComparison.OrdinalIgnoreCase))
				return ProtocolQuizResult.Fail(ProtocolLearningErrors.NotFound);
			if (attempt.Status != (int)LmsProtocolQuizAttemptStatuses.InProgress)
				return Result(attempt, ProtocolLearningErrors.AlreadySubmitted, false);

			var snapshot = LmsAttemptSnapshot.Parse(attempt.SnapshotJson);
			var cleaned = CleanAnswers(snapshot, answers);
			var outcome = LmsAssessmentEngine.Score(snapshot, cleaned);
			var expected = attempt.RowVersion;
			attempt.AnswersJson = JsonConvert.SerializeObject(cleaned);
			attempt.ResultsJson = JsonConvert.SerializeObject(outcome.Results);
			attempt.Score = outcome.Score;
			attempt.MaxScore = outcome.MaxScore;
			attempt.Percent = outcome.Percent;
			// Choice questions only: nothing waits for a grader, so the outcome is decided now.
			attempt.Passed = outcome.Passed ?? false;
			attempt.SubmittedOn = DateTime.UtcNow;
			attempt.Status = (int)LmsProtocolQuizAttemptStatuses.Scored;
			if (!await _attempts.TryUpdateAsync(attempt, expected, cancellationToken))
				return Result(await _attempts.GetByIdForDepartmentAsync(reader.DepartmentId, attemptId) ?? attempt, ProtocolLearningErrors.AlreadySubmitted, false);

			var completed = false;
			if (attempt.Passed == true)
			{
				try
				{
					// A signed acknowledgment that was waiting for this quiz completes now (worker 86 would otherwise pick it up).
					completed = await _acknowledgments.Value.CompleteIfQuizPassedAsync(reader.DepartmentId, attempt.ProtocolVersionId, reader.UserId, DateTime.UtcNow, cancellationToken);
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					Logging.LogException(ex, $"Acknowledgment for protocol quiz attempt {attempt.LmsProtocolQuizAttemptId} could not be completed at once.");
				}
			}

			return Result(attempt, ProtocolLearningErrors.None, completed);
		}

		private static ProtocolQuizResult Result(LmsProtocolQuizAttempt attempt, ProtocolLearningErrors error, bool completed)
		{
			var results = LmsQuestionResult.Parse(attempt?.ResultsJson);
			return new ProtocolQuizResult
			{
				Error = error,
				AttemptId = attempt?.LmsProtocolQuizAttemptId ?? 0,
				ProtocolVersionId = attempt?.ProtocolVersionId ?? 0,
				Percent = attempt?.Percent ?? 0,
				PassMark = attempt?.PassMark ?? 0,
				Passed = attempt?.Passed == true,
				Correct = results.Count(r => r.Correct),
				Total = results.Count,
				AcknowledgmentCompleted = completed
			};
		}

		#endregion Acknowledgment quizzes

		#region Micro-learning

		public async Task<int> RunMicroLearningAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var sent = 0;
			// Due work only (R-L9): departments with a micro-learning protocol whose weekly clock ran out. Never every department.
			foreach (var departmentId in await _learnings.GetMicroLearningDueDepartmentIdsAsync(utcNow, DueDepartmentsPerRun, cancellationToken) ?? new List<int>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					sent += await RunDepartmentAsync(departmentId, utcNow, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One department failing does not hold the others; its clock already moved, so it is not retried this week.
					Logging.LogException(ex, $"Micro-learning for department {departmentId} failed.");
				}
			}
			return sent;
		}

		/// <summary>One due department: claim the week, ask the gate once, then send each active, licensed member one question.</summary>
		internal async Task<int> RunDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null)
				return 0;
			var localNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc).TimeConverter(department);
			var weekKey = LmsProtocolLearningRules.WeekKey(localNow.Date);
			var nextUtc = DateTime.SpecifyKind(DateTime.SpecifyKind(LmsProtocolLearningRules.NextRunLocal(localNow), DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc);

			// Claim first, so a department whose add-on is off is not due again (and its gate not asked again) until next week.
			if (!await _micro.TryClaimRunAsync(departmentId, utcNow, nextUtc, weekKey, cancellationToken))
				return 0;

			// The LMS gate once per department per run: the flag, then the licensed members (one billing answer).
			if (!await _access.IsFlagEnabledAsync(departmentId))
				return 0;
			var licensed = await _seats.GetLicensedUserIdsAsync(departmentId) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (licensed.Count == 0)
				return 0;
			licensed = new HashSet<string>(licensed, StringComparer.OrdinalIgnoreCase);

			return await SendWeekAsync(department, weekKey, licensed, utcNow, cancellationToken);
		}

		internal async Task<int> SendWeekAsync(Department department, string weekKey, HashSet<string> licensed, DateTime utcNow, CancellationToken cancellationToken)
		{
			var departmentId = department.DepartmentId;
			var settings = (await _learnings.GetForDepartmentAsync(departmentId) ?? new List<LmsProtocolLearning>()).Where(l => l.DepartmentId == departmentId && l.MicroLearning).ToList();
			if (settings.Count == 0)
				return 0;

			var banks = await SharedBanksAsync(departmentId);
			var bankQuestions = new Dictionary<int, List<LmsQuestion>>();
			var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(departmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);

			var protocols = new Dictionary<int, DispatchProtocol>();
			var pools = new Dictionary<int, List<LmsQuestion>>();
			var memberProtocols = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
			foreach (var setting in settings)
			{
				var protocol = await _protocols.GetProtocolHeaderByIdAsync(setting.DispatchProtocolId);
				if (protocol == null || protocol.DepartmentId != departmentId || protocol.IsDeleted || !protocol.IsPublished || protocol.IsBulletin)
					continue;

				var pool = new List<LmsQuestion>();
				foreach (var bank in banks.Where(b => LmsProtocolLearningRules.IsBankTaggedTo(b, protocol, setting.LmsQuestionBankId)))
				{
					if (!bankQuestions.TryGetValue(bank.LmsQuestionBankId, out var questions))
						bankQuestions[bank.LmsQuestionBankId] = questions = await _questions.GetForBankAsync(departmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>();
					pool.AddRange(questions.Where(LmsProtocolLearningRules.IsDrawable));
				}
				if (pool.Count == 0)
					continue;

				protocols[protocol.DispatchProtocolId] = protocol;
				pools[protocol.DispatchProtocolId] = pool;
				// The protocol's audience: active members only (inactive-member policy), and only those holding an LMS seat.
				foreach (var userId in await AudienceAsync(protocol))
					if (active.Contains(userId) && licensed.Contains(userId))
					{
						if (!memberProtocols.TryGetValue(userId, out var ids))
							memberProtocols[userId] = ids = new List<int>();
						ids.Add(protocol.DispatchProtocolId);
					}
			}

			if (memberProtocols.Count == 0)
				return 0;

			var recent = (await _micro.GetRecentForDepartmentAsync(departmentId, utcNow.AddDays(-7 * LmsProtocolLearningRules.RecentWeeks)) ?? new List<LmsMicroLearningItem>())
				.Where(i => i.UserId != null).GroupBy(i => i.UserId, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(g => g.Key, g => g.Select(i => i.LmsQuestionId).ToList(), StringComparer.OrdinalIgnoreCase);
			var canTransmit = ConfigHelper.CanTransmit(departmentId);
			var number = canTransmit ? await _departmentSettings.GetTextToCallNumberForDepartmentAsync(departmentId) : null;
			var random = RandomFactory();
			var sent = 0;

			foreach (var (userId, ids) in memberProtocols.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase))
			{
				cancellationToken.ThrowIfCancellationRequested();
				var protocolId = LmsProtocolLearningRules.ChooseProtocol(userId, weekKey, ids);
				if (!protocolId.HasValue)
					continue;
				var question = LmsProtocolLearningRules.ChooseQuestion(pools[protocolId.Value], recent.TryGetValue(userId, out var asked) ? asked : null, random);
				if (question == null)
					continue;

				var profile = await _profiles.GetProfileByUserIdAsync(userId);
				var language = profile?.Language;
				var snapshot = LmsAssessmentEngine.Draw(new[] { question }, 1, null, true, 100, random, language);
				if (snapshot.Questions.Count == 0)
					continue;

				var item = new LmsMicroLearningItem
				{
					DepartmentId = departmentId,
					UserId = userId,
					DispatchProtocolId = protocolId.Value,
					LmsQuestionId = question.LmsQuestionId,
					WeekKey = weekKey,
					SnapshotJson = JsonConvert.SerializeObject(snapshot),
					SentOn = utcNow
				};
				if (!await _micro.TryInsertItemAsync(item, cancellationToken))
					continue;
				sent++;

				if (!canTransmit)
					continue;
				try
				{
					var protocol = protocols[protocolId.Value];
					var name = string.IsNullOrWhiteSpace(protocol.Code) ? protocol.Name : protocol.Code + " " + protocol.Name;
					var message = string.Format(LmsText.Culture(language), LmsText.Get("Notify.MicroLearning", language), name);
					await _communication.Value.SendNotificationAsync(userId, departmentId, message, number, department, LmsText.Get("Notify.MicroLearning.Title", language), profile);
				}
				catch (Exception ex)
				{
					// The question is on the member's page whether or not the notice went out.
					Logging.LogException(ex, $"Micro-learning notice to {userId} in department {departmentId} failed.");
				}
			}

			return sent;
		}

		private async Task<IReadOnlyCollection<string>> AudienceAsync(DispatchProtocol protocol)
		{
			var rules = (await _audiences.GetForProtocolAsync(protocol.DepartmentId, protocol.DispatchProtocolId) ?? new List<ProtocolAudience>())
				.Where(a => a.DepartmentId == protocol.DepartmentId).Cast<IContentAudienceRule>().ToList();
			if (rules.Count == 0)
				rules.Add(new ContentAudienceRule(ContentAudienceKinds.Everyone));
			var resolution = await _audienceService.ResolveAsync(protocol.DepartmentId, rules);
			return resolution?.MemberUserIds ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}

		public async Task<List<ProtocolMicroLearningItem>> GetMyMicroLearningAsync(int departmentId, string userId, int take)
		{
			var result = new List<ProtocolMicroLearningItem>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return result;
			var names = new Dictionary<int, DispatchProtocol>();
			foreach (var item in await _micro.GetForUserAsync(departmentId, userId, Math.Clamp(take, 1, 100)) ?? new List<LmsMicroLearningItem>())
				if (item.DepartmentId == departmentId && string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase))
					result.Add(await ToItemAsync(item, names));
			return result;
		}

		public async Task<ProtocolMicroLearningItem> GetMicroLearningAsync(int departmentId, string userId, int itemId)
		{
			var item = departmentId <= 0 ? null : await _micro.GetItemAsync(departmentId, itemId);
			if (item == null || item.DepartmentId != departmentId || !string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return ProtocolMicroLearningItem.Fail(ProtocolLearningErrors.NotFound);
			return await ToItemAsync(item, new Dictionary<int, DispatchProtocol>());
		}

		public async Task<ProtocolMicroLearningItem> AnswerMicroLearningAsync(int departmentId, string userId, int itemId, IEnumerable<string> keys, CancellationToken cancellationToken = default)
		{
			var item = departmentId <= 0 ? null : await _micro.GetItemAsync(departmentId, itemId);
			if (item == null || item.DepartmentId != departmentId || !string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return ProtocolMicroLearningItem.Fail(ProtocolLearningErrors.NotFound);
			if (item.AnsweredOn.HasValue)
			{
				var answered = await ToItemAsync(item, new Dictionary<int, DispatchProtocol>());
				answered.Error = ProtocolLearningErrors.AlreadyAnswered;
				return answered;
			}

			var snapshot = LmsAttemptSnapshot.Parse(item.SnapshotJson);
			var question = snapshot.Questions.FirstOrDefault();
			if (question == null)
				return ProtocolMicroLearningItem.Fail(ProtocolLearningErrors.NoQuestions);
			var cleaned = CleanAnswers(snapshot, new[] { new ProtocolQuizAnswer { QuestionId = question.QuestionId, Keys = (keys ?? Enumerable.Empty<string>()).ToList() } });
			if (cleaned.Count == 0 || cleaned[0].Keys.Count == 0)
				return ProtocolMicroLearningItem.Fail(ProtocolLearningErrors.InvalidInput);

			var outcome = LmsAssessmentEngine.Score(snapshot, cleaned);
			item.AnsweredOn = DateTime.UtcNow;
			item.AnswersJson = JsonConvert.SerializeObject(cleaned[0].Keys);
			item.Correct = outcome.Results.FirstOrDefault()?.Correct ?? false;
			if (!await _micro.TryAnswerAsync(item, cancellationToken))
			{
				var stored = await _micro.GetItemAsync(departmentId, itemId) ?? item;
				var view = await ToItemAsync(stored, new Dictionary<int, DispatchProtocol>());
				view.Error = ProtocolLearningErrors.AlreadyAnswered;
				return view;
			}

			return await ToItemAsync(item, new Dictionary<int, DispatchProtocol>());
		}

		/// <summary>
		/// The member's view of one weekly question: before an answer, the question and options only; after it, the chosen keys, the correct
		/// keys and the explanation.
		/// </summary>
		private async Task<ProtocolMicroLearningItem> ToItemAsync(LmsMicroLearningItem item, Dictionary<int, DispatchProtocol> protocols)
		{
			if (!protocols.TryGetValue(item.DispatchProtocolId, out var protocol))
				protocols[item.DispatchProtocolId] = protocol = await _protocols.GetProtocolHeaderByIdAsync(item.DispatchProtocolId);
			return ToItem(item, protocol);
		}

		public static ProtocolMicroLearningItem ToItem(LmsMicroLearningItem item, DispatchProtocol protocol)
		{
			var snapshot = LmsAttemptSnapshot.Parse(item.SnapshotJson);
			var view = new ProtocolMicroLearningItem
			{
				Id = item.LmsMicroLearningItemId,
				ProtocolId = item.DispatchProtocolId,
				ProtocolCode = protocol?.DepartmentId == item.DepartmentId ? protocol.Code : null,
				ProtocolName = protocol?.DepartmentId == item.DepartmentId ? protocol.Name : null,
				WeekKey = item.WeekKey,
				SentOn = item.SentOn,
				AnsweredOn = item.AnsweredOn,
				Correct = item.Correct,
				Question = LearnerQuestions(snapshot).FirstOrDefault()
			};
			if (item.AnsweredOn.HasValue)
			{
				view.ChosenKeys = ParseKeys(item.AnswersJson);
				var keyed = snapshot.Questions.FirstOrDefault();
				view.CorrectKeys = keyed?.CorrectKeys?.ToList() ?? new List<string>();
				view.Explanation = keyed?.Explanation;
			}
			return view;
		}

		private static List<string> ParseKeys(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<string>();
			try
			{
				return JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
			}
			catch (JsonException)
			{
				return new List<string>();
			}
		}

		#endregion Micro-learning

		private void Audit(int departmentId, string userId, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ProtocolLearningSettingsChanged,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
