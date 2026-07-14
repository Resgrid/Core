using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.IncidentCommand
{
	/// <summary>Input to establish command on a call.</summary>
	public class EstablishCommandInput
	{
		public int CallId { get; set; }
		public int? CommandDefinitionId { get; set; }
	}

	/// <summary>Input to transfer command to another user.</summary>
	public class TransferCommandInput
	{
		public string IncidentCommandId { get; set; }
		public string ToUserId { get; set; }
		public string Notes { get; set; }
	}

	/// <summary>Input to update the incident action plan.</summary>
	public class UpdateActionPlanInput
	{
		public string IncidentCommandId { get; set; }
		public string ActionPlan { get; set; }
	}

	/// <summary>Input to move a resource assignment to a different node.</summary>
	public class MoveResourceInput
	{
		public string ResourceAssignmentId { get; set; }
		public string TargetNodeId { get; set; }
	}

	public class IncidentCommandResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommand Data { get; set; }
	}

	public class IncidentCommandBoardResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommandBoard Data { get; set; }
	}

	public class CommandTransferResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.CommandTransfer Data { get; set; }
	}

	public class CommandNodeResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.CommandStructureNode Data { get; set; }
	}

	public class ResourceAssignmentResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ResourceAssignment Data { get; set; }

		/// <summary>
		/// Human-readable requirements notice. On Status=failure: why the assignment was rejected (forced
		/// lane requirements not met). On Status=success: an advisory warning when the resource doesn't
		/// meet the lane's non-forced requirements (also stamped on Data.RequirementsWarning/-Message).
		/// </summary>
		public string Message { get; set; }
	}

	public class TacticalObjectiveResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.TacticalObjective Data { get; set; }
	}

	public class IncidentTimerResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentTimer Data { get; set; }
	}

	public class IncidentMapAnnotationResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentMapAnnotation Data { get; set; }
	}

	public class CommandTimelineResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.CommandLogEntry> Data { get; set; } = new List<Resgrid.Model.CommandLogEntry>();
	}

	/// <summary>Simple boolean action result (delete/release operations).</summary>
	public class IncidentCommandActionResult : StandardApiResponseV4Base
	{
		public bool Data { get; set; }
	}

	/// <summary>Per-person accountability / PAR status for the incident.</summary>
	public class CommandAccountabilityResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.PersonnelCallCheckInStatus> Data { get; set; } = new List<Resgrid.Model.PersonnelCallCheckInStatus>();
	}

	/// <summary>User ids newly flagged Critical (PAR overdue) by an accountability sweep — same shape used by the manual endpoint and the recurring PAR worker.</summary>
	public class EvaluateAccountabilityResult : StandardApiResponseV4Base
	{
		public List<string> Data { get; set; } = new List<string>();
	}

	/// <summary>Input to create an on-demand incident tactical voice channel.</summary>
	public class CreateIncidentChannelInput
	{
		public int CallId { get; set; }
		public string Name { get; set; }
	}

	/// <summary>Result wrapper for a single incident voice channel.</summary>
	public class IncidentVoiceChannelResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.DepartmentVoiceChannel Data { get; set; }
	}

	/// <summary>Result wrapper for a collection of incident voice channels.</summary>
	public class IncidentVoiceChannelsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.DepartmentVoiceChannel> Data { get; set; } = new List<Resgrid.Model.DepartmentVoiceChannel>();
	}

	/// <summary>Assignable resources (own + mutual-aid) for the incident resource picker.</summary>
	public class MutualAidResourcesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.AssignableResource> Data { get; set; } = new List<Resgrid.Model.AssignableResource>();
	}

	/// <summary>Input to add an ad-hoc person to a unit roster.</summary>
	public class AssignPersonnelToUnitInput
	{
		public string IncidentAdHocPersonnelId { get; set; }
		public int RidingResourceKind { get; set; }
		public string RidingResourceId { get; set; }
	}

	/// <summary>Input to form an ad-hoc unit from on-scene ad-hoc personnel.</summary>
	public class FormUnitInput
	{
		public int CallId { get; set; }
		public string Name { get; set; }
		public string Type { get; set; }
		public int? UnitTypeId { get; set; }
		public string ExternalAgencyName { get; set; }
		public List<string> AdHocPersonnelIds { get; set; } = new List<string>();
	}

	public class AdHocUnitResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentAdHocUnit Data { get; set; }
	}

	public class AdHocUnitsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentAdHocUnit> Data { get; set; } = new List<Resgrid.Model.IncidentAdHocUnit>();
	}

	public class AdHocPersonnelResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentAdHocPersonnel Data { get; set; }
	}

	public class AdHocPersonnelListResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentAdHocPersonnel> Data { get; set; } = new List<Resgrid.Model.IncidentAdHocPersonnel>();
	}

	public class IncidentRoleResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentRoleAssignment Data { get; set; }
	}

	public class IncidentRolesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentRoleAssignment> Data { get; set; } = new List<Resgrid.Model.IncidentRoleAssignment>();
	}

	/// <summary>The current user's effective incident capabilities (raw flags value + granted names).</summary>
	public class IncidentCapabilitiesResult : StandardApiResponseV4Base
	{
		public int Value { get; set; }
		public List<string> Capabilities { get; set; } = new List<string>();
	}

	public class IncidentReportSummaryResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentReportSummary Data { get; set; }
	}

	public class IncidentAfterActionReportResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentAfterActionReport Data { get; set; }
	}
}
