namespace Resgrid.Model.Invoicing
{
	/// <summary>Where a generated hourly unit line's on-scene time came from (InvoiceLineItem.TimeSource).</summary>
	public enum InvoiceLineTimeSources
	{
		/// <summary>The unit's own On Scene status (and the status that ended it), linked to the call by the sender.</summary>
		UnitStatus = 1,

		/// <summary>On Scene (or the status that ended it) was sent without a call and Resgrid linked it: carried forward
		/// from the previous status, the unit's one open dispatch, or the unit a person rode.</summary>
		AutoLinkedStatus = 2,

		/// <summary>On Scene (or the status that ended it) was set with no destination while the unit was dispatched to
		/// the call, and is inferred to belong to it.</summary>
		InferredStatus = 3,

		/// <summary>The unit never reported On Scene on the call; the line bills the call's logged-to-closed window.</summary>
		CallWindow = 4
	}
}
