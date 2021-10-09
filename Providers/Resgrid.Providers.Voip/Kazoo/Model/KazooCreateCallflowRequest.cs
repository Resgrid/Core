using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCreateCallflowRequest
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("numbers")]
		public List<string> Numbers { get; set; }

		[JsonProperty("flow")]
		public FlowRequest Flow { get; set; } // id is conferenceid, module is conference
	}

	public class FlowRequest
	{
		[JsonProperty("data")]
		public FlowData Data { get; set; }

		[JsonProperty("module")]
		public string Module { get; set; }
	}
}
