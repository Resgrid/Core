using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Worker 82 (every minute): the desk's clock. Break reminders, "due now", "break over", overdue alerts and auto return;
	/// sign-off at the end of duty; expired offers, overflow, escalation; pending and scheduled calls added to the desk; the
	/// 180-day purge once an hour. Each part is isolated so one failing never stops the others.
	/// </summary>
	public class DispatcherDeskWorkerService : IDispatcherDeskWorkerService
	{
		private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
		private static DateTime _lastPurge = DateTime.MinValue;

		private readonly IDispatcherBreaksService _breaksService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherRoutingService _routingService;
		private readonly IDispatcherPresencesRepository _presencesRepository;

		public DispatcherDeskWorkerService(IDispatcherBreaksService breaksService, IDispatcherPresenceService presenceService,
			IDispatcherRoutingService routingService, IDispatcherPresencesRepository presencesRepository)
		{
			_breaksService = breaksService;
			_presenceService = presenceService;
			_routingService = routingService;
			_presencesRepository = presencesRepository;
		}

		public async Task<int> TickAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var changes = 0;

			changes += await RunAsync("break timers", () => _breaksService.ProcessBreakTimersAsync(utcNow, cancellationToken));
			changes += await RunAsync("presence", () => _presenceService.ProcessPresenceAsync(utcNow, cancellationToken));

			// Calls are added only where someone is on the desk to take them.
			var departments = (await _presencesRepository.GetSignedInAsync() ?? Enumerable.Empty<Model.DispatcherPresence>())
				.Select(x => x.DepartmentId).Distinct().ToList();
			foreach (var departmentId in departments)
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				changes += await RunAsync($"call sweep {departmentId}", () => _routingService.SyncCallsAsync(departmentId, utcNow, cancellationToken));
			}

			changes += await RunAsync("queue timeouts", () => _routingService.ProcessTimeoutsAsync(utcNow, cancellationToken));

			if (utcNow - _lastPurge > PurgeInterval)
			{
				_lastPurge = utcNow;
				changes += await RunAsync("purge", () => _routingService.PurgeAsync(utcNow, cancellationToken));
			}

			return changes;
		}

		private static async Task<int> RunAsync(string part, Func<Task<int>> work)
		{
			try
			{
				return await work();
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk worker: {part}");
				return 0;
			}
		}
	}
}
