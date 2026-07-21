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
