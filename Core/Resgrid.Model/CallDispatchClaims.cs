using System;

namespace Resgrid.Model
{
	/// <summary>
	/// The claim Dispatch Now and the scheduled-calls worker take on a waiting call before sending it (Calls.DispatchClaimedOn,
	/// M0266). It is held only while that dispatch is in flight: cleared once the broadcast is queued, given back when the
	/// dispatch fails. One left behind by a process that died mid-dispatch expires after <see cref="Lease"/>, and the call
	/// can then be claimed and sent again instead of staying marked sent with nobody paged.
	/// </summary>
	public static class CallDispatchClaims
	{
		/// <summary>Well past any real dispatch (run card enrichment, profile lookup, save, queue).</summary>
		public static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

		/// <summary>Marked dispatched by a claim that was never completed or given back, and whose lease has run out.</summary>
		public static bool IsAbandoned(Call call, DateTime utcNow)
		{
			return call != null && call.HasBeenDispatched == true && call.DispatchClaimedOn.HasValue &&
				call.DispatchClaimedOn.Value < utcNow - Lease;
		}
	}
}
