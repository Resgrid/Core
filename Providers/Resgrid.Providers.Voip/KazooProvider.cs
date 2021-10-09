using Resgrid.Framework;
using Resgrid.Providers.Voip.Kazoo.Model;
using RestSharp;
using RestSharp.Serializers.Newtonsoft.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using RestRequest = RestSharp.RestRequest;

namespace Resgrid.Providers.Voip
{
	public class KazooProvider
	{
		public async Task<KazooCredentials> GetAccountApiToken()
		{
			var credentials = Hashing.ComputeMD5Hash($"{Config.VoipConfig.KazooUsername}:{Config.VoipConfig.KazooPassword}");

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/user_auth", Method.PUT);

			var body = new
			{
				data = new {
					credentials = credentials,
					account_name = Config.VoipConfig.KazzoAccount
				},
				method = "md5"
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<KazooUserAuthResult>(request);

			if (response.StatusCode != HttpStatusCode.Created)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			var kazooCreds = new KazooCredentials();
			kazooCreds.AuthToken = response.Data.AuthToken;
			kazooCreds.AccountId = response.Data.Data.AccountId;

			return kazooCreds;
		}

		public async Task<List<KazooAccountUserDatumResult>> GetUsers()
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/users", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooAccountUsersResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCreateUserDataResult> CreateUser(KazooCreateUserRequest user)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/users", Method.PUT);
			request.JsonSerializer = new NewtonsoftJsonSerializer();
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var body = new
			{
				data = user
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<KazooCreateUserResult>(request);

			if (response.StatusCode != HttpStatusCode.Created)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCreateUserDataResult> DeleteUser(string userId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/users/{userId}", Method.DELETE);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCreateUserResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCreateUserDataResult> GetUser(string userId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/users/{userId}", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCreateUserResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<List<KazooDeviceResult>> GetDevices()
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/devices", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooDevicesResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooDeviceResult> CreateDevice(KazooCreateDeviceRequest device)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/devices", Method.PUT);
			request.JsonSerializer = new NewtonsoftJsonSerializer();
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var body = new
			{
				data = device
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<KazooCreateDeviceResult>(request);

			if (response.StatusCode != HttpStatusCode.Created)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooDeviceResult> DeleteDevice(string deviceId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/devices/{deviceId}", Method.DELETE);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCreateDeviceResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooDeviceResult> GetDevice(string deviceId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/devices/{deviceId}", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCreateDeviceResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<List<KazooConferenceResult>> GetConferences()
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/conferences", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooConferencesResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooConferenceResult> GetConference(string conferenceId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/conferences/{conferenceId}", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooGetConferenceResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooConferenceResult> CreateConference(KazooCreateConferenceRequest conference)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/conferences", Method.PUT);
			request.JsonSerializer = new NewtonsoftJsonSerializer();
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var body = new
			{
				data = conference
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<KazooGetConferenceResult>(request);

			if (response.StatusCode != HttpStatusCode.Created)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooConferenceResult> DeleteConference(string conferenceId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/conferences/{conferenceId}", Method.DELETE);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooGetConferenceResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<List<KazooCallflowResult>> GetCallflows()
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/callflows", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCallflowsResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCallflowDetailDataResult> GetCallflowDetails(string callFlowId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/callflows/{callFlowId}", Method.GET);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCallflowDetailsResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCallflowDetailDataResult> DeleteCallflow(string callFlowId)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/callflows/{callFlowId}", Method.DELETE);
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var response = await client.ExecuteAsync<KazooCallflowDetailsResult>(request);

			if (response.StatusCode != HttpStatusCode.OK)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}

		public async Task<KazooCallflowDetailDataResult> CreateCallflow(KazooCreateCallflowRequest callflow)
		{
			var credentials = await GetAccountApiToken();

			var client = new RestClient(Config.VoipConfig.KazooCrossbarApiUrl);
			var request = new RestRequest($"{Config.VoipConfig.KazooCrossbarApiVersion}/accounts/{credentials.AccountId}/callflows", Method.PUT);
			request.JsonSerializer = new NewtonsoftJsonSerializer();
			request.AddHeader("X-Auth-Token", credentials.AuthToken);

			var body = new
			{
				data = callflow
			};
			request.AddJsonBody(body);

			var response = await client.ExecuteAsync<KazooCallflowDetailsResult>(request);

			if (response.StatusCode != HttpStatusCode.Created)
				return null;

			if (response.Data == null)
				return null;

			if (response.Data.Status != "success")
				return null;

			return response.Data.Data;
		}
	}
}
