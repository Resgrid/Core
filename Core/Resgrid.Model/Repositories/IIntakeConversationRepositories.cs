using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Intake SMS conversations (IntakeConversations, M0325; plan §8). Every state change is one conditional statement, so racing
	/// webhooks, dispatchers and the worker never both win: the open-thread insert, connect, release, the AI turn claim and close.
	/// </summary>
	public interface IIntakeConversationsRepository : IRepository<IntakeConversation>
	{
		/// <summary>
		/// Inserts the thread unless the participant already has an open one with the department on that transport (the
		/// UX_IntakeConversations_Open index; single statement, no unique-violation noise). True when this call inserted it.
		/// </summary>
		Task<bool> TryInsertOpenAsync(IntakeConversation conversation, CancellationToken cancellationToken = default);

		/// <summary>The participant's open thread with the department on that transport, or null.</summary>
		Task<IntakeConversation> GetOpenForParticipantAsync(int departmentId, int channelType, string participantAddress);

		/// <summary>The participant's most recently active open thread (any department) active at or after <paramref name="activeSince"/>.</summary>
		Task<IntakeConversation> GetOpenForReplyAsync(int channelType, string participantAddress, DateTime activeSince);

		/// <summary>The newest thread (open or closed) feeding the call.</summary>
		Task<IntakeConversation> GetLatestForCallAsync(int departmentId, int callId);

		/// <summary>Open threads with no activity since <paramref name="idleBefore"/>, oldest first.</summary>
		Task<List<IntakeConversation>> GetOpenIdleAsync(DateTime idleBefore, int take);

		/// <summary>Open threads whose call is missing, deleted or no longer in one of <paramref name="openCallStates"/>.</summary>
		Task<List<IntakeConversation>> GetOpenWithEndedCallAsync(int[] openCallStates, int take);

		/// <summary>
		/// Assigns the thread to <paramref name="userId"/> (Handler Dispatcher) when it is open and assigned to
		/// <paramref name="expectedAssignedUserId"/> (null = nobody). True when this call assigned it.
		/// </summary>
		Task<bool> TryConnectAsync(string conversationId, int departmentId, string userId, string expectedAssignedUserId, DateTime assignedOn,
			CancellationToken cancellationToken = default);

		/// <summary>Unassigns <paramref name="userId"/> and sets <paramref name="handler"/> when the thread is open and theirs.</summary>
		Task<bool> TryReleaseAsync(string conversationId, int departmentId, string userId, int handler, CancellationToken cancellationToken = default);

		/// <summary>Moves an open thread from <paramref name="expectedHandler"/> to <paramref name="handler"/>; with
		/// <paramref name="exhaustAssistantTurns"/> AssistantTurns becomes at least <paramref name="maxAssistantTurns"/> (AI is done for good).</summary>
		Task<bool> TrySetHandlerAsync(string conversationId, int departmentId, int expectedHandler, int handler, bool exhaustAssistantTurns, int maxAssistantTurns,
			CancellationToken cancellationToken = default);

		/// <summary>Takes the next Enhanced AI turn: AssistantTurns + 1 when the thread is open, AI-handled and still at <paramref name="expectedTurns"/>.</summary>
		Task<bool> TryClaimAssistantTurnAsync(string conversationId, int departmentId, int expectedTurns, CancellationToken cancellationToken = default);

		/// <summary>Closes an open thread (exactly one caller wins). <paramref name="optedOut"/> sets OptedOut too.</summary>
		Task<bool> TryCloseAsync(string conversationId, int departmentId, string reason, bool optedOut, DateTime closedOn, CancellationToken cancellationToken = default);

		/// <summary>
		/// Closes an open thread that is still idle: only while its LastActivityOn is before <paramref name="idleBefore"/>, so a message that
		/// arrived after the sweep read the thread keeps it open.
		/// </summary>
		Task<bool> TryCloseIdleAsync(string conversationId, int departmentId, string reason, DateTime idleBefore, DateTime closedOn, CancellationToken cancellationToken = default);

		/// <summary>Records activity (and an inbound message's time when <paramref name="lastInboundOn"/> is set); never moves time backwards.</summary>
		Task TouchAsync(string conversationId, int departmentId, DateTime lastActivityOn, DateTime? lastInboundOn, CancellationToken cancellationToken = default);

		Task<bool> SetDeskItemAsync(string conversationId, int departmentId, string deskItemId, CancellationToken cancellationToken = default);
	}

	/// <summary>Delivery log of intake conversation messages (IntakeConversationMessages, M0325). The text is the linked call note.</summary>
	public interface IIntakeConversationMessagesRepository : IRepository<IntakeConversationMessage>
	{
		/// <summary>
		/// Inserts an inbound message unless the conversation already has one with that provider id (UX_IntakeConversationMessages_Provider;
		/// single statement). True when this call inserted it. Rows without a provider id always insert.
		/// </summary>
		Task<bool> TryInsertInboundAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default);

		/// <summary>Inserts an outbound (or any) message row.</summary>
		Task InsertMessageAsync(IntakeConversationMessage message, CancellationToken cancellationToken = default);

		/// <summary>The conversation's messages, oldest first.</summary>
		Task<List<IntakeConversationMessage>> GetForConversationAsync(int departmentId, string conversationId);

		/// <summary>Writes a send's outcome (status, carrier id, error, time).</summary>
		Task<bool> UpdateDeliveryAsync(string messageId, int departmentId, int status, string providerMessageId, string error, DateTime? sentOn,
			CancellationToken cancellationToken = default);

		/// <summary>Links the call note that holds the message text.</summary>
		Task<bool> SetCallNoteAsync(string messageId, int departmentId, int callNoteId, CancellationToken cancellationToken = default);
	}
}
