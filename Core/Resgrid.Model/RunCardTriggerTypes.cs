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
		CallPriorityAndType = 2
	}
}
