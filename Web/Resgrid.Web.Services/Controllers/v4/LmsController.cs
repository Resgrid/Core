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
	/// <para>
	/// Round 10 (Omni Responder): the member's LMS access answer, the self-enrollable catalog, hosted video for bearer clients (a signed,
	/// short-lived, enrollment-bound playlist and caption link a native player loads itself), the member's uploaded lesson files and
	/// surveys. These department LMS items also need the member's LMS seat (an unlicensed member keeps the free baseline and the Academy).
	/// </para>
	/// <para>
	/// Round 10 A7 (owner decision 2026-11-26): the seat is enforced by the LMS services for every learner endpoint here (Enroll, GetCourse,
	/// SaveProgress, CompleteLesson, StartAttempt, SubmitAttempt, CrewScan, the offline manifest, the playlist and caption links, lesson files and
	/// surveys), lapse-aware, and refused as <see cref="NotLicensedError"/>. Reading the member's own finished attempts stays open.
	/// </para>
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsController : V4AuthenticatedApiControllerbase
	{
		/// <summary>A 10 MB lesson file is about 13.4 MB as base64; the rest of the body is small.</summary>
		public const long MaxCompleteLessonRequestBytes = 15 * 1024 * 1024;

		/// <summary>The refusal of a department LMS item to a member without an LMS seat (the LmsErrors.NotLicensed name since round 10 A7).</summary>
		public const string NotLicensedError = nameof(LmsErrors.NotLicensed);

		private readonly ILmsGateService _gate;
		private readonly ILmsLearnerService _learner;
		private readonly ILmsAssessmentService _assessments;
		private readonly ILmsMediaService _media;
		private readonly ILmsCrewViewingService _crew;
		private readonly ILmsAcademyService _academy;
		private readonly ILmsAccessService _access;
		private readonly IAiKnowledgeService _knowledge;
		private readonly ILmsAppLaunchService _appLaunches;

		/// <summary>Round 10 A8: launch links a member may ask for per minute (per API server).</summary>
		public const int LessonLaunchesPerMinute = 20;

		public static readonly LmsAppLaunchLimiter LessonLaunchLimiter = new LmsAppLaunchLimiter();

		public LmsController(ILmsGateService gate, ILmsLearnerService learner, ILmsAssessmentService assessments, ILmsMediaService media, ILmsCrewViewingService crew,
			ILmsAcademyService academy, ILmsAccessService access, IAiKnowledgeService knowledge, ILmsAppLaunchService appLaunches)
		{
			_gate = gate;
			_learner = learner;
			_assessments = assessments;
			_media = media;
			_crew = crew;
			_academy = academy;
			_access = access;
			_knowledge = knowledge;
			_appLaunches = appLaunches;
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
			var result = new LmsCourseResult
			{
				Error = player.Error == LmsErrors.None ? null : player.Error.ToString(),
				Course = mine == null ? null : Map(mine),
				CurrentLessonId = player.Lesson?.LmsLessonId
			};
			// Round 10 A7: a course that may not run now (NotLicensed, LapseWindowEnded, AddonRequired, ...) is read-only: titles, kinds and the
			// member's own progress, never the lessons' content, links, prompts, survey questions or captions; Error says why.
			var readOnly = player.Error != LmsErrors.None;
			// Round 10: the member's own uploads (metadata only), by lesson.
			var files = (await _learner.GetMyLessonFilesAsync(DepartmentId, UserId, enrollmentId) ?? new List<LmsLessonFile>()).ToLookup(f => f.LmsLessonId);
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
						ContentHtml = readOnly || string.IsNullOrEmpty(lesson.ContentHtml) ? null : StringHelpers.SanitizeHtmlForDisplay(lesson.ContentHtml),
						VideoSource = lesson.Kind == (int)LmsLessonKinds.Video ? ((LmsVideoSources)lesson.VideoSource).ToString() : null,
						VideoUrl = readOnly || lesson.VideoSource == (int)LmsVideoSources.Hosted ? null : lesson.VideoUrl,
						TranscriptText = readOnly ? null : lesson.TranscriptText,
						DocumentUrl = readOnly ? null : lesson.DocumentUrl,
						FreeResponsePrompt = readOnly ? null : lesson.FreeResponsePrompt,
						FileRequestPrompt = readOnly ? null : lesson.FileRequestPrompt,
						MayAnswerOffline = !readOnly && LmsAttemptRules.MayAnswerOffline(lesson, player.Outline.Version),
						AvailableOffline = !readOnly && lesson.AvailableOffline,
						ProtocolVersionId = !readOnly && lesson.Kind == (int)LmsLessonKinds.ProtocolVersion ? lesson.ProtocolVersionId : null,
						Files = files[lesson.LmsLessonId].Select(MapFile).ToList(),
						SurveyQuestions = !readOnly && lesson.Kind == (int)LmsLessonKinds.Survey ? LmsSurveyQuestion.Parse(lesson.SurveyJson).Select(MapSurveyQuestion).ToList() : new List<LmsSurveyQuestionData>(),
						Captions = !readOnly && lesson.Kind == (int)LmsLessonKinds.Video ? LmsCaptionTrack.Parse(lesson.CaptionTracksJson).Select(t => new LmsCaptionTrackData
						{
							Locale = t.Locale,
							Label = t.Label,
							// An uploaded track is read through the API (GetLessonCaptions, or the signed link GetLessonPlaylist hands out).
							Url = string.IsNullOrWhiteSpace(t.ObjectKey) ? HttpsOrNull(t.Url) : null
						}).ToList() : new List<LmsCaptionTrackData>()
					});
				}
			return Ok(Populate(result, result.Lessons.Count, player.Error == LmsErrors.None ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		private static LmsLessonFileData MapFile(LmsLessonFile file) => new LmsLessonFileData
		{
			LessonFileId = file.LmsLessonFileId,
			FileName = file.FileName,
			ContentType = file.ContentType,
			Size = file.SizeBytes,
			UploadedOn = file.UploadedOn
		};

		private static LmsSurveyQuestionData MapSurveyQuestion(LmsSurveyQuestion question) => new LmsSurveyQuestionData
		{
			Key = question.Key,
			Kind = Enum.IsDefined(typeof(LmsSurveyQuestionKinds), question.Kind) ? ((LmsSurveyQuestionKinds)question.Kind).ToString() : LmsSurveyQuestionKinds.Text.ToString(),
			Text = question.Text,
			Options = question.Options ?? new List<string>()
		};

		private static string HttpsOrNull(string url) =>
			Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? url : null;

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

		/// <summary>
		/// Completes a lesson when its rules are met (seat time, watched percent, a free response, a requested file: round 10, base64 in
		/// FileData); the last one completes the course.
		/// </summary>
		[HttpPost("CompleteLesson")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxCompleteLessonRequestBytes)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsActionResult>> CompleteLesson([FromBody] LmsCompleteLessonInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var completion = new LmsLessonCompletionInput { ResponseText = input.ResponseText };
			if (!string.IsNullOrWhiteSpace(input.FileData))
			{
				byte[] data;
				try { data = Convert.FromBase64String(input.FileData.Trim()); }
				catch (FormatException) { return Ok(Action(LmsResult.Fail(LmsErrors.InvalidInput))); }
				if (data.LongLength > LmsLessonFile.MaxBytes)
					return Ok(Action(LmsResult.Fail(LmsErrors.FileTooLarge)));
				completion.FileData = data;
				completion.FileName = input.FileName;
				completion.ContentType = input.FileContentType;
			}
			var outcome = await _learner.CompleteLessonAsync(DepartmentId, UserId, input.EnrollmentId, input.LessonId, completion, cancellationToken);
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

		#region Round 10 (Omni Responder)

		/// <summary>
		/// The calling member's LMS access: the Trainings module, the Training.Lms flag, the add-on (null when the flag is off or billing could not
		/// answer), a lapse, the member's LMS seat and the Enhanced AI learning items. Answered while the module is off too (everything false).
		/// </summary>
		[HttpGet("GetMyAccess")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<LmsMyAccessResult>> GetMyAccess()
		{
			var result = new LmsMyAccessResult();
			if (await ModuleOnAsync())
			{
				var gate = await _gate.GetGateAsync(DepartmentId) ?? new LmsDepartmentGate { DepartmentId = DepartmentId };
				result.Data.TrainingModuleOn = true;
				result.Data.LmsFlagOn = gate.FlagOn;
				result.Data.AddonActive = gate.FlagOn ? gate.AddonActive : null;
				result.Data.IsLapsed = gate.IsLapsed;
				result.Data.CanBeTrained = gate.FlagOn && await _access.CanBeTrainedAsync(DepartmentId, UserId);
				try { result.Data.EnhancedAiActive = await _knowledge.IsEntitledAsync(DepartmentId); }
				catch (Exception ex) { Logging.LogException(ex, "The Enhanced AI entitlement could not be read."); }
			}
			return Ok(Populate(result, 1));
		}

		/// <summary>
		/// The department's self-enrollable courses the member may see (published, in the member's audience), with whether Lms/Enroll would take
		/// them and the member's open enrollment. Empty while the LMS is dark or the member holds no LMS seat.
		/// </summary>
		[HttpGet("GetCatalog")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsCatalogResult>> GetCatalog()
		{
			if (!await ModuleOnAsync())
				return NotFound();

			var result = new LmsCatalogResult();
			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			if (actor?.Gate?.FlagOn == true && actor.IsCurrentMember && await _access.CanBeTrainedAsync(DepartmentId, UserId))
				result.Data = (await _learner.GetCatalogAsync(actor) ?? new List<LmsCatalogEntry>())
					.Where(e => e?.Course != null && !e.Course.IsSystem && e.Course.DepartmentId == DepartmentId)
					.Select(e => new LmsCatalogCourseData
					{
						CourseId = e.Course.LmsCourseId,
						CourseVersionId = e.Version?.LmsCourseVersionId ?? e.Course.CurrentVersionId ?? 0,
						Title = e.Version?.Title ?? e.Course.Title,
						Summary = e.Course.Summary,
						Hours = e.Version?.Hours ?? 0,
						VersionLabel = e.Version?.Label,
						CanEnroll = e.CanEnroll,
						OpenEnrollmentId = e.OpenEnrollment?.LmsEnrollmentId
					}).ToList();
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>
		/// A hosted video lesson's streams for a bearer client (the web player's Courses/Hls and Courses/Mp4 rules): a signed HLS playlist URL and a
		/// signed MP4 URL a native player loads without the token, and signed links to the uploaded caption tracks. The links are bound to this
		/// enrollment and lesson and stop at UrlExpiresOn; each use runs the player's rules again. 404 for an unknown or another member's
		/// enrollment, or a lesson that is not a hosted video of it.
		/// </summary>
		[HttpGet("GetLessonPlaylist")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsLessonPlaylistResult>> GetLessonPlaylist(int enrollmentId, int lessonId)
		{
			if (!await ModuleOnAsync())
				return NotFound();

			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, enrollmentId, lessonId);
			if (player.Error == LmsErrors.NotFound || player.Outline == null)
				return NotFound();
			var lesson = player.Outline.Lessons.FirstOrDefault(l => l.LmsLessonId == lessonId);
			if (lesson == null || lesson.Kind != (int)LmsLessonKinds.Video || lesson.VideoSource != (int)LmsVideoSources.Hosted)
				return NotFound();

			LmsLessonPlaylistResult Refuse(string error) => Populate(new LmsLessonPlaylistResult { Error = error }, 0, ResponseHelper.Failure);
			if (player.Error != LmsErrors.None)
				return Ok(Refuse(player.Error.ToString()));
			if (player.LockedLessonIds.Contains(lessonId) || player.Lesson?.LmsLessonId != lessonId)
				return Ok(Refuse(LmsErrors.LessonLocked.ToString()));
			if (!player.HostedVideoPlayable || player.Media == null || !_media.IsStoreConfigured || !LmsMediaLinks.IsConfigured)
				return Ok(Refuse(LmsErrors.MediaUnavailable.ToString()));

			var now = DateTime.UtcNow;
			var expires = now.Add(LmsMediaLinks.Lifetime);
			var data = new LmsLessonPlaylistData
			{
				EnrollmentId = enrollmentId,
				LessonId = lessonId,
				PlaylistUrl = string.IsNullOrWhiteSpace(player.Media.HlsPrefix) ? null
					: LmsMediaLinks.Build(LmsMediaLinks.PlaylistKind, DepartmentId, enrollmentId, lessonId, null, expires),
				Mp4Url = await _media.GetSignedMp4UrlAsync(await _gate.GetGateAsync(DepartmentId), player.Media, now),
				UrlExpiresOn = expires,
				DurationSeconds = player.Media.DurationSeconds,
				Captions = (player.Captions ?? new List<LmsCaptionTrack>()).Where(t => t != null && !string.IsNullOrWhiteSpace(t.Locale)).Select(t => new LmsCaptionTrackData
				{
					Locale = t.Locale,
					Label = t.Label,
					Url = !string.IsNullOrWhiteSpace(t.ObjectKey)
						? LmsMediaLinks.Build(LmsMediaLinks.CaptionsKind, DepartmentId, enrollmentId, lessonId, t.Locale, expires)
						: HttpsOrNull(t.Url)
				}).ToList()
			};
			if (data.PlaylistUrl == null && data.Mp4Url == null)
				return Ok(Refuse(LmsErrors.MediaUnavailable.ToString()));

			Response.Headers["Cache-Control"] = "no-store";
			return Ok(Populate(new LmsLessonPlaylistResult { Data = data }, 1));
		}

		/// <summary>
		/// The player as the signed link's enrollment learner sees it, after the link's signature, the module, the enrollment, the player's rules
		/// (lapse, lock) and the learner's LMS seat; null when any refuses.
		/// </summary>
		private async Task<(int DepartmentId, LmsPlayerView Player, string Locale)?> SignedPlayerAsync(string kind, string d, string en, string ls, string loc, string e, string sig)
		{
			if (!LmsMediaLinks.TryValidate(kind, d, en, ls, loc, e, sig, DateTime.UtcNow, out var departmentId, out var enrollmentId, out var lessonId, out var locale))
				return null;
			if (!await _access.IsTrainingModuleOnAsync(departmentId))
				return null;
			var enrollment = await _learner.GetEnrollmentAsync(departmentId, enrollmentId);
			if (enrollment == null || enrollment.DepartmentId != departmentId || string.IsNullOrWhiteSpace(enrollment.UserId) ||
				enrollment.Status == (int)LmsEnrollmentStatuses.Withdrawn)
				return null;
			var player = await _learner.GetPlayerAsync(departmentId, enrollment.UserId, enrollmentId, lessonId);
			// The player's rules include the learner's LMS seat (round 10 A7): a link outlives neither the seat nor the lapse window.
			if (player.Error != LmsErrors.None || player.Outline == null || player.Lesson?.LmsLessonId != lessonId)
				return null;
			return (departmentId, player, locale);
		}

		/// <summary>
		/// The signed HLS playlist a native player loads from GetLessonPlaylist's PlaylistUrl (no bearer token): every segment a signed URL of the
		/// media store. 404 for a link that is malformed, expired, forged or no longer playable.
		/// </summary>
		[HttpGet(LmsMediaLinks.PlaylistRoute)]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> HostedPlaylist([FromQuery(Name = LmsMediaLinks.DepartmentParameter)] string d, [FromQuery(Name = LmsMediaLinks.EnrollmentParameter)] string en,
			[FromQuery(Name = LmsMediaLinks.LessonParameter)] string ls, [FromQuery(Name = LmsMediaLinks.ExpiresParameter)] string e,
			[FromQuery(Name = LmsMediaLinks.SignatureParameter)] string sig)
		{
			var signed = await SignedPlayerAsync(LmsMediaLinks.PlaylistKind, d, en, ls, null, e, sig);
			if (signed == null)
				return NotFound();
			var (departmentId, player, _) = signed.Value;
			if (player.Lesson.VideoSource != (int)LmsVideoSources.Hosted || !player.HostedVideoPlayable || player.Media == null)
				return NotFound();

			var playlist = await _media.GetSignedPlaylistAsync(await _gate.GetGateAsync(departmentId), player.Media, DateTime.UtcNow);
			if (playlist == null)
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";
			return Content(playlist, "application/vnd.apple.mpegurl");
		}

		/// <summary>An uploaded WebVTT caption track from GetLessonPlaylist's signed caption URL (no bearer token). 404 as for the playlist.</summary>
		[HttpGet(LmsMediaLinks.CaptionsRoute)]
		[AllowAnonymous]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> HostedCaptions([FromQuery(Name = LmsMediaLinks.DepartmentParameter)] string d, [FromQuery(Name = LmsMediaLinks.EnrollmentParameter)] string en,
			[FromQuery(Name = LmsMediaLinks.LessonParameter)] string ls, [FromQuery(Name = LmsMediaLinks.LocaleParameter)] string loc,
			[FromQuery(Name = LmsMediaLinks.ExpiresParameter)] string e, [FromQuery(Name = LmsMediaLinks.SignatureParameter)] string sig)
		{
			var signed = await SignedPlayerAsync(LmsMediaLinks.CaptionsKind, d, en, ls, loc, e, sig);
			if (signed == null)
				return NotFound();
			var (departmentId, player, locale) = signed.Value;
			var text = await _media.GetCaptionsAsync(departmentId, player.Lesson, locale);
			if (text == null)
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";
			return Content(text, "text/vtt");
		}

		/// <summary>An uploaded WebVTT caption track of a lesson of the member's own enrollment, by locale (the web player's Courses/Captions).</summary>
		[HttpGet("GetLessonCaptions")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetLessonCaptions(int enrollmentId, int lessonId, string locale)
		{
			if (!await ModuleOnAsync())
				return NotFound();
			var player = await _learner.GetPlayerAsync(DepartmentId, UserId, enrollmentId, lessonId);
			if (player.Error != LmsErrors.None || player.Outline == null || player.Lesson?.LmsLessonId != lessonId)
				return NotFound();
			var text = await _media.GetCaptionsAsync(DepartmentId, player.Lesson, LmsMediaLinks.NormalizeLocale(locale));
			if (text == null)
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";
			return Content(text, "text/vtt");
		}

		/// <summary>
		/// A file the member uploaded for a file-request lesson of one of their own enrollments (the web's Courses/LessonFile, own files only:
		/// graders and course reporters use the web). 404 otherwise.
		/// </summary>
		[HttpGet("GetLessonFile")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult> GetLessonFile(int lessonFileId)
		{
			if (!await ModuleOnAsync())
				return NotFound();
			// The learner service refuses a department lesson's file without the member's LMS seat (round 10 A7).
			var mine = await _learner.GetMyLessonFileAsync(DepartmentId, UserId, lessonFileId);
			if (mine?.File?.Data == null || mine.Course == null)
				return NotFound();

			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var name = System.IO.Path.GetFileName(mine.File.FileName ?? string.Empty);
			return File(mine.File.Data, string.IsNullOrWhiteSpace(mine.File.ContentType) ? "application/octet-stream" : mine.File.ContentType,
				string.IsNullOrWhiteSpace(name) ? "file" : name);
		}

		/// <summary>
		/// Answers a survey lesson (the web's Courses/Survey; anonymous: no user id is stored with the answers) and completes it; the last required
		/// lesson completes the course. Answers by question key (LmsLessonData.SurveyQuestions): Rating "1"-"5", Choice the 0-based option index,
		/// Text optional. Refusals: NotFound, LessonLocked, AlreadySubmitted, InvalidInput (a rating or choice missing or out of range), NotLicensed.
		/// </summary>
		[HttpPost("SubmitSurvey")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsActionResult>> SubmitSurvey([FromBody] LmsSubmitSurveyInput input, CancellationToken cancellationToken)
		{
			if (!await ModuleOnAsync() || input == null)
				return NotFound();
			var mine = (await _learner.GetMyCoursesAsync(DepartmentId, UserId) ?? new List<LmsMyCourse>()).FirstOrDefault(c => c.Enrollment?.LmsEnrollmentId == input.EnrollmentId);
			if (mine?.Course == null)
				return NotFound();
			// The learner service refuses without the member's LMS seat (NotLicensed, round 10 A7), before anything is stored.
			var answers = new Dictionary<string, string>(input.Answers ?? new Dictionary<string, string>(), StringComparer.Ordinal);
			var outcome = await _learner.SubmitSurveyAsync(DepartmentId, UserId, input.EnrollmentId, input.LessonId, answers, cancellationToken);
			return Ok(Action(outcome));
		}

		/// <summary>
		/// Round 10 A8: a SCORM 1.2 / 2004, cmi5 or LTI 1.3 lesson of the member's own enrollment for the app's web view. The player's rules run
		/// as for the web launch (Trainings module, the LMS flag, the gate and lapse window, the member's LMS seat, lesson order, the package being
		/// ready or the tool active); the answer is a single-use launch link to the Core web launch page (at most five minutes, bound to this member,
		/// enrollment and lesson; an opaque token, never the bearer) and the completion message the page posts back. <paramref name="item"/>
		/// optionally picks a SCO or AU. 404 for the module or flag off, another member's or an unknown enrollment, or a lesson of another kind.
		/// Not offline: the lesson needs the server throughout.
		/// </summary>
		[HttpGet("GetLessonLaunch")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status429TooManyRequests)]
		public async Task<ActionResult<LmsLessonLaunchResult>> GetLessonLaunch(int enrollmentId, int lessonId, string item = null, CancellationToken cancellationToken = default)
		{
			if (!await ModuleOnAsync() || !await _access.IsFlagEnabledAsync(DepartmentId))
				return NotFound();
			Response.Headers["Cache-Control"] = "no-store";
			LmsLessonLaunchResult Refuse(string error) => Populate(new LmsLessonLaunchResult { Error = error }, 0, ResponseHelper.Failure);

			if (!LessonLaunchLimiter.Allow(DepartmentId.ToString(CultureInfo.InvariantCulture) + "|" + UserId, LessonLaunchesPerMinute, DateTime.UtcNow))
				return StatusCode(StatusCodes.Status429TooManyRequests, Refuse("RateLimited"));

			var site = LmsStandardsUrls.SiteBase;
			if (!Uri.TryCreate(site, UriKind.Absolute, out var siteUri) || (siteUri.Scheme != Uri.UriSchemeHttps && siteUri.Scheme != Uri.UriSchemeHttp))
				return Ok(Refuse(LmsErrors.MediaUnavailable.ToString()));

			var issue = await _appLaunches.IssueAsync(DepartmentId, UserId, enrollmentId, lessonId, item, cancellationToken);
			if (issue.Error == LmsErrors.NotFound)
				return NotFound();
			if (!issue.Succeeded)
				return Ok(Refuse(issue.Error.ToString()));

			return Ok(Populate(new LmsLessonLaunchResult
			{
				Data = new LmsLessonLaunchData
				{
					Kind = issue.Kind.ToString(),
					LaunchUrl = site + "/lms-app/launch?token=" + Uri.EscapeDataString(issue.Token),
					ExpiresOn = issue.ExpiresOn,
					CompletionMessage = new LmsLessonCompletionMessageData
					{
						Type = LmsAppLaunchRules.MessageType,
						Version = LmsAppLaunchRules.MessageVersion,
						Origin = siteUri.GetLeftPart(UriPartial.Authority),
						EnrollmentId = enrollmentId,
						LessonId = lessonId,
						Statuses = LmsAppLaunchRules.Statuses.ToList(),
						ResultStatuses = LmsAppLaunchRules.ResultStatuses.ToList()
					}
				}
			}, 1));
		}

		#endregion Round 10 (Omni Responder)
	}
}
