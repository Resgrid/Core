using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>What a content set carries (ContentSets.Kind). Persisted: append only.</summary>
	public enum ContentSetKinds
	{
		/// <summary>Protocols P5 regional publisher/subscriber: a publisher's protocols, subscribed over a link that shares Protocols.</summary>
		Protocols = 0,

		/// <summary>Training T5 shared course sets: LMS courses and baseline trainings, subscribed over a link that shares Training.</summary>
		Training = 1
	}

	/// <summary>One item of a set (ContentSetItems.ItemKind, ContentSetCopies.ItemKind). Persisted: append only.</summary>
	public enum ContentSetItemKinds
	{
		/// <summary>A published protocol (DispatchProtocols). Baseline: free for every subscriber.</summary>
		Protocol = 0,

		/// <summary>An LMS course (LmsCourses). LMS-type: needs the subscriber's own LMS add-on or the publisher's sponsorship.</summary>
		LmsCourse = 1,

		/// <summary>A baseline training (Trainings). Baseline: free for every subscriber.</summary>
		Training = 2,

		/// <summary>
		/// An LMS learning path (LmsPaths). LMS-type. Its course and training steps must be items of the same set (or Academy courses): the
		/// subscriber's path points at its own copies of them, so the path is copied after them.
		/// </summary>
		LmsPath = 3
	}

	/// <summary>
	/// How much of a set item a subscriber may change (ContentSetItems.LockMode). Persisted: append only. "Locked" content is the publisher's
	/// (clinical content): a subscriber can only defer its updates up to the publisher's deadline. "Customizable blocks" lets the subscriber
	/// replace the blocks the publisher named (local phone numbers, destinations, resources) and add local blocks at the end; the rest stays
	/// locked. "Open" lets the subscriber change any block and decline updates.
	/// </summary>
	public enum ContentSetLockModes
	{
		Locked = 0,
		CustomizableBlocks = 1,
		Open = 2
	}

	/// <summary>ContentSetSubscriptions.Status. Persisted: append only.</summary>
	public enum ContentSetSubscriptionStatuses
	{
		/// <summary>Updates arrive and are applied.</summary>
		Active = 0,

		/// <summary>The link was disabled or the publisher stopped sharing the kind: nothing new arrives; copies stay as they are.</summary>
		Suspended = 1,

		/// <summary>Unsubscribed, ended by the publisher or the set was removed: the copies became the subscriber's own content.</summary>
		Ended = 2
	}

	/// <summary>Why a subscription ended or was suspended (ContentSetSubscriptions.EndReason). Persisted: append only.</summary>
	public enum ContentSetEndReasons
	{
		None = 0,
		Unsubscribed = 1,
		RemovedByPublisher = 2,
		LinkRevoked = 3,
		SetArchived = 4
	}

	/// <summary>What an inbox row brings (ContentSetUpdates.UpdateKind). Persisted: append only.</summary>
	public enum ContentSetUpdateKinds
	{
		/// <summary>The publisher put a new version of an item the subscriber already has into force.</summary>
		NewVersion = 0,

		/// <summary>The publisher added an item to the set after the subscriber subscribed (or the subscriber can now run an LMS item).</summary>
		NewItem = 1
	}

	/// <summary>An inbox row's state (ContentSetUpdates.Status). Persisted: append only.</summary>
	public enum ContentSetUpdateStatuses
	{
		/// <summary>Waiting for the subscriber's decision.</summary>
		Pending = 0,

		/// <summary>Accepted; it goes into force at <see cref="ContentSetUpdate.ApplyOn"/> (the department's update time, never mid-shift).</summary>
		Scheduled = 1,

		Applied = 2,

		/// <summary>Deferred to <see cref="ContentSetUpdate.DeferredUntil"/> (never past the deadline of a locked item).</summary>
		Deferred = 3,

		/// <summary>Declined (open items only); the subscriber stays on its version.</summary>
		Declined = 4,

		/// <summary>A newer version of the same item arrived before this one was applied.</summary>
		Superseded = 5,

		/// <summary>The copy could not be written (the item cannot be copied any more, or the subscriber cannot run it).</summary>
		Failed = 6
	}

	/// <summary>ContentSetProposals.Status. Persisted: append only.</summary>
	public enum ContentSetProposalStatuses
	{
		Open = 0,
		/// <summary>The publisher took it into its next draft.</summary>
		Accepted = 1,
		Declined = 2,
		Withdrawn = 3
	}

	/// <summary>Why a set operation was refused. Each maps to a localized message.</summary>
	public enum ContentSetErrors
	{
		None = 0,
		NotFound,
		NotAuthorized,
		InvalidInput,
		/// <summary>No enabled link over which the publisher shares the set's kind with the department.</summary>
		NotShared,
		AlreadySubscribed,
		/// <summary>The item cannot be put in a set (draft, licensed, document-backed, restricted lesson kinds…).</summary>
		ItemNotShareable,
		/// <summary>The item is already in the set.</summary>
		DuplicateItem,
		/// <summary>The update is no longer waiting for a decision.</summary>
		NotPending,
		/// <summary>A locked item's update cannot be declined, only deferred to the publisher's deadline.</summary>
		LockedCannotDecline,
		/// <summary>The date is past the publisher's deadline or before the publisher's effective date.</summary>
		DateOutOfRange,
		/// <summary>The block is locked by the publisher.</summary>
		BlockLocked,
		/// <summary>The subscriber cannot run LMS items: no LMS add-on and no sponsorship from the publisher.</summary>
		NeedsLms,
		/// <summary>A set cannot be subscribed by its own publisher.</summary>
		OwnSet
	}

	/// <summary>
	/// A publisher's set of protocols or courses (Protocols P5 regional publisher/subscriber, Training T5 shared course sets). A linked
	/// department that is shared the set's kind over an enabled Department Link may subscribe; each item is copied into the subscriber's
	/// department and later versions arrive in its inbox. No foreign keys: the department cleanup removes the rows of both parties.
	/// </summary>
	[Table("ContentSets")]
	public class ContentSet : IEntity
	{
		public const int NameMaxLength = 200;
		public const int DescriptionMaxLength = 2000;
		public const int DefaultDeferDays = 30;
		public const int MaxDeferDays = 365;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetId { get; set; }

		/// <summary>The publisher.</summary>
		public int DepartmentId { get; set; }

		/// <summary><see cref="ContentSetKinds"/>.</summary>
		public int Kind { get; set; }

		[MaxLength(NameMaxLength)]
		public string Name { get; set; }

		public string Description { get; set; }

		/// <summary>How long a subscriber may defer an update of a locked item (days after the publisher's effective date).</summary>
		public int LockedDeferDays { get; set; } = DefaultDeferDays;

		/// <summary>Archived sets take no new subscribers and send no more updates; subscriptions end.</summary>
		public bool IsArchived { get; set; }

		public DateTime CreatedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetId; set => ContentSetId = (int)value; }
		[NotMapped] public string TableName => "ContentSets";
		[NotMapped] public string IdName => "ContentSetId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>One item of a set, with how much subscribers may change it.</summary>
	[Table("ContentSetItems")]
	public class ContentSetItem : IEntity
	{
		public const int CustomizableKeysMaxLength = 2000;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetItemId { get; set; }

		/// <summary>The publisher.</summary>
		public int DepartmentId { get; set; }

		public int ContentSetId { get; set; }

		/// <summary><see cref="ContentSetItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		/// <summary>The publisher's DispatchProtocolId, LmsCourseId or TrainingId.</summary>
		public int ItemId { get; set; }

		/// <summary><see cref="ContentSetLockModes"/> (courses and trainings are Locked or Open).</summary>
		public int LockMode { get; set; }

		/// <summary>Comma separated block keys a subscriber may replace (<see cref="ContentSetLockModes.CustomizableBlocks"/>).</summary>
		[MaxLength(CustomizableKeysMaxLength)]
		public string CustomizableBlockKeys { get; set; }

		public int SortOrder { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedByUserId { get; set; }

		/// <summary>Removed from the set: subscribers' copies became their own content; kept for the history of updates.</summary>
		public DateTime? RemovedOn { get; set; }

		public string RemovedByUserId { get; set; }

		[NotMapped] [JsonIgnore] public bool IsRemoved => RemovedOn.HasValue;

		[NotMapped] [JsonIgnore]
		public IReadOnlyList<string> CustomizableKeys => ContentSetMerge.ParseKeys(CustomizableBlockKeys);

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetItemId; set => ContentSetItemId = (int)value; }
		[NotMapped] public string TableName => "ContentSetItems";
		[NotMapped] public string IdName => "ContentSetItemId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsRemoved", "CustomizableKeys" };
	}

	/// <summary>A subscriber department's subscription to a set, over one Department Link.</summary>
	[Table("ContentSetSubscriptions")]
	public class ContentSetSubscription : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetSubscriptionId { get; set; }

		/// <summary>The subscriber.</summary>
		public int DepartmentId { get; set; }

		public int ContentSetId { get; set; }

		public int PublisherDepartmentId { get; set; }

		/// <summary>The link the subscription was made over; every read re-checks it (enabled, and the kind still shared toward us).</summary>
		public int DepartmentLinkId { get; set; }

		/// <summary><see cref="ContentSetSubscriptionStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>Updates are accepted for the subscriber and go into force at its next update time on or after their effective date.</summary>
		public bool AutoAccept { get; set; }

		public DateTime SubscribedOn { get; set; }

		public string SubscribedByUserId { get; set; }

		public DateTime? SuspendedOn { get; set; }

		public DateTime? EndedOn { get; set; }

		public string EndedByUserId { get; set; }

		/// <summary><see cref="ContentSetEndReasons"/>.</summary>
		public int EndReason { get; set; }

		[NotMapped] [JsonIgnore] public bool IsActive => Status == (int)ContentSetSubscriptionStatuses.Active;

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetSubscriptionId; set => ContentSetSubscriptionId = (int)value; }
		[NotMapped] public string TableName => "ContentSetSubscriptions";
		[NotMapped] public string IdName => "ContentSetSubscriptionId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsActive" };
	}

	/// <summary>
	/// The subscriber's local copy of one set item: its own protocol, course or training, with the publisher version it carries and the
	/// subscriber's local changes (kept across updates). While the subscription lasts the copy is managed by the set (its content changes only
	/// through the inbox and the customize page); when it ends the copy is detached and becomes the subscriber's own.
	/// </summary>
	[Table("ContentSetCopies")]
	public class ContentSetCopy : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetCopyId { get; set; }

		/// <summary>The subscriber.</summary>
		public int DepartmentId { get; set; }

		public int ContentSetSubscriptionId { get; set; }

		public int ContentSetItemId { get; set; }

		public int PublisherDepartmentId { get; set; }

		/// <summary><see cref="ContentSetItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		/// <summary>The publisher's item id.</summary>
		public int SourceItemId { get; set; }

		/// <summary>The subscriber's DispatchProtocolId, LmsCourseId or TrainingId.</summary>
		public int LocalItemId { get; set; }

		/// <summary>The publisher's version the copy carries (ProtocolVersionId or LmsCourseVersionId; 0 for a training).</summary>
		public int SourceVersionId { get; set; }

		[MaxLength(50)]
		public string SourceVersionLabel { get; set; }

		/// <summary>The subscriber's version in force that was built from it.</summary>
		public int? LocalVersionId { get; set; }

		/// <summary><see cref="ContentSetLocalCustomization"/> JSON: the subscriber's block replacements and local additions.</summary>
		public string LocalCustomizationJson { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? UpdatedOn { get; set; }

		/// <summary>The copy stopped being managed by the set (unsubscribed, item removed, subscription ended).</summary>
		public DateTime? DetachedOn { get; set; }

		[NotMapped] [JsonIgnore] public bool IsManaged => !DetachedOn.HasValue;

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetCopyId; set => ContentSetCopyId = (int)value; }
		[NotMapped] public string TableName => "ContentSetCopies";
		[NotMapped] public string IdName => "ContentSetCopyId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsManaged" };
	}

	/// <summary>
	/// One row of a subscriber's pending-update inbox: a publisher version (or a newly added item) with its change summary, effective date and,
	/// for a locked item, the deadline it can be deferred to. Accepting schedules it for the subscriber's update time on or after the effective
	/// date; worker 100 applies it then (never mid-shift by surprise).
	/// </summary>
	[Table("ContentSetUpdates")]
	public class ContentSetUpdate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetUpdateId { get; set; }

		/// <summary>The subscriber.</summary>
		public int DepartmentId { get; set; }

		public int ContentSetSubscriptionId { get; set; }

		public int ContentSetItemId { get; set; }

		/// <summary>The copy it updates; null for a new item not copied yet.</summary>
		public int? ContentSetCopyId { get; set; }

		public int PublisherDepartmentId { get; set; }

		/// <summary><see cref="ContentSetUpdateKinds"/>.</summary>
		public int UpdateKind { get; set; }

		/// <summary><see cref="ContentSetItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		public int SourceItemId { get; set; }

		public int SourceVersionId { get; set; }

		[MaxLength(50)]
		public string SourceVersionLabel { get; set; }

		[MaxLength(250)]
		public string Title { get; set; }

		public string ChangeSummary { get; set; }

		/// <summary>The publisher's effective date: nothing goes into force before it.</summary>
		public DateTime SourceEffectiveOn { get; set; }

		/// <summary>Locked items: the latest it can be deferred to; past it worker 100 schedules it.</summary>
		public DateTime? DeadlineOn { get; set; }

		/// <summary><see cref="ContentSetUpdateStatuses"/>.</summary>
		public int Status { get; set; }

		/// <summary>When a scheduled update goes into force.</summary>
		public DateTime? ApplyOn { get; set; }

		public DateTime? DeferredUntil { get; set; }

		public DateTime? DecidedOn { get; set; }

		public string DecidedByUserId { get; set; }

		/// <summary>Accepted by the subscription's auto-accept or by the deadline (not by a person).</summary>
		public bool DecidedAutomatically { get; set; }

		public DateTime? AppliedOn { get; set; }

		public int? LocalVersionId { get; set; }

		[MaxLength(500)]
		public string FailureReason { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>Worker claim (two hosts never apply one update at the same time).</summary>
		public DateTime? ClaimedOn { get; set; }

		[NotMapped] [JsonIgnore]
		public bool IsOpen => Status == (int)ContentSetUpdateStatuses.Pending || Status == (int)ContentSetUpdateStatuses.Deferred || Status == (int)ContentSetUpdateStatuses.Scheduled;

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetUpdateId; set => ContentSetUpdateId = (int)value; }
		[NotMapped] public string TableName => "ContentSetUpdates";
		[NotMapped] public string IdName => "ContentSetUpdateId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName", "IsOpen" };
	}

	/// <summary>
	/// A subscriber member's proposed change to a publisher's item ("propose a change upstream", like MPDS Proposals for Change but open). It
	/// reaches the publisher as a suggestion thread on the item (for a protocol, a suggestion comment the owner sees on the next draft) and the
	/// publisher answers it; the subscriber sees the answer.
	/// </summary>
	[Table("ContentSetProposals")]
	public class ContentSetProposal : IEntity
	{
		public const int BodyMaxLength = 4000;
		public const int AnchorMaxLength = 128;

		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ContentSetProposalId { get; set; }

		/// <summary>The proposing (subscriber) department.</summary>
		public int DepartmentId { get; set; }

		public int PublisherDepartmentId { get; set; }

		public int ContentSetId { get; set; }

		public int ContentSetItemId { get; set; }

		/// <summary><see cref="ContentSetItemKinds"/>.</summary>
		public int ItemKind { get; set; }

		/// <summary>The publisher's item.</summary>
		public int SourceItemId { get; set; }

		/// <summary>The publisher's version the member was reading.</summary>
		public int? SourceVersionId { get; set; }

		/// <summary>The heading anchor or block key the proposal is about; null for the whole item.</summary>
		[MaxLength(AnchorMaxLength)]
		public string Anchor { get; set; }

		public string Body { get; set; }

		public string AuthorUserId { get; set; }

		/// <summary>The author's name when proposed (the publisher cannot read the subscriber's roster).</summary>
		[MaxLength(200)]
		public string AuthorName { get; set; }

		[MaxLength(200)]
		public string SubscriberName { get; set; }

		/// <summary><see cref="ContentSetProposalStatuses"/>.</summary>
		public int Status { get; set; }

		public string ResponseBody { get; set; }

		public string RespondedByUserId { get; set; }

		public DateTime? RespondedOn { get; set; }

		/// <summary>The suggestion comment it opened on the publisher's protocol (ProtocolComments).</summary>
		public int? PublisherCommentId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped] [JsonIgnore] public object IdValue { get => ContentSetProposalId; set => ContentSetProposalId = (int)value; }
		[NotMapped] public string TableName => "ContentSetProposals";
		[NotMapped] public string IdName => "ContentSetProposalId";
		[NotMapped] public int IdType => 0;
		[NotMapped] public IEnumerable<string> IgnoredProperties => new[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>A subscriber's local changes to a copy: replaced blocks (by the publisher's block key) and local blocks added at the end.</summary>
	public sealed class ContentSetLocalCustomization
	{
		public const int MaxAdditions = 20;

		[JsonProperty("overrides")]
		public Dictionary<string, string> Overrides { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

		[JsonProperty("additions")]
		public List<string> Additions { get; set; } = new List<string>();

		[JsonIgnore]
		public bool IsEmpty => (Overrides == null || Overrides.Count == 0) && (Additions == null || Additions.Count(a => !string.IsNullOrWhiteSpace(a)) == 0);

		public string ToJson() => IsEmpty ? null : JsonConvert.SerializeObject(this, Formatting.None);

		public static ContentSetLocalCustomization Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new ContentSetLocalCustomization();
			try
			{
				var parsed = JsonConvert.DeserializeObject<ContentSetLocalCustomization>(json) ?? new ContentSetLocalCustomization();
				parsed.Overrides = new Dictionary<string, string>(parsed.Overrides ?? new Dictionary<string, string>(), StringComparer.Ordinal);
				parsed.Additions ??= new List<string>();
				return parsed;
			}
			catch (JsonException)
			{
				return new ContentSetLocalCustomization();
			}
		}
	}

	/// <summary>
	/// The lock/override merge of a protocol set copy (pure, so the rules are tested without a database): the publisher's document with the
	/// subscriber's replacements of the blocks it may change and its local additions at the end. Locked blocks always come from the publisher,
	/// so an upstream update can change them; a replacement survives every update for as long as the publisher keeps the block's key.
	/// </summary>
	public static class ContentSetMerge
	{
		public const string LocalKeyPrefix = "local-";

		public static IReadOnlyList<string> ParseKeys(string keys) =>
			string.IsNullOrWhiteSpace(keys)
				? Array.Empty<string>()
				: keys.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(k => k.Trim()).Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();

		public static string JoinKeys(IEnumerable<string> keys)
		{
			var joined = string.Join(",", (keys ?? Enumerable.Empty<string>()).Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()).Distinct(StringComparer.Ordinal));
			return joined.Length == 0 ? null : joined.Length > ContentSetItem.CustomizableKeysMaxLength ? joined.Substring(0, ContentSetItem.CustomizableKeysMaxLength) : joined;
		}

		/// <summary>Whether the subscriber may replace the block with this key.</summary>
		public static bool IsCustomizable(ContentSetLockModes mode, IReadOnlyCollection<string> customizableKeys, string blockKey) =>
			!string.IsNullOrWhiteSpace(blockKey) && (mode == ContentSetLockModes.Open ||
				(mode == ContentSetLockModes.CustomizableBlocks && customizableKeys != null && customizableKeys.Contains(blockKey, StringComparer.Ordinal)));

		/// <summary>Whether the subscriber may add local blocks at the end.</summary>
		public static bool AllowsAdditions(ContentSetLockModes mode) => mode != ContentSetLockModes.Locked;

		/// <summary>Only an open item's update may be declined; locked content can only be deferred to the deadline.</summary>
		public static bool CanDecline(ContentSetLockModes mode) => mode == ContentSetLockModes.Open;

		/// <summary>
		/// The subscriber's document: the publisher's blocks in the publisher's order, each customizable block the subscriber replaced swapped for
		/// an html block with the same key, then the local additions (open and customizable items only). The decision graph and schema are the
		/// publisher's. Replacements of blocks that are locked now, or whose key the publisher removed, are dropped (and reported).
		/// </summary>
		public static ProtocolContentDocument Merge(ProtocolContentDocument source, ContentSetLockModes mode, IReadOnlyCollection<string> customizableKeys,
			ContentSetLocalCustomization local, out List<string> droppedOverrides)
		{
			droppedOverrides = new List<string>();
			var merged = new ProtocolContentDocument
			{
				Schema = source?.Schema ?? ProtocolContentDocument.CurrentSchema,
				Graph = source?.Graph,
				Blocks = new List<ProtocolContentBlock>(),
				Anchors = new List<ProtocolContentAnchor>()
			};
			local ??= new ContentSetLocalCustomization();
			var overrides = local.Overrides ?? new Dictionary<string, string>();
			var used = new HashSet<string>(StringComparer.Ordinal);

			foreach (var block in source?.Blocks ?? new List<ProtocolContentBlock>())
			{
				if (block == null)
					continue;
				if (block.Key != null && overrides.TryGetValue(block.Key, out var html) && IsCustomizable(mode, customizableKeys, block.Key))
				{
					merged.Blocks.Add(new ProtocolContentBlock { Type = ProtocolContentDocument.HtmlBlockType, Key = block.Key, Html = html ?? string.Empty });
					used.Add(block.Key);
				}
				else
				{
					merged.Blocks.Add(block);
				}
			}

			droppedOverrides.AddRange(overrides.Keys.Where(k => !used.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));

			if (AllowsAdditions(mode))
			{
				var n = 0;
				foreach (var addition in (local.Additions ?? new List<string>()).Where(a => !string.IsNullOrWhiteSpace(a)).Take(ContentSetLocalCustomization.MaxAdditions))
				{
					n++;
					merged.Blocks.Add(new ProtocolContentBlock { Type = ProtocolContentDocument.HtmlBlockType, Key = LocalKeyPrefix + n.ToString(System.Globalization.CultureInfo.InvariantCulture), Html = addition });
				}
			}

			return merged;
		}

		/// <summary>The keys of the blocks whose content differs between two publisher versions (added, removed or changed).</summary>
		public static List<string> ChangedBlockKeys(ProtocolContentDocument from, ProtocolContentDocument to)
		{
			string Fingerprint(ProtocolContentBlock b) => JsonConvert.SerializeObject(b, Formatting.None);
			var before = (from?.Blocks ?? new List<ProtocolContentBlock>()).Where(b => b?.Key != null).GroupBy(b => b.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Fingerprint(g.First()), StringComparer.Ordinal);
			var after = (to?.Blocks ?? new List<ProtocolContentBlock>()).Where(b => b?.Key != null).GroupBy(b => b.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Fingerprint(g.First()), StringComparer.Ordinal);
			return before.Keys.Union(after.Keys, StringComparer.Ordinal)
				.Where(k => !before.TryGetValue(k, out var b) || !after.TryGetValue(k, out var a) || !string.Equals(a, b, StringComparison.Ordinal))
				.OrderBy(k => k, StringComparer.Ordinal).ToList();
		}
	}

	/// <summary>
	/// When accepted updates go into force (pure). Never mid-shift by surprise: an update takes effect at the subscriber's update time
	/// (<see cref="ContentSetSettings.UpdateTimeOfDayMinutes"/>, local, e.g. the 07:00 shift change) on or after the publisher's effective date,
	/// or at a later time the subscriber picks.
	/// </summary>
	public static class ContentSetSchedule
	{
		/// <summary>
		/// The department's zone from its time zone setting (Windows or IANA id), through NodaTime's own database so it works where the host has no
		/// ICU (the hardened container). Blank keeps the legacy Pacific default; an unknown id is UTC.
		/// </summary>
		public static NodaTime.DateTimeZone Zone(string departmentTimeZone)
		{
			var id = Resgrid.Framework.DateTimeHelpers.ConvertTimeZoneString(string.IsNullOrWhiteSpace(departmentTimeZone) ? "Pacific Standard Time" : departmentTimeZone);
			var zone = NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(id);
			if (zone == null && TimeZoneConverter.TZConvert.TryWindowsToIana(id, out var iana))
				zone = NodaTime.DateTimeZoneProviders.Tzdb.GetZoneOrNull(iana);
			return zone ?? NodaTime.DateTimeZone.Utc;
		}

		/// <summary>The first instant at or after <paramref name="notBeforeUtc"/> that is <paramref name="minutesAfterMidnight"/> local time.</summary>
		public static DateTime NextUpdateTime(DateTime notBeforeUtc, string departmentTimeZone, int minutesAfterMidnight)
		{
			var zone = Zone(departmentTimeZone);
			minutesAfterMidnight = Math.Max(0, Math.Min(24 * 60 - 1, minutesAfterMidnight));
			var utc = DateTime.SpecifyKind(notBeforeUtc, DateTimeKind.Utc);
			var localDate = NodaTime.Instant.FromDateTimeUtc(utc).InZone(zone).Date;
			for (var day = 0; day < 3; day++)
			{
				var candidate = localDate.PlusDays(day).At(NodaTime.LocalTime.FromMinutesSinceMidnight(minutesAfterMidnight));
				// A time skipped by a daylight-saving change moves forward; a repeated one takes the earlier instant.
				var candidateUtc = candidate.InZoneLeniently(zone).ToDateTimeUtc();
				if (candidateUtc >= utc)
					return candidateUtc;
			}

			return utc;
		}

		/// <summary>A locked item's deadline: the publisher's effective date plus the set's defer window.</summary>
		public static DateTime? Deadline(ContentSetLockModes mode, DateTime sourceEffectiveOnUtc, int lockedDeferDays) =>
			mode == ContentSetLockModes.Open ? (DateTime?)null : sourceEffectiveOnUtc.AddDays(Math.Max(0, Math.Min(ContentSet.MaxDeferDays, lockedDeferDays)));

		/// <summary>
		/// The time an accepted update goes into force: the requested time (or now), never before the publisher's effective date, moved to
		/// the next update time unless the subscriber picked an exact time.
		/// </summary>
		public static DateTime ApplyTime(DateTime nowUtc, DateTime sourceEffectiveOnUtc, DateTime? requestedUtc, string departmentTimeZone, int minutesAfterMidnight)
		{
			var notBefore = sourceEffectiveOnUtc > nowUtc ? sourceEffectiveOnUtc : nowUtc;
			if (requestedUtc.HasValue)
				return requestedUtc.Value < notBefore ? notBefore : requestedUtc.Value;
			return NextUpdateTime(notBefore, departmentTimeZone, minutesAfterMidnight);
		}
	}

	/// <summary>Department setting 151 (ContentSetSettings): when accepted set updates go into force, and who hears about them.</summary>
	public sealed class ContentSetSettings
	{
		public const int DefaultUpdateTimeOfDayMinutes = 7 * 60;

		/// <summary>Local minutes after midnight when accepted updates go into force (default 07:00, a common shift change).</summary>
		[JsonProperty("updateMinutes")]
		public int UpdateTimeOfDayMinutes { get; set; } = DefaultUpdateTimeOfDayMinutes;

		/// <summary>Tell the department's set managers when an update arrives or a proposal is answered.</summary>
		[JsonProperty("notify")]
		public bool NotifyManagers { get; set; } = true;

		/// <summary>Most roles a department may name for set notices.</summary>
		public const int MaxNotifyRoles = 20;

		/// <summary>Whether the department's admins get set notices (default on). Turned off, only <see cref="NotifyRoleIds"/> hear them.</summary>
		[JsonProperty("admins")]
		public bool NotifyAdmins { get; set; } = true;

		/// <summary>Personnel roles whose current members also get set notices (updates, proposals, sponsorships, LTI registrations to make).</summary>
		[JsonProperty("roles")]
		public List<int> NotifyRoleIds { get; set; } = new List<int>();

		public static ContentSetSettings Parse(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new ContentSetSettings();
			try
			{
				var parsed = JsonConvert.DeserializeObject<ContentSetSettings>(json) ?? new ContentSetSettings();
				parsed.UpdateTimeOfDayMinutes = Math.Max(0, Math.Min(24 * 60 - 1, parsed.UpdateTimeOfDayMinutes));
				parsed.NotifyRoleIds = (parsed.NotifyRoleIds ?? new List<int>()).Where(r => r > 0).Distinct().Take(MaxNotifyRoles).ToList();
				return parsed;
			}
			catch (JsonException)
			{
				return new ContentSetSettings();
			}
		}

		public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);
	}
}
