using System;
using System.Globalization;
using System.Linq;
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
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// The member's learning requirements and learning paths (Training T3, LMS) for Omni's My Training: each requirement with its state and
	/// due date, and each path with its steps (locked, available, done) and the enrollment to play. Read-only; the older apps are not
	/// changed. Every endpoint is refused while the Trainings module is off; department requirements and paths follow the LMS gate
	/// (dark while the Training.Lms flag is off), Academy role paths run for every department.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class LmsRequirementsController : V4AuthenticatedApiControllerbase
	{
		private readonly ILmsRequirementService _requirements;
		private readonly ILmsPathService _paths;
		private readonly ILmsGateService _gate;
		private readonly ILmsAccessService _access;
		private readonly ILmsRecommendationService _recommendations;
		private readonly IAuthorizationService _authorization;

		public LmsRequirementsController(ILmsRequirementService requirements, ILmsPathService paths, ILmsGateService gate, ILmsAccessService access,
			ILmsRecommendationService recommendations, IAuthorizationService authorization)
		{
			_requirements = requirements;
			_paths = paths;
			_gate = gate;
			_access = access;
			_recommendations = recommendations;
			_authorization = authorization;
		}

		private static T Populate<T>(T result, int pageSize) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The member's learning requirements, the next action first (overdue, at risk, due later, then the rest).</summary>
		[HttpGet("GetMyRequirements")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsMyRequirementsResult>> GetMyRequirements()
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return NotFound();

			var mine = await _requirements.GetMyRequirementsAsync(DepartmentId, UserId, DateTime.UtcNow);
			var result = new LmsMyRequirementsResult
			{
				Data = mine.Select(m => new LmsMyRequirementData
				{
					RequirementId = m.Requirement.LmsRequirementId,
					Name = m.Requirement.Name,
					Description = m.Requirement.Description,
					Kind = ((LmsRequirementKinds)m.Requirement.Kind).ToString(),
					TargetName = m.TargetName,
					State = m.Status.State.ToString(),
					DueOn = m.Status.ShownDueUtc,
					LastCompletedOn = m.Status.LastCompletedUtc,
					Progress = m.Status.Progress,
					Required = m.Status.Required,
					CourseId = m.CourseId,
					PathId = m.PathId,
					TrainingId = m.TrainingId
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>The member's learning paths with their steps; steps that unlocked since are enrolled as the list is read.</summary>
		[HttpGet("GetMyPaths")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsMyPathsResult>> GetMyPaths(CancellationToken cancellationToken)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return NotFound();

			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			var paths = await _paths.GetMyPathsAsync(actor, DateTime.UtcNow, cancellationToken);
			var result = new LmsMyPathsResult
			{
				Data = paths.Select(p => new LmsMyPathData
				{
					PathId = p.Path.LmsPathId,
					Title = p.Title,
					IsAcademy = p.Path.IsSystem,
					AssignedOn = p.Assignment.AssignedOn,
					DueOn = p.Assignment.DueOn,
					CompletedOn = p.Assignment.CompletedOn ?? p.Progress.CompletedOn,
					Done = p.Progress.Done,
					Needed = p.Progress.Needed,
					Steps = p.Progress.Steps.Select(s => new LmsMyPathStepData
					{
						Order = s.Step.SortOrder,
						Kind = ((LmsPathStepKinds)s.Step.Kind).ToString(),
						TargetId = s.Step.TargetRefId,
						Title = p.StepTitles.TryGetValue(s.Step.SortOrder, out var title) ? title : null,
						State = s.State.ToString(),
						CompletedOn = s.CompletedOn,
						Prerequisites = s.Step.Prerequisites().ToList(),
						ElectiveGroup = s.Step.ElectiveGroup,
						EnrollmentId = p.StepEnrollmentIds.TryGetValue(s.Step.SortOrder, out var enrollmentId) ? enrollmentId : null
					}).ToList()
				}).ToList()
			};
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>
		/// K3: learning paths and courses recommended to a member (the caller by default; officers may name a member in their scope), ranked
		/// deterministically from open requirements, paths in progress, competency gaps and the courses offered to the member's roles, each with
		/// its reasons. Read-only. 404 unless the department has the LMS add-on; 403 outside the caller's scope.
		/// </summary>
		[HttpGet("GetRecommendations")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsRecommendationsResult>> GetRecommendations(string userId = null, CancellationToken cancellationToken = default)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return NotFound();

			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			var scope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId);
			var set = await _recommendations.GetForMemberAsync(actor, scope, userId, DateTime.UtcNow, cancellationToken);
			if (set.Error == LmsErrors.AddonRequired)
				return NotFound();
			if (set.Error != LmsErrors.None)
				return Forbid();

			var result = new LmsRecommendationsResult();
			result.Data.UserId = set.UserId;
			result.Data.CanExplain = set.CanExplain;
			result.Data.Items = set.Items.Select(r => new LmsRecommendationData
			{
				Key = LmsRecommendationExplanation.Key(r.TargetKind, r.TargetId),
				TargetKind = r.TargetKind.ToString(),
				TargetId = r.TargetId,
				Title = r.Title,
				Score = r.Score,
				DueOn = r.DueUtc,
				IsAcademy = r.IsSystem,
				EnrollmentId = string.Equals(set.UserId, UserId, StringComparison.OrdinalIgnoreCase) ? r.EnrollmentId : null,
				Reasons = r.Reasons.Select(s => new LmsRecommendationReasonData { Reason = s.Reason.ToString(), Detail = s.Detail, DueOn = s.DueUtc }).ToList()
			}).ToList();
			return Ok(Populate(result, result.Data.Items.Count));
		}

		/// <summary>
		/// K3: an optional AI explanation of a member's recommendations, one sentence per item written only from its reasons (Enhanced AI with
		/// knowledge answers; nothing is saved). 404 unless the department has both the LMS add-on and Enhanced AI knowledge answers.
		/// </summary>
		[HttpPost("ExplainRecommendations")]
		[Authorize(Policy = ResgridResources.Training_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<LmsRecommendationExplainResult>> ExplainRecommendations([FromBody] LmsRecommendationExplainInput input, CancellationToken cancellationToken)
		{
			if (!await _access.IsTrainingModuleOnAsync(DepartmentId))
				return NotFound();

			var actor = await _gate.GetActorAsync(DepartmentId, UserId);
			var scope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId);
			var explanation = await _recommendations.ExplainAsync(actor, scope, input?.UserId, CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, DateTime.UtcNow, cancellationToken);
			if (explanation == null || explanation.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var result = new LmsRecommendationExplainResult();
			result.Data.Outcome = explanation.Outcome;
			result.Data.Texts = explanation.Texts;
			result.Data.ExchangeId = explanation.ExchangeId;
			return Ok(Populate(result, explanation.Texts.Count));
		}
	}
}
