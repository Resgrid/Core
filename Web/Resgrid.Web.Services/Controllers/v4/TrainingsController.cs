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
	/// Training T1 adds the one transcript: a member's learning records, the department's training categories and outside
	/// training submissions (Pending until an officer verifies them on the web).
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
		private readonly ILearningRecordService _learningRecordService;
		private readonly ITrainingSubmissionService _submissions;

		/// <summary>
		/// Round 9B: 30 MB of evidence (TrainingSubmissionRules.MaxUploadBytes) is about 40 MB as base64; the rest of the body is small. Types,
		/// sizes and contents are checked again by the service (TrainingEvidencePolicy).
		/// </summary>
		public const int MaxSubmissionRequestBytes = 44 * 1024 * 1024;

		public TrainingsController(ITrainingService trainingService, ITrainingAssignmentService assignmentService, IMyTrainingService myTrainingService,
			IAuthorizationService authorizationService, IDepartmentSettingsService departmentSettingsService, ILearningRecordService learningRecordService,
			ITrainingCompletionRecordingService recordings, ITrainingSubmissionService submissions, ITrainingRecordDocumentService documents)
		{
			_documents = documents;
			_submissions = submissions;
			_recordings = recordings;
			_trainingService = trainingService;
			_assignmentService = assignmentService;
			_myTrainingService = myTrainingService;
			_authorizationService = authorizationService;
			_departmentSettingsService = departmentSettingsService;
			_learningRecordService = learningRecordService;
		}

		private readonly ITrainingCompletionRecordingService _recordings;

		/// <summary>Round 10: the member's own transcript and certificate PDFs (the web pages' documents).</summary>
		private readonly ITrainingRecordDocumentService _documents;

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

		/// <summary>
		/// Round 9A read and confirm (Omni): the member confirms they read and reviewed a training that asks for it (no quiz); the assignment
		/// completes and goes on the transcript. 400 when the training does not ask for it, has a quiz, has not started or is not assigned.
		/// </summary>
		[HttpPost("AcknowledgeTraining")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingAssignmentResult>> AcknowledgeTraining([FromBody] TrainingIdInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();
			if (input == null || input.TrainingId <= 0)
				return BadRequest();

			var now = DateTime.UtcNow;
			var acknowledged = await _trainingService.AcknowledgeTrainingAsync(DepartmentId, input.TrainingId, UserId, cancellationToken);
			var result = new TrainingAssignmentResult();
			if (acknowledged == null || !acknowledged.Complete)
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

		#region Transcript (Training T1)

		/// <summary>
		/// A member's transcript, newest first: course completions, training records, calendar attendance, imports and outside
		/// certificates with their hours and status (voided records are left out; only verified records count toward hours).
		/// Without <paramref name="userId"/> it is the caller's own. Another member's transcript is read only by a reviewer of
		/// their records: a department-wide record manager (Assign Training), or the admin of a group the member is in.
		/// </summary>
		/// <param name="userId">The member; empty for the caller.</param>
		/// <param name="from">Optional start (UTC) of the completion window.</param>
		/// <param name="to">Optional end (UTC) of the completion window.</param>
		[HttpGet("GetTranscript")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<LearningRecordsResult>> GetTranscript(string userId = null, DateTime? from = null, DateTime? to = null)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var target = string.IsNullOrWhiteSpace(userId) ? UserId : userId.Trim();
			if (string.Equals(target, UserId, StringComparison.OrdinalIgnoreCase))
				target = UserId;
			else if (!(await ScopeAsync()).CanReviewRecordsOf(target))
				return Unauthorized();

			if (from.HasValue && to.HasValue && from.Value > to.Value)
				(from, to) = (to, from);

			var names = CategoryNames(await _learningRecordService.GetCategoriesAsync(DepartmentId, true));
			var rows = (await _learningRecordService.GetTranscriptAsync(DepartmentId, target, from, to, null, null, false) ?? new List<LearningRecord>())
				.Where(r => r != null && r.DepartmentId == DepartmentId && !r.IsVoided && string.Equals(r.UserId, target, StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(r => r.CompletedOn).ThenByDescending(r => r.LearningRecordId).ToList();
			// Round 7B: a completion recorded by an instructor carries who, when, from where, the note and the attestation.
			var recordings = await _recordings.GetForRecordsAsync(DepartmentId, rows) ?? new Dictionary<int, TrainingCompletionRecording>();
			var records = rows.Select(r => MapLearningRecord(r, names, recordings)).ToList();

			var result = new LearningRecordsResult { Data = records };
			return Populate(result, records.Count);
		}

		/// <summary>
		/// The documents' language: <paramref name="locale"/>, else the first Accept-Language tag (the app's language), else English. Mapped to a
		/// supported culture by the service.
		/// </summary>
		private string DocumentLocale(string locale)
		{
			if (!string.IsNullOrWhiteSpace(locale) && locale.Trim().Length <= 16)
				return locale.Trim();
			var header = Request?.Headers["Accept-Language"].ToString();
			if (string.IsNullOrWhiteSpace(header))
				return "en";
			var first = header.Split(',')[0].Split(';')[0].Trim();
			return first.Length == 0 || first.Length > 16 ? "en" : first;
		}

		private TrainingRecordDocumentRequest DocumentRequest(string locale) => new TrainingRecordDocumentRequest
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			UserName = UserName,
			Culture = DocumentLocale(locale),
			QrDataUrl = ApiQrCode.DataUrl
		};

		private ActionResult PdfFile(TrainingRecordDocumentFile file)
		{
			Response.Headers["X-Resgrid-Seal"] = file.Sealed ? "sealed" : "pending";
			return File(file.Bytes, string.IsNullOrWhiteSpace(file.ContentType) ? "application/pdf" : file.ContentType, file.FileName);
		}

		/// <summary>
		/// The caller's own transcript as a PDF (the web's Training records → Transcript PDF): the same lines, hours by category and provenance,
		/// sealed at L3 when sealing is configured (header X-Resgrid-Seal: sealed or pending). Only the caller's own; voided records left out.
		/// </summary>
		/// <param name="from">Optional first day (department-local date, yyyy-MM-dd) of the completion window.</param>
		/// <param name="to">Optional last day (department-local date, yyyy-MM-dd) of the completion window.</param>
		/// <param name="categoryId">Optional training category.</param>
		/// <param name="source">Optional record source (LearningRecordSourceKinds value).</param>
		/// <param name="locale">The document's language (ar, de, el, en, es, fr, it, pl, sv, uk); the Accept-Language header when left out.</param>
		[HttpGet("GetTranscriptPdf")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<ActionResult> GetTranscriptPdf(DateTime? from = null, DateTime? to = null, int? categoryId = null, int? source = null, string locale = null,
			CancellationToken cancellationToken = default)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			try
			{
				var file = await _documents.GetMyTranscriptPdfAsync(DocumentRequest(locale), from, to, categoryId, source, cancellationToken);
				return file?.Bytes == null ? NotFound() : PdfFile(file);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "Training transcript PDF could not be generated.");
				return StatusCode(StatusCodes.Status503ServiceUnavailable);
			}
		}

		/// <summary>
		/// The certificate of completion of one of the caller's own verified learning records as a PDF (the web's Training records → Certificate):
		/// with the LMS add-on, the verification code, its QR and the department's default template; sealed like the transcript. 404 for another
		/// member's, an unverified or an unknown record.
		/// </summary>
		/// <param name="learningRecordId">The record (LearningRecordData.LearningRecordId from GetTranscript).</param>
		/// <param name="locale">The document's language; the Accept-Language header when left out.</param>
		[HttpGet("GetCertificate")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
		public async Task<ActionResult> GetCertificate(int learningRecordId, string locale = null, CancellationToken cancellationToken = default)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			try
			{
				var file = await _documents.GetMyCertificatePdfAsync(DocumentRequest(locale), learningRecordId, cancellationToken);
				return file?.Bytes == null ? NotFound() : PdfFile(file);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				Logging.LogException(ex, "Training certificate PDF could not be generated.");
				return StatusCode(StatusCodes.Status503ServiceUnavailable);
			}
		}

		/// <summary>The department's training categories (removed ones left out), for the submission form and the transcript.</summary>
		[HttpGet("GetTrainingCategories")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<TrainingCategoriesResult>> GetTrainingCategories()
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			var categories = (await _learningRecordService.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>())
				.Where(c => c != null && c.DepartmentId == DepartmentId && !c.IsDeleted)
				.OrderBy(c => c.SortOrder).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(c => new TrainingCategoryData
				{
					CategoryId = c.TrainingCategoryId,
					Name = c.Name,
					Code = c.Code,
					Kind = c.Kind,
					SortOrder = c.SortOrder,
					Description = c.Description,
					TargetHours = c.TargetHours,
					TargetPeriod = c.TargetPeriod
				}).ToList();

			var result = new TrainingCategoriesResult { Data = categories };
			return Populate(result, categories.Count);
		}

		/// <summary>
		/// The member submits training hours they completed outside the department (continuing education, CPD, a course), with optional
		/// evidence: <c>Data</c> (one file) and/or <c>Files</c> (round 9B: up to 5; PDF, JPG, JPEG, PNG, GIF, WEBP, DOC, DOCX or
		/// ODT; 10 MB each, 30 MB together, base64). Depending on the department's review settings it waits for a reviewer (Pending,
		/// <c>ReviewRequired</c> true) or is accepted at once as self-reported. Refused with <c>submissions_disabled</c> when the department
		/// does not take submissions from members; other refusals name the field (see <see cref="ExternalTrainingSubmissionResult.Error"/>).
		/// </summary>
		[HttpPost("SubmitExternalTraining")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(MaxSubmissionRequestBytes)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ExternalTrainingSubmissionResult>> SubmitExternalTraining([FromBody] SubmitExternalTrainingInput input, CancellationToken cancellationToken)
		{
			if (await TrainingDisabledAsync())
				return NotFound();

			if (input == null)
				return BadRequest();

			var categories = (await _learningRecordService.GetCategoriesAsync(DepartmentId, false) ?? new List<TrainingCategory>())
				.Where(c => c != null && c.DepartmentId == DepartmentId && !c.IsDeleted).ToList();
			var error = BuildExternalSubmission(input, UserId, categories, DateTime.UtcNow, out var draft, out var fileName, out var contentType, out var data);
			var files = new List<TrainingEvidenceUpload>();
			if (error == null)
				error = BuildEvidenceFiles(input.Files, out files);
			if (error != null)
				return SubmissionRefused(error);

			var evidence = new List<TrainingEvidenceUpload>();
			if (data != null)
				evidence.Add(new TrainingEvidenceUpload { FileName = fileName, ContentType = contentType, Data = data });
			evidence.AddRange(files);
			if (evidence.Count > TrainingSubmissionRules.MaxFilesPerUpload)
				return SubmissionRefused("too_many_files");

			// Round 9B: through the submission service (review decision, review history, notices and workflow triggers).
			var outcome = await _submissions.SubmitAsync(DepartmentId, UserId, draft, evidence, cancellationToken);
			if (outcome?.Refusal != null || outcome?.Record == null)
				return SubmissionRefused(outcome?.Refusal == "learning_record_submissions_disabled" ? "submissions_disabled" : outcome?.Refusal ?? "learning_record_not_found");

			var result = new ExternalTrainingSubmissionResult { Data = MapLearningRecord(outcome.Record, CategoryNames(categories)), ReviewRequired = outcome.ReviewRequired };
			return Populate(result, 1);
		}

		/// <summary>
		/// Round 9B: decodes the extra evidence files of a submission or resubmission (name, accepted type by extension, base64, size). Returns
		/// the refusal code (file_name_required, file_type_not_allowed, file_too_large, file_invalid, too_many_files), or null.
		/// </summary>
		public static string BuildEvidenceFiles(IReadOnlyCollection<TrainingEvidenceFileInput> inputs, out List<TrainingEvidenceUpload> files)
		{
			files = new List<TrainingEvidenceUpload>();
			var given = (inputs ?? Array.Empty<TrainingEvidenceFileInput>()).Where(f => f != null && !string.IsNullOrWhiteSpace(f.Data)).ToList();
			if (given.Count > TrainingSubmissionRules.MaxFilesPerUpload)
				return "too_many_files";

			long total = 0;
			foreach (var input in given)
			{
				var name = System.IO.Path.GetFileName((input.FileName ?? string.Empty).Trim());
				if (name.Length == 0)
					return "file_name_required";
				if (name.Length > 255)
					return "text_too_long";
				var type = Resgrid.Services.TrainingEvidencePolicy.ContentTypeOf(name);
				if (type == null)
					return "file_type_not_allowed";
				if (input.Data.Length > (LearningRecordFile.MaxBytes + 2) / 3 * 4 + 4096)
					return "file_too_large";

				byte[] bytes;
				try
				{
					bytes = Convert.FromBase64String(input.Data);
				}
				catch (FormatException)
				{
					return "file_invalid";
				}

				if (bytes.Length == 0)
					return "file_invalid";
				if (bytes.Length > LearningRecordFile.MaxBytes)
					return "file_too_large";
				total += bytes.Length;
				if (total > TrainingSubmissionRules.MaxUploadBytes)
					return "file_too_large";
				files.Add(new TrainingEvidenceUpload { FileName = name, ContentType = type, Data = bytes, Kind = input.Kind });
			}

			return null;
		}

		private ActionResult SubmissionRefused(string error) =>
			BadRequest(Populate(new ExternalTrainingSubmissionResult { Error = error }, 0, ResponseHelper.Failure));

		/// <summary>A refusal code thrown by the learning record service (learning_record_*), as opposed to an unexpected failure.</summary>
		private static bool IsServiceCode(InvalidOperationException ex) =>
			ex?.Message != null && System.Text.RegularExpressions.Regex.IsMatch(ex.Message, "^[a-z][a-z0-9_]{2,80}$",
				System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

		/// <summary>
		/// Checks an outside training submission as the web form does and builds the Pending draft and the evidence file. Returns
		/// the refusal code, or null when the submission may go to the service.
		/// </summary>
		public static string BuildExternalSubmission(SubmitExternalTrainingInput input, string userId, IReadOnlyCollection<TrainingCategory> categories, DateTime utcNow,
			out LearningRecordDraft draft, out string fileName, out string contentType, out byte[] data)
		{
			draft = null;
			fileName = null;
			contentType = null;
			data = null;

			var title = input.Title?.Trim() ?? string.Empty;
			if (title.Length == 0)
				return "title_required";
			if (title.Length > LearningRecord.MaxTitleLength)
				return "title_too_long";

			var completed = input.CompletedOn.Kind == DateTimeKind.Local ? input.CompletedOn.ToUniversalTime() : DateTime.SpecifyKind(input.CompletedOn, DateTimeKind.Utc);
			// A date picked in a time zone ahead of UTC may read as tomorrow; anything later is in the future.
			if (completed.Year < 1900 || completed > utcNow.AddDays(1))
				return "date_invalid";

			if (!input.Hours.HasValue || input.Hours.Value <= 0 || input.Hours.Value > 9999)
				return "hours_invalid";
			if (input.CeHours.HasValue && (input.CeHours.Value < 0 || input.CeHours.Value > 9999))
				return "ce_hours_invalid";

			int? categoryId = input.CategoryId.HasValue && input.CategoryId.Value > 0 ? input.CategoryId : null;
			if (categoryId.HasValue && (categories == null || !categories.Any(c => c != null && c.TrainingCategoryId == categoryId.Value && !c.IsDeleted)))
				return "category_invalid";
			if (input.Method.HasValue && !Enum.IsDefined(typeof(MethodsOfTraining), input.Method.Value))
				return "method_invalid";

			var ceProvider = string.IsNullOrWhiteSpace(input.CeProvider) ? null : input.CeProvider.Trim();
			var ceNumber = string.IsNullOrWhiteSpace(input.CeCourseNumber) ? null : input.CeCourseNumber.Trim();
			var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
			if (ceProvider?.Length > 250 || ceNumber?.Length > 100 || notes?.Length > LearningRecord.MaxNotesLength)
				return "text_too_long";

			if (!string.IsNullOrWhiteSpace(input.Data))
			{
				var name = System.IO.Path.GetFileName((input.FileName ?? string.Empty).Trim());
				if (name.Length == 0)
					return "file_name_required";
				if (name.Length > 255)
					return "text_too_long";
				var type = Resgrid.Services.TrainingEvidencePolicy.ContentTypeOf(name);
				if (type == null)
					return "file_type_not_allowed";

				// Base64 is 4 characters per 3 bytes: refuse on the encoded length before decoding allocates an oversized buffer
				// (with room for line breaks); the decoded length is checked exactly below.
				if (input.Data.Length > (LearningRecordFile.MaxBytes + 2) / 3 * 4 + 4096)
					return "file_too_large";

				byte[] bytes;
				try
				{
					bytes = Convert.FromBase64String(input.Data);
				}
				catch (FormatException)
				{
					return "file_invalid";
				}

				if (bytes.Length == 0)
					return "file_invalid";
				if (bytes.Length > LearningRecordFile.MaxBytes)
					return "file_too_large";

				fileName = name;
				contentType = type;
				data = bytes;
			}

			draft = new LearningRecordDraft
			{
				UserId = userId,
				Title = title,
				CompletedOn = completed,
				Hours = Math.Round(input.Hours.Value, 2),
				CategoryId = categoryId,
				MethodOfTraining = input.Method,
				CeProvider = ceProvider,
				CeCourseNumber = ceNumber,
				CeHours = input.CeHours.HasValue ? Math.Round(input.CeHours.Value, 2) : (decimal?)null,
				Notes = notes,
				Status = LearningRecordStatuses.Pending
			};

			return null;
		}

		private static Dictionary<int, string> CategoryNames(IEnumerable<TrainingCategory> categories) =>
			(categories ?? Enumerable.Empty<TrainingCategory>()).Where(c => c != null)
				.GroupBy(c => c.TrainingCategoryId).ToDictionary(g => g.Key, g => g.First().Name);

		public static LearningRecordData MapLearningRecord(LearningRecord record, IReadOnlyDictionary<int, string> categoryNames,
			IReadOnlyDictionary<int, TrainingCompletionRecording> recordings = null)
		{
			string categoryName = null;
			if (record.CategoryId.HasValue && categoryNames != null)
				categoryNames.TryGetValue(record.CategoryId.Value, out categoryName);

			return new LearningRecordData
			{
				Id = record.LearningRecordId,
				Title = record.Title,
				CourseCode = record.CourseCode,
				CompletedOn = record.CompletedOn,
				Hours = record.Hours,
				CategoryId = record.CategoryId,
				CategoryName = categoryName,
				SourceKind = record.SourceKind,
				SourceKindName = Enum.IsDefined(typeof(LearningRecordSourceKinds), record.SourceKind) ? ((LearningRecordSourceKinds)record.SourceKind).ToString() : null,
				Status = record.Status,
				StatusName = Enum.IsDefined(typeof(LearningRecordStatuses), record.Status) ? ((LearningRecordStatuses)record.Status).ToString() : null,
				CeHours = record.CeHours,
				CeProvider = record.CeProvider,
				CeCourseNumber = record.CeCourseNumber,
				IsBackfill = record.IsBackfill,
				PossibleDuplicate = record.HasUnresolvedDuplicate,
				CompletionMethod = Enum.IsDefined(typeof(LearningCompletionMethods), record.CompletionMethod) ? ((LearningCompletionMethods)record.CompletionMethod).ToString() : null,
				RecordedByUserId = record.IsRecordedByOther ? record.RecordedByUserId : null,
				RecordedOn = record.IsRecordedByOther ? record.RecordedOn : null,
				RecordedSource = record.IsRecordedByOther && record.RecordedCompletionId.HasValue && recordings != null && recordings.TryGetValue(record.RecordedCompletionId.Value, out var recording)
					? TrainingCompletionRecordingRules.SourceLabel(recording) : null,
				RecordedNote = record.IsRecordedByOther && record.RecordedCompletionId.HasValue && recordings != null && recordings.TryGetValue(record.RecordedCompletionId.Value, out var noted)
					? noted.Note : null,
				AttestationVersion = record.IsRecordedByOther && record.RecordedCompletionId.HasValue && recordings != null && recordings.TryGetValue(record.RecordedCompletionId.Value, out var attested)
					? attested.AttestationVersion : null,
				// Round 9B: self-reported vs reviewed vs recorded by an instructor, and a submission's review.
				Provenance = TrainingSubmissionRules.ProvenanceOf(record).ToString(),
				ReviewState = TrainingSubmissionRules.StateOf(record)?.ToString(),
				SubmittedOn = TrainingSubmissionRules.IsSubmission(record) ? record.SubmittedOn : null,
				ReviewNote = TrainingSubmissionRules.IsSubmission(record) ? record.ReviewNote : null
			};
		}

		#endregion Transcript (Training T1)

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
				RequiresAcknowledgment = training.RequiresAcknowledgment,
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
