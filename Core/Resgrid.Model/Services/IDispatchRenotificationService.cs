using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Re-notifies dispatched personnel and units until they acknowledge a call (Dispatch Settings, off by default). The call
	/// broadcast registers who it sent the dispatch to; the dispatch re-notify worker sweeps the due rows every minute.
	/// </summary>
	public interface IDispatchRenotificationService
	{
		/// <summary>
		/// Starts re-notification for the recipients a broadcast just reached, when the call's department has it on. Does
		/// nothing otherwise. <paramref name="dispatchedOnUtc"/> is when the dispatch was sent: a status set from then on
		/// acknowledges it.
		/// </summary>
		Task RegisterBroadcastAsync(Call call, IEnumerable<DispatchRenotifyRecipient> recipients, DateTime dispatchedOnUtc,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Re-notifies every recipient that is due and has not acknowledged, and finishes the rows that acknowledged, ran out of
		/// attempts, or no longer apply (call closed, recipient off the call, department turned it off).
		/// </summary>
		Task<DispatchRenotifySweepResult> ProcessDueAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>When the user acknowledged: their first status in the call's department set by a person since <paramref name="sinceUtc"/>; null when none.</summary>
		Task<DateTime?> GetUserAcknowledgmentAsync(string userId, int departmentId, DateTime sinceUtc, DateTime nowUtc);

		/// <summary>When the unit acknowledged: its first status set by a person since <paramref name="sinceUtc"/>; null when none.</summary>
		Task<DateTime?> GetUnitAcknowledgmentAsync(int unitId, DateTime sinceUtc, DateTime nowUtc);
	}
}
