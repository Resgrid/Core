using Newtonsoft.Json;

namespace Resgrid.WebCore.Areas.User.Models.Search
{
	public class SearchResultJson
	{
		[JsonProperty(PropertyName = "label")]
		public string Label { get; set; }

		[JsonProperty(PropertyName = "summary")]
		public string Summary { get; set; }

		[JsonProperty(PropertyName = "url")]
		public string Url { get; set; }
	}
}
