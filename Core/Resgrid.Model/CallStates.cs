namespace Resgrid.Model
{
	public enum CallStates
	{
		Active	= 0,
		Closed = 1,
		Cancelled = 2,
		Unfounded = 3,
		Founded = 4,
		Minor = 5,

		// 6 and 7 are the close types the Responder, Unit and Dispatch apps already send to v4 CloseCall,
		// so stored calls carry them; named here so nothing reuses those numbers.
		Transferred = 6,
		FalseAlarm = 7,

		/// <summary>
		/// Saved but not yet dispatched: waiting for a dispatcher to pick it up (calls fed in from another
		/// system, or entered for later). Nobody has been notified, and because every "active" read filters on
		/// State = 0 a pending call stays out of the field apps, maps and boards until it is dispatched.
		/// </summary>
		Pending = 8
	}

	public enum ClosedOnlyCallStates
	{
		Closed = 1,
		Cancelled = 2,
		Unfounded = 3,
		Founded = 4,
		Minor = 5
	}
}
