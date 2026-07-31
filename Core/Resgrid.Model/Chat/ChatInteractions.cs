using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>An emoji reaction on a chat message; one row per (message, participant, emoji).</summary>
	public class ChatMessageReaction : IEntity
	{
		public string ChatMessageReactionId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatParticipantType"/>.</summary>
		public int ParticipantType { get; set; }

		public string UserId { get; set; }

		public int? UnitId { get; set; }

		/// <summary>Unicode emoji string (e.g. "👍").</summary>
		public string Emoji { get; set; }

		public DateTime ReactedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatMessageReactions";

		[NotMapped]
		public string IdName => "ChatMessageReactionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageReactionId; }
			set { ChatMessageReactionId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>An @mention inside a chat message; drives mention notifications and "mentions of me" queries.</summary>
	public class ChatMessageMention : IEntity
	{
		public string ChatMessageMentionId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatMentionType"/>.</summary>
		public int MentionType { get; set; }

		public string TargetUserId { get; set; }

		public int? TargetUnitId { get; set; }

		public int? TargetRoleId { get; set; }

		public int? TargetGroupId { get; set; }

		[NotMapped]
		public string TableName => "ChatMessageMentions";

		[NotMapped]
		public string IdName => "ChatMessageMentionId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageMentionId; }
			set { ChatMessageMentionId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Required acknowledgment of an urgent message. Rows are provisioned per user for the resolved
	/// audience at send time; unit audiences expand to the unit's roster and any crew member's ack
	/// satisfies the unit.
	/// </summary>
	public class ChatMessageAck : IEntity
	{
		public string ChatMessageAckId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		public string UserId { get; set; }

		/// <summary>The unit this ack requirement was expanded from, when the audience member was a unit.</summary>
		public int? UnitId { get; set; }

		public DateTime RequiredOn { get; set; }

		public DateTime? AcknowledgedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatMessageAcks";

		[NotMapped]
		public string IdName => "ChatMessageAckId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageAckId; }
			set { ChatMessageAckId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
