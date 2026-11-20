using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Intake-side operations on SMS conversations that are not part of the desk contract (<see cref="IIntakeConversationService"/>):
	/// the intake pipeline's lookups, the worker's Enhanced AI turn, the transcript the AI enrichment reads, and the conversation view
	/// with a protected-data grant (v4). Implemented by the same class as <see cref="IIntakeConversationService"/>.
	/// </summary>
	public interface IIntakeConversationOperations
	{
		/// <summary>The participant's open thread with the department (the dedicated-number paths: a second text joins it, STOP closes it).</summary>
		Task<IntakeConversation> GetOpenForParticipantAsync(int departmentId, IntakeChannelTypes channelType, string participantAddress);

		/// <summary>
		/// Worker (aidispatchtriage queue, <see cref="AiDispatchChannels.IntakeConversation"/>): takes the thread's next Enhanced AI turn
		/// and texts the caller, or hands the thread to a dispatcher. Returns the outcome; never throws for model or policy failures.
		/// </summary>
		Task<string> ProcessAssistantTurnAsync(AiDispatchQueueItem item, CancellationToken cancellationToken = default);

		/// <summary>
		/// The texter's own messages, oldest first, one per line (read from the call's notes) for the AI enrichment of the pending call;
		/// null when the thread is not the department's or has nothing readable (protected notes are never returned).
		/// </summary>
		Task<string> GetParticipantTranscriptAsync(int departmentId, string conversationId, int maxCharacters);

		/// <summary>As <see cref="IIntakeConversationService.GetViewAsync"/>, decrypting protected note text with the viewer's grant.</summary>
		Task<IntakeConversationView> GetViewAsync(int departmentId, string conversationId, string viewerUserId, string grantToken,
			CancellationToken cancellationToken = default);
	}

	/// <summary>Outcomes of an Enhanced AI conversation turn (strings, as AI dispatch outcomes are).</summary>
	public static class IntakeAssistantOutcomes
	{
		public const string Answered = "Answered";
		public const string Disabled = "Disabled";
		public const string NotEntitled = "NotEntitled";
		public const string Unconfigured = "Unconfigured";
		public const string ProtectionUnsupported = "ProtectionUnsupported";
		public const string Busy = "Busy";
		public const string BudgetExhausted = "BudgetExhausted";
		public const string Unavailable = "Unavailable";
		public const string InvalidOutput = "InvalidOutput";
	}

	/// <summary>One line of a conversation as the model reads it.</summary>
	public sealed class IntakeAssistantLine
	{
		/// <summary>True for the texter; false for anything sent to them (Resgrid, Enhanced AI or a dispatcher).</summary>
		public bool FromCaller { get; set; }

		public string Text { get; set; }
	}

	public sealed class IntakeAssistantRequest
	{
		public int DepartmentId { get; set; }

		public string DepartmentName { get; set; }

		/// <summary>The conversation so far, oldest first.</summary>
		public List<IntakeAssistantLine> Lines { get; set; } = new List<IntakeAssistantLine>();

		/// <summary>This turn's number (1 = the first question after the welcome).</summary>
		public int Turn { get; set; }

		public int MaxTurns { get; set; }
	}

	/// <summary>The model's validated reply (Answered), or the outcome that stopped it.</summary>
	public sealed class IntakeAssistantAnswer
	{
		public string Outcome { get; set; }

		/// <summary>The next text to the caller (one question); null when <see cref="Done"/> or <see cref="WantsDispatcher"/> with nothing to say.</summary>
		public string ReplyText { get; set; }

		/// <summary>The model has what dispatch needs (situation, location, injuries/hazards, name/callback) or the caller has no more.</summary>
		public bool Done { get; set; }

		/// <summary>A person should take over (the caller asked for one, immediate danger, something the model must not handle).</summary>
		public bool WantsDispatcher { get; set; }
	}

	/// <summary>
	/// Enhanced AI for intake SMS conversations (plan §8 D16): entitlement (Ai.Intake, paid or operator-listed self-hosted), never for
	/// departments in Advanced Data Protection (D9), the operator's self-hosted model only, background admission and metering.
	/// </summary>
	public interface IIntakeConversationAssistant
	{
		/// <summary>Null when the department's conversations may be carried by Enhanced AI now; else the <see cref="IntakeAssistantOutcomes"/> value that blocks it.</summary>
		Task<string> CheckAsync(int departmentId);

		/// <summary>Asks the model for the next text. Never throws for model or policy failures.</summary>
		Task<IntakeAssistantAnswer> AskAsync(IntakeAssistantRequest request, CancellationToken cancellationToken = default);
	}
}
