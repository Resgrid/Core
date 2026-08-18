using System;
using System.Collections.Generic;

namespace Resgrid.Model
{
	/// <summary>
	/// Read-only incident view for a responder (user) or unit assigned to — or dispatched on — an incident:
	/// who has command, incident timing, important information, objectives, needs, notes and attachment
	/// metadata, plus the caller's own lane assignment (leads, lane objectives) when they have one.
	/// Assembled by <c>IIncidentCommandService.GetResourceIncidentViewAsync</c>; notes/attachments are
	/// visibility-filtered for callers without command capabilities.
	/// </summary>
	public class ResourceIncidentView
	{
		public string IncidentCommandId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="IncidentCommandStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime EstablishedOn { get; set; }

		public DateTime? EstimatedEndOn { get; set; }

		public DateTime? ClosedOn { get; set; }

		/// <summary>Important information every resource on the incident should see.</summary>
		public string ImportantInformation { get; set; }

		public string IncidentActionPlan { get; set; }

		/// <summary>The current Incident Commander.</summary>
		public IncidentContactInfo Commander { get; set; }

		public List<TacticalObjective> Objectives { get; set; } = new List<TacticalObjective>();

		public List<IncidentNeed> Needs { get; set; } = new List<IncidentNeed>();

		/// <summary>Operational status notes, visibility-filtered for the caller.</summary>
		public List<IncidentNote> Notes { get; set; } = new List<IncidentNote>();

		/// <summary>Attachment metadata (documents/maps/files/images), visibility-filtered; binary content downloads separately.</summary>
		public List<IncidentAttachment> Attachments { get; set; } = new List<IncidentAttachment>();

		/// <summary>The caller's active lane assignment, when they have one (null otherwise).</summary>
		public ResourceLaneAssignmentView MyAssignment { get; set; }

		/// <summary>
		/// ICS positions filled on this incident, with contact details, so a responder can reach the right
		/// person directly instead of going through command. Empty when nobody holds a position.
		/// </summary>
		public List<IncidentRoleContactInfo> Roles { get; set; } = new List<IncidentRoleContactInfo>();

		/// <summary>
		/// Chat channels the CALLER can actually reach, resolved server-side so clients never have to
		/// guess at access. Null means "not available to you" — the caller is not command staff, holds no
		/// lane lead slot, or the channel has not been provisioned.
		/// </summary>
		public IncidentChatChannels Chat { get; set; } = new IncidentChatChannels();
	}

	/// <summary>Who holds an ICS position on the incident, with the contact details to reach them.</summary>
	public class IncidentRoleContactInfo
	{
		/// <summary>Maps to <see cref="IncidentRoleType"/>.</summary>
		public int RoleType { get; set; }

		public IncidentContactInfo Contact { get; set; }
	}

	/// <summary>The incident's chat channels, filtered to the ones the caller may open.</summary>
	public class IncidentChatChannels
	{
		/// <summary>The call-wide incident channel (everyone on the call).</summary>
		public string IncidentChannelId { get; set; }

		/// <summary>The private command channel — only set for command staff (IC or an ICS role holder).</summary>
		public string CommandChannelId { get; set; }

		/// <summary>The "All Leads" channel — only set for the IC and lane primary/secondary leads.</summary>
		public string LeadsChannelId { get; set; }

		/// <summary>The caller's own lane channel, when they are assigned to a lane.</summary>
		public string LaneChannelId { get; set; }

		/// <summary>
		/// The incident's line to the dispatch desk. Available to everyone on the incident — a crew
		/// needing dispatch shouldn't have to route through command to reach them.
		/// </summary>
		public string DispatchChannelId { get; set; }

		/// <summary>True once the incident is closed: the conversations are readable but frozen.</summary>
		public bool IsFrozen { get; set; }

		/// <summary>
		/// Whether "Message the IC" is offerable: true only while a commander actually holds the incident.
		/// The line is addressed to the command role, so with nobody in the seat there is no one to
		/// address — clients keep the action disabled rather than opening a conversation into the void.
		/// The caller for the commander themselves is left false; they have no reason to message the seat
		/// they are sitting in.
		/// </summary>
		public bool CanMessageCommander { get; set; }
	}

	/// <summary>Contact card for a person relevant to a resource (commander or lane lead).</summary>
	public class IncidentContactInfo
	{
		/// <summary>Set when this contact is a Resgrid user; null for external contacts.</summary>
		public string UserId { get; set; }

		public string Name { get; set; }

		public string Phone { get; set; }

		public string Email { get; set; }
	}

	/// <summary>A resource's own lane assignment: the lane, its leads, and its linked objectives/need.</summary>
	public class ResourceLaneAssignmentView
	{
		public string ResourceAssignmentId { get; set; }

		public string CommandStructureNodeId { get; set; }

		public string LaneName { get; set; }

		/// <summary>Maps to <see cref="CommandNodeType"/>.</summary>
		public int NodeType { get; set; }

		public string Color { get; set; }

		public DateTime AssignedOn { get; set; }

		public IncidentContactInfo PrimaryLead { get; set; }

		public IncidentContactInfo SecondaryLead { get; set; }

		/// <summary>The lane's primary linked objective, resolved (null when not set).</summary>
		public TacticalObjective PrimaryObjective { get; set; }

		/// <summary>The lane's secondary linked objective, resolved (null when not set).</summary>
		public TacticalObjective SecondaryObjective { get; set; }

		/// <summary>The need this lane is fulfilling, resolved (null when not set).</summary>
		public IncidentNeed LinkedNeed { get; set; }
	}
}
