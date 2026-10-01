using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Resgrid.Config;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Web.Services.Middleware
{
	/// <summary>
	/// Closes this host's SignalR connections whose session ended, locked or passed its idle deadline, every
	/// <c>SessionSecurityConfig.ConnectionSweepIntervalSeconds</c> (passkey workbook section 12, slice 16). Invocations are
	/// validated as they arrive; this stops a connection that only listens from receiving broadcasts after its session ends.
	/// </summary>
	public sealed class SessionConnectionSweepService : BackgroundService
	{
		private readonly SessionConnectionRegistry _connections;
		private readonly IServiceScopeFactory _scopes;

		public SessionConnectionSweepService(SessionConnectionRegistry connections, IServiceScopeFactory scopes)
		{
			_connections = connections;
			_scopes = scopes;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			if (SessionSecurityConfig.ConnectionSweepIntervalSeconds <= 0)
				return;

			using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, SessionSecurityConfig.ConnectionSweepIntervalSeconds)));
			while (await timer.WaitForNextTickAsync(stoppingToken))
			{
				try
				{
					using var scope = _scopes.CreateScope();
					await _connections.SweepAsync(scope.ServiceProvider.GetRequiredService<IUserSessionService>(), stoppingToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					// The next tick tries again; invocations are still validated one by one meanwhile.
					Resgrid.Framework.Logging.LogException(ex, "The SignalR session sweep failed.");
				}
			}
		}
	}
}
