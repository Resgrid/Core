using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Dispatch re-notification tracking rows (DispatchRenotifications, M0311).</summary>
	public interface IDispatchRenotificationsRepository
	{
		/// <summary>
		/// Starts re-notification for each row. A recipient with no row for the call is added as given; one already pending or
		/// acknowledged is left alone; one exhausted or cancelled starts a new cycle (status, attempts and next time from the
		/// given row) and keeps its original <see cref="DispatchRenotification.AcknowledgeAfter"/>.
		/// </summary>
		Task RegisterAsync(IEnumerable<DispatchRenotification> rows, CancellationToken cancellationToken = default);

		/// <summary>Pending rows due at or before <paramref name="nowUtc"/>, earliest first.</summary>
		Task<List<DispatchRenotification>> GetDueAsync(DateTime nowUtc, int take, CancellationToken cancellationToken = default);

		/// <summary>
		/// Claims a re-notification attempt: moves a still-pending row from <paramref name="expectedAttempts"/> to
		/// <paramref name="newAttempts"/>, finishing it as exhausted when <paramref name="nextNotifyOn"/> is null. False when another
		/// sweep got there first, or the row is no longer pending; the caller then sends nothing.
		/// </summary>
		Task<bool> TryClaimAttemptAsync(string dispatchRenotificationId, int expectedAttempts, int newAttempts, DateTime? nextNotifyOn, DateTime nowUtc,
			CancellationToken cancellationToken = default);

		/// <summary>Finishes a still-pending row as acknowledged.</summary>
		Task<bool> MarkAcknowledgedAsync(string dispatchRenotificationId, DateTime acknowledgedOnUtc, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Finishes the still-pending rows given as cancelled. Returns how many changed.</summary>
		Task<int> CancelAsync(IEnumerable<string> dispatchRenotificationIds, DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>Deletes up to <paramref name="take"/> finished rows completed before <paramref name="cutoffUtc"/>.</summary>
		Task<int> PurgeCompletedAsync(DateTime cutoffUtc, int take, CancellationToken cancellationToken = default);
	}
}
