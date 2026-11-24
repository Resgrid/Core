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
					await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None);
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
				// Not Requested: a repeated callback (the first one moved it on), or a recording the gate never asked for.
				if (session.RecordingState != (int)VoiceIntakeRecordingStates.None)
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
								// The start was never confirmed and no callback came: as far as anything shows, nothing was recorded.
								if (await _sessions.SetRecordingStateAsync(row.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None))
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

			if (!await _sessions.TryClaimRecordingAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Completed, nowUtc,
				nowUtc.AddMinutes(-LeaseMinutes), CancellationToken.None))
				return VoiceRecordingOutcomes.Waiting;

			var attempt = session.RecordingAttempts + 1;
			try
			{
				var call = await _calls.GetCallByIdAsync(session.CallId.Value);
				if (call == null || call.IsDeleted || call.DepartmentId != session.DepartmentId)
				{
					await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None);
					await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, session.RecordingSid);
					return VoiceRecordingOutcomes.Discarded;
				}

				var download = await _provider.DownloadRecordingAsync(session.RecordingSid, Math.Max(1, IntakeConfig.VoiceRecordingMaxBytes), cancellationToken);
				switch (download?.Status ?? VoiceRecordingDownloadStatuses.Unavailable)
				{
					case VoiceRecordingDownloadStatuses.NotFound:
						Framework.Logging.LogError($"[Voice Recording] call {call.CallId}: Twilio has no recording {session.RecordingSid}; nothing attached.");
						await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Discarded, null, CancellationToken.None);
						await _sessions.MarkRecordingCarrierDeletedAsync(session.VoiceIntakeSessionId, nowUtc, CancellationToken.None);
						return VoiceRecordingOutcomes.Discarded;

					case VoiceRecordingDownloadStatuses.TooLarge:
						Framework.Logging.LogError($"[Voice Recording] call {call.CallId}: recording {session.RecordingSid} is larger than IntakeConfig.VoiceRecordingMaxBytes; kept at Twilio until the session purge.");
						await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Failed, null, CancellationToken.None);
						return VoiceRecordingOutcomes.Failed;

					case VoiceRecordingDownloadStatuses.Downloaded:
						break;

					default:
						return await ReleaseOrFailAsync(session, attempt, call.CallId, "the carrier could not hand it over");
				}

				var fileName = VoiceIntakeRecording.FileName(session);
				var existing = attempt > 1 ? await FindAttachedAsync(call.CallId, fileName, download.Data.Length, session.StartedOn) : null;
				var attachmentId = existing?.CallAttachmentId;
				if (!attachmentId.HasValue)
				{
					var saved = await _calls.SaveCallAttachmentAsync(new CallAttachment
					{
						CallId = call.CallId,
						CallAttachmentType = (int)CallAttachmentTypes.File,
						FileName = fileName,
						Name = DisplayName(session.RecordingDurationSeconds),
						Data = download.Data,
						Size = download.Data.Length,
						UserId = call.ReportingUserId,
						Timestamp = nowUtc
					}, CancellationToken.None);
					attachmentId = saved?.CallAttachmentId;
				}

				await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Attached, attachmentId, CancellationToken.None);
				Framework.Logging.LogInfo($"[Voice Recording] call {call.CallId}: caller recording attached as file {attachmentId} ({download.Data.Length} bytes).");
				await DeleteCarrierCopyAsync(session.VoiceIntakeSessionId, session.RecordingSid);
				return VoiceRecordingOutcomes.Attached;
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
			{
				Framework.Logging.LogException(ex, $"[Voice Recording] call {session.CallId}: recording {session.RecordingSid} could not be attached (attempt {attempt}).");
				return await ReleaseOrFailAsync(session, attempt, session.CallId.Value, ex.GetType().Name);
			}
		}

		private async Task<VoiceRecordingOutcomes> ReleaseOrFailAsync(VoiceIntakeSession session, int attempt, int callId, string reason)
		{
			if (attempt >= Math.Max(1, IntakeConfig.VoiceRecordingMaxAttempts))
			{
				Framework.Logging.LogError($"[Voice Recording] call {callId}: recording {session.RecordingSid} was not attached after {attempt} attempts ({reason}); kept at Twilio until the session purge.");
				await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Failed, null, CancellationToken.None);
				return VoiceRecordingOutcomes.Failed;
			}

			await _sessions.SetRecordingStateAsync(session.VoiceIntakeSessionId, (int)VoiceIntakeRecordingStates.Completed, null, CancellationToken.None);
			return VoiceRecordingOutcomes.Waiting;
		}

		/// <summary>
		/// A retried attempt whose earlier one saved the file but died before recording it: the same session's file is on the call already
		/// (its name, or under ADP where names are sealed, the same size saved after the call began).
		/// </summary>
		private async Task<CallAttachment> FindAttachedAsync(int callId, string fileName, int size, DateTime startedOn)
		{
			var files = await _attachments.GetCallDispatchesByCallIdAsync(callId);
			return files?.FirstOrDefault(a => a != null && !a.IsDeleted && a.CallAttachmentType == (int)CallAttachmentTypes.File &&
				(string.Equals(a.FileName, fileName, StringComparison.OrdinalIgnoreCase) || (a.Size == size && a.Timestamp >= startedOn)));
		}

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
