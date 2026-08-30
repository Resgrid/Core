using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>A user report ("flag") of a chat message for moderator review.</summary>
	public class ChatMessageFlag : IEntity
	{
		public string ChatMessageFlagId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		public string FlaggedByUserId { get; set; }

		/// <summary>Maps to <see cref="ChatFlagReason"/>.</summary>
		public int Reason { get; set; }

		public string Note { get; set; }

		public DateTime FlaggedOn { get; set; }

		/// <summary>Maps to <see cref="ChatFlagStatus"/>.</summary>
		public int Status { get; set; }

		public string ReviewedByUserId { get; set; }

		public DateTime? ReviewedOn { get; set; }

		public string ResolutionNote { get; set; }

		/// <summary>ADP row marker (M0139): true once this row's cataloged values carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		public string TableName => "ChatMessageFlags";

		[NotMapped]
		public string IdName => "ChatMessageFlagId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageFlagId; }
			set { ChatMessageFlagId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Immutable audit record of a moderation action (delete/mute/ban/lock/pin/flag-resolve/export).
	/// Also mirrored to the department AuditLog via IAuditService.
	/// </summary>
	public class ChatModerationAction : IEntity
	{
		public string ChatModerationActionId { get; set; }

		public int DepartmentId { get; set; }

		public string ChatChannelId { get; set; }

		public string ChatMessageId { get; set; }

		public string TargetUserId { get; set; }

		public int? TargetUnitId { get; set; }

		/// <summary>Maps to <see cref="ChatModerationActionType"/>.</summary>
		public int ActionType { get; set; }

		public string PerformedByUserId { get; set; }

		public DateTime PerformedOn { get; set; }

		public string Reason { get; set; }

		public string DetailsJson { get; set; }

		/// <summary>ADP row marker (M0139): true once this row's cataloged values carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		public string TableName => "ChatModerationActions";

		[NotMapped]
		public string IdName => "ChatModerationActionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatModerationActionId; }
			set { ChatModerationActionId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Per-department chat settings: retention policy and content toggles.</summary>
	public class ChatDepartmentSetting : IEntity
	{
		public string ChatDepartmentSettingId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Days to retain messages (0 = keep forever). Channel RetentionOverrideDays wins when set.</summary>
		public int RetentionDays { get; set; }

		public bool AllowImages { get; set; }

		public bool AllowGifs { get; set; }

		public bool AllowLocationSharing { get; set; }

		/// <summary>When true (default), Urgent messages notify even members who muted the channel.</summary>
		public bool UrgentOverridesMute { get; set; }

		public int MaxAttachmentSizeMb { get; set; }

		public bool ChatbotEnabled { get; set; }

		/// <summary>Per-department opt-in for the chatbot's conversational LLM fallback (also requires ChatConfig.ChatbotFallbackEnabled).</summary>
		public bool ChatbotFallbackEnabled { get; set; }

		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatDepartmentSettings";

		[NotMapped]
		public string IdName => "ChatDepartmentSettingId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatDepartmentSettingId; }
			set { ChatDepartmentSettingId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A queued chat transcript export job (records requests / FOIA); result stored as a ZIP/JSON/CSV blob.</summary>
	public class ChatExport : IEntity
	{
		public string ChatExportId { get; set; }

		public int DepartmentId { get; set; }

		public string RequestedByUserId { get; set; }

		public DateTime RequestedOn { get; set; }

		/// <summary>Limit export to one channel; null = all department channels.</summary>
		public string ChatChannelId { get; set; }

		public DateTime? StartDate { get; set; }

		public DateTime? EndDate { get; set; }

		/// <summary>Maps to <see cref="ChatExportFormat"/>.</summary>
		public int Format { get; set; }

		/// <summary>Maps to <see cref="ChatExportStatus"/>.</summary>
		public int Status { get; set; }

		public DateTime? CompletedOn { get; set; }

		[JsonIgnore]
		public byte[] Data { get; set; }

		public string Error { get; set; }

		/// <summary>ADP row marker (M0139): true once this row's cataloged values carry rgdp envelopes.</summary>
		public bool IsProtected { get; set; }

		[NotMapped]
		public string TableName => "ChatExports";

		[NotMapped]
		public string IdName => "ChatExportId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatExportId; }
			set { ChatExportId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
