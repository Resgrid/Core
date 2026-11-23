namespace Resgrid.Model.Events
{
	/// <summary>The dispatcher desk board changed (EventingTypes 19, hub <c>dispatcherDeskUpdated</c>).</summary>
	public class DispatcherDeskUpdatedEvent
	{
		public int DepartmentId { get; set; }

		/// <summary>The dispatcher it is about, when it is about one; empty otherwise.</summary>
		public string UserId { get; set; }

		/// <summary>Camel-cased JSON: { userId, state }.</summary>
		public string Payload { get; set; }
	}

	/// <summary>The dispatcher queue changed (EventingTypes 20, hub <c>dispatcherQueueUpdated</c>).</summary>
	public class DispatcherQueueUpdatedEvent
	{
		public int DepartmentId { get; set; }

		public string DispatcherQueueItemId { get; set; }

		/// <summary>Camel-cased JSON: { itemId, state, assignedUserId, kind }. Never message content.</summary>
		public string Payload { get; set; }
	}
}
