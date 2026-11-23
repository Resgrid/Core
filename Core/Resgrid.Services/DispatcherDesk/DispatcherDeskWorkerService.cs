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
	/// sign-off at the end of duty; expired offers, overflow, escalation; pending and scheduled calls added to the desk; voicemails
	/// whose request stopped before they became a call; the 180-day purge once an hour. Each part is isolated so one failing never
	/// stops the others.
	/// </summary>
	public class DispatcherDeskWorkerService : IDispatcherDeskWorkerService
	{
		private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);

		/// <summary>How far back stranded voicemail claims are recovered (an older one is left in the item's history).</summary>
		public static readonly TimeSpan VoicemailRecoveryWindow = TimeSpan.FromDays(2);
		private static DateTime _lastPurge = DateTime.MinValue;

		private readonly IDispatcherBreaksService _breaksService;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherRoutingService _routingService;
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IDispatcherDeskPendingCalls _pendingCalls;

		public DispatcherDeskWorkerService(IDispatcherBreaksService breaksService, IDispatcherPresenceService presenceService,
			IDispatcherRoutingService routingService, IDispatcherPresencesRepository presencesRepository, IDispatcherDeskPendingCalls pendingCalls)
		{
			_breaksService = breaksService;
			_presenceService = presenceService;
			_routingService = routingService;
			_presencesRepository = presencesRepository;
			_pendingCalls = pendingCalls;
		}

		public async Task<int> TickAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var changes = 0;

			changes += await RunAsync("break timers", () => _breaksService.ProcessBreakTimersAsync(utcNow, cancellationToken));
			changes += await RunAsync("presence", () => _presenceService.ProcessPresenceAsync(utcNow, cancellationToken));

			// Calls are added only where someone is on the desk to take them. The lookup is its own part: failing, it skips the
			// call sweeps but not the timeouts and purge below. Each department's sweep is isolated inside it.
			changes += await RunAsync("call sweep", async () =>
			{
				var swept = 0;
				var departments = (await _presencesRepository.GetSignedInAsync(cancellationToken) ?? Enumerable.Empty<Model.DispatcherPresence>())
					.Select(x => x.DepartmentId).Distinct().ToList();
				foreach (var departmentId in departments)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					swept += await RunAsync($"call sweep {departmentId}", () => _routingService.SyncCallsAsync(departmentId, utcNow, cancellationToken));
				}

				return swept;
			});

			changes += await RunAsync("queue timeouts", () => _routingService.ProcessTimeoutsAsync(utcNow, cancellationToken));

			// A voicemail's record action comes once: when its request stopped (or could not confirm its save) after claiming the item,
			// nothing else ever finishes it. Overflowed items are closed, so the queue timeouts above never see them.
			changes += await RunAsync("voicemail recovery",
				() => _pendingCalls.RecoverExpiredVoicemailClaimsAsync(utcNow, utcNow - VoicemailRecoveryWindow, cancellationToken));

			if (utcNow - _lastPurge > PurgeInterval)
			{
				// The clock moves only on success (zero rows included): a failed or cancelled purge runs again next tick.
				changes += await RunAsync("purge", async () =>
				{
					var purged = await _routingService.PurgeAsync(utcNow, cancellationToken);
					_lastPurge = utcNow;
					return purged;
				});
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
