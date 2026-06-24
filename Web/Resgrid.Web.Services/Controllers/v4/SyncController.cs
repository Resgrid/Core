using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Sync;
using System;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Offline-first delta sync. A reconnecting client calls Changes with its last cursor to pull everything that
	/// changed while it was offline (created / updated / removed incident-command rows), reconciles its local store,
	/// then replays its outbox. See docs/architecture/offline-first-architecture.md.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class SyncController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IIncidentCommandService _incidentCommandService;
		private readonly IIncidentResourcesService _incidentResourcesService;

		public SyncController(IIncidentCommandService incidentCommandService, IIncidentResourcesService incidentResourcesService)
		{
			_incidentCommandService = incidentCommandService;
			_incidentResourcesService = incidentResourcesService;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Returns incident-command rows changed since <paramref name="since"/> (Unix epoch milliseconds; 0 or omitted
		/// = full pull), scoped to the caller's department. Soft-deleted / closed / released rows are included so the
		/// client can remove them locally. Persist the returned <c>Data.ServerTimestampMs</c> and pass it as the next
		/// <paramref name="since"/>.
		/// </summary>
		[HttpGet("Changes")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<SyncChangesResult>> Changes(long since = 0)
		{
			var sinceUtc = since <= 0
				? DateTime.MinValue
				: DateTimeOffset.FromUnixTimeMilliseconds(since).UtcDateTime;

			var changes = await _incidentCommandService.GetChangesSinceAsync(DepartmentId, sinceUtc);

			// Ad-hoc resources live in IncidentResourcesService; aggregate them into the unified delta payload.
			var adHoc = await _incidentResourcesService.GetAdHocChangesSinceAsync(DepartmentId, sinceUtc);
			changes.AdHocUnits = adHoc.Units;
			changes.AdHocPersonnel = adHoc.Personnel;

			var result = new SyncChangesResult { Data = changes };
			result.PageSize = changes.Commands.Count + changes.Nodes.Count + changes.Assignments.Count
				+ changes.Objectives.Count + changes.Timers.Count + changes.Annotations.Count
				+ changes.Roles.Count + changes.AdHocUnits.Count + changes.AdHocPersonnel.Count + changes.TimelineEntries.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
