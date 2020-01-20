using Newtonsoft.Json;
using System;

namespace Resgrid.Model.Providers.Models.PrintNode
{
	public partial class Computer
	{
		[JsonProperty("createTimestamp")]
		public DateTimeOffset CreateTimestamp { get; set; }

		[JsonProperty("hostname")]
		public string Hostname { get; set; }

		[JsonProperty("id")]
		public long Id { get; set; }

		[JsonProperty("inet")]
		public string Inet { get; set; }

		[JsonProperty("inet6")]
		public object Inet6 { get; set; }

		[JsonProperty("jre")]
		public object Jre { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("state")]
		public string State { get; set; }

		[JsonProperty("version")]
		public string Version { get; set; }
	}
}
