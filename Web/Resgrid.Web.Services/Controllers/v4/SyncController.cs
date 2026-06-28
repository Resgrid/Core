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
		private readonly ISyncService _syncService;

		public SyncController(IIncidentCommandService incidentCommandService, IIncidentResourcesService incidentResourcesService, ISyncService syncService)
		{
			_incidentCommandService = incidentCommandService;
			_incidentResourcesService = incidentResourcesService;
			_syncService = syncService;
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

		/// <summary>
		/// Shift-start aggregate pull: a render-ready board (incl. computed accountability / PAR) for every ACTIVE
		/// incident in the caller's department, plus the active ad-hoc resources and a next-sync cursor — in a single
		/// round-trip. Persist <c>Data.ServerTimestampMs</c> and pass it as the next <see cref="Changes"/> `since`.
		/// Unlike <see cref="Changes"/> (a row-based delta), this returns the computed PAR/accountability state.
		/// </summary>
		[HttpGet("Bundle")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<SyncBundleResult>> Bundle(bool includeAccountability = true)
		{
			var bundle = await _incidentCommandService.GetBundleForDepartmentAsync(DepartmentId, includeAccountability);

			// Ad-hoc resources live in IncidentResourcesService; pull them for ALL active incidents in one batched call
			// (the previous per-board loop was an N+1 — each call scanned the department's ad-hoc tables).
			var adHoc = await _incidentResourcesService.GetActiveAdHocResourcesForDepartmentAsync(DepartmentId);
			bundle.AdHocUnits = adHoc.Units;
			bundle.AdHocPersonnel = adHoc.Personnel;

			var result = new SyncBundleResult { Data = bundle };
			result.PageSize = bundle.Boards.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>
		/// Shift-start REFERENCE pull: the slowly-changing department configuration + a safe personnel roster needed to
		/// start and run an incident offline (call types, command templates, units, personnel, groups, POIs, protocols,
		/// accountability config, statuses, feature flags). Pull once per shift / on manual refresh; the live incident
		/// state comes from <see cref="Bundle"/> and <see cref="Changes"/>.
		/// </summary>
		[HttpGet("Reference")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Command_View)]
		public async Task<ActionResult<SyncReferenceResult>> Reference(bool bypassCache = false)
		{
			var data = await _syncService.GetReferenceDataAsync(DepartmentId, bypassCache);

			var result = new SyncReferenceResult { Data = data };
			result.PageSize = data.Units.Count + data.Personnel.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
