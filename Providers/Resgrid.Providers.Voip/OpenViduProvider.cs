using Resgrid.Providers.Voip.OpenVidu.Model;
using RestSharp;
using System.Net;
using System.Threading.Tasks;

namespace Resgrid.Providers.Voip
{
	public class OpenViduProvider
	{
		public async Task<OpenViduSession> CreateSession(string sessionId)
		{
			var client = new RestClient(Config.VoipConfig.OpenViduUrl);

			var request = new RestRequest($"/openvidu/api/sessions", Method.Post);
			request.AddHeader("Authorization", "Basic " + Encode("OPENVIDUAPP:" + Config.VoipConfig.OpenViduSecret));
			request.AddHeader("Content-Type", "application/json");

			var body = new
			{
				customSessionId = sessionId
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<OpenViduSession>(request);

			if (response == null)
				return null;

			if (response.StatusCode == HttpStatusCode.Conflict) // Already a session with this id active
			{
				var session = new OpenViduSession();
				session.Id = sessionId;
				session.CustomSessionId = sessionId;

				return session;
			}
			else if (response.StatusCode == HttpStatusCode.NotAcceptable) // Already a session but no one in it?
			{
				var session = new OpenViduSession();
				session.Id = sessionId;
				session.CustomSessionId = sessionId;

				return session;
			}

			return response.Data;
		}

		public async Task<OpenViduToken> CreateToken(OpenViduSession session)
		{
			var client = new RestClient(Config.VoipConfig.OpenViduUrl);

			var request = new RestRequest($"/openvidu/api/sessions/" + session.Id + "/connection", Method.Post);
			request.AddHeader("Authorization", "Basic " + Encode("OPENVIDUAPP:" + Config.VoipConfig.OpenViduSecret));
			request.AddHeader("Content-Type", "application/json");

			var body = new
			{

			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<OpenViduToken>(request);

			if (response == null)
				return null;

			return response.Data;
		}

		private static string Encode(string toEncode)
		{
			byte[] bytes = System.Text.Encoding.UTF8.GetBytes(toEncode);
			string toReturn = System.Convert.ToBase64String(bytes);
			return toReturn;
		}
	}

}
