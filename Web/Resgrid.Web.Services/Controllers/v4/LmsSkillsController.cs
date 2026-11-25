using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Training T4 evaluator mode for Omni (LMS add-on, dark behind Training.Lms): the evaluator's pending evaluations and signatures, the
	/// sheets they may run (cacheable for offline use), start / save / evidence / submit online, a whole evaluation submitted offline with
	/// an outbox idempotency key (a replay returns the first result), and signing the evaluation at L2 (step-up from the session's server-side
	/// evidence; v4 has no passkey-bound signing). The member's own results and task books too. 404 unless the Trainings module and the
	/// operator flag are on; new evaluations need an active add-on (lapse rule 3). Omni itself is out of scope for this build.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsSkillsController : V4AuthenticatedApiControllerbase
	{
		private static readonly Regex ServiceCode = new Regex("^lms_(skill|task_book)_[a-z0-9_]{2,80}", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsSkillSheetService _sheets;
		private readonly ILmsSkillEvaluationService _evaluations;
		private readonly ILmsTaskBookService _taskBooks;
		private readonly ISignatureRequestService _requests;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;

		public LmsSkillsController(ILmsGateService gate, ILmsAccessService access, ILmsSkillSheetService sheets, ILmsSkillEvaluationService evaluations, ILmsTaskBookService taskBooks,
			ISignatureRequestService requests, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy)
		{
			_gate = gate;
			_access = access;
			_sheets = sheets;
			_evaluations = evaluations;
			_taskBooks = taskBooks;
			_requests = requests;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The module and the flag (one gate read); null when skills are dark here.</summary>
		private async Task<LmsActor> ActorAsync()
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return null;
			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			return actor?.Gate?.FlagOn == true && actor.IsCurrentMember ? actor : null;
		}

		public static LmsSkillEvaluationData Map(LmsSkillEvaluation evaluation, IEnumerable<LmsSkillEvaluationMember> members, bool withAnswers) => evaluation == null ? null : new LmsSkillEvaluationData
		{
			EvaluationId = evaluation.LmsSkillEvaluationId,
			SkillSheetId = evaluation.LmsSkillSheetId,
			SkillSheetVersionId = evaluation.LmsSkillSheetVersionId,
			Title = evaluation.Title,
			Code = evaluation.Code,
			Mode = ((LmsSkillEvaluationModes)evaluation.Mode).ToString(),
			Status = ((LmsSkillEvaluationStatuses)evaluation.Status).ToString(),
			Result = ((LmsSkillEvaluationResults)evaluation.Result).ToString(),
			Score = evaluation.Score,
			CriticalFailed = evaluation.CriticalFailed,
			TimeExceeded = evaluation.TimeExceeded,
			EvaluatorUserId = evaluation.EvaluatorUserId,
			MemberUserIds = (members ?? Enumerable.Empty<LmsSkillEvaluationMember>()).Where(m => m.LmsSkillEvaluationId == evaluation.LmsSkillEvaluationId && m.IsIncluded).Select(m => m.UserId).ToList(),
			StartedOn = evaluation.StartedOn,
			SubmittedOn = evaluation.SubmittedOn,
			CompletedOn = evaluation.CompletedOn,
			DurationSeconds = evaluation.DurationSeconds,
			TaskBookId = evaluation.LmsTaskBookId,
			TaskKey = evaluation.TaskKey,
			CallId = evaluation.CallId,
			DeploymentId = evaluation.DeploymentId,
			RowVersion = evaluation.RowVersion,
			SignatureRequestId = evaluation.SignatureRequestId,
			ContentHash = evaluation.ContentHash,
			IdempotencyKey = evaluation.IdempotencyKey,
			Answers = withAnswers ? LmsSkillAnswers.Parse(evaluation.AnswersJson) : null
		};

		private async Task<ActionResult<LmsSkillEvaluationResult>> ActAsync(Func<Task<LmsSkillEvaluation>> work)
		{
			var result = new LmsSkillEvaluationResult();
			try
			{
				var evaluation = await work();
				result.Data = Map(evaluation, evaluation == null ? null : await _evaluations.GetMembersAsync(DepartmentId, new[] { evaluation.LmsSkillEvaluationId }), true);
				return Ok(Populate(result, 1));
			}
			catch (InvalidOperationException ex) when (ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				result.Error = ex.Message;
				return Ok(Populate(result, 0, ResponseHelper.Failure));
			}
		}

		/// <summary>The evaluator's open evaluations and the evaluator signatures waiting for them.</summary>
		[HttpGet("GetMyPendingEvaluations")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillPendingResult>> GetMyPendingEvaluations()
		{
			if (await ActorAsync() == null)
				return NotFound();
			var open = await _evaluations.GetOpenForEvaluatorAsync(DepartmentId, UserId);
			var members = await _evaluations.GetMembersAsync(DepartmentId, open.Select(e => e.LmsSkillEvaluationId));
			var signatures = (await _requests.GetPendingForUserAsync(DepartmentId, UserId) ?? new List<PendingSignatureItem>())
				.Where(p => p.SubjectKind == (int)SignatureSubjectKinds.SkillEvaluation).ToList();
			var result = new LmsSkillPendingResult
			{
				Data = new LmsSkillPendingData
				{
					Evaluations = open.Select(e => Map(e, members, false)).ToList(),
					Signatures = signatures.Select(s => new LmsSkillPendingSignatureData
					{
						SignatureRequestId = s.SignatureRequestId, SignatureRequestSignerId = s.SignatureRequestSignerId, SubjectId = s.SubjectId, Title = s.Title,
						StepLabel = s.StepLabel, ExpiresOn = s.ExpiresOn, ContentCode = s.ContentCode
					}).ToList()
				}
			};
			return Ok(Populate(result, result.Data.Evaluations.Count + result.Data.Signatures.Count));
		}

		/// <summary>The published sheets the member may evaluate on, with their steps (cache them for offline evaluations by ContentHash).</summary>
		[HttpGet("GetEvaluatorSheets")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillSheetsResult>> GetEvaluatorSheets()
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			var result = new LmsSkillSheetsResult();
			foreach (var sheet in (await _sheets.GetSheetsAsync(DepartmentId)).Where(s => s.CurrentVersionId.HasValue))
			{
				if (!await _evaluations.CanEvaluateAsync(actor, sheet))
					continue;
				var data = await SheetDataAsync(sheet);
				if (data != null)
					result.Data.Add(data);
			}
			return Ok(Populate(result, result.Data.Count));
		}

		private async Task<LmsSkillSheetData> SheetDataAsync(LmsSkillSheet sheet)
		{
			var version = await _sheets.GetPublishedVersionAsync(DepartmentId, sheet.LmsSkillSheetId);
			if (version == null)
				return null;
			var next = SignerEligibility.Parse(sheet.NextSignerEligibilityJson);
			return new LmsSkillSheetData
			{
				SkillSheetId = sheet.LmsSkillSheetId,
				SkillSheetVersionId = version.LmsSkillSheetVersionId,
				Title = sheet.Title,
				Code = sheet.Code,
				Kind = ((LmsSkillSheetKinds)sheet.Kind).ToString(),
				Version = version.Label,
				ContentHash = version.ContentHash,
				PassThreshold = version.PassThreshold,
				TimeLimitSeconds = version.TimeLimitSeconds,
				AllowCrewEvaluation = sheet.AllowCrewEvaluation,
				AllowSelfEvaluation = sheet.AllowSelfEvaluation,
				NextSignerRequired = next != null,
				NextSignerUserIds = next == null ? new List<string>() : await _evaluations.GetNextSignerCandidatesAsync(DepartmentId, sheet),
				Steps = LmsSkillSheetContent.Parse(version.ContentJson).Steps
			};
		}

		[HttpGet("GetEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationResult>> GetEvaluation(int evaluationId)
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			var view = await _evaluations.GetViewAsync(DepartmentId, evaluationId);
			var e = view?.Evaluation;
			if (e == null || !(actor.MayManage || LmsSkillRules.SameUser(e.EvaluatorUserId, UserId) || LmsSkillRules.SameUser(e.NextSignerUserId, UserId) ||
				view.Members.Any(m => LmsSkillRules.SameUser(m.UserId, UserId))))
				return NotFound();
			return Ok(Populate(new LmsSkillEvaluationResult { Data = Map(e, view.Members, true) }, 1));
		}

		/// <summary>The member's own evaluations (results), newest first.</summary>
		[HttpGet("GetMyResults")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationsResult>> GetMyResults()
		{
			if (await ActorAsync() == null)
				return NotFound();
			var mine = (await _evaluations.GetForMemberAsync(DepartmentId, UserId)).Where(e => e.Status != (int)LmsSkillEvaluationStatuses.Voided).Take(100).ToList();
			var members = await _evaluations.GetMembersAsync(DepartmentId, mine.Select(e => e.LmsSkillEvaluationId));
			var result = new LmsSkillEvaluationsResult { Data = mine.Select(e => Map(e, members, false)).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		[HttpPost("StartEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationResult>> StartEvaluation([FromBody] LmsSkillStartInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			return await ActAsync(() => _evaluations.StartAsync(actor, new LmsSkillEvaluationStart
			{
				LmsSkillSheetId = input.SkillSheetId,
				MemberUserIds = input.MemberUserIds ?? new List<string>(),
				LmsTaskBookId = input.TaskBookId,
				TaskKey = input.TaskKey,
				CallId = input.CallId,
				DeploymentId = input.DeploymentId,
				Location = input.Location,
				IdempotencyKey = input.IdempotencyKey,
				StartedOnUtc = input.StartedOn.HasValue ? DateTime.SpecifyKind(input.StartedOn.Value, DateTimeKind.Utc) : (DateTime?)null
			}, cancellationToken));
		}

		[HttpPost("SaveEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationResult>> SaveEvaluation([FromBody] LmsSkillSaveInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			return await ActAsync(() => _evaluations.SaveAnswersAsync(actor, input.EvaluationId, input.RowVersion, input.Answers ?? new LmsSkillAnswers(), input.Notes, input.PassAll,
				cancellationToken));
		}

		/// <summary>Evidence as base64 (a replay of the same file for the same step returns the stored one).</summary>
		[HttpPost("AddEvidence")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[RequestSizeLimit(45 * 1024 * 1024)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvidenceResult>> AddEvidence([FromBody] LmsSkillEvidenceInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			byte[] data;
			try
			{
				data = Convert.FromBase64String(input.Data ?? string.Empty);
			}
			catch (FormatException)
			{
				return Ok(Populate(new LmsSkillEvidenceResult { Error = "lms_skill_evidence_size" }, 0, ResponseHelper.Failure));
			}
			try
			{
				var file = await _evaluations.AddEvidenceAsync(actor, input.EvaluationId, input.StepKey, input.FileName, input.ContentType, data, cancellationToken);
				return Ok(Populate(new LmsSkillEvidenceResult { FileId = file.LmsSkillEvaluationFileId, Sha256 = file.Sha256 }, 1));
			}
			catch (InvalidOperationException ex) when (ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				return Ok(Populate(new LmsSkillEvidenceResult { Error = ex.Message }, 0, ResponseHelper.Failure));
			}
		}

		[HttpPost("SubmitEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationResult>> SubmitEvaluation([FromBody] LmsSkillSubmitInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			return await ActAsync(() => _evaluations.SubmitAsync(actor, input.EvaluationId, new LmsSkillEvaluationSubmit
			{
				ExpectedRowVersion = input.RowVersion,
				NextSignerUserId = input.NextSignerUserId,
				EndedOnUtc = input.EndedOn.HasValue ? DateTime.SpecifyKind(input.EndedOn.Value, DateTimeKind.Utc) : (DateTime?)null,
				Culture = System.Globalization.CultureInfo.CurrentUICulture.Name,
				IpAddress = HttpContext?.Connection?.RemoteIpAddress?.ToString()
			}, cancellationToken));
		}

		/// <summary>
		/// A whole evaluation captured offline (the Omni outbox): created, graded and submitted in one call. The idempotency key is required;
		/// a replay returns what the first delivery made (submitted, or left in progress when the engine refused it).
		/// </summary>
		[HttpPost("SubmitOfflineEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillEvaluationResult>> SubmitOfflineEvaluation([FromBody] LmsSkillOfflineInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			return await ActAsync(() => _evaluations.SubmitOfflineAsync(actor, new LmsSkillOfflineSubmission
			{
				IdempotencyKey = input.IdempotencyKey,
				LmsSkillSheetId = input.SkillSheetId,
				LmsSkillSheetVersionId = input.SkillSheetVersionId,
				MemberUserIds = input.MemberUserIds ?? new List<string>(),
				Answers = input.Answers,
				Notes = input.Notes,
				StartedOnUtc = DateTime.SpecifyKind(input.StartedOn, DateTimeKind.Utc),
				EndedOnUtc = DateTime.SpecifyKind(input.EndedOn, DateTimeKind.Utc),
				LmsTaskBookId = input.TaskBookId,
				TaskKey = input.TaskKey,
				CallId = input.CallId,
				DeploymentId = input.DeploymentId,
				Location = input.Location,
				NextSignerUserId = input.NextSignerUserId,
				Culture = System.Globalization.CultureInfo.CurrentUICulture.Name
			}, cancellationToken));
		}

		/// <summary>The evaluator (or the next signer) signs the evaluation's open step at L2: a step-up recorded for this session (no passkey on v4).</summary>
		[HttpPost("SignEvaluation")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsSkillSignResult>> SignEvaluation([FromBody] LmsSkillSignInput input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			var view = await _evaluations.GetViewAsync(DepartmentId, input.EvaluationId);
			if (view?.Evaluation?.SignatureRequestId == null)
				return NotFound();
			var proof = await ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);
			var outcome = await _requests.SignStepAsync(new SignatureStepSignInput
			{
				DepartmentId = DepartmentId,
				SignatureRequestId = view.Evaluation.SignatureRequestId.Value,
				Proof = proof
			}, cancellationToken);
			var after = await _evaluations.GetViewAsync(DepartmentId, input.EvaluationId);
			var result = new LmsSkillSignResult
			{
				Succeeded = outcome?.Succeeded == true,
				Refusal = outcome == null ? SignatureRequestRefusals.InvalidRequest.ToString() : outcome.Refusal.ToString(),
				EvaluationStatus = after?.Evaluation == null ? null : ((LmsSkillEvaluationStatuses)after.Evaluation.Status).ToString()
			};
			return Ok(Populate(result, 1, result.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure));
		}

		/// <summary>The member's task books, and for evaluators the open books they may sign tasks of.</summary>
		[HttpGet("GetTaskBooks")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTaskBooksResult>> GetTaskBooks()
		{
			var actor = await ActorAsync();
			if (actor == null)
				return NotFound();
			var books = new List<LmsTaskBook>(await _taskBooks.GetForMemberAsync(DepartmentId, UserId));
			foreach (var book in await _taskBooks.GetTaskBooksAsync(DepartmentId, true))
			{
				if (books.Any(b => b.LmsTaskBookId == book.LmsTaskBookId) || LmsSkillRules.SameUser(book.UserId, UserId))
					continue;
				var rule = SignerEligibility.Parse(book.EvaluatorEligibilityJson);
				if (rule == null ? actor.MayManage : await _requests.IsEligibleAsync(DepartmentId, UserId, rule))
					books.Add(book);
			}

			var result = new LmsTaskBooksResult();
			foreach (var book in books)
			{
				var view = await _taskBooks.GetViewAsync(DepartmentId, book.LmsTaskBookId);
				if (view == null)
					continue;
				result.Data.Add(new LmsTaskBookData
				{
					TaskBookId = book.LmsTaskBookId,
					UserId = book.UserId,
					PositionCode = book.PositionCode,
					Name = book.Name,
					Status = ((LmsTaskBookStatuses)book.Status).ToString(),
					InitiatedOn = book.InitiatedOn,
					ExpiresOn = book.ExpiresOn,
					Tasks = view.Tasks.Select(t => new LmsTaskBookTaskData
					{
						Key = t.Task.Key, Number = t.Task.Number, Title = t.Task.Title, RequiredEvaluations = t.Task.RequiredEvaluations, Counted = t.Counted, Complete = t.Complete,
						RequiresIncident = t.Task.RequiresIncident, SkillSheetId = t.Task.LmsSkillSheetId
					}).ToList()
				});
			}
			return Ok(Populate(result, result.Data.Count));
		}
	}
}
