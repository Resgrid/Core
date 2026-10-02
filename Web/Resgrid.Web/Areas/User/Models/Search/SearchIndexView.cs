using System.Collections.Generic;

namespace Resgrid.WebCore.Areas.User.Models.Search
{
	/// <summary>The search page: the query as entered, the families the caller may narrow to, and one page of authorized hits.</summary>
	public class SearchIndexView
	{
		public string Query { get; set; }

		/// <summary>The selected family (a SearchEntityTypes value), or null for every family.</summary>
		public string Type { get; set; }

		/// <summary>Department-local dates as entered (yyyy-MM-dd).</summary>
		public string From { get; set; }
		public string To { get; set; }

		public string Sort { get; set; }

		public int Page { get; set; } = 1;
		public int PageSize { get; set; }

		/// <summary>Families the caller may search, in display order.</summary>
		public List<string> Families { get; set; } = new List<string>();

		public List<SearchResultRow> Results { get; set; } = new List<SearchResultRow>();

		/// <summary>Authorized total, or null when it cannot be proven (see UnifiedSearchResult.Total).</summary>
		public int? Total { get; set; }

		public bool HasMore { get; set; }

		public bool Searched { get; set; }

		/// <summary>Search is off for the department or the caller may not search any family.</summary>
		public bool Unavailable { get; set; }

		public bool Degraded { get; set; }

		public bool IndexBuilding { get; set; }

		/// <summary>The From/To input could not be read as a date; the filter was ignored.</summary>
		public bool InvalidDate { get; set; }

		public int MaxExportRows { get; set; }

		/// <summary>Words and phrases to mark in titles and excerpts.</summary>
		public List<string> HighlightTerms { get; set; } = new List<string>();

		public int FirstIndex => (Page - 1) * PageSize + 1;
		public int LastIndex => FirstIndex + Results.Count - 1;
	}

	public class SearchResultRow
	{
		public string EntityType { get; set; }
		public string Title { get; set; }
		public string Url { get; set; }
		public string Summary { get; set; }
		public string Snippet { get; set; }
		/// <summary>Department-local, formatted.</summary>
		public string OccurredOn { get; set; }
		public string Status { get; set; }
		public string Category { get; set; }
		/// <summary>Call number, for calls.</summary>
		public string Number { get; set; }
	}
}
