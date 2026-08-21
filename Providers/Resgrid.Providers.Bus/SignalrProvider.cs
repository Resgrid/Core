using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class SignalrProvider : ISignalrProvider
	{
		private static HubConnection _hubConnection;

		// One minute short of the five-minute lifetime the token endpoint mints for this client, so a
		// token handed out at the end of the window still has usable life left on it.
		private static readonly TimeSpan AccessTokenCacheLength = TimeSpan.FromMinutes(4);
		private const string AccessTokenCacheKey = "SignalrEventingAccessToken";
		//private static IHubProxy _eventingHubProxy;

		private readonly ICacheProvider _cacheProvider;

		public SignalrProvider(ICacheProvider cacheProvider)
		{
			_cacheProvider = cacheProvider;
			Create();
		}

		public async Task<bool> PersonnelStatusUpdated(int departmentId, ActionLog actionLog)
		{
			try
			{
				await Connect();
				await _hubConnection.InvokeAsync("personnelStatusUpdated", departmentId, actionLog.ActionLogId);
				return true;
			}
			catch (Exception e)
			{
				// Disabling due to unnecessary logging of redundant exceptions.
				//Logging.LogException(e);
			}

			return false;
		}

		public async Task<bool> PersonnelStaffingUpdated(int departmentId, UserState userState)
		{
			try
			{
				await Connect();
				await _hubConnection.InvokeAsync("personnelStaffingUpdated", departmentId, userState.UserStateId);
				return true;
			}
			catch (Exception e)
			{
				// Disabling due to unnecessary logging of redundant exceptions.
				//Logging.LogException(e);
			}

			return false;
		}

		public async Task<bool> UnitStatusUpdated(int departmentId, UnitState unitState)
		{
			try
			{
				await Connect();
				await _hubConnection.InvokeAsync("unitStatusUpdated", departmentId, unitState.UnitStateId);
				return true;
			}
			catch (Exception e)
			{
				// Disabling due to unnecessary logging of redundant exceptions.
				//Logging.LogException(e);
			}

			return false;
		}

		public async Task<bool> CallsUpdated(int departmentId, Call call)
		{
			try
			{
				await Connect();
				await _hubConnection.InvokeAsync("callsUpdated", departmentId, call.CallId);
				return true;
			}
			catch (Exception e)
			{
				// Disabling due to unnecessary logging of redundant exceptions.
				//Logging.LogException(e);
			}

			return false;
		}

		private void Create()
		{
			_hubConnection = new HubConnectionBuilder()
				.WithUrl($"{Config.SystemBehaviorConfig.ResgridEventingBaseUrl}/eventingHub", options => {
					options.AccessTokenProvider = GetAccessTokenAsync;
					options.HttpMessageHandlerFactory = (msg) =>
					{
						if (Config.ApiConfig.BypassSslChecks && msg is HttpClientHandler clientHandler)
						{
							clientHandler.ServerCertificateCustomValidationCallback =
								HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
						}

						return msg;
					};
				})
				.WithAutomaticReconnect()
				.Build();

			//_hubConnection.Closed += async (error) =>
			//{
			//	await Task.Delay(new Random().Next(0,5) * 1000);
			//	await _hubConnection.StartAsync();
			//};
		}

		private async Task<string> GetAccessTokenAsync()
		{
			if (string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
				return null;

			// Cache-aside through the shared provider, so every process works from the same token
			// instead of each one minting its own. A failed request returns null, which the provider
			// does not write back, so the next caller retries immediately.
			var accessToken = await _cacheProvider.RetrieveAsync(AccessTokenCacheKey,
				RequestAccessTokenAsync, AccessTokenCacheLength);

			return string.IsNullOrWhiteSpace(accessToken) ? null : accessToken;
		}

		private static async Task<string> RequestAccessTokenAsync()
		{
			using var handler = new HttpClientHandler();
			if (Config.ApiConfig.BypassSslChecks)
				handler.ServerCertificateCustomValidationCallback =
					HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
			// The SignalR handshake waits on this call, so the 100-second default would hold a
			// connection attempt open against an unresponsive token endpoint.
			using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
			using var request = new HttpRequestMessage(HttpMethod.Post,
				$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl.TrimEnd('/')}/api/v4/connect/token")
			{
				Content = new FormUrlEncodedContent(new Dictionary<string, string>
				{
					["grant_type"] = "client_credentials",
					["client_id"] = "resgrid_eventing",
					["client_secret"] = Config.ApiConfig.BackendInternalApikey
				})
			};
			using var response = await client.SendAsync(request);
			if (!response.IsSuccessStatusCode)
				return null;

			await using var stream = await response.Content.ReadAsStreamAsync();
			using var json = await JsonDocument.ParseAsync(stream);
			return json.RootElement.TryGetProperty("access_token", out var tokenElement)
				? tokenElement.GetString()
				: null;
		}

		private async Task Connect()
		{
			try
			{
				if (_hubConnection.State == HubConnectionState.Disconnected)
					await _hubConnection.StartAsync();
			}
			catch (Exception ex)
			{
				//Logging.LogException(ex);
				//Create();
			}
		}
	}
}
