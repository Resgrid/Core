using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Resgrid.Web.Eventing.Hubs;

namespace Resgrid.Web.Eventing.Services
{
	/// <summary>
	/// Re-applies the location visibility matrix to this instance's geolocation connections. The matrix is
	/// rebuilt when groups, roles or permissions change, and Eventing is not told; the instance publishing
	/// a location uses the new matrix on its next read, so memberships here must follow or viewers who
	/// kept access would miss updates and viewers who lost it would keep receiving them.
	/// </summary>
	public sealed class GeolocationVisibilitySync : BackgroundService
	{
		public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

		private readonly GeolocationConnectionTracker _connectionTracker;
		private readonly GeolocationMembership _membership;
		private readonly IHubContext<GeolocationHub> _geolocationHub;

		public GeolocationVisibilitySync(GeolocationConnectionTracker connectionTracker, GeolocationMembership membership,
			IHubContext<GeolocationHub> geolocationHub)
		{
			_connectionTracker = connectionTracker;
			_membership = membership;
			_geolocationHub = geolocationHub;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			using var timer = new PeriodicTimer(Interval);

			try
			{
				while (await timer.WaitForNextTickAsync(stoppingToken))
					await SyncAllAsync(stoppingToken);
			}
			catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
			{
			}
		}

		public async Task SyncAllAsync(CancellationToken cancellationToken)
		{
			foreach (var connection in _connectionTracker.GetAll())
			{
				// Disconnected since the listing; its groups went with it.
				if (!_connectionTracker.Contains(connection.ConnectionId))
					continue;

				try
				{
					await _membership.SyncAsync(_geolocationHub.Groups, connection, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// One connection (typically one that just dropped) must not stop the rest.
					if (_connectionTracker.Contains(connection.ConnectionId))
						Resgrid.Framework.Logging.LogException(ex, $"Unable to sync geolocation groups for connection {connection.ConnectionId}.");
				}
			}
		}
	}
}
