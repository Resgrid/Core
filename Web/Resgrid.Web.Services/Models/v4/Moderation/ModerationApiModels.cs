using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Moderation
{
	public class GetModerationRequestsResult : StandardApiResponseV4Base
	{
		public List<ModerationRequestResultData> Data { get; set; } = new List<ModerationRequestResultData>();
	}

	public class GetModerationRequestResult : StandardApiResponseV4Base
	{
		public ModerationRequestResultData Data { get; set; }
	}

	public class ModerationActionResult : StandardApiResponseV4Base
	{
		public bool Success { get; set; }
		public ModerationRequestResultData Data { get; set; }
	}

	public class ModerationRequestResultData
	{
		public string ModerationRequestId { get; set; }
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
		public bool HasOriginalContent { get; set; }
		public string OriginalMetadataJson { get; set; }
		public int Status { get; set; }
		public int Disposition { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime ModifiedOn { get; set; }
		public string CompletedByUserId { get; set; }
		public DateTime? CompletedOn { get; set; }
		public string AdminNote { get; set; }
		public List<ModerationReportResultData> Reports { get; set; } = new List<ModerationReportResultData>();
		public List<ModerationActionResultData> Actions { get; set; } = new List<ModerationActionResultData>();
	}

	public class ModerationReportResultData
	{
		public string ModerationReportId { get; set; }
		public string ReportedByUserId { get; set; }
		public int? ReporterGroupId { get; set; }
		public int Reason { get; set; }
		public string Note { get; set; }
		public DateTime ReportedOn { get; set; }
	}

	public class ModerationActionResultData
	{
		public string ModerationActionId { get; set; }
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
		public bool HasEvidence { get; set; }
	}

	public class FlagModerationInput
	{
		[Range(0, 3)]
		public int ItemType { get; set; }

		[Required]
		[StringLength(128)]
		public string ItemId { get; set; }

		[Range(0, 5)]
		public int Reason { get; set; }

		[StringLength(4000)]
		public string Note { get; set; }
	}

	public class CompleteModerationInput
	{
		[Range(1, 2)]
		public int Disposition { get; set; }

		[StringLength(4000)]
		public string AdminNote { get; set; }
	}
}
