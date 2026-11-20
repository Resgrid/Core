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

		/// <summary>Writes the outcome of a claimed message.</summary>
		Task<bool> CompleteAsync(string intakeMessageId, int state, int? callId, string reason, DateTime completedOn, CancellationToken cancellationToken = default);

		/// <summary>Rows for the department and transport received in [from, before), excluding the given states.</summary>
		Task<int> CountAsync(int departmentId, int channelType, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>Per transport counts for the department received in [from, before), excluding the given states.</summary>
		Task<Dictionary<int, int>> CountByTypeAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>Per channel counts for the department received in [from, before), excluding the given states.</summary>
		Task<Dictionary<string, int>> CountByChannelAsync(int departmentId, DateTime from, DateTime before, int[] excludedStates);

		/// <summary>The rows that created these calls (State CallCreated).</summary>
		Task<List<IntakeMessage>> GetByCallIdsAsync(int departmentId, IEnumerable<int> callIds);

		Task<int> PurgeReceivedBeforeAsync(DateTime before, CancellationToken cancellationToken = default);
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
	}
}
