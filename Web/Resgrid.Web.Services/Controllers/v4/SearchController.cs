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
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Search;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Unified Search (plan R4 Phase 2/3): one endpoint over the global index (calls, units, personnel, contacts,
	/// messages, documents, notes), the RMS records index, and the system-functionality catalog. The department and
	/// the caller's identity come from the token, never from the request. Each hit is re-checked against the entity's
	/// own authorization rule; totals are null when any hit was dropped. Typeahead is the same query in prefix mode.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class SearchController : V4AuthenticatedApiControllerbase
	{
		private const int MaxTake = 100;

		private readonly IUnifiedSearchService _unifiedSearch;
		private readonly IGlobalSearchService _globalSearch;
		private readonly IRecordsSearchService _recordsSearch;
		private readonly ISearchIndexMaintenanceService _maintenance;
		private readonly ISearchIndexStatesRepository _states;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IFeatureToggleService _featureToggles;
		private readonly IRecordsAuthorizationService _authorization;

		public SearchController(IUnifiedSearchService unifiedSearch, IGlobalSearchService globalSearch, IRecordsSearchService recordsSearch,
			ISearchIndexMaintenanceService maintenance, ISearchIndexStatesRepository states, IDepartmentSettingsService departmentSettings,
			IFeatureToggleService featureToggles, IRecordsAuthorizationService authorization)
		{
			_unifiedSearch = unifiedSearch;
			_globalSearch = globalSearch;
			_recordsSearch = recordsSearch;
			_maintenance = maintenance;
			_states = states;
			_departmentSettings = departmentSettings;
			_featureToggles = featureToggles;
			_authorization = authorization;
		}

		/// <summary>
		/// Full search. <paramref name="types"/> is a comma-separated list of entity types (Call, Unit, Personnel,
		/// Contact, Message, Document, Note, Record, Action); omit for all.
		/// </summary>
		[HttpGet("Search")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SearchResult>> Search(string query, string types = null, int skip = 0, int take = 20, CancellationToken cancellationToken = default)
		{
			if (!await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, DepartmentId))
				return NotFound();
			if (string.IsNullOrWhiteSpace(query))
				return BadRequest();

			var requestedTypes = ParseTypes(types);
			// The page metadata must describe the query that ran, so the clamped values feed both.
			var effectiveSkip = Math.Max(0, skip);
			var effectiveTake = Math.Max(1, Math.Min(MaxTake, take));
			var unified = await _unifiedSearch.SearchAsync(new UnifiedSearchRequest
			{
				Text = query,
				EntityTypes = requestedTypes,
				Skip = effectiveSkip,
				Take = effectiveTake,
				IncludeActions = requestedTypes == null || requestedTypes.Any(t => string.Equals(t, SearchEntityTypes.Action, StringComparison.OrdinalIgnoreCase)),
				IncludeRecords = requestedTypes == null || requestedTypes.Any(t => string.Equals(t, SearchEntityTypes.Record, StringComparison.OrdinalIgnoreCase)),
				Prefix = false
			}, await BuildPrincipalAsync(), cancellationToken);

			return Ok(Map(unified, effectiveSkip, effectiveTake));
		}

		/// <summary>Typeahead: prefix search on titles and identifiers, no records federation, small result list.</summary>
		[HttpGet("Typeahead")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<ActionResult<SearchResult>> Typeahead(string query, string types = null, int take = 8, CancellationToken cancellationToken = default)
		{
			if (!await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, DepartmentId))
				return NotFound();

			var requestedTypes = ParseTypes(types);
			var effectiveTake = Math.Max(1, Math.Min(25, take));
			var unified = await _unifiedSearch.SearchAsync(new UnifiedSearchRequest
			{
				Text = query ?? string.Empty,
				EntityTypes = requestedTypes,
				Skip = 0,
				Take = effectiveTake,
				IncludeActions = requestedTypes == null || requestedTypes.Any(t => string.Equals(t, SearchEntityTypes.Action, StringComparison.OrdinalIgnoreCase)),
				IncludeRecords = false,
				Prefix = true
			}, await BuildPrincipalAsync(), cancellationToken);

			return Ok(Map(unified, 0, effectiveTake));
		}

		/// <summary>Department admins: flag the department's global index for a full projection + index rebuild on the next sweep (plan R4 Phase 3).</summary>
		[HttpPost("Rebuild")]
		[ProducesResponseType(StatusCodes.Status202Accepted)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SearchRebuildResult>> Rebuild(CancellationToken cancellationToken)
		{
			if (!await _featureToggles.IsEnabledAsync(FeatureFlagKeys.SearchUnified, DepartmentId))
				return NotFound();
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _authorization.IsDepartmentAdminAsync(UserId, DepartmentId))
				return Forbid();

			var state = await _maintenance.RequestRebuildAsync(DepartmentId, cancellationToken);
			Logging.LogInfo($"Search index rebuild requested for department {DepartmentId} by {UserId}.");

			var result = new SearchRebuildResult
			{
				Data = new SearchRebuildData
				{
					DepartmentId = DepartmentId,
					State = ((SearchIndexBuildState)state.State).ToString(),
					RequestedOn = state.RebuildRequestedOn
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Accepted(result);
		}

		/// <summary>Department admins: host and index health plus this department's index state.</summary>
		[HttpGet("Health")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status403Forbidden)]
		[Authorize(Policy = ResgridResources.Department_Update)]
		public async Task<ActionResult<SearchHealthResult>> Health()
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _authorization.IsDepartmentAdminAsync(UserId, DepartmentId))
				return Forbid();

			var global = await _globalSearch.GetHealthAsync();
			var records = await _recordsSearch.GetHealthAsync();
			var state = await _states.GetAsync(SearchIndexNames.Global, DepartmentId);
			if (state != null && state.DepartmentId != DepartmentId)
				state = null;

			var result = new SearchHealthResult
			{
				Data = new SearchHealthData
				{
					Enabled = global.Enabled,
					GlobalOnline = global.Online,
					// Shared-index totals and revisions describe other departments as well; never expose them here.
					GlobalDocumentCount = null,
					RecordsOnline = records.Online,
					RecordsDocumentCount = null,
					StoreEnabled = global.StoreEnabled,
					LastSyncedRevision = null,
					LastSyncedOnUtc = null,
					DepartmentIndexState = state == null ? "None" : ((SearchIndexBuildState)state.State).ToString(),
					DepartmentDocumentCount = state?.DocumentCount ?? 0,
					DepartmentLastRebuiltOn = state?.LastRebuiltOn
				},
				PageSize = 1,
				Status = ResponseHelper.Success
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		private static List<string> ParseTypes(string types)
		{
			if (string.IsNullOrWhiteSpace(types))
				return null;
			var list = types.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return list.Count == 0 ? null : list;
		}

		private async Task<SearchPrincipal> BuildPrincipalAsync()
		{
			var user = HttpContext?.User;
			DepartmentModuleSettings modules = null;
			try { modules = await _departmentSettings.GetDepartmentModuleSettingsAsync(DepartmentId, true); }
			catch (Exception ex) { Logging.LogException(ex); }

			return new SearchPrincipal
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				HasClaim = (resource, action) => user != null && user.HasClaim(resource, action),
				IsModuleEnabled = module =>
				{
					if (modules == null) return false;
					switch (module)
					{
						case SystemActionModules.Messaging: return !modules.MessagingDisabled;
						case SystemActionModules.Mapping: return !modules.MappingDisabled;
						case SystemActionModules.Shifts: return !modules.ShiftsDisabled;
						case SystemActionModules.Logs: return !modules.LogsDisabled;
						case SystemActionModules.Reports: return !modules.ReportsDisabled;
						case SystemActionModules.Documents: return !modules.DocumentsDisabled;
						case SystemActionModules.Calendar: return !modules.CalendarDisabled;
						case SystemActionModules.Notes: return !modules.NotesDisabled;
						case SystemActionModules.Training: return !modules.TrainingDisabled;
						case SystemActionModules.Inventory: return !modules.InventoryDisabled;
						case SystemActionModules.Maintenance: return !modules.MaintenanceDisabled;
						case SystemActionModules.BusinessOperations: return !modules.BusinessOperationsDisabled;
						default: return true;
					}
				}
			};
		}

		private static SearchResult Map(UnifiedSearchResult unified, int skip, int take)
		{
			var result = new SearchResult
			{
				Data = new SearchResultData
				{
					Available = unified.Available,
					Degraded = unified.Degraded,
					DegradedReason = unified.DegradedReason,
					TotalCount = unified.Total,
					Truncated = unified.Truncated,
					QueryTimeMs = unified.QueryTimeMs,
					Results = unified.Hits.Select(h => new SearchHitData
					{
						EntityType = h.EntityType,
						EntityId = h.EntityId,
						Title = h.Title,
						Summary = h.Summary,
						Url = h.Url,
						Score = h.Score,
						OccurredOn = h.OccurredOn,
						Category = h.Category,
						Status = h.Status,
						Metadata = h.Metadata == null ? new Dictionary<string, string>() : new Dictionary<string, string>(h.Metadata)
					}).ToList(),
					Actions = unified.Actions.Select(a => new SearchActionData
					{
						Key = a.Key,
						Title = a.Title,
						Description = a.Description,
						Category = a.Category,
						Url = a.Url,
						Score = a.Score
					}).ToList()
				},
				Page = take > 0 ? skip / take : 0,
				PageSize = unified.Hits.Count,
				Status = unified.Available ? ResponseHelper.Success : ResponseHelper.NotFound
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}
	}
}
