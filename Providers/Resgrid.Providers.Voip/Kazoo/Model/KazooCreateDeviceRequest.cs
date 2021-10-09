using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.Kazoo.Model
{
	public class KazooCreateDeviceRequest
	{
		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("password")]
		public string Password { get; set; }

		[JsonProperty("owner_id")]
		public string OwnerId { get; set; }

		[JsonProperty("device_type")]
		public string DeviceType { get; set; } //sip_device

		[JsonProperty("sip")]
		public Sip Sip { get; set; }
	}

	public class Sip
	{
		[JsonProperty("username")]
		public string Username { get; set; }

		[JsonProperty("password")]
		public string Password { get; set; }
	}
}
