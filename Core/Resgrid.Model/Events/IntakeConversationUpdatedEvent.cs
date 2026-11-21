namespace Resgrid.Model.Events
{
	/// <summary>
	/// An intake SMS conversation changed: a message arrived or was sent, a dispatcher connected or released it, it closed
	/// (EventingTypes 21, M0325). Fans out over the eventing topic as <c>intakeConversationUpdated</c> with the call id and a small
	/// payload (conversation id, state, handler) but no message text: clients reload through the conversation endpoints, which check
	/// the viewer may see the call.
	/// </summary>
	public class IntakeConversationUpdatedEvent
	{
		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Payload { get; set; }
	}
}
