using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Lms;

namespace Resgrid.Model.Services
{
	// Training T5 items 4 and 5 (the course studio): AI course drafts from a protocol or SOP, flashcards, and the Resgrid starter course library.
	// LMS services: baseline code never calls these (§17.3); protocol publishing reaches the flashcards only through IContentVersionPublishedHook.

	/// <summary>A protocol an author may draft a course or flashcards from: published, readable, authorable and allowed to AI.</summary>
	public sealed class LmsDraftableProtocol
	{
		public int ProtocolId { get; set; }
		public string Name { get; set; }
		public string Code { get; set; }
		public int VersionId { get; set; }
		public string VersionLabel { get; set; }
	}

	/// <summary>A source the flashcard drafter offers for one course.</summary>
	public sealed class LmsFlashcardSourceChoice
	{
		/// <summary><see cref="LmsFlashcardSourceKinds"/>.</summary>
		public int SourceKind { get; set; }

		/// <summary>The course id, or the protocol id.</summary>
		public int ItemId { get; set; }

		public int VersionId { get; set; }
		public string Title { get; set; }
		public string VersionLabel { get; set; }
	}

	/// <summary>
	/// Course drafts (T5 item 5, §13a "Learning Studio: SOP → course"): Enhanced AI drafts a course (modules, block lessons that each cite their
	/// source sections, a question bank draft) from a published protocol version or an uploaded SOP document's text, into a new DRAFT course
	/// the author edits and submits through the normal approval. Needs both Enhanced AI (K1's knowledge entitlement, fails closed) and the LMS
	/// add-on (§17.2 row "AI"); "no AI" and licensed protocols are never sent; Advanced Data Protection keeps content local; every number the
	/// model writes must appear in the sections it cites; nothing is ever submitted or published. Each draft is logged to AiGenerations.
	/// </summary>
	public interface ILmsCourseDraftService
	{
		/// <summary>Whether the AI course tools are open to the member (both gates, the operator's model, not kept local) and whether they author.</summary>
		Task<LmsStudioStatus> GetStatusAsync(int departmentId, string userId, CancellationToken cancellationToken = default);

		/// <summary>The published protocols the member may draft from (readable, authorable, allowed to AI), by name.</summary>
		Task<List<LmsDraftableProtocol>> GetDraftableProtocolsAsync(int departmentId, string userId);

		Task<LmsCourseDraftResult> DraftAsync(LmsCourseDraftRequest request, CancellationToken cancellationToken = default);

		/// <summary>Where a course came from (AI draft or starter), or null.</summary>
		Task<LmsCourseOrigin> GetOriginAsync(int departmentId, int lmsCourseId);
	}

	/// <summary>
	/// Course flashcards (T5 item 5, §16.4): AI-drafted question/answer cards from the course's version in force or a protocol version the course
	/// is built from (both gates, as for course drafts); an author accepts the ones to keep; learners practise them in the course player (spaced
	/// practice in the browser, never graded and never written as a record). Each card keeps its source version and anchor; a newer version of
	/// that source flags it for review (<see cref="IContentVersionPublishedHook"/>), and a flagged card is hidden from learners until reviewed.
	/// This service holds no learning-record, certification, enrollment or attempt writer.
	/// </summary>
	public interface ILmsFlashcardService
	{
		/// <summary>The course's cards for an author (every status), or empty when the member may not edit the course.</summary>
		Task<List<LmsFlashcard>> GetCardsAsync(int departmentId, string userId, int lmsCourseId);

		/// <summary>The sources the drafter offers: the course's version in force and the protocols it was drafted from or embeds.</summary>
		Task<List<LmsFlashcardSourceChoice>> GetSourceChoicesAsync(int departmentId, string userId, int lmsCourseId);

		/// <summary>Drafts cards from one source (nothing is saved but the audit row).</summary>
		Task<LmsFlashcardDraft> DraftAsync(LmsFlashcardDraftRequest request, CancellationToken cancellationToken = default);

		/// <summary>Saves the drafted cards the author kept, read back from the stored exchange (never from the client).</summary>
		Task<LmsFlashcardResult> AcceptAsync(int departmentId, string userId, int lmsCourseId, string exchangeId, IReadOnlyCollection<int> cardIndexes,
			CancellationToken cancellationToken = default);

		/// <summary>An author's edit of a card's text (numbers must still appear in the cited section).</summary>
		Task<LmsFlashcardResult> SaveAsync(int departmentId, string userId, int lmsFlashcardId, string front, string back, CancellationToken cancellationToken = default);

		/// <summary>Re-confirms a flagged card against its source's version in force (its section must still be there and agree).</summary>
		Task<LmsFlashcardResult> ReviewAsync(int departmentId, string userId, int lmsFlashcardId, CancellationToken cancellationToken = default);

		Task<LmsFlashcardResult> RemoveAsync(int departmentId, string userId, int lmsFlashcardId, CancellationToken cancellationToken = default);

		/// <summary>
		/// The learner's deck for one of their enrollments: the course's active cards whose source version is still in force (and, for a protocol
		/// card, a protocol the learner may read). Empty while either gate is closed. Reads only; a card whose source has moved on is flagged.
		/// </summary>
		Task<List<LmsFlashcardDeckCard>> GetDeckAsync(int departmentId, string userId, int lmsEnrollmentId, CancellationToken cancellationToken = default);
	}

	/// <summary>The Resgrid starter course library (T5 item 4): Resgrid-authored starters and documented partner slots.</summary>
	public interface ILmsStarterCourseService
	{
		/// <summary>Every starter and partner slot (the embedded catalog).</summary>
		IReadOnlyList<LmsStarterCourseManifest> GetCatalog();

		/// <summary>Every starter and partner slot in a language (round 9C: machine-translated variants, English when none ships).</summary>
		IReadOnlyList<LmsStarterCourseManifest> GetCatalog(string culture);

		/// <summary>
		/// Clones a starter into the department as a new DRAFT course (modules, block lessons, a question bank and a closing check) with its
		/// attribution (source, licence, revision) recorded and shown on the course. Needs the LMS authoring gate (Manage Courses and the add-on).
		/// </summary>
		Task<LmsCourseDraftResult> CloneAsync(LmsActor actor, string starterKey, string locale, CancellationToken cancellationToken = default);
	}
}
