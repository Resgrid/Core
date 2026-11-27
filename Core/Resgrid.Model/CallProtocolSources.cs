namespace Resgrid.Model
{
	/// <summary>
	/// How a protocol came onto a call, stored in CallProtocols.Source. Persisted: append only, never renumber. Room is left
	/// for the sources the plan adds later (an Enhanced AI suggestion a person confirmed, a call template).
	/// </summary>
	public enum CallProtocolSources
	{
		/// <summary>One of the protocol's triggers matched the call's type, priority and logged time.</summary>
		Trigger = 0,

		/// <summary>Picked by hand: no trigger matched, and the dispatcher (or an app) attached it anyway.</summary>
		Manual = 1,
		/// <summary>
		/// Suggested by Enhanced AI from the intake transcript or SMS thread (P4) and confirmed by the dispatcher, who saw the suggestion and
		/// its prefilled answers before saving. The suggestion id is on the case record.
		/// </summary>
		Ai = 2
	}
}
