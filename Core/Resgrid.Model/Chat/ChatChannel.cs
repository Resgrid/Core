using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// A realtime chat channel: DM, ad-hoc group, department/group default, custom permission-locked,
	/// incident (call/lane/command) or per-user chatbot conversation. Audience for implicit channel
	/// types (department, group, incident) is resolved at read time by ChatPermissionService; explicit
	/// membership rows exist only where required (see <see cref="ChatChannelMember"/>).
	/// </summary>
	[ProtoContract]
	public class ChatChannel : IEntity, IChangeTracked
	{
		[ProtoMember(1)]
		public string ChatChannelId { get; set; }

		[ProtoMember(2)]
		public int DepartmentId { get; set; }

		/// <summary>Maps to <see cref="ChatChannelType"/>.</summary>
		[ProtoMember(3)]
		public int ChannelType { get; set; }

		[ProtoMember(4)]
		public string Name { get; set; }

		[ProtoMember(5)]
		public string Topic { get; set; }

		[ProtoMember(6)]
		public string CreatedByUserId { get; set; }

		[ProtoMember(7)]
		public DateTime CreatedOn { get; set; }

		/// <summary>Anchor for GroupDefault channels (FK DepartmentGroups).</summary>
		[ProtoMember(8)]
		public int? GroupId { get; set; }

		/// <summary>Anchor for Incident/IncidentLane/IncidentCommand channels.</summary>
		[ProtoMember(9)]
		public int? CallId { get; set; }

		/// <summary>Anchor for IncidentCommand/IncidentLane channels (FK IncidentCommands).</summary>
		[ProtoMember(10)]
		public string IncidentCommandId { get; set; }

		/// <summary>Anchor for IncidentLane channels (FK CommandStructureNodes).</summary>
		[ProtoMember(11)]
		public string CommandStructureNodeId { get; set; }

		/// <summary>Anchor for Chatbot channels: the user this bot conversation belongs to.</summary>
		[ProtoMember(12)]
		public string OwnerUserId { get; set; }

		/// <summary>
		/// Normalized identity key for one-channel-per-identity dedup, unique per department when set.
		/// DMs use the sorted participant pair ("u:{idA}|u:{idB}", "u:{userId}|unit:{unitId}");
		/// UnitDispatch channels use "unitdispatch:{unitId}".
		/// </summary>
		[ProtoMember(13)]
		public string DmKey { get; set; }

		[ProtoMember(14)]
		public bool IsArchived { get; set; }

		[ProtoMember(15)]
		public DateTime? ArchivedOn { get; set; }

		/// <summary>Locked = only moderators can post; everyone with access can still read.</summary>
		[ProtoMember(16)]
		public bool IsLocked { get; set; }

		[ProtoMember(17)]
		public string LockedByUserId { get; set; }

		[ProtoMember(18)]
		public DateTime? LockedOn { get; set; }

		/// <summary>Per-channel monotonic message sequence high-water mark; allocated atomically on send.</summary>
		[ProtoMember(19)]
		public long LastMessageSeq { get; set; }

		[ProtoMember(20)]
		public DateTime? LastMessageOn { get; set; }

		/// <summary>Overrides the department retention policy for this channel when set (days; 0 = keep forever).</summary>
		[ProtoMember(21)]
		public int? RetentionOverrideDays { get; set; }

		[ProtoMember(22)]
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
