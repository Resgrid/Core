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
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.UnitStatusAlerts;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Acknowledging, muting and annotating the alerts raised when a unit sits in a status longer than the
	/// department's Unit Status Timers allow. Reading is open to anyone who can see units. Acknowledging and
	/// clearing require the Create Call permission, the same people who dispatch the units.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UnitStatusAlertsController : V4AuthenticatedApiControllerbase
	{
		private readonly IUnitStatusAlertsService _unitStatusAlertsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly Model.Services.IAuthorizationService _authorizationService;

		public UnitStatusAlertsController(IUnitStatusAlertsService unitStatusAlertsService, IDepartmentsService departmentsService,
			Model.Services.IAuthorizationService authorizationService)
		{
			_unitStatusAlertsService = unitStatusAlertsService;
			_departmentsService = departmentsService;
			_authorizationService = authorizationService;
		}

		/// <summary>
		/// Gets the acknowledgements that cover each unit's current status episode. Acknowledgements for units
		/// the caller cannot see are left out.
		/// </summary>
		[HttpGet("GetActiveAcknowledgements")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		public async Task<ActionResult<GetUnitStatusAlertAcknowledgementsResult>> GetActiveAcknowledgements()
		{
			var result = new GetUnitStatusAlertAcknowledgementsResult();
			var acknowledgements = await _unitStatusAlertsService.GetCurrentAcknowledgementsForDepartmentAsync(DepartmentId);

			if (acknowledgements.Any())
			{
				var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
				var visibility = new Dictionary<int, bool>();

				foreach (var acknowledgement in acknowledgements)
				{
					if (!visibility.TryGetValue(acknowledgement.UnitId, out var canView))
					{
						canView = await _authorizationService.CanUserViewUnitViaMatrixAsync(acknowledgement.UnitId, UserId, DepartmentId);
						visibility[acknowledgement.UnitId] = canView;
					}

					if (canView)
						result.Data.Add(ConvertAcknowledgement(acknowledgement, names));
				}
			}

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);

			return Ok(result);
		}

		/// <summary>
		/// Acknowledges or mutes the status timer alert for a unit's current status, optionally with a note.
		/// Replaces any earlier acknowledgement of the same status.
		/// </summary>
		[HttpPost("Acknowledge")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<SaveUnitStatusAlertAcknowledgementResult>> Acknowledge([FromBody] AcknowledgeUnitStatusAlertInput input, CancellationToken cancellationToken)
		{
			if (input == null)
				return BadRequest();

			if (!await _authorizationService.CanUserViewUnitViaMatrixAsync(input.UnitId, UserId, DepartmentId))
				return await ToActionResultAsync(UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NotFound));

			var outcome = await _unitStatusAlertsService.AcknowledgeAsync(DepartmentId, input.UnitId, input.UnitStateId, (UnitStatusAlertLevels)input.Level,
				(UnitStatusAlertAcknowledgementModes)input.Mode, input.MuteMinutes, input.Note, UserId, cancellationToken);

			return await ToActionResultAsync(outcome);
		}

		/// <summary>
		/// Withdraws an acknowledgement, so the alert shows as unacknowledged again. Clearing one that is already
		/// cleared succeeds.
		/// </summary>
		[HttpDelete("Clear/{id}")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Unit_View)]
		[Authorize(Policy = ResgridResources.Call_Create)]
		public async Task<ActionResult<SaveUnitStatusAlertAcknowledgementResult>> Clear(string id, CancellationToken cancellationToken)
		{
			var existing = await _unitStatusAlertsService.GetAcknowledgementByIdAsync(DepartmentId, id);

			if (existing == null || !await _authorizationService.CanUserViewUnitViaMatrixAsync(existing.UnitId, UserId, DepartmentId))
				return await ToActionResultAsync(UnitStatusAlertAcknowledgementResult.Fail(UnitStatusAlertAcknowledgementResult.NotFound));

			var outcome = await _unitStatusAlertsService.ClearAsync(DepartmentId, id, UserId, cancellationToken);

			return await ToActionResultAsync(outcome);
		}

		private async Task<ActionResult<SaveUnitStatusAlertAcknowledgementResult>> ToActionResultAsync(UnitStatusAlertAcknowledgementResult outcome)
		{
			var result = new SaveUnitStatusAlertAcknowledgementResult();

			if (outcome.Acknowledgement != null)
			{
				var names = await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId);
				result.Data = ConvertAcknowledgement(outcome.Acknowledgement, names);
			}

			result.Error = outcome.Error;
			result.PageSize = result.Data == null ? 0 : 1;
			result.Status = outcome.Success ? ResponseHelper.Success : ResponseHelper.Failure;
			ResponseHelper.PopulateV4ResponseData(result);

			if (outcome.Success)
				return Ok(result);

			switch (outcome.Error)
			{
				case UnitStatusAlertAcknowledgementResult.NotFound:
					return NotFound(result);
				case UnitStatusAlertAcknowledgementResult.StatusChanged:
				case UnitStatusAlertAcknowledgementResult.NotOverdue:
				case UnitStatusAlertAcknowledgementResult.Conflict:
					return Conflict(result);
				default:
					return BadRequest(result);
			}
		}

		private static UnitStatusAlertAcknowledgementResultData ConvertAcknowledgement(UnitStatusAlertAcknowledgement acknowledgement, List<PersonName> names)
		{
			var name = names?.FirstOrDefault(x => string.Equals(x.UserId, acknowledgement.AcknowledgedByUserId, StringComparison.OrdinalIgnoreCase));

			return new UnitStatusAlertAcknowledgementResultData
			{
				UnitStatusAlertAcknowledgementId = acknowledgement.UnitStatusAlertAcknowledgementId,
				UnitId = acknowledgement.UnitId.ToString(),
				UnitStateId = acknowledgement.UnitStateId,
				Level = acknowledgement.Level,
				Mode = acknowledgement.Mode,
				MutedUntilUtc = acknowledgement.MutedUntil,
				Note = acknowledgement.Note,
				AcknowledgedByUserId = acknowledgement.AcknowledgedByUserId,
				AcknowledgedByName = name == null ? null : $"{name.FirstName} {name.LastName}".Trim(),
				AcknowledgedOnUtc = acknowledgement.AcknowledgedOn,
				ClearedOnUtc = acknowledgement.ClearedOn
			};
		}
	}
}
