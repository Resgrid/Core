using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public enum ModerationItemType
	{
		ChatMessage = 0,
		Message = 1,
		CallNote = 2,
		CallImage = 3
	}

	public enum ModerationReason
	{
		Other = 0,
		Inappropriate = 1,
		Harassment = 2,
		Spam = 3,
		SensitiveInformation = 4,
		PolicyViolation = 5
	}

	public enum ModerationRequestStatus
	{
		Pending = 0,
		Completed = 1
	}

	public enum ModerationDisposition
	{
		None = 0,
		NoAction = 1,
		ContentRemoved = 2
	}

	public enum ModerationActionType
	{
		ReportSubmitted = 0,
		RequestReopened = 1,
		CompletedNoAction = 2,
		ContentRemoved = 3,
		EvidenceDownloaded = 4
	}

	/// <summary>
	/// One permanent moderation case per department/content item. The original content and metadata are
	/// captured when the first report is submitted and are never refreshed from the live item.
	/// </summary>
	public class ModerationRequest : IEntity
	{
		public string ModerationRequestId { get; set; }
		public int DepartmentId { get; set; }
		public int ItemType { get; set; }
		public string ItemId { get; set; }
		public int? CallId { get; set; }
		public string ChatChannelId { get; set; }
		public string ContentAuthorUserId { get; set; }
		public int? ContentAuthorUnitId { get; set; }
		public DateTime? ContentCreatedOn { get; set; }
		public string OriginalSubject { get; set; }
		public string OriginalText { get; set; }
		public string OriginalFileName { get; set; }
		public string OriginalContentType { get; set; }

		[JsonIgnore]
		public byte[] OriginalContent { get; set; }

		public string OriginalMetadataJson { get; set; }
		public int Status { get; set; }
		public int Disposition { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string CompletedByUserId { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string AdminNote { get; set; }

		[NotMapped]
		public List<ModerationReport> Reports { get; set; } = new List<ModerationReport>();

		[NotMapped]
		public List<ModerationAction> Actions { get; set; } = new List<ModerationAction>();

		[NotMapped]
		public string TableName => "ModerationRequests";

		[NotMapped]
		public string IdName => "ModerationRequestId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ModerationRequestId; }
			set { ModerationRequestId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[]
		{
			"IdValue", "IdType", "TableName", "IdName", "Reports", "Actions"
		};
	}

	/// <summary>A reporter's flag within a shared moderation request.</summary>
	public class ModerationReport : IEntity
	{
		public string ModerationReportId { get; set; }
		public string ModerationRequestId { get; set; }
		public int DepartmentId { get; set; }
		public string ReportedByUserId { get; set; }
		public int? ReporterGroupId { get; set; }
		public int Reason { get; set; }
		public string Note { get; set; }
		public DateTime ReportedOn { get; set; }

		[NotMapped]
		public string TableName => "ModerationReports";

		[NotMapped]
		public string IdName => "ModerationReportId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ModerationReportId; }
			set { ModerationReportId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Immutable moderation audit entry. The first ReportSubmitted action holds the original evidence as
	/// well as its request snapshot so the audit trail survives removal of the live content.
	/// </summary>
	public class ModerationAction : IEntity
	{
		public string ModerationActionId { get; set; }
		public string ModerationRequestId { get; set; }
		public int DepartmentId { get; set; }
		public int ActionType { get; set; }
		public string PerformedByUserId { get; set; }
		public DateTime PerformedOn { get; set; }
		public string Note { get; set; }
		public int? PreviousStatus { get; set; }
		public int? NewStatus { get; set; }
		public string ActorRole { get; set; }
		public string IpAddress { get; set; }
		public string UserAgent { get; set; }
		public string TraceId { get; set; }
		public string ServerName { get; set; }
		public string DetailsJson { get; set; }
		public string EvidenceText { get; set; }

		[JsonIgnore]
		public byte[] EvidenceContent { get; set; }

		public string EvidenceMetadataJson { get; set; }

		[NotMapped]
		public string TableName => "ModerationActions";

		[NotMapped]
		public string IdName => "ModerationActionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ModerationActionId; }
			set { ModerationActionId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	public class ModerationSearchCriteria
	{
		public ModerationRequestStatus? Status { get; set; }
		public ModerationItemType? ItemType { get; set; }
		public string ContentAuthorUserId { get; set; }
		public string ReportedByUserId { get; set; }
		public DateTime? From { get; set; }
		public DateTime? To { get; set; }
		public int Page { get; set; }
		public int PageSize { get; set; }
	}
}
