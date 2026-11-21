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

			// The stored call must still be pending when it is cancelled. Claiming it the way a dispatch does (one conditional write on the
			// stored row) keeps a dispatcher's "Dispatch now" or the scheduled-calls worker from sending it while it is being dismissed,
			// and a call sent in the meantime is reported NotPending before anything (the block included) is done.
			var wasDispatched = call.HasBeenDispatched;
			var dispatchOn = call.DispatchOn;
			var claimedOn = await _calls.TryClaimCallForDispatchAsync(callId, departmentId, cancellationToken);
			if (!claimedOn.HasValue)
			{
				result.Result = IntakeDismissResults.NotPending;
				return result;
			}

			var now = _clock.GetUtcNow().UtcDateTime;
			var labelled = false;
			var cancelAttempted = false;
			Call saved;
			try
			{
				// The block goes first: a block asked for must be in force before the call goes, or the sender would keep reaching a
				// dispatcher who believes them blocked. One that cannot be made leaves the call pending for the dispatcher to try again.
				if (blockSender)
				{
					var sender = IntakeBlockedSenders.Normalize(message.Sender);
					if (sender == null)
						result.SenderNotBlockable = true;
					else
					{
						result.Block = await BlockAsync(departmentId, sender, message.ChannelType, callId, userId, now, cancellationToken);
						if (result.Block == null)
						{
							await GiveBackClaimAsync(callId, departmentId, claimedOn.Value, dispatchOn, wasDispatched);
							result.Result = IntakeDismissResults.Failed;
							return result;
						}
					}
				}

				// The label goes before the cancel: a dismissed call is never left unlabelled (an automatic reading treats None as its to
				// replace), and a label that cannot be written leaves the call pending for a retry instead of cancelled without it.
				labelled = await _messages.SetScreeningAsync(departmentId, callId, (int)IntakeScreenings.DismissedAsSpam, DismissReplaceable, userId, now,
					CancellationToken.None);

				// No row matched: either the label already reads DismissedAsSpam (an earlier attempt's, left on the pending call) or the
				// intake row is gone (retention purges by age). Only the first may go on to the cancel.
				if (!labelled && !await IsLabelledDismissedAsync(departmentId, callId))
				{
					await GiveBackClaimAsync(callId, departmentId, claimedOn.Value, dispatchOn, wasDispatched);
					Framework.Logging.LogError($"Intake call {callId} could not be labelled as spam (its intake record no longer matches); it is still pending.");
					result.Result = IntakeDismissResults.Failed;
					return result;
				}

				// Cancelled, as Omni's "Cancel call" on a pending call: nobody was dispatched, so nobody is told and nothing is released.
				// The stored row is read again under the claim so edits made before it are kept; the claim's dispatched mark is not.
				var stored = await _calls.GetCallByIdAsync(callId, true) ?? call;
				stored.State = (int)CallStates.Cancelled;
				stored.ClosedByUserId = userId;
				stored.ClosedOn = now;
				stored.HasBeenDispatched = wasDispatched;
				cancelAttempted = true;
				saved = await _calls.SaveCallAsync(stored, cancellationToken);
			}
			catch (Exception ex)
			{
				// SaveCallAsync writes the call before its post-save work (protected-write checks, search projection, index), which can
				// throw after the cancel committed. A cancelled row can no longer be claimed or given back, so a committed cancel keeps its
				// label and is a dismissal; only a cancel that never reached the row (or whose outcome cannot be read) is compensated.
				var committed = cancelAttempted ? await ReadCommittedCancelAsync(callId, departmentId) : null;
				if (committed == null)
				{
					// The label goes back while this claim still holds the call: given back first, another dismissal could claim it, take the
					// label as its own (IsLabelledDismissedAsync) and cancel, and this late restore would then strip a real dismissal's label.
					if (labelled)
						await RestoreScreeningAsync(departmentId, callId, message, now);
					await GiveBackClaimAsync(callId, departmentId, claimedOn.Value, dispatchOn, wasDispatched);

					if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
						throw;

					Framework.Logging.LogException(ex, $"Intake call {callId} could not be dismissed as spam; it is still pending.");
					result.Result = IntakeDismissResults.Failed;
					return result;
				}

				Framework.Logging.LogException(ex, $"Intake call {callId} was cancelled as spam, but work after its save failed; it stays dismissed.");
				saved = committed;
			}

			result.Result = IntakeDismissResults.Dismissed;
			try
			{
				await _calls.CompleteCallDispatchClaimAsync(callId, departmentId, claimedOn.Value, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// Harmless: a cancelled call can never be claimed again.
				Framework.Logging.LogException(ex, $"Intake call {callId} was dismissed but its claim could not be ended.");
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

		/// <summary>Puts the claimed call back to waiting (only this claim; a call that moved on meanwhile is left alone).</summary>
		private async Task GiveBackClaimAsync(int callId, int departmentId, DateTime claimedOn, DateTime? dispatchOn, bool? wasDispatched)
		{
			try
			{
				await _calls.ReleaseCallDispatchClaimAsync(callId, departmentId, claimedOn, (int)CallStates.Pending, dispatchOn, wasDispatched, CancellationToken.None);
			}
			catch (Exception ex)
			{
				// The claim's lease runs out on its own (CallDispatchClaims.Lease) and the call can be sent or dismissed again then.
				Framework.Logging.LogException(ex, $"Intake call {callId} claim could not be given back after a failed dismissal.");
			}
		}

		/// <summary>True when the call's intake row already carries the dismissal label (read again under the claim).</summary>
		private async Task<bool> IsLabelledDismissedAsync(int departmentId, int callId)
		{
			var current = (await _messages.GetByCallIdsAsync(departmentId, new[] { callId }) ?? new List<IntakeMessage>())
				.FirstOrDefault(m => m.CallId == callId && m.DepartmentId == departmentId);
			return current?.Screening == (int)IntakeScreenings.DismissedAsSpam;
		}

		/// <summary>
		/// The stored call when the dismissal's cancel is on the row (read past the cache); null when it is not, or when the read fails
		/// (an unknown outcome is compensated like a failed cancel: the claim give-back is conditional, so it cannot reopen a cancelled row).
		/// </summary>
		private async Task<Call> ReadCommittedCancelAsync(int callId, int departmentId)
		{
			try
			{
				var stored = await _calls.GetCallByIdAsync(callId, true);
				return stored != null && stored.DepartmentId == departmentId && !stored.IsDeleted && stored.State == (int)CallStates.Cancelled ? stored : null;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake call {callId} could not be read back after a failed dismissal.");
				return null;
			}
		}

		/// <summary>Takes the dismissal label back off a call that stayed pending, restoring what the row said before.</summary>
		private async Task RestoreScreeningAsync(int departmentId, int callId, IntakeMessage message, DateTime now)
		{
			try
			{
				await _messages.SetScreeningAsync(departmentId, callId, message.Screening ?? (int)IntakeScreenings.None, new[] { (int)IntakeScreenings.DismissedAsSpam },
					message.ScreenedByUserId, message.ScreenedOn ?? now, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"Intake call {callId} kept a dismissal label although it was not dismissed.");
			}
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
			var ids = (callIds ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToList();
			if (departmentId <= 0 || ids.Count == 0)
				return screenings;

			// In batches, so every call of a long list gets its screening (the IN list stays under SQL Server's parameter limit).
			var messages = new List<IntakeMessage>();
			foreach (var batch in ids.Chunk(IntakeService.CallLookupBatchSize))
				messages.AddRange(await _messages.GetByCallIdsAsync(departmentId, batch) ?? new List<IntakeMessage>());

			foreach (var message in messages)
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
					{
						// The stored expiry is decided in the renewing statement (a concurrent renewal may have set a later one): answer with it.
						// A block lifted right after the renewal reads as none, so the dismissal fails rather than report a sender blocked.
						var renewed = await _blocked.GetCurrentAsync(departmentId, sender);
						return renewed != null && renewed.IsInForce(now) ? renewed : null;
					}

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

			// Another writer keeps winning: the block it made stands. Null when there is none even now (it was lifted between every read
			// and write, or the row left standing has expired): the dismissal then fails rather than reporting a sender blocked who is not.
			var standing = await _blocked.GetCurrentAsync(departmentId, sender);
			return standing != null && standing.IsInForce(now) ? standing : null;
		}
	}
}
