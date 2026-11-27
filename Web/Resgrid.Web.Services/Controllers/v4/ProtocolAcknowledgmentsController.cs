using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Protocols;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Protocol acknowledgment for Omni (P2; the older apps get no client change and their members acknowledge in the browser from
	/// the notification link, R-S7): the member's acknowledgments and must-read bulletins, read-and-sign with the version id and the
	/// content hash read (accepted only while that version is the one in force, R-P11), and the primary file of a document-backed
	/// version behind the per-version read rule (R-P4). Round 5F (LMS): the question-bank acknowledgment quiz (questions without a key,
	/// scored on the server) and the member's weekly micro-learning questions, through IProtocolLearningExtensions.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class ProtocolAcknowledgmentsController : V4AuthenticatedApiControllerbase
	{
		private readonly IProtocolAccessService _access;
		private readonly IProtocolAcknowledgmentService _acknowledgments;
		private readonly IProtocolDocumentService _documents;
		private readonly IProtocolLibraryService _library;
		private readonly IMfaEvidenceService _evidence;
		private readonly IMfaPolicyService _mfaPolicy;
		private readonly IProtocolLearningExtensions _learning;

		private readonly IStepUpCapabilityService _stepUp;

		public ProtocolAcknowledgmentsController(IProtocolAccessService access, IProtocolAcknowledgmentService acknowledgments, IProtocolDocumentService documents,
			IProtocolLibraryService library, IMfaEvidenceService evidence, IMfaPolicyService mfaPolicy, IProtocolLearningExtensions learning,
			IStepUpCapabilityService stepUp)
		{
			_stepUp = stepUp;
			_learning = learning;
			_access = access;
			_acknowledgments = acknowledgments;
			_documents = documents;
			_library = library;
			_evidence = evidence;
			_mfaPolicy = mfaPolicy;
		}

		/// <summary>The member's outstanding acknowledgments (bulletins first, then the onboarding bundle's order), and completed ones when asked.</summary>
		[HttpGet("GetMyAcknowledgments")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetMyProtocolAcknowledgmentsResult>> GetMyAcknowledgments(bool includeCompleted = false)
		{
			var items = await _acknowledgments.GetMyAcknowledgmentsAsync(DepartmentId, UserId, includeCompleted, DateTime.UtcNow);
			return Ok(await ToResultAsync(items));
		}

		/// <summary>The member's must-read bulletins in force, not yet acknowledged (Omni decides where to show them: never over a call).</summary>
		[HttpGet("GetMustRead")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetMyProtocolAcknowledgmentsResult>> GetMustRead()
		{
			var items = await _acknowledgments.GetMustReadAsync(DepartmentId, UserId, DateTime.UtcNow);
			return Ok(await ToResultAsync(items));
		}

		private async Task<GetMyProtocolAcknowledgmentsResult> ToResultAsync(List<ProtocolAcknowledgmentItem> items)
		{
			var result = new GetMyProtocolAcknowledgmentsResult();
			var versions = new Dictionary<int, ProtocolVersion>();
			foreach (var item in items)
			{
				var ack = item.Acknowledgment;
				if (!versions.TryGetValue(ack.ProtocolVersionId, out var version))
					versions[ack.ProtocolVersionId] = version = await _library.GetVersionAsync(DepartmentId, ack.ProtocolVersionId);

				result.Data.Add(new ProtocolAcknowledgmentResultData
				{
					AcknowledgmentId = ack.ProtocolAcknowledgmentId,
					ProtocolId = item.ProtocolId,
					ProtocolVersionId = ack.ProtocolVersionId,
					Code = item.Code,
					Name = item.Name,
					Kind = item.Kind,
					IsBulletin = item.IsBulletin,
					VersionLabel = item.VersionLabel,
					ContentHash = item.ContentHash,
					Status = ack.Status == (int)ProtocolAcknowledgmentStatuses.Acknowledged ? "acknowledged"
						: ack.Status == (int)ProtocolAcknowledgmentStatuses.AwaitingQuiz ? "awaiting_quiz" : "open",
					IsOverdue = item.IsOverdue,
					AssignedOn = ack.AssignedOn,
					DueOn = ack.DueOn,
					SignedOn = ack.SignedOn,
					EffectiveOn = item.EffectiveOn,
					ExpiresOn = item.ExpiresOn,
					BundleOrder = ack.BundleOrder,
					QuizTrainingId = item.QuizTrainingId,
					QuizTrainingName = item.QuizTrainingName,
					QuizPassed = item.QuizPassed,
					IsDocument = version?.IsDocument ?? false,
					BankQuizOwed = item.BankQuizOwed
				});
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Read-and-sign with the version id and the content hash the member read. Online, the app has shown the version; offline
		/// (queued), it is accepted only while that version is still the one in force, else the answer is ReadNewerVersion with the
		/// version to read (R-P11). A department that requires a step-up for read-and-sign needs this session's second-factor evidence.
		/// </summary>
		[HttpPost("Acknowledge")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<AcknowledgeProtocolResult>> Acknowledge([FromBody] AcknowledgeProtocolInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.ProtocolVersionId <= 0 || string.IsNullOrWhiteSpace(input.ContentHash))
				return BadRequest();

			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!input.Offline)
			{
				// An online acknowledgment from the app: opening the version in the app is the read; record it before signing.
				await _acknowledgments.MarkOpenedAsync(DepartmentId, UserId, input.ProtocolVersionId, DateTime.UtcNow, cancellationToken);
			}

			var proof = await ApiSignatureProof.BuildAsync(HttpContext, UserId, DepartmentId, _evidence, _mfaPolicy, cancellationToken);
			var outcome = await _acknowledgments.AcknowledgeAsync(reader, input.ProtocolVersionId, input.ContentHash, proof, input.Offline, null, cancellationToken);
			// The department requires a step-up for read-and-sign: the one refusal (403 step_up_required, area acknowledgments).
			if (outcome.Outcome == ProtocolAcknowledgeOutcomes.StepUpRequired)
				return await ApiStepUp.RefuseAsync(HttpContext, _stepUp, DepartmentId, UserId, Model.Security.StepUpArea.Acknowledgments, cancellationToken);

			var result = new AcknowledgeProtocolResult
			{
				Data = new AcknowledgeProtocolResultData
				{
					Outcome = outcome.Outcome.ToString(),
					Accepted = outcome.Succeeded,
					CurrentVersionId = outcome.CurrentVersionId,
					CurrentContentHash = outcome.CurrentContentHash,
					AcknowledgmentId = outcome.Acknowledgment?.ProtocolAcknowledgmentId
				}
			};

			if (outcome.Outcome == ProtocolAcknowledgeOutcomes.ReadNewerVersion && outcome.CurrentVersionId.HasValue && string.IsNullOrEmpty(result.Data.CurrentContentHash))
				result.Data.CurrentContentHash = (await _library.GetVersionAsync(DepartmentId, outcome.CurrentVersionId.Value))?.ContentHash;

			result.PageSize = 1;
			result.Status = outcome.Succeeded ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Starts (or resumes) the question-bank quiz of a version the member owes (LMS). Questions and options only; never a key.</summary>
		[HttpGet("GetBankQuiz")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetBankQuizResult>> GetBankQuiz(int protocolVersionId, string locale = null, CancellationToken cancellationToken = default)
		{
			var quiz = await _learning.StartBankQuizAsync(await _access.GetReaderAsync(DepartmentId, UserId), protocolVersionId, locale, cancellationToken);
			var result = new GetBankQuizResult
			{
				Data = quiz.Error == ProtocolLearningErrors.None ? quiz : null,
				Error = quiz.Error.ToString(),
				PageSize = quiz.Error == ProtocolLearningErrors.None ? 1 : 0,
				Status = quiz.Error == ProtocolLearningErrors.None ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Scores a quiz attempt on the server; a pass completes a signed acknowledgment that was waiting for it.</summary>
		[HttpPost("SubmitBankQuiz")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<SubmitBankQuizResult>> SubmitBankQuiz([FromBody] SubmitBankQuizInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.AttemptId <= 0)
				return BadRequest();
			var scored = await _learning.SubmitBankQuizAsync(await _access.GetReaderAsync(DepartmentId, UserId), input.AttemptId, input.Answers, cancellationToken);
			if (scored.Error == ProtocolLearningErrors.NotFound || scored.Error == ProtocolLearningErrors.NotAuthorized)
				return NotFound();
			var result = new SubmitBankQuizResult
			{
				Data = scored,
				Error = scored.Error.ToString(),
				PageSize = 1,
				Status = scored.Error == ProtocolLearningErrors.None ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The member's weekly micro-learning questions (LMS), newest first.</summary>
		[HttpGet("GetWeeklyQuestions")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<GetWeeklyQuestionsResult>> GetWeeklyQuestions()
		{
			var result = new GetWeeklyQuestionsResult { Data = await _learning.GetMyMicroLearningAsync(DepartmentId, UserId, 12) };
			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Answers a weekly question once; the answer returns the correct keys and the explanation.</summary>
		[HttpPost("AnswerWeeklyQuestion")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult<AnswerWeeklyQuestionResult>> AnswerWeeklyQuestion([FromBody] AnswerWeeklyQuestionInput input, CancellationToken cancellationToken)
		{
			if (input == null || input.Id <= 0)
				return BadRequest();
			var item = await _learning.AnswerMicroLearningAsync(DepartmentId, UserId, input.Id, input.Keys, cancellationToken);
			if (item.Error == ProtocolLearningErrors.NotFound)
				return NotFound();
			var result = new AnswerWeeklyQuestionResult
			{
				Data = item,
				Error = item.Error.ToString(),
				PageSize = 1,
				Status = item.Error == ProtocolLearningErrors.None ? ResponseHelper.Success : ResponseHelper.Failure
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>The primary file of a document-backed version (PDF or DOCX), only for a member who may read that version.</summary>
		[HttpGet("GetVersionFile")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<ActionResult> GetVersionFile(int protocolVersionId)
		{
			var file = await _documents.GetReadableFileAsync(await _access.GetReaderAsync(DepartmentId, UserId), protocolVersionId);
			if (file?.Data == null)
				return NotFound();

			return File(file.Data, string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType, ProtocolDocumentRules.SafeFileName(file.FileName));
		}
	}
}
