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
}
