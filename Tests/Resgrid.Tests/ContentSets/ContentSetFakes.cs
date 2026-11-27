using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.ContentSets
{
	/// <summary>An in-memory repository: rows keyed by their IdValue (identity assigned on insert).</summary>
	public class FakeRepository<T> : IRepository<T> where T : class, IEntity
	{
		public readonly List<T> Rows = new List<T>();
		private int _next = 1;

		public Task<IEnumerable<T>> GetAllAsync() => Task.FromResult<IEnumerable<T>>(Rows.ToList());

		public Task<T> GetByIdAsync(object id) => Task.FromResult(Rows.FirstOrDefault(r => Equals(Convert.ToInt32(r.IdValue), Convert.ToInt32(id))));

		public Task<IEnumerable<T>> GetAllByDepartmentIdAsync(int departmentId) => throw new NotSupportedException("generic department query trap");

		public virtual Task<T> InsertAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			entity.IdValue = _next++;
			Rows.Add(entity);
			return Task.FromResult(entity);
		}

		public Task<T> UpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var index = Rows.FindIndex(r => Equals(r.IdValue, entity.IdValue));
			if (index >= 0)
				Rows[index] = entity;
			return Task.FromResult(entity);
		}

		public Task<bool> DeleteAsync(T entity, CancellationToken cancellationToken) => Task.FromResult(Rows.Remove(entity));

		public Task<T> SaveOrUpdateAsync(T entity, CancellationToken cancellationToken, bool firstLevelOnly = false) =>
			Convert.ToInt32(entity.IdValue) > 0 ? UpdateAsync(entity, cancellationToken) : InsertAsync(entity, cancellationToken);

		public Task<IEnumerable<T>> GetAllByUserIdAsync(string userId) => throw new NotSupportedException();

		public Task<bool> DeleteMultipleAsync(T entity, string parentKeyName, object parentKeyId, List<object> ids, CancellationToken cancellationToken) => throw new NotSupportedException();
	}

	public sealed class FakeSets : FakeRepository<ContentSet>, IContentSetsRepository
	{
		public Task<ContentSet> GetByIdForDepartmentAsync(int departmentId, int contentSetId) =>
			Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.ContentSetId == contentSetId));

		public Task<List<ContentSet>> GetForDepartmentAsync(int departmentId, ContentSetKinds kind, bool includeArchived) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.Kind == (int)kind && (includeArchived || !r.IsArchived)).ToList());

		public Task<List<ContentSet>> GetLiveForPublishersAsync(IEnumerable<int> publisherDepartmentIds, ContentSetKinds kind) =>
			Task.FromResult(Rows.Where(r => publisherDepartmentIds.Contains(r.DepartmentId) && r.Kind == (int)kind && !r.IsArchived).ToList());
	}

	public sealed class FakeItems : FakeRepository<ContentSetItem>, IContentSetItemsRepository
	{
		public Task<List<ContentSetItem>> GetForSetAsync(int contentSetId, bool includeRemoved) =>
			Task.FromResult(Rows.Where(r => r.ContentSetId == contentSetId && (includeRemoved || !r.IsRemoved)).ToList());

		public Task<List<ContentSetItem>> GetLiveByKindAsync(ContentSetItemKinds itemKind, int take) =>
			Task.FromResult(Rows.Where(r => r.ItemKind == (int)itemKind && !r.IsRemoved).Take(take).ToList());

		public Task<List<ContentSetItem>> GetLiveForItemAsync(int publisherDepartmentId, ContentSetItemKinds itemKind, int itemId) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == publisherDepartmentId && r.ItemKind == (int)itemKind && r.ItemId == itemId && !r.IsRemoved).ToList());
	}

	public sealed class FakeSubscriptions : FakeRepository<ContentSetSubscription>, IContentSetSubscriptionsRepository
	{
		private static bool Open(ContentSetSubscription s) => s.Status != (int)ContentSetSubscriptionStatuses.Ended;

		public Task<ContentSetSubscription> GetByIdForDepartmentAsync(int departmentId, int id) =>
			Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.ContentSetSubscriptionId == id));

		public Task<List<ContentSetSubscription>> GetForDepartmentAsync(int departmentId, bool includeEnded) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && (includeEnded || Open(r))).ToList());

		public Task<List<ContentSetSubscription>> GetOpenForSetAsync(int contentSetId) => Task.FromResult(Rows.Where(r => r.ContentSetId == contentSetId && Open(r)).ToList());

		public Task<ContentSetSubscription> GetOpenAsync(int contentSetId, int departmentId) =>
			Task.FromResult(Rows.FirstOrDefault(r => r.ContentSetId == contentSetId && r.DepartmentId == departmentId && Open(r)));

		public Task<List<ContentSetSubscription>> GetOpenForLinkAsync(int departmentLinkId) => Task.FromResult(Rows.Where(r => r.DepartmentLinkId == departmentLinkId && Open(r)).ToList());

		public Task<List<ContentSetSubscription>> GetAllOpenAsync(int take) => Task.FromResult(Rows.Where(Open).Take(take).ToList());

		public Task<List<ContentSetSubscription>> GetOpenBetweenAsync(int publisherDepartmentId, int subscriberDepartmentId) =>
			Task.FromResult(Rows.Where(r => r.PublisherDepartmentId == publisherDepartmentId && r.DepartmentId == subscriberDepartmentId && Open(r)).ToList());
	}

	public sealed class FakeCopies : FakeRepository<ContentSetCopy>, IContentSetCopiesRepository
	{
		public Task<ContentSetCopy> GetByIdForDepartmentAsync(int departmentId, int id) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.ContentSetCopyId == id));

		public Task<List<ContentSetCopy>> GetForSubscriptionAsync(int contentSetSubscriptionId) => Task.FromResult(Rows.Where(r => r.ContentSetSubscriptionId == contentSetSubscriptionId).ToList());

		public Task<ContentSetCopy> GetManagedForLocalItemAsync(int departmentId, ContentSetItemKinds itemKind, int localItemId) =>
			Task.FromResult(Rows.LastOrDefault(r => r.DepartmentId == departmentId && r.ItemKind == (int)itemKind && r.LocalItemId == localItemId && r.IsManaged));

		public Task<List<ContentSetCopy>> GetManagedForDepartmentAsync(int departmentId, ContentSetItemKinds itemKind) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.ItemKind == (int)itemKind && r.IsManaged).ToList());

		public Task<int> DetachAsync(int contentSetSubscriptionId, int? contentSetItemId, DateTime detachedOnUtc, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(r => r.ContentSetSubscriptionId == contentSetSubscriptionId && r.IsManaged && (!contentSetItemId.HasValue || r.ContentSetItemId == contentSetItemId.Value)).ToList();
			rows.ForEach(r => r.DetachedOn = detachedOnUtc);
			return Task.FromResult(rows.Count);
		}
	}

	public sealed class FakeUpdates : FakeRepository<ContentSetUpdate>, IContentSetUpdatesRepository
	{
		public Task<ContentSetUpdate> GetByIdForDepartmentAsync(int departmentId, int id) => Task.FromResult(Rows.FirstOrDefault(r => r.DepartmentId == departmentId && r.ContentSetUpdateId == id));

		public Task<List<ContentSetUpdate>> GetForDepartmentAsync(int departmentId, bool openOnly, int take) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && (!openOnly || r.IsOpen)).OrderByDescending(r => r.ContentSetUpdateId).Take(take).ToList());

		public Task<List<ContentSetUpdate>> GetOpenForItemAsync(int contentSetSubscriptionId, int contentSetItemId) =>
			Task.FromResult(Rows.Where(r => r.ContentSetSubscriptionId == contentSetSubscriptionId && r.ContentSetItemId == contentSetItemId && r.IsOpen).ToList());

		public Task<List<ContentSetUpdate>> GetDueAsync(DateTime utcNow, TimeSpan claimTimeout, int take) =>
			Task.FromResult(Rows.Where(r => r.Status == (int)ContentSetUpdateStatuses.Scheduled && r.ApplyOn <= utcNow && (!r.ClaimedOn.HasValue || r.ClaimedOn < utcNow - claimTimeout)).Take(take).ToList());

		public FakeSubscriptions Subscriptions { get; set; }

		public Task<List<ContentSetUpdate>> GetUndecidedDueAsync(DateTime utcNow, int take) =>
			Task.FromResult(Rows.Where(u => (u.Status == (int)ContentSetUpdateStatuses.Pending || u.Status == (int)ContentSetUpdateStatuses.Deferred) &&
				Subscriptions.Rows.Any(s => s.ContentSetSubscriptionId == u.ContentSetSubscriptionId && s.IsActive &&
					((u.Status == (int)ContentSetUpdateStatuses.Pending && s.AutoAccept) || (u.DeadlineOn.HasValue && u.DeadlineOn <= utcNow) ||
					 (u.Status == (int)ContentSetUpdateStatuses.Deferred && u.DeferredUntil.HasValue && u.DeferredUntil <= utcNow)))).Take(take).ToList());

		public Task<bool> TryClaimAsync(int id, DateTime utcNow, TimeSpan claimTimeout, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.ContentSetUpdateId == id && r.Status == (int)ContentSetUpdateStatuses.Scheduled && (!r.ClaimedOn.HasValue || r.ClaimedOn < utcNow - claimTimeout));
			if (row == null)
				return Task.FromResult(false);
			row.ClaimedOn = utcNow;
			return Task.FromResult(true);
		}

		// Rows are shared objects in memory: the service mutates the instance it read before the compare-and-set, so the expected status is
		// checked against the status the row had when the test last stored it.
		public readonly Dictionary<int, int> Stored = new Dictionary<int, int>();

		public override Task<ContentSetUpdate> InsertAsync(ContentSetUpdate entity, CancellationToken cancellationToken, bool firstLevelOnly = false)
		{
			var inserted = base.InsertAsync(entity, cancellationToken).Result;
			Stored[inserted.ContentSetUpdateId] = inserted.Status;
			return Task.FromResult(inserted);
		}

		public Task<bool> TryUpdateFromStatusAsync(ContentSetUpdate update, ContentSetUpdateStatuses expectedStatus, CancellationToken cancellationToken = default)
		{
			if (!Stored.TryGetValue(update.ContentSetUpdateId, out var status) || status != (int)expectedStatus)
				return Task.FromResult(false);
			Stored[update.ContentSetUpdateId] = update.Status;
			var index = Rows.FindIndex(r => r.ContentSetUpdateId == update.ContentSetUpdateId);
			Rows[index] = update;
			return Task.FromResult(true);
		}

		public Task<int> CloseOpenForSubscriptionAsync(int contentSetSubscriptionId, ContentSetUpdateStatuses status, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var rows = Rows.Where(r => r.ContentSetSubscriptionId == contentSetSubscriptionId && r.IsOpen).ToList();
			foreach (var row in rows)
			{
				row.Status = (int)status;
				Stored[row.ContentSetUpdateId] = row.Status;
			}
			return Task.FromResult(rows.Count);
		}
	}

	public sealed class FakeProposals : FakeRepository<ContentSetProposal>, IContentSetProposalsRepository
	{
		public Task<List<ContentSetProposal>> GetForPublisherAsync(int publisherDepartmentId, bool openOnly, int take) =>
			Task.FromResult(Rows.Where(r => r.PublisherDepartmentId == publisherDepartmentId && (!openOnly || r.Status == (int)ContentSetProposalStatuses.Open)).Take(take).ToList());

		public Task<List<ContentSetProposal>> GetForDepartmentAsync(int departmentId, int take) => Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId).Take(take).ToList());

		public Task<ContentSetProposal> GetForPublisherByIdAsync(int publisherDepartmentId, int id) =>
			Task.FromResult(Rows.FirstOrDefault(r => r.PublisherDepartmentId == publisherDepartmentId && r.ContentSetProposalId == id));
	}

	public sealed class FakeSponsorships : FakeRepository<LmsSponsorship>, ILmsSponsorshipsRepository
	{
		public Task<List<LmsSponsorship>> GetActiveForDepartmentAsync(int departmentId) => Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId && r.IsActive).ToList());

		public Task<List<LmsSponsorship>> GetActiveForSponsorAsync(int sponsorDepartmentId) => Task.FromResult(Rows.Where(r => r.SponsorDepartmentId == sponsorDepartmentId && r.IsActive).ToList());

		public Task<List<LmsSponsorship>> GetForPartyAsync(int departmentId, int take) =>
			Task.FromResult(Rows.Where(r => r.DepartmentId == departmentId || r.SponsorDepartmentId == departmentId).OrderByDescending(r => r.LmsSponsorshipId).Take(take).ToList());

		public Task<LmsSponsorship> GetActiveAsync(int sponsorDepartmentId, int departmentId) =>
			Task.FromResult(Rows.FirstOrDefault(r => r.SponsorDepartmentId == sponsorDepartmentId && r.DepartmentId == departmentId && r.IsActive));

		public Task<List<LmsSponsorship>> GetAllActiveAsync(int take) => Task.FromResult(Rows.Where(r => r.IsActive).Take(take).ToList());

		public Task<List<LmsSponsorship>> GetActiveForLinkAsync(int departmentLinkId) => Task.FromResult(Rows.Where(r => r.DepartmentLinkId == departmentLinkId && r.IsActive).ToList());

		public Task<bool> TryEndAsync(int id, LmsSponsorshipEndReasons reason, string userId, DateTime endedOnUtc, CancellationToken cancellationToken = default)
		{
			var row = Rows.FirstOrDefault(r => r.LmsSponsorshipId == id && r.IsActive);
			if (row == null)
				return Task.FromResult(false);
			row.Status = (int)LmsSponsorshipStatuses.Ended;
			row.EndReason = (int)reason;
			row.EndedByUserId = userId;
			row.EndedOn = endedOnUtc;
			return Task.FromResult(true);
		}
	}

	/// <summary>
	/// A set item source over in-memory publisher items (the set machinery tested without the protocol library): each publisher item has a
	/// current version; a copy is a local item id with the merged block keys it carries.
	/// </summary>
	public sealed class FakeItemSource : IContentSetItemSource
	{
		public sealed class Item
		{
			public int DepartmentId;
			public int ItemId;
			public string Title;
			public int VersionId;
			public int Major = 1;
			public string Label = "1.0";
			public DateTime? EffectiveOn;
			public string Problem;
			public ProtocolContentDocument Document;
		}

		public readonly List<Item> Items = new List<Item>();
		public readonly Dictionary<int, ProtocolContentDocument> Local = new Dictionary<int, ProtocolContentDocument>();
		public readonly List<(int Department, int LocalId, int SourceVersion)> Applied = new List<(int, int, int)>();
		private int _nextLocal = 1000;

		public FakeItemSource(ContentSetItemKinds kind = ContentSetItemKinds.Protocol, bool needsLms = false)
		{
			Kind = kind;
			NeedsLms = needsLms;
		}

		public ContentSetItemKinds Kind { get; }
		public bool NeedsLms { get; }
		public ContentSetKinds SetKind => Kind == ContentSetItemKinds.Protocol ? ContentSetKinds.Protocols : ContentSetKinds.Training;

		private static ContentSetSourceItem Describe(Item i) => new ContentSetSourceItem
		{
			Kind = ContentSetItemKinds.Protocol, ItemId = i.ItemId, Title = i.Title, VersionId = i.VersionId, VersionLabel = i.Label, Major = i.Major,
			EffectiveOn = i.EffectiveOn, Problem = i.Problem, ContentJson = i.Document?.ToJson(),
			Blocks = (i.Document?.Blocks ?? new List<ProtocolContentBlock>()).Select(b => new ContentSetBlockOption { Key = b.Key, Label = b.Key }).ToList()
		};

		public Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId) =>
			Task.FromResult(Items.Where(i => i.DepartmentId == publisherDepartmentId).Select(i => new ContentSetCandidate { Kind = Kind, ItemId = i.ItemId, Title = i.Title, Problem = i.Problem }).ToList());

		public Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId)
		{
			var item = Items.FirstOrDefault(i => i.DepartmentId == publisherDepartmentId && i.ItemId == itemId);
			return Task.FromResult(item == null ? null : Describe(item));
		}

		public Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId) => DescribeAsync(publisherDepartmentId, itemId);

		private ProtocolContentDocument Merge(ContentSetCopyContext context) =>
			ContentSetMerge.Merge(ProtocolContentDocument.Parse(context.Source.ContentJson, null), (ContentSetLockModes)context.Item.LockMode, context.Item.CustomizableKeys, context.Local, out _);

		public Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			var id = _nextLocal++;
			Local[id] = Merge(context);
			Applied.Add((context.SubscriberDepartmentId, id, context.Source.VersionId));
			return Task.FromResult(new ContentSetCopyOutcome { Succeeded = true, LocalItemId = id, LocalVersionId = id * 10 });
		}

		public Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			Local[context.Copy.LocalItemId] = Merge(context);
			Applied.Add((context.SubscriberDepartmentId, context.Copy.LocalItemId, context.Source.VersionId));
			return Task.FromResult(new ContentSetCopyOutcome { Succeeded = true, LocalItemId = context.Copy.LocalItemId, LocalVersionId = context.Source.VersionId * 100 });
		}

		public Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId) =>
			Task.FromResult(Local.TryGetValue(localItemId, out var d) ? d.Render() : string.Empty);

		public Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context)
		{
			var merged = ContentSetMerge.Merge(ProtocolContentDocument.Parse(context.Source.ContentJson, null), (ContentSetLockModes)context.Item.LockMode, context.Item.CustomizableKeys,
				context.Local, out var dropped);
			return Task.FromResult((merged.Render(), dropped));
		}

		public Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId) => Task.FromResult(Local.ContainsKey(localItemId));
	}

	/// <summary>A learning-path-like source: an LMS item that names other items (its steps) which must be in the set first.</summary>
	public sealed class FakePathSource : IContentSetItemSource, IContentSetItemDependencies
	{
		public readonly FakeItemSource Inner = new FakeItemSource(ContentSetItemKinds.LmsPath, true);
		public readonly Dictionary<int, List<KeyValuePair<ContentSetItemKinds, int>>> Dependencies = new Dictionary<int, List<KeyValuePair<ContentSetItemKinds, int>>>();

		public ContentSetItemKinds Kind => ContentSetItemKinds.LmsPath;
		public bool NeedsLms => true;
		public ContentSetKinds SetKind => ContentSetKinds.Training;
		public Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId) => Inner.GetCandidatesAsync(publisherDepartmentId);
		public Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId) => Inner.DescribeAsync(publisherDepartmentId, itemId);
		public Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId) => Inner.DescribeVersionAsync(publisherDepartmentId, itemId, versionId);
		public Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default) => Inner.CreateCopyAsync(context, cancellationToken);
		public Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default) => Inner.ApplyVersionAsync(context, cancellationToken);
		public Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId) => Inner.GetLocalHtmlAsync(subscriberDepartmentId, localItemId);
		public Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context) => Inner.PreviewAsync(context);
		public Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId) => Inner.LocalExistsAsync(subscriberDepartmentId, localItemId);

		public Task<List<KeyValuePair<ContentSetItemKinds, int>>> GetDependenciesAsync(int publisherDepartmentId, int itemId) =>
			Task.FromResult(Dependencies.TryGetValue(itemId, out var list) ? list : new List<KeyValuePair<ContentSetItemKinds, int>>());
	}
}
