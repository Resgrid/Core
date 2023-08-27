using System;
using System.Net.Http;
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
					//options.UseDefaultCredentials = true;
					options.HttpMessageHandlerFactory = (msg) =>
					{
						if (msg is HttpClientHandler clientHandler)
						{
							// bypass SSL certificate validation
							clientHandler.ServerCertificateCustomValidationCallback +=
								(sender, certificate, chain, sslPolicyErrors) => { return true; };
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
