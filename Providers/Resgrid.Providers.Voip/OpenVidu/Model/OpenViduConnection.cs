using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.OpenVidu.Model
{
	public class OpenViduConnection
	{
		[JsonProperty("numberOfElements")]
		public int NumberOfElements { get; set; }

		[JsonProperty("content")]
		public List<string> Content { get; set; }
	}
}
