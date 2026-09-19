using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Search;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The command palette behind the top search box (Unified Search plan R3 "Action" family + R4 Phase 2). System
	/// functionality always comes from the catalog, filtered by the caller's claims, module switches and feature flags;
	/// entity hits come from the unified endpoint when Search.Unified is on for the department, each re-checked against
	/// the entity's own authorization rule before it is returned.
	/// </summary>
	[Area("User")]
	public class SearchController : SecureBaseController
	{
		private readonly IUnifiedSearchService _unifiedSearch;
		private readonly ISystemActionsService _systemActions;

		public SearchController(IUnifiedSearchService unifiedSearch, ISystemActionsService systemActions)
		{
			_unifiedSearch = unifiedSearch;
			_systemActions = systemActions;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> GetSearchResults(string query, CancellationToken cancellationToken)
		{
			var principal = BuildPrincipal();
			var text = (query ?? string.Empty).Trim();
			var items = new List<SearchResultJson>();

			UnifiedSearchResult unified = null;
			try
			{
				unified = await _unifiedSearch.SearchAsync(new UnifiedSearchRequest
				{
					Text = text,
					Take = 10,
					Prefix = true,
					IncludeActions = true,
					IncludeRecords = false
				}, principal, cancellationToken);
			}
			catch (System.Exception ex)
			{
				// The query text stays out of the log: it can name protected people, addresses and record numbers.
				Logging.LogException(ex, $"Unified search failed for the command palette (department {DepartmentId}, user {UserId}, prefix query of {text.Length} chars); returning system actions only.");
			}

			List<SystemActionHit> actions;
			if (unified != null && unified.Available)
			{
				actions = unified.Actions;
			}
			else
			{
				// Flag off or search unavailable: the palette still finds system functionality.
				actions = text.Length == 0
					? await _systemActions.ListAsync(principal, cancellationToken)
					: await _systemActions.SearchAsync(text, principal, 8, cancellationToken);
			}

			items.AddRange((actions ?? new List<SystemActionHit>()).Select(a => new SearchResultJson
			{
				Label = a.Title,
				Summary = a.Description,
				Url = a.Url,
				Group = "Actions",
				Type = a.Category
			}));

			if (unified != null && unified.Available)
			{
				items.AddRange(unified.Hits.Select(h => new SearchResultJson
				{
					Label = h.Title,
					Summary = string.IsNullOrWhiteSpace(h.Summary) ? Badge(h) : h.Summary,
					Url = string.IsNullOrWhiteSpace(h.Url) ? null : (h.Url.StartsWith("http") ? h.Url : Config.SystemBehaviorConfig.ResgridBaseUrl + h.Url),
					Group = Plural(h.EntityType),
					Type = h.EntityType
				}));
			}

			return Content(JsonConvert.SerializeObject(items), "application/json");
		}

		private SearchPrincipal BuildPrincipal()
		{
			var user = HttpContext?.User;
			return new SearchPrincipal
			{
				UserId = UserId,
				DepartmentId = DepartmentId,
				IsDepartmentAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin(),
				HasClaim = (resource, action) => user != null && user.HasClaim(resource, action),
				IsModuleEnabled = module =>
				{
					switch (module)
					{
						case SystemActionModules.Messaging: return SettingsHelper.IsMessagingEnabled();
						case SystemActionModules.Mapping: return SettingsHelper.IsMappingEnabled();
						case SystemActionModules.Shifts: return SettingsHelper.IsShiftsEnabled();
						case SystemActionModules.Logs: return SettingsHelper.IsLogsEnabled();
						case SystemActionModules.Reports: return SettingsHelper.IsReportsEnabled();
						case SystemActionModules.Documents: return SettingsHelper.IsDocumentsEnabled();
						case SystemActionModules.Calendar: return SettingsHelper.IsCalendarEnabled();
						case SystemActionModules.Notes: return SettingsHelper.IsNotesEnabled();
						case SystemActionModules.Training: return SettingsHelper.IsTrainingEnabled();
						case SystemActionModules.Inventory: return SettingsHelper.IsInventoryEnabled();
						case SystemActionModules.Maintenance: return SettingsHelper.IsMaintenanceEnabled();
						case SystemActionModules.BusinessOperations: return SettingsHelper.IsBusinessOperationsEnabled();
						default: return true;
					}
				}
			};
		}

		private static string Badge(UnifiedSearchHit hit)
		{
			var parts = new List<string>();
			if (!string.IsNullOrWhiteSpace(hit.Category)) parts.Add(hit.Category);
			if (!string.IsNullOrWhiteSpace(hit.Status)) parts.Add(hit.Status);
			if (hit.OccurredOn.HasValue) parts.Add(hit.OccurredOn.Value.ToString("yyyy-MM-dd"));
			return string.Join(" · ", parts);
		}

		private static string Plural(string entityType)
		{
			switch (entityType)
			{
				case SearchEntityTypes.Call: return "Calls";
				case SearchEntityTypes.Unit: return "Units";
				case SearchEntityTypes.Personnel: return "Personnel";
				case SearchEntityTypes.Contact: return "Contacts";
				case SearchEntityTypes.Message: return "Messages";
				case SearchEntityTypes.Document: return "Documents";
				case SearchEntityTypes.Note: return "Notes";
				case SearchEntityTypes.Record: return "Records";
				default: return entityType;
			}
		}
	}
}
