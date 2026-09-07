using System;
using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.Records
{
	// Field Records for the four operational apps (RMS plan RMS-1D). Every filter is applied server-side from the
	// authenticated principal; these inputs narrow what comes back, they never widen it.

	public class FieldRecordContextInput
	{
		public int? CallId { get; set; }
		public int? UnitId { get; set; }
		public int? GroupId { get; set; }
		public string CommandRole { get; set; }
		public int? ContactId { get; set; }

		public FieldRecordContext ToContext() => new FieldRecordContext { CallId = CallId, UnitId = UnitId, GroupId = GroupId, CommandRole = CommandRole, ContactId = ContactId };
	}

	public class FieldRecordCatalogInput
	{
		/// <summary><see cref="RmsOriginClient"/>: 2 Responder, 3 Unit, 4 IncidentCommand, 5 Dispatch.</summary>
		public int? OriginClient { get; set; }
		public string AppVersion { get; set; }
		/// <summary>records.v1 / records.v1b / records.v1c; an unknown value is treated as the oldest.</summary>
		public string ClientCapability { get; set; }
		public FieldRecordContextInput Context { get; set; }
	}

	public class FieldRecordPrefillInput : FieldRecordCatalogInput
	{
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
	}

	public class FieldRecordSyncInput : FieldRecordCatalogInput
	{
		public long Since { get; set; }
		public string SinceId { get; set; }
		public string ScopeStamp { get; set; }
		public int Take { get; set; } = 200;
		public bool IncludeCatalog { get; set; } = true;
	}

	public class FieldRecordPreflightResult : StandardApiResponseV4Base
	{
		public FieldRecordPreflightData Data { get; set; }
	}

	public class FieldRecordPreflightData
	{
		public string ContractVersion { get; set; }
		public string SyncContractVersion { get; set; }
		public string OriginClient { get; set; }
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public bool ModuleEnabled { get; set; }
		public bool RecordsUsable { get; set; }
		public bool AppEnabled { get; set; }
		public string MinimumAppVersion { get; set; }
		public string AppVersion { get; set; }
		public string ClientCapability { get; set; }
		public string ProtectionState { get; set; }
		public long ServerTimestampMs { get; set; }
	}

	public class FieldRecordCatalogResult : StandardApiResponseV4Base
	{
		public FieldRecordCatalogData Data { get; set; }
	}

	public class FieldRecordCatalogData
	{
		public string ContractVersion { get; set; }
		public string OriginClient { get; set; }
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public string ContextKind { get; set; }
		public bool ContextVerified { get; set; }
		public string ProtectionState { get; set; }
		public string ScopeStamp { get; set; }
		public List<FieldRecordCatalogEntry> Definitions { get; set; } = new List<FieldRecordCatalogEntry>();
		public List<FieldRecordCatalogExclusion> Exclusions { get; set; } = new List<FieldRecordCatalogExclusion>();
		public long ServerTimestampMs { get; set; }
	}

	public class FieldRecordPrefillResult : StandardApiResponseV4Base
	{
		public FieldRecordPrefillData Data { get; set; }
	}

	public class FieldRecordPrefillData
	{
		public string ContractVersion { get; set; }
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public int PrefillVersion { get; set; }
		public int? CallId { get; set; }
		public int? UnitId { get; set; }
		public int? StationGroupId { get; set; }
		public List<FieldRecordPrefillValueData> Values { get; set; } = new List<FieldRecordPrefillValueData>();
		public List<FieldRecordPrefillProvenance> Provenance { get; set; } = new List<FieldRecordPrefillProvenance>();
		public List<string> SuggestedParticipantUserIds { get; set; } = new List<string>();
		public List<int> SuggestedUnitIds { get; set; } = new List<int>();
		public DateTime CalculatedOn { get; set; }
	}

	public class FieldRecordPrefillValueData
	{
		public string SectionKey { get; set; }
		public string FieldKey { get; set; }
		public string Value { get; set; }
		public string ReferenceType { get; set; }
		public string ReferenceId { get; set; }
	}

	public class FieldRecordSyncResult : StandardApiResponseV4Base
	{
		public FieldRecordSyncData Data { get; set; }
	}

	public class FieldRecordSyncData
	{
		public string ContractVersion { get; set; }
		public bool Ok { get; set; }
		public List<string> Reasons { get; set; } = new List<string>();
		public string ScopeStamp { get; set; }
		public bool ResetRequired { get; set; }
		public long Since { get; set; }
		public long ServerTimestampMs { get; set; }
		public string ServerCursorId { get; set; }
		public bool HasMore { get; set; }
		public FieldRecordCatalogData Catalog { get; set; }
		public List<RecordSummaryData> Records { get; set; } = new List<RecordSummaryData>();
		public List<string> Tombstones { get; set; } = new List<string>();
		public List<RecordSummaryData> Drafts { get; set; } = new List<RecordSummaryData>();
		public List<FieldRecordAssignmentData> Assignments { get; set; } = new List<FieldRecordAssignmentData>();
	}

	public class FieldRecordAssignmentsResult : StandardApiResponseV4Base
	{
		public List<FieldRecordAssignmentData> Data { get; set; } = new List<FieldRecordAssignmentData>();
	}

	public class FieldRecordAssignmentResult : StandardApiResponseV4Base
	{
		public FieldRecordAssignmentData Data { get; set; }
	}

	public class FieldRecordAssignmentData
	{
		public string AssignmentId { get; set; }
		public string RecordId { get; set; }
		public string AssigneeKind { get; set; }
		public string AssigneeUserId { get; set; }
		public int? AssigneeUnitId { get; set; }
		public int? AssigneeGroupId { get; set; }
		public string AssigneeRole { get; set; }
		public string Purpose { get; set; }
		public string Note { get; set; }
		public DateTime? DueOn { get; set; }
		public string State { get; set; }
		public DateTime? AcknowledgedOn { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string OriginClient { get; set; }
		public DateTime CreatedOn { get; set; }
		public string CreatedByUserId { get; set; }
		public long RowVersion { get; set; }
	}

	public class FieldRecordAssignInput
	{
		public string RecordId { get; set; }
		/// <summary><see cref="RmsWorkAssigneeKind"/>: 1 Person, 2 Unit, 3 Group, 4 CommandRole, 5 DispatchRole.</summary>
		public int AssigneeKind { get; set; } = 1;
		public string AssigneeUserId { get; set; }
		public int? AssigneeUnitId { get; set; }
		public int? AssigneeGroupId { get; set; }
		public string AssigneeRole { get; set; }
		public string Purpose { get; set; }
		public string Note { get; set; }
		public DateTime? DueOn { get; set; }
		public FieldRecordContextInput Context { get; set; }
		public int? OriginClient { get; set; }
	}

	/// <summary>A batch of safe rollout datapoints from one app (RMS plan RMS-1D). Counts and codes only.</summary>
	public class FieldRecordTelemetryInput
	{
		public int? OriginClient { get; set; }
		public string AppVersion { get; set; }
		public string ClientCapability { get; set; }
		public List<FieldRecordTelemetryEventInput> Events { get; set; } = new List<FieldRecordTelemetryEventInput>();
	}

	public class FieldRecordTelemetryEventInput
	{
		/// <summary>One of RmsFieldRolloutEventTypes; anything else is dropped rather than stored.</summary>
		public string EventType { get; set; }
		/// <summary>"ok" or the coded refusal/conflict the client was given.</summary>
		public string Outcome { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public string RecordId { get; set; }
		public long? DurationMs { get; set; }
		public int? ItemCount { get; set; }
		public DateTime? OccurredOn { get; set; }
	}

	public class FieldRecordTelemetryResult : StandardApiResponseV4Base
	{
		public FieldRecordTelemetryData Data { get; set; }
	}

	public class FieldRecordTelemetryData
	{
		/// <summary>How many events were kept after validation and truncation.</summary>
		public int Accepted { get; set; }
	}

	public class FieldRecordRolloutResult : StandardApiResponseV4Base
	{
		public RecordsFieldRollout Data { get; set; }
	}

	public class FieldRecordAssignmentCommandInput
	{
		public string AssignmentId { get; set; }
		public long? RowVersion { get; set; }
		public string Reason { get; set; }
		public FieldRecordContextInput Context { get; set; }
		public int? OriginClient { get; set; }
	}
}
