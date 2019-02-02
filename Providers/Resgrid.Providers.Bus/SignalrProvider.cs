using System;
using System.Net;
using Microsoft.AspNet.SignalR.Client;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.Bus
{
	public class SignalrProvider : ISignalrProvider
	{
		private static HubConnection _hubConnection;
		private static IHubProxy _eventingHubProxy;

		public SignalrProvider()
		{
			Create();
		}

		public void PersonnelStatusUpdated(int departmentId, ActionLog actionLog)
		{
			try
			{
				Connect();
				_eventingHubProxy.Invoke("PersonnelStatusUpdated", departmentId, actionLog.ActionLogId);
			}
			catch (Exception e)
			{
				Logging.LogException(e);
			}
		}

		public void PersonnelStaffingUpdated(int departmentId, UserState userState)
		{
			try
			{
				Connect();
				_eventingHubProxy.Invoke("PersonnelStaffingUpdated", departmentId, userState.UserStateId);
			}
			catch (Exception e)
			{
				Logging.LogException(e);
			}
		}

		public void UnitStatusUpdated(int departmentId, UnitState unitState)
		{
			try
			{
				Connect();
				_eventingHubProxy.Invoke("UnitStatusUpdated", departmentId, unitState.UnitStateId);
			}
			catch (Exception e)
			{
				Logging.LogException(e);
			}
		}

		public void CallsUpdated(int departmentId, Call call)
		{
			try
			{
				Connect();
				_eventingHubProxy.Invoke("CallsUpdated", departmentId, call.CallId);
			}
			catch (Exception e)
			{
				Logging.LogException(e);
			}
		}

		private void Create()
		{
			_hubConnection = new HubConnection(Config.SystemBehaviorConfig.ResgridApiBaseUrl);
			_eventingHubProxy = _hubConnection.CreateHubProxy("eventingHub");

			_hubConnection.Closed += () => { _hubConnection.Start().Wait(); };
			_hubConnection.Error += ex => Logging.LogException(ex);
		}

		private void Connect()
		{
			try
			{
				if (_hubConnection.State == ConnectionState.Disconnected)
					_hubConnection.Start().Wait();
			}
			catch
			{
				Create();
			}
		}
	}
}
