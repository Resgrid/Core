namespace Resgrid.Model.Events
{
	/// <summary>
	/// A call's Enhanced AI destination advice was written, applied, dismissed or superseded (EventingTypes 18, M0323). Fans out over the
	/// eventing topic as <c>aiDestinationAdviceUpdated</c> with the call id and a small payload (advice id, state, whether it is a change)
	/// but no recommendation or reasoning: clients reload through AiDestinations/GetAdvice, which checks the viewer may see the call.
	/// </summary>
	public class AiDestinationAdviceUpdatedEvent
	{
		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Payload { get; set; }
	}
}
