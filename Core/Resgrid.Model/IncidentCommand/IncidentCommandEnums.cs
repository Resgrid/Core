namespace Resgrid.Model
{
	/// <summary>Lifecycle status of a live incident command instance.</summary>
	public enum IncidentCommandStatus
	{
		Active = 0,
		Closed = 1
	}

	/// <summary>ICS structural node types (the "lanes" / span-of-control units on the command board).</summary>
	public enum CommandNodeType
	{
		Division = 0,
		Group = 1,
		Branch = 2,
		Sector = 3,
		StrikeTeam = 4,
		TaskForce = 5,
		Staging = 6,
		UnifiedCommand = 7
	}

	/// <summary>What kind of resource a <c>ResourceAssignment</c> points at (polymorphic).</summary>
	public enum ResourceAssignmentKind
	{
		RealUnit = 0,
		RealPersonnel = 1,
		LinkedDeptUnit = 2,
		LinkedDeptPersonnel = 3,
		AdHocUnit = 4,
		AdHocPersonnel = 5
	}

	/// <summary>Classification of a tactical objective / benchmark.</summary>
	public enum TacticalObjectiveType
	{
		General = 0,
		Benchmark = 1,
		Safety = 2
	}

	/// <summary>Completion state of a tactical objective.</summary>
	public enum TacticalObjectiveStatus
	{
		Pending = 0,
		Complete = 1
	}

	/// <summary>Type of incident timer (personnel PAR is handled by the Checkin feature, not these).</summary>
	public enum IncidentTimerType
	{
		Scene = 0,
		Benchmark = 1,
		Role = 2,
		Custom = 3
	}

	/// <summary>What an incident timer is scoped to.</summary>
	public enum IncidentTimerScopeType
	{
		Incident = 0,
		Node = 1,
		Unit = 2
	}

	/// <summary>Runtime status of an incident timer.</summary>
	public enum IncidentTimerStatus
	{
		Running = 0,
		Due = 1,
		Acknowledged = 2,
		Stopped = 3
	}

	/// <summary>Type of a real-time map annotation drawn on the tactical map.</summary>
	public enum IncidentMapAnnotationType
	{
		Line = 0,
		Polygon = 1,
		Symbol = 2,
		Text = 3,
		Marker = 4
	}

	/// <summary>Type of an entry in the append-only command (ICS-201) timeline.</summary>
	public enum CommandLogEntryType
	{
		CommandEstablished = 0,
		CommandTransferred = 1,
		NodeAdded = 2,
		NodeUpdated = 3,
		NodeRemoved = 4,
		ResourceAssigned = 5,
		ResourceMoved = 6,
		ResourceReleased = 7,
		ObjectiveAdded = 8,
		ObjectiveCompleted = 9,
		TimerStarted = 10,
		TimerAcknowledged = 11,
		AnnotationAdded = 12,
		AnnotationRemoved = 13,
		CheckIn = 14,
		ChannelOpened = 15,
		ChannelClosed = 16,
		RoleAssigned = 17,
		RoleRemoved = 18,
		AdHocResourceCreated = 19,
		Note = 20,
		CommandClosed = 21,

		/// <summary>
		/// Personnel accountability (PAR) for a member went Critical (overdue for check-in). Written by the
		/// PAR sweep (<c>IncidentCommandService.EvaluateCriticalParAsync</c>) keyed on the subject user, and
		/// doubles as the dedup marker so the alert only re-fires after the member checks in and lapses again.
		/// </summary>
		ParCritical = 22,
		IncidentNoteAdded = 23,
		IncidentNoteRemoved = 24,
		IncidentAttachmentAdded = 25,
		IncidentAttachmentRemoved = 26,
		ActionPlanUpdated = 27,
		CommandPostUpdated = 28,
		PublicSharingEnabled = 29,
		PublicSharingDisabled = 30
	}
}
