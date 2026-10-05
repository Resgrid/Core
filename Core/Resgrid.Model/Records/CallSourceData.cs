using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>What a <see cref="CallSourceEntry"/> records.</summary>
	public enum CallSourceEntryKind
	{
		/// <summary>The call was created, dispatched or closed.</summary>
		Call = 1,
		UnitDispatch = 2,
		PersonnelDispatch = 3,
		UnitStatus = 4,
		PersonnelStatus = 5,
		/// <summary>An Incident Command timeline entry (command established, transferred, resource assigned or released, PAR...).</summary>
		Command = 6,
		/// <summary>A command objective was completed.</summary>
		Objective = 7,
		/// <summary>A personnel or unit check-in on the call.</summary>
		CheckIn = 8
	}

	/// <summary>
	/// The report time a source entry can supply. Report forms offer an entry for the fields of its milestone first; the
	/// author may still use any entry's time anywhere.
	/// </summary>
	public enum CallSourceMilestone
	{
		None = 0,
		Dispatched = 1,
		Enroute = 2,
		OnScene = 3,
		Staging = 4,
		Cleared = 5,
		Cancelled = 6,
		/// <summary>Back in service (available) after clearing the call.</summary>
		InService = 7,
		CallCreated = 8,
		CallClosed = 9,
		CommandEstablished = 10,
		CommandClosed = 11,
		/// <summary>A command objective that names a NERIS tactic timestamp (see <see cref="CallSourceEntry.TacticTimestamp"/>).</summary>
		TacticMilestone = 12
	}

	/// <summary>
	/// One dated fact about a call that a report author may need: a status a unit or person set, a dispatch, a command
	/// event. Entries are read from their source at request time and are never stored; they carry who set them so the
	/// author can tell a crew's own button press from a dispatcher's or an incident commander's entry.
	/// </summary>
	public class CallSourceEntry
	{
		/// <summary>Stable within one read, e.g. <c>unit-state:123</c>.</summary>
		public string Id { get; set; }
		public CallSourceEntryKind Kind { get; set; }
		public DateTime TimestampUtc { get; set; }
		/// <summary>The unit the entry is about, or the unit a person rode on.</summary>
		public int? UnitId { get; set; }
		/// <summary>The person the entry is about.</summary>
		public string UserId { get; set; }
		/// <summary>The unit's or person's name, or the call/command for call-level entries.</summary>
		public string SubjectName { get; set; }
		/// <summary>Status text or event name.</summary>
		public string Label { get; set; }
		/// <summary>Status color (CSS), when the source has one.</summary>
		public string Color { get; set; }
		/// <summary>Status note, command entry description or objective name.</summary>
		public string Detail { get; set; }
		public CallSourceMilestone Milestone { get; set; }
		/// <summary>The NERIS tactic timestamp field an objective marks (<see cref="NerisTacticTimestamps"/>), else null.</summary>
		public string TacticTimestamp { get; set; }
		/// <summary>How a status was tied to the call (<see cref="StatusDestinationSources"/>); null for non-status entries and rows before M0228.</summary>
		public int? Linkage { get; set; }
		/// <summary>Who set a status relative to its subject (<see cref="StatusSetOrigins"/>); Unknown for rows before M0260.</summary>
		public int Origin { get; set; }
		/// <summary>The member who submitted the entry when it was someone other than its subject.</summary>
		public string SetByUserId { get; set; }
		public string SetByName { get; set; }
	}

	/// <summary>One unit's involvement in a call as the sources tell it.</summary>
	public class CallSourceUnit
	{
		public int UnitId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public int? StationGroupId { get; set; }
		/// <summary>True when the unit was dispatched to the call.</summary>
		public bool WasDispatched { get; set; }
		/// <summary>True when Incident Command assigned the unit on the command board.</summary>
		public bool AssignedByCommand { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? EnrouteOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? StagingOn { get; set; }
		/// <summary>Cancelled before arriving (no on-scene status).</summary>
		public DateTime? CancelledOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		/// <summary>Back in service after clearing (the first available status at or after the clear).</summary>
		public DateTime? InServiceOn { get; set; }
		public DateTime? CommandAssignedOn { get; set; }
		public DateTime? CommandReleasedOn { get; set; }
		/// <summary>How the status-derived times were linked (<see cref="Reporting.CallUnitTimesSources"/>).</summary>
		public int TimesSource { get; set; }
		/// <summary>Distinct people recorded riding the unit on this call; null when no crew was recorded.</summary>
		public int? Staffing { get; set; }
		public List<string> CrewUserIds { get; set; } = new List<string>();
		/// <summary>The crew's names, in the same order as <see cref="CrewUserIds"/>.</summary>
		public List<string> CrewNames { get; set; } = new List<string>();
		/// <summary>The status entries each time came from, keyed by milestone, for provenance.</summary>
		public Dictionary<CallSourceMilestone, CallSourceEntry> TimeEntries { get; set; } = new Dictionary<CallSourceMilestone, CallSourceEntry>();
	}

	/// <summary>One person's involvement in a call as the sources tell it.</summary>
	public class CallSourcePerson
	{
		public string UserId { get; set; }
		public string Name { get; set; }
		/// <summary>The unit the person rode on (the unit status that placed them on it, or a command assignment).</summary>
		public int? UnitId { get; set; }
		public string UnitName { get; set; }
		/// <summary>The unit seat role recorded with the unit status, when there was one.</summary>
		public string Role { get; set; }
		public bool WasDispatched { get; set; }
		public bool AssignedByCommand { get; set; }
		public DateTime? RespondingOn { get; set; }
		public DateTime? OnSceneOn { get; set; }
		public DateTime? ClearedOn { get; set; }
		/// <summary>The person's first and last status (or command assignment) on the call.</summary>
		public DateTime? FirstOn { get; set; }
		public DateTime? LastOn { get; set; }
		/// <summary>True when the person turned out (a non-clearing status, a crew seat, a check-in or a command assignment).</summary>
		public bool Engaged { get; set; }
	}

	/// <summary>A resource from another agency that Incident Command tracked on the call.</summary>
	public class CallSourceMutualAid
	{
		/// <summary>The other agency's name, or the linked department's name.</summary>
		public string AgencyName { get; set; }
		/// <summary>The linked Resgrid department, for a linked-department resource.</summary>
		public int? LinkedDepartmentId { get; set; }
		public List<string> ResourceNames { get; set; } = new List<string>();
		public DateTime? FirstOn { get; set; }
	}

	/// <summary>The Incident Command the call ran under, when there was one.</summary>
	public class CallSourceCommand
	{
		public string IncidentCommandId { get; set; }
		public string Name { get; set; }
		/// <summary>The earliest establishment across the call's commands.</summary>
		public DateTime? EstablishedOn { get; set; }
		public string EstablishedByUserId { get; set; }
		public DateTime? ClosedOn { get; set; }
		/// <summary>Everyone who held command, in order.</summary>
		public List<string> CommanderUserIds { get; set; } = new List<string>();
		public List<string> CommanderNames { get; set; } = new List<string>();
		/// <summary>NERIS tactic timestamps the command's objectives name (field to UTC time).</summary>
		public Dictionary<string, DateTime> TacticTimestamps { get; set; } = new Dictionary<string, DateTime>(StringComparer.Ordinal);
		public List<CallSourceMutualAid> MutualAid { get; set; } = new List<CallSourceMutualAid>();
	}

	/// <summary>
	/// Everything Resgrid holds about a call that a report needs — times, units, people and command — read once from the
	/// call, unit and personnel status history and Incident Command. Report starts prefill from it, report editors look
	/// times up in it, and nothing in it is a live reference.
	/// </summary>
	public class CallSourceData
	{
		public int CallId { get; set; }
		public string Number { get; set; }
		public string IncidentNumber { get; set; }
		public string Type { get; set; }
		/// <summary>The call's address and nature, or null when they are sealed under Protected Data.</summary>
		public string Address { get; set; }
		public string Nature { get; set; }
		public int Priority { get; set; }
		public DateTime LoggedOn { get; set; }
		public DateTime? DispatchedOn { get; set; }
		public DateTime? ClosedOn { get; set; }
		public List<CallSourceEntry> Entries { get; set; } = new List<CallSourceEntry>();
		public List<CallSourceUnit> Units { get; set; } = new List<CallSourceUnit>();
		public List<CallSourcePerson> Personnel { get; set; } = new List<CallSourcePerson>();
		public CallSourceCommand Command { get; set; }
		public DateTime CapturedOn { get; set; }
		/// <summary>Sources that could not be read; the rest of the data is still usable.</summary>
		public List<string> Warnings { get; set; } = new List<string>();
	}
}
