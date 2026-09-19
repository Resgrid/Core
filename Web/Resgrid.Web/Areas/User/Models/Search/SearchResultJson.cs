using Newtonsoft.Json;

namespace Resgrid.WebCore.Areas.User.Models.Search
{
	/// <summary>One command-palette row. Label/summary/url are the contract the layout script already renders; group and type drive the section headers.</summary>
	public class SearchResultJson
	{
		[JsonProperty("label")]
		public string Label { get; set; }

		[JsonProperty("summary")]
		public string Summary { get; set; }

		[JsonProperty("url")]
		public string Url { get; set; }

		/// <summary>"Actions" for system functionality, otherwise the plural entity family ("Calls", "Units", ...).</summary>
		[JsonProperty("group")]
		public string Group { get; set; }

		/// <summary>Action category or SearchEntityTypes value.</summary>
		[JsonProperty("type")]
		public string Type { get; set; }
	}
}
