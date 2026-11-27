using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Repositories
{
	/// <summary>Publishers' protocol and course sets (round 10 A3, M0415).</summary>
	public interface IContentSetsRepository : IRepository<ContentSet>
	{
		Task<ContentSet> GetByIdForDepartmentAsync(int departmentId, int contentSetId);

		/// <summary>The publisher's sets of one kind (archived ones too when asked), newest first.</summary>
		Task<List<ContentSet>> GetForDepartmentAsync(int departmentId, ContentSetKinds kind, bool includeArchived);

		/// <summary>The live (not archived) sets of one kind of the given publishers, by name.</summary>
		Task<List<ContentSet>> GetLiveForPublishersAsync(IEnumerable<int> publisherDepartmentIds, ContentSetKinds kind);
	}

	public interface IContentSetItemsRepository : IRepository<ContentSetItem>
	{
		/// <summary>A set's items in order (removed ones too when asked).</summary>
		Task<List<ContentSetItem>> GetForSetAsync(int contentSetId, bool includeRemoved);

		/// <summary>Live items of one kind across every set (worker 100 re-reads unversioned items: baseline trainings).</summary>
		Task<List<ContentSetItem>> GetLiveByKindAsync(ContentSetItemKinds itemKind, int take);

		/// <summary>The live set items that carry one of the publisher's items (the publish hook's read).</summary>
		Task<List<ContentSetItem>> GetLiveForItemAsync(int publisherDepartmentId, ContentSetItemKinds itemKind, int itemId);
	}

	public interface IContentSetSubscriptionsRepository : IRepository<ContentSetSubscription>
	{
		Task<ContentSetSubscription> GetByIdForDepartmentAsync(int departmentId, int contentSetSubscriptionId);

		/// <summary>The subscriber's subscriptions (ended ones too when asked), newest first.</summary>
		Task<List<ContentSetSubscription>> GetForDepartmentAsync(int departmentId, bool includeEnded);

		/// <summary>A set's open (active or suspended) subscriptions.</summary>
		Task<List<ContentSetSubscription>> GetOpenForSetAsync(int contentSetId);

		/// <summary>The subscriber's open subscription to the set, or null.</summary>
		Task<ContentSetSubscription> GetOpenAsync(int contentSetId, int departmentId);

		/// <summary>Open subscriptions made over a link (either direction).</summary>
		Task<List<ContentSetSubscription>> GetOpenForLinkAsync(int departmentLinkId);

		/// <summary>Open (active or suspended) subscriptions of every department, oldest first (worker 100's link check).</summary>
		Task<List<ContentSetSubscription>> GetAllOpenAsync(int take);

		/// <summary>Open subscriptions between a publisher and a subscriber (any set).</summary>
		Task<List<ContentSetSubscription>> GetOpenBetweenAsync(int publisherDepartmentId, int subscriberDepartmentId);
	}

	public interface IContentSetCopiesRepository : IRepository<ContentSetCopy>
	{
		Task<ContentSetCopy> GetByIdForDepartmentAsync(int departmentId, int contentSetCopyId);

		/// <summary>A subscription's copies (detached ones too).</summary>
		Task<List<ContentSetCopy>> GetForSubscriptionAsync(int contentSetSubscriptionId);

		/// <summary>The managed copy behind one of the subscriber's own items, or null (the edit lock's read).</summary>
		Task<ContentSetCopy> GetManagedForLocalItemAsync(int departmentId, ContentSetItemKinds itemKind, int localItemId);

		/// <summary>The subscriber's managed copies of one kind.</summary>
		Task<List<ContentSetCopy>> GetManagedForDepartmentAsync(int departmentId, ContentSetItemKinds itemKind);

		/// <summary>Detaches every managed copy of a subscription (or of one set item when given); returns how many.</summary>
		Task<int> DetachAsync(int contentSetSubscriptionId, int? contentSetItemId, DateTime detachedOnUtc, CancellationToken cancellationToken = default);
	}

	public interface IContentSetUpdatesRepository : IRepository<ContentSetUpdate>
	{
		Task<ContentSetUpdate> GetByIdForDepartmentAsync(int departmentId, int contentSetUpdateId);

		/// <summary>The subscriber's inbox: open rows (pending, deferred, scheduled) or every row of the last <paramref name="take"/>.</summary>
		Task<List<ContentSetUpdate>> GetForDepartmentAsync(int departmentId, bool openOnly, int take);

		/// <summary>The open rows of one subscription and set item (a newer version supersedes them).</summary>
		Task<List<ContentSetUpdate>> GetOpenForItemAsync(int contentSetSubscriptionId, int contentSetItemId);

		/// <summary>Scheduled rows whose apply time has come and that no host claimed in the last <paramref name="claimTimeout"/>.</summary>
		Task<List<ContentSetUpdate>> GetDueAsync(DateTime utcNow, TimeSpan claimTimeout, int take);

		/// <summary>
		/// Pending or deferred rows of active subscriptions a person no longer has to decide: the subscription auto-accepts, a locked item's
		/// deadline passed, or the deferral ended.
		/// </summary>
		Task<List<ContentSetUpdate>> GetUndecidedDueAsync(DateTime utcNow, int take);

		/// <summary>Claims a due row for this host; false when another host holds a fresh claim or it is no longer scheduled.</summary>
		Task<bool> TryClaimAsync(int contentSetUpdateId, DateTime utcNow, TimeSpan claimTimeout, CancellationToken cancellationToken = default);

		/// <summary>Writes the row only while its status is still <paramref name="expectedStatus"/>; false when it moved since it was read.</summary>
		Task<bool> TryUpdateFromStatusAsync(ContentSetUpdate update, ContentSetUpdateStatuses expectedStatus, CancellationToken cancellationToken = default);

		/// <summary>Supersedes (or, with <paramref name="status"/>, closes) every open row of a subscription; returns how many.</summary>
		Task<int> CloseOpenForSubscriptionAsync(int contentSetSubscriptionId, ContentSetUpdateStatuses status, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	public interface IContentSetProposalsRepository : IRepository<ContentSetProposal>
	{
		/// <summary>Proposals made to a publisher (open only when asked), newest first.</summary>
		Task<List<ContentSetProposal>> GetForPublisherAsync(int publisherDepartmentId, bool openOnly, int take);

		/// <summary>Proposals the department made upstream, newest first.</summary>
		Task<List<ContentSetProposal>> GetForDepartmentAsync(int departmentId, int take);

		Task<ContentSetProposal> GetForPublisherByIdAsync(int publisherDepartmentId, int contentSetProposalId);
	}

	/// <summary>Sponsored LMS seats (round 10 A3, M0416).</summary>
	public interface ILmsSponsorshipsRepository : IRepository<LmsSponsorship>
	{
		/// <summary>The department's active sponsorships (who pays for its seats).</summary>
		Task<List<LmsSponsorship>> GetActiveForDepartmentAsync(int departmentId);

		/// <summary>The sponsor's active sponsorships (whom it pays for).</summary>
		Task<List<LmsSponsorship>> GetActiveForSponsorAsync(int sponsorDepartmentId);

		/// <summary>Every sponsorship either party has had, newest first (the history on both screens).</summary>
		Task<List<LmsSponsorship>> GetForPartyAsync(int departmentId, int take);

		Task<LmsSponsorship> GetActiveAsync(int sponsorDepartmentId, int departmentId);

		/// <summary>Every active sponsorship, oldest first (worker 100's link check).</summary>
		Task<List<LmsSponsorship>> GetAllActiveAsync(int take);

		/// <summary>Active sponsorships made over a link.</summary>
		Task<List<LmsSponsorship>> GetActiveForLinkAsync(int departmentLinkId);

		/// <summary>Ends the sponsorship once; false when it already ended.</summary>
		Task<bool> TryEndAsync(int lmsSponsorshipId, LmsSponsorshipEndReasons reason, string userId, DateTime endedOnUtc, CancellationToken cancellationToken = default);
	}
}
