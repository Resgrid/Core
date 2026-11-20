using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Intake SMS conversations (plan §8, M0325). A text to a department's dedicated number opens a thread (after the intake
	/// pipeline created the pending call): Resgrid answers from the master number with the hand-off welcome ("You texted the
	/// dispatch number for {Department} …"), then Enhanced AI asks follow-up questions, or nobody does until a dispatcher connects
	/// ("You are now connected with {FirstName} …"). The texter's replies to the master number come back to the thread while it is
	/// open. Every message is also a note on the call.
	/// </summary>
	public interface IIntakeConversationService
	{
		/// <summary>
		/// The intake pipeline created <paramref name="call"/> from the texter's first message to <paramref name="channel"/>: open the
		/// thread (or join the texter's open thread with this department), record the message, send the welcome, offer it to the
		/// desk routers, and start Enhanced AI when the department has it. Never throws (logs); returns null when nothing was opened.
		/// </summary>
		Task<IntakeConversation> StartFromIntakeAsync(IntakeChannel channel, IntakeEnvelope envelope, Call call, CancellationToken cancellationToken = default);

		/// <summary>
		/// The open thread a text from <paramref name="participantAddress"/> to the master number belongs to (the most recently
		/// active one within IntakeConfig.ConversationReplyWindowHours across departments), or null (then the master number's usual
		/// handling applies: member commands, verification replies…).
		/// </summary>
		Task<IntakeConversation> FindOpenForReplyAsync(IntakeChannelTypes channelType, string participantAddress, DateTime nowUtc);

		/// <summary>
		/// A reply from the texter to the master number (or another text to the dedicated number while the thread is open). Records
		/// it on the call, tells the desk routers, and queues the next Enhanced AI turn when AI handles the thread. STOP closes and
		/// opts out. Idempotent per provider message id. Never throws.
		/// </summary>
		Task<bool> HandleParticipantMessageAsync(IntakeConversation conversation, string providerMessageId, string text, DateTime receivedOnUtc,
			CancellationToken cancellationToken = default);

		Task<IntakeConversation> GetByIdAsync(int departmentId, string conversationId);

		/// <summary>The thread feeding a call (open or closed, newest), or null.</summary>
		Task<IntakeConversation> GetForCallAsync(int departmentId, int callId);

		/// <summary>The thread and its messages (text read from the call's notes through the protected read path).</summary>
		Task<IntakeConversationView> GetViewAsync(int departmentId, string conversationId, string viewerUserId);

		/// <summary>
		/// A dispatcher takes the thread: Handler = Dispatcher, AI goes silent, the texter gets "You are now connected with
		/// {FirstName}. Please describe the situation you are having and your location." A second connect by the same dispatcher is
		/// Done without a second text; another dispatcher holding it is HeldByAnotherDispatcher unless <paramref name="takeOver"/>.
		/// </summary>
		Task<IntakeConversationActionResults> ConnectDispatcherAsync(int departmentId, string conversationId, string userId, bool takeOver = false,
			CancellationToken cancellationToken = default);

		/// <summary>The connected dispatcher texts the texter (connects first when nobody is connected).</summary>
		Task<IntakeConversationActionResults> SendDispatcherMessageAsync(int departmentId, string conversationId, string userId, string text,
			CancellationToken cancellationToken = default);

		/// <summary>The dispatcher steps away: Enhanced AI resumes when available, else Unassisted. No text is sent.</summary>
		Task<IntakeConversationActionResults> ReleaseDispatcherAsync(int departmentId, string conversationId, string userId,
			CancellationToken cancellationToken = default);

		/// <summary>Ends the thread; optionally tells the texter it ended (from the master number).</summary>
		Task<IntakeConversationActionResults> CloseAsync(int departmentId, string conversationId, string userId, string reason, bool notifyParticipant,
			CancellationToken cancellationToken = default);

		/// <summary>Worker 80: closes threads idle past IntakeConfig.ConversationIdleCloseMinutes or whose call was closed/deleted.</summary>
		Task<int> CloseIdleAsync(DateTime nowUtc, CancellationToken cancellationToken = default);

		/// <summary>The desk records its queue item id on the thread.</summary>
		Task SetDeskItemAsync(int departmentId, string conversationId, string deskItemId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Seam for the dispatcher desk (dispatchers-on-duty-plan.md F2): implemented by the desk, registered in ServicesModule; intake
	/// resolves every registration (<c>IEnumerable&lt;IIntakeConversationRouter&gt;</c>) and none means "no desk". Implementations
	/// must not throw and must return quickly (they run inside the Twilio webhook): queue work, don't do it inline.
	/// </summary>
	public interface IIntakeConversationRouter
	{
		/// <summary>A new thread (its pending call already saved). Offered = the desk will offer/queue it to on-duty dispatchers.</summary>
		Task<IntakeConversationRouting> RouteNewConversationAsync(IntakeConversation conversation, Call call, CancellationToken cancellationToken);

		/// <summary>Each later texter message (the desk shows it live, re-alerts the connected dispatcher, or re-offers).</summary>
		Task OnParticipantMessageAsync(IntakeConversation conversation, IntakeConversationMessage message, CancellationToken cancellationToken);

		/// <summary>
		/// The thread closed, whoever closed it (idle sweep, call closed, STOP, a dispatcher on the call screen, or the desk's own
		/// Complete calling CloseAsync): the desk completes its item. Called once per close; must be a no-op on an item already done.
		/// </summary>
		Task OnConversationClosedAsync(IntakeConversation conversation, CancellationToken cancellationToken);

		/// <summary>
		/// A dispatcher connected or took over (<see cref="IIntakeConversationService.ConnectDispatcherAsync"/>, including the
		/// connect inside SendDispatcherMessageAsync), from any surface: the desk's accept, the web call panel, v4. Called after every
		/// successful connect or take-over (not for a repeat connect by the same dispatcher). The desk assigns its item to
		/// <paramref name="userId"/> and stops offering it; idempotent.
		/// </summary>
		Task OnDispatcherConnectedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken);

		/// <summary>
		/// The connected dispatcher released the thread (<see cref="IIntakeConversationService.ReleaseDispatcherAsync"/>) from any
		/// surface: the desk puts its item back in the queue; idempotent.
		/// </summary>
		Task OnDispatcherReleasedAsync(IntakeConversation conversation, string userId, CancellationToken cancellationToken);
	}
}
