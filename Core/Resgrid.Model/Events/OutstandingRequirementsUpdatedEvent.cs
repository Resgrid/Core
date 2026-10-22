namespace Resgrid.Model.Events
{
	/// <summary>
	/// Raised when a call's outstanding requirements change (created, resolved, dismissed), when a freed unit matches calls
	/// awaiting resources, and when one was dispatched to such a call automatically. Fans out over the eventing topic as
	/// <c>outstandingRequirementsUpdated</c> with the call id (0 when several calls are concerned) and the serialized
	/// <see cref="OutstandingRequirementsAlert"/>, so dispatch clients refresh their awaiting-resources views and alert.
	/// </summary>
	public class OutstandingRequirementsUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string Payload { get; set; }
	}
}
