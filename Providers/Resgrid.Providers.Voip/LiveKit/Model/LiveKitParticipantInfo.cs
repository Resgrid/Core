using Newtonsoft.Json;

namespace Resgrid.Providers.Voip.LiveKit.Model
{
	/// <summary>
	/// https://docs.livekit.io/server/room-management/#participantinfo
	/// </summary>
	public class LiveKitParticipantInfo
	{
		[JsonProperty("sid")]
		public string Sid { get; set; }

		[JsonProperty("identity")]
		public string Identity { get; set; }

		[JsonProperty("name")]
		public string Name { get; set; }

		[JsonProperty("numberOfElements")]
		public int NumberOfElements { get; set; }

		//[JsonProperty("tracks")]
		//public int Tracks { get; set; }

		[JsonProperty("metadata")]
		public string Metadata { get; set; }

		[JsonProperty("joined_at")]
		public long JoinedAt { get; set; }

		//[JsonProperty("permission")]
		//public int Permission { get; set; }

		[JsonProperty("is_publisher")]
		public bool IsPublisher { get; set; }
	}
}
