using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Lms;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Lms;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Training T3 part 2 (LMS add-on, dark behind Training.Lms): training requests (submit, withdraw, report taken, decide) and the
	/// member's competency gaps, for Omni. 404 unless the Trainings module and the operator flag are on; new requests need an active
	/// add-on (lapse rule 3), open ones can still be decided, withdrawn and completed.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsProgramsController : V4AuthenticatedApiControllerbase
	{
		private static readonly Regex ServiceCode = new Regex("^[a-z][a-z0-9_]{2,80}$", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsTrainingRequestService _requests;
		private readonly ILmsCompetencyService _competencies;

		public LmsProgramsController(ILmsGateService gate, ILmsAccessService access, ILmsTrainingRequestService requests, ILmsCompetencyService competencies)
		{
			_gate = gate;
			_access = access;
			_requests = requests;
			_competencies = competencies;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The module and the flag (one gate read); null when the programs are dark here.</summary>
		private async Task<LmsActor> ActorAsync()
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return null;
			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			return actor?.Gate?.FlagOn == true ? actor : null;
		}

		public static LmsTrainingRequestData Map(LmsTrainingRequest request) => request == null ? null : new LmsTrainingRequestData
		{
			TrainingRequestId = request.LmsTrainingRequestId,
			UserId = request.UserId,
			Title = request.Title,
			Provider = request.Provider,
			Location = request.Location,
			CourseCode = request.CourseCode,
			StartsOn = request.StartsOn,
			EndsOn = request.EndsOn,
			Hours = request.Hours,
			CategoryId = request.CategoryId,
			CostTuition = request.CostTuition,
			CostTravel = request.CostTravel,
			CostOther = request.CostOther,
			ActualCost = request.ActualCost,
			Currency = request.Currency,
			Justification = request.Justification,
			Status = ((LmsTrainingRequestStatuses)request.Status).ToString(),
			CurrentStep = request.CurrentStep.HasValue ? ((LmsTrainingRequestSteps)request.CurrentStep.Value).ToString() : null,
			SubmittedOn = request.SubmittedOn,
			DecidedOn = request.DecidedOn,
			CompletedOn = request.CompletedOn,
			LearningRecordId = request.LearningRecordId
		};

		private async Task<ActionResult<LmsTrainingRequestResult>> ActAsync(Func<Task<LmsTrainingRequest>> work)
		{
			var result = new LmsTrainingRequestResult();
			try
			{
				result.Data = Map(await work());
				return Ok(Populate(result, 1));
			}
			catch (InvalidOperationException ex) when (ex.Message != null && ServiceCode.IsMatch(ex.Message))
			{
				result.Error = ex.Message;
				return Ok(Populate(result, 0, ResponseHelper.Failure));
			}
		}

		[HttpGet("GetMyTrainingRequests")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestsResult>> GetMyTrainingRequests()
		{
			if (await ActorAsync() == null)
				return NotFound();
			var result = new LmsTrainingRequestsResult { Data = (await _requests.GetMineAsync(DepartmentId, UserId)).Select(Map).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		[HttpGet("GetTrainingRequestsAwaitingMe")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestsResult>> GetTrainingRequestsAwaitingMe()
		{
			if (await ActorAsync() == null)
				return NotFound();
			var result = new LmsTrainingRequestsResult { Data = (await _requests.GetAwaitingDecisionAsync(DepartmentId, UserId)).Select(Map).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		[HttpPost("SubmitTrainingRequest")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestResult>> SubmitTrainingRequest([FromBody] LmsTrainingRequestInput input, CancellationToken cancellationToken)
		{
			var actor = await ActorAsync();
			if (actor == null || input == null)
				return NotFound();
			if (!actor.Gate.CanAuthor)
				return Ok(Populate(new LmsTrainingRequestResult { Error = "lms_program_addon_required" }, 0, ResponseHelper.Failure));

			return await ActAsync(() => _requests.SubmitAsync(DepartmentId, UserId, new LmsTrainingRequest
			{
				Title = input.Title,
				Provider = input.Provider,
				Location = input.Location,
				CourseCode = input.CourseCode,
				StartsOn = input.StartsOn,
				EndsOn = input.EndsOn,
				Hours = input.Hours,
				CategoryId = input.CategoryId > 0 ? input.CategoryId : null,
				CostTuition = input.CostTuition,
				CostTravel = input.CostTravel,
				CostOther = input.CostOther,
				Justification = input.Justification
			}, cancellationToken));
		}

		[HttpPost("WithdrawTrainingRequest")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestResult>> WithdrawTrainingRequest([FromBody] LmsTrainingRequestIdInput input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			return await ActAsync(() => _requests.WithdrawAsync(DepartmentId, input.TrainingRequestId, UserId, cancellationToken));
		}

		[HttpPost("DecideTrainingRequest")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestResult>> DecideTrainingRequest([FromBody] LmsTrainingRequestDecisionInput input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			return await ActAsync(() => _requests.DecideAsync(DepartmentId, input.TrainingRequestId, UserId, input.Approve, input.Note, cancellationToken));
		}

		/// <summary>The member took the class: writes the external submission an officer verifies (evidence can be added on the web).</summary>
		[HttpPost("CompleteTrainingRequest")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsTrainingRequestResult>> CompleteTrainingRequest([FromBody] LmsTrainingRequestCompleteInput input, CancellationToken cancellationToken)
		{
			if (await ActorAsync() == null || input == null)
				return NotFound();
			return await ActAsync(() => _requests.CompleteAsync(DepartmentId, input.TrainingRequestId, UserId, DateTime.SpecifyKind(input.CompletedOn, DateTimeKind.Utc),
				input.Hours, input.ActualCost, null, null, null, cancellationToken));
		}

		[HttpGet("GetMyCompetencyGaps")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsCompetencyGapsResult>> GetMyCompetencyGaps(int frameworkId)
		{
			if (await ActorAsync() == null || await _competencies.GetFrameworkAsync(DepartmentId, frameworkId) == null)
				return NotFound();
			var report = await _competencies.GetMemberGapsAsync(DepartmentId, frameworkId, UserId, DateTime.UtcNow);
			var result = new LmsCompetencyGapsResult
			{
				FrameworkId = frameworkId,
				FrameworkName = report.Framework.Name,
				Data = report.Rows.Select(r => new LmsCompetencyGapData
				{
					ItemId = r.LmsCompetencyItemId,
					ParentItemId = r.ParentItemId,
					Code = r.Code,
					Label = r.Label,
					State = r.State.ToString(),
					EvidenceOn = r.EvidenceOn,
					AgeDays = r.AgeDays,
					EvidenceTitle = r.EvidenceTitle
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}
	}
}
