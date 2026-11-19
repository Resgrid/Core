using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Pages owed to resources an edit added to a live call (M0321, <see cref="CallBroadcastOutboxEntry"/>). An edit pages only what
	/// it changed, so a page the queue refuses must outlive the request: the edit records it just before saving the call, sends it
	/// at the end, and worker 12 retries whatever is left. Who is paged is decided again at each attempt from the call as it is then.
	/// </summary>
	public interface ICallBroadcastOutboxService
	{
		/// <summary>
		/// Records the page for what the edit added. Never throws: when the row cannot be written the returned entry is not
		/// <see cref="CallBroadcastOutboxEntry.Persisted"/> and <see cref="SendPendingForCallAsync"/> sends it directly, once.
		/// Null when nothing was added.
		/// </summary>
		Task<CallBroadcastOutboxEntry> RecordAsync(Call call, IEnumerable<string> userIds, IEnumerable<int> groupIds, IEnumerable<int> unitIds,
			IEnumerable<int> roleIds, string createdByUserId);

		/// <summary>
		/// Sends the call's unsent pages now: <paramref name="recorded"/> and any an earlier edit left behind, built from
		/// <paramref name="call"/> (the caller's saved, populated call). Never throws; returns how many the queue accepted.
		/// </summary>
		Task<int> SendPendingForCallAsync(Call call, CallBroadcastOutboxEntry recorded = null);

		/// <summary>
		/// A full rebroadcast of <paramref name="broadcastCall"/> went out: the call's pending pages whose every resource was on that
		/// call are covered by it; a page for anyone it did not carry (a concurrent edit added them) stays. Never throws.
		/// </summary>
		Task SupersedeCoveredAsync(Call broadcastCall);

		/// <summary>Worker 12: sends the pages that are due again, then purges old finished rows. Returns how many the queue accepted.</summary>
		Task<int> SendDueAsync(int batchSize, CancellationToken cancellationToken);
	}
}
