using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCallflowsResult
	{
		[JsonProperty("page_size")]
		public int PageSize { get; set; }

		[JsonProperty("data")]
		public List<KazooCallflowResult> Data { get; set; }

		[JsonProperty("revision")]
		public string Revision { get; set; }

		[JsonProperty("timestamp")]
		public DateTime Timestamp { get; set; }

		[JsonProperty("version")]
		public string Version { get; set; }

		[JsonProperty("node")]
		public string Node { get; set; }

		[JsonProperty("request_id")]
		public string RequestId { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }

		[JsonProperty("auth_token")]
		public string AuthToken { get; set; }
	}

	public class KazooCallflowResult
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("numbers")]
		public List<string> Numbers { get; set; }

		[JsonProperty("patterns")]
		public List<string> Patterns { get; set; }

		[JsonProperty("featurecode")]
		public object Featurecode { get; set; }

		[JsonProperty("modules")]
		public List<string> Modules { get; set; }

		[JsonProperty("flags")]
		public List<object> Flags { get; set; }
	}
}
