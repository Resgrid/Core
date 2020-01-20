using Newtonsoft.Json;
using System;

namespace Resgrid.Model.Providers.Models.PrintNode
{
	public partial class PrintJob
	{
		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("printer")]
		public Printer Printer { get; set; }

		[JsonProperty("title")]
		public string Title { get; set; }

		[JsonProperty("contentType")]
		public string ContentType { get; set; }

		[JsonProperty("source")]
		public string Source { get; set; }

		[JsonProperty("expireAt")]
		public DateTimeOffset? ExpireAt { get; set; }

		[JsonProperty("createTimestamp")]
		public DateTimeOffset CreateTimestamp { get; set; }

		[JsonProperty("state")]
		public string State { get; set; }
	}
}
