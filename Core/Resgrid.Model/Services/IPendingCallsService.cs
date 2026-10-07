using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Outcome of <see cref="IPendingCallsService.DispatchNowAsync"/>.
	/// </summary>
	public enum DispatchNowOutcome
	{
		/// <summary>The call is now active and its dispatch was queued for broadcast.</summary>
		Dispatched = 0,

		/// <summary>The call is neither pending nor a scheduled call that is still waiting, so there is nothing to dispatch.</summary>
		NotWaiting = 1,

		/// <summary>The call has nobody to send it to (no personnel, groups, roles or units, and no run card added any).</summary>
		NoRecipients = 2,

		/// <summary>The broadcast could not be queued; the call was left unchanged so it can be tried again.</summary>
		QueueFailed = 3
	}

	/// <summary>
	/// Calls that are saved but not yet sent to anyone: pending calls (<see cref="CallStates.Pending"/>) waiting for a
	/// dispatcher, and scheduled calls whose dispatch time has not come yet.
	/// </summary>
	public interface IPendingCallsService
	{
		/// <summary>
		/// Whether the call is still waiting to be dispatched: pending, or active with a scheduled dispatch that has
		/// not gone out.
		/// </summary>
		bool IsWaitingForDispatch(Call call);

		/// <summary>
		/// Dispatches a waiting call now, using the dispatch lists already on it (personnel, groups, roles, units).
		/// The call must be loaded with those lists. On success it becomes Active, its dispatch time is set to now,
		/// run card auto-dispatch is applied, dispatch statuses are set and the broadcast is queued.
		/// </summary>
		/// <param name="call">The call, with its dispatch collections populated.</param>
		/// <param name="userId">The dispatcher, recorded on any run card activation.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		Task<DispatchNowOutcome> DispatchNowAsync(Call call, string userId, CancellationToken cancellationToken = default(CancellationToken));
	}
}
