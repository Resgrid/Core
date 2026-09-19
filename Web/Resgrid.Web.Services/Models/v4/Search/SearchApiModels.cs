using System;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Search
{
	/// <summary>Unified search response (plan R4 Phase 2). Shape follows Records/Search: availability and degradation are explicit, totals are null when they cannot be proven.</summary>
	public class SearchResult : StandardApiResponseV4Base
	{
		public SearchResultData Data { get; set; } = new SearchResultData();
	}

	public class SearchResultData
	{
		/// <summary>False when Search.Unified is off for the department.</summary>
		public bool Available { get; set; }

		/// <summary>True when the index could not serve; actions still return.</summary>
		public bool Degraded { get; set; }

		public string DegradedReason { get; set; }

		public List<SearchHitData> Results { get; set; } = new List<SearchHitData>();

		public List<SearchActionData> Actions { get; set; } = new List<SearchActionData>();

		/// <summary>Authorized total, or null when a hit was dropped by per-entity authorization.</summary>
		public int? TotalCount { get; set; }

		public bool Truncated { get; set; }

		public int QueryTimeMs { get; set; }
	}

	public class SearchHitData
	{
		public string EntityType { get; set; }
		public string EntityId { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		/// <summary>Relative web path; clients with native screens switch on EntityType/EntityId instead.</summary>
		public string Url { get; set; }
		public float Score { get; set; }
		public DateTime? OccurredOn { get; set; }
		public string Category { get; set; }
		public string Status { get; set; }
		public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
	}

	public class SearchActionData
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public string Category { get; set; }
		public string Url { get; set; }
		public float Score { get; set; }
	}

	public class SearchRebuildResult : StandardApiResponseV4Base
	{
		public SearchRebuildData Data { get; set; } = new SearchRebuildData();
	}

	public class SearchRebuildData
	{
		public int DepartmentId { get; set; }
		public string State { get; set; }
		public DateTime? RequestedOn { get; set; }
	}

	public class SearchHealthResult : StandardApiResponseV4Base
	{
		public SearchHealthData Data { get; set; } = new SearchHealthData();
	}

	public class SearchHealthData
	{
		public bool Enabled { get; set; }
		public bool GlobalOnline { get; set; }
		/// <summary>Suppressed: shared-index counts include other departments.</summary>
		public int? GlobalDocumentCount { get; set; }
		public bool RecordsOnline { get; set; }
		public int? RecordsDocumentCount { get; set; }
		public bool StoreEnabled { get; set; }
		public string LastSyncedRevision { get; set; }
		public DateTime? LastSyncedOnUtc { get; set; }
		public string DepartmentIndexState { get; set; }
		public int DepartmentDocumentCount { get; set; }
		public DateTime? DepartmentLastRebuiltOn { get; set; }
	}
}
