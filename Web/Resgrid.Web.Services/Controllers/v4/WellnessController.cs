using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Wellness;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Member wellness WL0 (protocols-training-knowledge-plan §13b): the member's own exposure and critical-incident log for Omni - list
	/// my entries and suggestions, log an entry, confirm or dismiss a suggestion built after a flagged call. Every endpoint acts on the
	/// signed-in member's own entries; there is no parameter naming another member, so the restricted tier holds by construction (the
	/// officer views stay on the web). Nothing confirms itself: a suggestion counts only after this member confirms it.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class WellnessController : V4AuthenticatedApiControllerbase
	{
		private readonly IMemberWellnessService _wellness;

		public WellnessController(IMemberWellnessService wellness)
		{
			_wellness = wellness;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>The signed-in member's entries (newest first), suggestions awaiting their review, counts and screening follow-ups.</summary>
		[HttpGet("GetMyExposures")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<MyExposuresResult>> GetMyExposures()
		{
			MemberWellnessOverview overview;
			try
			{
				overview = await _wellness.GetOverviewAsync(DepartmentId, UserId, UserId);
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}

			var result = new MyExposuresResult();
			result.Data.Entries = overview.Entries.Select(Map).ToList();
			result.Data.Suggestions = overview.Suggestions.Select(Map).ToList();
			result.Data.ConfirmedByCategory = overview.ConfirmedByCategory;
			result.Data.CriticalIncidentCount = overview.CriticalIncidentCount;
			result.Data.ScreeningFlagged = overview.ScreeningFlag != null;
			result.Data.ScreeningReviewOn = overview.ScreeningFlag?.ReviewOn;
			result.Data.FollowUps = overview.FollowUps.Select(f => new ExposureFollowUpData
			{
				FollowUpId = f.RmsRecordWorkAssignmentId,
				Kind = f.Note,
				AssigneeUserId = f.AssigneeUserId,
				DueOn = f.DueOn,
				State = f.State
			}).ToList();
			result.Data.PeerTeamConfigured = (await _wellness.GetSettingsAsync(DepartmentId)).PeerTeamCertificationTypeId > 0;
			return Ok(Populate(result, result.Data.Entries.Count + result.Data.Suggestions.Count));
		}

		/// <summary>Logs an entry for the signed-in member. It is confirmed by the act of logging it.</summary>
		[HttpPost("CreateExposure")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<ExposureEntryResult>> CreateExposure([FromBody] CreateExposureInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();
			var model = ToInput(input);
			model.UserId = UserId;
			return await RunAsync(() => _wellness.RecordAsync(DepartmentId, UserId, model, cancellationToken), ResponseHelper.Created);
		}

		/// <summary>Confirms one of the signed-in member's suggestions, optionally with their details.</summary>
		[HttpPost("ConfirmSuggestion")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ExposureEntryResult>> ConfirmSuggestion([FromBody] ConfirmExposureSuggestionInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ExposureId))
				return BadRequest();
			if (!await IsOwnAsync(input.ExposureId))
				return NotFound();
			var details = input.Details == null ? null : ToInput(input.Details);
			return await RunAsync(() => _wellness.ConfirmAsync(DepartmentId, UserId, input.ExposureId, details, cancellationToken), ResponseHelper.Updated);
		}

		/// <summary>Dismisses one of the signed-in member's suggestions ("this doesn't apply to me").</summary>
		[HttpPost("DismissSuggestion")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<ExposureEntryResult>> DismissSuggestion([FromBody] DismissExposureSuggestionInput input, CancellationToken cancellationToken)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.ExposureId))
				return BadRequest();
			if (!await IsOwnAsync(input.ExposureId))
				return NotFound();
			return await RunAsync(() => _wellness.DismissAsync(DepartmentId, UserId, input.ExposureId, cancellationToken), ResponseHelper.Updated);
		}

		/// <summary>The API acts on the caller's own entries only, even for a wellness officer (whose views are on the web).</summary>
		private async Task<bool> IsOwnAsync(string exposureId)
		{
			var entry = await _wellness.GetEntryAsync(DepartmentId, UserId, exposureId);
			return entry != null && string.Equals(entry.UserId, UserId, StringComparison.OrdinalIgnoreCase);
		}

		private async Task<ActionResult<ExposureEntryResult>> RunAsync(Func<Task<RmsResponderExposure>> act, string status)
		{
			var result = new ExposureEntryResult();
			try
			{
				var saved = await act();
				var revealed = await _wellness.GetEntryAsync(DepartmentId, UserId, saved.RmsResponderExposureId) ?? saved;
				result.Data = Map(revealed);
				return Ok(Populate(result, 1, status));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
			catch (MemberWellnessException ex)
			{
				result.Error = ex.Code;
				return BadRequest(Populate(result, 0, ResponseHelper.Failure));
			}
		}

		private static ResponderExposureInput ToInput(CreateExposureInput input) => new ResponderExposureInput
		{
			Category = input.Category,
			OccurredOn = DateTime.SpecifyKind(input.OccurredOn, input.OccurredOn.Kind == DateTimeKind.Local ? DateTimeKind.Local : DateTimeKind.Utc).ToUniversalTime(),
			DurationMinutes = input.DurationMinutes,
			CallId = input.CallId,
			Substance = input.Substance,
			Narrative = input.Narrative,
			PpeWorn = input.PpeWorn,
			Decontamination = input.Decontamination,
			HealthDetails = input.HealthDetails,
			RemindMember = input.RemindMember,
			RemindPeerTeam = input.RemindPeerTeam
		};

		[NonAction]
		public static ExposureEntryData Map(RmsResponderExposure entry)
		{
			var withheld = WellnessProtectedFields.Exposures.Values.Any(a => a.Get(entry) == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(entry)));
			string Safe(string value) => ProtectedDataEnvelope.HasEnvelopePrefix(value) ? ProtectedDataEnvelope.RedactionValue : value;
			return new ExposureEntryData
			{
				ExposureId = entry.RmsResponderExposureId,
				Category = entry.Category,
				CategoryName = Enum.IsDefined(typeof(ResponderExposureCategories), entry.Category) ? ((ResponderExposureCategories)entry.Category).ToString() : null,
				Status = entry.Status,
				StatusName = Enum.IsDefined(typeof(ResponderExposureStatuses), entry.Status) ? ((ResponderExposureStatuses)entry.Status).ToString() : null,
				Source = entry.Source,
				OccurredOn = DateTime.SpecifyKind(entry.OccurredOn, DateTimeKind.Utc),
				DurationMinutes = entry.DurationMinutes,
				CallId = entry.CallId,
				SuggestionBasis = string.IsNullOrWhiteSpace(entry.SuggestionBasis) ? new System.Collections.Generic.List<string>() : entry.SuggestionBasis.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
				Substance = Safe(entry.Substance),
				Narrative = Safe(entry.Narrative),
				PpeWorn = Safe(entry.PpeWorn),
				Decontamination = Safe(entry.Decontamination),
				HealthDetails = Safe(entry.HealthDetails),
				RemindMember = entry.RemindMember,
				RemindPeerTeam = entry.RemindPeerTeam,
				NextAnniversaryOn = entry.NextAnniversaryOn,
				Withheld = withheld,
				RowVersion = entry.RowVersion
			};
		}
	}
}
