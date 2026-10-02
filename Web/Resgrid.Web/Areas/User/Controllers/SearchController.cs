using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Records;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Search;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The command palette behind the top search box (Unified Search plan R3 "Action" family + R4 Phase 2) and the full
	/// search page. System functionality always comes from the catalog, filtered by the caller's claims, module switches
	/// and feature flags; entity hits come from the unified endpoint when Search.Unified is on for the department, each
	/// re-checked against the entity's own authorization rule before it is returned. The page narrows by family and date
	/// and exports every authorized match, so a question such as "every call whose notes mention this alarm point" ends in
	/// a complete list.
	/// </summary>
	[Area("User")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class SearchController : SecureBaseController
	{
		private const int PageSize = 25;

		/// <summary>Display order of the family filter; anything else the service reports follows in its own order.</summary>
		private static readonly string[] FamilyOrder =
		{
			SearchEntityTypes.Call, SearchEntityTypes.Record, SearchEntityTypes.Log, SearchEntityTypes.Personnel, SearchEntityTypes.Unit,
			SearchEntityTypes.Contact, SearchEntityTypes.Occupancy, SearchEntityTypes.Poi, SearchEntityTypes.Group, SearchEntityTypes.Message, SearchEntityTypes.Note,
			SearchEntityTypes.Document, SearchEntityTypes.Protocol, SearchEntityTypes.Training, SearchEntityTypes.CalendarEvent, SearchEntityTypes.Shift,
			SearchEntityTypes.Deployment, SearchEntityTypes.Invoice, SearchEntityTypes.Bid, SearchEntityTypes.ServiceContract, SearchEntityTypes.RateCard,
			SearchEntityTypes.CertificationType
		};

		private static readonly string[] ExportColumns = { "Type", "Title", "Number", "Date", "Status", "Category", "Summary", "Match", "Link" };

		private readonly IUnifiedSearchService _unifiedSearch;
		private readonly ISystemActionsService _systemActions;
		private readonly IDepartmentsService _departmentsService;

		public SearchController(IUnifiedSearchService unifiedSearch, ISystemActionsService systemActions, IDepartmentsService departmentsService)
		{
			_unifiedSearch = unifiedSearch;
			_systemActions = systemActions;
			_departmentsService = departmentsService;
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> Index(string q, string type, string from, string to, string sort, int page = 1, CancellationToken cancellationToken = default)
		{
			var principal = BuildPrincipal();
			var model = new SearchIndexView
			{
				Query = (q ?? string.Empty).Trim(),
				From = from,
				To = to,
				Sort = SearchSortOrders.Normalize(sort),
				Page = Math.Max(1, page),
				PageSize = PageSize,
				MaxExportRows = Math.Max(1, SearchConfig.MaxPageWindow)
			};

			model.Families = Ordered(await _unifiedSearch.GetSearchableEntityTypesAsync(principal, cancellationToken));
			if (model.Families.Count == 0)
			{
				model.Unavailable = true;
				return View(model);
			}
			model.Type = model.Families.FirstOrDefault(f => string.Equals(f, type, StringComparison.OrdinalIgnoreCase));

			if (model.Query.Length == 0)
				return View(model);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var (fromUtc, toUtc, invalid) = Range(from, to, department);
			model.InvalidDate = invalid;
			model.Searched = true;
			model.HighlightTerms = SearchSnippets.Terms(model.Query);

			UnifiedSearchResult result;
			try
			{
				// One extra hit tells whether a next page exists when the total cannot be proven.
				result = await _unifiedSearch.SearchAsync(BuildRequest(model.Query, model.Type, fromUtc, toUtc, model.Sort,
					(model.Page - 1) * PageSize, PageSize + 1), principal, cancellationToken);
			}
			catch (Exception ex)
			{
				// The query text stays out of the log: it can name protected people, addresses and record numbers.
				Logging.LogException(ex, $"Search page query failed (department {DepartmentId}, user {UserId}, query of {model.Query.Length} chars).");
				model.Degraded = true;
				return View(model);
			}

			if (!result.Available)
			{
				model.Unavailable = true;
				return View(model);
			}

			model.Degraded = result.Degraded;
			model.IndexBuilding = result.IndexBuilding;
			model.Total = result.Total;
			model.HasMore = result.Hits.Count > PageSize;
			model.Results = result.Hits.Take(PageSize).Select(h => Row(h, department)).ToList();
			return View(model);
		}

		/// <summary>Every authorized match of the query (up to SearchConfig.MaxPageWindow) as CSV.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Department_View)]
		public async Task<IActionResult> Export(string q, string type, string from, string to, string sort, CancellationToken cancellationToken = default)
		{
			var text = (q ?? string.Empty).Trim();
			if (text.Length == 0)
				return RedirectToAction("Index");

			var principal = BuildPrincipal();
			var families = await _unifiedSearch.GetSearchableEntityTypesAsync(principal, cancellationToken);
			if (families.Count == 0)
				return Unauthorized();
			var family = families.FirstOrDefault(f => string.Equals(f, type, StringComparison.OrdinalIgnoreCase));

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId);
			var (fromUtc, toUtc, _) = Range(from, to, department);
			var max = Math.Max(1, SearchConfig.MaxPageWindow);
			var result = await _unifiedSearch.SearchAsync(BuildRequest(text, family, fromUtc, toUtc, SearchSortOrders.Normalize(sort), 0, max, max),
				principal, cancellationToken);
			if (!result.Available)
				return Unauthorized();

			var csv = new StringBuilder();
			csv.Append(string.Join(",", ExportColumns)).Append("\r\n");
			foreach (var row in result.Hits.Select(h => Row(h, department)))
			{
				csv.Append(string.Join(",", new[]
				{
					row.EntityType, row.Title, row.Number, row.OccurredOn, row.Status, row.Category, row.Summary, row.Snippet, row.Url
				}.Select(RecordsListExport.Escape))).Append("\r\n");
			}

			var name = $"search-{(family ?? "all").ToLowerInvariant()}-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv";
			return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv", name);
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
				// Full-text mode: every word must match, the last one may be partial, and the summary and full text (call
				// notes included) count — prefix mode only looked at titles and identifiers.
				unified = await _unifiedSearch.SearchAsync(new UnifiedSearchRequest
				{
					Text = text,
					Take = 10,
					Prefix = false,
					IncludeActions = true,
					IncludeRecords = false,
					CountTotal = false
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
					Summary = !string.IsNullOrWhiteSpace(h.Snippet) ? h.Snippet : string.IsNullOrWhiteSpace(h.Summary) ? Badge(h) : h.Summary,
					Url = AbsoluteUrl(h.Url),
					Group = Plural(h.EntityType),
					Type = h.EntityType
				}));
			}

			return Content(JsonConvert.SerializeObject(items), "application/json");
		}

		private static UnifiedSearchRequest BuildRequest(string text, string family, DateTime? fromUtc, DateTime? toUtc, string sort, int skip, int take, int maxCandidates = 0)
		{
			// A single family or a date range is a narrowed question: authorize the deep window so the list and its count are
			// complete. Every family at once stays on the default window.
			var narrowed = family != null || fromUtc.HasValue || toUtc.HasValue;
			return new UnifiedSearchRequest
			{
				Text = text,
				EntityTypes = family == null ? null : new List<string> { family },
				Skip = skip,
				Take = take,
				Prefix = false,
				IncludeActions = false,
				IncludeRecords = family == null || family == SearchEntityTypes.Record,
				FromUtc = fromUtc,
				ToUtc = toUtc,
				Sort = sort,
				MaxCandidates = maxCandidates > 0 ? maxCandidates : narrowed ? SearchConfig.MaxPageWindow : 0
			};
		}

		/// <summary>Department-local From/To dates (yyyy-MM-dd) to a UTC range: From at local midnight, To through the end of its local day.</summary>
		private static (DateTime? fromUtc, DateTime? toUtc, bool invalid) Range(string from, string to, Department department)
		{
			var invalid = false;
			DateTime? Date(string value)
			{
				if (string.IsNullOrWhiteSpace(value))
					return null;
				if (DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
					return date.Date;
				invalid = true;
				return null;
			}
			DateTime Utc(DateTime local)
			{
				try { return DateTimeHelpers.ConvertToUtc(local, department?.TimeZone, true); }
				catch (Exception) { return DateTime.SpecifyKind(local, DateTimeKind.Utc); }
			}

			var fromDate = Date(from);
			var toDate = Date(to);
			if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
				(fromDate, toDate) = (toDate, fromDate);
			return (fromDate.HasValue ? Utc(fromDate.Value) : (DateTime?)null,
				toDate.HasValue ? Utc(toDate.Value.AddDays(1).AddTicks(-1)) : (DateTime?)null,
				invalid);
		}

		private static List<string> Ordered(IEnumerable<string> families)
		{
			var list = (families ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return list.OrderBy(f => { var i = Array.IndexOf(FamilyOrder, f); return i < 0 ? int.MaxValue : i; }).ToList();
		}

		private static SearchResultRow Row(UnifiedSearchHit hit, Department department)
		{
			string number = null;
			hit.Metadata?.TryGetValue("Number", out number);
			return new SearchResultRow
			{
				EntityType = hit.EntityType,
				Title = hit.Title,
				Url = AbsoluteUrl(hit.Url),
				Summary = hit.Summary,
				Snippet = hit.Snippet,
				OccurredOn = hit.OccurredOn.HasValue && department != null ? hit.OccurredOn.Value.TimeConverterToString(department) : null,
				Status = hit.Status,
				Category = hit.Category,
				Number = number
			};
		}

		private static string AbsoluteUrl(string url)
		{
			if (string.IsNullOrWhiteSpace(url))
				return null;
			return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : Config.SystemBehaviorConfig.ResgridBaseUrl + url;
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
				case SearchEntityTypes.Invoice: return "Invoices";
				case SearchEntityTypes.RateCard: return "Rate Cards";
				case SearchEntityTypes.Bid: return "Bids";
				case SearchEntityTypes.ServiceContract: return "Contracts";
				case SearchEntityTypes.Deployment: return "Deployments";
				case SearchEntityTypes.CertificationType: return "Certification Types";
				case SearchEntityTypes.Protocol: return "Protocols";
				case SearchEntityTypes.Training: return "Trainings";
				case SearchEntityTypes.CalendarEvent: return "Calendar";
				case SearchEntityTypes.Log: return "Logs";
				case SearchEntityTypes.Poi: return "Points of Interest";
				case SearchEntityTypes.Shift: return "Shifts";
				case SearchEntityTypes.Group: return "Groups & Stations";
				case SearchEntityTypes.Occupancy: return "Occupancies";
				default: return entityType;
			}
		}
	}
}
