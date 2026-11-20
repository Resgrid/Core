using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Intake SMS conversations (call-intake-and-ai-destinations-plan.md §8 D14–D17, M0325). A text to a department's dedicated
	/// number becomes a pending call (D7) and opens a thread continued from the region's master Resgrid number (the dedicated number is
	/// inbound-only, D5): the hand-off welcome, then Enhanced AI's follow-up questions (worker, aidispatchtriage queue) or nobody until a
	/// dispatcher connects ("You are now connected with {FirstName} …"). The texter's replies to the master number come back here
	/// (TwilioController, ahead of member commands). Every message is a note on the call: the texter's as <see cref="CallNoteSources.IntakeText"/>
	/// (a person's note, so it triggers the AI destination review), everything sent to them as System notes; IntakeConversationMessages
	/// is only the delivery log. Every change raises <see cref="IntakeConversationUpdatedEvent"/>; the dispatcher desk follows through the
	/// <see cref="IIntakeConversationRouter"/> callbacks (each awaited in its own try/catch; a router never fails an action). State changes
	/// are conditional statements that write only the columns they change (never a whole in-memory copy, which would wipe the desk's
	/// DeskItemId), so racing webhooks, dispatchers, the desk and worker 80 never both win.
	/// </summary>
	public class IntakeConversationService : IIntakeConversationService, IIntakeConversationOperations
	{
		/// <summary>A router call slower than this is logged; the desk must queue work, not do it inline (it runs inside the Twilio webhook).</summary>
		private static readonly TimeSpan RouterSlowWarning = TimeSpan.FromSeconds(3);

		private const int SweepBatch = 500;
		private static readonly int[] OpenCallStates = { (int)CallStates.Active, (int)CallStates.Pending };

		private readonly IIntakeConversationsRepository _conversations;
		private readonly IIntakeConversationMessagesRepository _messages;
		private readonly IIntakeChannelsRepository _channels;
		private readonly ICallsService _calls;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IIntakeSmsProvider _sms;
		private readonly IIntakeConversationAssistant _assistant;
		private readonly IProtectedReadService _protectedRead;
		private readonly IQueueService _queue;
		private readonly IEventAggregator _events;
		private readonly Lazy<IEnumerable<IIntakeConversationRouter>> _routers;
		private readonly TimeProvider _clock;
		private DateTime _lastStamp = DateTime.MinValue;

		public IntakeConversationService(IIntakeConversationsRepository conversations, IIntakeConversationMessagesRepository messages, IIntakeChannelsRepository channels,
			ICallsService calls, IDepartmentsService departments, IUserProfileService profiles, IIntakeSmsProvider sms, IIntakeConversationAssistant assistant,
			IProtectedReadService protectedRead, IQueueService queue, IEventAggregator events, Lazy<IEnumerable<IIntakeConversationRouter>> routers, TimeProvider clock)
		{
			_conversations = conversations;
			_messages = messages;
			_channels = channels;
			_calls = calls;
			_departments = departments;
			_profiles = profiles;
			_sms = sms;
			_assistant = assistant;
			_protectedRead = protectedRead;
			_queue = queue;
			_events = events;
			_routers = routers;
			_clock = clock;
		}

		#region Start and inbound

		public async Task<IntakeConversation> StartFromIntakeAsync(IntakeChannel channel, IntakeEnvelope envelope, Call call, CancellationToken cancellationToken = default)
		{
			IntakeConversation conversation = null;
			try
			{
				if (!IntakeConfig.Enabled || !IntakeConfig.ConversationsEnabled || channel == null || envelope == null || call == null || call.CallId <= 0 ||
					envelope.ChannelType != IntakeChannelTypes.Sms || channel.ChannelType != (int)IntakeChannelTypes.Sms)
					return null;

				var participant = Participant(IntakeChannelTypes.Sms, envelope.Sender);
				if (participant == null)
					return null;

				// Without the master number nothing could continue the thread: the caller gets the usual acknowledgement path instead.
				if (string.IsNullOrWhiteSpace(NumberProviderConfig.TwilioResgridNumber))
				{
					Framework.Logging.LogError("Intake conversations need NumberProviderConfig.TwilioResgridNumber (the master number); none was opened.");
					return null;
				}

				var department = await _departments.GetDepartmentByIdAsync(channel.DepartmentId, false);
				if (department == null)
					return null;

				// Template channels and departments without Enhanced AI get no automated turns: a dispatcher answers (D16).
				var aiHandles = channel.ProcessingMode == (int)IntakeProcessingModes.EnhancedAi && await _assistant.CheckAsync(channel.DepartmentId) == null;
				var now = Stamp();
				conversation = new IntakeConversation
				{
					IntakeConversationId = Guid.NewGuid().ToString("D"),
					DepartmentId = channel.DepartmentId,
					IntakeChannelId = channel.IntakeChannelId,
					CallId = call.CallId,
					ChannelType = (int)IntakeChannelTypes.Sms,
					ParticipantAddress = participant,
					ParticipantName = Cap(envelope.SenderName?.Trim(), 200),
					InboundAddress = Cap(channel.Address, 256),
					ReplyFromAddress = Cap(NumberProviderConfig.TwilioResgridNumber, 256),
					State = (int)IntakeConversationStates.Open,
					Handler = (int)(aiHandles ? IntakeConversationHandlers.Assistant : IntakeConversationHandlers.Unassisted),
					StartedOn = now,
					LastInboundOn = now,
					LastActivityOn = now
				};

				if (!await _conversations.TryInsertOpenAsync(conversation, CancellationToken.None))
				{
					// A concurrent text from the same person opened the thread first: this message joins it, and the extra pending call
					// says where the conversation is.
					var existing = await _conversations.GetOpenForParticipantAsync(channel.DepartmentId, (int)IntakeChannelTypes.Sms, participant);
					if (existing == null)
						return null;

					await HandleParticipantMessageAsync(existing, envelope.ProviderMessageId, envelope.Body, envelope.ReceivedOnUtc, cancellationToken);
					if (existing.CallId.HasValue && existing.CallId.Value != call.CallId)
					{
						var threadCall = await _calls.GetCallByIdAsync(existing.CallId.Value, false);
						await NoteAsync(call.CallId, $"This text joined the caller's open text conversation on call {threadCall?.Number ?? existing.CallId.Value.ToString()}.",
							CallNoteSources.System, department.ManagingUserId);
					}

					return existing;
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation could not be opened for call {call?.CallId}.");
				return null;
			}

			// The thread exists from here on: each step fails on its own, and the caller never gets a second (acknowledgement) text.
			var welcomeDepartment = await SafeDepartmentAsync(conversation.DepartmentId);
			IntakeConversationMessage first = null;
			try
			{
				first = NewMessage(conversation, IntakeConversationMessageDirections.Inbound, IntakeConversationAuthors.Participant, null,
					IntakeConversationMessageStatuses.Received, envelope.ProviderMessageId);
				first.CreatedOn = conversation.StartedOn;
				await _messages.TryInsertInboundAsync(first, CancellationToken.None);
				await LinkNoteAsync(first, await NoteAsync(conversation.CallId, IntakeConversationTexts.CallerNotePrefix + Body(envelope.Body),
					CallNoteSources.IntakeText, welcomeDepartment?.ManagingUserId));
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the first message could not be recorded.");
			}

			try
			{
				var unassisted = conversation.Handler != (int)IntakeConversationHandlers.Assistant;
				await SendAsync(conversation, IntakeConversationAuthors.System, null, IntakeConversationTexts.Welcome(welcomeDepartment?.Name, unassisted),
					IntakeConversationTexts.ResgridNotePrefix, welcomeDepartment);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the welcome could not be sent.");
			}

			await PublishAsync(conversation);

			foreach (var router in Routers())
				await CallRouterAsync(router, nameof(IIntakeConversationRouter.RouteNewConversationAsync),
					token => router.RouteNewConversationAsync(conversation, call, token), IntakeConversationRouting.NotRouted);

			if (conversation.Handler == (int)IntakeConversationHandlers.Assistant)
				await QueueTurnAsync(conversation, first?.IntakeConversationMessageId);

			return conversation;
		}

		public async Task<IntakeConversation> FindOpenForReplyAsync(IntakeChannelTypes channelType, string participantAddress, DateTime nowUtc)
		{
			if (!IntakeConfig.Enabled || !IntakeConfig.ConversationsEnabled)
				return null;

			var participant = Participant(channelType, participantAddress);
			if (participant == null)
				return null;

			try
			{
				var since = nowUtc.AddHours(-Math.Max(1, IntakeConfig.ConversationReplyWindowHours));
				return await _conversations.GetOpenForReplyAsync((int)channelType, participant, since);
			}
			catch (Exception ex)
			{
				// The master number's usual handling still works while the thread table cannot be read.
				Framework.Logging.LogException(ex, "Intake conversation reply lookup failed.");
				return null;
			}
		}

		public async Task<IntakeConversation> GetOpenForParticipantAsync(int departmentId, IntakeChannelTypes channelType, string participantAddress)
		{
			var participant = Participant(channelType, participantAddress);
			if (participant == null || departmentId <= 0)
				return null;

			return await _conversations.GetOpenForParticipantAsync(departmentId, (int)channelType, participant);
		}

		public async Task<bool> HandleParticipantMessageAsync(IntakeConversation conversation, string providerMessageId, string text, DateTime receivedOnUtc,
			CancellationToken cancellationToken = default)
		{
			try
			{
				if (conversation == null)
					return false;

				var current = await LoadAsync(conversation.DepartmentId, conversation.IntakeConversationId);
				if (current == null || !current.IsOpen)
					return false;

				var message = NewMessage(current, IntakeConversationMessageDirections.Inbound, IntakeConversationAuthors.Participant, null,
					IntakeConversationMessageStatuses.Received, providerMessageId);
				// A provider retry of the same text: already recorded, nothing more to do.
				if (!await _messages.TryInsertInboundAsync(message, CancellationToken.None))
					return true;

				var department = await SafeDepartmentAsync(current.DepartmentId);
				if (IntakeConversationTexts.IsOptOut(text))
				{
					// STOP: nothing more is ever sent on this thread, and it ends. Twilio itself confirms the opt-out to the texter.
					await LinkNoteAsync(message, await NoteAsync(current.CallId, IntakeConversationTexts.OptedOutNote, CallNoteSources.System, department?.ManagingUserId));
					await CloseCoreAsync(current, "OptedOut", true, message.CreatedOn);
					return true;
				}

				await LinkNoteAsync(message, await NoteAsync(current.CallId, IntakeConversationTexts.CallerNotePrefix + Body(text), CallNoteSources.IntakeText,
					department?.ManagingUserId));
				await _conversations.TouchAsync(current.IntakeConversationId, current.DepartmentId, message.CreatedOn, message.CreatedOn, CancellationToken.None);
				current.LastActivityOn = message.CreatedOn;
				current.LastInboundOn = message.CreatedOn;

				await PublishAsync(current);
				foreach (var router in Routers())
					await CallRouterAsync(router, nameof(IIntakeConversationRouter.OnParticipantMessageAsync),
						async token => { await router.OnParticipantMessageAsync(current, message, token); return true; }, false);

				if (current.Handler == (int)IntakeConversationHandlers.Assistant)
					await QueueTurnAsync(current, message.IntakeConversationMessageId);

				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation?.IntakeConversationId}: a reply from the caller could not be handled.");
				return false;
			}
		}

		#endregion Start and inbound

		#region Reads

		public Task<IntakeConversation> GetByIdAsync(int departmentId, string conversationId) => LoadAsync(departmentId, conversationId);

		public async Task<IntakeConversation> GetForCallAsync(int departmentId, int callId)
		{
			if (departmentId <= 0 || callId <= 0)
				return null;

			return await _conversations.GetLatestForCallAsync(departmentId, callId);
		}

		public Task<IntakeConversationView> GetViewAsync(int departmentId, string conversationId, string viewerUserId) =>
			GetViewAsync(departmentId, conversationId, viewerUserId, null, CancellationToken.None);

		public async Task<IntakeConversationView> GetViewAsync(int departmentId, string conversationId, string viewerUserId, string grantToken,
			CancellationToken cancellationToken = default)
		{
			var conversation = await LoadAsync(departmentId, conversationId);
			if (conversation == null)
				return null;

			var view = new IntakeConversationView { Conversation = conversation };
			var messages = await _messages.GetForConversationAsync(departmentId, conversation.IntakeConversationId) ?? new List<IntakeConversationMessage>();

			// The text is the call's notes, read like any note (attended protected read: plaintext, or REDACTED without a grant).
			var texts = await NoteTextsAsync(conversation, viewerUserId, grantToken, true, cancellationToken);
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var message in messages)
			{
				string authorName = null;
				if (message.Author == (int)IntakeConversationAuthors.Dispatcher && !string.IsNullOrWhiteSpace(message.AuthorUserId))
				{
					if (!names.TryGetValue(message.AuthorUserId, out authorName))
						names[message.AuthorUserId] = authorName = await FirstNameAsync(message.AuthorUserId) ?? "Dispatcher";
				}

				view.Messages.Add(new IntakeConversationEntry
				{
					MessageId = message.IntakeConversationMessageId,
					Direction = message.Direction,
					Author = message.Author,
					AuthorUserId = message.AuthorUserId,
					AuthorName = authorName,
					Text = message.CallNoteId.HasValue && texts.TryGetValue(message.CallNoteId.Value, out var text) ? text : null,
					Status = message.Status,
					CreatedOn = DateTime.SpecifyKind(message.CreatedOn, DateTimeKind.Utc)
				});
			}

			return view;
		}

		public async Task<string> GetParticipantTranscriptAsync(int departmentId, string conversationId, int maxCharacters)
		{
			var conversation = await LoadAsync(departmentId, conversationId);
			if (conversation == null)
				return null;

			var messages = await _messages.GetForConversationAsync(departmentId, conversation.IntakeConversationId) ?? new List<IntakeConversationMessage>();
			var texts = await NoteTextsAsync(conversation, null, null, false, CancellationToken.None);
			var lines = messages.Where(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound && m.CallNoteId.HasValue)
				.Select(m => texts.TryGetValue(m.CallNoteId.Value, out var text) ? text : null)
				.Where(t => !string.IsNullOrWhiteSpace(t))
				.Select(t => t.Trim())
				.ToList();
			if (lines.Count == 0)
				return null;

			// The newest texts that fit, oldest first.
			var max = Math.Max(200, maxCharacters);
			var kept = new List<string>();
			var total = 0;
			for (var i = lines.Count - 1; i >= 0; i--)
			{
				if (total + lines[i].Length + 1 > max)
					break;
				total += lines[i].Length + 1;
				kept.Insert(0, lines[i]);
			}

			if (kept.Count == 0)
				kept.Add(lines[lines.Count - 1].Substring(0, Math.Min(lines[lines.Count - 1].Length, max)));
			return string.Join("\n", kept);
		}

		#endregion Reads

		#region Dispatcher actions

		public async Task<IntakeConversationActionResults> ConnectDispatcherAsync(int departmentId, string conversationId, string userId, bool takeOver = false,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return IntakeConversationActionResults.NotFound;

			var conversation = await LoadAsync(departmentId, conversationId);
			var blocked = Blocked(conversation);
			if (blocked.HasValue)
				return blocked.Value;

			// A repeat connect by the connected dispatcher: no second text, no router call.
			if (conversation.Handler == (int)IntakeConversationHandlers.Dispatcher && SameUser(conversation.AssignedUserId, userId))
				return IntakeConversationActionResults.Done;

			var holder = string.IsNullOrWhiteSpace(conversation.AssignedUserId) ? null : conversation.AssignedUserId;
			if (holder != null && !SameUser(holder, userId) && !takeOver)
				return IntakeConversationActionResults.HeldByAnotherDispatcher;

			var now = Stamp();
			if (!await _conversations.TryConnectAsync(conversation.IntakeConversationId, departmentId, userId, holder, now, CancellationToken.None))
			{
				var again = await LoadAsync(departmentId, conversationId);
				blocked = Blocked(again);
				if (blocked.HasValue)
					return blocked.Value;
				return again.Handler == (int)IntakeConversationHandlers.Dispatcher && SameUser(again.AssignedUserId, userId)
					? IntakeConversationActionResults.Done
					: IntakeConversationActionResults.HeldByAnotherDispatcher;
			}

			conversation.Handler = (int)IntakeConversationHandlers.Dispatcher;
			conversation.AssignedUserId = userId;
			conversation.AssignedOn = now;
			conversation.LastActivityOn = now;

			var department = await SafeDepartmentAsync(departmentId);
			try
			{
				await SendAsync(conversation, IntakeConversationAuthors.System, userId, IntakeConversationTexts.Connected(await FirstNameAsync(userId)),
					IntakeConversationTexts.ResgridNotePrefix, department);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversationId}: the connected text could not be sent.");
			}

			await PublishAsync(conversation);
			foreach (var router in Routers())
				await CallRouterAsync(router, nameof(IIntakeConversationRouter.OnDispatcherConnectedAsync),
					async token => { await router.OnDispatcherConnectedAsync(conversation, userId, token); return true; }, false);

			return IntakeConversationActionResults.Done;
		}

		public async Task<IntakeConversationActionResults> SendDispatcherMessageAsync(int departmentId, string conversationId, string userId, string text,
			CancellationToken cancellationToken = default)
		{
			var body = text?.Trim();
			if (string.IsNullOrEmpty(body) || body.Length > Math.Max(1, IntakeConfig.ConversationMaxOutboundLength) || string.IsNullOrWhiteSpace(userId))
				return IntakeConversationActionResults.InvalidMessage;

			var conversation = await LoadAsync(departmentId, conversationId);
			var blocked = Blocked(conversation);
			if (blocked.HasValue)
				return blocked.Value;

			if (!SameUser(conversation.AssignedUserId, userId))
			{
				// Only the connected dispatcher texts; with nobody connected, sending connects first (the texter is introduced).
				if (!string.IsNullOrWhiteSpace(conversation.AssignedUserId))
					return IntakeConversationActionResults.HeldByAnotherDispatcher;

				var connected = await ConnectDispatcherAsync(departmentId, conversationId, userId, false, cancellationToken);
				if (connected != IntakeConversationActionResults.Done)
					return connected;

				conversation = await LoadAsync(departmentId, conversationId);
				blocked = Blocked(conversation);
				if (blocked.HasValue)
					return blocked.Value;
			}

			var department = await SafeDepartmentAsync(departmentId);
			var message = await SendAsync(conversation, IntakeConversationAuthors.Dispatcher, userId, body,
				IntakeConversationTexts.DispatcherNotePrefix(await FirstNameAsync(userId)), department);

			await PublishAsync(conversation);
			return message.Status == (int)IntakeConversationMessageStatuses.Sent ? IntakeConversationActionResults.Done : IntakeConversationActionResults.SendFailed;
		}

		public async Task<IntakeConversationActionResults> ReleaseDispatcherAsync(int departmentId, string conversationId, string userId,
			CancellationToken cancellationToken = default)
		{
			var conversation = await LoadAsync(departmentId, conversationId);
			if (conversation == null)
				return IntakeConversationActionResults.NotFound;
			if (!conversation.IsOpen)
				return IntakeConversationActionResults.Closed;

			if (!SameUser(conversation.AssignedUserId, userId))
				return string.IsNullOrWhiteSpace(conversation.AssignedUserId) ? IntakeConversationActionResults.Done : IntakeConversationActionResults.HeldByAnotherDispatcher;

			var handler = await ResumeHandlerAsync(conversation);
			if (!await _conversations.TryReleaseAsync(conversation.IntakeConversationId, departmentId, userId, (int)handler, CancellationToken.None))
			{
				var again = await LoadAsync(departmentId, conversationId);
				if (again == null)
					return IntakeConversationActionResults.NotFound;
				if (!again.IsOpen)
					return IntakeConversationActionResults.Closed;
				return SameUser(again.AssignedUserId, userId) || string.IsNullOrWhiteSpace(again.AssignedUserId)
					? IntakeConversationActionResults.Done
					: IntakeConversationActionResults.HeldByAnotherDispatcher;
			}

			conversation.Handler = (int)handler;
			conversation.AssignedUserId = null;
			conversation.AssignedOn = null;

			await PublishAsync(conversation);
			foreach (var router in Routers())
				await CallRouterAsync(router, nameof(IIntakeConversationRouter.OnDispatcherReleasedAsync),
					async token => { await router.OnDispatcherReleasedAsync(conversation, userId, token); return true; }, false);

			// Enhanced AI picks up again from the texter's last unanswered message, if there is one.
			if (handler == IntakeConversationHandlers.Assistant)
				await QueueTurnAsync(conversation, null);

			return IntakeConversationActionResults.Done;
		}

		public async Task<IntakeConversationActionResults> CloseAsync(int departmentId, string conversationId, string userId, string reason, bool notifyParticipant,
			CancellationToken cancellationToken = default)
		{
			var conversation = await LoadAsync(departmentId, conversationId);
			if (conversation == null)
				return IntakeConversationActionResults.NotFound;
			if (!conversation.IsOpen)
				return IntakeConversationActionResults.Closed;

			var why = string.IsNullOrWhiteSpace(reason) ? "Closed by dispatcher" : reason.Trim();
			if (!await CloseCoreAsync(conversation, why, false, Stamp(), notifyParticipant ? userId ?? string.Empty : null))
				return IntakeConversationActionResults.Closed;

			return IntakeConversationActionResults.Done;
		}

		public async Task<int> CloseIdleAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var closed = 0;

			// Calls closed, cancelled or deleted end their conversation; then threads nobody wrote on for the idle period.
			foreach (var conversation in await _conversations.GetOpenWithEndedCallAsync(OpenCallStates, SweepBatch) ?? new List<IntakeConversation>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await SafeCloseAsync(conversation, "CallClosed", nowUtc))
					closed++;
			}

			var idleBefore = nowUtc.AddMinutes(-Math.Max(1, IntakeConfig.ConversationIdleCloseMinutes));
			foreach (var conversation in await _conversations.GetOpenIdleAsync(idleBefore, SweepBatch) ?? new List<IntakeConversation>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await SafeCloseAsync(conversation, "Idle", nowUtc))
					closed++;
			}

			return closed;
		}

		public async Task SetDeskItemAsync(int departmentId, string conversationId, string deskItemId, CancellationToken cancellationToken = default)
		{
			var conversation = await LoadAsync(departmentId, conversationId);
			if (conversation == null)
				return;

			var item = string.IsNullOrWhiteSpace(deskItemId) ? null : Cap(deskItemId.Trim(), 36);
			if (await _conversations.SetDeskItemAsync(conversation.IntakeConversationId, departmentId, item, CancellationToken.None))
			{
				conversation.DeskItemId = item;
				await PublishAsync(conversation);
			}
		}

		#endregion Dispatcher actions

		#region Enhanced AI turns

		public async Task<string> ProcessAssistantTurnAsync(AiDispatchQueueItem item, CancellationToken cancellationToken = default)
		{
			if (item == null || item.DepartmentId <= 0 || string.IsNullOrWhiteSpace(item.IntakeConversationId))
				return "Invalid";

			var conversation = await LoadAsync(item.DepartmentId, item.IntakeConversationId);
			if (conversation == null)
				return "NotFound";
			if (!conversation.IsOpen || conversation.OptedOut)
				return "Closed";
			if (conversation.Handler != (int)IntakeConversationHandlers.Assistant)
				return "NotAssistant";

			var department = await SafeDepartmentAsync(conversation.DepartmentId);
			var max = Math.Max(0, IntakeConfig.ConversationMaxAssistantTurns);
			if (conversation.AssistantTurns >= max)
			{
				await FinishAssistantAsync(conversation, department);
				return "MaxTurns";
			}

			// Only the texter's latest message needs an answer; several queued turns for quick texts collapse into one reply.
			var messages = await _messages.GetForConversationAsync(conversation.DepartmentId, conversation.IntakeConversationId) ?? new List<IntakeConversationMessage>();
			var lastInbound = messages.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound);
			if (lastInbound == null)
				return "NothingToAnswer";
			if (messages.Any(m => m.Direction == (int)IntakeConversationMessageDirections.Outbound && m.CreatedOn > lastInbound.CreatedOn &&
				(m.Author == (int)IntakeConversationAuthors.Assistant || m.Author == (int)IntakeConversationAuthors.Dispatcher)))
				return "AlreadyAnswered";

			var blocked = await _assistant.CheckAsync(conversation.DepartmentId);
			if (blocked != null)
			{
				await FinishAssistantAsync(conversation, department);
				return blocked;
			}

			// The claim makes a redelivered or concurrent item a no-op, and counts toward the turn limit even when the model fails.
			if (!await _conversations.TryClaimAssistantTurnAsync(conversation.IntakeConversationId, conversation.DepartmentId, conversation.AssistantTurns, CancellationToken.None))
				return "Duplicate";

			var turn = conversation.AssistantTurns + 1;
			var texts = await NoteTextsAsync(conversation, null, null, false, cancellationToken);
			var lines = messages.Where(m => m.CallNoteId.HasValue && texts.ContainsKey(m.CallNoteId.Value))
				.Select(m => new IntakeAssistantLine { FromCaller = m.Direction == (int)IntakeConversationMessageDirections.Inbound, Text = texts[m.CallNoteId.Value] })
				.ToList();

			var answer = await _assistant.AskAsync(new IntakeAssistantRequest
			{
				DepartmentId = conversation.DepartmentId,
				DepartmentName = department?.Name,
				Lines = lines,
				Turn = turn,
				MaxTurns = max
			}, cancellationToken) ?? new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.Unavailable };

			// A dispatcher may have connected (or the texter stopped) while the model was thinking: then the AI stays silent.
			var fresh = await LoadAsync(conversation.DepartmentId, conversation.IntakeConversationId);
			if (fresh == null || !fresh.IsOpen || fresh.OptedOut || fresh.Handler != (int)IntakeConversationHandlers.Assistant)
				return "Superseded";

			if (answer.Outcome != IntakeAssistantOutcomes.Answered)
			{
				await FinishAssistantAsync(fresh, department);
				return answer.Outcome;
			}

			if (!string.IsNullOrWhiteSpace(answer.ReplyText))
				await SendAsync(fresh, IntakeConversationAuthors.Assistant, null, Cap(answer.ReplyText.Trim(), Math.Max(1, IntakeConfig.ConversationMaxOutboundLength)),
					IntakeConversationTexts.AssistantNotePrefix, department);

			var complete = answer.Done || answer.WantsDispatcher || turn >= max;
			if (complete)
				await FinishAssistantAsync(fresh, department);
			else
				await PublishAsync(fresh);

			// The call's fields (type, address, contact, name) improve from the whole transcript through the existing enrichment.
			await QueueRefreshAsync(fresh, complete);
			return complete ? "Finished" : IntakeAssistantOutcomes.Answered;
		}

		/// <summary>
		/// Enhanced AI is done with the thread for good (done, turn limit, wants a dispatcher, unavailable): Unassisted, the turn count
		/// exhausted so a later release does not restart it, and the closing text.
		/// </summary>
		private async Task FinishAssistantAsync(IntakeConversation conversation, Department department)
		{
			var max = Math.Max(0, IntakeConfig.ConversationMaxAssistantTurns);
			if (!await _conversations.TrySetHandlerAsync(conversation.IntakeConversationId, conversation.DepartmentId, (int)IntakeConversationHandlers.Assistant,
				(int)IntakeConversationHandlers.Unassisted, true, max, CancellationToken.None))
				return;

			conversation.Handler = (int)IntakeConversationHandlers.Unassisted;
			conversation.AssistantTurns = Math.Max(conversation.AssistantTurns, max);
			try
			{
				await SendAsync(conversation, IntakeConversationAuthors.System, null, IntakeConversationTexts.AssistantFinished(department?.Name),
					IntakeConversationTexts.ResgridNotePrefix, department);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the closing text could not be sent.");
			}

			await PublishAsync(conversation);
		}

		/// <summary>Enhanced AI when the channel uses it, the department may, and it has turns left; else a dispatcher (Unassisted).</summary>
		private async Task<IntakeConversationHandlers> ResumeHandlerAsync(IntakeConversation conversation)
		{
			try
			{
				if (conversation.OptedOut || conversation.AssistantTurns >= Math.Max(0, IntakeConfig.ConversationMaxAssistantTurns))
					return IntakeConversationHandlers.Unassisted;

				var channel = string.IsNullOrWhiteSpace(conversation.IntakeChannelId) ? null : await _channels.GetByIdAsync(conversation.IntakeChannelId);
				if (channel == null || channel.ProcessingMode != (int)IntakeProcessingModes.EnhancedAi)
					return IntakeConversationHandlers.Unassisted;

				return await _assistant.CheckAsync(conversation.DepartmentId) == null ? IntakeConversationHandlers.Assistant : IntakeConversationHandlers.Unassisted;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return IntakeConversationHandlers.Unassisted;
			}
		}

		private async Task QueueTurnAsync(IntakeConversation conversation, string messageId)
		{
			if (!conversation.CallId.HasValue)
				return;

			try
			{
				await _queue.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = conversation.DepartmentId,
					CallId = conversation.CallId.Value,
					Channel = AiDispatchChannels.IntakeConversation,
					QueuedOnUtc = _clock.GetUtcNow().UtcDateTime,
					IntakeConversationId = conversation.IntakeConversationId,
					IntakeConversationMessageId = messageId
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the Enhanced AI turn could not be queued.");
			}
		}

		/// <summary>Re-enrichment of the pending call from the transcript (AI dispatch Enrich, IntakeSms + conversation id).</summary>
		private async Task QueueRefreshAsync(IntakeConversation conversation, bool complete)
		{
			if (!AiDispatchConfig.EnrichEnabled || !conversation.CallId.HasValue)
				return;

			try
			{
				// The deterministic "{channel}: {sender}" name may still be replaced by the model's title, as on the first enrichment.
				var placeholder = false;
				var channel = string.IsNullOrWhiteSpace(conversation.IntakeChannelId) ? null : await _channels.GetByIdAsync(conversation.IntakeChannelId);
				var call = await _calls.GetCallByIdAsync(conversation.CallId.Value, false);
				if (channel != null && call != null)
					placeholder = string.Equals(call.Name, Cap($"{channel.Name}: {conversation.ParticipantAddress}", 200), StringComparison.Ordinal);

				await _queue.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = conversation.DepartmentId,
					CallId = conversation.CallId.Value,
					Channel = AiDispatchChannels.IntakeSms,
					QueuedOnUtc = _clock.GetUtcNow().UtcDateTime,
					NameIsPlaceholder = placeholder,
					IntakeConversationId = conversation.IntakeConversationId,
					ConversationComplete = complete
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the call enrichment could not be queued.");
			}
		}

		#endregion Enhanced AI turns

		#region Helpers

		/// <summary>
		/// Records, notes and texts one outbound message from the master number: the delivery row goes Queued, then Sent (with the
		/// carrier's id) or Failed. Nothing is sent to a texter who opted out.
		/// </summary>
		private async Task<IntakeConversationMessage> SendAsync(IntakeConversation conversation, IntakeConversationAuthors author, string authorUserId, string text,
			string notePrefix, Department department)
		{
			var message = NewMessage(conversation, IntakeConversationMessageDirections.Outbound, author, authorUserId, IntakeConversationMessageStatuses.Queued, null);
			await _messages.InsertMessageAsync(message, CancellationToken.None);
			await LinkNoteAsync(message, await NoteAsync(conversation.CallId, notePrefix + text, CallNoteSources.System, authorUserId ?? department?.ManagingUserId));

			if (conversation.OptedOut)
			{
				message.Status = (int)IntakeConversationMessageStatuses.Failed;
				message.Error = "OptedOut";
			}
			else
			{
				IntakeSmsSendResult result;
				try
				{
					result = await _sms.SendAsync(conversation.ParticipantAddress, text, conversation.DepartmentId, Math.Max(1, IntakeConfig.ConversationMaxOutboundLength),
						CancellationToken.None) ?? new IntakeSmsSendResult { Error = "NoResult" };
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the text could not be sent.");
					result = new IntakeSmsSendResult { Error = ex.GetType().Name };
				}

				message.Status = (int)(result.Success ? IntakeConversationMessageStatuses.Sent : IntakeConversationMessageStatuses.Failed);
				message.ProviderMessageId = Cap(result.ProviderMessageId, 256);
				message.Error = result.Success ? null : Cap(result.Error ?? "Failed", 1000);
				message.SentOn = result.Success ? Stamp() : (DateTime?)null;
			}

			await _messages.UpdateDeliveryAsync(message.IntakeConversationMessageId, message.DepartmentId, message.Status, message.ProviderMessageId, message.Error,
				message.SentOn, CancellationToken.None);
			await _conversations.TouchAsync(conversation.IntakeConversationId, conversation.DepartmentId, message.CreatedOn, null, CancellationToken.None);
			conversation.LastActivityOn = message.CreatedOn > conversation.LastActivityOn ? message.CreatedOn : conversation.LastActivityOn;
			return message;
		}

		/// <summary>
		/// Closes the thread when it is still open (exactly one caller wins), optionally texting the closing message (<paramref name="notifyUserId"/>
		/// non-null), then the event and every router's OnConversationClosedAsync, once.
		/// </summary>
		private async Task<bool> CloseCoreAsync(IntakeConversation conversation, string reason, bool optedOut, DateTime now, string notifyUserId = null)
		{
			var why = Cap(reason, 200);
			if (!await _conversations.TryCloseAsync(conversation.IntakeConversationId, conversation.DepartmentId, why, optedOut, now, CancellationToken.None))
				return false;

			conversation.State = (int)IntakeConversationStates.Closed;
			conversation.ClosedOn = now;
			conversation.CloseReason = why;
			conversation.OptedOut = conversation.OptedOut || optedOut;

			if (notifyUserId != null && !conversation.OptedOut)
			{
				try
				{
					var department = await SafeDepartmentAsync(conversation.DepartmentId);
					await SendAsync(conversation, IntakeConversationAuthors.System, notifyUserId.Length == 0 ? null : notifyUserId,
						IntakeConversationTexts.Closed(department?.Name), IntakeConversationTexts.ResgridNotePrefix, department);
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the closing text could not be sent.");
				}
			}

			await PublishAsync(conversation);
			foreach (var router in Routers())
				await CallRouterAsync(router, nameof(IIntakeConversationRouter.OnConversationClosedAsync),
					async token => { await router.OnConversationClosedAsync(conversation, token); return true; }, false);

			return true;
		}

		private async Task<bool> SafeCloseAsync(IntakeConversation conversation, string reason, DateTime now)
		{
			try
			{
				return await CloseCoreAsync(conversation, reason, false, now);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation?.IntakeConversationId} could not be closed ({reason}).");
				return false;
			}
		}

		private async Task<int?> NoteAsync(int? callId, string text, CallNoteSources source, string userId)
		{
			if (!callId.HasValue || callId.Value <= 0 || string.IsNullOrWhiteSpace(text))
				return null;

			try
			{
				var saved = await _calls.SaveCallNoteAsync(new CallNote
				{
					CallId = callId.Value,
					UserId = userId,
					Note = text,
					Source = (int)source,
					Timestamp = _clock.GetUtcNow().UtcDateTime
				}, CancellationToken.None);

				return saved != null && saved.CallNoteId > 0 ? saved.CallNoteId : (int?)null;
			}
			catch (Exception ex)
			{
				// The text still goes out and the delivery row stays; only its copy on the call is missing.
				Framework.Logging.LogException(ex, $"Intake conversation note on call {callId} failed.");
				return null;
			}
		}

		private async Task LinkNoteAsync(IntakeConversationMessage message, int? callNoteId)
		{
			if (message == null || !callNoteId.HasValue)
				return;

			message.CallNoteId = callNoteId;
			try
			{
				await _messages.SetCallNoteAsync(message.IntakeConversationMessageId, message.DepartmentId, callNoteId.Value, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation message {message.IntakeConversationMessageId} could not be linked to its note.");
			}
		}

		/// <summary>Note id → message text (prefix removed) for the conversation's call. Protected notes are resolved (view) or skipped (AI).</summary>
		private async Task<Dictionary<int, string>> NoteTextsAsync(IntakeConversation conversation, string viewerUserId, string grantToken, bool attendedRead,
			CancellationToken cancellationToken)
		{
			var texts = new Dictionary<int, string>();
			if (!conversation.CallId.HasValue)
				return texts;

			var call = await _calls.GetCallByIdAsync(conversation.CallId.Value, false);
			if (call == null || call.DepartmentId != conversation.DepartmentId)
				return texts;

			call = await _calls.PopulateCallData(call, false, false, true, false, false, false, false, false, false) ?? call;
			var notes = (call.CallNotes ?? new List<CallNote>()).Where(n => n != null).ToList();
			if (attendedRead && notes.Count > 0)
				await _protectedRead.ResolveNotesForReadAsync(conversation.DepartmentId, notes, grantToken, viewerUserId, cancellationToken);

			foreach (var note in notes)
			{
				if (string.IsNullOrWhiteSpace(note.Note) || ProtectedDataEnvelope.IsEnveloped(note.Note) || !attendedRead && note.IsProtected)
					continue;
				texts[note.CallNoteId] = IntakeConversationTexts.StripPrefix(note.Note);
			}

			return texts;
		}

		private async Task PublishAsync(IntakeConversation conversation)
		{
			try
			{
				await _events.SendMessageAsync(new IntakeConversationUpdatedEvent
				{
					DepartmentId = conversation.DepartmentId,
					CallId = conversation.CallId ?? 0,
					Payload = Payload(conversation)
				});
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the realtime update could not be published.");
			}
		}

		/// <summary>The eventing payload: ids and state only, never text or the texter's number.</summary>
		public static string Payload(IntakeConversation conversation) => JsonConvert.SerializeObject(new
		{
			conversationId = conversation.IntakeConversationId,
			state = conversation.State,
			handler = conversation.Handler
		});

		private IReadOnlyList<IIntakeConversationRouter> Routers()
		{
			try
			{
				return (_routers?.Value ?? Enumerable.Empty<IIntakeConversationRouter>()).Where(r => r != null).ToList();
			}
			catch (Exception ex)
			{
				// A broken desk registration never stops intake.
				Framework.Logging.LogException(ex, "Intake conversation routers could not be resolved.");
				return Array.Empty<IIntakeConversationRouter>();
			}
		}

		/// <summary>
		/// One router call in its own try/catch; a router that throws never fails the action. The call is awaited to the end, never
		/// abandoned behind a timeout: an abandoned router task would keep using this request's unit-of-work connection after the request
		/// moved on (the UoW "connection is closed" race). Routers are written to return quickly (they queue work); the token is
		/// CancellationToken.None because what they write must not be cut off by a client disconnect. A slow router is logged.
		/// </summary>
		private static async Task<T> CallRouterAsync<T>(IIntakeConversationRouter router, string what, Func<CancellationToken, Task<T>> call, T fallback)
		{
			var started = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				var task = call(CancellationToken.None);
				return task == null ? fallback : await task;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation router {router.GetType().Name}.{what} failed.");
				return fallback;
			}
			finally
			{
				if (started.Elapsed > RouterSlowWarning)
					Framework.Logging.LogError($"Intake conversation router {router.GetType().Name}.{what} took {started.Elapsed.TotalSeconds:0.#} s; routers must queue work, not do it inline.");
			}
		}

		private async Task<IntakeConversation> LoadAsync(int departmentId, string conversationId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(conversationId))
				return null;

			var conversation = await _conversations.GetByIdAsync(conversationId.Trim());
			return conversation != null && conversation.DepartmentId == departmentId ? conversation : null;
		}

		private static IntakeConversationActionResults? Blocked(IntakeConversation conversation)
		{
			if (conversation == null)
				return IntakeConversationActionResults.NotFound;
			if (!conversation.IsOpen)
				return IntakeConversationActionResults.Closed;
			if (conversation.OptedOut)
				return IntakeConversationActionResults.OptedOut;
			return null;
		}

		private async Task<Department> SafeDepartmentAsync(int departmentId)
		{
			try
			{
				return await _departments.GetDepartmentByIdAsync(departmentId, false);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return null;
			}
		}

		private async Task<string> FirstNameAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId))
				return null;

			try
			{
				return IntakeConversationTexts.SafeFirstName((await _profiles.GetProfileByUserIdAsync(userId))?.FirstName);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex);
				return null;
			}
		}

		private IntakeConversationMessage NewMessage(IntakeConversation conversation, IntakeConversationMessageDirections direction, IntakeConversationAuthors author,
			string authorUserId, IntakeConversationMessageStatuses status, string providerMessageId) => new IntakeConversationMessage
		{
			IntakeConversationMessageId = Guid.NewGuid().ToString("D"),
			IntakeConversationId = conversation.IntakeConversationId,
			DepartmentId = conversation.DepartmentId,
			Direction = (int)direction,
			Author = (int)author,
			AuthorUserId = authorUserId,
			ProviderMessageId = string.IsNullOrWhiteSpace(providerMessageId) ? null : Cap(providerMessageId.Trim(), 256),
			Status = (int)status,
			CreatedOn = Stamp()
		};

		/// <summary>Now, strictly after the previous stamp this instance gave, so one operation's rows keep their order.</summary>
		private DateTime Stamp()
		{
			var now = _clock.GetUtcNow().UtcDateTime;
			if (now <= _lastStamp)
				now = _lastStamp.AddTicks(10);
			_lastStamp = now;
			return now;
		}

		private static string Participant(IntakeChannelTypes channelType, string address)
		{
			if (string.IsNullOrWhiteSpace(address))
				return null;

			var value = channelType == IntakeChannelTypes.Sms ? IntakeChannelAddresses.PhoneNumber(address) ?? address.Trim() : address.Trim();
			return Cap(value, 256);
		}

		private static bool SameUser(string a, string b) =>
			!string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

		private static string Body(string text)
		{
			var body = text?.Trim() ?? string.Empty;
			var max = Math.Max(100, IntakeConfig.MaxMessageLength);
			const string marker = " [message shortened]";
			return body.Length <= max ? body : body.Substring(0, max - marker.Length) + marker;
		}

		private static string Cap(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);

		#endregion Helpers
	}
}
