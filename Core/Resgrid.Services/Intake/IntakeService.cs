using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Intake (call-intake-and-ai-destinations-plan.md D5-D7): a message on a department's intake channel becomes a PENDING call.
	/// Order: resolve the channel → claim the provider message id (IntakeMessages, unique per channel) → checks (Intake.Channels
	/// flag, channel enabled, sender allowed) → the call, built deterministically (the channel's template, or the raw text when the
	/// template finds nothing or the channel is EnhancedAi) → save → CallUpdatedEvent (as the v4 pending path: a pending call is not
	/// "added" until dispatched) → Enhanced AI enrichment queued for EnhancedAi channels and voice. The call has no recipients: the
	/// dispatcher chooses who to send it to. Nothing is broadcast. Every outcome is written to the message row.
	/// </summary>
	public class IntakeService : IIntakeService
	{
		/// <summary>Nothing travels further than this in Sender (column width).</summary>
		private const int MaxSenderLength = 256;
		private const int MaxNameLength = 200;
		private const int MaxReasonLength = 1000;

		/// <summary>A claim that stayed Received this long was abandoned by a crashed request and may be taken again.</summary>
		private static readonly TimeSpan AbandonedClaim = TimeSpan.FromMinutes(10);

		// A blocked sender's messages are refused before anything is built, so they never count toward the monthly volume (M0326).
		private static readonly int[] NotCounted = { (int)IntakeMessageStates.Duplicate, (int)IntakeMessageStates.SenderBlocked };

		private static readonly int[] IntakeCallSources = { (int)CallSources.Intake, (int)CallSources.VoiceIntake };

		/// <summary>Call ids per IntakeMessages read when looking up many calls' sources at once.</summary>
		public const int CallLookupBatchSize = 500;

		private readonly IIntakeChannelsRepository _channels;
		private readonly IIntakeMessagesRepository _messages;
		private readonly IVoiceIntakeSessionsRepository _sessions;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IFeatureToggleService _flags;
		private readonly ICallsService _calls;
		private readonly INumbersService _numbers;
		private readonly IGeoLocationProvider _geo;
		private readonly IEventAggregator _events;
		private readonly IQueueService _queue;
		private readonly ICommunicationService _communication;
		private readonly ICacheProvider _cache;
		private readonly IIntakeConversationService _conversations;
		private readonly IIntakeConversationOperations _conversationOperations;
		private readonly IIntakeBlockedSendersRepository _blocked;
		private readonly TimeProvider _clock;
		// Lazy: the desk's routing reaches back into intake (its conversations and screenings) through its own lazies.
		private readonly Lazy<IDispatcherRoutingService> _dispatcherRouting;
		private readonly Lazy<IEmailCallUpdateService> _emailCallUpdates;

		public IntakeService(IIntakeChannelsRepository channels, IIntakeMessagesRepository messages, IVoiceIntakeSessionsRepository sessions,
			IDepartmentsService departments, IDepartmentSettingsService departmentSettings, IFeatureToggleService flags, ICallsService calls,
			INumbersService numbers, IGeoLocationProvider geo, IEventAggregator events, IQueueService queue, ICommunicationService communication,
			ICacheProvider cache, IIntakeConversationService conversations, IIntakeConversationOperations conversationOperations,
			IIntakeBlockedSendersRepository blocked, TimeProvider clock, Lazy<IDispatcherRoutingService> dispatcherRouting,
			Lazy<IEmailCallUpdateService> emailCallUpdates = null)
		{
			_emailCallUpdates = emailCallUpdates;
			_blocked = blocked;
			_dispatcherRouting = dispatcherRouting;
			_channels = channels;
			_messages = messages;
			_sessions = sessions;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_flags = flags;
			_calls = calls;
			_numbers = numbers;
			_geo = geo;
			_events = events;
			_queue = queue;
			_communication = communication;
			_cache = cache;
			_conversations = conversations;
			_conversationOperations = conversationOperations;
			_clock = clock;
		}

		public async Task<IntakeResult> ReceiveAsync(IntakeEnvelope envelope, CancellationToken cancellationToken = default)
		{
			if (envelope == null)
				return new IntakeResult { State = IntakeMessageStates.Failed, Reason = "NoMessage" };
			if (!IntakeConfig.Enabled)
				return new IntakeResult { State = IntakeMessageStates.Refused, Reason = "IntakeDisabled" };

			var channel = await ResolveAsync(envelope);
			if (channel == null)
				return new IntakeResult { State = IntakeMessageStates.Refused, Reason = "NoChannel" };

			var result = new IntakeResult { DepartmentId = channel.DepartmentId, IntakeChannelId = channel.IntakeChannelId };
			var department = await _departments.GetDepartmentByIdAsync(channel.DepartmentId, false);
			if (department == null)
			{
				result.State = IntakeMessageStates.Refused;
				result.Reason = "NoDepartment";
				return result;
			}

			var now = _clock.GetUtcNow().UtcDateTime;
			var (message, recoveredCallId) = await ClaimAsync(channel, envelope, now, cancellationToken);
			if (recoveredCallId.HasValue)
			{
				// A claim abandoned after its call was saved (the process died before the call id was recorded): the call is this message's.
				result.State = IntakeMessageStates.Duplicate;
				result.CallId = recoveredCallId;
				result.Reason = IntakeResultReasons.Duplicate;
				return result;
			}

			if (message == null)
			{
				var existing = await _messages.GetByProviderMessageIdAsync(channel.IntakeChannelId, Cap(envelope.ProviderMessageId?.Trim(), 256));
				result.State = IntakeMessageStates.Duplicate;
				result.CallId = existing?.CallId;
				// Another delivery still owns the claim (or is retrying a failed one) and has not saved a call yet: the transport should
				// answer so the provider delivers again later (Postmark 409), not acknowledge a message that may still fail.
				result.Reason = existing != null && !existing.CallId.HasValue &&
					(existing.State == (int)IntakeMessageStates.Received || existing.State == (int)IntakeMessageStates.Failed)
					? IntakeResultReasons.DuplicateInProgress
					: IntakeResultReasons.Duplicate;
				return result;
			}

			int? savedCallId = null;
			Call pendingCall = null;
			// The call the message was added to as a note (its open conversation, or the sender's open call past the hourly limit). Once
			// the note is saved, a later failure records that outcome instead of Failed: a retry must not add the same text again.
			int? notedCallId = null;
			var notedState = IntakeMessageStates.AddedToCall;
			string notedReason = null;
			try
			{
				string refusal = null;
				var state = IntakeMessageStates.Refused;
				if (!string.IsNullOrWhiteSpace(envelope.RefusalReason))
				{
					// The transport refused it (a lapsed number). A sender the department blocked is still recorded as blocked (and not
					// counted toward the month); a block list that cannot be read keeps the transport's reason.
					if (await IsBlockedAsync(channel.DepartmentId, envelope.Sender, now))
					{
						refusal = "SenderBlocked";
						state = IntakeMessageStates.SenderBlocked;
					}
					else
						refusal = Cap(envelope.RefusalReason.Trim(), 100);
				}
				else if (!await _flags.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, channel.DepartmentId))
					refusal = "FeatureOff";
				else if (!channel.IsEnabled)
					refusal = "ChannelDisabled";
				else if (string.IsNullOrWhiteSpace(envelope.Body) && string.IsNullOrWhiteSpace(envelope.Subject))
					refusal = "EmptyMessage";
				else if (await IsBlockedAsync(channel.DepartmentId, envelope.Sender, now))
				{
					// M0326: a sender a dispatcher blocked (dismiss as spam). Recorded, never answered, not counted.
					refusal = "SenderBlocked";
					state = IntakeMessageStates.SenderBlocked;
				}
				else if (!IsSenderAllowed(channel, envelope.Sender))
				{
					refusal = "SenderNotAllowed";
					state = IntakeMessageStates.SenderNotAllowed;
				}

				if (refusal != null)
				{
					await _messages.CompleteAsync(message.IntakeMessageId, (int)state, null, refusal, now, CancellationToken.None);
					result.State = state;
					result.Reason = refusal;
					return result;
				}

				// Plan §8 D15: a text from someone who already has an open conversation with the department (dedicated number) joins
				// that conversation as a note on its call; no second pending call. A conversation whose call ended does not count.
				var conversational = envelope.OpensConversation && envelope.ChannelType == IntakeChannelTypes.Sms && IntakeConfig.ConversationsEnabled;
				if (conversational)
				{
					var joined = await JoinOpenConversationAsync(channel, envelope, cancellationToken);
					// A request cancelled while the text was joining its thread ends here (the claim is released for the provider's
					// retry, which finds the text on the thread if it got there), never with a pending call built for a cancelled request.
					if (joined == null)
						cancellationToken.ThrowIfCancellationRequested();
					if (joined != null)
					{
						(notedCallId, notedState, notedReason) = (joined.CallId, IntakeMessageStates.AddedToConversation, "Conversation");
						await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.AddedToConversation, joined.CallId, "Conversation", now, CancellationToken.None);
						result.State = IntakeMessageStates.AddedToConversation;
						result.CallId = joined.CallId;
						result.Reason = "Conversation";
						result.IntakeConversationId = joined.IntakeConversationId;
						await CheckSmsFairUseAsync(department, now);
						return result;
					}
				}

				// D8 (cad-email-call-updates-plan.md): an email channel that accepts CAD updates hands an email carrying the CAD call ID of
				// a call the department already has to the CAD update engine, which updates, closes or notes that call; otherwise the
				// message makes its pending call below, which then carries the ID.
				var cad = await MatchCadUpdateAsync(channel, department, envelope, cancellationToken);
				if (cad.Result != null)
				{
					if (cad.Result.Outcome == CallEmailUpdateOutcomes.Busy)
					{
						// Another email for the same CAD call is still being handled: recorded as failed so the provider's retry takes the claim over.
						await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.Failed, null, "CadUpdateBusy", now, CancellationToken.None);
						result.State = IntakeMessageStates.Failed;
						result.Reason = "CadUpdateBusy";
						return result;
					}

					if (!cad.Result.CreatedNewCall)
					{
						var touchedCallId = cad.Result.Call?.CallId > 0 ? cad.Result.Call.CallId : (int?)null;
						await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.UpdatedCall, touchedCallId, cad.Result.Outcome.ToString(), now, CancellationToken.None);
						result.State = IntakeMessageStates.UpdatedCall;
						result.CallId = touchedCallId;
						result.Reason = cad.Result.Outcome.ToString();
						return result;
					}
				}

				// M0326 per-sender limit: past IntakeConfig.MaxCallsPerSenderPerHour, the message joins the sender's newest open call as a note.
				var limit = await CheckSenderLimitAsync(channel, department, envelope, now, cancellationToken);
				if (limit.JoinedCallId.HasValue)
				{
					(notedCallId, notedState, notedReason) = (limit.JoinedCallId, IntakeMessageStates.AddedToCall, "SenderLimit");
					await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.AddedToCall, limit.JoinedCallId, "SenderLimit", now, CancellationToken.None);
					result.State = IntakeMessageStates.AddedToCall;
					result.CallId = limit.JoinedCallId;
					result.Reason = "SenderLimit";
					if (envelope.ChannelType == IntakeChannelTypes.Sms)
						await CheckSmsFairUseAsync(department, now);
					return result;
				}

				var built = await BuildCallAsync(channel, department, envelope, now);
				if (cad.CadCallId != null)
				{
					// The CAD's later emails find this call by the ID it carries.
					if (string.IsNullOrWhiteSpace(built.Call.ExternalIdentifier))
						built.Call.ExternalIdentifier = cad.CadCallId;
					if (string.IsNullOrWhiteSpace(built.Call.IncidentNumber))
						built.Call.IncidentNumber = cad.CadCallId;
				}

				pendingCall = built.Call;
				var saved = await _calls.SaveCallAsync(built.Call, cancellationToken);
				savedCallId = saved.CallId > 0 ? saved.CallId : (int?)null;

				if (cad.CadCallId != null && savedCallId.HasValue)
					await _emailCallUpdates.Value.RecordCreatedAsync(cad.Request, saved, cad.CadCallId);

				// The id is on the claim before anything else runs, so a retry after a later failure finds the call instead of making another
				// (a process that dies between the two is covered by ClaimAsync's lookup of the call by SourceIdentifier).
				await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.CallCreated, saved.CallId, built.Reason, now, CancellationToken.None);
				result.State = IntakeMessageStates.CallCreated;
				result.CallId = saved.CallId;
				result.Reason = built.Reason;

				// Over the limit with none of the sender's calls still open: the call stands, labelled so the desk does not offer it (before
				// the event below, so the boards and the desk read the label with the call).
				if (limit.OverLimit)
					await SafeScreenAsync(channel.DepartmentId, saved.CallId, IntakeScreenings.RepeatSender, now);

				// A chat app's call reaches the dispatcher desk as a chat (the desk's Chats option), put there before the event below so the
				// event's pending-call hand-off finds it on the desk instead of racing it.
				if (IsChatChannel(envelope.ChannelType))
					await PutChatOnDeskAsync(envelope, saved);

				// As the v4 pending path: dispatcher screens refresh; "call added" (chat channel, workflows, field apps) waits for dispatch.
				_events.SendMessage(new CallUpdatedEvent { DepartmentId = channel.DepartmentId, Call = saved });

				// The conversation's welcome (from the master number) replaces the acknowledgement: one text, never two. Started before the
				// enrichment is queued so the first Enhanced AI question is not stuck behind it on the queue. A repeat sender gets neither:
				// nothing goes back to a number that is flooding the line, and its next texts join this call as notes.
				IntakeConversation conversation = null;
				if (conversational && !limit.OverLimit)
					conversation = await _conversations.StartFromIntakeAsync(channel, envelope, saved, cancellationToken);
				result.IntakeConversationId = conversation?.IntakeConversationId;

				if (channel.ProcessingMode == (int)IntakeProcessingModes.EnhancedAi || envelope.ChannelType == IntakeChannelTypes.Voice)
					await QueueEnrichmentAsync(saved, envelope.ChannelType, built.NameIsPlaceholder, cancellationToken);

				if (envelope.ChannelType == IntakeChannelTypes.Sms)
					await CheckSmsFairUseAsync(department, now);

				if (channel.SendAcknowledgement && conversation == null && !limit.OverLimit)
					result.Acknowledgement = Acknowledgement(department, saved);

				return result;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				savedCallId ??= pendingCall?.CallId > 0 ? pendingCall.CallId : (int?)null;
				// A call already saved (or a note already added) keeps its claim, so the provider's retry is a duplicate rather than a
				// second call or a second copy of the note.
				if (!savedCallId.HasValue && notedCallId.HasValue)
					await SafeCompleteAsync(message.IntakeMessageId, notedState, notedCallId, notedReason, now);
				else
					await SafeCompleteAsync(message.IntakeMessageId, savedCallId.HasValue ? IntakeMessageStates.CallCreated : IntakeMessageStates.Failed, savedCallId, "Cancelled", now);
				throw;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake message {message.IntakeMessageId} on channel {channel.IntakeChannelId} failed.");
				// The call row commits before its child rows: a failure after it leaves the call, which the claim must keep.
				savedCallId ??= pendingCall?.CallId > 0 ? pendingCall.CallId : (int?)null;
				if (savedCallId.HasValue)
				{
					await SafeCompleteAsync(message.IntakeMessageId, IntakeMessageStates.CallCreated, savedCallId, "AfterSaveFailed", now);
					result.State = IntakeMessageStates.CallCreated;
					result.CallId = savedCallId;
					result.Reason = "AfterSaveFailed";
					return result;
				}

				// The text is already a note on its call (conversation or sender limit): the claim records that, so the provider's
				// retry is a duplicate and the note is never added twice.
				if (notedCallId.HasValue)
				{
					await SafeCompleteAsync(message.IntakeMessageId, notedState, notedCallId, notedReason, now);
					result.State = notedState;
					result.CallId = notedCallId;
					result.Reason = notedReason;
					return result;
				}

				await SafeCompleteAsync(message.IntakeMessageId, IntakeMessageStates.Failed, null, Cap(ex.GetType().Name + ": " + ex.Message, MaxReasonLength), now);
				result.State = IntakeMessageStates.Failed;
				result.Reason = "Failed";
				return result;
			}
		}

		/// <summary>What the CAD update engine made of an intake email: its result when it matched a call, else the ID the new call carries.</summary>
		private sealed class CadMatch
		{
			public EmailCallUpdateResult Result { get; set; }
			public string CadCallId { get; set; }
			public EmailCallUpdateRequest Request { get; set; }
		}

		/// <summary>
		/// D8: an opted-in email channel's message read with the channel's template (Generic for an AI channel), matched by the
		/// department's CAD update settings. No call is created here: when nothing matches, the caller's normal intake path makes the
		/// pending call (outside the engine's lock: two first emails for one incident arriving together can make two pending calls,
		/// which the desk shows side by side).
		/// </summary>
		private async Task<CadMatch> MatchCadUpdateAsync(IntakeChannel channel, Department department, IntakeEnvelope envelope, CancellationToken cancellationToken)
		{
			var none = new CadMatch();
			if (_emailCallUpdates == null || channel == null || !channel.CadUpdates || envelope.ChannelType != IntakeChannelTypes.Email)
				return none;

			var format = channel.ProcessingMode == (int)IntakeProcessingModes.Template ? channel.TemplateFormat ?? (int)CallEmailTypes.Generic : (int)CallEmailTypes.Generic;
			var config = await _departmentSettings.GetEmailCallUpdateConfigAsync(channel.DepartmentId, format);
			if (config == null || !config.Enabled)
				return none;

			var body = MessageText(envelope.Body);
			var email = new CallEmail
			{
				MessageId = envelope.ProviderMessageId,
				From = envelope.Sender,
				Subject = string.IsNullOrWhiteSpace(envelope.Subject) ? null : envelope.Subject.Trim(),
				Body = body,
				TextBody = body,
				SentOn = envelope.SentOnUtc
			};

			var priorities = await _calls.GetActiveCallPrioritiesForDepartmentAsync(channel.DepartmentId) ?? new List<DepartmentCallPriority>();
			var callTypes = await _calls.GetCallTypesForDepartmentAsync(channel.DepartmentId) ?? new List<CallType>();
			var priority = channel.DefaultPriority ?? CallPriorityMatch.DefaultForImport(priorities);

			// No members or units go in, as for every intake template read: intake never dispatches anyone itself.
			var parsed = await _emailCallUpdates.Value.ParseAsync(format, email, department.ManagingUserId, new List<IdentityUser>(), department,
				new List<Unit>(), priority, priorities, callTypes, cancellationToken);
			if (parsed?.Call == null)
				parsed = new CallEmailParseResult { Call = new Call { Name = email.Subject, NatureOfCall = body, Priority = priority } };

			var request = new EmailCallUpdateRequest
			{
				DepartmentId = channel.DepartmentId,
				Department = department,
				FormatType = format,
				Email = email,
				Parsed = parsed,
				Source = CallEmailUpdateSources.IntakeChannel,
				IntakeChannelId = channel.IntakeChannelId,
				Config = config
			};

			// The engine asks for a new call only when nothing matched; that call is made by the normal intake path afterwards.
			var result = await _emailCallUpdates.Value.ProcessAsync(request, unsaved => Task.FromResult<Call>(null), cancellationToken);

			return new CadMatch
			{
				Result = result,
				CadCallId = result.Outcome == CallEmailUpdateOutcomes.Created ? result.CadCallId : null,
				Request = request
			};
		}

		public async Task<int> GetMonthlyCountAsync(int departmentId, IntakeChannelTypes channelType, DateTime nowUtc)
		{
			var (from, before) = Month(nowUtc);
			if (channelType == IntakeChannelTypes.Voice)
				return await _sessions.CountStartedAsync(departmentId, from, before);

			return await _messages.CountAsync(departmentId, (int)channelType, from, before, NotCounted);
		}

		public async Task<Dictionary<IntakeChannelTypes, int>> GetMonthlyCountsAsync(int departmentId, DateTime nowUtc)
		{
			var (from, before) = Month(nowUtc);
			var counts = (await _messages.CountByTypeAsync(departmentId, from, before, NotCounted) ?? new Dictionary<int, int>())
				.Where(c => Enum.IsDefined(typeof(IntakeChannelTypes), c.Key) && c.Key != (int)IntakeChannelTypes.Voice)
				.ToDictionary(c => (IntakeChannelTypes)c.Key, c => c.Value);

			var voice = await _sessions.CountStartedAsync(departmentId, from, before);
			if (voice > 0)
				counts[IntakeChannelTypes.Voice] = voice;

			return counts;
		}

		public async Task<Dictionary<string, int>> GetMonthlyChannelCountsAsync(int departmentId, DateTime nowUtc)
		{
			var (from, before) = Month(nowUtc);
			return await _messages.CountByChannelAsync(departmentId, from, before, NotCounted) ?? new Dictionary<string, int>();
		}

		public async Task<Dictionary<int, IntakeCallSource>> GetCallSourcesAsync(int departmentId, IEnumerable<int> callIds)
		{
			var sources = new Dictionary<int, IntakeCallSource>();
			var ids = (callIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToList();
			if (ids.Count == 0)
				return sources;

			// Read in batches (the IN list stays well under SQL Server's parameter limit) so every call in a long list gets its source.
			var messages = new List<IntakeMessage>();
			foreach (var batch in ids.Chunk(CallLookupBatchSize))
				messages.AddRange(await _messages.GetByCallIdsAsync(departmentId, batch) ?? new List<IntakeMessage>());
			if (messages.Count == 0)
				return sources;

			// Deleted channels keep their name on the calls they created.
			var channels = (await _channels.GetAllByDepartmentIdAsync(departmentId) ?? Enumerable.Empty<IntakeChannel>())
				.Where(c => c.DepartmentId == departmentId)
				.ToDictionary(c => c.IntakeChannelId, StringComparer.OrdinalIgnoreCase);

			foreach (var message in messages.Where(m => m.CallId.HasValue && m.DepartmentId == departmentId))
			{
				IntakeChannel channel = null;
				if (message.IntakeChannelId != null)
					channels.TryGetValue(message.IntakeChannelId, out channel);

				sources[message.CallId.Value] = new IntakeCallSource
				{
					CallId = message.CallId.Value,
					ChannelType = message.ChannelType,
					ChannelName = channel?.Name,
					Screening = message.Screening ?? (int)IntakeScreenings.None
				};
			}

			return sources;
		}

		public async Task<IntakePurgeResult> PurgeExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var messagesBefore = nowUtc.AddDays(-Math.Max(1, IntakeConfig.MessageRetentionDays));
			var sessionsBefore = nowUtc.AddHours(-Math.Max(1, IntakeConfig.VoiceSessionRetentionHours));

			return new IntakePurgeResult
			{
				Messages = await _messages.PurgeReceivedBeforeAsync(messagesBefore, cancellationToken),
				VoiceSessions = await _sessions.PurgeStartedBeforeAsync(sessionsBefore, cancellationToken),
				// A lifted or expired block is kept as long as the messages it refused, then goes.
				BlockedSenders = await _blocked.PurgeEndedBeforeAsync(messagesBefore, cancellationToken)
			};
		}

		/// <summary>
		/// The sender is on the department's blocked list (M0326). A list that cannot be read lets the message through: a block-list
		/// outage must never lose a real request.
		/// </summary>
		private async Task<bool> IsBlockedAsync(int departmentId, string sender, DateTime now)
		{
			var key = IntakeBlockedSenders.Normalize(sender);
			if (key == null)
				return false;

			try
			{
				return await _blocked.GetInForceAsync(departmentId, key, now) != null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake blocked-sender check failed for department {departmentId}; the message is let through.");
				return false;
			}
		}

		/// <summary>The per-sender hourly limit's verdict for one message (M0326).</summary>
		private readonly struct SenderLimit
		{
			public SenderLimit(bool overLimit, int? joinedCallId)
			{
				OverLimit = overLimit;
				JoinedCallId = joinedCallId;
			}

			/// <summary>The sender already made the hour's allowance of calls.</summary>
			public bool OverLimit { get; }

			/// <summary>The open call the message was added to as a note; null = the message makes a call of its own.</summary>
			public int? JoinedCallId { get; }
		}

		/// <summary>
		/// M0326: a sender (recorded form) who already created IntakeConfig.MaxCallsPerSenderPerHour calls in the department this rolling
		/// hour has the message added to the newest of those calls still open (pending or active) as a note. With none open the message
		/// still makes a call (OverLimit, screened RepeatSender). Withheld caller ids share one placeholder number, so they are never
		/// limited as one sender. Any failure lets the message make its own call.
		/// </summary>
		private async Task<SenderLimit> CheckSenderLimitAsync(IntakeChannel channel, Department department, IntakeEnvelope envelope, DateTime now,
			CancellationToken cancellationToken)
		{
			var max = IntakeConfig.MaxCallsPerSenderPerHour;
			var sender = Cap(envelope.Sender?.Trim(), MaxSenderLength);
			if (max <= 0 || !IsSenderLimited(channel) || IntakeBlockedSenders.Normalize(sender) == null)
				return default;

			try
			{
				var recent = await _messages.GetCallsCreatedBySenderSinceAsync(channel.DepartmentId, sender, now.AddHours(-1), PublicPhoneLines) ?? new List<IntakeMessage>();
				if (recent.Count < max)
					return default;

				foreach (var callId in recent.Where(m => m.CallId.HasValue).Select(m => m.CallId.Value).Distinct())
				{
					var call = await _calls.GetCallByIdAsync(callId, false);
					if (call == null || call.IsDeleted || call.DepartmentId != channel.DepartmentId ||
						call.State != (int)CallStates.Pending && call.State != (int)CallStates.Active)
						continue;

					var text = MessageText(envelope.Body) ?? Cap(envelope.Subject?.Trim(), MaxNameLength);
					var pendingNote = new CallNote
					{
						CallId = call.CallId,
						UserId = department.ManagingUserId,
						Note = $"Another message from {SenderLabel(envelope)} ({channel.Name}): {text}",
						Source = (int)CallNoteSources.IntakeText,
						Timestamp = now
					};

					CallNote note;
					try
					{
						note = await _calls.SaveCallNoteAsync(pendingNote, cancellationToken);
					}
					catch (Exception ex) when (pendingNote.CallNoteId > 0)
					{
						// SaveCallNoteAsync inserts the note before its post-save work (protected-write check, search projection): the
						// insert gave the note its id, so the message is on the call even though that work failed. It must not go on to
						// make a call of its own as well (nor, on a cancelled request, be retried into a second note).
						Framework.Logging.LogException(ex, $"Intake per-sender note {pendingNote.CallNoteId} on call {call.CallId} was saved but its follow-up failed.");
						note = pendingNote;
					}

					if (note == null || note.CallNoteId <= 0)
						break;

					// The message is on the call now: a failed refresh must not send it on to make a call of its own as well.
					try
					{
						_events.SendMessage(new CallUpdatedEvent { DepartmentId = channel.DepartmentId, Call = call });
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex, $"Intake per-sender note on call {call.CallId} was saved but its update could not be published.");
					}

					return new SenderLimit(true, call.CallId);
				}

				return new SenderLimit(true, null);
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				Framework.Logging.LogException(ex, $"Intake per-sender limit check failed for channel {channel.IntakeChannelId}; the message makes its own call.");
				return default;
			}
		}

		/// <summary>
		/// The per-sender limit covers the public phone lines only (SMS, voice, WhatsApp) and only while the channel takes anyone. Email,
		/// chat and webhook channels are linked by an admin and often carry a CAD bridge or alarm panel that legitimately sends many
		/// incidents an hour from one address, and a sender on a channel's allowed list is trusted: their messages always make calls.
		/// A list that names nobody in particular ("*", which <see cref="IsSenderAllowed"/> reads as anyone) is no allowed list.
		/// </summary>
		public static bool IsSenderLimited(IntakeChannel channel) =>
			channel != null && !NamesSpecificSenders(channel.AllowedSenders) && PublicPhoneLines.Contains(channel.ChannelType);

		/// <summary>The transports the per-sender limit covers; only their history counts toward a sender's hour.</summary>
		private static readonly int[] PublicPhoneLines = { (int)IntakeChannelTypes.Sms, (int)IntakeChannelTypes.Voice, (int)IntakeChannelTypes.WhatsApp };

		/// <summary>The allowed-sender list restricts the channel: it has entries and none of them is the "*" anyone pattern.</summary>
		private static bool NamesSpecificSenders(string allowedSenders)
		{
			var patterns = SenderPatterns(allowedSenders);
			return patterns.Count > 0 && !patterns.Contains("*");
		}

		private static List<string> SenderPatterns(string allowedSenders) => IntakeChannelsService.SenderEntries(allowedSenders);

		/// <summary>Labels a call this service just created; a failure only loses the label, never the call.</summary>
		private async Task SafeScreenAsync(int departmentId, int callId, IntakeScreenings screening, DateTime now)
		{
			try
			{
				await _messages.SetScreeningAsync(departmentId, callId, (int)screening, new[] { (int)IntakeScreenings.None }, null, now, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake call {callId} could not be screened {screening}.");
			}
		}

		/// <summary>Whether the sender may create calls on the channel (blank list = anyone who can reach the address).</summary>
		public bool IsSenderAllowed(IntakeChannel channel, string sender)
		{
			if (string.IsNullOrWhiteSpace(channel?.AllowedSenders))
				return true;
			if (string.IsNullOrWhiteSpace(sender))
				return false;

			var patterns = SenderPatterns(channel.AllowedSenders);
			if (patterns.Count == 0 || patterns.Contains("*"))
				return true;

			var value = sender.Trim();
			switch ((IntakeChannelTypes)channel.ChannelType)
			{
				case IntakeChannelTypes.Sms:
				case IntakeChannelTypes.Voice:
				case IntakeChannelTypes.WhatsApp:
					var number = IntakeChannelAddresses.PhoneNumber(value) ?? value;
					var numberPatterns = patterns.Where(p => p.Any(char.IsDigit)).ToList();
					return numberPatterns.Count > 0 && _numbers.DoesNumberMatchAnyPattern(numberPatterns, number);

				case IntakeChannelTypes.Email:
					var address = value.ToLowerInvariant();
					var at = address.LastIndexOf('@');
					var domain = at >= 0 ? address.Substring(at) : null;
					return patterns.Any(p =>
					{
						var pattern = p.ToLowerInvariant();
						if (pattern.StartsWith("*@", StringComparison.Ordinal))
							pattern = pattern.Substring(1);
						return pattern.StartsWith("@", StringComparison.Ordinal) ? domain != null && domain == pattern : address == pattern;
					});

				default:
					return patterns.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
			}
		}

		/// <summary>The chat apps (WhatsApp, Slack, Teams, Telegram, Discord, Google Chat): their calls come to the dispatcher desk as chats.</summary>
		public static bool IsChatChannel(IntakeChannelTypes channelType) =>
			channelType == IntakeChannelTypes.WhatsApp || channelType == IntakeChannelTypes.Slack || channelType == IntakeChannelTypes.Teams ||
			channelType == IntakeChannelTypes.Telegram || channelType == IntakeChannelTypes.Discord || channelType == IntakeChannelTypes.GoogleChat;

		private async Task PutChatOnDeskAsync(IntakeEnvelope envelope, Call saved)
		{
			try
			{
				// NotRouted (desk off, or Chats unticked) leaves the call to the event's hand-off, which puts it on the desk as a pending
				// call when the desk takes those, as an emailed call is when Emails is unticked. The call is saved, so nothing here cancels.
				await _dispatcherRouting.Value.EnqueueAsync(new DispatcherEnqueueRequest
				{
					DepartmentId = saved.DepartmentId,
					Kind = DispatcherQueueItemKinds.Chat,
					CallId = saved.CallId,
					SourceAddress = Cap(envelope.Sender?.Trim(), MaxSenderLength),
					SourceName = Cap(envelope.SenderName?.Trim(), MaxNameLength),
					Subject = saved.Name,
					Body = saved.NatureOfCall,
					CreatedByUserId = saved.ReportingUserId
				}, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The call event that follows tries again under the same kind (DispatcherDeskCallEventService reads the call's intake
				// channel), as does every later update of the waiting call, so a desk that takes chats but not pending calls still gets it.
				Framework.Logging.LogException(ex, $"Intake chat call {saved.CallId} could not be put on the dispatcher desk.");
			}
		}

		/// <summary>The pending call, built the same way whether or not AI runs afterwards (D7).</summary>
		public sealed class BuiltCall
		{
			public Call Call { get; set; }

			public bool NameIsPlaceholder { get; set; }

			public string Reason { get; set; }
		}

		public async Task<BuiltCall> BuildCallAsync(IntakeChannel channel, Department department, IntakeEnvelope envelope, DateTime now)
		{
			var departmentId = department.DepartmentId;
			var priorities = await _calls.GetActiveCallPrioritiesForDepartmentAsync(departmentId) ?? new List<DepartmentCallPriority>();
			var callTypes = await _calls.GetCallTypesForDepartmentAsync(departmentId) ?? new List<CallType>();
			var priority = channel.DefaultPriority ?? CallPriorityMatch.DefaultForImport(priorities);
			var body = MessageText(envelope.Body);
			var subject = string.IsNullOrWhiteSpace(envelope.Subject) ? null : Cap(envelope.Subject.Trim(), MaxNameLength);
			var voice = envelope.ChannelType == IntakeChannelTypes.Voice;
			string reason = null;

			Call call = null;
			if (!voice && channel.ProcessingMode == (int)IntakeProcessingModes.Template && !string.IsNullOrWhiteSpace(body))
			{
				var format = channel.TemplateFormat ?? (int)CallEmailTypes.Generic;
				var email = new CallEmail { MessageId = envelope.ProviderMessageId, From = envelope.Sender, Subject = subject, Body = body, TextBody = body };

				// No members, units or active calls go in: the template only reads the message (nobody is dispatched and no live call
				// is ever updated from intake), which also keeps department rosters out of the template's reach.
				call = await _calls.GenerateCallFromEmail(format, email, department.ManagingUserId, new List<IdentityUser>(), department,
					new List<Call>(), new List<Unit>(), priority, priorities, callTypes);

				if (call != null && call.CallId > 0)
				{
					reason = "TemplateMatchedExistingCall";
					call = null;
				}
				else if (call == null)
					reason = "TemplateFoundNoCall";
			}

			var templated = call != null;
			call ??= new Call
			{
				Name = subject,
				NatureOfCall = body,
				Notes = subject == null ? body : subject + " " + body,
				Priority = priority
			};

			// The call is pending and nobody is on it: the dispatcher reviews it and chooses who to send it to.
			call.CallId = 0;
			call.DepartmentId = departmentId;
			call.State = (int)CallStates.Pending;
			call.CallSource = (int)(voice ? CallSources.VoiceIntake : CallSources.Intake);
			// The claim's provider message id (as ClaimAsync stores it): an abandoned claim's retry finds its saved call by it.
			call.SourceIdentifier = Cap(envelope.ProviderMessageId?.Trim(), 100);
			call.ReportingUserId = string.IsNullOrWhiteSpace(call.ReportingUserId) ? department.ManagingUserId : call.ReportingUserId;
			call.LoggedOn = call.LoggedOn == default ? now : call.LoggedOn;
			call.DispatchOn = null;
			call.HasBeenDispatched = null;
			call.Dispatches = new Collection<CallDispatch>();
			call.GroupDispatches = new Collection<CallDispatchGroup>();
			call.UnitDispatches = new Collection<CallDispatchUnit>();
			call.RoleDispatches = new Collection<CallDispatchRole>();

			if (string.IsNullOrWhiteSpace(call.NatureOfCall))
				call.NatureOfCall = body ?? subject ?? string.Empty;

			if (call.Priority <= 0 && channel.DefaultPriority.HasValue)
				call.Priority = channel.DefaultPriority.Value;

			if (string.IsNullOrWhiteSpace(call.Type) && !call.CallTypeId.HasValue && channel.DefaultCallTypeId.HasValue)
			{
				var callType = callTypes.FirstOrDefault(t => t.CallTypeId == channel.DefaultCallTypeId.Value);
				if (callType != null)
				{
					call.Type = callType.Type;
					call.CallTypeId = callType.CallTypeId;
				}
			}

			// No subject: a name the template made up from the body (or nothing) gives way to "{channel}: {sender}".
			var nameIsPlaceholder = false;
			if (subject == null && (string.IsNullOrWhiteSpace(call.Name) || !templated || string.Equals(call.Name?.Trim(), body?.Trim(), StringComparison.Ordinal)))
			{
				call.Name = Cap($"{channel.Name}: {SenderLabel(envelope)}", MaxNameLength);
				nameIsPlaceholder = true;
			}
			else
				call.Name = Cap(call.Name ?? subject, MaxNameLength);

			ApplyContact(call, envelope);

			if (string.IsNullOrWhiteSpace(call.ExternalIdentifier) && !string.IsNullOrWhiteSpace(envelope.ExternalId))
				call.ExternalIdentifier = Cap(envelope.ExternalId.Trim(), 100);

			if (string.IsNullOrWhiteSpace(call.Address) && !string.IsNullOrWhiteSpace(envelope.Address))
				call.Address = Cap(envelope.Address.Trim(), 500);

			if ((string.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1) && ValidCoordinates(envelope.Latitude, envelope.Longitude))
				call.GeoLocationData = envelope.Latitude.Value.ToString("0.######", CultureInfo.InvariantCulture) + "," +
					envelope.Longitude.Value.ToString("0.######", CultureInfo.InvariantCulture);

			if (!string.IsNullOrWhiteSpace(call.Address) && (string.IsNullOrWhiteSpace(call.GeoLocationData) || call.GeoLocationData.Length <= 1))
			{
				try
				{
					call.GeoLocationData = await _geo.GetLatLonFromAddress(call.Address);
				}
				catch (Exception ex)
				{
					// A geocoder outage never loses the message; the dispatcher places the call.
					Framework.Logging.LogException(ex, "Intake geocoding failed.");
				}
			}

			return new BuiltCall { Call = call, NameIsPlaceholder = nameIsPlaceholder, Reason = reason };
		}

		private static void ApplyContact(Call call, IntakeEnvelope envelope)
		{
			if (envelope.ChannelType == IntakeChannelTypes.Voice)
			{
				var answers = envelope.Answers ?? new Dictionary<string, string>();
				if (string.IsNullOrWhiteSpace(call.ContactName) && answers.TryGetValue(nameof(VoiceIntakeSteps.CallerName), out var callerName) && !string.IsNullOrWhiteSpace(callerName))
					call.ContactName = Cap(callerName.Trim(), 100);
				if (string.IsNullOrWhiteSpace(call.ContactNumber))
					call.ContactNumber = Cap(answers.TryGetValue(nameof(VoiceIntakeSteps.Callback), out var callback) && !string.IsNullOrWhiteSpace(callback)
						? callback.Trim() : envelope.Sender, 32);
				return;
			}

			if (string.IsNullOrWhiteSpace(call.ContactName) && !string.IsNullOrWhiteSpace(envelope.SenderName))
				call.ContactName = Cap(envelope.SenderName.Trim(), 100);

			// A texting caller's number is how the dispatcher reaches them.
			if (string.IsNullOrWhiteSpace(call.ContactNumber) &&
				(envelope.ChannelType == IntakeChannelTypes.Sms || envelope.ChannelType == IntakeChannelTypes.WhatsApp))
				call.ContactNumber = Cap(envelope.Sender, 32);
		}

		private async Task<IntakeChannel> ResolveAsync(IntakeEnvelope envelope)
		{
			IntakeChannel channel;
			if (!string.IsNullOrWhiteSpace(envelope.IntakeChannelId))
				channel = await _channels.GetByIdAsync(envelope.IntakeChannelId);
			else
			{
				var address = IntakeChannelAddresses.Normalize(envelope.ChannelType, envelope.ChannelAddress);
				channel = address == null ? null : await _channels.GetLiveByAddressAsync((int)envelope.ChannelType, address);
			}

			return channel != null && !channel.IsDeleted && channel.ChannelType == (int)envelope.ChannelType ? channel : null;
		}

		/// <summary>
		/// The claim row, or null when another delivery of the same provider message owns or finished it. RecoveredCallId is set (and
		/// the claim null) when an abandoned claim turns out to have saved its call before its process died: the claim now records
		/// that call, and the retry answers with it instead of saving another.
		/// </summary>
		private async Task<(IntakeMessage Message, int? RecoveredCallId)> ClaimAsync(IntakeChannel channel, IntakeEnvelope envelope, DateTime now,
			CancellationToken cancellationToken)
		{
			var message = new IntakeMessage
			{
				IntakeMessageId = Guid.NewGuid().ToString("D"),
				DepartmentId = channel.DepartmentId,
				IntakeChannelId = channel.IntakeChannelId,
				ChannelType = channel.ChannelType,
				ProviderMessageId = Cap(envelope.ProviderMessageId?.Trim(), 256),
				Sender = Cap(envelope.Sender?.Trim(), MaxSenderLength),
				State = (int)IntakeMessageStates.Received,
				ReceivedOn = now
			};

			if (await _messages.TryClaimAsync(message, cancellationToken))
				return (message, null);

			// A failed attempt (or one whose request died before it finished) left no call: the provider's retry takes it over.
			var existing = await _messages.GetByProviderMessageIdAsync(channel.IntakeChannelId, message.ProviderMessageId);
			if (existing == null || existing.CallId.HasValue)
				return (null, null);

			var abandoned = existing.State == (int)IntakeMessageStates.Received && now - existing.ReceivedOn > AbandonedClaim;
			if (existing.State != (int)IntakeMessageStates.Failed && !abandoned)
				return (null, null);

			var previouslyReceivedOn = existing.ReceivedOn;
			if (!await _messages.TryReclaimAsync(existing.IntakeMessageId, existing.State, now, now - AbandonedClaim, cancellationToken))
				return (null, null);

			// An abandoned claim may have saved its call and died before recording the id (the call commits first). The call carries the
			// provider message id (SourceIdentifier) and no intake message names it: then it is this message's call, never a second one.
			// A Failed claim never has one: Failed is only written when no call was saved. The day of slack before the claim covers a
			// template that logged the call at a time read from the message.
			if (abandoned)
			{
				try
				{
					var orphan = await _messages.GetUnclaimedIntakeCallIdAsync(channel.DepartmentId, Cap(message.ProviderMessageId, 100), IntakeCallSources,
						previouslyReceivedOn.AddDays(-1), cancellationToken);
					if (orphan.HasValue)
					{
						await _messages.CompleteAsync(existing.IntakeMessageId, (int)IntakeMessageStates.CallCreated, orphan, "RecoveredAfterAbandon", now, CancellationToken.None);
						return (null, orphan);
					}
				}
				catch
				{
					// An unknown answer is not "no saved call", so nothing is built. The takeover is given back as it was (still abandoned):
					// the provider's next delivery repeats the lookup at once instead of answering "in progress" until a fresh lease ran out.
					await SafeReleaseReclaimAsync(existing.IntakeMessageId, now, previouslyReceivedOn);
					throw;
				}
			}

			existing.State = (int)IntakeMessageStates.Received;
			existing.ReceivedOn = now;
			return (existing, null);
		}

		/// <summary>
		/// The sender's open conversation with the department, after their text was added to it; null when there is none (or its call
		/// ended, which closes it), so the text becomes a new pending call and conversation.
		/// </summary>
		private async Task<IntakeConversation> JoinOpenConversationAsync(IntakeChannel channel, IntakeEnvelope envelope, CancellationToken cancellationToken)
		{
			try
			{
				var thread = await _conversationOperations.GetOpenForParticipantAsync(channel.DepartmentId, IntakeChannelTypes.Sms, envelope.Sender);
				if (thread == null)
					return null;

				var call = thread.CallId.HasValue ? await _calls.GetCallByIdAsync(thread.CallId.Value, false) : null;
				if (call == null || call.IsDeleted || call.DepartmentId != channel.DepartmentId ||
					call.State != (int)CallStates.Active && call.State != (int)CallStates.Pending)
				{
					await _conversations.CloseAsync(channel.DepartmentId, thread.IntakeConversationId, null, "CallClosed", false, CancellationToken.None);
					return null;
				}

				return await _conversations.HandleParticipantMessageAsync(thread, envelope.ProviderMessageId, envelope.Body, envelope.ReceivedOnUtc, cancellationToken)
					? thread
					: null;
			}
			catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
			{
				// The text is never lost: it becomes a pending call of its own.
				Framework.Logging.LogException(ex, $"Intake conversation lookup failed for channel {channel.IntakeChannelId}.");
				return null;
			}
		}

		/// <param name="claimedOn">The ReceivedOn this attempt's takeover wrote: only a row still carrying it is given back.</param>
		private async Task SafeReleaseReclaimAsync(string intakeMessageId, DateTime claimedOn, DateTime receivedOn)
		{
			try
			{
				await _messages.ReleaseReclaimAsync(intakeMessageId, claimedOn, receivedOn, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake message {intakeMessageId} takeover could not be given back; it is retried once its lease runs out.");
			}
		}

		private async Task SafeCompleteAsync(string intakeMessageId, IntakeMessageStates state, int? callId, string reason, DateTime now)
		{
			try
			{
				await _messages.CompleteAsync(intakeMessageId, (int)state, callId, reason, now, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake message {intakeMessageId} outcome could not be recorded.");
			}
		}

		private async Task QueueEnrichmentAsync(Call saved, IntakeChannelTypes channelType, bool nameIsPlaceholder, CancellationToken cancellationToken)
		{
			if (!AiDispatchConfig.EnrichEnabled || saved == null || saved.CallId <= 0)
				return;

			try
			{
				// Entitlement (Ai.Intake / Ai.VoiceIntake) is checked by the worker, so a lapsed add-on still leaves the pending call.
				await _queue.EnqueueAiDispatchTriageAsync(new AiDispatchQueueItem
				{
					DepartmentId = saved.DepartmentId,
					CallId = saved.CallId,
					Channel = AiDispatchChannels.ForIntake(channelType),
					QueuedOnUtc = _clock.GetUtcNow().UtcDateTime,
					NameIsPlaceholder = nameIsPlaceholder,
					// The worker replaces the name only while it is still exactly this placeholder (a dispatcher's rename always wins).
					PlaceholderNameFingerprint = nameIsPlaceholder
						? Resgrid.Services.AiDispatch.AiDispatchEnrichmentService.PlaceholderFingerprint(saved.DepartmentId, saved.CallId, saved.Name)
						: null
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake AI enrichment could not be queued for call {saved.CallId}.");
			}
		}

		/// <summary>
		/// D6: texts over the monthly fair-use limit still create calls; the department's admins are told once per calendar month
		/// (a cache marker, else exactly at the first text over the limit when the cache is down). Each admin reached is remembered on
		/// their own; when anyone was not reached the department's marker is cleared, so the next text over the limit tries only them.
		/// The markers are best effort: an admin whose marker could not be written may be told twice, which beats an admin never told.
		/// </summary>
		private async Task CheckSmsFairUseAsync(Department department, DateTime now)
		{
			try
			{
				var limit = DedicatedNumberAddonConfig.MonthlySmsIntakeLimit;
				if (limit <= 0)
					return;

				var count = await GetMonthlyCountAsync(department.DepartmentId, IntakeChannelTypes.Sms, now);
				if (count <= limit)
					return;

				var key = $"IntakeSmsFairUse_{department.DepartmentId}_{now:yyyyMM}";
				var alert = count == limit + 1;
				var tracked = false;
				try
				{
					var marker = await _cache.TryGetStringAsync(key);
					if (marker.Succeeded)
					{
						alert = string.IsNullOrEmpty(marker.Value) && await _cache.SetStringAsync(key, "1", FairUseMarkerLifetime);
						tracked = alert;
					}
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "Intake fair-use marker unavailable.");
				}

				if (!alert)
					return;

				var missed = 0;
				try
				{
					var text = $"Resgrid: {department.Name} has received {count} texts on its private inbound dispatch number this month, over the fair-use limit of {limit}. " +
						"Texts still create pending calls. Contact Resgrid support if this volume is expected.";
					var number = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(department.DepartmentId);
					foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(department.DepartmentId) ?? new List<IdentityUser>())
					{
						// Each admin who was told is remembered on their own, so a retry after a partial failure reaches only the others.
						var adminKey = key + "_" + admin.UserId;
						if (tracked && await HasFairUseMarkerAsync(adminKey))
							continue;

						var delivered = false;
						try
						{
							delivered = await _communication.SendNotificationDeliveredAsync(admin.UserId, department.DepartmentId, text, number, department,
								"Private Inbound Dispatch Number usage", null, false, null);
						}
						catch (Exception ex)
						{
							Framework.Logging.LogException(ex, $"Intake fair-use notice to an administrator of department {department.DepartmentId} failed.");
						}

						if (!delivered)
							missed++;
						else if (tracked)
							await SetFairUseMarkerAsync(adminKey);
					}
				}
				catch (Exception ex)
				{
					// The number or the admin list could not be read, so nobody (or not everyone) was told: the marker this attempt just set
					// must not keep the next text over the limit from trying again.
					missed++;
					Framework.Logging.LogException(ex, $"Intake fair-use notice for department {department.DepartmentId} could not be sent.");
				}

				// Someone was not reached: the department's marker goes, so the next text over the limit tries those admins again.
				if (tracked && missed > 0)
				{
					try
					{
						await _cache.RemoveAsync(key);
					}
					catch (Exception ex)
					{
						Framework.Logging.LogException(ex, "Intake fair-use marker could not be cleared for a retry.");
					}
				}
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake fair-use check failed for department {department.DepartmentId}.");
			}
		}

		private static readonly TimeSpan FairUseMarkerLifetime = TimeSpan.FromDays(32);

		private async Task<bool> HasFairUseMarkerAsync(string key)
		{
			try
			{
				var marker = await _cache.TryGetStringAsync(key);
				return marker.Succeeded && !string.IsNullOrEmpty(marker.Value);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Intake fair-use marker unavailable.");
				return false;
			}
		}

		private async Task SetFairUseMarkerAsync(string key)
		{
			try
			{
				await _cache.SetStringAsync(key, "1", FairUseMarkerLifetime);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, "Intake fair-use marker could not be written.");
			}
		}

		/// <summary>No message content: the reply only confirms receipt (and travels from the master number for SMS, D5).</summary>
		public static string Acknowledgement(Department department, Call call) =>
			$"Received by {department?.Name ?? "the department"}. Your request is call {call?.Number} and is waiting for a dispatcher to review it.";

		private static string SenderLabel(IntakeEnvelope envelope)
		{
			if (!string.IsNullOrWhiteSpace(envelope.SenderName))
				return envelope.SenderName.Trim();
			return string.IsNullOrWhiteSpace(envelope.Sender) ? "unknown sender" : envelope.Sender.Trim();
		}

		private static string MessageText(string body)
		{
			if (string.IsNullOrWhiteSpace(body))
				return null;

			var text = body.Trim();
			var max = Math.Max(100, IntakeConfig.MaxMessageLength);
			const string marker = " [message shortened]";
			return text.Length <= max ? text : text.Substring(0, max - marker.Length) + marker;
		}

		private static bool ValidCoordinates(double? latitude, double? longitude) =>
			latitude.HasValue && longitude.HasValue && !double.IsNaN(latitude.Value) && !double.IsNaN(longitude.Value) &&
			latitude.Value >= -90 && latitude.Value <= 90 && longitude.Value >= -180 && longitude.Value <= 180 &&
			!(latitude.Value == 0 && longitude.Value == 0);

		private static (DateTime From, DateTime Before) Month(DateTime nowUtc)
		{
			var from = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
			return (from, from.AddMonths(1));
		}

		private static string Cap(string value, int max) => value == null ? null : value.Length <= max ? value : value.Substring(0, max);
	}
}
