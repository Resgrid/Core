using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>Intake channels (IntakeChannels, M0323).</summary>
	public interface IIntakeChannelsRepository : IRepository<IntakeChannel>
	{
		/// <summary>The department's channels that are not deleted.</summary>
		Task<List<IntakeChannel>> GetLiveForDepartmentAsync(int departmentId);

		/// <summary>The live (not deleted) channel on this transport and normalized address, enabled or not, any department.</summary>
		Task<IntakeChannel> GetLiveByAddressAsync(int channelType, string address);
	}

	/// <summary>Intake messages (IntakeMessages, M0323): the per-message claim and audit row.</summary>
	public interface IIntakeMessagesRepository : IRepository<IntakeMessage>
	{
		/// <summary>
		/// Inserts the claim unless the channel already has a row for the provider message id (single statement, no unique-violation
		/// noise). True when this call inserted it. Rows without a channel or provider id always insert.
		/// </summary>
		Task<bool> TryClaimAsync(IntakeMessage message, CancellationToken cancellationToken = default);

		Task<IntakeMessage> GetByProviderMessageIdAsync(string intakeChannelId, string providerMessageId);

		/// <summary>
		/// Takes over a claim still in <paramref name="expectedState"/> with no call (a failed or abandoned attempt), so the
		/// provider's retry processes it once. A Received claim is taken only while it is still older than
		/// <paramref name="abandonedBefore"/>; the takeover restamps ReceivedOn with <paramref name="now"/>, so a second retry that
		/// read the same row fails and the new owner gets a fresh abandonment window. True when this call took it.
		/// </summary>
		Task<bool> TryReclaimAsync(string intakeMessageId, int expectedState, DateTime now, DateTime abandonedBefore, CancellationToken cancellationToken = default);

		/// <summary>
		/// A call an abandoned claim saved before its process died (so the claim never got the call id): the department's newest
		/// intake call (<paramref name="callSources"/>) carrying <paramref name="sourceIdentifier"/>, logged at or after
		/// <paramref name="loggedSince"/> and not recorded on any intake message. Null when there is none.
		/// </summary>
		Task<int?> GetUnclaimedIntakeCallIdAsync(int departmentId, string sourceIdentifier, int[] callSources, DateTime loggedSince,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes the outcome of a claimed message. A call already recorded on the row is never cleared or replaced by another (false
		/// then): a late completion from an attempt that lost its claim cannot unlink the call the new owner recorded.
		/// </summary>
		Task<bool> CompleteAsync(string intakeMessageId, int state, int? callId, string reason, DateTime completedOn, CancellationToken cancellationToken = default);

		/// <summary>
		/// Gives back a takeover (<see cref="TryReclaimAsync"/>) whose saved-call lookup failed: ReceivedOn goes back to
		/// <paramref name="receivedOn"/> (the abandoned attempt's) while the row is still Received with no call, so the next delivery
		/// takes it over at once and repeats the lookup instead of waiting out a fresh abandonment window. Only the caller's own takeover
		/// moves: the row must still carry <paramref name="claimedOn"/> (the ReceivedOn its <see cref="TryReclaimAsync"/> wrote), so an attempt
		/// that stalled past its lease never gives back a newer owner's takeover.
		/// </summary>
		Task<bool> ReleaseReclaimAsync(string intakeMessageId, DateTime claimedOn, DateTime receivedOn, CancellationToken cancellationToken = default);

		/// <summary>Rows for the department and transport received in [from, before), excluding the given states.</summary>
		Task<int> CountAsync(int departmentId, int channelType, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>Per transport counts for the department received in [from, before), excluding the given states.</summary>
		Task<Dictionary<int, int>> CountByTypeAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>Per channel counts for the department received in [from, before), excluding the given states.</summary>
		Task<Dictionary<string, int>> CountByChannelAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>The rows that created these calls (State CallCreated).</summary>
		Task<List<IntakeMessage>> GetByCallIdsAsync(int departmentId, IEnumerable<int> callIds);

		/// <summary>
		/// The department's rows from <paramref name="sender"/> (as recorded) on <paramref name="channelTypes"/> that created a call and
		/// were received at or after <paramref name="since"/>, newest first (M0326 per-sender limit). The limit passes the public phone
		/// lines, so a webhook or chat row whose sender label happens to be a phone number never counts toward a texter's or caller's
		/// hour or receives their notes.
		/// </summary>
		Task<List<IntakeMessage>> GetCallsCreatedBySenderSinceAsync(int departmentId, string sender, DateTime since, int[] channelTypes);

		/// <summary>
		/// Sets the screening of the row that created <paramref name="callId"/> (M0326), only while the row is unscreened or holds one of
		/// <paramref name="replaceable"/>, so an automatic screening never overwrites a dispatcher's dismissal. True when a row changed.
		/// </summary>
		Task<bool> SetScreeningAsync(int departmentId, int callId, int screening, int[] replaceable, string userId, DateTime screenedOn,
			CancellationToken cancellationToken = default);

		Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default);
	}

	/// <summary>Senders blocked from a department's intake channels (IntakeBlockedSenders, M0326).</summary>
	public interface IIntakeBlockedSendersRepository : IRepository<IntakeBlockedSender>
	{
		/// <summary>The department's block of <paramref name="sender"/> (normalized) that applies at <paramref name="nowUtc"/>, else null.</summary>
		Task<IntakeBlockedSender> GetInForceAsync(int departmentId, string sender, DateTime nowUtc);

		/// <summary>The department's row for <paramref name="sender"/> that has not been lifted (it may have expired), else null.</summary>
		Task<IntakeBlockedSender> GetCurrentAsync(int departmentId, string sender);

		/// <summary>The department's rows that have not been lifted, newest first (expired ones included).</summary>
		Task<List<IntakeBlockedSender>> GetCurrentForDepartmentAsync(int departmentId);

		/// <summary>
		/// Inserts the block unless the department already has a row for the sender that has not been lifted (one conditional statement).
		/// True when this call inserted it.
		/// </summary>
		Task<bool> TryInsertAsync(IntakeBlockedSender block, CancellationToken cancellationToken = default);

		/// <summary>
		/// Renews a row that has not been lifted: who blocked it, when, why and from which call, and an expiry that only moves later
		/// (decided against the stored row in the same statement, so a renewal that read a stale row cannot shorten it; a row that never
		/// expires stays so, and a null expiry makes it permanent).
		/// </summary>
		Task<bool> RenewAsync(IntakeBlockedSender block, CancellationToken cancellationToken = default);

		/// <summary>Lifts the department's block (sets RemovedOn / RemovedByUserId). True when a standing row changed.</summary>
		Task<bool> RemoveAsync(int departmentId, string intakeBlockedSenderId, string userId, DateTime removedOn, CancellationToken cancellationToken = default);

		/// <summary>Deletes rows lifted, or expired, before <paramref name="before"/>.</summary>
		Task<int> PurgeEndedBeforeAsync(DateTime before, CancellationToken cancellationToken = default);
	}

	/// <summary>Voice intake sessions (VoiceIntakeSessions, M0323), one per phone call.</summary>
	public interface IVoiceIntakeSessionsRepository : IRepository<VoiceIntakeSession>
	{
		Task<VoiceIntakeSession> GetByProviderCallIdAsync(string providerCallId);

		/// <summary>Inserts the session unless one exists for the CallSid. True when this call inserted it.</summary>
		Task<bool> TryInsertAsync(VoiceIntakeSession session, CancellationToken cancellationToken = default);

		/// <summary>
		/// Writes step, answers, retries, call and completion of the session, only while the stored row is still at <paramref name="expectedStep"/>
		/// and <paramref name="expectedRetries"/> and not completed (the state the caller read). False when another write got there first.
		/// </summary>
		Task<bool> UpdateProgressAsync(VoiceIntakeSession session, int expectedStep, int expectedRetries, CancellationToken cancellationToken = default);

		Task<int> CountStartedAsync(int departmentId, DateTime from, DateTime before);

		/// <summary>Deletes sessions started before the cutoff (their transcripts with them).</summary>
		Task<int> PurgeStartedBeforeAsync(DateTime before, CancellationToken cancellationToken = default);

		// Call recording (M0332). Each write touches only the Recording* columns, and only from the state it expects, so the guided
		// questions' progress writes and the recording's callback / worker never overwrite each other.

		/// <summary>None → Requested for the call's session. True for the one caller that may start the recording.</summary>
		Task<bool> TryBeginRecordingAsync(string providerCallId, CancellationToken cancellationToken = default);

		/// <summary>Stores the sid the carrier answered with on the call's session, unless one is stored already.</summary>
		Task<bool> SetRecordingSidAsync(string providerCallId, string recordingSid, CancellationToken cancellationToken = default);

		/// <summary>
		/// The carrier's status callback: Requested → <paramref name="state"/> (Completed or Discarded) with the sid and duration. False when the
		/// session is not Requested (a repeated callback, or a recording this service did not start).
		/// </summary>
		Task<bool> CompleteRecordingAsync(string providerCallId, string recordingSid, int? durationSeconds, int state, CancellationToken cancellationToken = default);

		/// <summary>
		/// Takes the attach lease: only while the state is <paramref name="expectedState"/> and no lease newer than <paramref name="staleBefore"/>
		/// is held; counts an attempt. False when another attempt holds it or the state moved on.
		/// </summary>
		Task<bool> TryClaimRecordingAsync(string voiceIntakeSessionId, int expectedState, DateTime claimedOn, DateTime staleBefore,
			CancellationToken cancellationToken = default);

		/// <summary>Ends an attempt: the new state (and the call file), lease released.</summary>
		Task<bool> SetRecordingStateAsync(string voiceIntakeSessionId, int state, int? callAttachmentId, CancellationToken cancellationToken = default);

		Task<bool> MarkRecordingCarrierDeletedAsync(string voiceIntakeSessionId, DateTime deletedOn, CancellationToken cancellationToken = default);

		/// <summary>
		/// Recordings with work left: Requested or Completed, plus (with <paramref name="includeCarrierCopies"/>) Twilio copies still to delete —
		/// Attached or Discarded, and Failed ones of sessions started before <paramref name="purgeBefore"/> (about to be purged). Oldest first,
		/// without the answers.
		/// </summary>
		Task<List<VoiceIntakeSession>> GetRecordingWorkAsync(DateTime purgeBefore, bool includeCarrierCopies, int max, CancellationToken cancellationToken = default);
	}
}
