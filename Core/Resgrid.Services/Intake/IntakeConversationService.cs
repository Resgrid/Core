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

		/// <summary>
		/// How long an Enhanced AI reply row may stay Queued while its turn runs (admission wait, model request, send); a Queued row older than
		/// this belongs to a turn that died and no longer holds the texter's message.
		/// </summary>
		private static readonly TimeSpan AssistantReplyInFlight = TimeSpan.FromMinutes(3);

		/// <summary>The Error prefix of an Enhanced AI reply row that never carried text (see <see cref="WithdrawAsync"/>).</summary>
		public const string WithdrawnPrefix = "Withdrawn:";

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
		private readonly IIntakeBlockedSendersRepository _blocked;
		private readonly TimeProvider _clock;
		private DateTime _lastStamp = DateTime.MinValue;

		public IntakeConversationService(IIntakeConversationsRepository conversations, IIntakeConversationMessagesRepository messages, IIntakeChannelsRepository channels,
			ICallsService calls, IDepartmentsService departments, IUserProfileService profiles, IIntakeSmsProvider sms, IIntakeConversationAssistant assistant,
			IProtectedReadService protectedRead, IQueueService queue, IEventAggregator events, Lazy<IEnumerable<IIntakeConversationRouter>> routers,
			IIntakeBlockedSendersRepository blocked, TimeProvider clock)
		{
			_blocked = blocked;
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
			// True = the text is this thread's and durable: on the call (its note), a provider retry of a text already there, a STOP whose
			// close took, or a blocked sender's text dropped on purpose. Once the texter's words are on the call, a later failure (activity,
			// event, routers, the AI turn) is logged and the text still counts as handled: unhandled would make intake create a second pending
			// call carrying the same text. False = not on this thread (closed meanwhile), or not durable yet (the row, its note or a STOP's close
			// failed): the dedicated-number path keeps the text on a pending call of its own and the master number answers a retryable 503;
			// a redelivery of the same provider id finishes what this one could not (the duplicate branch below).
			var recorded = false;
			try
			{
				if (conversation == null)
					return false;

				var current = await LoadAsync(conversation.DepartmentId, conversation.IntakeConversationId);
				if (current == null || !current.IsOpen)
					return false;

				var optOut = IntakeConversationTexts.IsOptOut(text);

				// M0326: a sender the department blocked after the thread opened is not heard on it either (nothing on the call, no AI turn,
				// nothing sent back). STOP still opts out and closes the thread.
				if (!optOut && await IsBlockedAsync(current))
				{
					Framework.Logging.LogInfo($"Intake conversation {current.IntakeConversationId}: a text from a sender department {current.DepartmentId} blocked was dropped.");
					return true;
				}

				var message = NewMessage(current, IntakeConversationMessageDirections.Inbound, IntakeConversationAuthors.Participant, null,
					IntakeConversationMessageStatuses.Received, providerMessageId);
				if (!await _messages.TryInsertInboundAsync(message, CancellationToken.None))
				{
					// A provider retry of the same text. A STOP whose first delivery was recorded but whose close failed (the thread is still open
					// here) finishes closing it now, so the opt-out never stays half done.
					if (optOut)
						return await CloseOptedOutAsync(current, message.CreatedOn);

					// The first delivery recorded the row but not its note (the text was on no call): this one writes the note on the same call
					// and finishes the steps the first skipped. A row that has its note needs nothing more.
					var stored = (await _messages.GetForConversationAsync(current.DepartmentId, current.IntakeConversationId) ?? new List<IntakeConversationMessage>())
						.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound && !string.IsNullOrWhiteSpace(m.ProviderMessageId) &&
							string.Equals(m.ProviderMessageId, message.ProviderMessageId, StringComparison.OrdinalIgnoreCase));
					if (stored == null || stored.CallNoteId.HasValue)
						return true;
					message = stored;
				}

				var department = await SafeDepartmentAsync(current.DepartmentId);
				if (optOut)
				{
					// STOP: nothing more is ever sent on this thread, and it ends. Twilio itself confirms the opt-out to the texter.
					await LinkNoteAsync(message, await NoteAsync(current.CallId, IntakeConversationTexts.OptedOutNote, CallNoteSources.System, department?.ManagingUserId));
					recorded = true;
					return await CloseOptedOutAsync(current, message.CreatedOn);
				}

				await LinkNoteAsync(message, await NoteAsync(current.CallId, IntakeConversationTexts.CallerNotePrefix + Body(text), CallNoteSources.IntakeText,
					department?.ManagingUserId));
				if (!message.CallNoteId.HasValue)
				{
					// The text is in the delivery log only, on no call: nothing a dispatcher or Enhanced AI reads holds it yet.
					Framework.Logging.LogError($"Intake conversation {current.IntakeConversationId}: a reply from the caller could not be noted on call {current.CallId}.");
					return false;
				}

				recorded = true;
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
				Framework.Logging.LogException(ex, recorded
					? $"Intake conversation {conversation?.IntakeConversationId}: a reply from the caller was noted on the call, but a later step failed."
					: $"Intake conversation {conversation?.IntakeConversationId}: a reply from the caller could not be handled.");
				return recorded;
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
				if (IsAssistantPlaceholder(message))
					continue;

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

			// A repeat connect by the connected dispatcher: no router call, and no second introduction unless the carrier refused the first.
			if (conversation.Handler == (int)IntakeConversationHandlers.Dispatcher && SameUser(conversation.AssignedUserId, userId))
			{
				await EnsureIntroducedAsync(conversation, userId, await SafeDepartmentAsync(departmentId));
				return IntakeConversationActionResults.Done;
			}

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

			var justConnected = false;
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

				// Another dispatcher may have taken the thread over between this connect and the reload: then it is theirs to text.
				if (!SameUser(conversation.AssignedUserId, userId))
					return IntakeConversationActionResults.HeldByAnotherDispatcher;
				justConnected = true;
			}

			var department = await SafeDepartmentAsync(departmentId);

			// The texter hears who they are talking to before the dispatcher's first words (the connect just tried it, so no second try here).
			if (!justConnected)
				await EnsureIntroducedAsync(conversation, userId, department);

			var message = await SendAsync(conversation, IntakeConversationAuthors.Dispatcher, userId, body,
				IntakeConversationTexts.DispatcherNotePrefix(await FirstNameAsync(userId)), department);

			await PublishAsync(conversation);
			return SendOutcome(message);
		}

		/// <summary>The action result for a dispatcher's text: sent, refused at the last moment (take-over, STOP, close), or failed at the carrier.</summary>
		private static IntakeConversationActionResults SendOutcome(IntakeConversationMessage message)
		{
			if (message.Status == (int)IntakeConversationMessageStatuses.Sent)
				return IntakeConversationActionResults.Done;

			switch (message.Error)
			{
				case SendRefusals.NotConnected:
					return IntakeConversationActionResults.HeldByAnotherDispatcher;
				case SendRefusals.OptedOut:
					return IntakeConversationActionResults.OptedOut;
				case SendRefusals.Closed:
					return IntakeConversationActionResults.Closed;
				case SendRefusals.NotFound:
					return IntakeConversationActionResults.NotFound;
				default:
					return IntakeConversationActionResults.SendFailed;
			}
		}

		/// <summary>
		/// The connected dispatcher's introduction ("You are now connected with …") reached the carrier, or is sent again: a repeat connect or
		/// the dispatcher's next text retries an introduction the carrier refused, never one it accepted or may still be sending.
		/// </summary>
		private async Task EnsureIntroducedAsync(IntakeConversation conversation, string userId, Department department)
		{
			try
			{
				var since = conversation.AssignedOn ?? DateTime.MinValue;
				var messages = await _messages.GetForConversationAsync(conversation.DepartmentId, conversation.IntakeConversationId) ?? new List<IntakeConversationMessage>();
				var introduction = messages.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Outbound &&
					m.Author == (int)IntakeConversationAuthors.System && SameUser(m.AuthorUserId, userId) && m.CreatedOn >= since);
				if (introduction != null && introduction.Status != (int)IntakeConversationMessageStatuses.Failed)
					return;

				await SendAsync(conversation, IntakeConversationAuthors.System, userId, IntakeConversationTexts.Connected(await FirstNameAsync(userId)),
					IntakeConversationTexts.ResgridNotePrefix, department);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the connected text could not be sent again.");
			}
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

			// The close itself re-checks the idle cutoff: a text that arrives between this read and the close keeps the thread open.
			var idleBefore = nowUtc.AddMinutes(-Math.Max(1, IntakeConfig.ConversationIdleCloseMinutes));
			foreach (var conversation in await _conversations.GetOpenIdleAsync(idleBefore, SweepBatch) ?? new List<IntakeConversation>())
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (await SafeCloseAsync(conversation, "Idle", nowUtc, idleBefore))
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
			if (Answered(messages, lastInbound))
				return "AlreadyAnswered";

			var blocked = await _assistant.CheckAsync(conversation.DepartmentId);
			if (blocked != null)
			{
				// Earlier turns queued interim refreshes (no summary note); the final one still runs.
				if (await FinishAssistantAsync(conversation, department) && conversation.AssistantTurns > 0)
					await QueueRefreshAsync(conversation, true);
				return blocked;
			}

			// The reply's row is written (Queued, no text) BEFORE the turn is claimed. A second worker for the same text (a redelivery, or one that
			// loaded the thread after this claim) reads its messages after loading the thread, so it always finds this row and stands down
			// (Answered: in flight); and a text that arrives while the model thinks is newer than this row, so it is never taken as answered by it.
			var reply = NewMessage(conversation, IntakeConversationMessageDirections.Outbound, IntakeConversationAuthors.Assistant, null,
				IntakeConversationMessageStatuses.Queued, null);
			await _messages.InsertMessageAsync(reply, CancellationToken.None);

			// The claim makes a concurrent item that loaded the same turn count a no-op, and counts toward the turn limit even when the model fails.
			if (!await _conversations.TryClaimAssistantTurnAsync(conversation.IntakeConversationId, conversation.DepartmentId, conversation.AssistantTurns, CancellationToken.None))
			{
				await WithdrawAsync(reply, "Duplicate");
				return "Duplicate";
			}

			var turn = conversation.AssistantTurns + 1;
			IntakeAssistantAnswer answer;
			try
			{
				// The model reads what the texter actually saw: every message of theirs, and only the texts to them that reached the carrier
				// (Sent). A refused or failed text keeps its note for the dispatcher's history but is no part of the dialogue.
				var texts = await NoteTextsAsync(conversation, null, null, false, cancellationToken);
				var lines = messages.Where(m => m.CallNoteId.HasValue && texts.ContainsKey(m.CallNoteId.Value) &&
						(m.Direction == (int)IntakeConversationMessageDirections.Inbound || m.Status == (int)IntakeConversationMessageStatuses.Sent))
					.Select(m => new IntakeAssistantLine { FromCaller = m.Direction == (int)IntakeConversationMessageDirections.Inbound, Text = texts[m.CallNoteId.Value] })
					.ToList();

				answer = await _assistant.AskAsync(new IntakeAssistantRequest
				{
					DepartmentId = conversation.DepartmentId,
					DepartmentName = department?.Name,
					Lines = lines,
					Turn = turn,
					MaxTurns = max
				}, cancellationToken) ?? new IntakeAssistantAnswer { Outcome = IntakeAssistantOutcomes.Unavailable };
			}
			catch
			{
				// The worker does not retry the item, so nothing would ever answer the texter's message: the placeholder goes (it would read
				// as "being answered" for minutes) and a dispatcher takes the thread, as when a turn cannot be queued.
				await WithdrawAsync(reply, "Failed");
				await HandOverAfterFailedTurnAsync(conversation, department, turn);
				throw;
			}

			// A dispatcher may have connected (or the texter stopped) while the model was thinking: then the AI stays silent.
			var fresh = await LoadAsync(conversation.DepartmentId, conversation.IntakeConversationId);
			if (fresh == null || !fresh.IsOpen || fresh.OptedOut || fresh.Handler != (int)IntakeConversationHandlers.Assistant)
			{
				await WithdrawAsync(reply, SendRefusals.Superseded);
				return "Superseded";
			}

			// The texter wrote again while the model was thinking: this answer never saw that text, so it is not sent. A turn for the newest
			// text runs instead (its own turn may have been the one this claim beat).
			var newest = (await _messages.GetForConversationAsync(fresh.DepartmentId, fresh.IntakeConversationId) ?? new List<IntakeConversationMessage>())
				.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound);
			if (newest != null && newest.IntakeConversationMessageId != lastInbound.IntakeConversationMessageId)
			{
				await WithdrawAsync(reply, SendRefusals.Superseded);
				await QueueTurnAsync(fresh, newest.IntakeConversationMessageId);
				return "Superseded";
			}

			if (answer.Outcome != IntakeAssistantOutcomes.Answered)
			{
				await WithdrawAsync(reply, "NoReply");
				// An earlier turn's refresh was an interim one (no summary note): the final refresh runs now that Enhanced AI stops.
				if (await FinishAssistantAsync(fresh, department) && turn > 1)
					await QueueRefreshAsync(fresh, true);
				return answer.Outcome;
			}

			if (!string.IsNullOrWhiteSpace(answer.ReplyText))
			{
				var delivered = await DeliverAsync(fresh, reply, Cap(answer.ReplyText.Trim(), Math.Max(1, IntakeConfig.ConversationMaxOutboundLength)),
					IntakeConversationTexts.AssistantNotePrefix, department, answersInboundMessageId: lastInbound.IntakeConversationMessageId);

				// The texter wrote again while this reply's note was being written (the check above was passed): the reply is not sent, and
				// the newest text gets a turn of its own, as above.
				if (delivered.Error == SendRefusals.NewerMessage)
				{
					var newer = (await _messages.GetForConversationAsync(fresh.DepartmentId, fresh.IntakeConversationId) ?? new List<IntakeConversationMessage>())
						.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound);
					await QueueTurnAsync(fresh, newer?.IntakeConversationMessageId);
					return "Superseded";
				}
			}
			else
				await WithdrawAsync(reply, "NoReply");

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
		private async Task<bool> FinishAssistantAsync(IntakeConversation conversation, Department department)
		{
			var max = Math.Max(0, IntakeConfig.ConversationMaxAssistantTurns);
			if (!await _conversations.TrySetHandlerAsync(conversation.IntakeConversationId, conversation.DepartmentId, (int)IntakeConversationHandlers.Assistant,
				(int)IntakeConversationHandlers.Unassisted, true, max, CancellationToken.None))
				return false;

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
			return true;
		}

		/// <summary>
		/// A turn that threw before it could answer (reading the transcript, the model call): a dispatcher takes the thread, and when earlier
		/// turns ran (their refreshes were interim, no summary note) the final refresh is queued. Never throws: the caller rethrows its own error.
		/// </summary>
		private async Task HandOverAfterFailedTurnAsync(IntakeConversation conversation, Department department, int turn)
		{
			try
			{
				if (await FinishAssistantAsync(conversation, department) && turn > 1)
					await QueueRefreshAsync(conversation, true);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId} could not be handed to a dispatcher after a failed turn.");
			}
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

		/// <summary>
		/// Queues the next Enhanced AI turn. When the queue refuses it (or fails, acceptance unknown) nothing would ever answer the texter's
		/// message, so a dispatcher takes the thread instead (the closing text says one may reply). That is safe either way: a turn the queue did
		/// take after all finds the thread Unassisted and does nothing.
		/// </summary>
		private async Task QueueTurnAsync(IntakeConversation conversation, string messageId)
		{
			if (!conversation.CallId.HasValue)
				return;

			bool queued;
			try
			{
				queued = await _queue.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
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
				queued = false;
			}

			if (queued)
				return;

			Framework.Logging.LogError($"Intake conversation {conversation.IntakeConversationId}: no Enhanced AI turn was queued; the thread goes to a dispatcher.");
			try
			{
				// Earlier turns queued interim refreshes (no summary note): the final one runs now that Enhanced AI stops, as on every other
				// way a thread leaves Enhanced AI.
				var hadTurns = conversation.AssistantTurns > 0;
				if (await FinishAssistantAsync(conversation, await SafeDepartmentAsync(conversation.DepartmentId)) && hadTurns)
					await QueueRefreshAsync(conversation, true);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId} could not be handed to a dispatcher.");
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
					// As the first enrichment: the worker keeps a dispatcher's rename made after this point (the name no longer matches).
					PlaceholderNameFingerprint = placeholder ? AiDispatch.AiDispatchEnrichmentService.PlaceholderFingerprint(call.DepartmentId, call.CallId, call.Name) : null,
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
		/// carrier's id) or Failed. Nothing is sent to a texter who opted out. <paramref name="closingNotice"/>: the dispatcher's
		/// "conversation ended" text, sent after the close.
		/// </summary>
		private async Task<IntakeConversationMessage> SendAsync(IntakeConversation conversation, IntakeConversationAuthors author, string authorUserId, string text,
			string notePrefix, Department department, bool closingNotice = false)
		{
			var message = NewMessage(conversation, IntakeConversationMessageDirections.Outbound, author, authorUserId, IntakeConversationMessageStatuses.Queued, null);
			await _messages.InsertMessageAsync(message, CancellationToken.None);
			return await DeliverAsync(conversation, message, text, notePrefix, department, closingNotice);
		}

		/// <summary>
		/// Notes and texts an outbound message whose Queued delivery row is already recorded (<see cref="SendAsync"/>, or an Enhanced AI turn's reply).
		/// <paramref name="answersInboundMessageId"/>: the texter's message an Enhanced AI reply answers; a newer one recorded meanwhile stops it at
		/// the carrier boundary (<see cref="SendRefusals.NewerMessage"/>).
		/// </summary>
		private async Task<IntakeConversationMessage> DeliverAsync(IntakeConversation conversation, IntakeConversationMessage message, string text, string notePrefix,
			Department department, bool closingNotice = false, string answersInboundMessageId = null)
		{
			await LinkNoteAsync(message, await NoteAsync(conversation.CallId, notePrefix + text, CallNoteSources.System, message.AuthorUserId ?? department?.ManagingUserId));

			var refusal = await SendRefusalAsync(conversation, message, closingNotice, answersInboundMessageId);
			if (refusal != null)
			{
				message.Status = (int)IntakeConversationMessageStatuses.Failed;
				message.Error = refusal;
			}
			else
			{
				// The cap is for what dispatchers and Enhanced AI write. Resgrid's own fixed texts (welcome, introduction, closing) go whole, so the
				// emergency-number sentence they end with is never cut off by a small ConversationMaxOutboundLength.
				var maxLength = Math.Max(1, IntakeConfig.ConversationMaxOutboundLength);
				if (message.Author == (int)IntakeConversationAuthors.System)
					maxLength = Math.Max(maxLength, text?.Length ?? 0);

				IntakeSmsSendResult result;
				try
				{
					result = await _sms.SendAsync(conversation.ParticipantAddress, text, conversation.DepartmentId, maxLength,
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

		/// <summary>Why an outbound text is not handed to the carrier after all (the delivery row's Error).</summary>
		private static class SendRefusals
		{
			public const string OptedOut = "OptedOut";
			public const string Closed = "Closed";
			public const string NotFound = "NotFound";
			public const string NotConnected = "NotConnected";
			public const string Superseded = "Superseded";

			/// <summary>An Enhanced AI reply whose texter wrote again after the turn read the thread: it never saw that text.</summary>
			public const string NewerMessage = "NewerMessage";

			/// <summary>The thread could not be read again before the send: an opt-out may have committed, so nothing goes out (a dispatcher retries).</summary>
			public const string Unconfirmed = "Unconfirmed";
		}

		/// <summary>
		/// The thread is read again immediately before the carrier is asked, after the row and the note were written: a STOP, a close, a
		/// take-over or a dispatcher connecting that committed meanwhile wins over the message in hand (the snapshot the caller loaded can be
		/// seconds old). Null = send. A closing notice goes out on the thread it just closed, unless the texter opted out. A thread that cannot
		/// be read is not permission to send: the text fails as <see cref="SendRefusals.Unconfirmed"/> (a dispatcher's send reports SendFailed
		/// and can be retried, and the retry reads the thread again).
		/// </summary>
		private async Task<string> SendRefusalAsync(IntakeConversation conversation, IntakeConversationMessage message, bool closingNotice, string answersInboundMessageId)
		{
			if (conversation.OptedOut)
				return SendRefusals.OptedOut;

			IntakeConversation stored;
			try
			{
				stored = await _conversations.GetByIdAsync(conversation.IntakeConversationId);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId} could not be read again before a send; nothing was sent.");
				return SendRefusals.Unconfirmed;
			}

			if (stored == null || stored.DepartmentId != conversation.DepartmentId)
				return SendRefusals.NotFound;
			if (stored.OptedOut)
			{
				conversation.OptedOut = true;
				return SendRefusals.OptedOut;
			}
			if (closingNotice)
				return null;
			if (!stored.IsOpen)
				return SendRefusals.Closed;

			// Enhanced AI speaks only while it holds the thread; a dispatcher (and their introduction) only while connected.
			if (message.Author == (int)IntakeConversationAuthors.Assistant && stored.Handler != (int)IntakeConversationHandlers.Assistant)
				return SendRefusals.Superseded;
			if ((message.Author == (int)IntakeConversationAuthors.Dispatcher || message.Author == (int)IntakeConversationAuthors.System && !string.IsNullOrWhiteSpace(message.AuthorUserId)) &&
				!SameUser(stored.AssignedUserId, message.AuthorUserId))
				return SendRefusals.NotConnected;

			// An Enhanced AI reply answers the texter's latest message only while it still is the latest.
			if (!string.IsNullOrWhiteSpace(answersInboundMessageId))
			{
				try
				{
					var newest = (await _messages.GetForConversationAsync(conversation.DepartmentId, conversation.IntakeConversationId) ?? new List<IntakeConversationMessage>())
						.LastOrDefault(m => m.Direction == (int)IntakeConversationMessageDirections.Inbound);
					if (newest != null && !string.Equals(newest.IntakeConversationMessageId, answersInboundMessageId, StringComparison.Ordinal))
						return SendRefusals.NewerMessage;
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: its messages could not be read again before a send; nothing was sent.");
					return SendRefusals.Unconfirmed;
				}
			}

			return null;
		}

		/// <summary>
		/// An Enhanced AI reply row that will carry no text (duplicate claim, superseded, no question): Failed with a "Withdrawn:" reason. It never
		/// counts as an answer and the conversation view leaves it out.
		/// </summary>
		private async Task WithdrawAsync(IntakeConversationMessage message, string why)
		{
			message.Status = (int)IntakeConversationMessageStatuses.Failed;
			message.Error = WithdrawnPrefix + why;
			try
			{
				await _messages.UpdateDeliveryAsync(message.IntakeConversationMessageId, message.DepartmentId, message.Status, null, message.Error, null, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation message {message.IntakeConversationMessageId} could not be withdrawn.");
			}
		}

		/// <summary>An Enhanced AI reply row still being prepared (Queued, no text yet) or withdrawn: not part of what anyone reads.</summary>
		private static bool IsAssistantPlaceholder(IntakeConversationMessage message) =>
			message.Direction == (int)IntakeConversationMessageDirections.Outbound && message.Author == (int)IntakeConversationAuthors.Assistant &&
			!message.CallNoteId.HasValue && (message.Status == (int)IntakeConversationMessageStatuses.Queued ||
				message.Error != null && message.Error.StartsWith(WithdrawnPrefix, StringComparison.Ordinal));

		/// <summary>
		/// The texter's message was answered: a reply from Enhanced AI or a dispatcher after it reached the carrier (Sent; a failed text answered
		/// nothing), or Enhanced AI is answering it right now (its reply row, written before its turn was claimed, is younger than
		/// <see cref="AssistantReplyInFlight"/>; an older Queued row belongs to a turn that died).
		/// </summary>
		private bool Answered(IEnumerable<IntakeConversationMessage> messages, IntakeConversationMessage inbound)
		{
			var now = _clock.GetUtcNow().UtcDateTime;
			return messages.Any(m => m.Direction == (int)IntakeConversationMessageDirections.Outbound && m.CreatedOn > inbound.CreatedOn &&
				(m.Author == (int)IntakeConversationAuthors.Assistant || m.Author == (int)IntakeConversationAuthors.Dispatcher) &&
				(m.Status == (int)IntakeConversationMessageStatuses.Sent ||
				 m.Author == (int)IntakeConversationAuthors.Assistant && m.Status == (int)IntakeConversationMessageStatuses.Queued && now - m.CreatedOn < AssistantReplyInFlight));
		}

		/// <summary>
		/// Closes the thread when it is still open (exactly one caller wins), optionally texting the closing message (<paramref name="notifyUserId"/>
		/// non-null), then the event and every router's OnConversationClosedAsync, once.
		/// </summary>
		private async Task<bool> CloseCoreAsync(IntakeConversation conversation, string reason, bool optedOut, DateTime now, string notifyUserId = null,
			DateTime? idleBefore = null)
		{
			var why = Cap(reason, 200);
			var closed = idleBefore.HasValue
				? await _conversations.TryCloseIdleAsync(conversation.IntakeConversationId, conversation.DepartmentId, why, idleBefore.Value, now, CancellationToken.None)
				: await _conversations.TryCloseAsync(conversation.IntakeConversationId, conversation.DepartmentId, why, optedOut, now, CancellationToken.None);
			if (!closed)
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
						IntakeConversationTexts.Closed(department?.Name), IntakeConversationTexts.ResgridNotePrefix, department, closingNotice: true);
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

		/// <summary>
		/// A STOP's close, tried twice: true once the thread is closed (by this call or one that won first), false when the close itself
		/// keeps failing, so the caller answers the provider with a retryable failure and its redelivery of the STOP closes the thread.
		/// </summary>
		private async Task<bool> CloseOptedOutAsync(IntakeConversation conversation, DateTime now)
		{
			try
			{
				await CloseCoreAsync(conversation, "OptedOut", true, now);
				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the opt-out close failed; trying again.");
			}

			try
			{
				await CloseCoreAsync(conversation, "OptedOut", true, now);
				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the opt-out close failed again; the provider's redelivery finishes it.");
				return false;
			}
		}

		/// <summary>The texter is on the department's blocked list (M0326). A list that cannot be read lets the text through, as intake does.</summary>
		private async Task<bool> IsBlockedAsync(IntakeConversation conversation)
		{
			var key = IntakeBlockedSenders.Normalize(conversation.ParticipantAddress);
			if (key == null)
				return false;

			try
			{
				return await _blocked.GetInForceAsync(conversation.DepartmentId, key, _clock.GetUtcNow().UtcDateTime) != null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake conversation {conversation.IntakeConversationId}: the blocked-sender check failed; the text is let through.");
				return false;
			}
		}

		private async Task<bool> SafeCloseAsync(IntakeConversation conversation, string reason, DateTime now, DateTime? idleBefore = null)
		{
			try
			{
				return await CloseCoreAsync(conversation, reason, false, now, null, idleBefore);
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
