using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooDevicesResult
	{
		[JsonProperty("page_size")]
		public int PageSize { get; set; }

		[JsonProperty("data")]
		public List<KazooDeviceResult> Data { get; set; }

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

	public class KazooDeviceResult
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("mac_address")]
		public string MacAddress { get; set; }

		[JsonProperty("owner_id")]
		public string OwnerId { get; set; }

		[JsonProperty("enabled")]
		public bool Enabled { get; set; }

		[JsonProperty("device_type")]
		public string DeviceType { get; set; }

		[JsonProperty("flags")]
		public List<object> Flags { get; set; }
	}
}
