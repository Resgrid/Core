using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Trainings;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Baseline training (Training T0 item 3, protocols-training-knowledge-plan §13): the member's My Training, reading a training,
	/// its attachments, taking the quiz (scored on the server: questions and options go out, the answer key never does, R-T11),
	/// their own attempts, self-enrolment, "flag this question" and certification pins; and the officer's report. Every endpoint
	/// is refused while the department's Trainings module is switched off. Which trainings a member may open, take or report on
	/// comes from IAuthorizationService.GetTrainingManagementScopeAsync, as on the web pages. Omni's Training screen uses this.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class TrainingsController : V4AuthenticatedApiControllerbase
	{
		private readonly ITrainingService _trainingService;
		private readonly ITrainingAssignmentService _assignmentService;
		private readonly IMyTrainingService _myTrainingService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentSettingsService _departmentSettingsService;

		public TrainingsController(ITrainingService trainingService, ITrainingAssignmentService assignmentService, IMyTrainingService myTrainingService,
			IAuthorizationService authorizationService, IDepartmentSettingsService departmentSettingsService)
		{
			_trainingService = trainingService;
			_assignmentService = assignmentService;
			_myTrainingService = myTrainingService;
			_authorizationService = authorizationService;
			_departmentSettingsService = departmentSettingsService;
		}

		/// <summary>True when the department switched the Trainings module off (T0 item 1b): every endpoint then answers 404.</summary>
		private async Task<bool> TrainingDisabledAsync()
		{
			try
			{
				var modules = await _departmentSettingsService.GetDepartmentModuleSettingsAsync(DepartmentId);
				return modules != null && modules.TrainingDisabled;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Training module state could not be read for department {DepartmentId}");
				return false;
			}
		}

		private Task<TrainingManagementScope> ScopeAsync() => _authorizationService.GetTrainingManagementScopeAsync(UserId, DepartmentId);

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#region Member

		/// <summary>
		/// The member's My Training: their assignments with state and due moment (overdue, due soon, in progress, open and recently
		/// completed), the catalog of open trainings they are not assigned to, and their certifications that are due or pinned.
		/// </summary>
		[HttpGet("GetMyTrainings")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<MyTrainingsResult>> GetMyTrainings()
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var now = DateTime.UtcNow;
			var overview = await _myTrainingService.GetOverviewAsync(DepartmentId, UserId, now) ?? new MyTrainingOverview();
			var result = new MyTrainingsResult();

			result.Data.Assignments = overview.Overdue.Concat(overview.DueSoon).Concat(overview.InProgress).Concat(overview.Other)
				.OrderBy(x => x.DueUtc.HasValue ? 0 : 1).ThenBy(x => x.DueUtc)
				.Concat(overview.RecentCompletions)
				.Select(x => MapAssignment(x, now))
				.ToList();
			result.Data.Catalog = overview.Catalog.Select(t => new TrainingCatalogData
			{
				TrainingId = t.TrainingId,
				Name = t.Name,
				Summary = PlainText(t.Description),
				CanSelfEnroll = t.AllowSelfEnroll && !t.ClosedToNewAssignees,
				HasQuiz = t.HasQuiz,
				Hours = t.Hours,
				DueDays = t.DueDays,
				CreatedOn = t.CreatedOn
			}).ToList();
			result.Data.Certifications = overview.Certifications.Select(c => new TrainingCertificationData
			{
				CertificationId = c.Certification?.PersonnelCertificationId ?? 0,
				Name = c.Name,
				ExpiresOn = c.ExpiresOn,
				Pinned = c.Pinned,
				IsExpired = c.IsExpired,
				DueSoon = c.DueSoon,
				DaysLeft = c.DaysLeft
			}).ToList();
			result.Data.OnLeave = overview.OnLeave;

			return Populate(result, result.Data.Assignments.Count);
		}

		/// <summary>
		/// One training: its content (sanitized; allow-listed embeds only), attachment metadata and, for its assignees and authors,
		/// the quiz as questions and options. Opening it marks the member's assignment viewed (written once).
		/// </summary>
		[HttpGet("GetTraining")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<TrainingResult>> GetTraining(int trainingId, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var training = await _trainingService.GetTrainingByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId)
				return Unauthorized();

			var now = DateTime.UtcNow;
			var scope = await ScopeAsync();
			if (!scope.CanOpen(training, now))
				return Unauthorized();

			var viewed = await _trainingService.SetTrainingAsViewedAsync(trainingId, UserId, cancellationToken);
			var assignment = viewed ?? scope.AssignmentOf(training);

			TrainingAssignmentData assignmentData = null;
			if (assignment != null && !assignment.RemovedOn.HasValue)
			{
				var status = (await _trainingService.GetAssignmentsForUserAsync(DepartmentId, UserId, now))?.FirstOrDefault(x => x.Training?.TrainingId == trainingId);
				assignmentData = status != null ? MapAssignment(status, now) : null;
			}

			var result = new TrainingResult { Data = MapTraining(training, assignmentData, scope.CanOpenQuiz(training, now)) };
			return Populate(result, 1);
		}

		/// <summary>An attachment of a training the member may open: the one place its bytes are served (T-D22).</summary>
		[HttpGet("GetTrainingAttachment")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult> GetTrainingAttachment(int trainingAttachmentId)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var metadata = await _trainingService.GetTrainingAttachmentByIdAsync(trainingAttachmentId);
			if (metadata == null)
				return NotFound();

			var training = await _trainingService.GetTrainingRowByIdAsync(metadata.TrainingId);
			if (training == null || training.DepartmentId != DepartmentId)
				return Unauthorized();

			if (!(await ScopeAsync()).CanOpen(training))
				return Unauthorized();

			var attachment = await _trainingService.GetTrainingAttachmentWithDataAsync(trainingAttachmentId);
			if (attachment?.Data == null)
				return NotFound();

			return File(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType, attachment.FileName);
		}

		/// <summary>
		/// Submits the member's quiz answers. Scored on the server against the training's own questions and options: unknown ids
		/// are dropped, each question counts once (at most <see cref="TrainingQuizScorer.MaxQuestions"/>), the attempt limit is
		/// enforced and the attempt is kept with its snapshot. The response carries the score and result, never the answer key.
		/// </summary>
		[HttpPost("SubmitQuiz")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<TrainingQuizSubmissionResult>> SubmitQuiz([FromBody] SubmitTrainingQuizInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			if (input == null || input.TrainingId <= 0)
				return BadRequest();

			var submission = await _trainingService.RecordQuizAttemptAsync(DepartmentId, input.TrainingId, UserId, ToAnswers(input.Answers), cancellationToken);
			var result = new TrainingQuizSubmissionResult();
			result.Data.Status = submission.Status.ToString();
			result.Data.AttemptsLeft = submission.AttemptsLeft;

			switch (submission.Status)
			{
				case TrainingQuizSubmissionStatuses.Recorded:
					result.Data.Recorded = true;
					result.Data.Score = submission.Result?.Score ?? 0;
					result.Data.Passed = submission.Result?.Passed ?? false;
					result.Data.QuestionCount = submission.Result?.QuestionCount ?? 0;
					result.Data.CorrectCount = submission.Result?.CorrectCount ?? 0;
					result.Data.AttemptNumber = submission.Attempt?.AttemptNumber ?? 0;
					return Populate(result, 1);
				case TrainingQuizSubmissionStatuses.NotFound:
					return NotFound();
				case TrainingQuizSubmissionStatuses.NotAssigned:
					return Unauthorized();
				default:
					return BadRequest(Populate(result, 0, ResponseHelper.Failure));
			}
		}

		/// <summary>
		/// The posted answers as the scorer reads them: positive ids only, each question once (the first answer for it wins), at
		/// most <see cref="TrainingQuizScorer.MaxQuestions"/> questions and <see cref="TrainingQuizScorer.MaxOptionsPerQuestion"/>
		/// options per question (T-D11).
		/// </summary>
		public static Dictionary<int, List<int>> ToAnswers(IEnumerable<TrainingQuizAnswerInput> answers)
		{
			var map = new Dictionary<int, List<int>>();
			foreach (var answer in answers ?? Enumerable.Empty<TrainingQuizAnswerInput>())
			{
				if (answer == null || answer.QuestionId <= 0 || map.ContainsKey(answer.QuestionId))
					continue;
				if (map.Count >= TrainingQuizScorer.MaxQuestions)
					break;

				map[answer.QuestionId] = (answer.OptionIds ?? new List<int>()).Where(id => id > 0).Distinct().Take(TrainingQuizScorer.MaxOptionsPerQuestion).ToList();
			}

			return map;
		}

		/// <summary>The member's own attempts at a training's quiz: result and time only, never the answers.</summary>
		[HttpGet("GetMyAttempts")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingAttemptsResult>> GetMyAttempts(int trainingId)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null || training.DepartmentId != DepartmentId)
				return NotFound();

			var attempts = await _trainingService.GetAttemptsForMemberAsync(DepartmentId, trainingId, UserId) ?? new List<TrainingQuizAttempt>();
			var result = new TrainingAttemptsResult
			{
				Data = attempts.Where(a => string.Equals(a.UserId, UserId, StringComparison.OrdinalIgnoreCase)).OrderByDescending(a => a.AttemptNumber).Select(a => new TrainingAttemptData
				{
					AttemptNumber = a.AttemptNumber,
					Cycle = a.Cycle,
					SubmittedOn = a.SubmittedOn,
					Score = a.Score,
					Passed = a.Passed,
					QuestionCount = a.QuestionCount,
					CorrectCount = a.CorrectCount
				}).ToList()
			};

			return Populate(result, result.Data.Count);
		}

		/// <summary>Enrols the member in an open training that allows self-enrolment, to be tracked with its due rule.</summary>
		[HttpPost("SelfEnroll")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingAssignmentResult>> SelfEnroll([FromBody] TrainingIdInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			if (input == null || input.TrainingId <= 0)
				return BadRequest();

			var now = DateTime.UtcNow;
			var enrolled = await _assignmentService.SelfEnrollAsync(DepartmentId, input.TrainingId, UserId, now, cancellationToken);
			var result = new TrainingAssignmentResult();
			if (enrolled == null)
				return BadRequest(Populate(result, 0, ResponseHelper.Failure));

			var status = (await _trainingService.GetAssignmentsForUserAsync(DepartmentId, UserId, now))?.FirstOrDefault(x => x.Training?.TrainingId == input.TrainingId);
			result.Data = status != null ? MapAssignment(status, now) : null;
			return Populate(result, result.Data == null ? 0 : 1);
		}

		/// <summary>"Flag this question": the member tells the author a question of a quiz they may take looks wrong.</summary>
		[HttpPost("FlagQuestion")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<TrainingActionResult>> FlagQuestion([FromBody] FlagTrainingQuestionInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			if (input == null || input.TrainingId <= 0 || input.QuestionId <= 0)
				return BadRequest();

			var training = await _trainingService.GetTrainingRowByIdAsync(input.TrainingId);
			if (training == null || training.DepartmentId != DepartmentId)
				return NotFound();

			if (!(await ScopeAsync()).CanOpenQuiz(training))
				return Unauthorized();

			var flag = await _trainingService.FlagQuestionAsync(DepartmentId, input.TrainingId, input.QuestionId, UserId, input.Comment, cancellationToken);
			var result = new TrainingActionResult { Data = flag != null };
			return flag == null ? BadRequest(Populate(result, 0, ResponseHelper.Failure)) : Populate(result, 1);
		}

		/// <summary>Pins (or unpins) one of the member's own certifications to My Training.</summary>
		[HttpPost("PinCertification")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<TrainingActionResult>> PinCertification([FromBody] PinTrainingCertificationInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			if (input == null || input.CertificationId <= 0)
				return BadRequest();

			var done = await _myTrainingService.PinCertificationAsync(DepartmentId, UserId, input.CertificationId, input.Pinned, cancellationToken);
			var result = new TrainingActionResult { Data = done };
			return done ? Populate(result, 1) : BadRequest(Populate(result, 0, ResponseHelper.Failure));
		}

		#endregion Member

		#region Officer

		/// <summary>
		/// The officer's report for a training they may assign and report on (Assign Training, scoped to their group for a group
		/// admin): per-assignee state, due moment, attempts, last score, extension and exemption, and the completion KPIs.
		/// Per-question answers are not part of it (R-T12).
		/// </summary>
		[HttpGet("GetTrainingReport")]
		[Authorize(Policy = ResgridResources.Training_Update)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<TrainingReportResult>> GetTrainingReport(int trainingId)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var training = await _trainingService.GetTrainingRowByIdAsync(trainingId);
			if (training == null)
				return NotFound();
			if (training.DepartmentId != DepartmentId || !(await ScopeAsync()).CanAssign(training))
				return Unauthorized();

			var report = await _trainingService.GetTrainingReportAsync(DepartmentId, trainingId, DateTime.UtcNow);
			if (report == null)
				return NotFound();

			var result = new TrainingReportResult
			{
				Data = new TrainingReportData
				{
					TrainingId = report.Training.TrainingId,
					Name = report.Training.Name,
					IsArchived = report.Training.IsArchived,
					CompleteCount = report.CompleteCount,
					OverdueCount = report.OverdueCount,
					ExemptCount = report.ExemptCount,
					CompletionPercent = report.CompletionPercent,
					OpenQuestionFlags = report.Flags?.Count(f => !f.ResolvedOn.HasValue) ?? 0,
					Rows = report.Rows.Select(r => new TrainingReportRowData
					{
						UserId = r.UserId,
						Name = r.Name,
						GroupName = r.GroupName,
						IsActiveMember = r.IsActiveMember,
						State = (int)r.State,
						StateName = r.State.ToString(),
						AssignedOn = r.Assignment?.AssignedOn,
						DueOn = r.DueUtc,
						OriginalDueOn = r.Assignment?.OriginalDueOn,
						Viewed = r.Assignment?.Viewed ?? false,
						ViewedOn = r.Assignment?.ViewedOn,
						Complete = r.Assignment?.Complete ?? false,
						CompletedOn = r.Assignment?.CompletedOn,
						Score = r.Assignment?.Score ?? 0,
						Passed = r.Assignment?.Passed,
						AttemptCount = r.AttemptCount,
						LastAttemptScore = r.LastAttempt?.Score,
						LastAttemptOn = r.LastAttempt?.SubmittedOn,
						OnLeave = r.OnLeave,
						ExemptOn = r.Assignment?.ExemptOn,
						ExemptReason = r.Assignment?.ExemptReason,
						ExemptUntil = r.Assignment?.ExemptUntil,
						ExtendedOn = r.Assignment?.ExtendedOn,
						ExtensionReason = r.Assignment?.ExtensionReason,
						RemovedOn = r.Assignment?.RemovedOn
					}).ToList()
				}
			};

			return Populate(result, result.Data.Rows.Count);
		}

		#endregion Officer

		#region Mapping

		private static string PlainText(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return null;

			var text = StringHelpers.HtmlToPlainText(html)?.Trim();
			return string.IsNullOrEmpty(text) ? null : text.Length > 500 ? text.Substring(0, 500) : text;
		}

		public static TrainingAssignmentData MapAssignment(TrainingAssignmentStatus status, DateTime utcNow)
		{
			var training = status.Training;
			var assignment = status.Assignment;
			var limit = training?.QuizAttemptLimit.GetValueOrDefault() ?? 0;

			return new TrainingAssignmentData
			{
				TrainingId = training?.TrainingId ?? 0,
				Name = training?.Name,
				Summary = PlainText(training?.Description),
				State = (int)status.State,
				StateName = status.State.ToString(),
				AssignedOn = assignment?.AssignedOn,
				DueOn = status.DueUtc,
				Viewed = assignment?.Viewed ?? false,
				ViewedOn = assignment?.ViewedOn,
				Complete = assignment?.Complete ?? false,
				CompletedOn = assignment?.CompletedOn,
				HasQuiz = training?.HasQuiz ?? false,
				Score = assignment?.Score ?? 0,
				Passed = assignment?.Passed,
				AttemptCount = assignment?.AttemptCount ?? 0,
				AttemptsLeft = limit > 0 ? Math.Max(0, limit - (assignment?.AttemptCount ?? 0)) : (int?)null,
				Exempt = assignment?.IsExemptOn(utcNow) ?? false,
				ExemptUntil = assignment?.ExemptUntil,
				Extended = assignment?.ExtendedOn.HasValue ?? false,
				Hours = training?.Hours
			};
		}

		/// <summary>
		/// The training as the member reads it. The quiz carries questions and options only (R-T11), and only when
		/// <paramref name="includeQuiz"/> (the member may take it).
		/// </summary>
		public static TrainingDetailData MapTraining(Training training, TrainingAssignmentData assignment, bool includeQuiz)
		{
			var data = new TrainingDetailData
			{
				TrainingId = training.TrainingId,
				Name = training.Name,
				Description = StringHelpers.SanitizeHtmlWithEmbedsForDisplay(training.Description),
				TrainingText = StringHelpers.SanitizeHtmlWithEmbedsForDisplay(training.TrainingText),
				CreatedByUserId = training.CreatedByUserId,
				CreatedOn = training.CreatedOn,
				StartsOn = training.StartsOn,
				OpenToAllMembers = training.OpenToAllMembers,
				AllowSelfEnroll = training.AllowSelfEnroll,
				Hours = training.Hours,
				CeHours = training.CeHours,
				CeProvider = training.CeProvider,
				CeCourseNumber = training.CeCourseNumber,
				HasQuiz = training.HasQuiz,
				Assignment = assignment,
				Attachments = (training.Attachments ?? Enumerable.Empty<TrainingAttachment>()).Where(a => a != null).Select(a => new TrainingAttachmentData
				{
					TrainingAttachmentId = a.TrainingAttachmentId,
					FileName = a.FileName,
					FileType = a.FileType,
					FileSize = a.FileSize
				}).ToList()
			};

			var questions = (training.Questions ?? Enumerable.Empty<TrainingQuestion>()).Where(q => q != null).OrderBy(q => q.TrainingQuestionId).ToList();
			if (includeQuiz && questions.Count > 0)
			{
				data.Quiz = new TrainingQuizData
				{
					MinimumScore = training.MinimumScore,
					OneQuestionPerPage = training.OneQuestionPerPage,
					AttemptLimit = training.QuizAttemptLimit,
					Questions = questions.Select(q => new TrainingQuizQuestionData
					{
						QuestionId = q.TrainingQuestionId,
						Text = q.Question,
						Type = q.Type,
						Options = (q.Answers ?? Enumerable.Empty<TrainingQuestionAnswer>()).Where(a => a != null).OrderBy(a => a.TrainingQuestionAnswerId)
							.Select(a => new TrainingQuizOptionData { OptionId = a.TrainingQuestionAnswerId, Text = a.Answer }).ToList()
					}).ToList()
				};
			}

			return data;
		}

		#endregion Mapping
	}
}
