using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// Protocols P5 regional publisher/subscriber and Training T5 shared course sets (round 10 A3). See <see cref="IContentSetService"/>.
	/// <para>
	/// Access: a set is offered to, subscribed by and updated for a department only while an enabled Department Link shares the set's kind
	/// (Protocols, Training) from the publisher toward it (<see cref="DepartmentLinkSharing.Shares"/>), checked on every operation. Managing
	/// needs Manage Content Sets (permission 136) in the acting department; proposing upstream needs only to be a current member.
	/// </para>
	/// <para>
	/// Timing: an accepted update goes into force at the subscriber's update time (setting 151, default 07:00 local) on or after the publisher's
	/// effective date, or at a later time the subscriber picks; worker 100 applies it. A locked item's update can be deferred to its deadline
	/// (the publisher's effective date + the set's defer window) and is scheduled by the worker when the deadline passes.
	/// </para>
	/// </summary>
	public class ContentSetService : IContentSetService
	{
		public const int WorkerBatch = 200;
		public static readonly TimeSpan ClaimTimeout = TimeSpan.FromMinutes(15);

		private readonly IContentSetsRepository _sets;
		private readonly IContentSetItemsRepository _items;
		private readonly IContentSetSubscriptionsRepository _subscriptions;
		private readonly IContentSetCopiesRepository _copies;
		private readonly IContentSetUpdatesRepository _updates;
		private readonly IContentSetProposalsRepository _proposals;
		private readonly IDepartmentLinksService _links;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _settings;
		private readonly IPermissionsService _permissions;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IUserProfileService _profiles;
		private readonly IEnumerable<IContentSetItemSource> _sources;
		private readonly ILmsAccessService _lmsAccess;
		private readonly ILmsSponsorshipService _sponsorships;
		private readonly IProtocolCommentsRepository _protocolComments;
		private readonly IProtocolLibraryService _library;
		private readonly IContentSetNotifier _notifier;
		private readonly IEventAggregator _events;

		public ContentSetService(IContentSetsRepository sets, IContentSetItemsRepository items, IContentSetSubscriptionsRepository subscriptions,
			IContentSetCopiesRepository copies, IContentSetUpdatesRepository updates, IContentSetProposalsRepository proposals, IDepartmentLinksService links,
			IDepartmentsService departments, IDepartmentSettingsService settings, IPermissionsService permissions, IDepartmentGroupsService groups,
			IPersonnelRolesService roles, IUserProfileService profiles, IEnumerable<IContentSetItemSource> sources, ILmsAccessService lmsAccess,
			ILmsSponsorshipService sponsorships, IProtocolCommentsRepository protocolComments, IProtocolLibraryService library, IContentSetNotifier notifier,
			IEventAggregator events)
		{
			_sets = sets;
			_items = items;
			_subscriptions = subscriptions;
			_copies = copies;
			_updates = updates;
			_proposals = proposals;
			_links = links;
			_departments = departments;
			_settings = settings;
			_permissions = permissions;
			_groups = groups;
			_roles = roles;
			_profiles = profiles;
			_sources = sources ?? Enumerable.Empty<IContentSetItemSource>();
			_lmsAccess = lmsAccess;
			_sponsorships = sponsorships;
			_protocolComments = protocolComments;
			_library = library;
			_notifier = notifier;
			_events = events;
		}

		/// <summary>The link share kind a set kind travels on.</summary>
		public static DepartmentLinkShareTypes ShareKindFor(ContentSetKinds kind) =>
			kind == ContentSetKinds.Training ? DepartmentLinkShareTypes.Training : DepartmentLinkShareTypes.Protocols;

		private IContentSetItemSource Source(ContentSetItemKinds kind) => _sources.FirstOrDefault(s => s.Kind == kind);

		private IContentSetItemSource Source(int kind) => Source((ContentSetItemKinds)kind);

		#region Actor and settings

		public async Task<ContentSetActor> GetActorAsync(int departmentId, string userId)
		{
			var actor = ContentSetActor.Nobody(departmentId);
			actor.UserId = userId;
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return actor;

			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			if (department == null || department.DepartmentId != departmentId)
				return actor;

			var member = await _departments.GetDepartmentMemberAsync(userId, departmentId, false);
			var owner = string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase);
			actor.IsCurrentMember = owner || DepartmentMemberStateHelper.IsCurrentMember(member, departmentId);
			if (!actor.IsCurrentMember)
				return actor;

			actor.IsDepartmentAdmin = owner || department.IsUserAnAdmin(userId);
			var group = await _groups.GetGroupForUserAsync(userId, departmentId);
			var isGroupAdmin = group != null && group.DepartmentId == departmentId && group.IsUserGroupAdmin(userId);
			var roleIds = ((await _roles.GetRolesForUserAsync(userId, departmentId)) ?? new List<PersonnelRole>())
				.Where(r => r != null && r.DepartmentId == departmentId).Select(r => r.PersonnelRoleId).ToList();
			actor.CanManage = PermissionRights.Holds(await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, PermissionTypes.ManageContentSets),
				actor.IsDepartmentAdmin, isGroupAdmin, roleIds);
			actor.Name = (await _profiles.GetProfileByUserIdAsync(userId))?.FullName?.AsFirstNameLastName?.Trim();
			return actor;
		}

		public async Task<ContentSetSettings> GetSettingsAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new ContentSetSettings();
			try
			{
				return ContentSetSettings.Parse((await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.ContentSetSettings))?.Setting);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Content set settings could not be read; defaults apply.");
				return new ContentSetSettings();
			}
		}

		public async Task<ContentSetSettings> SaveSettingsAsync(ContentSetActor actor, ContentSetSettings settings, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage || settings == null)
				return null;
			var normalized = ContentSetSettings.Parse(settings.ToJson());
			await _settings.SaveOrUpdateSettingAsync(actor.DepartmentId, normalized.ToJson(), DepartmentSettingTypes.ContentSetSettings, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { settings = normalized });
			return normalized;
		}

		/// <summary>The department's time zone setting (resolved by <see cref="ContentSetSchedule.Zone"/>).</summary>
		private async Task<string> ZoneAsync(int departmentId)
		{
			try
			{
				return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.TimeZone;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		private async Task<string> DepartmentNameAsync(int departmentId)
		{
			try
			{
				return (await _departments.GetDepartmentByIdAsync(departmentId, false))?.Name;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
		}

		#endregion Actor and settings

		#region Link and LMS checks

		/// <summary>The enabled link over which the publisher shares the set's kind toward the subscriber now, or null.</summary>
		private async Task<DepartmentLink> SharingLinkAsync(int publisherDepartmentId, int subscriberDepartmentId, ContentSetKinds kind, int? preferredLinkId = null)
		{
			if (preferredLinkId.HasValue && preferredLinkId.Value > 0)
			{
				var preferred = await _links.GetLinkByIdAsync(preferredLinkId.Value);
				if (DepartmentLinkSharing.Shares(preferred, publisherDepartmentId, subscriberDepartmentId, ShareKindFor(kind)))
					return preferred;
			}

			var links = await _links.GetAllLinksForDepartmentAsync(subscriberDepartmentId) ?? new List<DepartmentLink>();
			return DepartmentLinkSharing.FindSharingLink(links, publisherDepartmentId, subscriberDepartmentId, ShareKindFor(kind));
		}

		/// <summary>The subscriber may receive the publisher's LMS items: its own LMS add-on, or the publisher sponsors it.</summary>
		private async Task<(bool CanRun, bool Sponsored)> CanRunLmsAsync(int subscriberDepartmentId, int publisherDepartmentId)
		{
			try
			{
				var grant = await _sponsorships.GetGrantAsync(subscriberDepartmentId);
				if (grant?.Sponsorship != null && grant.Sponsorship.SponsorDepartmentId == publisherDepartmentId)
					return (true, true);
				return (await _lmsAccess.CanAuthorAndManageAsync(subscriberDepartmentId), false);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Whether the department can run LMS set items could not be read.");
				return (false, false);
			}
		}

		#endregion Link and LMS checks

		#region Publisher

		private async Task<ContentSet> OwnSetAsync(ContentSetActor actor, int contentSetId) =>
			actor == null || !actor.CanManage || contentSetId <= 0 ? null : await _sets.GetByIdForDepartmentAsync(actor.DepartmentId, contentSetId);

		public async Task<List<ContentSetDetail>> GetPublishedSetsAsync(ContentSetActor actor, ContentSetKinds kind)
		{
			var result = new List<ContentSetDetail>();
			if (actor == null || !actor.CanManage)
				return result;

			foreach (var set in await _sets.GetForDepartmentAsync(actor.DepartmentId, kind, true) ?? new List<ContentSet>())
			{
				var items = await _items.GetForSetAsync(set.ContentSetId, false) ?? new List<ContentSetItem>();
				var subscribers = await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>();
				result.Add(new ContentSetDetail
				{
					Set = set,
					Items = items.Select(i => new ContentSetItemView { Item = i }).ToList(),
					Subscribers = subscribers.Select(s => new ContentSetSubscriberView { Subscription = s }).ToList()
				});
			}

			return result;
		}

		public async Task<ContentSetDetail> GetSetAsync(ContentSetActor actor, int contentSetId)
		{
			var set = await OwnSetAsync(actor, contentSetId);
			if (set == null)
				return null;

			var detail = new ContentSetDetail { Set = set };
			foreach (var item in await _items.GetForSetAsync(set.ContentSetId, false) ?? new List<ContentSetItem>())
				detail.Items.Add(await ItemViewAsync(item));

			var sponsored = new HashSet<int>();
			try
			{
				foreach (var view in await _sponsorships.GetForDepartmentAsync(actor.DepartmentId) ?? new List<Model.Lms.LmsSponsorshipView>())
					if (view.Sponsorship != null && view.Sponsorship.IsActive && view.Sponsorship.SponsorDepartmentId == actor.DepartmentId)
						sponsored.Add(view.Sponsorship.DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			foreach (var subscription in await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>())
				detail.Subscribers.Add(new ContentSetSubscriberView
				{
					Subscription = subscription,
					DepartmentName = await DepartmentNameAsync(subscription.DepartmentId),
					Sponsored = sponsored.Contains(subscription.DepartmentId)
				});

			detail.OpenProposals = (await _proposals.GetForPublisherAsync(actor.DepartmentId, true, 500) ?? new List<ContentSetProposal>())
				.Count(p => p.ContentSetId == set.ContentSetId);
			return detail;
		}

		private async Task<ContentSetItemView> ItemViewAsync(ContentSetItem item)
		{
			var source = Source(item.ItemKind);
			var described = source == null ? null : await source.DescribeAsync(item.DepartmentId, item.ItemId);
			var keys = item.CustomizableKeys;
			var mode = (ContentSetLockModes)item.LockMode;
			return new ContentSetItemView
			{
				Item = item,
				Title = described?.Title,
				VersionLabel = described?.VersionLabel,
				Problem = described == null ? "not_found" : described.Problem,
				NeedsLms = source?.NeedsLms ?? false,
				Blocks = (described?.Blocks ?? new List<ContentSetBlockOption>()).Select(b => new ContentSetBlockOption
				{
					Key = b.Key,
					Label = b.Label,
					Customizable = ContentSetMerge.IsCustomizable(mode, keys, b.Key)
				}).ToList()
			};
		}

		public async Task<ContentSetResult> SaveSetAsync(ContentSetActor actor, int contentSetId, ContentSetKinds kind, string name, string description, int lockedDeferDays,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			if (string.IsNullOrWhiteSpace(name) || !Enum.IsDefined(typeof(ContentSetKinds), kind))
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);

			var now = DateTime.UtcNow;
			var days = Math.Max(1, Math.Min(ContentSet.MaxDeferDays, lockedDeferDays <= 0 ? ContentSet.DefaultDeferDays : lockedDeferDays));
			ContentSet set;
			if (contentSetId > 0)
			{
				set = await OwnSetAsync(actor, contentSetId);
				if (set == null)
					return ContentSetResult.Fail(ContentSetErrors.NotFound);
				set.Name = Cut(name, ContentSet.NameMaxLength);
				set.Description = Cut(description, ContentSet.DescriptionMaxLength);
				set.LockedDeferDays = days;
				set.UpdatedOn = now;
				set.UpdatedByUserId = actor.UserId;
				set = await _sets.UpdateAsync(set, cancellationToken, true);
			}
			else
			{
				set = await _sets.InsertAsync(new ContentSet
				{
					DepartmentId = actor.DepartmentId,
					Kind = (int)kind,
					Name = Cut(name, ContentSet.NameMaxLength),
					Description = Cut(description, ContentSet.DescriptionMaxLength),
					LockedDeferDays = days,
					CreatedOn = now,
					CreatedByUserId = actor.UserId
				}, cancellationToken, true);
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { set.ContentSetId, set.Name, set.Kind, set.LockedDeferDays, action = contentSetId > 0 ? "saved" : "created" });
			return new ContentSetResult { Set = set };
		}

		public async Task<ContentSetResult> ArchiveSetAsync(ContentSetActor actor, int contentSetId, CancellationToken cancellationToken = default)
		{
			var set = await OwnSetAsync(actor, contentSetId);
			if (set == null)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);

			var now = DateTime.UtcNow;
			set.IsArchived = true;
			set.UpdatedOn = now;
			set.UpdatedByUserId = actor.UserId;
			await _sets.UpdateAsync(set, cancellationToken, true);
			foreach (var subscription in await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>())
				await EndSubscriptionAsync(subscription, ContentSetEndReasons.SetArchived, actor.UserId, now, cancellationToken);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { set.ContentSetId, action = "archived" });
			return new ContentSetResult { Set = set };
		}

		public async Task<List<ContentSetCandidate>> GetCandidatesAsync(ContentSetActor actor, int contentSetId)
		{
			var set = await OwnSetAsync(actor, contentSetId);
			if (set == null)
				return new List<ContentSetCandidate>();

			var existing = new HashSet<(int, int)>((await _items.GetForSetAsync(set.ContentSetId, false) ?? new List<ContentSetItem>()).Select(i => (i.ItemKind, i.ItemId)));
			var result = new List<ContentSetCandidate>();
			foreach (var source in _sources.Where(s => s.SetKind == (ContentSetKinds)set.Kind))
			{
				foreach (var candidate in await source.GetCandidatesAsync(actor.DepartmentId) ?? new List<ContentSetCandidate>())
				{
					if (existing.Contains(((int)candidate.Kind, candidate.ItemId)))
						continue;
					candidate.NeedsLms = source.NeedsLms;
					result.Add(candidate);
				}
			}

			return result;
		}

		public async Task<ContentSetResult> AddItemAsync(ContentSetActor actor, int contentSetId, ContentSetItemKinds kind, int itemId, ContentSetLockModes lockMode,
			IEnumerable<string> customizableKeys, CancellationToken cancellationToken = default)
		{
			var set = await OwnSetAsync(actor, contentSetId);
			if (set == null)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);
			if (set.IsArchived)
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);

			var source = Source(kind);
			if (source == null || source.SetKind != (ContentSetKinds)set.Kind || !Enum.IsDefined(typeof(ContentSetLockModes), lockMode))
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);

			var described = await source.DescribeAsync(actor.DepartmentId, itemId);
			if (described == null)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);
			if (described.Problem != null)
				return ContentSetResult.Fail(ContentSetErrors.ItemNotShareable, described.Problem);

			var items = await _items.GetForSetAsync(set.ContentSetId, false) ?? new List<ContentSetItem>();
			if (items.Any(i => i.ItemKind == (int)kind && i.ItemId == itemId))
				return ContentSetResult.Fail(ContentSetErrors.DuplicateItem);
			// A learning path travels with its own courses and trainings: they must be in the set first.
			if (source is IContentSetItemDependencies dependent &&
				(await dependent.GetDependenciesAsync(actor.DepartmentId, itemId) ?? new List<KeyValuePair<ContentSetItemKinds, int>>())
					.Any(d => !items.Any(i => i.ItemKind == (int)d.Key && i.ItemId == d.Value)))
				return ContentSetResult.Fail(ContentSetErrors.ItemNotShareable, "path_steps");

			var now = DateTime.UtcNow;
			var item = await _items.InsertAsync(new ContentSetItem
			{
				DepartmentId = actor.DepartmentId,
				ContentSetId = set.ContentSetId,
				ItemKind = (int)kind,
				ItemId = itemId,
				LockMode = (int)NormalizeMode(kind, lockMode),
				CustomizableBlockKeys = NormalizeKeys(kind, lockMode, customizableKeys, described),
				SortOrder = items.Count == 0 ? 1 : items.Max(i => i.SortOrder) + 1,
				AddedOn = now,
				AddedByUserId = actor.UserId
			}, cancellationToken, true);

			// Departments already subscribed get the new item in their inbox (it is adopted on its own decision, like an update).
			foreach (var subscription in (await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>()).Where(s => s.IsActive))
				await QueueUpdateAsync(set, item, subscription, null, described, ContentSetUpdateKinds.NewItem, now, cancellationToken);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { set.ContentSetId, item.ContentSetItemId, item.ItemKind, item.ItemId, item.LockMode, action = "item added" });
			return new ContentSetResult { Set = set, Item = item };
		}

		/// <summary>Courses and trainings have no blocks to customize: they are locked or open.</summary>
		private static ContentSetLockModes NormalizeMode(ContentSetItemKinds kind, ContentSetLockModes mode) =>
			kind != ContentSetItemKinds.Protocol && mode == ContentSetLockModes.CustomizableBlocks ? ContentSetLockModes.Locked : mode;

		private static string NormalizeKeys(ContentSetItemKinds kind, ContentSetLockModes mode, IEnumerable<string> keys, ContentSetSourceItem described)
		{
			if (kind != ContentSetItemKinds.Protocol || mode != ContentSetLockModes.CustomizableBlocks)
				return null;
			var known = new HashSet<string>((described?.Blocks ?? new List<ContentSetBlockOption>()).Select(b => b.Key), StringComparer.Ordinal);
			return ContentSetMerge.JoinKeys((keys ?? Enumerable.Empty<string>()).Where(known.Contains));
		}

		public async Task<ContentSetResult> UpdateItemAsync(ContentSetActor actor, int contentSetItemId, ContentSetLockModes lockMode, IEnumerable<string> customizableKeys,
			CancellationToken cancellationToken = default)
		{
			var (set, item) = await OwnItemAsync(actor, contentSetItemId);
			if (item == null)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);
			if (!Enum.IsDefined(typeof(ContentSetLockModes), lockMode))
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);

			var source = Source(item.ItemKind);
			var described = source == null ? null : await source.DescribeAsync(actor.DepartmentId, item.ItemId);
			item.LockMode = (int)NormalizeMode((ContentSetItemKinds)item.ItemKind, lockMode);
			item.CustomizableBlockKeys = NormalizeKeys((ContentSetItemKinds)item.ItemKind, (ContentSetLockModes)item.LockMode, customizableKeys, described);
			item = await _items.UpdateAsync(item, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { set.ContentSetId, item.ContentSetItemId, item.LockMode, item.CustomizableBlockKeys, action = "item changed" });
			return new ContentSetResult { Set = set, Item = item };
		}

		public async Task<ContentSetResult> RemoveItemAsync(ContentSetActor actor, int contentSetItemId, CancellationToken cancellationToken = default)
		{
			var (set, item) = await OwnItemAsync(actor, contentSetItemId);
			if (item == null)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);

			var now = DateTime.UtcNow;
			item.RemovedOn = now;
			item.RemovedByUserId = actor.UserId;
			await _items.UpdateAsync(item, cancellationToken, true);
			foreach (var subscription in await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>())
			{
				// The subscribers keep their copies as their own content; nothing more arrives for the item.
				await _copies.DetachAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId, now, cancellationToken);
				foreach (var open in await _updates.GetOpenForItemAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId) ?? new List<ContentSetUpdate>())
					await CloseAsync(open, ContentSetUpdateStatuses.Superseded, now, cancellationToken);
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetChanged, new { set.ContentSetId, item.ContentSetItemId, action = "item removed" });
			return new ContentSetResult { Set = set, Item = item };
		}

		private async Task<(ContentSet Set, ContentSetItem Item)> OwnItemAsync(ContentSetActor actor, int contentSetItemId)
		{
			if (actor == null || !actor.CanManage || contentSetItemId <= 0)
				return (null, null);
			var item = await _items.GetByIdAsync(contentSetItemId);
			if (item == null || item.DepartmentId != actor.DepartmentId || item.IsRemoved)
				return (null, null);
			var set = await _sets.GetByIdForDepartmentAsync(actor.DepartmentId, item.ContentSetId);
			return set == null ? (null, null) : (set, item);
		}

		public async Task<ContentSetResult> RemoveSubscriberAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			var subscription = await _subscriptions.GetByIdAsync(contentSetSubscriptionId);
			if (subscription == null || subscription.PublisherDepartmentId != actor.DepartmentId || subscription.Status == (int)ContentSetSubscriptionStatuses.Ended)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);

			await EndSubscriptionAsync(subscription, ContentSetEndReasons.RemovedByPublisher, actor.UserId, DateTime.UtcNow, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetSubscriptionChanged, new { subscription.ContentSetSubscriptionId, subscription.DepartmentId, action = "removed by publisher" });
			return new ContentSetResult { Subscription = subscription };
		}

		public async Task<List<ContentSetProposalView>> GetProposalsToPublisherAsync(ContentSetActor actor, bool openOnly)
		{
			if (actor == null || !actor.CanManage)
				return new List<ContentSetProposalView>();
			return await ProposalViewsAsync(await _proposals.GetForPublisherAsync(actor.DepartmentId, openOnly, 200) ?? new List<ContentSetProposal>());
		}

		private async Task<List<ContentSetProposalView>> ProposalViewsAsync(List<ContentSetProposal> proposals)
		{
			var result = new List<ContentSetProposalView>();
			var setNames = new Dictionary<int, string>();
			var titles = new Dictionary<(int, int), string>();
			foreach (var proposal in proposals)
			{
				if (!setNames.TryGetValue(proposal.ContentSetId, out var setName))
					setNames[proposal.ContentSetId] = setName = (await _sets.GetByIdAsync(proposal.ContentSetId))?.Name;
				if (!titles.TryGetValue((proposal.ItemKind, proposal.SourceItemId), out var title))
				{
					var source = Source(proposal.ItemKind);
					titles[(proposal.ItemKind, proposal.SourceItemId)] = title = source == null ? null : (await source.DescribeAsync(proposal.PublisherDepartmentId, proposal.SourceItemId))?.Title;
				}
				result.Add(new ContentSetProposalView { Proposal = proposal, SetName = setName, ItemTitle = title });
			}

			return result;
		}

		public async Task<ContentSetResult> RespondToProposalAsync(ContentSetActor actor, int contentSetProposalId, bool accept, string response,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			var proposal = await _proposals.GetForPublisherByIdAsync(actor.DepartmentId, contentSetProposalId);
			if (proposal == null)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);
			if (proposal.Status != (int)ContentSetProposalStatuses.Open)
				return ContentSetResult.Fail(ContentSetErrors.NotPending);
			if (!accept && string.IsNullOrWhiteSpace(response))
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);

			var now = DateTime.UtcNow;
			proposal.Status = (int)(accept ? ContentSetProposalStatuses.Accepted : ContentSetProposalStatuses.Declined);
			proposal.ResponseBody = Cut(response, ContentSetProposal.BodyMaxLength);
			proposal.RespondedByUserId = actor.UserId;
			proposal.RespondedOn = now;
			proposal = await _proposals.UpdateAsync(proposal, cancellationToken, true);

			// A declined proposal's suggestion thread is resolved; an accepted one stays open on the next draft until the owner resolves it.
			if (!accept && proposal.PublisherCommentId.HasValue)
			{
				try
				{
					var comment = await _protocolComments.GetByIdAsync(proposal.PublisherCommentId.Value);
					if (comment != null && comment.DepartmentId == actor.DepartmentId && !comment.IsResolved)
					{
						comment.ResolvedOn = now;
						comment.ResolvedByUserId = actor.UserId;
						await _protocolComments.UpdateAsync(comment, cancellationToken, true);
					}
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "The proposal's suggestion thread could not be resolved.");
				}
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided, new { proposal.ContentSetProposalId, proposal.Status, action = "proposal answered" });
			var views = await ProposalViewsAsync(new List<ContentSetProposal> { proposal });
			await NotifyAsync(() => _notifier.ProposalAnsweredAsync(proposal, views[0].SetName, views[0].ItemTitle, null, cancellationToken), cancellationToken);
			var answeredSet = await _sets.GetByIdAsync(proposal.ContentSetId);
			ContentSetWorkflowEvents.Raise(_events, proposal.DepartmentId, ContentSetWorkflowChanges.ProposalAnswered, answeredSet, (ContentSetItemKinds)proposal.ItemKind,
				views[0].ItemTitle, proposal.PublisherDepartmentId, proposal.DepartmentId, null, null, ((ContentSetProposalStatuses)proposal.Status).ToString(), actor.UserId);
			return new ContentSetResult { Proposal = proposal };
		}

		#endregion Publisher

		#region Subscriber

		public async Task<List<AvailableContentSet>> GetAvailableSetsAsync(ContentSetActor actor, ContentSetKinds kind)
		{
			var result = new List<AvailableContentSet>();
			if (actor == null || !actor.IsCurrentMember)
				return result;

			var links = await _links.GetAllLinksForDepartmentAsync(actor.DepartmentId) ?? new List<DepartmentLink>();
			var byPublisher = new Dictionary<int, DepartmentLink>();
			foreach (var link in links)
			{
				var publisher = DepartmentLinkSharing.GetSharedDepartmentId(link, actor.DepartmentId, ShareKindFor(kind));
				if (publisher.HasValue && !byPublisher.ContainsKey(publisher.Value))
					byPublisher[publisher.Value] = link;
			}
			if (byPublisher.Count == 0)
				return result;

			var mine = (await _subscriptions.GetForDepartmentAsync(actor.DepartmentId, false) ?? new List<ContentSetSubscription>()).ToDictionary(s => s.ContentSetId);
			foreach (var set in await _sets.GetLiveForPublishersAsync(byPublisher.Keys, kind) ?? new List<ContentSet>())
			{
				var link = byPublisher[set.DepartmentId];
				var (canRun, sponsored) = kind == ContentSetKinds.Training ? await CanRunLmsAsync(actor.DepartmentId, set.DepartmentId) : (false, false);
				result.Add(new AvailableContentSet
				{
					Set = set,
					DepartmentLinkId = link.DepartmentLinkId,
					PublisherName = link.DepartmentId == set.DepartmentId ? link.Department?.Name : link.LinkedDepartment?.Name,
					Subscription = mine.TryGetValue(set.ContentSetId, out var subscription) ? subscription : null,
					CanRunLms = canRun,
					SponsoredByPublisher = sponsored
				});
			}

			return result;
		}

		public async Task<AvailableContentSet> GetAvailableSetAsync(ContentSetActor actor, int contentSetId)
		{
			if (actor == null || !actor.IsCurrentMember || contentSetId <= 0)
				return null;
			var set = await _sets.GetByIdAsync(contentSetId);
			if (set == null || set.IsArchived || set.DepartmentId == actor.DepartmentId)
				return null;
			var link = await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind);
			if (link == null)
				return null;

			var (canRun, sponsored) = (ContentSetKinds)set.Kind == ContentSetKinds.Training ? await CanRunLmsAsync(actor.DepartmentId, set.DepartmentId) : (false, false);
			var view = new AvailableContentSet
			{
				Set = set,
				DepartmentLinkId = link.DepartmentLinkId,
				PublisherName = await DepartmentNameAsync(set.DepartmentId),
				Subscription = await _subscriptions.GetOpenAsync(set.ContentSetId, actor.DepartmentId),
				CanRunLms = canRun,
				SponsoredByPublisher = sponsored
			};
			foreach (var item in await _items.GetForSetAsync(set.ContentSetId, false) ?? new List<ContentSetItem>())
			{
				var itemView = await ItemViewAsync(item);
				// The subscriber sees what each item is and how locked it is, never the publisher's block editor.
				itemView.Blocks = itemView.Blocks.Where(b => b.Customizable).ToList();
				view.Items.Add(itemView);
			}

			return view;
		}

		public async Task<ContentSetResult> SubscribeAsync(ContentSetActor actor, int contentSetId, bool autoAccept, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			var set = await _sets.GetByIdAsync(contentSetId);
			if (set == null || set.IsArchived)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);
			if (set.DepartmentId == actor.DepartmentId)
				return ContentSetResult.Fail(ContentSetErrors.OwnSet);
			var link = await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind);
			if (link == null)
				return ContentSetResult.Fail(ContentSetErrors.NotShared);
			if (await _subscriptions.GetOpenAsync(set.ContentSetId, actor.DepartmentId) != null)
				return ContentSetResult.Fail(ContentSetErrors.AlreadySubscribed);

			var now = DateTime.UtcNow;
			ContentSetSubscription subscription;
			try
			{
				subscription = await _subscriptions.InsertAsync(new ContentSetSubscription
				{
					DepartmentId = actor.DepartmentId,
					ContentSetId = set.ContentSetId,
					PublisherDepartmentId = set.DepartmentId,
					DepartmentLinkId = link.DepartmentLinkId,
					Status = (int)ContentSetSubscriptionStatuses.Active,
					AutoAccept = autoAccept,
					SubscribedOn = now,
					SubscribedByUserId = actor.UserId
				}, cancellationToken, true);
			}
			catch (Exception ex) when (IsUniqueViolation(ex))
			{
				// Another admin subscribed at the same moment (one open subscription per set and department).
				return ContentSetResult.Fail(ContentSetErrors.AlreadySubscribed);
			}

			var result = new ContentSetResult { Set = set, Subscription = subscription };
			var publisherName = await DepartmentNameAsync(set.DepartmentId);
			foreach (var item in CopyOrder(await _items.GetForSetAsync(set.ContentSetId, false)))
			{
				var outcome = await CopyItemAsync(set, item, subscription, publisherName, actor.UserId, cancellationToken);
				if (outcome != null)
					result.Skipped.Add(outcome);
			}

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetSubscriptionChanged,
				new { subscription.ContentSetSubscriptionId, set.ContentSetId, set.DepartmentId, subscription.DepartmentLinkId, subscription.AutoAccept, skipped = result.Skipped, action = "subscribed" });
			ContentSetWorkflowEvents.Raise(_events, actor.DepartmentId, ContentSetWorkflowChanges.Subscribed, set, null, null, set.DepartmentId, actor.DepartmentId, null, null,
				ContentSetSubscriptionStatuses.Active.ToString(), actor.UserId);
			return result;
		}

		/// <summary>Items in the order a subscriber copies them: items that name others (learning paths) after everything else, then the set's order.</summary>
		public static List<ContentSetItem> CopyOrder(IEnumerable<ContentSetItem> items) =>
			(items ?? Enumerable.Empty<ContentSetItem>()).Where(i => i != null)
				.OrderBy(i => i.ItemKind == (int)ContentSetItemKinds.LmsPath ? 1 : 0).ThenBy(i => i.SortOrder).ThenBy(i => i.ContentSetItemId).ToList();

		/// <summary>Creates the subscriber's copy of one item now; returns the item's title when it was skipped (with its reason), null when copied.</summary>
		private async Task<string> CopyItemAsync(ContentSet set, ContentSetItem item, ContentSetSubscription subscription, string publisherName, string userId,
			CancellationToken cancellationToken)
		{
			var source = Source(item.ItemKind);
			if (source == null)
				return null;
			var described = await source.DescribeAsync(set.DepartmentId, item.ItemId);
			if (described == null)
				return null;
			if (source.NeedsLms && !(await CanRunLmsAsync(subscription.DepartmentId, set.DepartmentId)).CanRun)
				return described.Title + " (lms)";
			if (described.Problem != null)
				return described.Title + " (" + described.Problem + ")";

			var context = new ContentSetCopyContext
			{
				SubscriberDepartmentId = subscription.DepartmentId,
				UserId = userId,
				Set = set,
				Item = item,
				Source = described,
				Local = new ContentSetLocalCustomization(),
				PublisherName = publisherName
			};
			ContentSetCopyOutcome outcome;
			try
			{
				outcome = await source.CreateCopyAsync(context, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Set item {item.ContentSetItemId} could not be copied into department {subscription.DepartmentId}.");
				outcome = ContentSetCopyOutcome.Fail("error");
			}

			if (!outcome.Succeeded)
				return described.Title + " (" + outcome.Problem + ")";

			await _copies.InsertAsync(new ContentSetCopy
			{
				DepartmentId = subscription.DepartmentId,
				ContentSetSubscriptionId = subscription.ContentSetSubscriptionId,
				ContentSetItemId = item.ContentSetItemId,
				PublisherDepartmentId = set.DepartmentId,
				ItemKind = item.ItemKind,
				SourceItemId = item.ItemId,
				LocalItemId = outcome.LocalItemId,
				SourceVersionId = described.VersionId,
				SourceVersionLabel = Cut(described.VersionLabel, 50),
				LocalVersionId = outcome.LocalVersionId,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
			return null;
		}

		private async Task<ContentSetSubscription> OwnSubscriptionAsync(ContentSetActor actor, int contentSetSubscriptionId) =>
			actor == null || !actor.CanManage || contentSetSubscriptionId <= 0 ? null : await _subscriptions.GetByIdForDepartmentAsync(actor.DepartmentId, contentSetSubscriptionId);

		public async Task<ContentSetResult> UnsubscribeAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default)
		{
			var subscription = await OwnSubscriptionAsync(actor, contentSetSubscriptionId);
			if (subscription == null || subscription.Status == (int)ContentSetSubscriptionStatuses.Ended)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);

			await EndSubscriptionAsync(subscription, ContentSetEndReasons.Unsubscribed, actor.UserId, DateTime.UtcNow, cancellationToken);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetSubscriptionChanged, new { subscription.ContentSetSubscriptionId, action = "unsubscribed" });
			return new ContentSetResult { Subscription = subscription };
		}

		/// <summary>Ends a subscription: its copies become the subscriber's own content and its open inbox rows are superseded.</summary>
		private async Task EndSubscriptionAsync(ContentSetSubscription subscription, ContentSetEndReasons reason, string userId, DateTime now, CancellationToken cancellationToken)
		{
			subscription.Status = (int)ContentSetSubscriptionStatuses.Ended;
			subscription.EndReason = (int)reason;
			subscription.EndedOn = now;
			subscription.EndedByUserId = userId;
			await _subscriptions.UpdateAsync(subscription, cancellationToken, true);
			await _copies.DetachAsync(subscription.ContentSetSubscriptionId, null, now, cancellationToken);
			await _updates.CloseOpenForSubscriptionAsync(subscription.ContentSetSubscriptionId, ContentSetUpdateStatuses.Superseded, now, cancellationToken);
		}

		public async Task<ContentSetResult> SetAutoAcceptAsync(ContentSetActor actor, int contentSetSubscriptionId, bool autoAccept, CancellationToken cancellationToken = default)
		{
			var subscription = await OwnSubscriptionAsync(actor, contentSetSubscriptionId);
			if (subscription == null || subscription.Status == (int)ContentSetSubscriptionStatuses.Ended)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);
			subscription.AutoAccept = autoAccept;
			subscription = await _subscriptions.UpdateAsync(subscription, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetSubscriptionChanged, new { subscription.ContentSetSubscriptionId, subscription.AutoAccept, action = "auto-accept" });
			return new ContentSetResult { Subscription = subscription };
		}

		public async Task<ContentSetResult> SyncAsync(ContentSetActor actor, int contentSetSubscriptionId, CancellationToken cancellationToken = default)
		{
			var subscription = await OwnSubscriptionAsync(actor, contentSetSubscriptionId);
			if (subscription == null || !subscription.IsActive)
				return ContentSetResult.Fail(actor?.CanManage == true ? ContentSetErrors.NotFound : ContentSetErrors.NotAuthorized);
			var set = await _sets.GetByIdAsync(subscription.ContentSetId);
			if (set == null || set.IsArchived)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);
			if (await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) == null)
				return ContentSetResult.Fail(ContentSetErrors.NotShared);

			var copied = new HashSet<int>((await _copies.GetForSubscriptionAsync(subscription.ContentSetSubscriptionId) ?? new List<ContentSetCopy>())
				.Where(c => c.IsManaged).Select(c => c.ContentSetItemId));
			var result = new ContentSetResult { Set = set, Subscription = subscription };
			var publisherName = await DepartmentNameAsync(set.DepartmentId);
			foreach (var item in CopyOrder(await _items.GetForSetAsync(set.ContentSetId, false)).Where(i => !copied.Contains(i.ContentSetItemId)))
			{
				// An item the department declined stays declined unless it is offered again (a new inbox row); sync only fills gaps nobody decided.
				var open = await _updates.GetOpenForItemAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId) ?? new List<ContentSetUpdate>();
				if (open.Count > 0)
					continue;
				var skipped = await CopyItemAsync(set, item, subscription, publisherName, actor.UserId, cancellationToken);
				if (skipped != null)
					result.Skipped.Add(skipped);
			}

			return result;
		}

		public async Task<List<ContentSetSubscriptionView>> GetSubscriptionsAsync(ContentSetActor actor)
		{
			var result = new List<ContentSetSubscriptionView>();
			if (actor == null || !actor.IsCurrentMember)
				return result;

			var open = (await _updates.GetForDepartmentAsync(actor.DepartmentId, true, 500) ?? new List<ContentSetUpdate>()).ToLookup(u => u.ContentSetSubscriptionId);
			Model.Services.LmsSponsorshipGrant grant = null;
			try
			{
				grant = await _sponsorships.GetGrantAsync(actor.DepartmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
			}

			foreach (var subscription in await _subscriptions.GetForDepartmentAsync(actor.DepartmentId, true) ?? new List<ContentSetSubscription>())
			{
				var set = await _sets.GetByIdAsync(subscription.ContentSetId);
				if (set == null)
					continue;
				var view = new ContentSetSubscriptionView
				{
					Subscription = subscription,
					Set = set,
					PublisherName = await DepartmentNameAsync(subscription.PublisherDepartmentId),
					OpenUpdates = open[subscription.ContentSetSubscriptionId].Count(),
					LinkOk = subscription.Status != (int)ContentSetSubscriptionStatuses.Ended &&
						await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) != null,
					SponsoredByPublisher = grant?.Sponsorship?.SponsorDepartmentId == subscription.PublisherDepartmentId
				};
				if (subscription.Status != (int)ContentSetSubscriptionStatuses.Ended)
				{
					var items = (await _items.GetForSetAsync(set.ContentSetId, true) ?? new List<ContentSetItem>()).ToDictionary(i => i.ContentSetItemId);
					foreach (var copy in (await _copies.GetForSubscriptionAsync(subscription.ContentSetSubscriptionId) ?? new List<ContentSetCopy>()).Where(c => c.IsManaged))
					{
						items.TryGetValue(copy.ContentSetItemId, out var item);
						var source = Source(copy.ItemKind);
						view.Copies.Add(new ContentSetCopyRow
						{
							Copy = copy,
							Item = item,
							Title = source == null ? null : (await source.DescribeAsync(copy.PublisherDepartmentId, copy.SourceItemId))?.Title,
							HasLocalChanges = !ContentSetLocalCustomization.Parse(copy.LocalCustomizationJson).IsEmpty
						});
					}
				}

				result.Add(view);
			}

			return result;
		}

		public async Task<List<ContentSetInboxRow>> GetInboxAsync(ContentSetActor actor, bool openOnly)
		{
			var rows = new List<ContentSetInboxRow>();
			if (actor == null || !actor.IsCurrentMember)
				return rows;

			var setNames = new Dictionary<int, (string Name, string Publisher)>();
			var items = new Dictionary<int, ContentSetItem>();
			foreach (var update in await _updates.GetForDepartmentAsync(actor.DepartmentId, openOnly, 200) ?? new List<ContentSetUpdate>())
				rows.Add(await InboxRowAsync(update, setNames, items));
			return rows;
		}

		private async Task<ContentSetInboxRow> InboxRowAsync(ContentSetUpdate update, Dictionary<int, (string Name, string Publisher)> setNames = null, Dictionary<int, ContentSetItem> items = null)
		{
			setNames ??= new Dictionary<int, (string, string)>();
			items ??= new Dictionary<int, ContentSetItem>();
			if (!items.TryGetValue(update.ContentSetItemId, out var item))
				items[update.ContentSetItemId] = item = await _items.GetByIdAsync(update.ContentSetItemId);
			var setId = item?.ContentSetId ?? 0;
			if (!setNames.TryGetValue(setId, out var names))
			{
				var set = setId > 0 ? await _sets.GetByIdAsync(setId) : null;
				setNames[setId] = names = (set?.Name, await DepartmentNameAsync(update.PublisherDepartmentId));
			}

			var mode = (ContentSetLockModes)(item?.LockMode ?? (int)ContentSetLockModes.Locked);
			return new ContentSetInboxRow
			{
				Update = update,
				SetName = names.Name,
				PublisherName = names.Publisher,
				LockMode = mode,
				CanDecline = ContentSetMerge.CanDecline(mode)
			};
		}

		public async Task<ContentSetUpdateView> GetUpdateAsync(ContentSetActor actor, int contentSetUpdateId)
		{
			if (actor == null || !actor.IsCurrentMember)
				return null;
			var update = await _updates.GetByIdForDepartmentAsync(actor.DepartmentId, contentSetUpdateId);
			if (update == null)
				return null;

			var row = await InboxRowAsync(update);
			var view = new ContentSetUpdateView { Row = row, CanManage = actor.CanManage };
			var settings = await GetSettingsAsync(actor.DepartmentId);
			view.SuggestedApplyOnUtc = ContentSetSchedule.ApplyTime(DateTime.UtcNow, update.SourceEffectiveOn, null, await ZoneAsync(actor.DepartmentId), settings.UpdateTimeOfDayMinutes);

			var source = Source(update.ItemKind);
			var item = await _items.GetByIdAsync(update.ContentSetItemId);
			var set = item == null ? null : await _sets.GetByIdAsync(item.ContentSetId);
			if (source == null || item == null || set == null)
				return view;

			var copy = update.ContentSetCopyId.HasValue ? await _copies.GetByIdForDepartmentAsync(actor.DepartmentId, update.ContentSetCopyId.Value) : null;
			var described = await source.DescribeVersionAsync(set.DepartmentId, item.ItemId, update.SourceVersionId);
			var context = new ContentSetCopyContext
			{
				SubscriberDepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Set = set,
				Item = item,
				Copy = copy,
				Source = described,
				Local = ContentSetLocalCustomization.Parse(copy?.LocalCustomizationJson),
				PublisherName = row.PublisherName
			};
			try
			{
				view.CurrentHtml = copy == null ? string.Empty : await source.GetLocalHtmlAsync(actor.DepartmentId, copy.LocalItemId);
				var (html, dropped) = await source.PreviewAsync(context);
				view.CandidateHtml = html;
				view.DroppedChanges = dropped ?? new List<string>();
				view.Diff = ProtocolVersionDiff.Compare(view.CurrentHtml, view.CandidateHtml);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The set update redline could not be built.");
			}

			return view;
		}

		public async Task<ContentSetResult> AcceptUpdateAsync(ContentSetActor actor, int contentSetUpdateId, DateTime? applyOnUtc, CancellationToken cancellationToken = default)
		{
			var (update, subscription, item, error) = await DecidableAsync(actor, contentSetUpdateId);
			if (error != ContentSetErrors.None)
				return ContentSetResult.Fail(error);

			var now = DateTime.UtcNow;
			var settings = await GetSettingsAsync(actor.DepartmentId);
			var requested = applyOnUtc.HasValue ? DateTime.SpecifyKind(applyOnUtc.Value, DateTimeKind.Utc) : (DateTime?)null;
			var applyOn = ContentSetSchedule.ApplyTime(now, update.SourceEffectiveOn, requested, await ZoneAsync(actor.DepartmentId), settings.UpdateTimeOfDayMinutes);
			if (update.DeadlineOn.HasValue && requested.HasValue && applyOn > update.DeadlineOn.Value)
				return ContentSetResult.Fail(ContentSetErrors.DateOutOfRange);

			var expected = (ContentSetUpdateStatuses)update.Status;
			update.Status = (int)ContentSetUpdateStatuses.Scheduled;
			update.ApplyOn = applyOn;
			update.DeferredUntil = null;
			update.DecidedOn = now;
			update.DecidedByUserId = actor.UserId;
			update.DecidedAutomatically = false;
			if (!await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken))
				return ContentSetResult.Fail(ContentSetErrors.NotPending);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided, new { update.ContentSetUpdateId, update.ApplyOn, action = "accepted" });

			// The subscriber asked for now: apply it in this request rather than at the next worker run.
			if (applyOn <= now.AddMinutes(1) && await _updates.TryClaimAsync(update.ContentSetUpdateId, now, ClaimTimeout, cancellationToken))
				await ApplyAsync(await _updates.GetByIdForDepartmentAsync(actor.DepartmentId, update.ContentSetUpdateId) ?? update, now, cancellationToken);

			return new ContentSetResult { Update = await _updates.GetByIdForDepartmentAsync(actor.DepartmentId, update.ContentSetUpdateId) ?? update, Subscription = subscription, Item = item };
		}

		public async Task<ContentSetResult> DeferUpdateAsync(ContentSetActor actor, int contentSetUpdateId, DateTime untilUtc, CancellationToken cancellationToken = default)
		{
			var (update, subscription, item, error) = await DecidableAsync(actor, contentSetUpdateId);
			if (error != ContentSetErrors.None)
				return ContentSetResult.Fail(error);

			var now = DateTime.UtcNow;
			var until = DateTime.SpecifyKind(untilUtc, DateTimeKind.Utc);
			if (until <= now || (update.DeadlineOn.HasValue && until > update.DeadlineOn.Value))
				return ContentSetResult.Fail(ContentSetErrors.DateOutOfRange);

			var expected = (ContentSetUpdateStatuses)update.Status;
			update.Status = (int)ContentSetUpdateStatuses.Deferred;
			update.DeferredUntil = until;
			update.ApplyOn = null;
			update.DecidedOn = now;
			update.DecidedByUserId = actor.UserId;
			if (!await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken))
				return ContentSetResult.Fail(ContentSetErrors.NotPending);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided, new { update.ContentSetUpdateId, update.DeferredUntil, action = "deferred" });
			return new ContentSetResult { Update = update, Subscription = subscription, Item = item };
		}

		public async Task<ContentSetResult> DeclineUpdateAsync(ContentSetActor actor, int contentSetUpdateId, CancellationToken cancellationToken = default)
		{
			var (update, subscription, item, error) = await DecidableAsync(actor, contentSetUpdateId);
			if (error != ContentSetErrors.None)
				return ContentSetResult.Fail(error);
			if (!ContentSetMerge.CanDecline((ContentSetLockModes)item.LockMode))
				return ContentSetResult.Fail(ContentSetErrors.LockedCannotDecline);

			var expected = (ContentSetUpdateStatuses)update.Status;
			update.Status = (int)ContentSetUpdateStatuses.Declined;
			update.DecidedOn = DateTime.UtcNow;
			update.DecidedByUserId = actor.UserId;
			update.ApplyOn = null;
			update.DeferredUntil = null;
			if (!await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken))
				return ContentSetResult.Fail(ContentSetErrors.NotPending);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided, new { update.ContentSetUpdateId, action = "declined" });
			return new ContentSetResult { Update = update, Subscription = subscription, Item = item };
		}

		/// <summary>An update the actor may decide: pending or deferred, its subscription active and its link still sharing.</summary>
		private async Task<(ContentSetUpdate, ContentSetSubscription, ContentSetItem, ContentSetErrors)> DecidableAsync(ContentSetActor actor, int contentSetUpdateId)
		{
			if (actor == null || !actor.CanManage)
				return (null, null, null, ContentSetErrors.NotAuthorized);
			var update = await _updates.GetByIdForDepartmentAsync(actor.DepartmentId, contentSetUpdateId);
			if (update == null)
				return (null, null, null, ContentSetErrors.NotFound);
			if (update.Status != (int)ContentSetUpdateStatuses.Pending && update.Status != (int)ContentSetUpdateStatuses.Deferred)
				return (update, null, null, ContentSetErrors.NotPending);

			var subscription = await _subscriptions.GetByIdForDepartmentAsync(actor.DepartmentId, update.ContentSetSubscriptionId);
			var item = await _items.GetByIdAsync(update.ContentSetItemId);
			if (subscription == null || !subscription.IsActive || item == null || item.IsRemoved)
				return (update, subscription, item, ContentSetErrors.NotPending);
			var set = await _sets.GetByIdAsync(item.ContentSetId);
			if (set == null || set.IsArchived || await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) == null)
				return (update, subscription, item, ContentSetErrors.NotShared);
			return (update, subscription, item, ContentSetErrors.None);
		}

		public async Task<ContentSetCopyInfo> GetCopyInfoAsync(ContentSetActor actor, ContentSetItemKinds kind, int localItemId)
		{
			if (actor == null || !actor.IsCurrentMember || localItemId <= 0)
				return null;
			ContentSetCopy copy;
			try
			{
				copy = await _copies.GetManagedForLocalItemAsync(actor.DepartmentId, kind, localItemId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex);
				return null;
			}
			if (copy == null)
				return null;

			var item = await _items.GetByIdAsync(copy.ContentSetItemId);
			var set = item == null ? null : await _sets.GetByIdAsync(item.ContentSetId);
			var subscription = await _subscriptions.GetByIdForDepartmentAsync(actor.DepartmentId, copy.ContentSetSubscriptionId);
			var linkOk = set != null && subscription != null && subscription.IsActive &&
				await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) != null;
			var mode = (ContentSetLockModes)(item?.LockMode ?? (int)ContentSetLockModes.Locked);
			return new ContentSetCopyInfo
			{
				Copy = copy,
				Item = item,
				Set = set,
				PublisherName = await DepartmentNameAsync(copy.PublisherDepartmentId),
				CanCustomize = linkOk && actor.CanManage && kind == ContentSetItemKinds.Protocol && mode != ContentSetLockModes.Locked,
				CanPropose = linkOk
			};
		}

		private async Task<(ContentSetCopy Copy, ContentSetItem Item, ContentSet Set, ContentSetSubscription Subscription)> ManagedCopyAsync(ContentSetActor actor, int contentSetCopyId)
		{
			if (actor == null || !actor.IsCurrentMember || contentSetCopyId <= 0)
				return (null, null, null, null);
			var copy = await _copies.GetByIdForDepartmentAsync(actor.DepartmentId, contentSetCopyId);
			if (copy == null || !copy.IsManaged)
				return (null, null, null, null);
			var subscription = await _subscriptions.GetByIdForDepartmentAsync(actor.DepartmentId, copy.ContentSetSubscriptionId);
			var item = await _items.GetByIdAsync(copy.ContentSetItemId);
			var set = item == null ? null : await _sets.GetByIdAsync(item.ContentSetId);
			if (subscription == null || !subscription.IsActive || item == null || item.IsRemoved || set == null || set.IsArchived)
				return (null, null, null, null);
			if (await SharingLinkAsync(set.DepartmentId, actor.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) == null)
				return (null, null, null, null);
			return (copy, item, set, subscription);
		}

		public async Task<ContentSetCustomizeView> GetCustomizeAsync(ContentSetActor actor, int contentSetCopyId)
		{
			var (copy, item, set, _) = await ManagedCopyAsync(actor, contentSetCopyId);
			if (copy == null || copy.ItemKind != (int)ContentSetItemKinds.Protocol)
				return null;

			var source = Source(ContentSetItemKinds.Protocol);
			var described = source == null ? null : await source.DescribeVersionAsync(set.DepartmentId, item.ItemId, copy.SourceVersionId);
			if (described == null)
				return null;

			var mode = (ContentSetLockModes)item.LockMode;
			var local = ContentSetLocalCustomization.Parse(copy.LocalCustomizationJson);
			var document = ProtocolContentDocument.Parse(described.ContentJson, null);
			var view = new ContentSetCustomizeView
			{
				Copy = copy,
				Item = item,
				Title = described.Title,
				LockMode = mode,
				AllowsAdditions = ContentSetMerge.AllowsAdditions(mode),
				Additions = local.Additions?.ToList() ?? new List<string>()
			};
			foreach (var block in document.Blocks.Where(b => b != null && !string.IsNullOrWhiteSpace(b.Key)))
			{
				var single = new ProtocolContentDocument { Schema = document.Schema, Blocks = new List<ProtocolContentBlock> { block } };
				view.Blocks.Add(new ContentSetCustomizeBlock
				{
					Key = block.Key,
					Label = ProtocolSetItemSource.BlockLabel(block),
					PublisherHtml = _library.RenderContentJson(single.ToJson(), described.Locale),
					LocalHtml = local.Overrides != null && local.Overrides.TryGetValue(block.Key, out var html) ? html : null,
					Customizable = ContentSetMerge.IsCustomizable(mode, item.CustomizableKeys, block.Key)
				});
			}

			return view;
		}

		public async Task<ContentSetResult> SaveCustomizationAsync(ContentSetActor actor, int contentSetCopyId, IDictionary<string, string> overrides, IList<string> additions,
			CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.CanManage)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			var (copy, item, set, subscription) = await ManagedCopyAsync(actor, contentSetCopyId);
			if (copy == null || copy.ItemKind != (int)ContentSetItemKinds.Protocol)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);

			var mode = (ContentSetLockModes)item.LockMode;
			if (mode == ContentSetLockModes.Locked)
				return ContentSetResult.Fail(ContentSetErrors.BlockLocked);

			var local = new ContentSetLocalCustomization();
			foreach (var pair in overrides ?? new Dictionary<string, string>())
			{
				if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
					continue;
				if (!ContentSetMerge.IsCustomizable(mode, item.CustomizableKeys, pair.Key))
					return ContentSetResult.Fail(ContentSetErrors.BlockLocked);
				local.Overrides[pair.Key] = StringHelpers.SanitizeHtmlForStorage(pair.Value) ?? string.Empty;
			}
			if (additions != null && additions.Any(a => !string.IsNullOrWhiteSpace(a)))
			{
				if (!ContentSetMerge.AllowsAdditions(mode))
					return ContentSetResult.Fail(ContentSetErrors.BlockLocked);
				local.Additions = additions.Where(a => !string.IsNullOrWhiteSpace(a)).Take(ContentSetLocalCustomization.MaxAdditions)
					.Select(a => StringHelpers.SanitizeHtmlForStorage(a) ?? string.Empty).ToList();
			}

			var source = Source(ContentSetItemKinds.Protocol);
			var described = await source.DescribeVersionAsync(set.DepartmentId, item.ItemId, copy.SourceVersionId);
			if (described == null)
				return ContentSetResult.Fail(ContentSetErrors.NotFound);
			// The local version is built from the publisher version the copy carries (an update still in the inbox is not taken in by a local edit).
			described.Problem = null;
			var outcome = await source.ApplyVersionAsync(new ContentSetCopyContext
			{
				SubscriberDepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Set = set,
				Item = item,
				Copy = copy,
				Source = described,
				Local = local,
				PublisherName = await DepartmentNameAsync(set.DepartmentId)
			}, cancellationToken);
			if (!outcome.Succeeded)
				return ContentSetResult.Fail(ContentSetErrors.ItemNotShareable, outcome.Problem);

			copy.LocalCustomizationJson = local.ToJson();
			copy.LocalVersionId = outcome.LocalVersionId;
			copy.UpdatedOn = DateTime.UtcNow;
			copy = await _copies.UpdateAsync(copy, cancellationToken, true);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided,
				new { copy.ContentSetCopyId, overrides = local.Overrides.Keys.ToList(), additions = local.Additions.Count, action = "customized" });
			return new ContentSetResult { Copy = copy, Item = item, Set = set, Subscription = subscription };
		}

		public async Task<ContentSetResult> ProposeAsync(ContentSetActor actor, int contentSetCopyId, string anchor, string body, CancellationToken cancellationToken = default)
		{
			if (actor == null || !actor.IsCurrentMember)
				return ContentSetResult.Fail(ContentSetErrors.NotAuthorized);
			if (string.IsNullOrWhiteSpace(body))
				return ContentSetResult.Fail(ContentSetErrors.InvalidInput);
			var (copy, item, set, _) = await ManagedCopyAsync(actor, contentSetCopyId);
			if (copy == null)
				return ContentSetResult.Fail(ContentSetErrors.NotShared);

			var subscriberName = await DepartmentNameAsync(actor.DepartmentId);
			var text = Cut(body, ContentSetProposal.BodyMaxLength);
			var proposal = await _proposals.InsertAsync(new ContentSetProposal
			{
				DepartmentId = actor.DepartmentId,
				PublisherDepartmentId = set.DepartmentId,
				ContentSetId = set.ContentSetId,
				ContentSetItemId = item.ContentSetItemId,
				ItemKind = item.ItemKind,
				SourceItemId = item.ItemId,
				SourceVersionId = copy.SourceVersionId > 0 ? copy.SourceVersionId : (int?)null,
				Anchor = Cut(anchor, ContentSetProposal.AnchorMaxLength),
				Body = text,
				AuthorUserId = actor.UserId,
				AuthorName = Cut(actor.Name, 200),
				SubscriberName = Cut(subscriberName, 200),
				Status = (int)ContentSetProposalStatuses.Open,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);

			// A protocol proposal opens a suggestion thread on the publisher's protocol (on its open draft when there is one), the way a
			// member's own suggestion reaches the owner; the author is named in the text (the publisher cannot read our roster).
			if (item.ItemKind == (int)ContentSetItemKinds.Protocol)
			{
				try
				{
					var draft = await _library.GetOpenDraftAsync(set.DepartmentId, item.ItemId);
					var comment = await _protocolComments.InsertAsync(new ProtocolComment
					{
						DepartmentId = set.DepartmentId,
						DispatchProtocolId = item.ItemId,
						ProtocolVersionId = draft?.ProtocolVersionId ?? (copy.SourceVersionId > 0 ? copy.SourceVersionId : (int?)null),
						Kind = (int)ProtocolCommentKinds.Suggestion,
						Anchor = proposal.Anchor,
						Body = Cut("[" + (subscriberName ?? string.Empty) + (string.IsNullOrWhiteSpace(actor.Name) ? string.Empty : ", " + actor.Name) + "] " + text, ProtocolComment.BodyMaxLength),
						CreatedOn = proposal.CreatedOn
					}, cancellationToken, true);
					proposal.PublisherCommentId = comment.ProtocolCommentId;
					proposal = await _proposals.UpdateAsync(proposal, cancellationToken, true);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, "The proposal's suggestion thread could not be opened on the publisher's protocol.");
				}
			}

			var views = await ProposalViewsAsync(new List<ContentSetProposal> { proposal });
			await NotifyAsync(() => _notifier.ProposalReceivedAsync(proposal, set.Name, views[0].ItemTitle, cancellationToken), cancellationToken);
			ContentSetWorkflowEvents.Raise(_events, set.DepartmentId, ContentSetWorkflowChanges.ProposalReceived, set, (ContentSetItemKinds)item.ItemKind, views[0].ItemTitle,
				set.DepartmentId, actor.DepartmentId, null, null, ContentSetProposalStatuses.Open.ToString(), actor.UserId);
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ContentSetUpdateDecided, new { proposal.ContentSetProposalId, set.ContentSetId, item.ContentSetItemId, action = "proposed" });
			return new ContentSetResult { Proposal = proposal, Set = set, Item = item, Copy = copy };
		}

		public async Task<List<ContentSetProposalView>> GetProposalsFromDepartmentAsync(ContentSetActor actor)
		{
			if (actor == null || !actor.IsCurrentMember)
				return new List<ContentSetProposalView>();
			return await ProposalViewsAsync(await _proposals.GetForDepartmentAsync(actor.DepartmentId, 200) ?? new List<ContentSetProposal>());
		}

		#endregion Subscriber

		#region Publish fan-out and worker 100

		public async Task<int> OnSourcePublishedAsync(int publisherDepartmentId, ContentSetItemKinds kind, int itemId, CancellationToken cancellationToken = default)
		{
			var queued = 0;
			var items = await _items.GetLiveForItemAsync(publisherDepartmentId, kind, itemId) ?? new List<ContentSetItem>();
			if (items.Count == 0)
				return 0;

			var source = Source(kind);
			var described = source == null ? null : await source.DescribeAsync(publisherDepartmentId, itemId);
			if (described == null || described.VersionId <= 0)
				return 0;

			var now = DateTime.UtcNow;
			foreach (var item in items)
			{
				var set = await _sets.GetByIdForDepartmentAsync(publisherDepartmentId, item.ContentSetId);
				if (set == null || set.IsArchived)
					continue;

				var publisherName = await DepartmentNameAsync(publisherDepartmentId);
				foreach (var subscription in (await _subscriptions.GetOpenForSetAsync(set.ContentSetId) ?? new List<ContentSetSubscription>()).Where(s => s.IsActive))
				{
					try
					{
						// A link that stopped sharing gets nothing new (worker 100 suspends the subscription).
						if (await SharingLinkAsync(set.DepartmentId, subscription.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) == null)
							continue;
						var copy = (await _copies.GetForSubscriptionAsync(subscription.ContentSetSubscriptionId) ?? new List<ContentSetCopy>())
							.FirstOrDefault(c => c.IsManaged && c.ContentSetItemId == item.ContentSetItemId);
						if (copy != null && copy.SourceVersionId == described.VersionId)
							continue;
						var update = await QueueUpdateAsync(set, item, subscription, copy, described, copy == null ? ContentSetUpdateKinds.NewItem : ContentSetUpdateKinds.NewVersion, now,
							cancellationToken);
						if (update != null)
						{
							queued++;
							await NotifyAsync(() => _notifier.UpdatesArrivedAsync(subscription.DepartmentId, set.Name, publisherName, new[] { update }, cancellationToken), cancellationToken);
							ContentSetWorkflowEvents.Raise(_events, subscription.DepartmentId, ContentSetWorkflowChanges.UpdateArrived, set, kind, update.Title, set.DepartmentId,
								subscription.DepartmentId, update.SourceVersionLabel, update.SourceEffectiveOn, ((ContentSetUpdateKinds)update.UpdateKind).ToString(), null);
						}
					}
					catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
					{
						throw;
					}
					catch (Exception ex)
					{
						Logging.LogException(ex, $"Set update for subscription {subscription.ContentSetSubscriptionId} could not be queued.");
					}
				}
			}

			return queued;
		}

		/// <summary>Writes one inbox row (superseding the item's open rows); null when the department already has a newer one open.</summary>
		private async Task<ContentSetUpdate> QueueUpdateAsync(ContentSet set, ContentSetItem item, ContentSetSubscription subscription, ContentSetCopy copy,
			ContentSetSourceItem described, ContentSetUpdateKinds kind, DateTime now, CancellationToken cancellationToken)
		{
			foreach (var open in await _updates.GetOpenForItemAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId) ?? new List<ContentSetUpdate>())
			{
				if (open.SourceVersionId == described.VersionId)
					return null;
				await CloseAsync(open, ContentSetUpdateStatuses.Superseded, now, cancellationToken);
			}

			var effective = described.EffectiveOn.HasValue && described.EffectiveOn.Value > now ? described.EffectiveOn.Value : now;
			return await _updates.InsertAsync(new ContentSetUpdate
			{
				DepartmentId = subscription.DepartmentId,
				ContentSetSubscriptionId = subscription.ContentSetSubscriptionId,
				ContentSetItemId = item.ContentSetItemId,
				ContentSetCopyId = copy?.ContentSetCopyId,
				PublisherDepartmentId = set.DepartmentId,
				UpdateKind = (int)kind,
				ItemKind = item.ItemKind,
				SourceItemId = item.ItemId,
				SourceVersionId = described.VersionId,
				SourceVersionLabel = Cut(described.VersionLabel, 50),
				Title = Cut(described.Title, 250),
				ChangeSummary = described.ChangeSummary,
				SourceEffectiveOn = effective,
				DeadlineOn = ContentSetSchedule.Deadline((ContentSetLockModes)item.LockMode, effective, set.LockedDeferDays),
				Status = (int)ContentSetUpdateStatuses.Pending,
				CreatedOn = now
			}, cancellationToken, true);
		}

		private async Task CloseAsync(ContentSetUpdate update, ContentSetUpdateStatuses status, DateTime now, CancellationToken cancellationToken)
		{
			var expected = (ContentSetUpdateStatuses)update.Status;
			update.Status = (int)status;
			update.DecidedOn ??= now;
			update.DecidedAutomatically = true;
			await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken);
		}

		public async Task<ContentSetRunResult> RunDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var result = new ContentSetRunResult();
			result.Suspended = await SuspendLapsedAsync(null, utcNow, cancellationToken);

			// Baseline trainings have no publish event: a changed text is picked up here and queued like a new version.
			foreach (var item in (await _items.GetLiveByKindAsync(ContentSetItemKinds.Training, 2000) ?? new List<ContentSetItem>())
				.GroupBy(i => (i.DepartmentId, i.ItemId)).Select(g => g.First()))
			{
				try
				{
					await OnSourcePublishedAsync(item.DepartmentId, ContentSetItemKinds.Training, item.ItemId, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Set training {item.ItemId} could not be checked for changes.");
				}
			}

			// Updates nobody has to decide any more: auto-accepted subscriptions, locked items past their deadline, deferrals that ended.
			foreach (var update in await _updates.GetUndecidedDueAsync(utcNow, WorkerBatch) ?? new List<ContentSetUpdate>())
			{
				try
				{
					var subscription = await _subscriptions.GetByIdForDepartmentAsync(update.DepartmentId, update.ContentSetSubscriptionId);
					if (subscription == null || !subscription.IsActive)
						continue;
					var deadlinePassed = update.DeadlineOn.HasValue && update.DeadlineOn.Value <= utcNow;
					var expected = (ContentSetUpdateStatuses)update.Status;
					if (subscription.AutoAccept || deadlinePassed)
					{
						var settings = await GetSettingsAsync(update.DepartmentId);
						update.Status = (int)ContentSetUpdateStatuses.Scheduled;
						update.ApplyOn = ContentSetSchedule.ApplyTime(utcNow, update.SourceEffectiveOn, null, await ZoneAsync(update.DepartmentId), settings.UpdateTimeOfDayMinutes);
						update.DeferredUntil = null;
						update.DecidedOn = utcNow;
						update.DecidedByUserId = null;
						update.DecidedAutomatically = true;
						if (await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken))
							result.Accepted++;
					}
					else if (update.Status == (int)ContentSetUpdateStatuses.Deferred)
					{
						// The deferral ended: the update waits for a decision again.
						update.Status = (int)ContentSetUpdateStatuses.Pending;
						update.DeferredUntil = null;
						await _updates.TryUpdateFromStatusAsync(update, expected, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Set update {update.ContentSetUpdateId} could not be decided automatically.");
				}
			}

			foreach (var due in await _updates.GetDueAsync(utcNow, ClaimTimeout, WorkerBatch) ?? new List<ContentSetUpdate>())
			{
				if (!await _updates.TryClaimAsync(due.ContentSetUpdateId, utcNow, ClaimTimeout, cancellationToken))
					continue;
				var applied = await ApplyAsync(await _updates.GetByIdForDepartmentAsync(due.DepartmentId, due.ContentSetUpdateId) ?? due, utcNow, cancellationToken);
				if (applied)
					result.Applied++;
				else
					result.Failed++;
			}

			return result;
		}

		/// <summary>Puts one claimed, scheduled update into force (or records why it could not be).</summary>
		private async Task<bool> ApplyAsync(ContentSetUpdate update, DateTime now, CancellationToken cancellationToken)
		{
			string problem = null;
			try
			{
				var subscription = await _subscriptions.GetByIdForDepartmentAsync(update.DepartmentId, update.ContentSetSubscriptionId);
				var item = await _items.GetByIdAsync(update.ContentSetItemId);
				var set = item == null ? null : await _sets.GetByIdAsync(item.ContentSetId);
				var source = Source(update.ItemKind);
				if (subscription == null || !subscription.IsActive || item == null || item.IsRemoved || set == null || set.IsArchived || source == null)
					return await FinishAsync(update, ContentSetUpdateStatuses.Superseded, null, null, now, cancellationToken);
				if (await SharingLinkAsync(set.DepartmentId, update.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) == null)
					return await FinishAsync(update, ContentSetUpdateStatuses.Failed, "not_shared", null, now, cancellationToken);

				var described = await source.DescribeAsync(set.DepartmentId, item.ItemId);
				if (described == null || described.VersionId != update.SourceVersionId)
					// The publisher moved on (or withdrew the item): a newer row carries the version now in force.
					return await FinishAsync(update, ContentSetUpdateStatuses.Superseded, null, null, now, cancellationToken);
				if (described.Problem != null)
					return await FinishAsync(update, ContentSetUpdateStatuses.Failed, described.Problem, null, now, cancellationToken);
				if (source.NeedsLms && !(await CanRunLmsAsync(update.DepartmentId, set.DepartmentId)).CanRun)
					return await FinishAsync(update, ContentSetUpdateStatuses.Failed, "lms", null, now, cancellationToken);

				var copy = (await _copies.GetForSubscriptionAsync(subscription.ContentSetSubscriptionId) ?? new List<ContentSetCopy>())
					.FirstOrDefault(c => c.IsManaged && c.ContentSetItemId == item.ContentSetItemId);
				var userId = update.DecidedByUserId ?? subscription.SubscribedByUserId;
				var publisherName = await DepartmentNameAsync(set.DepartmentId);
				if (copy != null && !await source.LocalExistsAsync(update.DepartmentId, copy.LocalItemId))
				{
					// The department deleted its copy: the item is adopted afresh.
					await _copies.DetachAsync(subscription.ContentSetSubscriptionId, item.ContentSetItemId, now, cancellationToken);
					copy = null;
				}

				if (copy == null)
				{
					var skipped = await CopyItemAsync(set, item, subscription, publisherName, userId, cancellationToken);
					if (skipped != null)
						return await FinishAsync(update, ContentSetUpdateStatuses.Failed, Cut(skipped, 500), null, now, cancellationToken);
					var created = (await _copies.GetForSubscriptionAsync(subscription.ContentSetSubscriptionId) ?? new List<ContentSetCopy>())
						.FirstOrDefault(c => c.IsManaged && c.ContentSetItemId == item.ContentSetItemId);
					update.ContentSetCopyId = created?.ContentSetCopyId;
					var ok = await FinishAsync(update, ContentSetUpdateStatuses.Applied, null, created?.LocalVersionId, now, cancellationToken);
					AfterApplied(update, set, publisherName);
					return ok;
				}

				var previous = await source.DescribeVersionAsync(set.DepartmentId, item.ItemId, copy.SourceVersionId);
				var outcome = await source.ApplyVersionAsync(new ContentSetCopyContext
				{
					SubscriberDepartmentId = update.DepartmentId,
					UserId = userId,
					Set = set,
					Item = item,
					Copy = copy,
					Source = described,
					Local = ContentSetLocalCustomization.Parse(copy.LocalCustomizationJson),
					PublisherName = publisherName,
					Major = previous != null && described.Major > previous.Major
				}, cancellationToken);
				if (!outcome.Succeeded)
					return await FinishAsync(update, ContentSetUpdateStatuses.Failed, outcome.Problem, null, now, cancellationToken);

				copy.SourceVersionId = described.VersionId;
				copy.SourceVersionLabel = Cut(described.VersionLabel, 50);
				copy.LocalVersionId = outcome.LocalVersionId;
				copy.UpdatedOn = now;
				await _copies.UpdateAsync(copy, cancellationToken, true);
				update.ContentSetCopyId = copy.ContentSetCopyId;
				var finished = await FinishAsync(update, ContentSetUpdateStatuses.Applied, null, outcome.LocalVersionId, now, cancellationToken);
				AfterApplied(update, set, publisherName);
				return finished;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Set update {update.ContentSetUpdateId} could not be applied.");
				problem = "error";
			}

			await FinishAsync(update, ContentSetUpdateStatuses.Failed, problem, null, now, cancellationToken);
			return false;
		}

		private void AfterApplied(ContentSetUpdate update, ContentSet set, string publisherName)
		{
			Audit(update.DepartmentId, update.DecidedByUserId, AuditLogTypes.ContentSetUpdateDecided, new { update.ContentSetUpdateId, update.LocalVersionId, action = "applied" });
			ContentSetWorkflowEvents.Raise(_events, update.DepartmentId, ContentSetWorkflowChanges.UpdateApplied, set, (ContentSetItemKinds)update.ItemKind, update.Title,
				set.DepartmentId, update.DepartmentId, update.SourceVersionLabel, update.AppliedOn, ((ContentSetUpdateKinds)update.UpdateKind).ToString(), update.DecidedByUserId);
		}

		private async Task<bool> FinishAsync(ContentSetUpdate update, ContentSetUpdateStatuses status, string problem, int? localVersionId, DateTime now,
			CancellationToken cancellationToken)
		{
			update.Status = (int)status;
			update.FailureReason = Cut(problem, 500);
			update.LocalVersionId = localVersionId ?? update.LocalVersionId;
			if (status == ContentSetUpdateStatuses.Applied)
				update.AppliedOn = now;
			update.ClaimedOn = null;
			await _updates.TryUpdateFromStatusAsync(update, ContentSetUpdateStatuses.Scheduled, cancellationToken);
			return status == ContentSetUpdateStatuses.Applied;
		}

		public async Task<int> SuspendLapsedAsync(int? departmentLinkId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var suspended = 0;
			List<ContentSetSubscription> open;
			if (departmentLinkId.HasValue)
				open = await _subscriptions.GetOpenForLinkAsync(departmentLinkId.Value) ?? new List<ContentSetSubscription>();
			else
				open = await _subscriptions.GetAllOpenAsync(5000) ?? new List<ContentSetSubscription>();

			foreach (var subscription in open.Where(s => s.IsActive))
			{
				try
				{
					var set = await _sets.GetByIdAsync(subscription.ContentSetId);
					if (set == null)
						continue;
					if (await SharingLinkAsync(set.DepartmentId, subscription.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId) != null)
						continue;

					// Nothing new arrives and nothing scheduled goes into force; the copies stay as they are, still locked, until the link shares
					// again (the subscription resumes) or the department unsubscribes (they become its own).
					subscription.Status = (int)ContentSetSubscriptionStatuses.Suspended;
					subscription.SuspendedOn = utcNow;
					subscription.EndReason = (int)ContentSetEndReasons.LinkRevoked;
					await _subscriptions.UpdateAsync(subscription, cancellationToken, true);
					await _updates.CloseOpenForSubscriptionAsync(subscription.ContentSetSubscriptionId, ContentSetUpdateStatuses.Superseded, utcNow, cancellationToken);
					suspended++;
					Audit(subscription.DepartmentId, null, AuditLogTypes.ContentSetSubscriptionChanged, new { subscription.ContentSetSubscriptionId, subscription.DepartmentLinkId, action = "suspended" });
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Set subscription {subscription.ContentSetSubscriptionId} could not be checked against its link.");
				}
			}

			// A suspended subscription whose link shares again resumes (updates published in between arrive with the next publish).
			foreach (var subscription in open.Where(s => s.Status == (int)ContentSetSubscriptionStatuses.Suspended))
			{
				try
				{
					var set = await _sets.GetByIdAsync(subscription.ContentSetId);
					if (set == null || set.IsArchived)
						continue;
					var link = await SharingLinkAsync(set.DepartmentId, subscription.DepartmentId, (ContentSetKinds)set.Kind, subscription.DepartmentLinkId);
					if (link == null)
						continue;
					subscription.Status = (int)ContentSetSubscriptionStatuses.Active;
					subscription.SuspendedOn = null;
					subscription.EndReason = (int)ContentSetEndReasons.None;
					subscription.DepartmentLinkId = link.DepartmentLinkId;
					await _subscriptions.UpdateAsync(subscription, cancellationToken, true);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex);
				}
			}

			return suspended;
		}

		#endregion Publish fan-out and worker 100

		#region Helpers

		private static async Task NotifyAsync(Func<Task> send, CancellationToken cancellationToken)
		{
			try
			{
				await send();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "A content set notice could not be sent.");
			}
		}

		private static bool IsUniqueViolation(Exception ex)
		{
			for (var e = ex; e != null; e = e.InnerException)
			{
				var message = e.Message ?? string.Empty;
				if (message.IndexOf("duplicate", StringComparison.OrdinalIgnoreCase) >= 0 || message.IndexOf("unique", StringComparison.OrdinalIgnoreCase) >= 0 ||
					message.Contains("2601") || message.Contains("2627") || message.Contains("23505"))
					return true;
			}

			return false;
		}

		private static string Cut(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length <= max ? trimmed : trimmed.Substring(0, max);
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object after) =>
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});

		#endregion Helpers
	}
}
