namespace Resgrid.Model.Events
{
	/// <summary>Raised when command is established on an incident (§3.12 workflow trigger).</summary>
	public class CommandEstablishedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string EstablishedByUserId { get; set; }
	}

	/// <summary>Raised when command is transferred to another user.</summary>
	public class CommandTransferredEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string FromUserId { get; set; }
		public string ToUserId { get; set; }
	}

	/// <summary>Raised when a tactical objective / benchmark is completed.</summary>
	public class IncidentObjectiveCompletedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string TacticalObjectiveId { get; set; }
		public string Name { get; set; }
	}

	/// <summary>Raised when command is closed on an incident.</summary>
	public class IncidentClosedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
	}

	/// <summary>A previously closed incident command was reopened.</summary>
	public class IncidentReopenedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string Reason { get; set; }
		public string ReopenedByUserId { get; set; }
	}

	/// <summary>Raised when a resource is assigned to a command structure node.</summary>
	public class IncidentResourceAssignedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public int ResourceKind { get; set; }
		public string ResourceId { get; set; }
	}

	/// <summary>Raised when a resource is released from an incident.</summary>
	public class IncidentResourceReleasedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string ResourceAssignmentId { get; set; }
	}

	/// <summary>Raised when a user is assigned a functional incident role.</summary>
	public class IncidentRoleAssignedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string UserId { get; set; }
		public int RoleType { get; set; }
	}

	/// <summary>Raised when an ad-hoc resource is created for an incident.</summary>
	public class AdHocResourceCreatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string ResourceId { get; set; }
		public string Name { get; set; }
		public string Kind { get; set; }
	}

	/// <summary>Raised when an on-demand tactical voice channel is opened for an incident.</summary>
	public class IncidentChannelOpenedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string DepartmentVoiceChannelId { get; set; }
		public string Name { get; set; }
	}

	/// <summary>
	/// Raised when personnel accountability reaches a critical state (PAR overdue). No firing point yet — a PAR
	/// evaluation worker would raise this; included so departments can configure workflows for it.
	/// </summary>
	public class CriticalParDetectedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string UserId { get; set; }
	}

	/// <summary>
	/// Raised whenever an incident command board changes (establish/transfer/close, lane/resource/objective/timer/
	/// annotation/role mutations, check-in/PAR). Drives the real-time SignalR "Real Time Sync" fan-out to connected
	/// IC clients via the eventing topic, mirroring <see cref="CallUpdatedEvent"/>.
	/// </summary>
	public class IncidentCommandUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
	}

	public class IncidentNoteAddedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string IncidentNoteId { get; set; }
		public int Visibility { get; set; }
		public int NoteType { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public decimal? ContainmentPercent { get; set; }
		public string CreatedByUserId { get; set; }
	}

	public class IncidentNoteRemovedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string IncidentNoteId { get; set; }
		public int Visibility { get; set; }
		public string RemovedByUserId { get; set; }
	}

	public class IncidentAttachmentAddedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string IncidentAttachmentId { get; set; }
		public int Visibility { get; set; }
		public string FileName { get; set; }
		public string ContentType { get; set; }
		public long ContentLength { get; set; }
		public string Sha256Hash { get; set; }
		public string Description { get; set; }
		public string UploadedByUserId { get; set; }
	}

	public class IncidentAttachmentRemovedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string IncidentAttachmentId { get; set; }
		public int Visibility { get; set; }
		public string FileName { get; set; }
		public string RemovedByUserId { get; set; }
	}

	public class IncidentActionPlanUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string ActionPlan { get; set; }
		public string UpdatedByUserId { get; set; }
	}

	public class IncidentCommandPostUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
		public string UpdatedByUserId { get; set; }
	}

	public class IncidentPublicSharingChangedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public bool Enabled { get; set; }
		public string UpdatedByUserId { get; set; }
	}

	/// <summary>Raised when a resource assignment is moved between lanes.</summary>
	public class IncidentResourceMovedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string ResourceAssignmentId { get; set; }
		public int ResourceKind { get; set; }
		public string ResourceId { get; set; }
		public string FromNodeId { get; set; }
		public string ToNodeId { get; set; }
	}

	/// <summary>Raised when a lane's primary or secondary lead changes.</summary>
	public class LaneLeadChangedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string CommandStructureNodeId { get; set; }
		public string LaneName { get; set; }
		public bool IsPrimary { get; set; }
		/// <summary>User id of the lead going off (null when the outgoing lead was external or the slot was empty).</summary>
		public string PreviousLeadUserId { get; set; }
		/// <summary>Display name of the lead going off (external leads; null when the slot was empty).</summary>
		public string PreviousLeadName { get; set; }
		/// <summary>User id of the lead coming on (null when the incoming lead is external or the slot was cleared).</summary>
		public string NewLeadUserId { get; set; }
		/// <summary>Display name of the lead coming on (external leads; null when the slot was cleared).</summary>
		public string NewLeadName { get; set; }
	}

	/// <summary>Raised when an incident need is created or its status/fulfillment changes.</summary>
	public class IncidentNeedChangedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string IncidentNeedId { get; set; }
		public string Name { get; set; }
		public int Status { get; set; }
	}

	/// <summary>Raised when command details (estimated end / important information) are updated.</summary>
	public class IncidentCommandDetailsUpdatedEvent
	{
		public int DepartmentId { get; set; }
		public int CallId { get; set; }
		public string IncidentCommandId { get; set; }
		public string UpdatedByUserId { get; set; }
	}
}
