using Newtonsoft.Json;
using System.Collections.Generic;

namespace Resgrid.Providers.Voip.LiveKit.Model
{
	/// <summary>
	/// https://docs.livekit.io/server/room-management/#participantinfo
	/// </summary>
	public class LiveKitParticipants
	{
		[JsonProperty("participants")]
		public List<LiveKitParticipantInfo> Participants { get; set; }
	}
}
