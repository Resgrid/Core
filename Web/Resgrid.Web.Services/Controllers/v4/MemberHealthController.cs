using System;
using System.Collections.Generic;
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
	/// Member wellness WL1 (protocols-training-knowledge-plan §13b) for Omni: the signed-in member's duty status, injuries and exposure cases,
	/// reporting a bloodborne-pathogen or infectious-disease exposure, reporting an injury, and the duty board for supervisors and staffing
	/// (operational only: duty and clearance status, never why). The member endpoints act on the caller's own records only; there is no
	/// parameter naming another member. Officer work (case steps, duty status, clearances, the OSHA log) stays on the web.
	/// Workforce Pro (2026-11-26): 404 while the Workforce.Pro flag is off; while the add-on is not licensed (or the member holds no seat) the
	/// reads still answer and the reports are refused with 400 and the code workforce_pro_required / workforce_pro_seat_required.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class MemberHealthController : V4AuthenticatedApiControllerbase
	{
		private readonly IMemberHealthService _health;
		private readonly IWorkforceProAccessService _workforcePro;

		public MemberHealthController(IMemberHealthService health, IWorkforceProAccessService workforcePro)
		{
			_health = health;
			_workforcePro = workforcePro;
		}

		private static T Populate<T>(T result, int pageSize, string status = ResponseHelper.Success) where T : StandardApiResponseV4Base
		{
			result.PageSize = pageSize;
			result.Status = status;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Workforce Pro: every endpoint answers 404 while the Workforce.Pro flag is off (the service refuses as well; data is kept).</summary>
		private Task<bool> OfferedAsync() => _workforcePro.IsOfferedAsync(DepartmentId);

		/// <summary>The signed-in member's duty status, clearance status, exposure cases and injuries.</summary>
		[HttpGet("GetMyHealth")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<MyHealthResult>> GetMyHealth()
		{
			if (!await OfferedAsync())
				return NotFound();
			try
			{
				var result = new MyHealthResult();
				result.Data.DutyStatus = MapStatus(await _health.GetDutyStatusAsync(DepartmentId, UserId, UserId));
				result.Data.Cases = (await _health.GetCasesForMemberAsync(DepartmentId, UserId, UserId)).Select(MapCase).ToList();
				result.Data.Injuries = (await _health.GetInjuriesForMemberAsync(DepartmentId, UserId, UserId)).Select(MapInjury).ToList();
				return Ok(Populate(result, result.Data.Cases.Count + result.Data.Injuries.Count));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
		}

		/// <summary>The signed-in member's operational duty and clearance status.</summary>
		[HttpGet("GetMyDutyStatus")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<DutyStatusResult>> GetMyDutyStatus()
		{
			if (!await OfferedAsync())
				return NotFound();
			try
			{
				var result = new DutyStatusResult { Data = MapStatus(await _health.GetDutyStatusAsync(DepartmentId, UserId, UserId)) };
				return Ok(Populate(result, 1));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
		}

		/// <summary>The signed-in member's injuries (newest first), with their own details revealed for the caller's grant.</summary>
		[HttpGet("GetMyInjuries")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<MyInjuriesResult>> GetMyInjuries()
		{
			if (!await OfferedAsync())
				return NotFound();
			try
			{
				var result = new MyInjuriesResult { Data = (await _health.GetInjuriesForMemberAsync(DepartmentId, UserId, UserId)).Select(MapInjury).ToList() };
				return Ok(Populate(result, result.Data.Count));
			}
			catch (UnauthorizedAccessException)
			{
				return Unauthorized();
			}
		}

		/// <summary>
		/// Members on restricted or off duty, or not fully cleared, that the caller supervises (department admins and health officers:
		/// everyone; group admins: their group; anyone else: themselves). Operational only.
		/// </summary>
		[HttpGet("GetDutyBoard")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<DutyBoardResult>> GetDutyBoard()
		{
			if (!await OfferedAsync())
				return NotFound();
			var result = new DutyBoardResult { Data = (await _health.GetDutyBoardAsync(DepartmentId, UserId) ?? new List<MemberDutyStatus>()).Select(MapStatus).ToList() };
			return Ok(Populate(result, result.Data.Count));
		}

		/// <summary>Reports a bloodborne-pathogen or infectious-disease exposure for the signed-in member; the designated officers are told.</summary>
		[HttpPost("ReportExposure")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<ExposureCaseResult>> ReportExposure([FromBody] ReportExposureInput input, CancellationToken cancellationToken)
		{
			if (!await OfferedAsync())
				return NotFound();
			if (input == null)
				return BadRequest();
			var result = new ExposureCaseResult();
			try
			{
				var incident = await _health.ReportExposureAsync(DepartmentId, UserId, new ExposureIncidentReport
				{
					UserId = UserId,
					ResponderExposureId = input.ExposureId,
					Route = input.Route,
					OccurredOn = Utc(input.OccurredOn),
					CallId = input.CallId,
					Narrative = input.Narrative,
					PpeWorn = input.PpeWorn
				}, cancellationToken);
				result.Data = MapCase(incident);
				return Ok(Populate(result, 1, ResponseHelper.Created));
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

		/// <summary>Reports an injury for the signed-in member; the health officers are told and keep the duty status and OSHA facts.</summary>
		[HttpPost("ReportInjury")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status401Unauthorized)]
		public async Task<ActionResult<InjuryResult>> ReportInjury([FromBody] ReportInjuryInput input, CancellationToken cancellationToken)
		{
			if (!await OfferedAsync())
				return NotFound();
			if (input == null)
				return BadRequest();
			var result = new InjuryResult();
			try
			{
				var injury = await _health.ReportInjuryAsync(DepartmentId, UserId, new MemberInjuryInput
				{
					UserId = UserId,
					OccurredOn = Utc(input.OccurredOn),
					CallId = input.CallId,
					Location = input.Location,
					Description = input.Description,
					InjuryDetails = input.InjuryDetails
				}, cancellationToken);
				result.Data = MapInjury(await _health.GetInjuryAsync(DepartmentId, UserId, injury.RmsMemberInjuryId) ?? injury);
				return Ok(Populate(result, 1, ResponseHelper.Created));
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

		private static DateTime Utc(DateTime value) =>
			DateTime.SpecifyKind(value, value.Kind == DateTimeKind.Local ? DateTimeKind.Local : DateTimeKind.Utc).ToUniversalTime();

		[NonAction]
		public static DutyStatusData MapStatus(MemberDutyStatus status)
		{
			if (status == null)
				return null;
			return new DutyStatusData
			{
				UserId = status.UserId,
				DutyStatus = (int)status.DutyStatus,
				DutyStatusName = status.DutyStatus.ToString(),
				DutyStatusSince = status.DutyStatusSince,
				ExpectedReturnOn = status.ExpectedReturnOn,
				ClearanceStatus = status.ClearanceStatus.HasValue ? (int)status.ClearanceStatus.Value : (int?)null,
				ClearanceStatusName = status.ClearanceStatus?.ToString(),
				ClearanceValidUntil = status.ClearanceValidUntil
			};
		}

		[NonAction]
		public static ExposureCaseData MapCase(RmsExposureIncident incident) => new ExposureCaseData
		{
			CaseId = incident.RmsExposureIncidentId,
			ExposureId = incident.ResponderExposureId,
			CallId = incident.CallId,
			Route = incident.Route,
			Stage = incident.Stage,
			StageName = ExposureIncidentWorkflow.IsKnownStage(incident.Stage) ? ((ExposureIncidentStages)incident.Stage).ToString() : null,
			OccurredOn = DateTime.SpecifyKind(incident.OccurredOn, DateTimeKind.Utc),
			ReportedOn = DateTime.SpecifyKind(incident.ReportedOn, DateTimeKind.Utc),
			ClosedOn = incident.ClosedOn
		};

		[NonAction]
		public static InjuryData MapInjury(RmsMemberInjury injury)
		{
			string Safe(string value) => ProtectedDataEnvelope.HasEnvelopePrefix(value) ? ProtectedDataEnvelope.RedactionValue : value;
			var withheld = MemberHealthProtectedFields.Injuries.Values.Any(a => a.Get(injury) == ProtectedDataEnvelope.RedactionValue || ProtectedDataEnvelope.HasEnvelopePrefix(a.Get(injury)));
			return new InjuryData
			{
				InjuryId = injury.RmsMemberInjuryId,
				CaseNumber = OshaRules.CaseNumber(injury.OshaYear, injury.OshaCaseNumber),
				OccurredOn = DateTime.SpecifyKind(injury.OccurredOn, DateTimeKind.Utc),
				CallId = injury.CallId,
				State = injury.State,
				StateName = Enum.IsDefined(typeof(MemberInjuryStates), injury.State) ? ((MemberInjuryStates)injury.State).ToString() : null,
				DutyStatus = injury.DutyStatus,
				DutyStatusName = MemberHealthRules.IsKnownDutyStatus(injury.DutyStatus) ? ((MemberDutyStatuses)injury.DutyStatus).ToString() : null,
				DutyStatusSince = injury.DutyStatusSince,
				ExpectedReturnOn = injury.ExpectedReturnOn,
				ReturnedToDutyOn = injury.ReturnedToDutyOn,
				Location = injury.Location,
				Description = Safe(injury.Description),
				InjuryDetails = Safe(injury.InjuryDetails),
				Restrictions = Safe(injury.Restrictions),
				Withheld = withheld,
				RowVersion = injury.RowVersion
			};
		}
	}
}
