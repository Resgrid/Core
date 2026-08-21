using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
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
		private static readonly SemaphoreSlim TokenLock = new SemaphoreSlim(1, 1);
		private static string _accessToken;
		private static DateTime _accessTokenRefreshOn;
		//private static IHubProxy _eventingHubProxy;

		public SignalrProvider()
		{
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

		private static async Task<string> GetAccessTokenAsync()
		{
			if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenRefreshOn > DateTime.UtcNow)
				return _accessToken;
			if (string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
				return null;

			await TokenLock.WaitAsync();
			try
			{
				if (!string.IsNullOrWhiteSpace(_accessToken) && _accessTokenRefreshOn > DateTime.UtcNow)
					return _accessToken;

				using var handler = new HttpClientHandler();
				if (Config.ApiConfig.BypassSslChecks)
					handler.ServerCertificateCustomValidationCallback =
						HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
				using var client = new HttpClient(handler);
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
				if (!json.RootElement.TryGetProperty("access_token", out var tokenElement))
					return null;

				_accessToken = tokenElement.GetString();
				_accessTokenRefreshOn = DateTime.UtcNow.AddMinutes(4);
				return _accessToken;
			}
			finally
			{
				TokenLock.Release();
			}
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
