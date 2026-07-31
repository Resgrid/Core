using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A realtime chat channel: DM, ad-hoc group, department/group default, custom permission-locked,
	/// incident (call/lane/command) or per-user chatbot conversation. Audience for implicit channel
	/// types (department, group, incident) is resolved at read time by ChatPermissionService; explicit
	/// membership rows exist only where required (see <see cref="ChatChannelMember"/>).
	/// </summary>
	public class ChatChannel : IEntity, IChangeTracked
	{
		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatChannelType"/>.</summary>
		public int ChannelType { get; set; }

		public string Name { get; set; }

		public string Topic { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>Anchor for GroupDefault channels (FK DepartmentGroups).</summary>
		public int? GroupId { get; set; }

		/// <summary>Anchor for Incident/IncidentLane/IncidentCommand channels.</summary>
		public int? CallId { get; set; }

		/// <summary>Anchor for IncidentCommand/IncidentLane channels (FK IncidentCommands).</summary>
		public string IncidentCommandId { get; set; }

		/// <summary>Anchor for IncidentLane channels (FK CommandStructureNodes).</summary>
		public string CommandStructureNodeId { get; set; }

		/// <summary>Anchor for Chatbot channels: the user this bot conversation belongs to.</summary>
		public string OwnerUserId { get; set; }

		/// <summary>
		/// Normalized participant identity key for DM dedup, unique per department when set.
		/// Sorted, e.g. "u:{idA}|u:{idB}" or "u:{userId}|unit:{unitId}".
		/// </summary>
		public string DmKey { get; set; }

		public bool IsArchived { get; set; }

		public DateTime? ArchivedOn { get; set; }

		/// <summary>Locked = only moderators can post; everyone with access can still read.</summary>
		public bool IsLocked { get; set; }

		public string LockedByUserId { get; set; }

		public DateTime? LockedOn { get; set; }

		/// <summary>Per-channel monotonic message sequence high-water mark; allocated atomically on send.</summary>
		public long LastMessageSeq { get; set; }

		public DateTime? LastMessageOn { get; set; }

		/// <summary>Overrides the department retention policy for this channel when set (days; 0 = keep forever).</summary>
		public int? RetentionOverrideDays { get; set; }

		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatChannels";

		[NotMapped]
		public string IdName => "ChatChannelId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatChannelId; }
			set { ChatChannelId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Access rule for a CustomLocked channel. Rules are OR-evaluated: a user matching any rule
	/// (group membership, personnel role, or explicit user) can access the channel.
	/// </summary>
	public class ChatChannelAccessRule : IEntity
	{
		public string ChatChannelAccessRuleId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatAccessRuleType"/>.</summary>
		public int RuleType { get; set; }

		public int? GroupId { get; set; }

		public int? PersonnelRoleId { get; set; }

		public string UserId { get; set; }

		public string AddedByUserId { get; set; }

		public DateTime AddedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatChannelAccessRules";

		[NotMapped]
		public string IdName => "ChatChannelAccessRuleId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatChannelAccessRuleId; }
			set { ChatChannelAccessRuleId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// A participant's per-channel state. Explicit membership for DM/AdHocGroup/CustomLocked(user rules)/Chatbot
	/// channels; created lazily for implicit-audience channels (department/group/incident) the first time the
	/// participant reads the channel or changes a preference, purely to hold read pointers and preferences.
	/// Polymorphic: a person (UserId), a unit-shared identity (UnitId), or the bot.
	/// </summary>
	public class ChatChannelMember : IEntity, IChangeTracked
	{
		public string ChatChannelMemberId { get; set; }

		public string ChatChannelId { get; set; }

		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatParticipantType"/>.</summary>
		public int ParticipantType { get; set; }

		public string UserId { get; set; }

		public int? UnitId { get; set; }

		/// <summary>Display identity override, e.g. "Incident Commander" or "Resgrid Assistant".</summary>
		public string DisplayNameOverride { get; set; }

		public bool IsModerator { get; set; }

		public DateTime JoinedOn { get; set; }

		public string AddedByUserId { get; set; }

		/// <summary>Set when the participant left or was removed; row kept for history.</summary>
		public DateTime? RemovedOn { get; set; }

		/// <summary>Highest MessageSeq this participant has read (Slack-style pointer; no per-message receipt rows).</summary>
		public long LastReadSeq { get; set; }

		public DateTime? LastReadOn { get; set; }

		/// <summary>Highest MessageSeq delivered to any of this participant's devices.</summary>
		public long LastDeliveredSeq { get; set; }

		/// <summary>Admin mute: participant cannot post until this UTC time (null = not muted).</summary>
		public DateTime? MutedUntil { get; set; }

		public bool IsBanned { get; set; }

		public DateTime? BannedOn { get; set; }

		public string BannedByUserId { get; set; }

		/// <summary>Maps to <see cref="ChatNotificationPreference"/>.</summary>
		public int NotificationPreference { get; set; }

		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "ChatChannelMembers";

		[NotMapped]
		public string IdName => "ChatChannelMemberId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ChatChannelMemberId; }
			set { ChatChannelMemberId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
