using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Intake text threads on the desk (dispatchers-on-duty-plan.md F2; call-intake-and-ai-destinations-plan.md §8, D17). Intake owns
	/// the SMS thread: the welcome from the region's master number, every message as a call note, Enhanced AI's questions, and the
	/// "You are now connected with {FirstName}" text. The desk offers the thread to on-duty dispatchers as a text item linked to
	/// its pending call (<see cref="DispatcherRoutingPolicy.IntakeReference"/>) and keeps the two in step: accepting connects the
	/// dispatcher, releasing hands the thread back (AI resumes), completing closes it, and intake's callbacks (a connect or release
	/// from the call page, a close by the idle sweep, STOP or the call closing) move the item. Writes to intake use
	/// <see cref="CancellationToken.None"/>: once the desk has changed the item, the thread must follow even if the request goes away.
	/// </summary>
	public partial class DispatcherRoutingService
	{
		private const string LinkedDetail = "Linked to the intake text thread";
		private const string ConnectedFromCallDetail = "Connected to the text thread from the call";

		/// <summary>Intake's conversation service, when this build has intake (none before the intake merge: threads never reach the desk then).</summary>
		private IIntakeConversationService IntakeConversations => _intakeConversations.Value?.FirstOrDefault();

		public async Task<DispatcherRouteResult> RouteIntakeConversationAsync(IntakeConversation conversation, Call call, CancellationToken cancellationToken = default)
		{
			if (conversation == null || call == null || string.IsNullOrWhiteSpace(conversation.IntakeConversationId) || conversation.DepartmentId <= 0 ||
				call.DepartmentId != conversation.DepartmentId || !conversation.IsOpen)
				return DispatcherRouteResult.NotRouted();

			var config = await _configService.GetConfigAsync(conversation.DepartmentId);
			if (!config.Routes(DispatcherQueueItemKinds.Text))
				return DispatcherRouteResult.NotRouted();

			var linked = await FindIntakeItemAsync(conversation);
			if (linked != null)
				return new DispatcherRouteResult { Outcome = DispatcherRouteOutcomes.Duplicate, Item = linked, UserId = linked.AssignedUserId };

			var result = await EnqueueAsync(new DispatcherEnqueueRequest
			{
				DepartmentId = conversation.DepartmentId,
				Kind = DispatcherQueueItemKinds.Text,
				CallId = call.CallId,
				SourceAddress = conversation.ParticipantAddress,
				SourceName = conversation.ParticipantName,
				TargetAddress = conversation.InboundAddress,
				Subject = call.Name,
				IntakeConversationId = conversation.IntakeConversationId
			}, cancellationToken);

			if (result == null || result.Item == null || result.Outcome == DispatcherRouteOutcomes.NotRouted)
				return result ?? DispatcherRouteResult.NotRouted();

			// The call reached the desk first (its Call Added handler queued it as a pending call): that item becomes the thread's.
			if (DispatcherRoutingPolicy.IntakeConversationIdOf(result.Item) == null)
			{
				var item = await LinkIntakeThreadAsync(result.Item, conversation);
				if (item == null)
					return DispatcherRouteResult.NotRouted();

				result.Item = item;
				result.UserId = item.AssignedUserId;
			}

			await CloseUnlinkedCallItemsAsync(result.Item);
			await RecordDeskItemAsync(conversation, result.Item);

			return result;
		}

		public async Task OnIntakeParticipantMessageAsync(IntakeConversation conversation, CancellationToken cancellationToken = default)
		{
			var item = await FindIntakeItemAsync(conversation);
			if (item == null || !item.IsOpen())
				return;

			var now = UtcNow();
			await TouchAsync(item, now, CancellationToken.None);
			await AddEventAsync(item, DispatcherQueueEventTypes.MessageReceived, null, null, now, CancellationToken.None);

			// The text itself stays in the call's notes (ADP-protected); the push only says there is a new message.
			if (item.State == (int)DispatcherQueueItemStates.Accepted && !string.IsNullOrWhiteSpace(item.AssignedUserId))
				await _notifier.NotifyMessageAsync(item, item.AssignedUserId, "Open the desk to read it and reply.", CancellationToken.None);

			await PublishAsync(item);
		}

		public async Task OnIntakeDispatcherConnectedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return;

			var item = await FindIntakeItemAsync(conversation);
			if (item == null || !item.IsOpen())
				return;

			// The desk's own accept (and a transfer accepted) already holds the item for this dispatcher.
			if (item.State == (int)DispatcherQueueItemStates.Accepted && SameUser(item.AssignedUserId, userId))
				return;

			var now = UtcNow();
			var previous = item.AssignedUserId;
			var expected = item.Version;
			item.State = (int)DispatcherQueueItemStates.Accepted;
			item.AssignedUserId = userId;
			item.AcceptedOn = now;
			item.OfferExpiresOn = null;
			item.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(item, expected, CancellationToken.None))
				return;

			await AddEventAsync(item, DispatcherQueueEventTypes.Accepted, userId, ConnectedFromCallDetail, now, CancellationToken.None);
			await PublishAsync(item);

			if (!string.IsNullOrWhiteSpace(previous) && !SameUser(previous, userId))
				await _notifier.PublishDeskChangedAsync(item.DepartmentId, previous);

			await _notifier.PublishDeskChangedAsync(item.DepartmentId, userId);
		}

		public async Task OnIntakeDispatcherReleasedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken = default)
		{
			var item = await FindIntakeItemAsync(conversation);

			// The desk's own release has already put the item back.
			if (item == null || item.State != (int)DispatcherQueueItemStates.Accepted || !SameUser(item.AssignedUserId, userId))
				return;

			var now = UtcNow();
			if (!await ReturnToQueueAsync(item, userId, addToDeclined: false, now, CancellationToken.None))
				return;

			await AddEventAsync(item, DispatcherQueueEventTypes.Released, userId, "Stepped away from the text thread on the call", now, CancellationToken.None);
			await AfterReturnAsync(item, now, CancellationToken.None);
			await _notifier.PublishDeskChangedAsync(item.DepartmentId, userId);
		}

		public async Task OnIntakeConversationClosedAsync(IntakeConversation conversation, CancellationToken cancellationToken = default)
		{
			var item = await FindIntakeItemAsync(conversation);

			// The desk's own complete closed the thread: nothing left to do.
			if (item == null || !item.IsOpen())
				return;

			var now = UtcNow();
			await AddEventAsync(item, DispatcherQueueEventTypes.ConversationClosed, null, conversation.CloseReason, now, CancellationToken.None);

			var call = item.CallId.HasValue ? await _callsService.Value.GetCallByIdAsync(item.CallId.Value) : null;
			if (IsWaiting(call))
			{
				// The thread is over but the call still waits for a dispatcher: the item stays on the desk as the call's work. Not yet
				// taken, it reads as a pending call again (no reply box for a thread that can no longer answer).
				if (item.State != (int)DispatcherQueueItemStates.Accepted)
				{
					for (var attempt = 0; attempt < 3 && item != null && item.IsOpen(); attempt++)
					{
						var expected = item.Version;
						item.Kind = (int)(call.State == (int)CallStates.Pending ? DispatcherQueueItemKinds.PendingCall : DispatcherQueueItemKinds.ScheduledCall);
						item.LastActivityOn = now;
						if (await _itemsRepository.TryUpdateAsync(item, expected, CancellationToken.None))
							break;

						item = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
					}
				}

				if (item != null)
					await PublishAsync(item);

				return;
			}

			var outcome = call == null || call.IsDeleted ? DispatcherQueueItemOutcomes.NoActionNeeded
				: call.State == (int)CallStates.Active ? DispatcherQueueItemOutcomes.Dispatched : DispatcherQueueItemOutcomes.Closed;
			var holder = item.State == (int)DispatcherQueueItemStates.Accepted ? item.AssignedUserId : null;
			var version = item.Version;

			item.State = (int)DispatcherQueueItemStates.Completed;
			item.Outcome = (int)outcome;
			item.CompletedOn = now;
			item.CompletedByUserId = holder;
			item.OfferExpiresOn = null;
			item.LastActivityOn = now;

			if (!await _itemsRepository.TryUpdateAsync(item, version, CancellationToken.None))
				return;

			await AddEventAsync(item, DispatcherQueueEventTypes.Completed, holder, outcome + ": the text thread closed", now, CancellationToken.None);
			await PublishAsync(item);

			if (!string.IsNullOrWhiteSpace(holder))
			{
				await _notifier.PublishDeskChangedAsync(item.DepartmentId, holder);
				await RouteDepartmentAsync(item.DepartmentId, CancellationToken.None);
			}
		}

		// ---------------------------------------------------------------- desk actions on a linked thread

		/// <summary>
		/// After the desk accepted an item: connects the dispatcher to its intake thread. Null to go on; a refusal when someone
		/// already connected to the thread outside the desk (the item is then theirs).
		/// </summary>
		private async Task<DispatcherDeskResult<DispatcherQueueItem>> ConnectIntakeThreadAsync(DispatcherQueueItem item, string userId, bool takeOver, DateTime now,
			CancellationToken cancellationToken)
		{
			var conversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item);
			var intake = IntakeConversations;
			if (conversationId == null || intake == null)
				return null;

			IntakeConversationActionResults result;
			try
			{
				result = await intake.ConnectDispatcherAsync(item.DepartmentId, conversationId, userId, takeOver, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The item stays taken: the dispatcher still has the call, and can connect to the thread from the call page.
				Logging.LogException(ex, $"Dispatcher desk: connecting {item.DispatcherQueueItemId} to intake thread {conversationId}");
				return null;
			}

			// Done, or the thread already closed / the texter opted out: the item stays the dispatcher's as the call's work.
			if (result != IntakeConversationActionResults.HeldByAnotherDispatcher)
				return null;

			var conversation = await intake.GetByIdAsync(item.DepartmentId, conversationId);
			var holder = conversation?.AssignedUserId;
			var expected = item.Version;

			if (!string.IsNullOrWhiteSpace(holder) && !SameUser(holder, userId))
			{
				item.AssignedUserId = holder;
				item.AcceptedOn = now;
				item.LastActivityOn = now;
				if (await _itemsRepository.TryUpdateAsync(item, expected, CancellationToken.None))
					await AddEventAsync(item, DispatcherQueueEventTypes.Accepted, holder, ConnectedFromCallDetail, now, CancellationToken.None);

				await _notifier.PublishDeskChangedAsync(item.DepartmentId, holder);
			}
			else if (await ReturnToQueueAsync(item, userId, addToDeclined: false, now, CancellationToken.None))
			{
				await AddEventAsync(item, DispatcherQueueEventTypes.Released, userId, "The text thread could not be connected", now, CancellationToken.None);
			}

			await PublishAsync(item);
			await _notifier.PublishDeskChangedAsync(item.DepartmentId, userId);

			return Fail(DispatcherDeskErrors.ItemNotAvailable, "Another dispatcher is already talking to this texter.");
		}

		/// <summary>A held thread went back to the queue: Enhanced AI resumes (or nobody answers) until the next dispatcher connects.</summary>
		private async Task ReleaseIntakeThreadAsync(DispatcherQueueItem item, string userId, CancellationToken cancellationToken)
		{
			var conversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item);
			var intake = IntakeConversations;
			if (conversationId == null || intake == null || string.IsNullOrWhiteSpace(userId))
				return;

			try
			{
				await intake.ReleaseDispatcherAsync(item.DepartmentId, conversationId, userId, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: releasing intake thread {conversationId}");
			}
		}

		/// <summary>The item was completed: its thread ends too (optionally telling the texter, from the master number).</summary>
		private async Task CloseIntakeThreadAsync(DispatcherQueueItem item, string userId, DispatcherQueueItemOutcomes outcome, bool notifyParticipant,
			CancellationToken cancellationToken)
		{
			var conversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item);
			var intake = IntakeConversations;
			if (conversationId == null || intake == null)
				return;

			try
			{
				await intake.CloseAsync(item.DepartmentId, conversationId, userId, $"Completed on the dispatcher desk ({outcome})", notifyParticipant, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: closing intake thread {conversationId}");
			}
		}

		private async Task<IntakeConversationView> GetIntakeThreadAsync(DispatcherQueueItem item, string viewerUserId)
		{
			var conversationId = DispatcherRoutingPolicy.IntakeConversationIdOf(item);
			var intake = IntakeConversations;
			if (conversationId == null || intake == null)
				return null;

			try
			{
				return await intake.GetViewAsync(item.DepartmentId, conversationId, viewerUserId ?? item.AssignedUserId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: reading intake thread {conversationId}");
				return null;
			}
		}

		// ---------------------------------------------------------------- linking

		private async Task<DispatcherQueueItem> FindIntakeItemAsync(IntakeConversation conversation)
		{
			if (conversation == null || string.IsNullOrWhiteSpace(conversation.IntakeConversationId))
				return null;

			DispatcherQueueItem item = null;
			if (!string.IsNullOrWhiteSpace(conversation.DeskItemId))
				item = await _itemsRepository.GetItemAsync(conversation.DeskItemId);

			if (item == null || DispatcherRoutingPolicy.IntakeConversationIdOf(item) != conversation.IntakeConversationId)
				item = await _itemsRepository.GetByExternalReferenceAsync(DispatcherRoutingPolicy.IntakeReference(conversation.IntakeConversationId));

			return item != null && item.DepartmentId == conversation.DepartmentId ? item : null;
		}

		/// <summary>Turns the call's open item (queued by the call's own event) into the thread's text item.</summary>
		private async Task<DispatcherQueueItem> LinkIntakeThreadAsync(DispatcherQueueItem item, IntakeConversation conversation)
		{
			var now = UtcNow();

			for (var attempt = 0; attempt < 3 && item != null && item.IsOpen(); attempt++)
			{
				var expected = item.Version;
				item.Kind = (int)DispatcherQueueItemKinds.Text;
				item.ExternalReference = DispatcherRoutingPolicy.IntakeReference(conversation.IntakeConversationId);
				item.ConversationKey = null;
				item.SourceAddress = Truncate(conversation.ParticipantAddress, 256);
				item.SourceName = Truncate(conversation.ParticipantName, 200);
				item.TargetAddress = Truncate(conversation.InboundAddress, 256);
				item.LastActivityOn = now;

				if (await _itemsRepository.TryUpdateAsync(item, expected, CancellationToken.None))
				{
					await AddEventAsync(item, DispatcherQueueEventTypes.MessageReceived, null, LinkedDetail, now, CancellationToken.None);

					// Already taken as the pending call: that dispatcher is the one the texter is now talking to.
					if (item.State == (int)DispatcherQueueItemStates.Accepted && !string.IsNullOrWhiteSpace(item.AssignedUserId))
						await ConnectIntakeThreadAsync(item, item.AssignedUserId, takeOver: false, now, CancellationToken.None);

					await PublishAsync(item);
					return item;
				}

				item = await _itemsRepository.GetItemAsync(item.DispatcherQueueItemId);
			}

			return null;
		}

		/// <summary>
		/// The call's own event and the thread can both queue the call at the same moment: a second, unlinked item for the same
		/// call that nobody holds yet is closed as a duplicate of the thread's.
		/// </summary>
		private async Task CloseUnlinkedCallItemsAsync(DispatcherQueueItem linked)
		{
			if (!linked.CallId.HasValue)
				return;

			var now = UtcNow();
			var others = (await _itemsRepository.GetOpenForCallAsync(linked.DepartmentId, linked.CallId.Value) ?? Enumerable.Empty<DispatcherQueueItem>())
				.Where(x => x.DispatcherQueueItemId != linked.DispatcherQueueItemId && DispatcherRoutingPolicy.IntakeConversationIdOf(x) == null &&
							x.State != (int)DispatcherQueueItemStates.Accepted)
				.ToList();

			foreach (var other in others)
			{
				var expected = other.Version;
				var offeredTo = other.AssignedUserId;
				other.State = (int)DispatcherQueueItemStates.Completed;
				other.Outcome = (int)DispatcherQueueItemOutcomes.Duplicate;
				other.CompletedOn = now;
				other.OfferExpiresOn = null;
				other.LastActivityOn = now;

				if (!await _itemsRepository.TryUpdateAsync(other, expected, CancellationToken.None))
					continue;

				await AddEventAsync(other, DispatcherQueueEventTypes.Completed, null, "Duplicate of the intake text thread's item", now, CancellationToken.None);
				await PublishAsync(other);
				if (!string.IsNullOrWhiteSpace(offeredTo))
					await _notifier.PublishDeskChangedAsync(other.DepartmentId, offeredTo);
			}
		}

		private async Task RecordDeskItemAsync(IntakeConversation conversation, DispatcherQueueItem item)
		{
			var intake = IntakeConversations;
			if (intake == null || item == null || string.Equals(conversation.DeskItemId, item.DispatcherQueueItemId, StringComparison.Ordinal))
				return;

			try
			{
				await intake.SetDeskItemAsync(conversation.DepartmentId, conversation.IntakeConversationId, item.DispatcherQueueItemId, CancellationToken.None);
				conversation.DeskItemId = item.DispatcherQueueItemId;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Dispatcher desk: recording item {item.DispatcherQueueItemId} on intake thread {conversation.IntakeConversationId}");
			}
		}

		private static bool SameUser(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
	}
}
