namespace Resgrid.Model
{
	/// <summary>
	/// What a run card trigger matches against. Mirrors ProtocolTriggerTypes semantics
	/// but run card triggers reference CallTypeId (a real FK) instead of the type name.
	/// </summary>
	public enum RunCardTriggerTypes
	{
		CallPriority = 0,
		CallType = 1,
		CallPriorityAndType = 2,

		/// <summary>Protocols P4 shared trigger engine: any call, narrowed only by the trigger's conditions (<see cref="DispatchTriggerBases.Conditions"/>).</summary>
		Conditions = 3
	}
}
