namespace Resgrid.Model
{
	/// <summary>
	/// How a unit state's or personnel status's destination was decided. Stored on UnitStates.DestinationSource and
	/// ActionLogs.DestinationSource (null on rows written before M0228).
	/// </summary>
	public enum StatusDestinationSources
	{
		/// <summary>The member, dispatcher or system process that set the status chose the destination.</summary>
		Explicit = 1,

		/// <summary>No destination was sent; the server kept the call of the unit's/person's previous status, which was
		/// still open and was not a clearing status.</summary>
		CarryForward = 2,

		/// <summary>No destination was sent; the server used the one open call the unit or person is dispatched to.</summary>
		Dispatch = 3,

		/// <summary>A person riding a unit took the call of the unit status that placed them on it.</summary>
		Unit = 4,

		/// <summary>
		/// Read-only, never stored: a status with no destination that a unit or person dispatched to the call set while
		/// working it, shown on the call's record as inferred.
		/// </summary>
		Inferred = 5
	}
}
