using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Intake
{
	/// <summary>
	/// Voice intake call recording on the Private Inbound Dispatch Number (M0332). The number's switch makes the press-1 gate say the call
	/// is recorded; the caller's 1 is the consent, and only then <see cref="StartAsync"/> starts the carrier recording (once per CallSid).
	/// The recording ends with the call; its status callback (<see cref="HandleStatusAsync"/>) downloads the MP3 and saves it on the
	/// pending call as a file (CallAttachmentTypes.File, through ICallsService so ADP applies). A call the caller hung up on before the
	/// pending call existed keeps nothing. Worker 80 (<see cref="ProcessPendingAsync"/>) retries, discards and deletes the carrier copies.
	/// Writes use CancellationToken.None: a caller or carrier hanging up never leaves a half-written state.
	/// </summary>
	public class VoiceIntakeRecordingService : IVoiceIntakeRecordingService
	{
		/// <summary>The Web.Services route of the status callback (TwilioController.VoiceIntakeRecordingCallback, route api/Twilio/VoiceIntakeRecording).</summary>
		public const string StatusCallbackPath = "/api/Twilio/VoiceIntakeRecording";

		/// <summary>Minutes one attach attempt holds its lease; an attempt that died is taken over after this.</summary>
		public const int LeaseMinutes = 5;

		private const int BatchSize = 200;

		private readonly IVoiceIntakeSessionsRepository _sessions;
		private readonly IVoiceRecordingProvider _provider;
		private readonly ICallsService _calls;
		private readonly ICallAttachmentRepository _attachments;
		private readonly TimeProvider _clock;

		public VoiceIntakeRecordingService(IVoiceIntakeSessionsRepository sessions, IVoiceRecordingProvider provider, ICallsService calls,
			ICallAttachmentRepository attachments, TimeProvider clock)
		{
			_sessions = sessions;
			_provider = provider;
			_calls = calls;
			_attachments = attachments;
			_clock = clock;
		}

		public static string StatusCallbackUrl() => $"{SystemBehaviorConfig.ResgridApiBaseUrl?.TrimEnd('/')}{StatusCallbackPath}";

		public async Task<bool> StartAsync(DepartmentPhoneNumber number, string providerCallId, CancellationToken cancellationToken = default)
		{
			if (!VoiceIntakeRecording.IsEnabledFor(number) || string.IsNullOrWhiteSpace(providerCallId))
				return false;

			var callSid = providerCallId.Trim();
			try
			{
				var session = await _sessions.GetByProviderCallIdAsync(callSid);
				if (session == null || session.DepartmentId != number.DepartmentId || session.RecordingState != (int)VoiceIntakeRecordingStates.None)
					return false;

				// One start per call: a Twilio retry of the gate webhook (or a second press) finds the session Requested already.
				if (!await _sessions.TryBeginRecordingAsync(callSid, CancellationToken.None))
					return false;

				string recordingSid;
				using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
				{
					wait.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(IntakeConfig.VoiceRecordingStartTimeoutSeconds, 1, 10)));
					try
					{
						recordingSid = await _provider.StartCallRecordingAsync(callSid, StatusCallbackUrl(), wait.Token);
					}
					catch (OperationCanceledException)
					{
						// Twilio is slow to answer, or the caller hung up: the session stays Requested. The status callback carries the CallSid, so a
						// recording that did start is still bound; one that never did is discarded by worker 80 after VoiceRecordingWaitMinutes.
						Framework.Logging.LogInfo($"[Voice Recording] CallSid={callSid}: the start was not confirmed in time; waiting for the status callback.");
						return true;
					}
				}

				if (string.IsNullOrWhiteSpace(recordingSid))
				{
					await _sessions.TryDiscardUnconfirmedRecordingAsync(session.VoiceIntakeSessionId, CancellationToken.None);
					return false;
				}

				await _sessions.SetRecordingSidAsync(callSid, recordingSid, CancellationToken.None);
				return true;
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				// Fail open: the caller still leaves the request, unrecorded.
				Framework.Logging.LogException(ex, $"[Voice Recording] CallSid={callSid}: the recording could not be started.");
				return false;
			}
		}

		public async Task<VoiceRecordingOutcomes> HandleStatusAsync(VoiceRecordingStatus status, CancellationToken cancellationToken = default)
		{
			if (status == null || string.IsNullOrWhiteSpace(status.CallSid))
				return VoiceRecordingOutcomes.Unknown;

			// Another account's recording is never touched (the signature check already ties the callback to this account's token).
			if (!string.IsNullOrWhiteSpace(status.AccountSid) && !string.IsNullOrWhiteSpace(NumberProviderConfig.TwilioAccountSid) &&
				!string.Equals(status.AccountSid.Trim(), NumberProviderConfig.TwilioAccountSid.Trim(), StringComparison.Ordinal))
				return VoiceRecordingOutcomes.Unknown;

			var callSid = status.CallSid.Trim();
			var session = await _sessions.GetByProviderCallIdAsync(callSid);
			if (session == null)
			{
				// No session (purged, or never one): a recording this line will never attach. The carrier copy is not kept.
				await DeleteCarrierCopyAsync(null, status.RecordingSid);
				return VoiceRecordingOutcomes.Unknown;
			}

			var completed = string.Equals(status.Status?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);
			var state = completed ? VoiceIntakeRecordingStates.Completed : VoiceIntakeRecordingStates.Discarded;
			if (!await _sessions.CompleteRecordingAsync(callSid, status.RecordingSid, status.DurationSeconds, (int)state, CancellationToken.None))
			{
				// Not Requested (read again: the first read may predate a worker 80 discard): a repeated callback, a recording the gate never
				// asked for, or the first sid of one worker 80 already discarded as never started.
				var stored = await _sessions.GetByProviderCallIdAsync(callSid) ?? session;
				if (stored.RecordingState == (int)VoiceIntakeRecordingStates.Discarded && string.IsNullOrWhiteSpace(stored.RecordingSid) &&
					!string.IsNullOrWhiteSpace(status.RecordingSid))
				{
					// The session gave this recording up before its sid arrived: it is kept nowhere, so Twilio's copy goes too. The sid is stored
					// first, so a delete that fails now is retried by worker 80 (and the session purge keeps it until then).
					Framework.Logging.LogInfo($"[Voice Recording] CallSid={callSid}: recording {status.RecordingSid} arrived after the session gave it up; deleting it.");
					await _sessions.SetRecordingSidAsync(callSid, status.RecordingSid, CancellationToken.None);
					await DeleteCarrierCopyAsync(stored.VoiceIntakeSessionId, status.RecordingSid);
					return VoiceRecordingOutcomes.Discarded;
				}

				if (stored.RecordingState != (int)VoiceIntakeRecordingStates.None)
					return VoiceRecordingOutcomes.Duplicate;

				await DeleteCarrierCopyAsync(null, status.RecordingSid);
				return VoiceRecordingOutcomes.Unknown;
			}

			if (state == VoiceIntakeRecordingStates.Discarded)
			{
				Framework.Logging.LogInfo($"[Voice Recording] CallSid={callSid}: recording ended '{status.Status}'; nothing to attach.");
				await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, status.RecordingSid ?? session.RecordingSid);
				return VoiceRecordingOutcomes.Discarded;
			}

			var current = await _sessions.GetByProviderCallIdAsync(callSid) ?? session;
			return await TryAttachAsync(current, _clock.GetUtcNow().UtcDateTime, cancellationToken);
		}

		public async Task<int> ProcessPendingAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
		{
			var purgeBefore = nowUtc.AddHours(-Math.Max(1, IntakeConfig.VoiceSessionRetentionHours));
			var rows = await _sessions.GetRecordingWorkAsync(purgeBefore, IntakeConfig.VoiceRecordingDeleteCarrierCopy, BatchSize, cancellationToken);
			var changed = 0;

			foreach (var row in rows)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					switch ((VoiceIntakeRecordingStates)row.RecordingState)
					{
						case VoiceIntakeRecordingStates.Requested:
							if (nowUtc - row.StartedOn < WaitWindow())
								break;

							if (string.IsNullOrWhiteSpace(row.RecordingSid))
							{
								// The start was never confirmed and no callback came: as far as anything shows, nothing was recorded. Only while that
								// is still so: a callback that bound the sid (and completed it) since this row was read keeps its recording.
								if (await _sessions.TryDiscardUnconfirmedRecordingAsync(row.VoiceIntakeSessionId, CancellationToken.None))
									changed++;
								break;
							}

							// The status callback was lost: the call ended long ago, so the recording is fetched by its sid.
							if (await _sessions.CompleteRecordingAsync(row.ProviderCallId, row.RecordingSid, row.RecordingDurationSeconds,
								(int)VoiceIntakeRecordingStates.Completed, CancellationToken.None))
							{
								row.RecordingState = (int)VoiceIntakeRecordingStates.Completed;
								if (await TryAttachAsync(row, nowUtc, cancellationToken) != VoiceRecordingOutcomes.Waiting)
									changed++;
							}
							break;

						case VoiceIntakeRecordingStates.Completed:
							if (await TryAttachAsync(row, nowUtc, cancellationToken) != VoiceRecordingOutcomes.Waiting)
								changed++;
							break;

						default:
							// Attached / Discarded (and Failed ones about to be purged): only the carrier copy is left.
							if (await DeleteCarrierCopyAsync(row.VoiceIntakeSessionId, row.RecordingSid))
								changed++;
							break;
					}
				}
				catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
				{
					Framework.Logging.LogException(ex, $"[Voice Recording] session {row.VoiceIntakeSessionId}: the recording step failed; worker 80 tries again.");
				}
			}

			return changed;
		}

		/// <summary>A Completed recording: attach it to the session's call, wait for the call, or give up on it.</summary>
		private async Task<VoiceRecordingOutcomes> TryAttachAsync(VoiceIntakeSession session, DateTime nowUtc, CancellationToken cancellationToken)
		{
			if (!session.CallId.HasValue)
			{
				// No pending call (yet). Guided intake creates it before the closing prompt, so a finished session without one, or a long
				// wait, means the caller hung up first: the recording of a request that was never made is not kept.
				var step = (VoiceIntakeSteps)session.Step;
				var finished = step == VoiceIntakeSteps.Completed || step == VoiceIntakeSteps.Abandoned;
				if (!finished && nowUtc - session.StartedOn < WaitWindow())
					return VoiceRecordingOutcomes.Waiting;

				await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None);
				await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, session.RecordingSid);
				return VoiceRecordingOutcomes.Discarded;
			}

			// The lease: every write after this one holds only while the lease is still this attempt's (a stalled attempt taken over after
			// LeaseMinutes changes nothing, saves nothing and deletes nothing).
			var claim = nowUtc;
			if (!await _sessions.TryClaimRecordingAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Completed, claim,
				nowUtc.AddMinutes(-LeaseMinutes), CancellationToken.None))
				return VoiceRecordingOutcomes.Waiting;

			var attempt = session.RecordingAttempts + 1;
			try
			{
				var call = await _calls.GetCallByIdAsync(session.CallId.Value);
				if (call == null || call.IsDeleted || call.DepartmentId != session.DepartmentId)
				{
					if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None))
						return LeaseLost(session);
					await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, session.RecordingSid);
					return VoiceRecordingOutcomes.Discarded;
				}

				var download = await _provider.DownloadRecordingAsync(session.RecordingSid, Math.Max(1, IntakeConfig.VoiceRecordingMaxBytes), cancellationToken);
				switch (download?.Status ?? VoiceRecordingDownloadStatuses.Unavailable)
				{
					case VoiceRecordingDownloadStatuses.NotFound:
						Framework.Logging.LogError($"[Voice Recording] call {call.CallId}: Twilio has no recording {session.RecordingSid}; nothing attached.");
						if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None))
							return LeaseLost(session);
						await _sessions.MarkRecordingCarrierDeletedAsync(session.VoiceIntakeSessionId, nowUtc, CancellationToken.None);
						return VoiceRecordingOutcomes.Discarded;

					case VoiceRecordingDownloadStatuses.TooLarge:
						Framework.Logging.LogError($"[Voice Recording] call {call.CallId}: recording {session.RecordingSid} is larger than IntakeConfig.VoiceRecordingMaxBytes; kept at Twilio until the session purge.");
						if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Failed, null, CancellationToken.None))
							return LeaseLost(session);
						return VoiceRecordingOutcomes.Failed;

					case VoiceRecordingDownloadStatuses.Downloaded:
						break;

					default:
						return await ReleaseOrFailAsync(session, claim, attempt, call.CallId, "the carrier could not hand it over");
				}

				// The download can take a while: renew the lease right before the save, so a takeover cannot start while this attempt writes
				// the file (and an attempt that already lost it never writes one).
				var renewed = Later(_clock.GetUtcNow().UtcDateTime, claim);
				if (!await _sessions.RenewRecordingClaimAsync(session.VoiceIntakeSessionId, claim, renewed, CancellationToken.None))
					return LeaseLost(session);
				claim = renewed;

				// A file an earlier attempt saved before it died is completed rather than added again: saved once more by its id, which runs
				// the protected-write step that attempt may not have finished (ADP seals the file only after the first insert).
				var fileName = VoiceIntakeRecording.FileName(session);
				var stamp = RecordingStamp(session);
				var existing = await FindAttachedAsync(call.CallId, fileName, download.Data.Length, stamp);
				var saved = await _calls.SaveCallAttachmentAsync(new CallAttachment
				{
					CallAttachmentId = existing?.CallAttachmentId ?? 0,
					CallId = call.CallId,
					CallAttachmentType = (int)CallAttachmentTypes.File,
					FileName = fileName,
					Name = DisplayName(session.RecordingDurationSeconds),
					Data = download.Data,
					Size = download.Data.Length,
					UserId = call.ReportingUserId,
					// When the recorded call began, to the second: the file's identity on a retry even where ADP seals its name.
					Timestamp = stamp
				}, CancellationToken.None);
				var attachmentId = saved?.CallAttachmentId ?? existing?.CallAttachmentId;

				if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Attached, attachmentId, CancellationToken.None))
					return LeaseLost(session);
				Framework.Logging.LogInfo($"[Voice Recording] call {call.CallId}: caller recording attached as file {attachmentId} ({download.Data.Length} bytes).");
				await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, session.RecordingSid);
				return VoiceRecordingOutcomes.Attached;
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				Framework.Logging.LogException(ex, $"[Voice Recording] call {session.CallId}: recording {session.RecordingSid} could not be attached (attempt {attempt}).");
				return await ReleaseOrFailAsync(session, claim, attempt, session.CallId.Value, ex.GetType().Name);
			}
		}

		private async Task<VoiceRecordingOutcomes> ReleaseOrFailAsync(VoiceIntakeSession session, DateTime claim, int attempt, int callId, string reason)
		{
			if (attempt >= Math.Max(1, IntakeConfig.VoiceRecordingMaxAttempts))
			{
				Framework.Logging.LogError($"[Voice Recording] call {callId}: recording {session.RecordingSid} was not attached after {attempt} attempts ({reason}); kept at Twilio until the session purge.");
				if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Failed, null, CancellationToken.None))
					return LeaseLost(session);
				return VoiceRecordingOutcomes.Failed;
			}

			if (!await _sessions.SetClaimedRecordingStateAsync(session.VoiceIntakeSessionId, claim, (int)VoiceIntakeRecordingStates.Completed, null, CancellationToken.None))
				return LeaseLost(session);
			return VoiceRecordingOutcomes.Waiting;
		}

		/// <summary>Another attempt took the lease over: this one stops, and that one finishes the recording.</summary>
		private static VoiceRecordingOutcomes LeaseLost(VoiceIntakeSession session)
		{
			Framework.Logging.LogInfo($"[Voice Recording] session {session.VoiceIntakeSessionId}: the attach lease was taken over by another attempt; this one stops.");
			return VoiceRecordingOutcomes.Waiting;
		}

		/// <summary>
		/// The session's file on the call already, from an earlier attempt that saved it but died before recording it: its name, or (under
		/// ADP, where names are sealed) its size and the exact timestamp the recording is saved with. Nothing else on the call carries that
		/// timestamp: it is the second the call began, before the pending call (and so any other file on it) existed.
		/// </summary>
		private async Task<CallAttachment> FindAttachedAsync(int callId, string fileName, int size, DateTime stamp)
		{
			var files = await _attachments.GetCallDispatchesByCallIdAsync(callId);
			return files?.FirstOrDefault(a => a != null && !a.IsDeleted && a.CallAttachmentType == (int)CallAttachmentTypes.File &&
				(string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase) || (a.Size == size && a.Timestamp == stamp)));
		}

		/// <summary>The recording file's timestamp: when the session began, whole seconds (exact in every engine's datetime type).</summary>
		public static DateTime RecordingStamp(VoiceIntakeSession session) =>
			new DateTime(session.StartedOn.Ticks - session.StartedOn.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

		private static DateTime Later(DateTime a, DateTime b) => a > b ? a : b;

		/// <summary>Deletes Twilio's copy (when the host says so) and records it on the session. True when it is gone.</summary>
		private async Task<bool> DeleteCarrierCopyAsync(string voiceIntakeSessionId, string recordingSid)
		{
			if (string.IsNullOrWhiteSpace(recordingSid) || !IntakeConfig.VoiceRecordingDeleteCarrierCopy)
				return false;

			try
			{
				if (!await _provider.DeleteRecordingAsync(recordingSid, CancellationToken.None))
					return false;

				if (voiceIntakeSessionId != null)
					await _sessions.MarkRecordingCarrierDeletedAsync(voiceIntakeSessionId, _clock.GetUtcNow().UtcDateTime, CancellationToken.None);
				return true;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogException(ex, $"[Voice Recording] recording {recordingSid} could not be deleted at Twilio; worker 80 tries again.");
				return false;
			}
		}

		/// <summary>The file's title on the call: "Caller recording (2:41)".</summary>
		public static string DisplayName(int? durationSeconds)
		{
			if (!durationSeconds.HasValue || durationSeconds.Value <= 0)
				return "Caller recording";

			var length = TimeSpan.FromSeconds(durationSeconds.Value);
			return $"Caller recording ({(int)length.TotalMinutes}:{length.Seconds:00})";
		}

		private static TimeSpan WaitWindow() => TimeSpan.FromMinutes(Math.Max(1, IntakeConfig.VoiceRecordingWaitMinutes));
	}
}
