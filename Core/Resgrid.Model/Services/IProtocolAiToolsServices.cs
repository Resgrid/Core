using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	// Enhanced AI on protocols (round 7A): every tool here needs only the Enhanced AI add-on with knowledge answers (IAiKnowledgeService.
	// IsEntitledAsync, failing closed), never the LMS. "No AI" and licensed protocols are never sent; Advanced Data Protection keeps content
	// local (no model call); every model output is logged to AiGenerations and validated against the source (K1's number-in-source rule); nothing
	// is saved until an author accepts it. Baseline services: they never reference an LMS course type (§17.3 separation guard).

	/// <summary>
	/// Protocol flashcards: AI-drafted question/answer cards from a published protocol's version in force, kept only when a protocol author or
	/// approver accepts them, practised on the protocol by members who may read it (spacing in the browser; never graded or recorded). Each card
	/// cites one section and its version; a newer version going into force flags it for review (<see cref="IContentVersionPublishedHook"/>) and a
	/// flagged card is hidden until reviewed. This service holds no learning-record, certification, enrollment or attempt writer.
	/// </summary>
	public interface IProtocolFlashcardService
	{
		/// <summary>Whether the drafting tools are open to the member for this protocol (Enhanced AI, an author, not "no AI", not kept local).</summary>
		Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default);

		/// <summary>The protocol's cards for an author (every status), or empty when the member may not author the protocol.</summary>
		Task<List<ProtocolFlashcard>> GetCardsAsync(int departmentId, string userId, int protocolId);

		/// <summary>Drafts cards from the protocol's version in force (nothing is saved but the audit row).</summary>
		Task<ProtocolFlashcardDraft> DraftAsync(ProtocolFlashcardDraftRequest request, CancellationToken cancellationToken = default);

		/// <summary>Saves the drafted cards the author kept, read back from the stored exchange (never from the client).</summary>
		Task<ProtocolFlashcardResult> AcceptAsync(int departmentId, string userId, int protocolId, string exchangeId, IReadOnlyCollection<int> cardIndexes,
			CancellationToken cancellationToken = default);

		/// <summary>An author's edit of a card's text (every number must still appear in the cited section).</summary>
		Task<ProtocolFlashcardResult> SaveAsync(int departmentId, string userId, int protocolFlashcardId, string front, string back, CancellationToken cancellationToken = default);

		/// <summary>Re-confirms a flagged card against the version in force (its section must still be there and agree).</summary>
		Task<ProtocolFlashcardResult> ReviewAsync(int departmentId, string userId, int protocolFlashcardId, CancellationToken cancellationToken = default);

		Task<ProtocolFlashcardResult> RemoveAsync(int departmentId, string userId, int protocolFlashcardId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The member's practice deck for a protocol they may read: its active cards drafted from the version in force. Empty while Enhanced AI is
		/// off, for a protocol now "no AI", or when the member may not read the protocol. Reads only.
		/// </summary>
		Task<List<ProtocolFlashcardDeckCard>> GetDeckAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// "Draft a training from this protocol": Enhanced AI drafts a baseline Training (not an LMS course) from a published protocol version: the
	/// training text (the model's short introduction and key points per part, then the cited sections' own text and a citation line to the
	/// version and anchor) and a single-choice quiz with explanations. The author reviews the draft; accepting it creates the training unassigned
	/// and not open to members (a draft the author finishes, assigns or uses for the protocol's quiz-gated acknowledgment). Needs the protocol
	/// author right and the right to create trainings.
	/// </summary>
	public interface IProtocolTrainingDraftService
	{
		Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolId, CancellationToken cancellationToken = default);

		/// <summary>Drafts the training (nothing is saved but the audit row).</summary>
		Task<ProtocolTrainingDraft> DraftAsync(ProtocolTrainingDraftRequest request, CancellationToken cancellationToken = default);

		/// <summary>Re-reads the stored draft (never the client's copy), checks it against the version in force again and creates the training.</summary>
		Task<ProtocolTrainingDraftResult> CreateAsync(int departmentId, string userId, int protocolId, string exchangeId, string title, string locale,
			CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// "Draft translation with AI" on a draft translation of a locked protocol version: Enhanced AI translates only the translatable wording of
	/// each block (fields still in the source wording) into the translation's language; doses, medication numbers, links, attachments and the
	/// structure always stay the source's, and a drafted field is kept only when every number of its source appears unchanged, none is added,
	/// and its markup and links are the source's. Nothing is saved: the author reviews the drafted wording in the editor and saves the draft, which
	/// still needs its normal L2 approval.
	/// </summary>
	public interface IProtocolTranslationDraftService
	{
		Task<ProtocolAiStatus> GetStatusAsync(int departmentId, string userId, int protocolVersionTranslationId, CancellationToken cancellationToken = default);

		Task<ProtocolTranslationDraft> DraftAsync(int departmentId, string userId, int protocolVersionTranslationId, CancellationToken cancellationToken = default);
	}
}
