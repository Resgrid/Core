using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Search
{
	/// <summary>Who is asking. Built by the controller from authenticated state; never from request input (plan 2026-08-15 correction).</summary>
	public class SearchPrincipal
	{
		public string UserId { get; set; }

		public int DepartmentId { get; set; }

		public bool IsDepartmentAdmin { get; set; }

		public bool IsGroupAdmin { get; set; }

		/// <summary>Claim check against the caller's principal: (resource, action) → held.</summary>
		public Func<string, string, bool> HasClaim { get; set; } = (r, a) => false;

		/// <summary>Department module toggles (Messaging, Mapping, ...); missing or failed checks deny module access.</summary>
		public Func<string, bool> IsModuleEnabled { get; set; }

		public bool HasResourceClaim(string resource, string action)
		{
			try { return HasClaim != null && HasClaim(resource, action); }
			catch { return false; }
		}

		public bool ModuleEnabled(string module)
		{
			if (string.IsNullOrWhiteSpace(module))
				return true;
			try { return IsModuleEnabled != null && IsModuleEnabled(module); }
			catch { return false; }
		}
	}

	public class UnifiedSearchRequest
	{
		public string Text { get; set; }

		/// <summary>Restrict to these <see cref="SearchEntityTypes"/>; null or empty means every indexed family plus Records and Actions.</summary>
		public List<string> EntityTypes { get; set; }

		public bool IncludeActions { get; set; } = true;

		public bool IncludeRecords { get; set; } = true;

		public int Skip { get; set; }

		public int Take { get; set; } = 20;

		/// <summary>Typeahead: prefix-match the title of every family; short result list, no records federation.</summary>
		public bool Prefix { get; set; }

		/// <summary>Only hits that occurred at or after this instant (UTC).</summary>
		public DateTime? FromUtc { get; set; }

		/// <summary>Only hits that occurred at or before this instant (UTC).</summary>
		public DateTime? ToUtc { get; set; }

		/// <summary>One of <see cref="SearchSortOrders"/>; null or unknown means relevance.</summary>
		public string Sort { get; set; }

		/// <summary>
		/// How many index candidates may be authorized to serve this request (0 = the typeahead default). The search page and
		/// its export ask for more so a narrowed query can list and count every match; capped by SearchConfig.MaxPageWindow.
		/// </summary>
		public int MaxCandidates { get; set; }

		/// <summary>
		/// Authorize every candidate in the window so an exact total can be returned. A caller that shows no total (the
		/// command palette) turns it off and authorization stops once the page is full; Total is then null.
		/// </summary>
		public bool CountTotal { get; set; } = true;
	}

	/// <summary>Result orderings the unified endpoint understands.</summary>
	public static class SearchSortOrders
	{
		public const string Relevance = "relevance";
		public const string Newest = "newest";
		public const string Oldest = "oldest";

		public static string Normalize(string sort)
		{
			if (string.Equals(sort, Newest, StringComparison.OrdinalIgnoreCase)) return Newest;
			if (string.Equals(sort, Oldest, StringComparison.OrdinalIgnoreCase)) return Oldest;
			return Relevance;
		}
	}

	public class UnifiedSearchHit
	{
		public string EntityType { get; set; }
		public string EntityId { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Url { get; set; }
		public float Score { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string Category { get; set; }
		public string Status { get; set; }
		/// <summary>The part of the indexed text around the first match (full-text queries only); plain text, never HTML.</summary>
		public string Snippet { get; set; }
		public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
	}

	public class UnifiedSearchResult
	{
		public List<UnifiedSearchHit> Hits { get; set; } = new List<UnifiedSearchHit>();

		public List<SystemActionHit> Actions { get; set; } = new List<SystemActionHit>();

		/// <summary>Authorized total, or null when any candidate was dropped or was outside the authorization window.</summary>
		public int? Total { get; set; }

		public bool Truncated { get; set; }

		/// <summary>False when the department's search flag is off or the caller may not search at all.</summary>
		public bool Available { get; set; } = true;

		/// <summary>True when the index could not serve (host off, index not built yet, error); actions still return.</summary>
		public bool Degraded { get; set; }

		public string DegradedReason { get; set; }

		/// <summary>True while the department's index is queued for, or in the middle of, a build: results may be incomplete.</summary>
		public bool IndexBuilding { get; set; }

		public int QueryTimeMs { get; set; }
	}

	/// <summary>Categories for system functionality entries.</summary>
	public static class SystemActionCategories
	{
		public const string Navigate = "Navigate";
		public const string Create = "Create";
		public const string Manage = "Manage";
		public const string Account = "Account";
	}

	/// <summary>Module switch names understood by <see cref="SearchPrincipal.IsModuleEnabled"/>.</summary>
	public static class SystemActionModules
	{
		public const string Messaging = "Messaging";
		public const string Mapping = "Mapping";
		public const string Shifts = "Shifts";
		public const string Logs = "Logs";
		public const string Reports = "Reports";
		public const string Documents = "Documents";
		public const string Calendar = "Calendar";
		public const string Notes = "Notes";
		public const string Training = "Training";
		public const string Inventory = "Inventory";
		public const string Maintenance = "Maintenance";
		public const string BusinessOperations = "BusinessOperations";
	}

	/// <summary>
	/// One piece of system functionality a user can jump to: a page or an action. Static catalog, filtered per caller
	/// by claim, department-admin status, module switch and feature flag before it is ever scored.
	/// </summary>
	public class SystemActionDefinition
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string[] Keywords { get; set; } = Array.Empty<string>();
		public string Category { get; set; } = SystemActionCategories.Navigate;

		/// <summary>Relative web path, e.g. /User/Dispatch/Dashboard. "{userId}" is replaced with the caller's id.</summary>
		public string WebPath { get; set; }

		/// <summary>Claim resource + action required, e.g. ("Call", "Create"); null = any member.</summary>
		public string ClaimResource { get; set; }
		public string ClaimAction { get; set; }

		public bool DepartmentAdminOnly { get; set; }

		/// <summary>Department module switch that must be on (see <see cref="SystemActionModules"/>).</summary>
		public string Module { get; set; }

		/// <summary>Feature flag key that must evaluate true for the department (FeatureFlagKeys).</summary>
		public string FeatureFlag { get; set; }

		/// <summary>Hide once the department's Records cutover is active: a legacy Logs write that the Logs pages now refuse. Reads stay listed.</summary>
		public bool HiddenAfterRecordsCutover { get; set; }
	}

	public class SystemActionHit
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string Url { get; set; }
		public float Score { get; set; }
	}
}
