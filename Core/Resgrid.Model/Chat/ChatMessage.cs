using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A chat message. Bodies are immutable for audit: edits and moderator/sender deletes preserve the
	/// prior body in <see cref="ChatMessageEdit"/> and deletes are tombstones (DeletedOn set, body cleared)
	/// until the retention purge removes the row entirely.
	/// </summary>
	public class ChatMessage : IEntity
	{
		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		/// <summary>Denormalized for retention purge and export scoping.</summary>
		public int DepartmentId { get; set; }

		/// <summary>Per-channel monotonic sequence allocated atomically from ChatChannels.LastMessageSeq.</summary>
		public long MessageSeq { get; set; }

		/// <summary>Maps to <see cref="ChatParticipantType"/>.</summary>
		public int SenderParticipantType { get; set; }

		/// <summary>
		/// The human behind the message. Always populated for audit — even when sending as a Unit or as
		/// the Incident Commander. Null only for Bot messages.
		/// </summary>
		public string SenderUserId { get; set; }

		public int? SenderUnitId { get; set; }

		/// <summary>Display identity snapshot at send time, e.g. "Engine 6" or "Incident Commander (J. Smith)".</summary>
		public string SenderDisplayName { get; set; }

		public string Body { get; set; }

		/// <summary>Maps to <see cref="ChatMessageType"/>.</summary>
		public int MessageType { get; set; }

		/// <summary>Maps to <see cref="ChatMessagePriority"/>. Urgent provisions acknowledgment rows.</summary>
		public int Priority { get; set; }

		/// <summary>Root message when this is a thread reply; null for top-level messages.</summary>
		public string ThreadRootMessageId { get; set; }

		/// <summary>Reply count maintained on thread roots for badge display.</summary>
		public int ThreadReplyCount { get; set; }

		public DateTime? LastThreadReplyOn { get; set; }

		/// <summary>Thread replies flagged to also appear in the main channel stream.</summary>
		public bool AlsoSendToChannel { get; set; }

		/// <summary>JSON payload for link previews, GIF url/dimensions, or shared location lat/lon/label.</summary>
		public string MetadataJson { get; set; }

		/// <summary>Client-supplied idempotency key so the mobile offline outbox can retry sends safely.</summary>
		public string ClientMessageId { get; set; }

		public DateTime SentOn { get; set; }

		public DateTime? EditedOn { get; set; }

		public DateTime? DeletedOn { get; set; }

		public string DeletedByUserId { get; set; }

		/// <summary>True when the tombstone was applied by a moderator rather than the sender.</summary>
		public bool IsModerated { get; set; }

		public DateTime? PinnedOn { get; set; }

		public string PinnedByUserId { get; set; }

		[NotMapped]
		public string TableName => "ChatMessages";

		[NotMapped]
		public string IdName => "ChatMessageId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageId; }
			set { ChatMessageId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Audit history row preserving a message body prior to an edit or delete.</summary>
	public class ChatMessageEdit : IEntity
	{
		public string ChatMessageEditId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		public string PriorBody { get; set; }

		/// <summary>Maps to <see cref="ChatMessageEditType"/>.</summary>
		public int EditType { get; set; }

		public string EditedByUserId { get; set; }

		public DateTime EditedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatMessageEdits";

		[NotMapped]
		public string IdName => "ChatMessageEditId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatMessageEditId; }
			set { ChatMessageEditId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A file/image attached to a chat message. Separate from the legacy Files table (which is bound to
	/// inbox Messages) so attachments carry channel/department scoping for auth checks and retention purge.
	/// BLOB-in-DB per existing storage convention.
	/// </summary>
	public class ChatAttachment : IEntity
	{
		public string ChatAttachmentId { get; set; }

		public string ChatMessageId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		public string FileName { get; set; }

		public string ContentType { get; set; }

		public long Size { get; set; }

		/// <summary>SHA-256 of Data for integrity/dedup checks.</summary>
		public string Sha256 { get; set; }

		[JsonIgnore]
		public byte[] Data { get; set; }

		[JsonIgnore]
		public byte[] ThumbnailData { get; set; }

		public string UploadedByUserId { get; set; }

		public DateTime UploadedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatAttachments";

		[NotMapped]
		public string IdName => "ChatAttachmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatAttachmentId; }
			set { ChatAttachmentId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
