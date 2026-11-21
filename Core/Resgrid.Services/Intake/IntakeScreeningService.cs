using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Intake spam handling (call-intake-and-ai-destinations-plan.md §9, M0326). A dispatcher's "dismiss as spam" cancels a pending
	/// intake call (nobody was dispatched, nobody is notified), closes its SMS conversation without texting the sender, labels the call
	/// and blocks the sender for IntakeConfig.SpamBlockDays. IntakeService refuses blocked senders before it builds anything.
	/// Enhanced AI's "likely not an incident" reading is recorded here too, as a label only: the call stays a pending call.
	/// </summary>
	public class IntakeScreeningService : IIntakeScreeningService
	{
		/// <summary>Screenings an automatic reading may replace: never a dispatcher's dismissal, never the repeat-sender label.</summary>
		private static readonly int[] AiReplaceable = { (int)IntakeScreenings.None, (int)IntakeScreenings.LikelyNotIncident };

		/// <summary>A dismissal replaces any automatic label.</summary>
		private static readonly int[] DismissReplaceable = { (int)IntakeScreenings.None, (int)IntakeScreenings.LikelyNotIncident, (int)IntakeScreenings.RepeatSender };

		private readonly IIntakeMessagesRepository _messages;
		private readonly IIntakeBlockedSendersRepository _blocked;
		private readonly ICallsService _calls;
		private readonly Lazy<IIntakeConversationService> _conversations;
		private readonly IEventAggregator _events;
		private readonly TimeProvider _clock;

		/// <param name="conversations">Lazy: the conversation service reaches the AI and desk paths that read screenings.</param>
		public IntakeScreeningService(IIntakeMessagesRepository messages, IIntakeBlockedSendersRepository blocked, ICallsService calls,
			Lazy<IIntakeConversationService> conversations, IEventAggregator events, TimeProvider clock)
		{
			_messages = messages;
			_blocked = blocked;
			_calls = calls;
			_conversations = conversations;
			_events = events;
			_clock = clock;
		}

		public async Task<IntakeDismissResult> DismissAsSpamAsync(int departmentId, int callId, string userId, bool blockSender,
			CancellationToken cancellationToken = default)
		{
			var result = new IntakeDismissResult { CallId = callId, Result = IntakeDismissResults.NotFound };
			if (departmentId <= 0 || callId <= 0)
				return result;

			var call = await _calls.GetCallByIdAsync(callId, true);
			if (call == null || call.DepartmentId != departmentId || call.IsDeleted ||
				call.CallSource != (int)CallSources.Intake && call.CallSource != (int)CallSources.VoiceIntake)
				return result;

			var message = (await _messages.GetByCallIdsAsync(departmentId, new[] { callId }) ?? new List<IntakeMessage>())
				.FirstOrDefault(m => m.CallId == callId && m.DepartmentId == departmentId);
			if (message == null)
				return result;

			if (call.State != (int)CallStates.Pending)
			{
				result.Result = IntakeDismissResults.NotPending;
				return result;
			}

			var now = _clock.GetUtcNow().UtcDateTime;

			// The block goes first: a block that fails leaves the call untouched for the dispatcher to try again.
			if (blockSender)
			{
				var sender = IntakeBlockedSenders.Normalize(message.Sender);
				if (sender == null)
					result.SenderNotBlockable = true;
				else
					result.Block = await BlockAsync(departmentId, sender, message.ChannelType, callId, userId, now, cancellationToken);
			}

			// Cancelled, as Omni's "Cancel call" on a pending call: nobody was dispatched, so nobody is told and nothing is released.
			call.State = (int)CallStates.Cancelled;
			call.ClosedByUserId = userId;
			call.ClosedOn = now;
			var saved = await _calls.SaveCallAsync(call, cancellationToken);
			result.Result = IntakeDismissResults.Dismissed;

			try
			{
				await _messages.SetScreeningAsync(departmentId, callId, (int)IntakeScreenings.DismissedAsSpam, DismissReplaceable, userId, now, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake call {callId} was dismissed but its screening could not be recorded.");
			}

			// The texter is not told the thread ended: nothing goes back to a number dismissed as spam.
			try
			{
				var conversation = await _conversations.Value.GetForCallAsync(departmentId, callId);
				if (conversation != null && conversation.State == (int)IntakeConversationStates.Open)
					await _conversations.Value.CloseAsync(departmentId, conversation.IntakeConversationId, userId, "DismissedAsSpam", false, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake call {callId} was dismissed but its conversation could not be closed.");
			}

			_events.SendMessage(new CallClosedEvent { DepartmentId = departmentId, Call = saved ?? call });
			return result;
		}

		public async Task<IntakeBlockedSender> GetBlockAsync(int departmentId, string sender)
		{
			var key = IntakeBlockedSenders.Normalize(sender);
			if (departmentId <= 0 || key == null)
				return null;

			return await _blocked.GetInForceAsync(departmentId, key, _clock.GetUtcNow().UtcDateTime);
		}

		public async Task<List<IntakeBlockedSender>> GetBlockedSendersAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new List<IntakeBlockedSender>();

			return (await _blocked.GetCurrentForDepartmentAsync(departmentId) ?? new List<IntakeBlockedSender>())
				.Where(b => b.DepartmentId == departmentId).ToList();
		}

		public async Task<bool> UnblockAsync(int departmentId, string intakeBlockedSenderId, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(intakeBlockedSenderId))
				return false;

			return await _blocked.RemoveAsync(departmentId, intakeBlockedSenderId.Trim(), userId, _clock.GetUtcNow().UtcDateTime, cancellationToken);
		}

		public async Task<bool> SetAiScreeningAsync(int departmentId, int callId, bool likelyNotIncident, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || callId <= 0)
				return false;

			// Setting replaces None (or itself, a no-op); clearing replaces only the AI's own label.
			return await _messages.SetScreeningAsync(departmentId, callId,
				(int)(likelyNotIncident ? IntakeScreenings.LikelyNotIncident : IntakeScreenings.None),
				likelyNotIncident ? AiReplaceable : new[] { (int)IntakeScreenings.LikelyNotIncident },
				null, _clock.GetUtcNow().UtcDateTime, cancellationToken);
		}

		public async Task<Dictionary<int, IntakeScreenings>> GetScreeningsAsync(int departmentId, IEnumerable<int> callIds)
		{
			var screenings = new Dictionary<int, IntakeScreenings>();
			var ids = (callIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().Take(500).ToList();
			if (departmentId <= 0 || ids.Count == 0)
				return screenings;

			foreach (var message in await _messages.GetByCallIdsAsync(departmentId, ids) ?? new List<IntakeMessage>())
			{
				if (message.CallId.HasValue && message.DepartmentId == departmentId && (message.Screening ?? 0) != (int)IntakeScreenings.None &&
					Enum.IsDefined(typeof(IntakeScreenings), message.Screening.Value))
					screenings[message.CallId.Value] = (IntakeScreenings)message.Screening.Value;
			}

			return screenings;
		}

		/// <summary>
		/// The standing block for the sender, made or renewed: a new dismissal restarts the block period (a block that never expires
		/// stays that way). Two dispatchers racing leave one row: the loser of the conditional insert renews the winner's.
		/// </summary>
		private async Task<IntakeBlockedSender> BlockAsync(int departmentId, string sender, int channelType, int callId, string userId, DateTime now,
			CancellationToken cancellationToken)
		{
			var days = IntakeConfig.SpamBlockDays;
			var expiresOn = days > 0 ? now.AddDays(days) : (DateTime?)null;

			for (var attempt = 0; attempt < 2; attempt++)
			{
				var current = await _blocked.GetCurrentAsync(departmentId, sender);
				if (current != null)
				{
					current.ChannelType = channelType;
					current.Reason = IntakeBlockReasons.Spam;
					current.CallId = callId;
					current.BlockedByUserId = userId;
					current.BlockedOn = now;
					// A standing row with no expiry is permanent and stays so; otherwise the later of its expiry and the new period's.
					if (current.ExpiresOn.HasValue)
						current.ExpiresOn = !expiresOn.HasValue || current.ExpiresOn.Value > expiresOn.Value
							? (expiresOn.HasValue ? current.ExpiresOn : null)
							: expiresOn;
					if (await _blocked.RenewAsync(current, cancellationToken))
						return current;

					continue;
				}

				var block = new IntakeBlockedSender
				{
					IntakeBlockedSenderId = Guid.NewGuid().ToString("D"),
					DepartmentId = departmentId,
					Sender = sender,
					ChannelType = channelType,
					Reason = IntakeBlockReasons.Spam,
					CallId = callId,
					BlockedByUserId = userId,
					BlockedOn = now,
					ExpiresOn = expiresOn
				};
				if (await _blocked.TryInsertAsync(block, cancellationToken))
					return block;
			}

			// Another writer keeps winning: the block it made stands.
			return await _blocked.GetCurrentAsync(departmentId, sender);
		}
	}
}
