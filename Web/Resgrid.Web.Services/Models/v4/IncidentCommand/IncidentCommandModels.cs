using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

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

	public class UpdateCommandPostInput
	{
		public string IncidentCommandId { get; set; }
		public string Latitude { get; set; }
		public string Longitude { get; set; }
	}

	public class AddIncidentNoteInput
	{
		public string IncidentCommandId { get; set; }
		public int NoteType { get; set; }
		public int Visibility { get; set; }
		public string Title { get; set; }
		public string Body { get; set; }
		public decimal? ContainmentPercent { get; set; }
	}

	public class AddIncidentAttachmentInput
	{
		public string IncidentCommandId { get; set; }
		public int Visibility { get; set; }
		public string Description { get; set; }
		public IFormFile File { get; set; }
	}

	/// <summary>Input to move a resource assignment to a different node.</summary>
	public class MoveResourceInput
	{
		public string ResourceAssignmentId { get; set; }
		public string TargetNodeId { get; set; }
	}

	/// <summary>Input to update command-level details every resource should see.</summary>
	public class UpdateCommandDetailsInput
	{
		public string IncidentCommandId { get; set; }
		public System.DateTime? EstimatedEndOn { get; set; }
		public string ImportantInformation { get; set; }
	}

	/// <summary>Input to reopen a previously closed command, with the caller's reason for reopening.</summary>
	public class ReopenCommandInput
	{
		public string IncidentCommandId { get; set; }
		public string Reason { get; set; }
	}

	/// <summary>
	/// Input to update core incident metadata and the ICP/HQ, Staging, and Rehab locations. Null fields are
	/// left unchanged; empty strings clear. A location whose text is set while its coordinates are blank is
	/// geocoded server-side on save.
	/// </summary>
	public class UpdateCommandInfoInput
	{
		public string IncidentCommandId { get; set; }
		public string Name { get; set; }
		public System.DateTime? EstablishedOn { get; set; }
		public System.DateTime? EstimatedEndOn { get; set; }
		public bool ClearEstimatedEndOn { get; set; }
		public string ImportantInformation { get; set; }
		public int? IcsLevel { get; set; }
		public string CommandPostLocationText { get; set; }
		public string CommandPostLatitude { get; set; }
		public string CommandPostLongitude { get; set; }
		public string StagingLocationText { get; set; }
		public string StagingLatitude { get; set; }
		public string StagingLongitude { get; set; }
		public string RehabLocationText { get; set; }
		public string RehabLatitude { get; set; }
		public string RehabLongitude { get; set; }
	}

	/// <summary>Input to set an objective's progress percentage (0-100; 100 completes it).</summary>
	public class UpdateObjectiveProgressInput
	{
		public string TacticalObjectiveId { get; set; }
		public int ProgressPercent { get; set; }
	}

	/// <summary>Input to create/update the incident map's saved view (center + zoom) for consistent framing.</summary>
	public class UpdateMapViewInput
	{
		public string IncidentCommandId { get; set; }
		public string CenterLatitude { get; set; }
		public string CenterLongitude { get; set; }
		/// <summary>Map zoom level, 0-22.</summary>
		public string ZoomLevel { get; set; }
	}

	/// <summary>Optional close-out details when completing an objective.</summary>
	public class CompleteObjectiveInput
	{
		/// <summary>Maps to Resgrid.Model.TacticalObjectiveOutcome (Successful/Partial/Unsuccessful).</summary>
		public int Outcome { get; set; }
		/// <summary>Optional close-out note, recorded on the objective and the incident log.</summary>
		public string Note { get; set; }
	}

	/// <summary>Input to transition an incident need's fulfillment status.</summary>
	public class SetNeedStatusInput
	{
		public string IncidentNeedId { get; set; }
		/// <summary>Maps to Resgrid.Model.IncidentNeedStatus.</summary>
		public int Status { get; set; }
		/// <summary>New fulfilled quantity — may be lower than the current value (a fill got called off).</summary>
		public int? QuantityFulfilled { get; set; }
		/// <summary>Optional context recorded on the audit trail and incident log ("Engine 1 from mutual aid").</summary>
		public string Note { get; set; }
	}

	public class IncidentCommandResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommand Data { get; set; }
	}

	public class IncidentCommandBoardResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentCommandBoard Data { get; set; }
	}

	/// <summary>List-card summaries for the department's incident commands (active only or incl. closed).</summary>
	public class IncidentCommandSummariesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentCommandSummary> Data { get; set; } = new List<Resgrid.Model.IncidentCommandSummary>();
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

	public class IncidentNeedResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentNeed Data { get; set; }
	}

	public class IncidentNeedsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentNeed> Data { get; set; } = new List<Resgrid.Model.IncidentNeed>();
	}

	/// <summary>Input to create an Entity need: specific units/users/roles/groups requested by command.</summary>
	public class RequestNeedEntitiesInput
	{
		public string IncidentCommandId { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public List<NeedEntityInput> Entities { get; set; } = new List<NeedEntityInput>();
	}

	/// <summary>One requested entity: kind maps to Resgrid.Model.NeedEntityKind (Unit/User/Role/Group).</summary>
	public class NeedEntityInput
	{
		public int EntityKind { get; set; }
		public string EntityId { get; set; }
	}

	/// <summary>The requested entities under one Entity-category need.</summary>
	public class IncidentNeedEntitiesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentNeedEntity> Data { get; set; } = new List<Resgrid.Model.IncidentNeedEntity>();
	}

	/// <summary>Audit trail for one need's fulfillment changes (newest first).</summary>
	public class IncidentNeedUpdatesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentNeedUpdate> Data { get; set; } = new List<Resgrid.Model.IncidentNeedUpdate>();
	}

	/// <summary>Read-only incident view for a responder or unit: commander, timing, objectives, needs, notes, attachments, own lane assignment.</summary>
	public class ResourceIncidentViewResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ResourceIncidentView Data { get; set; }
	}

	public class IncidentTimerResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentTimer Data { get; set; }
	}

	public class IncidentMapAnnotationResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentMapAnnotation Data { get; set; }
	}

	/// <summary>A single named incident tactical map.</summary>
	public class IncidentMapResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentMap Data { get; set; }
	}

	/// <summary>The incident's named tactical maps.</summary>
	public class IncidentMapsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentMap> Data { get; set; } = new List<Resgrid.Model.IncidentMap>();
	}

	public class IncidentTimesReportResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentTimesReport Data { get; set; }
	}

	public class ResourceUtilizationReportResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.ResourceUtilizationReport Data { get; set; }
	}

	public class VoiceTransmissionLogResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.VoiceTransmissionLog Data { get; set; }
	}

	public class VoiceTransmissionLogsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.VoiceTransmissionLog> Data { get; set; } = new List<Resgrid.Model.VoiceTransmissionLog>();
	}

	/// <summary>Input logging one completed PTT transmission on an incident channel.</summary>
	public class LogTransmissionInput
	{
		public int CallId { get; set; }
		public string DepartmentVoiceChannelId { get; set; }
		public string StartedOn { get; set; }
		public string EndedOn { get; set; }
	}

	public class IncidentNoteResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentNote Data { get; set; }
	}

	public class IncidentNotesResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentNote> Data { get; set; } = new List<Resgrid.Model.IncidentNote>();
	}

	public class IncidentAttachmentResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentAttachment Data { get; set; }
	}

	public class IncidentAttachmentsResult : StandardApiResponseV4Base
	{
		public List<Resgrid.Model.IncidentAttachment> Data { get; set; } = new List<Resgrid.Model.IncidentAttachment>();
	}

	public class IncidentWeatherResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentWeather Data { get; set; }
	}

	public class PublicIncidentInformationResult : StandardApiResponseV4Base
	{
		public Resgrid.Model.IncidentPublicInformation Data { get; set; }
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
