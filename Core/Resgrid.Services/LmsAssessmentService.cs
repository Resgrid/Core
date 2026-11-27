using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Assessments (T2 item 4). Attempts are drawn and scored on the server only (R-T11): the browser and the apps receive questions
	/// and options, never the key. Each attempt records the quiz it was taken against (R-T6: a revision hash and a snapshot of the
	/// questions, the options in the order shown and the key) in the LMS attempts table (R-T7). Time limits run on the server clock.
	/// Short answers wait in the grading queue (permission 133); item analysis reports difficulty and discrimination per question.
	/// Per-question answers are visible to the member and graders only (R-T12).
	/// </summary>
	public class LmsAssessmentService : ILmsAssessmentService
	{
		/// <summary>Grace after a deadline for the submit request itself to arrive.</summary>
		public static readonly TimeSpan SubmitGrace = TimeSpan.FromSeconds(30);

		private readonly ILmsAssessmentAttemptsRepository _attempts;
		private readonly ILmsEnrollmentsRepository _enrollments;
		private readonly ILmsLessonsRepository _lessons;
		private readonly ILmsCourseVersionsRepository _versions;
		private readonly ILmsCoursesRepository _courses;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly ILmsQuestionsRepository _questions;
		private readonly ILmsGateService _gate;
		private readonly ILmsLearnerService _learner;
		private readonly IEventAggregator _eventAggregator;
		private readonly ILmsXapiStatementQueue _xapi;

		/// <summary>The draw's randomness (a seam for tests).</summary>
		public Func<Random> RandomFactory { get; set; } = () => new Random();

		public LmsAssessmentService(ILmsAssessmentAttemptsRepository attempts, ILmsEnrollmentsRepository enrollments, ILmsLessonsRepository lessons,
			ILmsCourseVersionsRepository versions, ILmsCoursesRepository courses, ILmsCourseOwnersRepository owners, ILmsQuestionsRepository questions,
			ILmsGateService gate, ILmsLearnerService learner, IEventAggregator eventAggregator, ILmsXapiStatementQueue xapi)
		{
			_attempts = attempts;
			_enrollments = enrollments;
			_lessons = lessons;
			_versions = versions;
			_courses = courses;
			_owners = owners;
			_questions = questions;
			_gate = gate;
			_learner = learner;
			_eventAggregator = eventAggregator;
			_xapi = xapi;
		}

		/// <summary>
		/// T5 xAPI out: a finished, scored attempt becomes "passed" or "failed" for the lesson (department courses only; pre-tests are not
		/// results). Non-fatal; the queue drops it when the department has no LRS.
		/// </summary>
		private async Task QueueAttemptXapiAsync(LmsAssessmentAttempt attempt, LmsLesson lesson, CancellationToken cancellationToken)
		{
			if (_xapi == null || attempt == null || !attempt.Passed.HasValue || attempt.IsPreTest)
				return;
			try
			{
				var version = await _versions.GetVisibleByIdAsync(attempt.DepartmentId, attempt.LmsCourseVersionId);
				if (version == null || version.DepartmentId != attempt.DepartmentId)
					return;
				await _xapi.EnqueueAsync(new LmsXapiEvent
				{
					DepartmentId = attempt.DepartmentId,
					UserId = attempt.UserId,
					VerbId = attempt.Passed.Value ? LmsXapiVerbs.Passed : LmsXapiVerbs.Failed,
					LmsCourseId = version.LmsCourseId,
					CourseTitle = version.Title,
					LmsLessonId = attempt.LmsLessonId,
					LessonTitle = lesson?.Title,
					Success = attempt.Passed.Value,
					ScorePercent = attempt.Percent,
					DurationSeconds = attempt.SubmittedOn.HasValue ? (int)Math.Max(0, Math.Min(int.MaxValue, (attempt.SubmittedOn.Value - attempt.StartedOn).TotalSeconds)) : (int?)null,
					OccurredOn = attempt.SubmittedOn ?? DateTime.UtcNow
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "An assessment xAPI statement could not be queued.");
			}
		}

		public async Task<LmsAttemptView> StartAttemptAsync(int departmentId, string userId, int lmsEnrollmentId, int lmsLessonId, bool attested, string ipAddress,
			string userAgent, string locale, CancellationToken cancellationToken = default)
		{
			var player = await _learner.GetPlayerAsync(departmentId, userId, lmsEnrollmentId, lmsLessonId);
			if (player.Error != LmsErrors.None)
				return new LmsAttemptView { Error = player.Error };
			var lesson = player.Lesson;
			if (lesson == null || lesson.LmsLessonId != lmsLessonId || lesson.Kind != (int)LmsLessonKinds.Assessment)
				return new LmsAttemptView { Error = player.LockedLessonIds.Contains(lmsLessonId) ? LmsErrors.LessonLocked : LmsErrors.NotFound };

			var now = DateTime.UtcNow;
			var open = LmsAttemptRules.OpenAttempt(player.Attempts, now);
			if (open != null)
				return View(open, lesson, now);

			var (blocked, _) = LmsAttemptRules.CanStart(lesson, player.Attempts, now);
			if (blocked != LmsErrors.None)
				return new LmsAttemptView { Error = blocked, Lesson = lesson };
			if (lesson.RequireAttestation && !attested)
				return new LmsAttemptView { Error = LmsErrors.InvalidInput, Lesson = lesson };

			// Expire an attempt whose time ran out before it was submitted (it counts as used).
			foreach (var stale in player.Attempts.Where(a => a.Status == (int)LmsAttemptStatuses.InProgress && a.DeadlineOn.HasValue && a.DeadlineOn.Value <= now))
				await ExpireAsync(stale, cancellationToken);

			var pool = lesson.LmsQuestionBankId.HasValue ? await _questions.GetForBankAsync(departmentId, lesson.LmsQuestionBankId.Value) ?? new List<LmsQuestion>() : new List<LmsQuestion>();
			var snapshot = LmsAssessmentEngine.Draw(pool, lesson.DrawCount, lesson.DrawTags, lesson.RandomizeAnswers, lesson.PassMark, RandomFactory(), locale);
			if (snapshot.Questions.Count == 0)
				return new LmsAttemptView { Error = LmsErrors.InvalidInput, Lesson = lesson };

			var attempt = new LmsAssessmentAttempt
			{
				DepartmentId = departmentId,
				LmsCourseVersionId = player.Enrollment.LmsCourseVersionId,
				LmsLessonId = lesson.LmsLessonId,
				LmsEnrollmentId = player.Enrollment.LmsEnrollmentId,
				UserId = player.Enrollment.UserId,
				AttemptNumber = player.Attempts.Count + 1,
				Status = (int)LmsAttemptStatuses.InProgress,
				StartedOn = now,
				DeadlineOn = lesson.TimeLimitMinutes > 0 ? now.AddMinutes(lesson.TimeLimitMinutes) : (DateTime?)null,
				IsPreTest = lesson.IsPreTest,
				PassMark = lesson.PassMark,
				RevisionHash = LmsAssessmentEngine.RevisionHash(snapshot),
				SnapshotJson = JsonConvert.SerializeObject(snapshot),
				Attested = attested,
				IpAddress = LmsCourseService.Truncate(ipAddress, 64),
				UserAgent = LmsCourseService.Truncate(userAgent, 512)
			};
			await _attempts.InsertAsync(attempt, cancellationToken, true);
			return View(attempt, lesson, now);
		}

		public async Task<LmsAttemptView> GetAttemptAsync(int departmentId, string userId, int lmsAssessmentAttemptId)
		{
			var attempt = await _attempts.GetByIdForDepartmentAsync(departmentId, lmsAssessmentAttemptId);
			if (attempt == null || !string.Equals(attempt.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return new LmsAttemptView { Error = LmsErrors.NotFound };
			var lesson = await _lessons.GetVisibleByIdAsync(departmentId, attempt.LmsLessonId);
			// Round 10 A7: answering an open attempt of a department course is a learner action (the member's LMS seat); a finished one stays
			// readable as the member's own record.
			if (attempt.Status == (int)LmsAttemptStatuses.InProgress && !await SeatedForAsync(departmentId, userId, attempt))
				return new LmsAttemptView { Error = LmsErrors.NotLicensed, Attempt = attempt, Lesson = lesson };
			return View(attempt, lesson, DateTime.UtcNow);
		}

		/// <summary>
		/// Round 10 A7 (owner decision 2026-11-26): whether the member may still act on this attempt as a learner: an attempt of a department
		/// course (or one whose course cannot be read) needs their LMS seat; the Academy and external learners never ask.
		/// </summary>
		private async Task<bool> SeatedForAsync(int departmentId, string userId, LmsAssessmentAttempt attempt)
		{
			var version = await _versions.GetVisibleByIdAsync(departmentId, attempt.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(departmentId, version.LmsCourseId);
			if (!LmsLearnerSeats.Needed(course?.IsSystem ?? false, userId))
				return true;
			return await _gate.HoldsLearnerSeatAsync(departmentId, userId);
		}

		private static LmsAttemptView View(LmsAssessmentAttempt attempt, LmsLesson lesson, DateTime now)
		{
			var view = new LmsAttemptView
			{
				Attempt = attempt,
				Lesson = lesson,
				Questions = LmsAssessmentEngine.ToLearnerView(LmsAttemptSnapshot.Parse(attempt.SnapshotJson))
			};
			if (attempt.Status != (int)LmsAttemptStatuses.InProgress)
				view.Error = LmsErrors.AlreadySubmitted;
			else if (attempt.DeadlineOn.HasValue)
			{
				var remaining = (int)Math.Floor((attempt.DeadlineOn.Value - now).TotalSeconds);
				view.SecondsRemaining = Math.Max(0, remaining);
				if (remaining <= 0)
					view.Error = LmsErrors.TimeExpired;
			}
			return view;
		}

		public async Task<LmsAttemptResultView> SubmitAttemptAsync(int departmentId, string userId, int lmsAssessmentAttemptId, IEnumerable<LmsAttemptAnswer> answers,
			CancellationToken cancellationToken = default)
		{
			var attempt = await _attempts.GetByIdForDepartmentAsync(departmentId, lmsAssessmentAttemptId);
			if (attempt == null || !string.Equals(attempt.UserId, userId, StringComparison.OrdinalIgnoreCase))
				return new LmsAttemptResultView { Error = LmsErrors.NotFound };
			if (attempt.Status != (int)LmsAttemptStatuses.InProgress)
				return new LmsAttemptResultView { Error = LmsErrors.AlreadySubmitted, Attempt = attempt };
			// Round 10 A7: submitting is a learner action; without the LMS seat nothing is scored or changed (the attempt stays open).
			if (!await SeatedForAsync(departmentId, userId, attempt))
				return new LmsAttemptResultView { Error = LmsErrors.NotLicensed, Attempt = attempt };

			var lesson = await _lessons.GetVisibleByIdAsync(departmentId, attempt.LmsLessonId);
			var now = DateTime.UtcNow;
			if (attempt.DeadlineOn.HasValue && now > attempt.DeadlineOn.Value.Add(SubmitGrace))
			{
				await ExpireAsync(attempt, cancellationToken);
				return new LmsAttemptResultView { Error = LmsErrors.TimeExpired, Attempt = attempt, Lesson = lesson };
			}

			var snapshot = LmsAttemptSnapshot.Parse(attempt.SnapshotJson);
			var known = new HashSet<int>(snapshot.Questions.Select(q => q.QuestionId));
			var cleaned = (answers ?? Enumerable.Empty<LmsAttemptAnswer>()).Where(a => a != null && known.Contains(a.QuestionId))
				.Select(a => new LmsAttemptAnswer
				{
					QuestionId = a.QuestionId,
					Keys = (a.Keys ?? new List<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Take(LmsQuestionBankService.MaxOptions).ToList(),
					Text = a.Text == null ? null : LmsCourseService.Truncate(a.Text.Trim(), 4000)
				}).ToList();

			var outcome = LmsAssessmentEngine.Score(snapshot, cleaned);
			var expected = attempt.RowVersion;
			attempt.AnswersJson = JsonConvert.SerializeObject(cleaned);
			attempt.ResultsJson = JsonConvert.SerializeObject(outcome.Results);
			attempt.Score = outcome.Score;
			attempt.MaxScore = outcome.MaxScore;
			attempt.Percent = outcome.Percent;
			attempt.Passed = outcome.Passed;
			attempt.SubmittedOn = now;
			attempt.Status = outcome.PendingGrading ? (int)LmsAttemptStatuses.PendingGrading : (int)LmsAttemptStatuses.Scored;
			if (!await _attempts.TryUpdateAsync(attempt, expected, cancellationToken))
				return new LmsAttemptResultView { Error = LmsErrors.AlreadySubmitted, Attempt = await _attempts.GetByIdForDepartmentAsync(departmentId, lmsAssessmentAttemptId), Lesson = lesson };

			RaiseFailed(attempt, lesson?.Title);
			// Workflow trigger 272: an answer needs a grader (the compare-and-set above submitted it once).
			if (attempt.Status == (int)LmsAttemptStatuses.PendingGrading)
				await RaiseAttemptAsync(departmentId, attempt, lesson, (a, l, v) => LmsWorkflowEvents.AwaitingGrading(a, l, v, DateTime.UtcNow));
			await QueueAttemptXapiAsync(attempt, lesson, cancellationToken);
			var completed = attempt.Passed == true && await _learner.OnAttemptFinishedAsync(attempt, cancellationToken);
			var view = Review(attempt, lesson, includeAnswersForGrader: false);
			view.CourseCompleted = completed;
			return view;
		}

		/// <summary>
		/// Training T3 workflow trigger 226 (TrainingFailed): a finished, scored attempt that did not pass (pre-tests excluded: failing one only
		/// means the learner takes the course). The payload carries the lesson title and the score, never the member's contact data.
		/// </summary>
		private void RaiseFailed(LmsAssessmentAttempt attempt, string lessonTitle)
		{
			if (attempt == null || attempt.Passed != false || attempt.IsPreTest)
				return;

			_eventAggregator?.SendMessage(new TrainingWorkflowEvent
			{
				DepartmentId = attempt.DepartmentId,
				Change = TrainingWorkflowChanges.Failed,
				UserId = attempt.UserId,
				Subject = "Assessment",
				TrainingName = lessonTitle,
				State = "Failed",
				CompletedOn = attempt.SubmittedOn,
				Score = attempt.Percent,
				OccurredOn = DateTime.UtcNow
			});
		}

		private async Task RaiseAttemptAsync(int departmentId, LmsAssessmentAttempt attempt, LmsLesson lesson,
			Func<LmsAssessmentAttempt, LmsLesson, LmsCourseVersion, CourseWorkflowEvent> build)
		{
			LmsCourseVersion version = null;
			try
			{
				version = await _versions.GetVisibleByIdAsync(departmentId, attempt.LmsCourseVersionId);
			}
			catch (Exception ex)
			{
				// The course's name is a convenience in the payload; the event goes out with the ids.
				Logging.LogException(ex, $"The course version of attempt {attempt.LmsAssessmentAttemptId} could not be read.");
			}

			ContentWorkflowContext.Publish(_eventAggregator, build(attempt, lesson, version));
		}

		private async Task ExpireAsync(LmsAssessmentAttempt attempt, CancellationToken cancellationToken)
		{
			var expected = attempt.RowVersion;
			attempt.Status = (int)LmsAttemptStatuses.Expired;
			attempt.Passed = false;
			attempt.SubmittedOn ??= attempt.DeadlineOn;
			await _attempts.TryUpdateAsync(attempt, expected, cancellationToken);
		}

		public async Task<LmsAttemptResultView> GetResultAsync(LmsActor actor, int lmsAssessmentAttemptId)
		{
			var attempt = await _attempts.GetByIdForDepartmentAsync(actor?.DepartmentId ?? 0, lmsAssessmentAttemptId);
			if (attempt == null)
				return new LmsAttemptResultView { Error = LmsErrors.NotFound };
			var own = string.Equals(attempt.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase);
			// R-T12: a member's answers and failed attempts are theirs and the graders' only.
			if (!own && !actor.MayGrade)
				return new LmsAttemptResultView { Error = LmsErrors.NotAuthorized };
			var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, attempt.LmsLessonId);
			return Review(attempt, lesson, includeAnswersForGrader: !own);
		}

		/// <summary>
		/// The attempt as a learner may see it: the answer-review policy decides correctness and the key. A grader looking at another
		/// member's attempt sees everything.
		/// </summary>
		internal static LmsAttemptResultView Review(LmsAssessmentAttempt attempt, LmsLesson lesson, bool includeAnswersForGrader)
		{
			var policy = (LmsShowAnswersPolicies)(lesson?.ShowAnswersPolicy ?? (int)LmsShowAnswersPolicies.Never);
			var view = new LmsAttemptResultView
			{
				Attempt = attempt,
				Lesson = lesson,
				ShowCorrectness = includeAnswersForGrader || (attempt.Status != (int)LmsAttemptStatuses.InProgress && LmsAssessmentEngine.MayShowCorrectness(policy, attempt.Passed)),
				ShowAnswers = includeAnswersForGrader || (attempt.Status != (int)LmsAttemptStatuses.InProgress && LmsAssessmentEngine.MayShowAnswers(policy, attempt.Passed))
			};

			var snapshot = LmsAttemptSnapshot.Parse(attempt.SnapshotJson);
			var learnerQuestions = LmsAssessmentEngine.ToLearnerView(snapshot).ToDictionary(q => q.QuestionId);
			var answers = LmsAttemptAnswer.Parse(attempt.AnswersJson).GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.Last());
			var results = LmsQuestionResult.Parse(attempt.ResultsJson).GroupBy(r => r.QuestionId).ToDictionary(g => g.Key, g => g.Last());
			foreach (var question in snapshot.Questions)
			{
				results.TryGetValue(question.QuestionId, out var result);
				answers.TryGetValue(question.QuestionId, out var answer);
				view.Questions.Add(new LmsReviewedQuestion
				{
					Question = learnerQuestions[question.QuestionId],
					Answer = answer,
					AwaitingGrade = result?.AwaitingGrade == true,
					Correct = view.ShowCorrectness && result != null && !result.AwaitingGrade ? result.Correct : (bool?)null,
					CorrectKeys = view.ShowAnswers ? (question.Kind == (int)LmsQuestionKinds.Ordering ? question.CorrectOrder : question.CorrectKeys) : null,
					Explanation = view.ShowAnswers ? question.Explanation : null
				});
			}
			return view;
		}

		public async Task<List<LmsGradingItem>> GetGradingQueueAsync(LmsActor actor)
		{
			var items = new List<LmsGradingItem>();
			if (actor == null || !actor.MayGrade)
				return items;

			foreach (var attempt in await _attempts.GetPendingGradingAsync(actor.DepartmentId) ?? new List<LmsAssessmentAttempt>())
			{
				var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, attempt.LmsLessonId);
				var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, attempt.LmsCourseVersionId);
				var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
				var snapshot = LmsAttemptSnapshot.Parse(attempt.SnapshotJson);
				var answers = LmsAttemptAnswer.Parse(attempt.AnswersJson).GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.Last());
				var results = LmsQuestionResult.Parse(attempt.ResultsJson);
				var modelAnswers = new Dictionary<int, string>();
				foreach (var id in results.Where(r => r.AwaitingGrade).Select(r => r.QuestionId))
					modelAnswers[id] = (await _questions.GetVisibleByIdAsync(actor.DepartmentId, id))?.Explanation;
				items.Add(new LmsGradingItem
				{
					Attempt = attempt,
					CourseTitle = version?.Title ?? course?.Title,
					LessonTitle = lesson?.Title,
					Questions = results.Where(r => r.AwaitingGrade).Select(r => new LmsGradingQuestion
					{
						QuestionId = r.QuestionId,
						Text = snapshot.Questions.FirstOrDefault(q => q.QuestionId == r.QuestionId)?.Text,
						ModelAnswer = modelAnswers.TryGetValue(r.QuestionId, out var model) ? model : null,
						AnswerText = answers.TryGetValue(r.QuestionId, out var answer) ? answer.Text : null,
						Points = r.Points
					}).ToList()
				});
			}
			return items;
		}

		public async Task<LmsAttemptResultView> GradeAsync(LmsActor actor, int lmsAssessmentAttemptId, IEnumerable<LmsManualGrade> grades, string notes,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.MayGrade)
				return new LmsAttemptResultView { Error = LmsErrors.NotAuthorized };
			var attempt = await _attempts.GetByIdForDepartmentAsync(actor.DepartmentId, lmsAssessmentAttemptId);
			if (attempt == null)
				return new LmsAttemptResultView { Error = LmsErrors.NotFound };
			if (attempt.Status != (int)LmsAttemptStatuses.PendingGrading)
				return new LmsAttemptResultView { Error = LmsErrors.AlreadySubmitted, Attempt = attempt };
			// Nobody grades their own attempt.
			if (string.Equals(attempt.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				return new LmsAttemptResultView { Error = LmsErrors.NotAuthorized };

			var snapshot = LmsAttemptSnapshot.Parse(attempt.SnapshotJson);
			var pending = LmsQuestionResult.Parse(attempt.ResultsJson).Where(r => r.AwaitingGrade).Select(r => r.QuestionId).ToHashSet();
			var gradeList = (grades ?? Enumerable.Empty<LmsManualGrade>()).Where(g => g != null && pending.Contains(g.QuestionId)).ToList();
			if (!pending.All(id => gradeList.Any(g => g.QuestionId == id)))
				return new LmsAttemptResultView { Error = LmsErrors.InvalidInput, Attempt = attempt };

			var outcome = LmsAssessmentEngine.Score(snapshot, LmsAttemptAnswer.Parse(attempt.AnswersJson), gradeList);
			var expected = attempt.RowVersion;
			attempt.ResultsJson = JsonConvert.SerializeObject(outcome.Results);
			attempt.Score = outcome.Score;
			attempt.MaxScore = outcome.MaxScore;
			attempt.Percent = outcome.Percent;
			attempt.Passed = outcome.Passed;
			attempt.Status = (int)LmsAttemptStatuses.Graded;
			attempt.GradedByUserId = actor.UserId;
			attempt.GradedOn = DateTime.UtcNow;
			attempt.GradeNotes = LmsCourseService.Truncate(notes?.Trim(), 2000);
			if (!await _attempts.TryUpdateAsync(attempt, expected, cancellationToken))
				return new LmsAttemptResultView { Error = LmsErrors.StaleCopy, Attempt = attempt };

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsAttemptGraded,
				After = JsonConvert.SerializeObject(new { attempt.LmsAssessmentAttemptId, attempt.UserId, attempt.Percent, attempt.Passed }),
				Successful = true,
				ServerName = Environment.MachineName
			});

			var gradedLesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, attempt.LmsLessonId);
			// Workflow trigger 261 (the compare-and-set above graded it once): pass or fail and the score, never an answer or the notes.
			await RaiseAttemptAsync(actor.DepartmentId, attempt, gradedLesson, (a, l, v) => LmsWorkflowEvents.AssessmentGraded(a, l, v, v?.LmsCourseId, DateTime.UtcNow));
			RaiseFailed(attempt, gradedLesson?.Title);
			await QueueAttemptXapiAsync(attempt, gradedLesson, cancellationToken);
			var completed = attempt.Passed == true && await _learner.OnAttemptFinishedAsync(attempt, cancellationToken);
			var view = Review(attempt, gradedLesson, includeAnswersForGrader: true);
			view.CourseCompleted = completed;
			return view;
		}

		public async Task<LmsItemAnalysisReport> GetItemAnalysisAsync(LmsActor actor, int lmsLessonId)
		{
			if (actor == null || !actor.MayGrade)
				return null;
			var lesson = await _lessons.GetVisibleByIdAsync(actor.DepartmentId, lmsLessonId);
			if (lesson == null || lesson.Kind != (int)LmsLessonKinds.Assessment)
				return null;
			var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			var attempts = await _attempts.GetFinishedForLessonAsync(actor.DepartmentId, lmsLessonId) ?? new List<LmsAssessmentAttempt>();
			return new LmsItemAnalysisReport
			{
				Course = course,
				Lesson = lesson,
				Attempts = attempts.Count,
				PassRate = attempts.Count == 0 ? 0 : Math.Round(attempts.Count(a => a.Passed == true) * 100m / attempts.Count, 1),
				Items = LmsAssessmentEngine.ItemAnalysis(attempts)
			};
		}

		public async Task<LmsPrintableTest> GetPrintableTestAsync(LmsActor actor, int lmsLessonId, bool withKey)
		{
			var lesson = await _lessons.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsLessonId);
			if (lesson == null || lesson.Kind != (int)LmsLessonKinds.Assessment || lesson.DepartmentId != actor.DepartmentId || !lesson.LmsQuestionBankId.HasValue)
				return null;
			var version = await _versions.GetVisibleByIdAsync(actor.DepartmentId, lesson.LmsCourseVersionId);
			var course = version == null ? null : await _courses.GetVisibleByIdAsync(actor.DepartmentId, version.LmsCourseId);
			if (course == null)
				return null;
			var rights = actor.RightsOn(course, await _owners.GetForCourseAsync(actor.DepartmentId, course.LmsCourseId));
			if (!rights.Edit && !rights.Assign && !actor.MayGrade)
				return null;

			var pool = await _questions.GetForBankAsync(actor.DepartmentId, lesson.LmsQuestionBankId.Value) ?? new List<LmsQuestion>();
			var snapshot = LmsAssessmentEngine.Draw(pool, lesson.DrawCount, lesson.DrawTags, lesson.RandomizeAnswers, lesson.PassMark, RandomFactory());
			var includeKey = withKey && (rights.Edit || actor.MayGrade);
			if (!includeKey)
				foreach (var question in snapshot.Questions)
				{
					question.CorrectKeys = new List<string>();
					question.CorrectOrder = new List<string>();
					question.Explanation = null;
				}
			return new LmsPrintableTest { Course = course, Lesson = lesson, Questions = snapshot.Questions, IncludesKey = includeKey };
		}
	}
}
