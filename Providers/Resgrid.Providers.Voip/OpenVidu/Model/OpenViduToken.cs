using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.OpenVidu.Model
{
	public class OpenViduToken
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("object")]
		public string ObjectType { get; set; }

		[JsonProperty("status")]
		public string Status { get; set; }

		[JsonProperty("connectionId")]
		public string ConnectionId { get; set; }

		[JsonProperty("sessionId")]
		public string SessionId { get; set; }

		[JsonProperty("createdAt")]
		public double CreatedAt { get; set; }

		[JsonProperty("type")]
		public string Type { get; set; }

		[JsonProperty("record")]
		public bool Record { get; set; }

		[JsonProperty("role")]
		public string Role { get; set; }

		[JsonProperty("kurentoOptions")]
		public string KurentoOptions { get; set; }

		[JsonProperty("rtspUri")]
		public string RtspUri { get; set; }

		[JsonProperty("adaptativeBitrate")]
		public string AdaptativeBitrate { get; set; }

		[JsonProperty("onlyPlayWithSubscribers")]
		public string OnlyPlayWithSubscribers { get; set; }

		[JsonProperty("networkCache")]
		public string NetworkCache { get; set; }

		[JsonProperty("serverData")]
		public string ServerData { get; set; }

		[JsonProperty("token")]
		public string Token { get; set; }

		[JsonProperty("activeAt")]
		public string ActiveAt { get; set; }

		[JsonProperty("location")]
		public string Location { get; set; }

		[JsonProperty("ip")]
		public string IP { get; set; }

		[JsonProperty("platform")]
		public string Platform { get; set; }

		[JsonProperty("clientData")]
		public string ClientData { get; set; }

		[JsonProperty("publishers")]
		public string Publishers { get; set; }

		[JsonProperty("subscribers")]
		public string Subscribers { get; set; }
	}
}
