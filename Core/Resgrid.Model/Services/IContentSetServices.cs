using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Protocols P5 "Linked departments": a department reads the other side's published protocols over a Department Link that shares
	/// Protocols toward it (<see cref="Helpers.DepartmentLinkSharing"/>), read-only and only the version in force. Licensed, audience-restricted,
	/// disabled and document-backed protocols are never shared. Every read re-checks the link, so revoking it or turning the kind off cuts
	/// access at once. Nothing is evented, so the kind has no realtime share group.
	/// </summary>
	public interface ILinkedProtocolService
	{
		/// <summary>The protocols the viewer may read over the link; null when the link does not share Protocols toward the viewer.</summary>
		Task<LinkedProtocolList> GetSharedProtocolsAsync(int viewerDepartmentId, int departmentLinkId);

		/// <summary>One shared protocol's version in force; null when it is not shared toward the viewer (or not eligible any more).</summary>
		Task<LinkedProtocolPage> GetSharedProtocolAsync(int viewerDepartmentId, int departmentLinkId, int protocolId);
	}

	public sealed class LinkedProtocolList
	{
		public DepartmentLink Link { get; set; }
		public int SharingDepartmentId { get; set; }
		public string SharingDepartmentName { get; set; }
		public List<LinkedProtocolSummary> Protocols { get; set; } = new List<LinkedProtocolSummary>();
	}

	public sealed class LinkedProtocolSummary
	{
		public int ProtocolId { get; set; }
		public string Name { get; set; }
		public string Number { get; set; }
		public int Kind { get; set; }
		public string CategoryName { get; set; }
		public string VersionLabel { get; set; }
		public DateTime? EffectiveOn { get; set; }
	}

	public sealed class LinkedProtocolPage
	{
		public LinkedProtocolSummary Summary { get; set; }
		public string SharingDepartmentName { get; set; }
		/// <summary>The version's stored (sanitized) HTML; render it with Html.ProtocolContent.</summary>
		public string BodyHtml { get; set; }
		public string ChangeSummary { get; set; }
		public string ContentShortCode { get; set; }
		public int DepartmentLinkId { get; set; }
	}

	/// <summary>Who is acting on sets in a department: a current member, and whether they hold Manage Content Sets (permission 136).</summary>
	public sealed class ContentSetActor
	{
		public int DepartmentId { get; set; }
		public string UserId { get; set; }
		public string Name { get; set; }
		public bool IsCurrentMember { get; set; }
		public bool IsDepartmentAdmin { get; set; }
		public bool CanManage { get; set; }

		public static ContentSetActor Nobody(int departmentId) => new ContentSetActor { DepartmentId = departmentId };
	}

	public sealed class ContentSetResult
	{
		public ContentSetErrors Error { get; set; }
		public ContentSet Set { get; set; }
		public ContentSetItem Item { get; set; }
		public ContentSetSubscription Subscription { get; set; }
		public ContentSetUpdate Update { get; set; }
		public ContentSetCopy Copy { get; set; }
		public ContentSetProposal Proposal { get; set; }
		/// <summary>Items a subscription could not copy now (an LMS item without the add-on or sponsorship), by title.</summary>
		public List<string> Skipped { get; set; } = new List<string>();
		/// <summary>Why an item cannot be shared (<see cref="ContentSetErrors.ItemNotShareable"/>): a stable reason key.</summary>
		public string Reason { get; set; }
		public bool Succeeded => Error == ContentSetErrors.None;

		public static ContentSetResult Fail(ContentSetErrors error, string reason = null) => new ContentSetResult { Error = error, Reason = reason };
	}

	/// <summary>A set as its publisher manages it.</summary>
	public sealed class ContentSetDetail
	{
		public ContentSet Set { get; set; }
		public List<ContentSetItemView> Items { get; set; } = new List<ContentSetItemView>();
		public List<ContentSetSubscriberView> Subscribers { get; set; } = new List<ContentSetSubscriberView>();
		public int OpenProposals { get; set; }
	}

	public sealed class ContentSetItemView
	{
		public ContentSetItem Item { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>The item can still be copied (null) or why not (a reason key).</summary>
		public string Problem { get; set; }
		/// <summary>Protocols: the current version's blocks (key and a short label), for choosing the customizable ones.</summary>
		public List<ContentSetBlockOption> Blocks { get; set; } = new List<ContentSetBlockOption>();
		public bool NeedsLms { get; set; }
	}

	public sealed class ContentSetBlockOption
	{
		public string Key { get; set; }
		public string Label { get; set; }
		public bool Customizable { get; set; }
	}

	public sealed class ContentSetSubscriberView
	{
		public ContentSetSubscription Subscription { get; set; }
		public string DepartmentName { get; set; }
		public bool Sponsored { get; set; }
	}

	/// <summary>One item a publisher could add to a set.</summary>
	public sealed class ContentSetCandidate
	{
		public ContentSetItemKinds Kind { get; set; }
		public int ItemId { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
		/// <summary>Null when it can be added, else a reason key (draft, licensed, document-backed, lesson kinds…).</summary>
		public string Problem { get; set; }
		public bool NeedsLms { get; set; }
	}

	/// <summary>A set the department can subscribe to (or already does), over a link.</summary>
	public sealed class AvailableContentSet
	{
		public ContentSet Set { get; set; }
		public string PublisherName { get; set; }
		public int DepartmentLinkId { get; set; }
		public ContentSetSubscription Subscription { get; set; }
		public List<ContentSetItemView> Items { get; set; } = new List<ContentSetItemView>();
		/// <summary>The department can run the set's LMS items (its own add-on, or the publisher sponsors it).</summary>
		public bool CanRunLms { get; set; }
		public bool SponsoredByPublisher { get; set; }
	}

	/// <summary>A subscription as the subscriber sees it.</summary>
	public sealed class ContentSetSubscriptionView
	{
		public ContentSetSubscription Subscription { get; set; }
		public ContentSet Set { get; set; }
		public string PublisherName { get; set; }
		public List<ContentSetCopyRow> Copies { get; set; } = new List<ContentSetCopyRow>();
		public int OpenUpdates { get; set; }
		/// <summary>The link still shares the set's kind toward the department.</summary>
		public bool LinkOk { get; set; }
		public bool SponsoredByPublisher { get; set; }
	}

	public sealed class ContentSetCopyRow
	{
		public ContentSetCopy Copy { get; set; }
		public ContentSetItem Item { get; set; }
		public string Title { get; set; }
		public bool HasLocalChanges { get; set; }
	}

	/// <summary>One inbox row with what the subscriber may do with it.</summary>
	public sealed class ContentSetInboxRow
	{
		public ContentSetUpdate Update { get; set; }
		public string SetName { get; set; }
		public string PublisherName { get; set; }
		public ContentSetLockModes LockMode { get; set; }
		public bool CanDecline { get; set; }
	}

	/// <summary>An inbox row opened: the redline of the subscriber's version against what accepting would put in force.</summary>
	public sealed class ContentSetUpdateView
	{
		public ContentSetInboxRow Row { get; set; }
		/// <summary>The subscriber's current HTML (empty for a new item).</summary>
		public string CurrentHtml { get; set; }
		/// <summary>What would go into force: the publisher's version merged with the subscriber's changes.</summary>
		public string CandidateHtml { get; set; }
		public ProtocolDiffResult Diff { get; set; }
		/// <summary>The subscriber's changes the new version no longer has a place for (the publisher removed or locked those blocks).</summary>
		public List<string> DroppedChanges { get; set; } = new List<string>();
		/// <summary>The default apply time (the department's next update time on or after the effective date), UTC.</summary>
		public DateTime SuggestedApplyOnUtc { get; set; }
		public bool CanManage { get; set; }
	}

	/// <summary>The customize page of one managed protocol copy.</summary>
	public sealed class ContentSetCustomizeView
	{
		public ContentSetCopy Copy { get; set; }
		public ContentSetItem Item { get; set; }
		public string Title { get; set; }
		public ContentSetLockModes LockMode { get; set; }
		public List<ContentSetCustomizeBlock> Blocks { get; set; } = new List<ContentSetCustomizeBlock>();
		public List<string> Additions { get; set; } = new List<string>();
		public bool AllowsAdditions { get; set; }
	}

	public sealed class ContentSetCustomizeBlock
	{
		public string Key { get; set; }
		public string Label { get; set; }
		/// <summary>The publisher's rendered block (what stays when the local change is cleared).</summary>
		public string PublisherHtml { get; set; }
		/// <summary>The subscriber's replacement, or null.</summary>
		public string LocalHtml { get; set; }
		public bool Customizable { get; set; }
	}

	/// <summary>The banner on a subscriber's own protocol or course: it is a managed copy of a publisher's set item.</summary>
	public sealed class ContentSetCopyInfo
	{
		public ContentSetCopy Copy { get; set; }
		public ContentSet Set { get; set; }
		public ContentSetItem Item { get; set; }
		public string PublisherName { get; set; }
		public bool CanCustomize { get; set; }
		public bool CanPropose { get; set; }
	}

	public sealed class ContentSetProposalView
	{
		public ContentSetProposal Proposal { get; set; }
		public string SetName { get; set; }
		public string ItemTitle { get; set; }
	}

	/// <summary>What one run of worker 100 did.</summary>
	public sealed class ContentSetRunResult
	{
		public int Accepted { get; set; }
		public int Applied { get; set; }
		public int Failed { get; set; }
		public int Suspended { get; set; }
		public int SponsorshipsEnded { get; set; }
		/// <summary>Partner countersignatures and deployment acceptances voided because their link no longer routes them.</summary>
		public int CountersignaturesLapsed { get; set; }
	}

	/// <summary>
	/// One kind of set item (protocol, LMS course, baseline training): how the publisher's item is described and checked, and how the subscriber's
	/// copy is created and moved to a new publisher version. Registered per kind; the LMS kind declares <see cref="NeedsLms"/>.
	/// </summary>
	public interface IContentSetItemSource
	{
		ContentSetItemKinds Kind { get; }

		/// <summary>The subscriber needs the LMS add-on (or the publisher's sponsorship) to receive this kind (decision: LMS-type items).</summary>
		bool NeedsLms { get; }

		/// <summary>The set kind this item kind belongs to.</summary>
		ContentSetKinds SetKind { get; }

		/// <summary>The publisher's items of this kind that could be added, each with its problem (or none).</summary>
		Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId);

		/// <summary>The publisher's item as it is now; null when it is not the publisher's.</summary>
		Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId);

		/// <summary>The publisher's item at one of its versions (the version a copy carries); the current one when that version is gone.</summary>
		Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId);

		/// <summary>Creates the subscriber's copy from the publisher's version in force.</summary>
		Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default);

		/// <summary>Moves the subscriber's copy to the publisher's version in force (with the subscriber's changes kept).</summary>
		Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default);

		/// <summary>The subscriber's copy as it reads now (HTML), for the redline.</summary>
		Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId);

		/// <summary>The subscriber's copy as accepting would leave it (HTML), and the local changes that would be dropped.</summary>
		Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context);

		/// <summary>Whether the subscriber's copy still exists (a deleted local item ends the copy's management).</summary>
		Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId);
	}

	/// <summary>The publisher's item as a set sees it.</summary>
	public sealed class ContentSetSourceItem
	{
		public ContentSetItemKinds Kind { get; set; }
		public int ItemId { get; set; }
		public string Title { get; set; }
		/// <summary>The version in force (ProtocolVersionId, LmsCourseVersionId; for a training its id), 0 when none.</summary>
		public int VersionId { get; set; }
		public string VersionLabel { get; set; }
		public int Major { get; set; }
		public DateTime? EffectiveOn { get; set; }
		public string ChangeSummary { get; set; }
		/// <summary>Null when it can be copied, else a reason key.</summary>
		public string Problem { get; set; }
		/// <summary>Protocols: the version's block document JSON.</summary>
		public string ContentJson { get; set; }
		public string Locale { get; set; }
		public List<ContentSetBlockOption> Blocks { get; set; } = new List<ContentSetBlockOption>();
	}

	/// <summary>Everything a source needs to create or move one subscriber copy.</summary>
	public sealed class ContentSetCopyContext
	{
		public int SubscriberDepartmentId { get; set; }
		public string UserId { get; set; }
		public ContentSet Set { get; set; }
		public ContentSetItem Item { get; set; }
		public ContentSetCopy Copy { get; set; }
		public ContentSetSourceItem Source { get; set; }
		public ContentSetLocalCustomization Local { get; set; }
		public string PublisherName { get; set; }
		/// <summary>The source's major changed since the copy's version (a new local major version).</summary>
		public bool Major { get; set; }
	}

	public sealed class ContentSetCopyOutcome
	{
		public bool Succeeded { get; set; }
		public int LocalItemId { get; set; }
		public int? LocalVersionId { get; set; }
		public string Problem { get; set; }

		public static ContentSetCopyOutcome Fail(string problem) => new ContentSetCopyOutcome { Problem = problem };
	}

	/// <summary>
	/// A set item kind whose items name other items that must travel with them (a learning path's course and training steps): the publisher
	/// may add such an item only once those items are in the set, and subscribers copy it after them.
	/// </summary>
	public interface IContentSetItemDependencies
	{
		Task<List<KeyValuePair<ContentSetItemKinds, int>>> GetDependenciesAsync(int publisherDepartmentId, int itemId);
	}

	/// <summary>
	/// The edit lock on managed set copies (the protocol library and the course editor ask it before a draft): a copy's content changes only
	/// through the set. A cheap repository read; no dependency on the set service, so the library and the set service do not depend on each other.
	/// </summary>
	public interface IContentSetCopyLocks
	{
		Task<bool> IsManagedCopyAsync(int departmentId, ContentSetItemKinds kind, int localItemId);
	}

	/// <summary>
	/// Protocols P5 regional publisher/subscriber and Training T5 shared course sets (round 10 A3). A publisher groups protocols (or courses and
	/// trainings) into a set; a linked department the set's kind is shared with subscribes over the link; each item is copied into the
	/// subscriber's department; later publisher versions arrive in the subscriber's inbox as a redline and go into force on their effective date
	/// at the subscriber's update time once accepted (or automatically). Locked content can only be deferred to the publisher's deadline;
	/// customizable blocks keep the subscriber's changes across updates; members propose changes upstream. Every operation re-checks the link.
	/// </summary>
	public interface IContentSetService
	{
		Task<ContentSetActor> GetActorAsync(int departmentId, string userId);

		Task<ContentSetSettings> GetSettingsAsync(int departmentId);

		Task<ContentSetSettings> SaveSettingsAsync(ContentSetActor actor, ContentSetSettings settings, CancellationToken cancellationToken = default);

		// ---- publisher ----

		Task<List<ContentSetDetail>> GetPublishedSetsAsync(ContentSetActor actor, ContentSetKinds kind);

		Task<ContentSetDetail> GetSetAsync(ContentSetActor actor, int contentSetId);

		Task<ContentSetResult> SaveSetAsync(ContentSetActor actor, int contentSetId, ContentSetKinds kind, string name, string description, int lockedDeferDays,
			CancellationToken cancellationToken = default);

		/// <summary>Archives a set: no new subscribers, no more updates; open subscriptions end and their copies become the subscribers' own.</summary>
		Task<ContentSetResult> ArchiveSetAsync(ContentSetActor actor, int contentSetId, CancellationToken cancellationToken = default);

		Task<List<ContentSetCandidate>> GetCandidatesAsync(ContentSetActor actor, int contentSetId);

		Task<ContentSetResult> AddItemAsync(ContentSetActor actor, int contentSetId, ContentSetItemKinds kind, int itemId, ContentSetLockModes lockMode,
			IEnumerable<string> customizableKeys, CancellationToken cancellationToken = default);

		Task<ContentSetResult> UpdateItemAsync(ContentSetActor actor, int contentSetItemId, ContentSetLockModes lockMode, IEnumerable<string> customizableKeys,
			CancellationToken cancellationToken = default);

		/// <summary>Removes an item: subscribers' copies of it become their own content.</summary>
		Task<ContentSetResult> RemoveItemAsync(ContentSetActor actor, int contentSetItemId, CancellationToken cancellationToken = default);

		/// <summary>The publisher ends one department's subscription.</summary>
		Task<ContentSetResult> RemoveSubscriberAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default);

		Task<List<ContentSetProposalView>> GetProposalsToPublisherAsync(ContentSetActor actor, bool openOnly);

		Task<ContentSetResult> RespondToProposalAsync(ContentSetActor actor, int contentSetProposalId, bool accept, string response,
			CancellationToken cancellationToken = default);

		// ---- subscriber ----

		/// <summary>Sets of departments that share the kind toward the actor's department over an enabled link.</summary>
		Task<List<AvailableContentSet>> GetAvailableSetsAsync(ContentSetActor actor, ContentSetKinds kind);

		Task<AvailableContentSet> GetAvailableSetAsync(ContentSetActor actor, int contentSetId);

		Task<ContentSetResult> SubscribeAsync(ContentSetActor actor, int contentSetId, bool autoAccept, CancellationToken cancellationToken = default);

		/// <summary>Ends the subscription; the copies become the department's own content.</summary>
		Task<ContentSetResult> UnsubscribeAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default);

		Task<ContentSetResult> SetAutoAcceptAsync(ContentSetActor actor, int contentSetSubscriptionId, bool autoAccept, CancellationToken cancellationToken = default);

		/// <summary>Copies the set items the department could not receive before (an LMS item once the add-on or sponsorship is there).</summary>
		Task<ContentSetResult> SyncAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default);

		Task<List<ContentSetSubscriptionView>> GetSubscriptionsAsync(ContentSetActor actor);

		Task<List<ContentSetInboxRow>> GetInboxAsync(ContentSetActor actor, bool openOnly);

		Task<ContentSetUpdateView> GetUpdateAsync(ContentSetActor actor, int contentSetUpdateId);

		/// <summary>Accepts an update: it goes into force at <paramref name="applyOnUtc"/> (never before the publisher's effective date), or the next update time.</summary>
		Task<ContentSetResult> AcceptUpdateAsync(ContentSetActor actor, int contentSetUpdateId, DateTime? applyOnUtc, CancellationToken cancellationToken = default);

		/// <summary>Defers an update (a locked item's never past its deadline).</summary>
		Task<ContentSetResult> DeferUpdateAsync(ContentSetActor actor, int contentSetUpdateId, DateTime untilUtc, CancellationToken cancellationToken = default);

		/// <summary>Declines an open item's update (locked content cannot be declined).</summary>
		Task<ContentSetResult> DeclineUpdateAsync(ContentSetActor actor, int contentSetUpdateId, CancellationToken cancellationToken = default);

		/// <summary>The set behind one of the department's own items, or null when it is not a managed copy.</summary>
		Task<ContentSetCopyInfo> GetCopyInfoAsync(ContentSetActor actor, ContentSetItemKinds kind, int localItemId);

		Task<ContentSetCustomizeView> GetCustomizeAsync(ContentSetActor actor, int contentSetCopyId);

		/// <summary>Saves the department's changes to a protocol copy (customizable blocks, local additions) and puts them into force now.</summary>
		Task<ContentSetResult> SaveCustomizationAsync(ContentSetActor actor, int contentSetCopyId, IDictionary<string, string> overrides, IList<string> additions,
			CancellationToken cancellationToken = default);

		/// <summary>A member proposes a change upstream (any member who can read the copy).</summary>
		Task<ContentSetResult> ProposeAsync(ContentSetActor actor, int contentSetCopyId, string anchor, string body, CancellationToken cancellationToken = default);

		Task<List<ContentSetProposalView>> GetProposalsFromDepartmentAsync(ContentSetActor actor);

		// ---- system ----

		/// <summary>Worker 100: auto-accepts, enforces deadlines, applies due updates, and suspends subscriptions whose link no longer shares the kind.</summary>
		Task<ContentSetRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The publish hook's work: a publisher's item went into force at a new version.</summary>
		Task<int> OnSourcePublishedAsync(int publisherDepartmentId, ContentSetItemKinds kind, int itemId, CancellationToken cancellationToken = default);

		/// <summary>
		/// Suspends the open subscriptions over a link (or, with null, every open subscription) whose link no longer shares the set's kind toward
		/// the subscriber; returns how many were suspended. Called on revocation and by worker 100.
		/// </summary>
		Task<int> SuspendLapsedAsync(int? departmentLinkId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Member notices for sets and sponsorships (round 10 A3, EventTypes 83-85), in each recipient's language: an update arrived in a
	/// subscriber's inbox, a proposal reached a publisher, a publisher answered a proposal, a sponsorship started or ended. Never fatal.
	/// </summary>
	public interface IContentSetNotifier
	{
		Task UpdatesArrivedAsync(int subscriberDepartmentId, string setName, string publisherName, IReadOnlyList<ContentSetUpdate> updates, CancellationToken cancellationToken = default);

		Task ProposalReceivedAsync(ContentSetProposal proposal, string setName, string itemTitle, CancellationToken cancellationToken = default);

		Task ProposalAnsweredAsync(ContentSetProposal proposal, string setName, string itemTitle, string publisherName, CancellationToken cancellationToken = default);

		/// <summary>The sponsored department hears a sponsorship start or end (ids, not the LMS entity: baseline code stays free of LMS types).</summary>
		Task SponsorshipChangedAsync(int sponsoredDepartmentId, int sponsorshipId, int seatLimit, bool started, string sponsorName, CancellationToken cancellationToken = default);

		/// <summary>A subscriber's course copy uses an LTI tool its department has not registered yet: the launch stays closed until it does.</summary>
		Task ToolRegistrationNeededAsync(int subscriberDepartmentId, string setName, string toolName, string publisherName, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Work that follows a Department Link losing access (disabled, deleted, or a share kind turned off): the set subscriptions, sponsorships and
	/// countersignature requests made over it stop at once. Every read re-checks the link too, so a failed listener never leaves access open.
	/// </summary>
	public interface IDepartmentLinkAccessListener
	{
		Task OnLinkAccessReducedAsync(DepartmentLink stored, DepartmentLink updated, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Sponsored LMS seats (T5 item 2a, R-L3; §25.4a items 8 and 8b), granted and revoked over a Department Link that shares Training from the
	/// sponsor toward the sponsored department. The sponsor needs an active LMS add-on; the sponsored department needs none and may be on the free
	/// plan. Its LMS-enabled members count in the sponsor's packs and it runs the sponsor's course-set copies only.
	/// </summary>
	public interface ILmsSponsorshipService
	{
		/// <summary>Sponsorships the department gives and receives (active and ended), with names and seat use.</summary>
		Task<List<LmsSponsorshipView>> GetForDepartmentAsync(int departmentId);

		/// <summary>Starts sponsoring a linked department (the acting member must hold Manage Content Sets in the sponsor).</summary>
		Task<LmsSponsorshipErrors> SponsorAsync(ContentSetActor sponsor, int sponsoredDepartmentId, int? seatLimit, CancellationToken cancellationToken = default);

		Task<LmsSponsorshipErrors> SetSeatLimitAsync(ContentSetActor sponsor, int lmsSponsorshipId, int? seatLimit, CancellationToken cancellationToken = default);

		/// <summary>Ends a sponsorship (either party; the sponsored department declines it).</summary>
		Task<LmsSponsorshipErrors> EndAsync(ContentSetActor actor, int lmsSponsorshipId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The department's sponsorship that pays for its seats now: active, the link still shares Training toward it and the sponsor's add-on is
		/// entitled. Null when there is none (the department's own add-on, if any, decides).
		/// </summary>
		Task<LmsSponsorshipGrant> GetGrantAsync(int departmentId);

		/// <summary>
		/// The department's course copies the LMS runtime may run through a sponsorship, each with the time its sponsorship stopped paying (null
		/// while it pays). Copies of an ended sponsorship stay listed through the lapse windows so lapse rules apply from that time.
		/// </summary>
		Task<Dictionary<int, DateTime?>> GetSponsoredCourseIdsAsync(int departmentId);

		/// <summary>The department's learning path copies a sponsorship lets it assign, on the same terms as <see cref="GetSponsoredCourseIdsAsync"/>.</summary>
		Task<Dictionary<int, DateTime?>> GetSponsoredPathIdsAsync(int departmentId);

		/// <summary>Seats the sponsor's sponsored departments hold in its packs now.</summary>
		Task<int> GetSponsoredSeatsAsync(int sponsorDepartmentId);

		/// <summary>Ends sponsorships whose link no longer shares Training (called on revocation and by worker 100).</summary>
		Task<int> EndLapsedAsync(int? departmentLinkId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>A sponsorship that pays for a department's seats now.</summary>
	public sealed class LmsSponsorshipGrant
	{
		public LmsSponsorship Sponsorship { get; set; }
		public string SponsorName { get; set; }
		/// <summary>Seats the department may hold under the sponsorship.</summary>
		public int Capacity { get; set; }
		public int SponsorPacks { get; set; }
	}

	/// <summary>
	/// Mutual-aid agreement countersignature over a Department Link (round 10 A3, e-Sign S4): the approved agreement is routed to the partner
	/// department, where any department admin countersigns it at L2 against the same content hash (a multi-signer request in the partner
	/// department, subject <see cref="SignatureSubjectKinds.MutualAidAgreementCountersignature"/>). The link is re-checked on every read and sign.
	/// </summary>
	public interface IMutualAidCountersignService
	{
		/// <summary>Opens the partner's request once the department's chain approved an agreement that requires it. Never fatal to the approval.</summary>
		Task<Approvals.MutualAidAgreement> RequestAsync(int departmentId, int mutualAidAgreementId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The agreement with its countersignature state brought up to date (a declined, expired or voided request is recorded).</summary>
		Task<Approvals.MutualAidAgreement> RefreshAsync(Approvals.MutualAidAgreement agreement, CancellationToken cancellationToken = default);

		/// <summary>Agreements linked departments ask this department to countersign (with the open request in this department).</summary>
		Task<List<MutualAidCountersignItem>> GetAwaitingAsync(int partnerDepartmentId);

		/// <summary>Voids the open requests over a link that no longer exists or is disabled (revocation, worker 100).</summary>
		Task<int> LapseForLinkAsync(int? departmentLinkId, CancellationToken cancellationToken = default);
	}

	public sealed class MutualAidCountersignItem
	{
		public Approvals.MutualAidAgreement Agreement { get; set; }
		public string OriginDepartmentName { get; set; }
		public SignatureRequestRecord Request { get; set; }
	}
}
