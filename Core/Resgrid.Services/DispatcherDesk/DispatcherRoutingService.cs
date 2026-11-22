using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.AiDispatch;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The dispatcher queue (M0324, dispatchers-on-duty-plan.md D5). Work is offered to one available dispatcher at a time; an
	/// offer that is declined or expires moves on to the next. Calls (pending, scheduled, and conversations that already have a
	/// pending call) wait in the shared queue when nobody is free; live conversations without a call overflow: texts and chats
	/// become a pending call (optionally filled in by Enhanced AI), held phone callers go to the department's voice overflow.
	/// Every state change is a conditional write on the item's Version, so two dispatchers never hold one item.
	/// </summary>
	public partial class DispatcherRoutingService : IDispatcherRoutingService
	{
		/// <summary>How long closed items (and their history and messages) are kept for the desk activity report.</summary>
		public const int RetentionDays = 180;

		/// <summary>The sweep adds pending calls logged at most this long ago (a department turning the desk on does not inherit years of them).</summary>
		public const int SweepPendingCallDays = 3;

		public const int MaxSweepPerTick = 50;

		private readonly IDispatcherQueueItemsRepository _itemsRepository;
		private readonly IDispatcherQueueEventsRepository _eventsRepository;
		private readonly IDispatcherConversationMessagesRepository _messagesRepository;
		private readonly IDispatcherPresencesRepository _presencesRepository;
		private readonly IDispatcherBreaksRepository _breaksRepository;
		private readonly IDispatcherPresenceService _presenceService;
		private readonly IDispatcherDeskConfigService _configService;
		private readonly IDispatcherDeskNotifier _notifier;
		private readonly IDispatcherVoiceProvider _voiceProvider;
		private readonly IDepartmentsService _departmentsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IQueueService _queueService;
		private readonly Lazy<ICallsService> _callsService;
		private readonly Lazy<IDispatcherConversationService> _conversationService;
		private readonly Lazy<IAiDispatchEnrichmentService> _aiDispatchEnrichmentService;
		private readonly Lazy<IEnumerable<IIntakeConversationService>> _intakeConversations;
		private readonly Lazy<IIntakeScreeningService> _screening;
		private readonly Lazy<IIntakeService> _intakeService;

		public DispatcherRoutingService(IDispatcherQueueItemsRepository itemsRepository, IDispatcherQueueEventsRepository eventsRepository,
			IDispatcherConversationMessagesRepository messagesRepository, IDispatcherPresencesRepository presencesRepository,
			IDispatcherBreaksRepository breaksRepository, IDispatcherPresenceService presenceService, IDispatcherDeskConfigService configService,
			IDispatcherDeskNotifier notifier, IDispatcherVoiceProvider voiceProvider, IDepartmentsService departmentsService,
			IUserProfileService userProfileService, IQueueService queueService, Lazy<ICallsService> callsService,
			Lazy<IDispatcherConversationService> conversationService, Lazy<IAiDispatchEnrichmentService> aiDispatchEnrichmentService,
			Lazy<IEnumerable<IIntakeConversationService>> intakeConversations = null, Lazy<IIntakeScreeningService> screening = null,
			Lazy<IIntakeService> intakeService = null)
		{
			_screening = screening;
			_intakeService = intakeService;
			_itemsRepository = itemsRepository;
			_eventsRepository = eventsRepository;
			_messagesRepository = messagesRepository;
			_presencesRepository = presencesRepository;
			_breaksRepository = breaksRepository;
			_presenceService = presenceService;
			_configService = configService;
			_notifier = notifier;
			_voiceProvider = voiceProvider;
			_departmentsService = departmentsService;
			_userProfileService = userProfileService;
			_queueService = queueService;
			_callsService = callsService;
			_conversationService = conversationService;
			_aiDispatchEnrichmentService = aiDispatchEnrichmentService;
			_intakeConversations = intakeConversations ?? new Lazy<IEnumerable<IIntakeConversationService>>(Enumerable.Empty<IIntakeConversationService>);
		}

		public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

		// ---------------------------------------------------------------- enqueue / inbound

		public async Task<DispatcherRouteResult> EnqueueAsync(DispatcherEnqueueRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0)
				return DispatcherRouteResult.NotRouted();

			var config = await _configService.GetConfigAsync(request.DepartmentId);
			if (!config.Routes(request.Kind))
				return DispatcherRouteResult.NotRouted();

			var duplicate = await FindDuplicateAsync(request);
			if (duplicate != null)
				return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = duplicate, UserId = duplicate.AssignedUserId };

			var now = UtcNow();
			Call call = null;
			if (request.CallId.HasValue)
			{
				call = await _callsService.Value.GetCallByIdAsync(request.CallId.Value);
				// The request may have waited (a call event behind the config read): a call dispatched, closed or deleted since is not offered.
				if (call == null || call.DepartmentId != request.DepartmentId || !IsEligibleFor(request.Kind, call))
					return DispatcherRouteResult.NotRouted();

				// A pending call an overflowed text, chat or voicemail became belongs to that item (SourceIdentifier "DQ:{itemId}"): while
				// the item is open it is the call's desk item, even before the overflow has linked the two.
				var owner = await GetOverflowOwnerAsync(call);
				if (owner != null)
					return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = owner, UserId = owner.AssignedUserId };
			}

			var item = new DispatcherQueueItem
			{
				DispatcherQueueItemId = Guid.NewGuid().ToString(),
				DepartmentId = request.DepartmentId,
				Kind = (int)request.Kind,
				State = (int)DispatcherQueueItemStates.Queued,
				CallId = request.CallId,
				Priority = request.Priority ?? DispatcherRoutingPolicy.KindPriority(request.Kind) + (call?.Priority ?? 0) * 10,
				SourceAddress = Truncate(request.SourceAddress, 256),
				SourceName = Truncate(request.SourceName, 200),
				TargetAddress = Truncate(request.TargetAddress, 256),
				Subject = Truncate(request.Subject ?? call?.Name, 250),
				Preview = DispatcherQueueItem.TruncatePreview(request.Body ?? call?.NatureOfCall),
				ExternalReference = Truncate(DispatcherRoutingPolicy.IntakeReference(request.IntakeConversationId) ?? request.ExternalReference, 128),
				// An intake thread is joined by intake (replies to the master number), never by the desk's own text key.
				ConversationKey = IsConversation(request.Kind) && string.IsNullOrWhiteSpace(request.IntakeConversationId)
					? DispatcherRoutingPolicy.ConversationKey(request.Kind, request.SourceAddress) : null,
				DueOn = request.DueOn ?? (request.Kind == DispatcherQueueItemKinds.ScheduledCall ? call?.DispatchOn : null),
				CreatedOn = now,
				CreatedByUserId = request.CreatedByUserId,
				LastActivityOn = now,
				Version = 0
			};

			// The unique open-item indexes decide a race the duplicate check above cannot (a call event and the sweep, a provider retry).
			if (!await _itemsRepository.TryInsertAsync(item, cancellationToken))
			{
				var winner = await FindOpenWinnerAsync(item);
				return winner != null
					? new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = winner, UserId = winner.AssignedUserId }
					: DispatcherRouteResult.NotRouted();
			}

			await AddEventAsync(item, DispatcherQueueEventTypes.Created, request.CreatedByUserId, KindName(request.Kind), now, cancellationToken);

			if (IsConversation(request.Kind) && !string.IsNullOrWhiteSpace(request.Body) && string.IsNullOrWhiteSpace(request.IntakeConversationId))
			{
				var recorded = await _conversationService.Value.RecordInboundAsync(item, request.SourceAddress, request.TargetAddress, request.Body,
					request.ProviderMessageId, cancellationToken);

				// A concurrent delivery of the same provider message stored it first (on the item it opened): this item is that
				// delivery's duplicate and is closed before anyone is offered it.
				if (recorded == null && !string.IsNullOrWhiteSpace(request.ProviderMessageId))
				{
					await CompleteAsCoveredAsync(item, "Duplicate delivery of a message already on the desk", now, cancellationToken);
					var stored = await _messagesRepository.GetByProviderMessageIdAsync(request.ProviderMessageId);
					var original = stored == null ? null : await _itemsRepository.GetItemAsync(stored.DispatcherQueueItemId);
					return original != null && original.DepartmentId == item.DepartmentId
						? new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = original, UserId = original.AssignedUserId }
						: new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = item };
				}
			}

			var members = await _presenceService.GetRoutableAsync(item.DepartmentId, now);
			var target = PickTarget(members, item, request.PreferredUserId);

			if (target != null && await TryOfferAsync(item, target, config, now, cancellationToken))
			{
				await AcknowledgeAsync(item, config, cancellationToken);
				return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Offered, Item = item, UserId = target.UserId };
			}

			await PublishAsync(item);

			if (NeedsOverflow(item, config, now))
			{
				// A phone caller is still on the inbound webhook: its handler answers with the overflow TwiML itself, so the live
				// call is not redirected here (that would race the webhook's own answer).
				if (item.Kind == (int)DispatcherQueueItemKinds.Voice)
				{
					await MarkOverflowedAsync(item, "No dispatcher was free.", cancellationToken);
					return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Overflow, Item = item };
				}

				var overflow = await OverflowAsync(item, config, now, "No dispatcher was free.", cancellationToken);
				if (overflow == OverflowResults.Converted)
				{
					// A texter whose message overflowed at once still hears it was received (it is a pending call now).
					await AcknowledgeAsync(item, config, cancellationToken, overflowed: true);
					return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Overflow, Item = item };
				}

				if (overflow == OverflowResults.Closed)
					return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Overflow, Item = item };

				// No pending call yet (the save failed, or another pass is making it): the message waits in the queue, where the worker's
				// timeout pass retries the overflow; the sender is told it arrived, not that it was passed on.
			}

			await AcknowledgeAsync(item, config, cancellationToken);
			return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Queued, Item = item };
		}

		public async Task<DispatcherRouteResult> RouteInboundMessageAsync(DispatcherEnqueueRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null)
				return DispatcherRouteResult.NotRouted();

			if (!string.IsNullOrWhiteSpace(request.ProviderMessageId) && await _messagesRepository.GetByProviderMessageIdAsync(request.ProviderMessageId) != null)
				return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate };

			var key = DispatcherRoutingPolicy.ConversationKey(request.Kind, request.SourceAddress);
			var now = UtcNow();

			if (key != null)
			{
				DispatcherQueueItem open;
				if (request.DepartmentId > 0)
				{
					var config = await _configService.GetConfigAsync(request.DepartmentId);
					open = config.Enabled
						? await _itemsRepository.GetOpenByConversationKeyAsync(request.DepartmentId, key, now.AddMinutes(-config.EffectiveConversationJoinMinutes()))
						: null;
				}
				else
				{
					// A reply to the master number: whichever department's conversation with this sender is open and recent. Desk replies are
					// all sent from the master number (TextMessageProvider sends every SMS from TwilioResgridNumber), so this is the reply path
					// for every desk text conversation, whichever department number the first text reached; the sender's most recently active
					// open conversation is the one they are answering. Intake threads (dedicated numbers) have no ConversationKey and are never
					// matched here: intake joins those itself.
					open = await _itemsRepository.GetOpenByConversationKeyAnyDepartmentAsync(key, now.AddMinutes(-DispatcherDeskConfig.DefaultConversationJoinMinutes));
				}

				if (open != null)
				{
					var recorded = await _conversationService.Value.RecordInboundAsync(open, request.SourceAddress, request.TargetAddress, request.Body,
						request.ProviderMessageId, cancellationToken);

					// Nothing recorded for a real message: a concurrent delivery of the same provider message stored it first. It is that
					// delivery's to announce; this one is a duplicate (no second MessageReceived, no second push).
					if (recorded == null && !string.IsNullOrWhiteSpace(request.Body))
						return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = open, UserId = open.AssignedUserId };

					await TouchAsync(open, now, cancellationToken);
					await AddEventAsync(open, DispatcherQueueEventTypes.MessageReceived, null, null, now, cancellationToken);

					if (!string.IsNullOrWhiteSpace(open.AssignedUserId))
						await _notifier.NotifyMessageAsync(open, open.AssignedUserId, request.Body, cancellationToken);

					await PublishAsync(open);
					return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Joined, Item = open, UserId = open.AssignedUserId };
				}
			}

			if (request.DepartmentId <= 0)
				return DispatcherRouteResult.NotRouted();

			return await EnqueueAsync(request, cancellationToken);
		}

		// ---------------------------------------------------------------- routing

		public async Task<int> RouteDepartmentAsync(int departmentId, CancellationToken cancellationToken = default)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			if (!config.Enabled)
				return 0;

			var now = UtcNow();
			var queued = DispatcherRoutingPolicy.QueueOrder((await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => x.State == (int)DispatcherQueueItemStates.Queued));
			if (queued.Count == 0)
				return 0;

			var members = await _presenceService.GetRoutableAsync(departmentId, now);
			var offers = 0;

			foreach (var item in queued)
			{
				if (members.Count == 0 || cancellationToken.IsCancellationRequested)
					break;

				var target = PickTarget(members, item, null);
				if (target == null)
					continue;

				if (await TryOfferAsync(item, target, config, now, cancellationToken))
				{
					offers++;
					members.Remove(target);
				}
			}

			return offers;
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> AcceptAsync(int departmentId, string dispatcherQueueItemId, string userId,
			CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			if (item.State == (int)DispatcherQueueItemStates.Accepted)
				return string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase) ? DispatcherDeskResult<DispatcherQueueItem>.Ok(item) : Fail(DispatcherDeskErrors.ItemNotAvailable);

			if (item.State == (int)DispatcherQueueItemStates.Offered && !string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return Fail(DispatcherDeskErrors.NotSignedIn);

			if (presence.State == (int)DispatcherPresenceStates.OnBreak)
				return Fail(DispatcherDeskErrors.AlreadyOnBreak);

			var now = UtcNow();
			var expected = item.Version;
			var wasOffered = item.State == (int)DispatcherQueueItemStates.Offered;
			item.State = (int)DispatcherQueueItemStates.Accepted;
			item.AssignedUserId = userId;
			item.AcceptedOn = now;
			item.OfferExpiresOn = null;
			item.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			await AddEventAsync(item, DispatcherQueueEventTypes.Accepted, userId, wasOffered ? null : "Taken from the queue", now, cancellationToken);

			// An intake text thread: the texter is told who they are now talking to (intake sends it). Offered to this dispatcher
			// (a transfer included) takes the thread over from whoever had it; taken from the queue does not.
			var refused = await ConnectIntakeThreadAsync(item, userId, takeOver: wasOffered, now, cancellationToken);
			if (refused != null)
				return refused;

			if (presence.MissedOffers != 0 || presence.State == (int)DispatcherPresenceStates.Away)
			{
				presence.MissedOffers = 0;
				if (presence.State == (int)DispatcherPresenceStates.Away && presence.AwayReason == AwayAfterMissedOffers)
				{
					presence.State = (int)DispatcherPresenceStates.Available;
					presence.AwayReason = null;
					presence.StateChangedOn = now;
				}

				await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);
			}

			await PublishAsync(item);
			await _notifier.PublishDeskChangedAsync(departmentId, userId);

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> DeclineAsync(int departmentId, string dispatcherQueueItemId, string userId, string reason,
			CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			if (item.State != (int)DispatcherQueueItemStates.Offered || !string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			var now = UtcNow();
			if (!await ReturnToQueueAsync(item, userId, addToDeclined: true, now, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			await AddEventAsync(item, DispatcherQueueEventTypes.Declined, userId, Truncate(reason, 500), now, cancellationToken);
			await AfterReturnAsync(item, now, cancellationToken);

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> ReleaseAsync(int departmentId, string dispatcherQueueItemId, string userId,
			CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			if (item.State == (int)DispatcherQueueItemStates.Queued)
				return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);

			var holder = string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (!holder && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			var now = UtcNow();
			var previous = item.AssignedUserId;
			var wasOffered = item.State == (int)DispatcherQueueItemStates.Offered;
			var wasHeld = item.State == (int)DispatcherQueueItemStates.Accepted;

			// An offer handed back is not offered to the same dispatcher again; held work may come back to them later.
			if (!await ReturnToQueueAsync(item, previous, addToDeclined: wasOffered, now, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			if (wasHeld)
				await ReleaseIntakeThreadAsync(item, previous, cancellationToken);

			await AddEventAsync(item, DispatcherQueueEventTypes.Released, userId, holder ? null : "Returned by a supervisor", now, cancellationToken);
			await AfterReturnAsync(item, now, cancellationToken);
			await _notifier.PublishDeskChangedAsync(departmentId, previous);

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> TransferAsync(int departmentId, string dispatcherQueueItemId, string userId, string toUserId,
			string note, CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			if (string.IsNullOrWhiteSpace(toUserId) || string.Equals(toUserId, item.AssignedUserId, StringComparison.OrdinalIgnoreCase))
				return Fail(DispatcherDeskErrors.InvalidInput);

			var holder = string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			if (!holder && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			var now = UtcNow();
			var target = await _presenceService.GetMemberAsync(departmentId, toUserId, now);
			if (target == null || !target.IsOnDuty || target.Presence == null || !target.Presence.IsSignedIn())
				return Fail(DispatcherDeskErrors.NotSignedIn);

			if (target.DisplayState == DispatcherDisplayStates.OnBreak)
				return Fail(DispatcherDeskErrors.AlreadyOnBreak);

			var config = await _configService.GetConfigAsync(departmentId);
			var previous = item.AssignedUserId;
			var expected = item.Version;

			item.State = (int)DispatcherQueueItemStates.Offered;
			item.AssignedUserId = toUserId;
			item.OfferedOn = now;
			item.OfferExpiresOn = now.AddSeconds(config.EffectiveOfferTimeoutSeconds());
			item.OfferAttempts++;
			item.AcceptedOn = null;
			item.LastActivityOn = now;
			item.DeclinedUserIds = RemoveUser(item.DeclinedUserIds, toUserId);

			if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			await AddEventAsync(item, DispatcherQueueEventTypes.Transferred, userId, Truncate($"To {toUserId}" + (string.IsNullOrWhiteSpace(note) ? string.Empty : ": " + note), 500), now, cancellationToken);
			await _notifier.NotifyOfferAsync(item, toUserId, cancellationToken);
			await PublishAsync(item);
			await _notifier.PublishDeskChangedAsync(departmentId, previous);

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> CompleteAsync(int departmentId, string dispatcherQueueItemId, string userId,
			DispatcherQueueItemOutcomes outcome, int? callId, string note, bool notifyParticipant = true, CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			var holder = string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			var queued = item.State == (int)DispatcherQueueItemStates.Queued;
			if (!holder && !queued && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			if (callId.HasValue && !await CallBelongsAsync(departmentId, callId.Value))
				return Fail(DispatcherDeskErrors.InvalidInput);

			var now = UtcNow();
			var previous = item.AssignedUserId;
			var expected = item.Version;

			item.State = (int)DispatcherQueueItemStates.Completed;
			item.Outcome = (int)outcome;
			item.CompletedOn = now;
			item.CompletedByUserId = userId;
			item.CallId ??= callId;
			item.OfferExpiresOn = null;
			item.LastActivityOn = now;
			if (!string.IsNullOrWhiteSpace(note))
				item.Note = Truncate(note, 1000);

			if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			await AddEventAsync(item, DispatcherQueueEventTypes.Completed, userId, outcome.ToString(), now, cancellationToken);
			await CloseIntakeThreadAsync(item, userId, outcome, notifyParticipant, cancellationToken);
			await PublishAsync(item);

			if (!string.IsNullOrWhiteSpace(previous))
			{
				await _notifier.PublishDeskChangedAsync(departmentId, previous);
				await RouteDepartmentAsync(departmentId, cancellationToken);
			}

			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		public async Task<DispatcherDeskResult<DispatcherQueueItem>> LinkCallAsync(int departmentId, string dispatcherQueueItemId, string userId, int callId,
			CancellationToken cancellationToken = default)
		{
			var (item, error) = await LoadOpenAsync(departmentId, dispatcherQueueItemId);
			if (error != DispatcherDeskErrors.None)
				return Fail(error);

			if (item.CallId.HasValue && item.CallId.Value != callId)
				return Fail(DispatcherDeskErrors.InvalidInput, "The item already has a call.");

			// The same rule as completing it: its holder, anyone while it waits in the queue, or a supervisor (never someone else's work).
			var holder = string.Equals(item.AssignedUserId, userId, StringComparison.OrdinalIgnoreCase);
			var queued = item.State == (int)DispatcherQueueItemStates.Queued;
			if (!holder && !queued && !await _presenceService.CanSuperviseAsync(departmentId, userId))
				return Fail(DispatcherDeskErrors.NotAssignedToYou);

			if (item.CallId == callId)
				return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);

			if (!await CallBelongsAsync(departmentId, callId))
				return Fail(DispatcherDeskErrors.InvalidInput);

			var now = UtcNow();

			// One open item per call: the call's own item (queued by its pending-call event) is covered by this one, unless a
			// dispatcher is working it. Another conversation that has the call (a voicemail waiting for call back) is never closed here.
			var others = (await _itemsRepository.GetOpenForCallAsync(departmentId, callId) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => x.DispatcherQueueItemId != item.DispatcherQueueItemId).ToList();
			if (others.Any(x => x.State == (int)DispatcherQueueItemStates.Accepted || IsConversation((DispatcherQueueItemKinds)x.Kind)))
				return Fail(DispatcherDeskErrors.InvalidInput, "That call is already on the desk as another item.");

			foreach (var other in others)
				await CompleteAsCoveredAsync(other, "Covered by the desk item the call was linked to", now, cancellationToken);

			var expected = item.Version;
			item.CallId = callId;
			item.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return Fail(DispatcherDeskErrors.ItemNotAvailable);

			await PublishAsync(item);
			return DispatcherDeskResult<DispatcherQueueItem>.Ok(item);
		}

		/// <summary>Completes an open item another item now stands for (Outcome Duplicate); false when it changed meanwhile.</summary>
		private async Task<bool> CompleteAsCoveredAsync(DispatcherQueueItem other, string detail, DateTime now, CancellationToken cancellationToken)
		{
			var expected = other.Version;
			var offeredTo = other.AssignedUserId;
			other.State = (int)DispatcherQueueItemStates.Completed;
			other.Outcome = (int)DispatcherQueueItemOutcomes.Duplicate;
			other.CompletedOn = now;
			other.OfferExpiresOn = null;
			other.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(other, expected, cancellationToken))
				return false;

			await AddEventAsync(other, DispatcherQueueEventTypes.Completed, null, detail, now, cancellationToken);
			await PublishAsync(other);
			if (!string.IsNullOrWhiteSpace(offeredTo))
				await _notifier.PublishDeskChangedAsync(other.DepartmentId, offeredTo);
			return true;
		}

		// ---------------------------------------------------------------- reads

		public async Task<List<DispatcherQueueItem>> GetOpenItemsAsync(int departmentId) =>
			DispatcherRoutingPolicy.QueueOrder(await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>());

		public async Task<DispatcherQueueItemDetail> GetItemDetailAsync(int departmentId, string dispatcherQueueItemId, string viewerUserId = null)
		{
			var item = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			if (item == null || item.DepartmentId != departmentId)
				return null;

			var detail = new DispatcherQueueItemDetail
			{
				Item = item,
				Events = (await _eventsRepository.GetForItemAsync(item.DispatcherQueueItemId) ?? Enumerable.Empty<DispatcherQueueEvent>()).ToList(),
				Messages = (await _messagesRepository.GetForItemAsync(item.DispatcherQueueItemId) ?? Enumerable.Empty<DispatcherConversationMessage>()).ToList()
			};

			if (item.CallId.HasValue)
			{
				var call = await _callsService.Value.GetCallByIdAsync(item.CallId.Value);
				detail.Call = call != null && call.DepartmentId == departmentId ? call : null;
			}

			detail.IntakeConversation = await GetIntakeThreadAsync(item, viewerUserId);

			return detail;
		}

		public Task<DispatcherQueueItem> GetItemByExternalReferenceAsync(string externalReference) =>
			string.IsNullOrWhiteSpace(externalReference) ? Task.FromResult<DispatcherQueueItem>(null) : _itemsRepository.GetByExternalReferenceAsync(externalReference);

		// ---------------------------------------------------------------- calls

		public async Task OnCallChangedAsync(int departmentId, int callId, CancellationToken cancellationToken = default)
		{
			var items = (await _itemsRepository.GetOpenForCallAsync(departmentId, callId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();
			if (items.Count == 0)
				return;

			var call = await _callsService.Value.GetCallByIdAsync(callId);
			if (IsWaiting(call))
			{
				// M0326: the call was screened after its offer went out (Enhanced AI runs after the call is saved): the offer comes back.
				await WithdrawHeldOffersAsync(departmentId, items, cancellationToken);
				return;
			}

			var outcome = call == null || call.IsDeleted ? DispatcherQueueItemOutcomes.NoActionNeeded
				: call.State == (int)CallStates.Active ? DispatcherQueueItemOutcomes.Dispatched : DispatcherQueueItemOutcomes.Closed;
			var now = UtcNow();

			foreach (var item in items)
			{
				// A conversation someone holds stays open: the dispatcher may still be talking to the sender.
				if (IsConversation((DispatcherQueueItemKinds)item.Kind) && item.State == (int)DispatcherQueueItemStates.Accepted)
					continue;

				var expected = item.Version;
				var holder = item.AssignedUserId;
				var held = item.State == (int)DispatcherQueueItemStates.Accepted;
				item.State = (int)DispatcherQueueItemStates.Completed;
				item.Outcome = (int)outcome;
				item.CompletedOn = now;
				item.CompletedByUserId = held ? holder : null;
				item.OfferExpiresOn = null;
				item.LastActivityOn = now;

				if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
					continue;

				await AddEventAsync(item, DispatcherQueueEventTypes.Completed, null, "Call " + outcome, now, cancellationToken);
				await PublishAsync(item);

				if (!string.IsNullOrWhiteSpace(holder))
					await _notifier.PublishDeskChangedAsync(departmentId, holder);
			}

			await RouteDepartmentAsync(departmentId, cancellationToken);
		}

		public async Task<int> SyncCallsAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			if (!config.Enabled)
				return 0;

			var added = 0;

			// Waiting pending calls go on the desk under their own kind (as DispatcherDeskCallEventService does on a call event): a chat
			// app's intake call as a Chat and an emailed call as an Email when the desk takes those, anything else as a PendingCall. So a
			// chat or email whose first hand-off failed is recovered here even when the desk does not take pending calls (CR40).
			if (config.Routes(DispatcherQueueItemKinds.PendingCall) || config.Routes(DispatcherQueueItemKinds.Chat) ||
				config.Routes(DispatcherQueueItemKinds.Email))
			{
				var candidates = (await _callsService.Value.GetPendingCallsByDepartmentIdAsync(departmentId) ?? new List<Call>())
					.Where(x => !x.IsDeleted && x.State == (int)CallStates.Pending && x.LoggedOn >= utcNow.AddDays(-SweepPendingCallDays))
					.OrderBy(x => x.LoggedOn)
					.ToList();
				var notOnDesk = await NotYetOnDeskAsync(departmentId, candidates, int.MaxValue);
				var chatCallIds = await ChatCallIdsAsync(departmentId, config, notOnDesk);

				// The limit applies after the calls this desk would not take are left out, so they never keep routable ones off it.
				var pending = notOnDesk
					.Select(call => (Call: call, Kind: WaitingCallKind(config, call, chatCallIds)))
					.Where(x => x.Kind.HasValue)
					.Take(MaxSweepPerTick)
					.ToList();

				foreach (var (call, kind) in pending)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					var result = await EnqueueAsync(new DispatcherEnqueueRequest
					{
						DepartmentId = departmentId,
						Kind = kind.Value,
						CallId = call.CallId,
						SourceName = kind.Value == DispatcherQueueItemKinds.Email ? null : call.ContactName,
						SourceAddress = kind.Value == DispatcherQueueItemKinds.PendingCall ? call.ContactNumber : null
					}, cancellationToken);

					if (result.Outcome == DispatcherRouteOutcomes.Offered || result.Outcome == DispatcherRouteOutcomes.Queued)
						added++;
				}
			}

			if (config.RouteScheduledCalls)
			{
				var horizon = utcNow.AddMinutes(config.EffectiveScheduledCallLeadMinutes());
				var candidates = (await _callsService.Value.GetAllNonDispatchedScheduledCallsByDepartmentIdAsync(departmentId) ?? new List<Call>())
					.Where(x => !x.IsDeleted && x.State == (int)CallStates.Active && x.DispatchOn.HasValue && x.HasBeenDispatched != true &&
								x.DispatchOn.Value > utcNow && x.DispatchOn.Value <= horizon)
					.OrderBy(x => x.DispatchOn)
					.ToList();
				var scheduled = await NotYetOnDeskAsync(departmentId, candidates);

				foreach (var call in scheduled)
				{
					if (cancellationToken.IsCancellationRequested)
						break;

					var result = await EnqueueAsync(new DispatcherEnqueueRequest
					{
						DepartmentId = departmentId,
						Kind = DispatcherQueueItemKinds.ScheduledCall,
						CallId = call.CallId,
						DueOn = call.DispatchOn
					}, cancellationToken);

					if (result.Outcome == DispatcherRouteOutcomes.Offered || result.Outcome == DispatcherRouteOutcomes.Queued)
						added++;
				}
			}

			return added;
		}

		/// <summary>
		/// The sweep's next calls, oldest first: those never on the desk (any state), up to <see cref="MaxSweepPerTick"/>. The limit applies
		/// after the calls the desk already had are left out, so a run of handled calls never keeps newer ones off the desk.
		/// </summary>
		private async Task<List<Call>> NotYetOnDeskAsync(int departmentId, List<Call> candidates, int take = MaxSweepPerTick)
		{
			if (candidates.Count == 0)
				return candidates;

			var onDesk = new HashSet<int>(await _itemsRepository.GetCallIdsOnDeskAsync(departmentId, candidates.Select(x => x.CallId)) ?? Enumerable.Empty<int>());
			return candidates.Where(x => !onDesk.Contains(x.CallId)).Take(take).ToList();
		}

		/// <summary>
		/// The desk kind a waiting pending call is swept under (the same rule as DispatcherDeskCallEventService): Chat for a chat app's
		/// intake call when the desk takes chats, Email for an emailed call that is not one of the desk's own "DQ:" overflow calls when
		/// it takes emails, otherwise PendingCall when it takes pending calls; null when the desk takes none of them.
		/// </summary>
		internal static DispatcherQueueItemKinds? WaitingCallKind(DispatcherDeskConfig config, Call call, ISet<int> chatCallIds)
		{
			if (call.CallSource == (int)CallSources.Intake && config.Routes(DispatcherQueueItemKinds.Chat) && chatCallIds != null &&
				chatCallIds.Contains(call.CallId))
				return DispatcherQueueItemKinds.Chat;

			if (call.CallSource == (int)CallSources.EmailImport && config.Routes(DispatcherQueueItemKinds.Email) &&
				(call.SourceIdentifier == null || !call.SourceIdentifier.StartsWith(AiDispatch.AiDispatchEnrichmentService.DeskCallSourcePrefix, StringComparison.Ordinal)))
				return DispatcherQueueItemKinds.Email;

			return config.Routes(DispatcherQueueItemKinds.PendingCall) ? DispatcherQueueItemKinds.PendingCall : (DispatcherQueueItemKinds?)null;
		}

		/// <summary>
		/// The sweep's intake calls that came from a chat app (one batched intake read). Empty when the desk does not take chats, intake
		/// is not available, or the read fails: those calls are then swept as pending calls, as an unticked Chats option would.
		/// </summary>
		private async Task<ISet<int>> ChatCallIdsAsync(int departmentId, DispatcherDeskConfig config, IEnumerable<Call> calls)
		{
			var chatCallIds = new HashSet<int>();
			if (_intakeService == null || !config.Routes(DispatcherQueueItemKinds.Chat))
				return chatCallIds;

			var intakeCallIds = calls.Where(x => x.CallSource == (int)CallSources.Intake).Select(x => x.CallId).ToList();
			if (intakeCallIds.Count == 0)
				return chatCallIds;

			try
			{
				var sources = await _intakeService.Value.GetCallSourcesAsync(departmentId, intakeCallIds);
				foreach (var source in sources?.Values ?? Enumerable.Empty<IntakeCallSource>())
				{
					if (source != null && Intake.IntakeService.IsChatChannel((IntakeChannelTypes)source.ChannelType))
						chatCallIds.Add(source.CallId);
				}
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk sweep could not read intake call sources for department {departmentId}");
			}

			return chatCallIds;
		}

		// ---------------------------------------------------------------- worker

		public async Task<int> ProcessTimeoutsAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var changes = 0;
			foreach (var departmentId in (await _itemsRepository.GetDepartmentsWithOpenItemsAsync() ?? Enumerable.Empty<int>()).ToList())
			{
				if (cancellationToken.IsCancellationRequested)
					break;

				try
				{
					changes += await ProcessDepartmentTimeoutsAsync(departmentId, utcNow, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher queue timeouts for department {departmentId}");
				}
			}

			return changes;
		}

		private async Task<int> ProcessDepartmentTimeoutsAsync(int departmentId, DateTime now, CancellationToken cancellationToken)
		{
			var config = await _configService.GetConfigAsync(departmentId);
			var items = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();
			var changes = 0;

			// Calls handled elsewhere (dispatched from the web, closed, deleted) leave the desk.
			foreach (var callId in items.Where(x => x.CallId.HasValue).Select(x => x.CallId.Value).Distinct().ToList())
				await OnCallChangedAsync(departmentId, callId, cancellationToken);

			items = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();

			if (!config.Enabled)
			{
				// The desk was turned off: nothing is offered any more; held work stays with its holder until completed.
				foreach (var offered in items.Where(x => x.State == (int)DispatcherQueueItemStates.Offered))
				{
					if (await ReturnToQueueAsync(offered, offered.AssignedUserId, addToDeclined: false, now, cancellationToken))
						changes++;
				}

				return changes;
			}

			foreach (var expired in items.Where(x => x.State == (int)DispatcherQueueItemStates.Offered && x.OfferExpiresOn.HasValue && x.OfferExpiresOn.Value <= now))
			{
				var missedBy = expired.AssignedUserId;
				if (!await ReturnToQueueAsync(expired, missedBy, addToDeclined: true, now, cancellationToken))
					continue;

				changes++;
				await AddEventAsync(expired, DispatcherQueueEventTypes.OfferExpired, missedBy, null, now, cancellationToken);
				await PublishAsync(expired);
				await CountMissedOfferAsync(departmentId, missedBy, config, now, cancellationToken);
			}

			changes += await RouteDepartmentAsync(departmentId, cancellationToken);

			items = (await _itemsRepository.GetOpenForDepartmentAsync(departmentId) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();

			foreach (var waiting in items.Where(x => x.State == (int)DispatcherQueueItemStates.Queued && NeedsOverflow(x, config, now)))
			{
				if (await OverflowAsync(waiting, config, now, "Nobody took it.", cancellationToken) != OverflowResults.Skipped)
					changes++;
			}

			var escalateAfter = config.EffectiveQueueEscalationMinutes();
			if (escalateAfter > 0)
			{
				var stale = items.Where(x => x.State == (int)DispatcherQueueItemStates.Queued && !x.EscalatedOn.HasValue &&
											 (x.DueOn ?? x.CreatedOn) <= now.AddMinutes(-escalateAfter)).ToList();
				if (stale.Count > 0)
				{
					var desk = await _presenceService.GetDeskAsync(departmentId, now);
					var recipients = desk.Members.Where(x => x.IsOnDuty && x.Presence != null && x.Presence.IsSignedIn() && x.DisplayState != DispatcherDisplayStates.OnBreak)
						.Select(x => x.UserId)
						.Concat(await _presenceService.GetSupervisorUserIdsAsync(departmentId, now))
						.Distinct(StringComparer.OrdinalIgnoreCase)
						.ToList();

					foreach (var item in stale)
					{
						var expected = item.Version;
						item.EscalatedOn = now;
						if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
							continue;

						changes++;
						await AddEventAsync(item, DispatcherQueueEventTypes.Escalated, null, $"{recipients.Count} notified", now, cancellationToken);
						await _notifier.NotifyQueueEscalationAsync(item, recipients, cancellationToken);
						await PublishAsync(item);
					}
				}
			}

			return changes;
		}

		public Task MarkOverflowedAsync(DispatcherQueueItem item, string note, CancellationToken cancellationToken = default) =>
			MarkOverflowedCoreAsync(item, note, requireQueued: false, copyBack: false, cancellationToken);

		public Task<bool> TryMarkOverflowedAsync(DispatcherQueueItem item, string note, CancellationToken cancellationToken = default) =>
			MarkOverflowedCoreAsync(item, note, requireQueued: false, copyBack: true, cancellationToken, fenced: true);

		/// <param name="requireQueued">
		/// Only while nobody has it: an item without a call is closed as Overflowed only if it is still Queued (an offer or an accept that
		/// landed since wins).
		/// </param>
		/// <param name="copyBack">Refresh <paramref name="item"/> from the row as written (or as found).</param>
		/// <param name="fenced">
		/// Only the row the caller judged: its Version and State must still be those of <paramref name="item"/>; one write, no retry.
		/// </param>
		private async Task<bool> MarkOverflowedCoreAsync(DispatcherQueueItem item, string note, bool requireQueued, bool copyBack,
			CancellationToken cancellationToken, bool fenced = false)
		{
			if (item == null)
				return false;

			var checkedVersion = item.Version;
			var checkedState = item.State;

			for (var attempt = 0; attempt < 3; attempt++)
			{
				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId) ?? (fenced ? null : item);
				if (current == null)
					return false;

				if (fenced && (current.Version != checkedVersion || current.State != checkedState))
				{
					if (copyBack)
						Copy(current, item);
					return false;
				}

				var now = UtcNow();
				var expected = current.Version;

				current.CallId ??= item.CallId;
				if (!current.CallId.HasValue && requireQueued && current.State != (int)DispatcherQueueItemStates.Queued)
				{
					if (copyBack)
						Copy(current, item);
					return false;
				}

				current.OverflowedOn = now;
				current.OverflowNote = Truncate(note, 250);
				current.LastActivityOn = now;
				if (!current.CallId.HasValue)
				{
					current.State = (int)DispatcherQueueItemStates.Overflowed;
					current.AssignedUserId = null;
					current.OfferExpiresOn = null;
				}
				else if (current.State == (int)DispatcherQueueItemStates.Overflowed)
				{
					// The overflow produced a call (a voicemail): the item waits in the queue again, now as that call.
					current.State = (int)DispatcherQueueItemStates.Queued;
					current.AssignedUserId = null;
				}

				if (!await _itemsRepository.TryUpdateAsync(current, expected, cancellationToken))
				{
					// Linking the pending call the overflow became is retried: that link must not be lost to a touch (a new message, a
					// voice event) landing in between. Closing an item without a call is not, nor a fenced write: whatever changed it since wins.
					if (current.CallId.HasValue && !fenced)
						continue;
					return false;
				}

				if (copyBack)
					Copy(current, item);
				await AddEventAsync(current, DispatcherQueueEventTypes.Overflowed, null, current.OverflowNote, now, cancellationToken);
				await PublishAsync(current);
				return true;
			}

			return false;
		}

		public async Task RecordVoiceEventAsync(DispatcherQueueItem item, DispatcherQueueEventTypes eventType, string userId, string detail,
			CancellationToken cancellationToken = default)
		{
			if (item == null)
				return;

			var now = UtcNow();
			await AddEventAsync(item, eventType, userId, Truncate(detail, 500), now, cancellationToken);

			var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
			if (current != null)
				await TouchAsync(current, now, cancellationToken);

			await PublishAsync(current ?? item);
		}

		public async Task<DispatcherQueueStats> GetStatsAsync(int departmentId, DateTime fromUtc, DateTime toUtc)
		{
			var items = (await _itemsRepository.GetCreatedInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<DispatcherQueueItem>()).ToList();
			var events = (await _eventsRepository.GetForDepartmentInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<DispatcherQueueEvent>()).ToList();
			var breaks = (await _breaksRepository.GetForDepartmentInRangeAsync(departmentId, fromUtc, toUtc) ?? Enumerable.Empty<DispatcherBreak>()).ToList();
			var profiles = await _userProfileService.GetAllProfilesForDepartmentAsync(departmentId) ?? new Dictionary<string, UserProfile>();

			return DispatcherDeskStatsCalculator.Build(items, events, breaks, id => profiles.TryGetValue(id, out var p) ? DispatcherPresenceService.NameOf(p) ?? id : id);
		}

		public async Task<int> PurgeAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var removed = await _itemsRepository.PurgeClosedBeforeAsync(utcNow.AddDays(-RetentionDays), cancellationToken);
			removed += await _breaksRepository.PurgeBeforeAsync(utcNow.AddDays(-RetentionDays), cancellationToken);
			return removed;
		}

		// ---------------------------------------------------------------- internals

		/// <summary>The Away reason the desk sets after missed offers (cleared when the dispatcher takes work again).</summary>
		public const string AwayAfterMissedOffers = "Missed offers";

		private async Task<bool> TryOfferAsync(DispatcherQueueItem item, DispatcherDeskMember target, DispatcherDeskConfig config, DateTime now,
			CancellationToken cancellationToken)
		{
			// A screened intake call (M0326) waits in the queue for a dispatcher to take; nobody is pushed or timed for it.
			if (await IsHeldForReviewAsync(item))
				return false;

			var expected = item.Version;
			item.State = (int)DispatcherQueueItemStates.Offered;
			item.AssignedUserId = target.UserId;
			item.OfferedOn = now;
			item.OfferExpiresOn = now.AddSeconds(config.EffectiveOfferTimeoutSeconds());
			item.OfferAttempts++;
			item.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
			{
				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current != null)
					Copy(current, item);
				return false;
			}

			if (target.Presence != null)
			{
				// Only the offer stamp: the member's presence was read before this pass, and writing it all back could undo a break (or
				// any state change) the dispatcher made since.
				target.Presence.LastOfferedOn = now;
				await _presencesRepository.SetLastOfferedOnAsync(target.Presence.DispatcherPresenceId, now, cancellationToken);
			}

			await AddEventAsync(item, DispatcherQueueEventTypes.Offered, target.UserId, null, now, cancellationToken);
			await _notifier.NotifyOfferAsync(item, target.UserId, cancellationToken);
			await PublishAsync(item);
			await _notifier.PublishDeskChangedAsync(item.DepartmentId, target.UserId);

			return true;
		}

		private async Task<bool> ReturnToQueueAsync(DispatcherQueueItem item, string userId, bool addToDeclined, DateTime now, CancellationToken cancellationToken)
		{
			var expected = item.Version;
			item.State = (int)DispatcherQueueItemStates.Queued;
			item.AssignedUserId = null;
			item.AcceptedOn = null;
			item.OfferExpiresOn = null;
			item.LastActivityOn = now;
			if (addToDeclined && !string.IsNullOrWhiteSpace(userId))
				item.DeclinedUserIds = AddUser(item.DeclinedUserIds, userId);

			if (await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return true;

			var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
			if (current != null)
				Copy(current, item);
			return false;
		}

		/// <summary>
		/// The item's intake call carries a screening (M0326: Enhanced AI read it as likely not an incident, or its sender is over the hourly
		/// limit), so the desk holds it in the queue instead of offering it. Items without a call never are; a screening that cannot be read
		/// lets the offer go (a real request must never wait on a lookup).
		/// </summary>
		private async Task<bool> IsHeldForReviewAsync(DispatcherQueueItem item)
		{
			if (_screening == null || item == null || !item.CallId.HasValue || item.CallId.Value <= 0)
				return false;

			try
			{
				var screenings = await _screening.Value.GetScreeningsAsync(item.DepartmentId, new[] { item.CallId.Value });
				return screenings != null && screenings.TryGetValue(item.CallId.Value, out var screening) && screening != IntakeScreenings.None;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"The dispatcher desk could not read the screening of call {item.CallId}; offering it.");
				return false;
			}
		}

		/// <summary>Offers out for a waiting call that is now screened go back to the queue (no decline, no missed offer).</summary>
		private async Task WithdrawHeldOffersAsync(int departmentId, List<DispatcherQueueItem> items, CancellationToken cancellationToken)
		{
			foreach (var item in items.Where(x => x.State == (int)DispatcherQueueItemStates.Offered))
			{
				if (!await IsHeldForReviewAsync(item))
					continue;

				var offeredTo = item.AssignedUserId;
				var now = UtcNow();
				if (!await ReturnToQueueAsync(item, offeredTo, addToDeclined: false, now, cancellationToken))
					continue;

				await AddEventAsync(item, DispatcherQueueEventTypes.HeldForReview, null, "Screened intake call", now, cancellationToken);
				await PublishAsync(item);
				if (!string.IsNullOrWhiteSpace(offeredTo))
					await _notifier.PublishDeskChangedAsync(departmentId, offeredTo);
			}
		}

		/// <summary>After an item went back to the queue: offer it on, overflow it when it is live and nobody is left.</summary>
		private async Task AfterReturnAsync(DispatcherQueueItem item, DateTime now, CancellationToken cancellationToken)
		{
			await PublishAsync(item);

			var config = await _configService.GetConfigAsync(item.DepartmentId);
			var members = await _presenceService.GetRoutableAsync(item.DepartmentId, now);
			var target = PickTarget(members, item, null);

			if (target != null && await TryOfferAsync(item, target, config, now, cancellationToken))
				return;

			if (NeedsOverflow(item, config, now))
				await OverflowAsync(item, config, now, "Every free dispatcher passed on it.", cancellationToken);
		}

		private async Task CountMissedOfferAsync(int departmentId, string userId, DispatcherDeskConfig config, DateTime now, CancellationToken cancellationToken)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return;

			var presence = await _presencesRepository.GetForUserAsync(departmentId, userId);
			if (presence == null || !presence.IsSignedIn())
				return;

			presence.MissedOffers++;
			var limit = config.EffectiveMissedOffersBeforeAway();
			var setAway = limit > 0 && presence.MissedOffers >= limit && presence.State == (int)DispatcherPresenceStates.Available;
			if (setAway)
			{
				presence.State = (int)DispatcherPresenceStates.Away;
				presence.AwayReason = AwayAfterMissedOffers;
				presence.StateChangedOn = now;
			}

			await _presencesRepository.SaveOrUpdateAsync(presence, cancellationToken, true);
			if (setAway)
				await _notifier.PublishDeskChangedAsync(departmentId, userId);
		}

		/// <summary>
		/// Live work with nobody to take it: a text or chat becomes a pending call (and keeps waiting, now as a call); a held phone
		/// caller is sent to the voice overflow (voicemail / AI voice intake), unless the department holds callers a while first.
		/// </summary>
		private bool NeedsOverflow(DispatcherQueueItem item, DispatcherDeskConfig config, DateTime now)
		{
			if (item.State != (int)DispatcherQueueItemStates.Queued || item.CallId.HasValue || item.OverflowedOn.HasValue)
				return false;

			switch ((DispatcherQueueItemKinds)item.Kind)
			{
				case DispatcherQueueItemKinds.Text:
				case DispatcherQueueItemKinds.Chat:
				case DispatcherQueueItemKinds.Other:
					return true;
				case DispatcherQueueItemKinds.Voice:
					return item.CreatedOn <= now.AddSeconds(-config.EffectiveVoiceHoldSeconds());
				default:
					return false;
			}
		}

		/// <summary>What an overflow attempt did to the item.</summary>
		private enum OverflowResults
		{
			/// <summary>Nothing: another pass has it, or it no longer waits (taken, already overflowed, the caller hung up).</summary>
			Skipped,

			/// <summary>Done: the text became a pending call linked to the item, or the held caller was sent to the voice overflow.</summary>
			Converted,

			/// <summary>Not done and left waiting in the queue (the save or the redirect failed); nothing was promised to the sender.</summary>
			Retry,

			/// <summary>Not done and the item was closed as Overflowed (nothing could be made of it).</summary>
			Closed
		}

		/// <remarks>
		/// Every path that sees the item waiting (the inbound text, the worker's timeout pass, a declined or returned offer) may get here
		/// at once, so the overflow first claims the item with a version-checked write: only the winner saves a pending call or moves the
		/// caller, and a dispatcher's accept landing first makes the claim fail.
		/// </remarks>
		private async Task<OverflowResults> OverflowAsync(DispatcherQueueItem item, DispatcherDeskConfig config, DateTime now, string reason,
			CancellationToken cancellationToken)
		{
			try
			{
				if (item.Kind == (int)DispatcherQueueItemKinds.Voice)
					return await OverflowVoiceAsync(item, now, reason, cancellationToken);

				// The claim stamps OverflowedOn while the item stays Queued (a dispatcher may still take the text); NeedsOverflow is false
				// for it from then on, so no other pass saves a second pending call for the same message.
				if (!await TryClaimAsync(item, now, Truncate(reason + " Saving it as a pending call.", 250), DispatcherQueueItemStates.Queued, cancellationToken))
					return OverflowResults.Skipped;

				Call call;
				try
				{
					call = await CreatePendingCallAsync(item, cancellationToken);
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Dispatcher queue overflow for item {item.DispatcherQueueItemId}: the pending call could not be saved");
					await ReleaseClaimAsync(item, reason + " The pending call could not be saved; trying again.", cancellationToken);
					return OverflowResults.Retry;
				}

				if (call == null)
				{
					await MarkOverflowedCoreAsync(item, reason + " No pending call could be created.", requireQueued: true, copyBack: true, cancellationToken);
					return item.IsOpen() ? OverflowResults.Retry : OverflowResults.Closed;
				}

				item.CallId = call.CallId;
				var useAi = config.GetTextOverflowMode() == DispatcherTextOverflowModes.EnhancedAi && await AiAvailableAsync(item.DepartmentId);
				if (!await MarkOverflowedCoreAsync(item, reason + $" Saved as pending call {call.Number ?? call.CallId.ToString()}" + (useAi ? "; Enhanced AI is filling it in." : "."),
						requireQueued: false, copyBack: true, cancellationToken))
				{
					// The call exists (dispatchers see it with the pending calls) but the item could not be linked to it.
					Logging.LogError($"Dispatcher queue overflow for item {item.DispatcherQueueItemId}: pending call {call.CallId} was saved but could not be linked to the item.");
				}

				if (useAi)
				{
					try
					{
						await _queueService.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
						{
							DepartmentId = item.DepartmentId,
							CallId = call.CallId,
							Channel = 1,
							QueuedOnUtc = now
						}, cancellationToken);
					}
					catch (Exception ex)
					{
						// The pending call stands without the AI's reading; a dispatcher fills it in.
						Logging.LogException(ex, $"Dispatcher queue overflow for item {item.DispatcherQueueItemId}: Enhanced AI could not be queued for call {call.CallId}");
					}
				}

				return OverflowResults.Converted;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher queue overflow for item {item.DispatcherQueueItemId}");
				return item.IsOpen() ? OverflowResults.Retry : OverflowResults.Closed;
			}
		}

		/// <summary>
		/// A held caller nobody took is sent to the voice overflow. The item is closed as Overflowed first (version-checked from Queued),
		/// so a dispatcher cannot accept a caller who is being moved and two passes never both move them; a redirect the provider refused
		/// (an error, or a call that already ended: the two cannot be told apart) reopens it in the queue, where a dispatcher can take it or
		/// call back and the caller's own hold loop still takes a message.
		/// </summary>
		private async Task<OverflowResults> OverflowVoiceAsync(DispatcherQueueItem item, DateTime now, string reason, CancellationToken cancellationToken)
		{
			// A caller who hung up while held stays on the desk for Call back (plan D8): nothing to send to voicemail.
			if (await CallerLeftAsync(item))
				return OverflowResults.Skipped;

			if (!await TryClaimAsync(item, now, Truncate(reason + " Sending the caller to the voice overflow.", 250), DispatcherQueueItemStates.Overflowed, cancellationToken))
				return OverflowResults.Skipped;

			await PublishAsync(item);

			bool redirected;
			try
			{
				redirected = !string.IsNullOrWhiteSpace(item.ExternalReference) &&
							 await _voiceProvider.RedirectCallAsync(item.ExternalReference, VoiceOverflowUrl(item), cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher queue overflow for item {item.DispatcherQueueItemId}: the caller could not be redirected");
				redirected = false;
			}

			if (redirected)
			{
				await AddEventAsync(item, DispatcherQueueEventTypes.Overflowed, null, reason + " Caller sent to the voice overflow.", now, cancellationToken);
				return OverflowResults.Converted;
			}

			// Reopen only what this claim closed: the voicemail (or the hold loop's own overflow) may have moved the item on since.
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current == null || current.State != (int)DispatcherQueueItemStates.Overflowed || current.CallId.HasValue)
					return OverflowResults.Skipped;

				var expected = current.Version;
				current.State = (int)DispatcherQueueItemStates.Queued;
				current.OverflowNote = Truncate(reason + " The caller could not be sent to the voice overflow.", 250);
				current.LastActivityOn = UtcNow();
				if (!await _itemsRepository.TryUpdateAsync(current, expected, cancellationToken))
					continue;

				Copy(current, item);
				await AddEventAsync(item, DispatcherQueueEventTypes.Reopened, null, current.OverflowNote, now, cancellationToken);
				await PublishAsync(item);
				return OverflowResults.Retry;
			}

			return OverflowResults.Skipped;
		}

		/// <summary>
		/// The overflow claim: a version-checked write, from the snapshot this pass judged waiting (Queued, no call, not overflowed), that
		/// stamps OverflowedOn and moves the item to <paramref name="to"/>. False when anything changed the item since.
		/// </summary>
		private async Task<bool> TryClaimAsync(DispatcherQueueItem item, DateTime now, string note, DispatcherQueueItemStates to, CancellationToken cancellationToken)
		{
			if (item.State != (int)DispatcherQueueItemStates.Queued || item.CallId.HasValue || item.OverflowedOn.HasValue)
				return false;

			var before = new DispatcherQueueItem();
			Copy(item, before);

			var expected = item.Version;
			item.State = (int)to;
			item.OverflowedOn = now;
			item.OverflowNote = note;
			item.LastActivityOn = now;
			if (to != DispatcherQueueItemStates.Queued)
			{
				item.AssignedUserId = null;
				item.OfferExpiresOn = null;
			}

			if (await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
				return true;

			Copy(await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId) ?? before, item);
			return false;
		}

		/// <summary>Gives a text overflow claim back (the pending call was not saved), so the worker's next pass tries again.</summary>
		private async Task ReleaseClaimAsync(DispatcherQueueItem item, string note, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3; attempt++)
			{
				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current == null || !current.IsOpen() || current.CallId.HasValue || !current.OverflowedOn.HasValue)
					return;

				var expected = current.Version;
				current.OverflowedOn = null;
				current.OverflowNote = Truncate(note, 250);
				if (await _itemsRepository.TryUpdateAsync(current, expected, cancellationToken))
				{
					Copy(current, item);
					return;
				}
			}
		}

		/// <summary>The pending call a text or chat conversation becomes when no dispatcher can take it (nobody dispatched yet).</summary>
		private async Task<Call> CreatePendingCallAsync(DispatcherQueueItem item, CancellationToken cancellationToken)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(item.DepartmentId, false);
			if (department == null)
				return null;

			var calls = _callsService.Value;
			var priorities = await calls.GetActiveCallPrioritiesForDepartmentAsync(item.DepartmentId);
			var messages = (await _messagesRepository.GetForItemAsync(item.DispatcherQueueItemId) ?? Enumerable.Empty<DispatcherConversationMessage>())
				.Where(x => x.Direction == (int)DispatcherMessageDirections.Inbound && !string.IsNullOrWhiteSpace(x.Body))
				.Select(x => x.Body.Trim())
				.ToList();
			var text = messages.Count > 0 ? string.Join(Environment.NewLine, messages) : item.Preview ?? string.Empty;
			var now = UtcNow();
			var from = string.IsNullOrWhiteSpace(item.SourceName) ? item.SourceAddress : item.SourceName;

			var call = new Call
			{
				DepartmentId = item.DepartmentId,
				Name = Truncate($"{(item.Kind == (int)DispatcherQueueItemKinds.Chat ? "Chat" : "Text")} from {from}", 100),
				NatureOfCall = text,
				Notes = text,
				ReportingUserId = department.ManagingUserId,
				Priority = CallPriorityMatch.DefaultForImport(priorities),
				LoggedOn = now,
				State = (int)CallStates.Pending,
				CallSource = (int)CallSources.EmailImport,
				SourceIdentifier = "DQ:" + item.DispatcherQueueItemId,
				ContactName = item.SourceName,
				ContactNumber = item.Kind == (int)DispatcherQueueItemKinds.Text ? item.SourceAddress : null,
				Dispatches = new Collection<CallDispatch>()
			};

			return await calls.SaveCallAsync(call, cancellationToken);
		}

		/// <summary>The held caller's own leg ended (events about the caller's leg carry no user id; a dispatcher's leg carries theirs).</summary>
		private async Task<bool> CallerLeftAsync(DispatcherQueueItem item) =>
			(await _eventsRepository.GetForItemAsync(item.DispatcherQueueItemId) ?? Enumerable.Empty<DispatcherQueueEvent>())
			.Any(x => x.EventType == (int)DispatcherQueueEventTypes.VoiceEnded && string.IsNullOrWhiteSpace(x.UserId));

		private async Task<bool> AiAvailableAsync(int departmentId)
		{
			try
			{
				return await _aiDispatchEnrichmentService.Value.IsAvailableAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk AI availability");
				return false;
			}
		}

		internal static string VoiceOverflowUrl(DispatcherQueueItem item) =>
			$"{Config.SystemBehaviorConfig.ResgridApiBaseUrl}/api/Twilio/DeskOverflow?itemId={Uri.EscapeDataString(item.DispatcherQueueItemId)}";

		private async Task AcknowledgeAsync(DispatcherQueueItem item, DispatcherDeskConfig config, CancellationToken cancellationToken, bool overflowed = false)
		{
			// Intake already answered an intake thread with its hand-off welcome.
			if (!config.AcknowledgeTexts || item.Kind != (int)DispatcherQueueItemKinds.Text || string.IsNullOrWhiteSpace(item.SourceAddress) ||
				DispatcherRoutingPolicy.IntakeConversationIdOf(item) != null)
				return;

			try
			{
				// Overflowed: no dispatcher was free, so the text became a pending call the dispatchers will see; no reply is promised.
				await _conversationService.Value.SendSystemReplyAsync(item, overflowed
						? $"We received your message and passed it to the dispatchers. If this is a life-threatening emergency, call {EmergencyNumber.Current}."
						: $"We received your message. A dispatcher will reply here as soon as possible. If this is a life-threatening emergency, call {EmergencyNumber.Current}.",
					cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Dispatcher desk text acknowledgement");
			}
		}

		private async Task<DispatcherQueueItem> FindDuplicateAsync(DispatcherEnqueueRequest request)
		{
			if (request.CallId.HasValue)
			{
				var open = (await _itemsRepository.GetOpenForCallAsync(request.DepartmentId, request.CallId.Value) ?? Enumerable.Empty<DispatcherQueueItem>()).FirstOrDefault();
				if (open != null)
					return open;
			}

			if (!string.IsNullOrWhiteSpace(request.ExternalReference))
			{
				var existing = await _itemsRepository.GetByExternalReferenceAsync(request.ExternalReference);
				if (existing != null && existing.DepartmentId == request.DepartmentId)
					return existing;
			}

			if (!string.IsNullOrWhiteSpace(request.ProviderMessageId))
			{
				var message = await _messagesRepository.GetByProviderMessageIdAsync(request.ProviderMessageId);
				if (message != null)
					return await _itemsRepository.GetItemAsync(message.DispatcherQueueItemId);
			}

			return null;
		}

		/// <summary>The open item that won an insert race against <paramref name="item"/> (same call, or same external reference).</summary>
		private async Task<DispatcherQueueItem> FindOpenWinnerAsync(DispatcherQueueItem item)
		{
			if (item.CallId.HasValue)
			{
				var forCall = (await _itemsRepository.GetOpenForCallAsync(item.DepartmentId, item.CallId.Value) ?? Enumerable.Empty<DispatcherQueueItem>())
					.FirstOrDefault(x => x.DispatcherQueueItemId != item.DispatcherQueueItemId);
				if (forCall != null)
					return forCall;
			}

			if (!string.IsNullOrWhiteSpace(item.ExternalReference))
			{
				var byReference = await _itemsRepository.GetByExternalReferenceAsync(item.ExternalReference);
				if (byReference != null && byReference.DepartmentId == item.DepartmentId && byReference.IsOpen() &&
					byReference.DispatcherQueueItemId != item.DispatcherQueueItemId)
					return byReference;
			}

			return null;
		}

		/// <summary>The open item a pending call was made from by its overflow ("DQ:{itemId}"), or null.</summary>
		private async Task<DispatcherQueueItem> GetOverflowOwnerAsync(Call call)
		{
			const string prefix = "DQ:";
			if (call?.SourceIdentifier == null || !call.SourceIdentifier.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				return null;

			var ownerId = call.SourceIdentifier.Substring(prefix.Length).Trim();
			if (ownerId.Length == 0)
				return null;

			var owner = await _itemsRepository.GetItemAsync(ownerId);
			return owner != null && owner.DepartmentId == call.DepartmentId && owner.IsOpen() ? owner : null;
		}

		/// <summary>A call item is offered only while its call still waits for the kind it is queued as.</summary>
		internal static bool IsEligibleFor(DispatcherQueueItemKinds kind, Call call)
		{
			switch (kind)
			{
				case DispatcherQueueItemKinds.PendingCall:
					return !call.IsDeleted && call.State == (int)CallStates.Pending;
				case DispatcherQueueItemKinds.ScheduledCall:
					return !call.IsDeleted && call.State == (int)CallStates.Active && call.DispatchOn.HasValue && call.HasBeenDispatched != true;
				default:
					return true;
			}
		}

		private static DispatcherDeskMember PickTarget(List<DispatcherDeskMember> members, DispatcherQueueItem item, string preferredUserId)
		{
			if (members == null || members.Count == 0)
				return null;

			var declined = new HashSet<string>(item.GetDeclinedUserIds(), StringComparer.OrdinalIgnoreCase);
			var candidates = members.Where(x => !declined.Contains(x.UserId)).ToList();

			if (!string.IsNullOrWhiteSpace(preferredUserId))
			{
				var preferred = candidates.FirstOrDefault(x => string.Equals(x.UserId, preferredUserId, StringComparison.OrdinalIgnoreCase));
				if (preferred != null)
					return preferred;
			}

			return candidates.FirstOrDefault();
		}

		private async Task<(DispatcherQueueItem Item, DispatcherDeskErrors Error)> LoadOpenAsync(int departmentId, string dispatcherQueueItemId)
		{
			if (string.IsNullOrWhiteSpace(dispatcherQueueItemId))
				return (null, DispatcherDeskErrors.ItemNotFound);

			var item = await _itemsRepository.GetItemAsync(dispatcherQueueItemId);
			if (item == null || item.DepartmentId != departmentId)
				return (null, DispatcherDeskErrors.ItemNotFound);

			return item.IsOpen() ? (item, DispatcherDeskErrors.None) : (item, DispatcherDeskErrors.ItemNotAvailable);
		}

		private async Task<bool> CallBelongsAsync(int departmentId, int callId)
		{
			var call = await _callsService.Value.GetCallByIdAsync(callId);
			return call != null && call.DepartmentId == departmentId;
		}

		private async Task TouchAsync(DispatcherQueueItem item, DateTime now, CancellationToken cancellationToken)
		{
			for (var attempt = 0; attempt < 3; attempt++)
			{
				// Never backwards: a retry after a newer writer keeps that writer's later activity time.
				if (item.LastActivityOn >= now)
					return;

				var expected = item.Version;
				item.LastActivityOn = now;
				if (await _itemsRepository.TryUpdateAsync(item, expected, cancellationToken))
					return;

				var current = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
				if (current == null)
					return;

				Copy(current, item);
			}
		}

		private async Task AddEventAsync(DispatcherQueueItem item, DispatcherQueueEventTypes type, string userId, string detail, DateTime now,
			CancellationToken cancellationToken)
		{
			try
			{
				await _eventsRepository.InsertAsync(new DispatcherQueueEvent
				{
					DispatcherQueueEventId = Guid.NewGuid().ToString(),
					DepartmentId = item.DepartmentId,
					DispatcherQueueItemId = item.DispatcherQueueItemId,
					EventType = (int)type,
					UserId = userId,
					OccurredOn = now,
					Detail = Truncate(detail, 500)
				}, cancellationToken, true);
			}
			catch (Exception ex)
			{
				// History is for the record and the report; never fail the desk action over it.
				Logging.LogException(ex, $"Dispatcher queue event {type} for {item.DispatcherQueueItemId}");
			}
		}

		private Task PublishAsync(DispatcherQueueItem item)
		{
			if (_notifier is DispatcherDeskNotifier concrete)
				return concrete.PublishQueueChangedAsync(item);

			return _notifier.PublishQueueChangedAsync(item.DepartmentId, item.DispatcherQueueItemId);
		}

		internal static bool IsWaiting(Call call) =>
			call != null && !call.IsDeleted && (call.State == (int)CallStates.Pending ||
				call.State == (int)CallStates.Active && call.DispatchOn.HasValue && call.HasBeenDispatched != true);

		internal static bool IsConversation(DispatcherQueueItemKinds kind) =>
			kind == DispatcherQueueItemKinds.Text || kind == DispatcherQueueItemKinds.Chat || kind == DispatcherQueueItemKinds.Email ||
			kind == DispatcherQueueItemKinds.Voice || kind == DispatcherQueueItemKinds.Other;

		private static string KindName(DispatcherQueueItemKinds kind) => kind.ToString();

		private static void Copy(DispatcherQueueItem from, DispatcherQueueItem to)
		{
			to.State = from.State;
			to.AssignedUserId = from.AssignedUserId;
			to.OfferedOn = from.OfferedOn;
			to.OfferExpiresOn = from.OfferExpiresOn;
			to.OfferAttempts = from.OfferAttempts;
			to.DeclinedUserIds = from.DeclinedUserIds;
			to.AcceptedOn = from.AcceptedOn;
			to.CompletedOn = from.CompletedOn;
			to.CompletedByUserId = from.CompletedByUserId;
			to.Outcome = from.Outcome;
			to.CallId = from.CallId;
			to.Version = from.Version;
			to.LastActivityOn = from.LastActivityOn;
			to.OverflowedOn = from.OverflowedOn;
			to.OverflowNote = from.OverflowNote;
			to.EscalatedOn = from.EscalatedOn;
			to.Note = from.Note;
		}

		private static string AddUser(string list, string userId)
		{
			var users = (list ?? string.Empty).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
			if (!users.Contains(userId, StringComparer.OrdinalIgnoreCase))
				users.Add(userId);
			return string.Join(",", users);
		}

		private static string RemoveUser(string list, string userId)
		{
			var users = (list ?? string.Empty).Split(',').Select(x => x.Trim())
				.Where(x => x.Length > 0 && !string.Equals(x, userId, StringComparison.OrdinalIgnoreCase)).ToList();
			return users.Count == 0 ? null : string.Join(",", users);
		}

		private static DispatcherDeskResult<DispatcherQueueItem> Fail(DispatcherDeskErrors error, string message = null) =>
			DispatcherDeskResult<DispatcherQueueItem>.Fail(error, message);

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);
	}
}
