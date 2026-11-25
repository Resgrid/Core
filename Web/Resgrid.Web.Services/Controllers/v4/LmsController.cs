using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Lms;
using ApiProgressInput = Resgrid.Web.Services.Models.v4.Lms.LmsProgressInput;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The LMS learner runtime (Training T2 and the A1 Academy, protocols-training-knowledge-plan §13 T2, §15): the member's courses,
	/// the player's outline and progress (seat time capped by the server's clock), lesson completion, assessments drawn and scored
	/// on the server (questions and options go out, the key never does, R-T11), the offline video manifest (R-C9), crew-viewing
	/// scans (R-C4) and the Academy catalog. Department LMS content follows the LMS gate and its lapse rules; Academy content runs for
	/// every department. Every endpoint is refused while the Trainings module is off. Omni would use this; Omni is not built here.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsController : V4AuthenticatedApiControllerbase
	{
		private readonly ILmsGateService _gate;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsAssessmentService _assessments;
		private readonly ILmsMediaService _media;
		private readonly ILmsCrewViewingService _crew;
		private readonly ILmsAcademyService _academy;
		private readonly ILmsAccessService _access;

		public LmsController(ILmsGateService gate, ILmsLearnerService learner, ILmsAssessmentService assessments, ILmsMediaService media, ILmsCrewViewingService crew,
			ILmsAcademyService academy, ILmsAccessService access)
		{
			_gate = gate;
			_learner = learner;
			_assessments = assessments;
			_media = media;
			_crew = crew;
			_academy = academy;
			_access = access;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private Task<bool> ModuleOnAsync() => _access.IsTrainingModuleOnAsync(DepartmentId);

		private static string Locale(HttpRequest request)
		{
			var header = request?.Headers["Accept-Language"].ToString();
			if (string.IsNullOrWhiteSpace(header))
				return null;
			var first = header.Split(',')[0].Split(';')[0].Trim();
			return first.Length == 0 || first.Length > 16 ? null : first;
		}

		private static LmsMyCourseData Map(LmsMyCourse course) => new LmsMyCourseData
		{
			EnrollmentId = course.Enrollment.LmsEnrollmentId,
			CourseId = course.Course.LmsCourseId,
			CourseVersionId = course.Version.LmsCourseVersionId,
			Title = course.Version.Title ?? course.Course.Title,
			VersionLabel = course.Version.Label,
			IsAcademy = course.Course.IsSystem,
			Status = ((LmsEnrollmentStatuses)course.Enrollment.Status).ToString(),
			ProgressPercent = course.Enrollment.ProgressPercent,
			AssignedOn = course.Enrollment.AssignedOn,
			DueOn = course.Enrollment.DueOn,
			CompletedOn = course.Enrollment.CompletedOn,
			IsOverdue = course.IsOverdue,
			CanRun = course.CanRun,
			Hours = course.Version.Hours
		};

		/// <summary>The member's LMS courses (open first, then completed), Academy lessons included.</summary>
		[HttpGet("GetMyCourses")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsMyCoursesResult>> GetMyCourses()
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var result = new LmsMyCoursesResult { Data = (await _learner.GetMyCoursesAsync(DepartmentId, UserId)).Select(Map).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>The player outline of one enrollment: the version the member takes, each lesson with its progress and lock.</summary>
		[HttpGet("GetCourse")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsCourseResult>> GetCourse(int enrollmentId)
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, enrollmentId, null);
			if (player.Error == LmsErrors.NotFound || player.Outline == null)
				return NotFound();

			var mine = (await _learner.GetMyCoursesAsync(DepartmentId, UserId)).FirstOrDefault(c => c.Enrollment.LmsEnrollmentId == enrollmentId);
			var result = new LmsCourseResult { Course = mine == null ? null : Map(mine), CurrentLessonId = player.Lesson?.LmsLessonId };
			foreach (var module in player.Outline.Modules)
				foreach (var lesson in module.Lessons)
				{
					player.Progress.TryGetValue(lesson.LmsLessonId, out var progress);
					result.Lessons.Add(new LmsLessonData
					{
						LessonId = lesson.LmsLessonId,
						ModuleId = module.Module.LmsCourseModuleId,
						ModuleTitle = module.Module.Title,
						Title = lesson.Title,
						Kind = ((LmsLessonKinds)lesson.Kind).ToString(),
						IsRequired = lesson.IsRequired,
						IsLocked = player.LockedLessonIds.Contains(lesson.LmsLessonId),
						Progress = ((LmsLessonProgressStatuses)(progress?.Status ?? 0)).ToString(),
						SeatSeconds = progress?.SeatSeconds ?? 0,
						MinimumSeatSeconds = lesson.MinimumSeatSeconds,
						VideoPositionSeconds = progress?.VideoPositionSeconds ?? 0,
						WatchedPercent = progress?.WatchedPercent ?? 0,
						RequiredWatchPercent = lesson.RequiredWatchPercent,
						// Sanitized when stored and again on output (the stored-html rule: the save path is never trusted alone).
						ContentHtml = string.IsNullOrEmpty(lesson.ContentHtml) ? null : StringHelpers.SanitizeHtmlForDisplay(lesson.ContentHtml),
						VideoSource = lesson.Kind == (int)LmsLessonKinds.Video ? ((LmsVideoSources)lesson.VideoSource).ToString() : null,
						VideoUrl = lesson.VideoSource == (int)LmsVideoSources.Hosted ? null : lesson.VideoUrl,
						TranscriptText = lesson.TranscriptText,
						DocumentUrl = lesson.DocumentUrl,
						FreeResponsePrompt = lesson.FreeResponsePrompt,
						FileRequestPrompt = lesson.FileRequestPrompt,
						MayAnswerOffline = LmsAttemptRules.MayAnswerOffline(lesson, player.Outline.Version),
						AvailableOffline = lesson.AvailableOffline
					});
				}
			return Ok(Populate(result, result.Lessons.Count, player.Error == LmsErrors.None ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>Saves progress on a lesson change or heartbeat (seat time, video position, watched percent).</summary>
		[HttpPost("SaveProgress")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsActionResult>> SaveProgress([FromBody] ApiProgressInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var outcome = await _learner.SaveProgressAsync(DepartmentId, UserId, input.EnrollmentId, input.LessonId, new Resgrid.Model.Lms.LmsProgressInput
			{
				Seconds = input.Seconds,
				VideoPositionSeconds = input.VideoPositionSeconds,
				WatchedPercent = input.WatchedPercent
			}, cancellationToken);
			return Ok(Action(outcome));
		}

		/// <summary>Completes a lesson when its rules are met (seat time, watched percent, a free response); the last one completes the course.</summary>
		[HttpPost("CompleteLesson")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsActionResult>> CompleteLesson([FromBody] LmsCompleteLessonInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var outcome = await _learner.CompleteLessonAsync(DepartmentId, UserId, input.EnrollmentId, input.LessonId,
				new LmsLessonCompletionInput { ResponseText = input.ResponseText }, cancellationToken);
			return Ok(Action(outcome));
		}

		private static LmsActionResult Action(LmsResult outcome)
		{
			var result = new LmsActionResult
			{
				Error = outcome.Succeeded ? null : (outcome.Error != LmsErrors.None ? outcome.Error.ToString() : outcome.LifecycleError.ToString()),
				CourseCompleted = outcome.Count > 0 && outcome.Enrollment?.Status == (int)LmsEnrollmentStatuses.Completed,
				ProgressPercent = outcome.Enrollment?.ProgressPercent ?? 0,
				EnrollmentId = outcome.Enrollment?.LmsEnrollmentId
			};
			return Populate(result, 1, outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure);
		}

		/// <summary>Starts (or resumes) an assessment attempt: the drawn questions and options, never the key; a server-side deadline when timed.</summary>
		[HttpPost("StartAttempt")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsAttemptResult>> StartAttempt([FromBody] LmsStartAttemptInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var view = await _assessments.StartAttemptAsync(DepartmentId, UserId, input.EnrollmentId, input.LessonId, input.Attested,
				IpAddressHelper.GetRequestIP(Request, true), Request.Headers["User-Agent"].ToString(), Locale(Request), cancellationToken);
			var result = new LmsAttemptResult
			{
				Error = view.Error == LmsErrors.None ? null : view.Error.ToString(),
				AttemptId = view.Attempt?.LmsAssessmentAttemptId ?? 0,
				DeadlineOn = view.Attempt?.DeadlineOn,
				SecondsRemaining = view.SecondsRemaining,
				Questions = view.Error == LmsErrors.None ? view.Questions.Select(q => new LmsQuestionData
				{
					QuestionId = q.QuestionId,
					Kind = ((LmsQuestionKinds)q.Kind).ToString(),
					Text = q.Text,
					ImageUrl = q.ImageUrl,
					Points = q.Points,
					Options = q.Options.Select(o => new LmsQuestionOptionData { Key = o.Key, Text = o.Text }).ToList()
				}).ToList() : new List<LmsQuestionData>()
			};
			return Ok(Populate(result, result.Questions.Count, view.Error == LmsErrors.None ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>Submits an attempt: scored on the server; the review follows the lesson's answer-review policy.</summary>
		[HttpPost("SubmitAttempt")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsAttemptOutcomeResult>> SubmitAttempt([FromBody] LmsSubmitAttemptInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var view = await _assessments.SubmitAttemptAsync(DepartmentId, UserId, input.AttemptId,
				(input.Answers ?? new List<LmsAnswerInput>()).Select(a => new LmsAttemptAnswer { QuestionId = a.QuestionId, Keys = a.Keys ?? new List<string>(), Text = a.Text }),
				cancellationToken);
			return Ok(Outcome(view));
		}

		/// <summary>The member's own finished attempt, as the answer-review policy allows.</summary>
		[HttpGet("GetAttemptResult")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsAttemptOutcomeResult>> GetAttemptResult(int attemptId)
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			var view = await _assessments.GetResultAsync(actor, attemptId);
			// The member's own attempt only through the API (graders use the web queue).
			if (view.Attempt != null && !string.Equals(view.Attempt.UserId, UserId, StringComparison.OrdinalIgnoreCase))
				return NotFound();
			return Ok(Outcome(view));
		}

		private static LmsAttemptOutcomeResult Outcome(LmsAttemptResultView view)
		{
			var result = new LmsAttemptOutcomeResult
			{
				Error = view.Error == LmsErrors.None ? null : view.Error.ToString(),
				AttemptId = view.Attempt?.LmsAssessmentAttemptId ?? 0,
				Percent = view.Attempt?.Percent ?? 0,
				Passed = view.Attempt?.Passed,
				CourseCompleted = view.CourseCompleted,
				Questions = view.Questions.Select(q => new LmsReviewedQuestionData
				{
					QuestionId = q.Question.QuestionId,
					Correct = q.Correct,
					AwaitingGrade = q.AwaitingGrade,
					CorrectKeys = q.CorrectKeys,
					Explanation = q.Explanation
				}).ToList()
			};
			return Populate(result, result.Questions.Count, view.Error == LmsErrors.None ? ResponseHelper.Success : ResponseHelper.Failure);
		}

		/// <summary>
		/// The hosted videos of the member's open courses marked available offline (R-C9): one rendition each within the lesson's size
		/// cap, a signed URL and the date the cached copy must be deleted by. Empty when hosted video is not playable (lapse rule 6).
		/// </summary>
		[HttpGet("GetOfflineManifest")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsOfflineManifestResult>> GetOfflineManifest()
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var result = new LmsOfflineManifestResult
			{
				Data = (await _media.GetOfflineManifestAsync(DepartmentId, UserId, DateTime.UtcNow)).Select(v => new LmsOfflineVideoData
				{
					EnrollmentId = v.LmsEnrollmentId,
					LessonId = v.LmsLessonId,
					Title = v.Title,
					Url = v.Url,
					Bytes = v.Bytes,
					MaxBytes = v.MaxBytes,
					UrlExpiresOn = v.UrlExpiresOn,
					CacheExpiresOn = v.CacheExpiresOn
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>A crew-viewing scan from the member's own phone (R-C4): marks them present and completes the lesson shown.</summary>
		[HttpPost("CrewScan")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsActionResult>> CrewScan([FromBody] LmsCrewScanInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var outcome = await _crew.ScanAsync(DepartmentId, UserId, input.ViewingId, input.Code, IpAddressHelper.GetRequestIP(Request, true), cancellationToken);
			return Ok(Action(outcome));
		}

		/// <summary>The Resgrid Academy lessons the department may take (visibility follows entitlement), with the member's state.</summary>
		[HttpGet("GetAcademy")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsAcademyResult>> GetAcademy()
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var locale = Locale(Request);
			var catalog = await _academy.GetCatalogAsync(await _gate.GetActorAsync(DepartmentId, UserId));
			var result = new LmsAcademyResult
			{
				Data = catalog.Lessons.Select(l => new LmsAcademyLessonData
				{
					LessonId = l.Definition.LessonId,
					CourseId = l.Course.LmsCourseId,
					Title = _academy.Text(l.Definition.LessonId + ".Title", locale),
					Minutes = l.Definition.Lesson.Minutes,
					Hours = l.Definition.Hours,
					IsWalkthrough = l.Definition.Lesson.IsWalkthrough,
					EnrollmentId = l.MyEnrollment?.LmsEnrollmentId,
					Completed = l.Completed
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>Enrols the member in an Academy lesson (or a self-enrollable department course) and returns the enrollment.</summary>
		[HttpPost("Enroll")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsActionResult>> Enroll(int courseId, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var outcome = await _learner.EnrollSelfAsync(await _gate.GetActorAsync(DepartmentId, UserId), courseId, cancellationToken);
			return Ok(Action(outcome));
		}
	}
}
