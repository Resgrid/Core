using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.OpenVidu.Model
{
	public class OpenViduSession
	{
		[JsonProperty("id")]
		public string Id { get; set; }

		[JsonProperty("object")]
		public string ObjectType { get; set; }

		[JsonProperty("sessionId")]
		public string SessionId { get; set; }

		[JsonProperty("createdAt")]
		public double CreatedAt { get; set; }

		[JsonProperty("mediaMode")]
		public string MediaMode { get; set; }

		[JsonProperty("recordingMode")]
		public string RecordingMode { get; set; }

		[JsonProperty("defaultRecordingProperties")]
		public OpenViduDefaultRecordingProperties DefaultRecordingProperties { get; set; }

		[JsonProperty("customSessionId")]
		public string CustomSessionId { get; set; }

		[JsonProperty("connections")]
		public OpenViduConnection Connections { get; set; }

		[JsonProperty("recording")]
		public bool Recording { get; set; }

		[JsonProperty("forcedVideoCodec")]
		public string ForcedVideoCodec { get; set; }

		[JsonProperty("allowTranscoding")]
		public bool AllowTranscoding { get; set; }
	}
}
