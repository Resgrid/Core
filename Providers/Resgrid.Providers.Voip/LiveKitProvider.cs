using Resgrid.Providers.Voip.LiveKit;
using Resgrid.Providers.Voip.LiveKit.Model;
using RestSharp;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Resgrid.Providers.Voip
{
	public class LiveKitProvider
	{
		public string GetTokenForRoom(string name, string roomName)
		{
			var ac = new LiveKitAccessToken(new LiveKitGrant()
			{
				video = new LiveKitVideoGrant()
				{
					roomJoin = true,
					room = roomName
				}
			}, 1440, name);

			return ac.GetToken();
		}



		public async Task<List<LiveKitParticipantInfo>> ListRoomParticipants(string roomName)
		{
			var client = new RestClient(Config.VoipConfig.LiveKitServerApiUrl);

			var ac = new LiveKitAccessToken(new LiveKitGrant()
			{
				video = new LiveKitVideoGrant()
				{
					roomJoin = true,
					roomAdmin = true,
					room = roomName
				}
			});

			var request = new RestRequest($"/twirp/livekit.RoomService/ListParticipants", Method.Post);
			request.AddHeader("Authorization", "Bearer  " + ac.GetToken());
			request.AddHeader("Content-Type", "application/json");

			var body = new
			{
				room = roomName
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<LiveKitParticipants>(request);

			if (response.StatusCode == HttpStatusCode.NotFound)
				return new List<LiveKitParticipantInfo>(); // Room is not active

			if (response == null || response.Data == null)
				return new List<LiveKitParticipantInfo>();

			return response.Data.Participants;
		}
	}
}
