namespace Resgrid.Model.Events
{
	/// <summary>
	/// Raised when a unit status timer acknowledgement is added, replaced or cleared. Fans out over the
	/// eventing topic as <c>unitStatusAlertUpdated</c>, so every board in the department refreshes its
	/// acknowledgements and nobody keeps staring at an alert a colleague has already handled.
	/// </summary>
	public class UnitStatusAlertUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int UnitId { get; set; }
	}
}
