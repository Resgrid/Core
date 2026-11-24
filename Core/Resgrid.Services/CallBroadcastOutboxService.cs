using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Pages owed to resources an edit added to a live call (M0321, <see cref="CallBroadcastOutboxEntry"/>). The edit records the
	/// page just before the call saves and sends it at the end of the request; whatever the queue refused (or a request that failed
	/// in between never sent) is retried by worker 12 with a short backoff until <see cref="MaximumAge"/>. Each attempt claims the row
	/// first, so two senders never page the same row; a sender that stops after the queue accepted but before the row was marked
	/// sent leaves it to be sent again once its lease runs out (at least once: a repeated page over a lost one).
	/// </summary>
	public class CallBroadcastOutboxService : ICallBroadcastOutboxService
	{
		/// <summary>How long a new row waits for its own edit to send it before the worker may.</summary>
		public static readonly TimeSpan InProcessGrace = TimeSpan.FromMinutes(2);

		/// <summary>How long a claim keeps others off a row; a sender that stopped mid-send is taken over after it.</summary>
		public static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

		/// <summary>A page the queue kept refusing for this long is dropped (and logged): the call has moved on.</summary>
		public static readonly TimeSpan MaximumAge = TimeSpan.FromHours(2);

		/// <summary>Finished rows are kept this long for diagnosis.</summary>
		public static readonly TimeSpan RetainFinishedFor = TimeSpan.FromDays(7);

		/// <summary>
		/// A row is recorded before its edit saves the call. Until it is this old, the worker finding nobody owed on the stored call
		/// may only mean that save has not landed yet, so it tries again later instead of closing the row.
		/// </summary>
		public static readonly TimeSpan SettleWindow = TimeSpan.FromMinutes(10);

		private readonly ICallBroadcastOutboxRepository _outboxRepository;
		private readonly ICallsService _callsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IQueueService _queueService;

		public CallBroadcastOutboxService(ICallBroadcastOutboxRepository outboxRepository, ICallsService callsService, IUserProfileService userProfileService,
			IQueueService queueService)
		{
			_outboxRepository = outboxRepository;
			_callsService = callsService;
			_userProfileService = userProfileService;
			_queueService = queueService;
		}

		/// <summary>Retry delay after attempt n (1-based): 30 s, 1, 2, 4 minutes, then every 5 (worker 12 polls every 5 minutes).</summary>
		public static TimeSpan BackoffFor(int attempts)
		{
			return TimeSpan.FromSeconds(Math.Min(300, 30 * Math.Pow(2, Math.Max(0, attempts - 1))));
		}

		public async Task<CallBroadcastOutboxEntry> RecordAsync(Call call, IEnumerable<string> userIds, IEnumerable<int> groupIds, IEnumerable<int> unitIds,
			IEnumerable<int> roleIds, string createdByUserId)
		{
			var entry = NewEntry(Guid.NewGuid().ToString(), call, userIds, groupIds, unitIds, roleIds, createdByUserId);
			if (entry == null)
				return null;

			try
			{
				// Not tied to the request: a client that goes away after the call saved must not cost the page.
				await _outboxRepository.InsertEntryAsync(entry, CancellationToken.None);
				entry.Persisted = true;
			}
			catch (Exception ex)
			{
				// The page still goes out from the request, once, as it did before the outbox; only its retry is lost.
				Logging.LogException(ex, $"Could not record the page for resources added to call {call.CallId}; it is sent without a retry.");
				entry.Persisted = false;
			}

			return entry;
		}

		public async Task<CallBroadcastOutboxEntry> RecordOnceAsync(string callBroadcastOutboxId, Call call, IEnumerable<string> userIds, IEnumerable<int> groupIds,
			IEnumerable<int> unitIds, IEnumerable<int> roleIds, string createdByUserId)
		{
			if (string.IsNullOrWhiteSpace(callBroadcastOutboxId))
				return await RecordAsync(call, userIds, groupIds, unitIds, roleIds, createdByUserId);

			var entry = NewEntry(callBroadcastOutboxId, call, userIds, groupIds, unitIds, roleIds, createdByUserId);
			if (entry == null)
				return null;

			var recorded = await FindRecordedAsync(callBroadcastOutboxId, call.CallId);
			if (recorded != null)
				return recorded;

			try
			{
				await _outboxRepository.InsertEntryAsync(entry, CancellationToken.None);
				entry.Persisted = true;
				return entry;
			}
			catch (Exception ex)
			{
				// An insert that reports a failure may still have gone in (a lost commit reply): read it back before calling it unrecorded.
				recorded = await FindRecordedAsync(callBroadcastOutboxId, call.CallId);
				if (recorded != null)
					return recorded;

				Logging.LogException(ex, $"Could not record the page {callBroadcastOutboxId} for call {call.CallId}; its caller retries it.");
				entry.Persisted = false;
				return entry;
			}
		}

		/// <summary>The entry already recorded under the id, or null (none, or it could not be read).</summary>
		private async Task<CallBroadcastOutboxEntry> FindRecordedAsync(string callBroadcastOutboxId, int callId)
		{
			try
			{
				var existing = await _outboxRepository.GetEntryAsync(callBroadcastOutboxId, CancellationToken.None);
				if (existing == null)
					return null;

				existing.Persisted = true;
				return existing;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Could not read the page {callBroadcastOutboxId} for call {callId}.");
				return null;
			}
		}

		private static CallBroadcastOutboxEntry NewEntry(string callBroadcastOutboxId, Call call, IEnumerable<string> userIds, IEnumerable<int> groupIds,
			IEnumerable<int> unitIds, IEnumerable<int> roleIds, string createdByUserId)
		{
			if (call == null || call.CallId <= 0)
				return null;

			var now = DateTime.UtcNow;
			var entry = new CallBroadcastOutboxEntry
			{
				CallBroadcastOutboxId = callBroadcastOutboxId,
				DepartmentId = call.DepartmentId,
				CallId = call.CallId,
				State = (int)CallBroadcastOutboxStates.Pending,
				Attempts = 0,
				NextAttemptOn = now.Add(InProcessGrace),
				CreatedByUserId = createdByUserId,
				CreatedOn = now
			};
			entry.SetRecipients(userIds, groupIds, unitIds, roleIds);

			return entry.HasRecipients ? entry : null;
		}

		public async Task<int> SendPendingForCallAsync(Call call, CallBroadcastOutboxEntry recorded = null)
		{
			if (call == null || call.CallId <= 0)
				return 0;

			var sent = 0;

			if (recorded != null && !recorded.Persisted && recorded.CallId == call.CallId)
			{
				try
				{
					if (await AttemptAsync(recorded, call) == Attempt.Sent)
						sent++;
					else
						Logging.LogError($"The page for resources added to call {call.CallId} was not sent and could not be kept for a retry.");
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"The page for resources added to call {call.CallId} could not be sent.");
				}
			}

			List<string> ids;
			try
			{
				ids = ((await _outboxRepository.GetOpenIdsForCallAsync(call.CallId, DateTime.UtcNow, CancellationToken.None)) ?? Enumerable.Empty<string>()).ToList();
			}
			catch (Exception ex)
			{
				// Worker 12 sends them instead.
				Logging.LogException(ex, $"Could not read the unsent pages of call {call.CallId}; the worker retries them.");
				return sent;
			}

			foreach (var id in ids)
			{
				try
				{
					// Every open row goes now, whatever its backoff says. Only the edit's own row is judged from the edit's saved call;
					// a row another edit recorded is judged from the call as stored, because that edit may not have saved yet and this
					// edit's copy must never close its page (from the store, a young row with nobody owed yet waits instead).
					var own = recorded != null && recorded.Persisted && string.Equals(id, recorded.CallBroadcastOutboxId, StringComparison.OrdinalIgnoreCase);
					if (await ClaimAndSendAsync(id, own ? call : null, true))
						sent++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not send page {id} of call {call.CallId}; the worker retries it.");
				}
			}

			return sent;
		}

		public async Task SupersedeCoveredAsync(Call broadcastCall)
		{
			if (broadcastCall == null || broadcastCall.CallId <= 0)
				return;

			try
			{
				var ids = ((await _outboxRepository.GetOpenIdsForCallAsync(broadcastCall.CallId, DateTime.UtcNow, CancellationToken.None)) ?? Enumerable.Empty<string>()).ToList();
				foreach (var id in ids)
				{
					var entry = await _outboxRepository.GetEntryAsync(id, CancellationToken.None);

					// Only what the broadcast carried is covered: a page for a resource a concurrent edit added after this request read the
					// call still owes that resource its page. Recipients never change on a row, so the check holds for the update below.
					if (entry != null && entry.State == (int)CallBroadcastOutboxStates.Pending && IsCoveredBy(entry, broadcastCall))
						await _outboxRepository.TrySupersedeAsync(id, DateTime.UtcNow, CancellationToken.None);
				}
			}
			catch (Exception ex)
			{
				// Left pending, a row pages its resources once more; nothing is lost.
				Logging.LogException(ex, $"Could not mark the pending pages of call {broadcastCall.CallId} as covered by its rebroadcast.");
			}
		}

		/// <summary>Every resource on the row was on the call the full broadcast carried.</summary>
		private static bool IsCoveredBy(CallBroadcastOutboxEntry entry, Call broadcastCall)
		{
			var users = new HashSet<string>((broadcastCall.Dispatches ?? new List<CallDispatch>()).Where(x => x != null).Select(x => x.UserId), StringComparer.OrdinalIgnoreCase);
			var groups = new HashSet<int>((broadcastCall.GroupDispatches ?? new List<CallDispatchGroup>()).Select(x => x.DepartmentGroupId));
			var units = new HashSet<int>((broadcastCall.UnitDispatches ?? new List<CallDispatchUnit>()).Select(x => x.UnitId));
			var roles = new HashSet<int>((broadcastCall.RoleDispatches ?? new List<CallDispatchRole>()).Select(x => x.RoleId));

			return entry.GetUserIds().All(users.Contains) && entry.GetGroupIds().All(groups.Contains) &&
				entry.GetUnitIds().All(units.Contains) && entry.GetRoleIds().All(roles.Contains);
		}

		public async Task<int> SendDueAsync(int batchSize, CancellationToken cancellationToken)
		{
			var ids = ((await _outboxRepository.GetDueIdsAsync(DateTime.UtcNow, batchSize <= 0 ? 100 : batchSize, cancellationToken)) ?? Enumerable.Empty<string>()).ToList();

			var sent = 0;
			foreach (var id in ids)
			{
				// Stopping: no further row is started (a claimed one finishes; a stopped one is taken over after its lease).
				cancellationToken.ThrowIfCancellationRequested();

				try
				{
					if (await ClaimAndSendAsync(id, null, false))
						sent++;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Could not send call page {id}; it is retried on the next pass.");
				}
			}

			try
			{
				await _outboxRepository.PurgeCompletedBeforeAsync(DateTime.UtcNow.Subtract(RetainFinishedFor), cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Could not purge finished call pages.");
			}

			return sent;
		}

		/// <summary>
		/// Claims the row and makes one attempt. <paramref name="call"/> is the edit's saved, populated call; null (the worker) loads
		/// it. True when the queue accepted the page.
		/// </summary>
		private async Task<bool> ClaimAndSendAsync(string id, Call call, bool ignoreSchedule)
		{
			var claimToken = Guid.NewGuid().ToString("N");
			var now = DateTime.UtcNow;

			if (!await _outboxRepository.TryClaimAsync(id, claimToken, now, now.Add(LeaseDuration), ignoreSchedule, CancellationToken.None))
				return false;

			var entry = await _outboxRepository.GetEntryAsync(id, CancellationToken.None);
			if (entry == null)
				return false;

			Attempt attempt;
			var fromStore = call == null || call.CallId != entry.CallId;
			try
			{
				if (fromStore)
					call = await LoadCallAsync(entry.CallId);

				attempt = await AttemptAsync(entry, call);
			}
			catch (Exception ex)
			{
				// The call could not be read (or the profiles): try again later rather than drop the page.
				Logging.LogException(ex, $"Call page {id} for call {entry.CallId} could not be prepared; it is retried.");
				await _outboxRepository.RetryLaterAsync(id, claimToken, DateTime.UtcNow.Add(BackoffFor(entry.Attempts)), ex.Message, CancellationToken.None);
				return false;
			}

			switch (attempt)
			{
				case Attempt.Sent:
					await _outboxRepository.CompleteAsync(id, claimToken, CallBroadcastOutboxStates.Sent, DateTime.UtcNow, null, CancellationToken.None);
					return true;

				case Attempt.NobodyLeft:
					// Read from the store while its edit may still be saving: not settled yet. The edit's own call is its saved state.
					if (fromStore && DateTime.UtcNow - entry.CreatedOn < SettleWindow)
					{
						await _outboxRepository.RetryLaterAsync(id, claimToken, DateTime.UtcNow.Add(BackoffFor(entry.Attempts)), "Nobody owed the page yet.", CancellationToken.None);
						return false;
					}

					await _outboxRepository.CompleteAsync(id, claimToken, CallBroadcastOutboxStates.Skipped, DateTime.UtcNow, null, CancellationToken.None);
					return false;

				default:
					var age = DateTime.UtcNow - entry.CreatedOn;
					if (age >= MaximumAge)
					{
						Logging.LogError($"Call {entry.CallId}: the page for resources added on {entry.CreatedOn:u} was refused by the queue for {age.TotalMinutes:0} minutes and was dropped ({id}).");
						await _outboxRepository.CompleteAsync(id, claimToken, CallBroadcastOutboxStates.Expired, DateTime.UtcNow, "The queue refused the page.", CancellationToken.None);
					}
					else
					{
						await _outboxRepository.RetryLaterAsync(id, claimToken, DateTime.UtcNow.Add(BackoffFor(entry.Attempts)), "The queue refused the page.", CancellationToken.None);
					}

					return false;
			}
		}

		private async Task<Call> LoadCallAsync(int callId)
		{
			var call = await _callsService.GetCallByIdAsync(callId);
			if (call == null)
				return null;

			// The same picture of the call the edit pages from (dispatches, releases, attachments for the dispatch audio, protocols).
			return await _callsService.PopulateCallData(call, true, true, true, true, true, true, true, true, true);
		}

		/// <summary>
		/// One attempt at the page, with whoever is still owed it on <paramref name="call"/> as it is now: the call still live and in
		/// the department, members still dispatched and not released since, groups and roles still on it, units still active on it.
		/// </summary>
		private async Task<Attempt> AttemptAsync(CallBroadcastOutboxEntry entry, Call call)
		{
			if (call == null || call.IsDeleted || call.DepartmentId != entry.DepartmentId || !IsLive(call))
				return Attempt.NobodyLeft;

			var userIds = new List<string>();
			foreach (var id in entry.GetUserIds())
			{
				// The dispatch row's spelling: the broadcast filter matches ids exactly.
				var dispatch = call.Dispatches?.FirstOrDefault(x => x != null && string.Equals(x.UserId, id, StringComparison.OrdinalIgnoreCase));
				if (dispatch != null && !call.IsUserReleased(dispatch.UserId) && !userIds.Contains(dispatch.UserId, StringComparer.OrdinalIgnoreCase))
					userIds.Add(dispatch.UserId);
			}

			var callGroupIds = new HashSet<int>((call.GroupDispatches ?? new List<CallDispatchGroup>()).Select(x => x.DepartmentGroupId));
			var callRoleIds = new HashSet<int>((call.RoleDispatches ?? new List<CallDispatchRole>()).Select(x => x.RoleId));
			var activeUnitIds = CallDispatchUnitReleases.ActiveUnitIds(call.UnitDispatches);

			var groupIds = entry.GetGroupIds().Where(callGroupIds.Contains).ToList();
			var unitIds = entry.GetUnitIds().Where(activeUnitIds.Contains).ToList();
			var roleIds = entry.GetRoleIds().Where(callRoleIds.Contains).ToList();

			if (!userIds.Any() && !groupIds.Any() && !unitIds.Any() && !roleIds.Any())
				return Attempt.NobodyLeft;

			var cqi = new CallQueueItem();
			cqi.Call = call;
			cqi.SetBroadcastDispatches(userIds, groupIds, unitIds, roleIds);

			// As the edit always did: everyone's profile when a group, unit or role is paged, else just the members'.
			if (groupIds.Any() || unitIds.Any() || roleIds.Any())
				cqi.Profiles = ((await _userProfileService.GetAllProfilesForDepartmentAsync(call.DepartmentId)) ?? new Dictionary<string, UserProfile>()).Select(x => x.Value).ToList();
			else
				cqi.Profiles = await _userProfileService.GetSelectedUserProfilesAsync(userIds);

			bool accepted;
			try
			{
				accepted = await _queueService.EnqueueCallBroadcastAsync(cqi, CancellationToken.None);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The queue refused the page for resources added to call {call.CallId}.");
				accepted = false;
			}

			return accepted ? Attempt.Sent : Attempt.QueueRefused;
		}

		/// <summary>Gone out (or due): a pending call, or a scheduled one before its time, sends its whole list when it is dispatched.</summary>
		private static bool IsLive(Call call)
		{
			return call.State == (int)CallStates.Active &&
				(call.HasBeenDispatched.GetValueOrDefault() || !call.DispatchOn.HasValue || call.DispatchOn.Value <= DateTime.UtcNow);
		}

		private enum Attempt
		{
			Sent,
			NobodyLeft,
			QueueRefused
		}
	}
}
