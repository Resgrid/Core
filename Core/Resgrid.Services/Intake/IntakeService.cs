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

		private static readonly int[] NotCounted = { (int)IntakeMessageStates.Duplicate };

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
		private readonly TimeProvider _clock;

		public IntakeService(IIntakeChannelsRepository channels, IIntakeMessagesRepository messages, IVoiceIntakeSessionsRepository sessions,
			IDepartmentsService departments, IDepartmentSettingsService departmentSettings, IFeatureToggleService flags, ICallsService calls,
			INumbersService numbers, IGeoLocationProvider geo, IEventAggregator events, IQueueService queue, ICommunicationService communication,
			ICacheProvider cache, IIntakeConversationService conversations, IIntakeConversationOperations conversationOperations, TimeProvider clock)
		{
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
			var message = await ClaimAsync(channel, envelope, now, cancellationToken);
			if (message == null)
			{
				var existing = await _messages.GetByProviderMessageIdAsync(channel.IntakeChannelId, Cap(envelope.ProviderMessageId, 256));
				result.State = IntakeMessageStates.Duplicate;
				result.CallId = existing?.CallId;
				result.Reason = "Duplicate";
				return result;
			}

			int? savedCallId = null;
			Call pendingCall = null;
			try
			{
				string refusal = null;
				var state = IntakeMessageStates.Refused;
				if (!string.IsNullOrWhiteSpace(envelope.RefusalReason))
					refusal = Cap(envelope.RefusalReason.Trim(), 100);
				else if (!await _flags.IsEnabledAsync(FeatureFlagKeys.IntakeChannels, channel.DepartmentId))
					refusal = "FeatureOff";
				else if (!channel.IsEnabled)
					refusal = "ChannelDisabled";
				else if (string.IsNullOrWhiteSpace(envelope.Body) && string.IsNullOrWhiteSpace(envelope.Subject))
					refusal = "EmptyMessage";
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
					if (joined != null)
					{
						await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.AddedToConversation, joined.CallId, "Conversation", now, CancellationToken.None);
						result.State = IntakeMessageStates.AddedToConversation;
						result.CallId = joined.CallId;
						result.Reason = "Conversation";
						result.IntakeConversationId = joined.IntakeConversationId;
						await CheckSmsFairUseAsync(department, now);
						return result;
					}
				}

				var built = await BuildCallAsync(channel, department, envelope, now);
				pendingCall = built.Call;
				var saved = await _calls.SaveCallAsync(built.Call, cancellationToken);
				savedCallId = saved.CallId > 0 ? saved.CallId : (int?)null;

				// The id is on the claim before anything else runs, so a retry after a later failure finds the call instead of making another.
				await _messages.CompleteAsync(message.IntakeMessageId, (int)IntakeMessageStates.CallCreated, saved.CallId, built.Reason, now, CancellationToken.None);
				result.State = IntakeMessageStates.CallCreated;
				result.CallId = saved.CallId;
				result.Reason = built.Reason;

				// As the v4 pending path: dispatcher screens refresh; "call added" (chat channel, workflows, field apps) waits for dispatch.
				_events.SendMessage(new CallUpdatedEvent { DepartmentId = channel.DepartmentId, Call = saved });

				// The conversation's welcome (from the master number) replaces the acknowledgement: one text, never two. Started before the
				// enrichment is queued so the first Enhanced AI question is not stuck behind it on the queue.
				IntakeConversation conversation = null;
				if (conversational)
					conversation = await _conversations.StartFromIntakeAsync(channel, envelope, saved, cancellationToken);
				result.IntakeConversationId = conversation?.IntakeConversationId;

				if (channel.ProcessingMode == (int)IntakeProcessingModes.EnhancedAi || envelope.ChannelType == IntakeChannelTypes.Voice)
					await QueueEnrichmentAsync(saved, envelope.ChannelType, built.NameIsPlaceholder, cancellationToken);

				if (envelope.ChannelType == IntakeChannelTypes.Sms)
					await CheckSmsFairUseAsync(department, now);

				if (channel.SendAcknowledgement && conversation == null)
					result.Acknowledgement = Acknowledgement(department, saved);

				return result;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				savedCallId ??= pendingCall?.CallId > 0 ? pendingCall.CallId : (int?)null;
				// A call already saved keeps its claim, so the provider's retry is a duplicate rather than a second call.
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

				await SafeCompleteAsync(message.IntakeMessageId, IntakeMessageStates.Failed, null, Cap(ex.GetType().Name + ": " + ex.Message, MaxReasonLength), now);
				result.State = IntakeMessageStates.Failed;
				result.Reason = "Failed";
				return result;
			}
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
			var ids = (callIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().Take(500).ToList();
			if (ids.Count == 0)
				return sources;

			var messages = await _messages.GetByCallIdsAsync(departmentId, ids) ?? new List<IntakeMessage>();
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
					ChannelName = channel?.Name
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
				VoiceSessions = await _sessions.PurgeStartedBeforeAsync(sessionsBefore, cancellationToken)
			};
		}

		/// <summary>Whether the sender may create calls on the channel (blank list = anyone who can reach the address).</summary>
		public bool IsSenderAllowed(IntakeChannel channel, string sender)
		{
			if (string.IsNullOrWhiteSpace(channel?.AllowedSenders))
				return true;
			if (string.IsNullOrWhiteSpace(sender))
				return false;

			var patterns = channel.AllowedSenders.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
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
			call.SourceIdentifier = Cap(envelope.ProviderMessageId, 100);
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

		/// <summary>The claim row, or null when another delivery of the same provider message owns or finished it.</summary>
		private async Task<IntakeMessage> ClaimAsync(IntakeChannel channel, IntakeEnvelope envelope, DateTime now, CancellationToken cancellationToken)
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
				return message;

			// A failed attempt (or one whose request died before it finished) left no call: the provider's retry takes it over.
			var existing = await _messages.GetByProviderMessageIdAsync(channel.IntakeChannelId, message.ProviderMessageId);
			if (existing == null || existing.CallId.HasValue)
				return null;

			var retryable = existing.State == (int)IntakeMessageStates.Failed ||
				existing.State == (int)IntakeMessageStates.Received && now - existing.ReceivedOn > AbandonedClaim;
			if (!retryable)
				return null;

			if (!await _messages.TryReclaimAsync(existing.IntakeMessageId, existing.State, cancellationToken))
				return null;

			existing.State = (int)IntakeMessageStates.Received;
			return existing;
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
			catch (Exception ex)
			{
				// The text is never lost: it becomes a pending call of its own.
				Framework.Logging.LogException(ex, $"Intake conversation lookup failed for channel {channel.IntakeChannelId}.");
				return null;
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
					NameIsPlaceholder = nameIsPlaceholder
				}, cancellationToken);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake AI enrichment could not be queued for call {saved.CallId}.");
			}
		}

		/// <summary>
		/// D6: texts over the monthly fair-use limit still create calls; the department's admins are told once per calendar month
		/// (a cache marker, else exactly at the first text over the limit when the cache is down).
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
				try
				{
					var marker = await _cache.TryGetStringAsync(key);
					if (marker.Succeeded)
						alert = string.IsNullOrEmpty(marker.Value) && await _cache.SetStringAsync(key, "1", TimeSpan.FromDays(32));
				}
				catch (Exception ex)
				{
					Framework.Logging.LogException(ex, "Intake fair-use marker unavailable.");
				}

				if (!alert)
					return;

				var text = $"Resgrid: {department.Name} has received {count} texts on its private inbound dispatch number this month, over the fair-use limit of {limit}. " +
					"Texts still create pending calls. Contact Resgrid support if this volume is expected.";
				var number = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(department.DepartmentId);
				foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(department.DepartmentId) ?? new List<IdentityUser>())
					await _communication.SendNotificationAsync(admin.UserId, department.DepartmentId, text, number, department, "Private Inbound Dispatch Number usage");
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake fair-use check failed for department {department.DepartmentId}.");
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
