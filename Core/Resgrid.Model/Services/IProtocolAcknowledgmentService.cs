using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>A new document-backed protocol (single upload, bulk upload, or the move from Documents).</summary>
	public sealed class ProtocolDocumentCreateRequest
	{
		public int Kind { get; set; }
		public int? CategoryId { get; set; }
		public string Title { get; set; }
		public string Number { get; set; }
		public string Code { get; set; }
		public string Description { get; set; }
		public string FileName { get; set; }
		public byte[] Data { get; set; }
		public bool? RequiresAcknowledgment { get; set; }
		public List<ContentAudienceRule> Audience { get; set; } = new List<ContentAudienceRule>();
	}

	/// <summary>One file of a bulk upload: its name, bytes and the folder it came from in a zip (folders become categories).</summary>
	public sealed class ProtocolUploadFile
	{
		public string FileName { get; set; }
		public string Folder { get; set; }
		public byte[] Data { get; set; }
	}

	/// <summary>
	/// Document-backed protocols (P1 R-P5, R-P6): a version whose content is one uploaded PDF or DOCX, with the same lifecycle,
	/// approval, audiences and citations as authored content; its hash is the SHA-256 of the file. Text is extracted on upload
	/// (page-numbered, bounded; large files queued for worker 86) and feeds Unified Search, audience-filtered. Bulk upload lands
	/// files as drafts; "Move to Protocols" turns Documents into document-backed protocols and leaves a redirect.
	/// </summary>
	public interface IProtocolDocumentService
	{
		/// <summary>A new protocol (Draft) whose first draft version is the uploaded file.</summary>
		Task<ProtocolLifecycleResult> CreateFromFileAsync(ProtocolReader reader, ProtocolDocumentCreateRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// A new upload is a new version: the protocol's open draft (started from the version in force when there is none) becomes
		/// document-backed with this file. Refused when the open working copy is in review or approved.
		/// </summary>
		Task<ProtocolLifecycleResult> UploadToDraftAsync(ProtocolReader reader, int protocolId, string fileName, byte[] data, CancellationToken cancellationToken = default);

		/// <summary>The version's primary file with its bytes, when the reader may read that version (R-P4); else null.</summary>
		Task<ProtocolVersionFile> GetReadableFileAsync(ProtocolReader reader, int protocolVersionId);

		/// <summary>The version's primary file (no bytes), its extraction and its pages, for the reading view.</summary>
		Task<ProtocolDocumentView> GetDocumentViewAsync(int departmentId, int protocolVersionId);

		/// <summary>Many files at once into a category as draft document-backed protocols (title and number from the file name).</summary>
		Task<List<ProtocolBulkUploadItem>> BulkUploadAsync(ProtocolReader reader, int kind, int? categoryId, bool foldersAsCategories, IReadOnlyList<ProtocolUploadFile> files,
			CancellationToken cancellationToken = default);

		/// <summary>The files of a zip archive (PDF and DOCX entries only, with their folder), bounded by size and count.</summary>
		List<ProtocolUploadFile> ReadZip(byte[] zip);

		/// <summary>
		/// "Move to Protocols" (R-P6): a document becomes a document-backed protocol of kind Policy or Reference (draft); the document
		/// row stays as the redirect at its old link. Protected (ADP) documents stay in Documents.
		/// </summary>
		Task<ProtocolBulkUploadItem> MoveFromDocumentAsync(ProtocolReader reader, int documentId, int kind, bool canSeeAdminsOnly, CancellationToken cancellationToken = default);

		/// <summary>Moves every document of a Documents category (the same rules per document).</summary>
		Task<List<ProtocolBulkUploadItem>> MoveCategoryFromDocumentsAsync(ProtocolReader reader, string category, int kind, bool canSeeAdminsOnly,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Extracts a file's text now when it is small enough, else queues it for worker 86. Never throws: the upload already
		/// succeeded, and a failed extraction leaves the file searchable by title only.
		/// </summary>
		Task<ProtocolFileText> ExtractOrQueueAsync(int departmentId, int protocolId, ProtocolFileSourceKinds sourceKind, int sourceId, int? protocolVersionId,
			string fileName, string contentType, byte[] data, CancellationToken cancellationToken = default);

		// ---- worker 86 -------------------------------------------------------------------------------------------------------

		/// <summary>Queues PDF and DOCX attachments that have no extraction yet. Returns how many.</summary>
		Task<int> QueueAttachmentExtractionsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Extracts queued files (bounded per file). Returns how many were finished.</summary>
		Task<int> ProcessPendingExtractionsAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>What the reading view shows for a document-backed version.</summary>
	public sealed class ProtocolDocumentView
	{
		public ProtocolVersionFile File { get; set; }
		public ProtocolFileText Text { get; set; }
		public List<ProtocolFileTextPage> Pages { get; set; } = new List<ProtocolFileTextPage>();
	}

	/// <summary>
	/// P2 distribution and acknowledgment (free): assignment on publish and on entering the audience (R-P3, through worker 83),
	/// withdrawal on leaving, minor carries / major re-assigns, read-and-sign at L1 bound to the version hash (L2 when the department
	/// asks), the offline rule (R-P11), quiz-gated acknowledgment, priority bulletins (R-P7), reminders and escalation (worker 86),
	/// the compliance grid. Standards tags and the proof-of-compliance report are LMS (ILmsAccessService, fails closed).
	/// </summary>
	public interface IProtocolAcknowledgmentService
	{
		// ---- assignment ------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Assigns a version that requires acknowledgment to its audience (active members only): on publish, on scheduling (a future
		/// version can be acknowledged from publish), for a bulletin. Idempotent. Returns how many were assigned (carried included).
		/// </summary>
		Task<int> AssignVersionAsync(DispatchProtocol protocol, ProtocolVersion version, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>A version came into force: open rows of the protocol's other versions are superseded, then the version is assigned.</summary>
		Task<int> OnVersionActivatedAsync(DispatchProtocol protocol, ProtocolVersion version, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The protocol was retired, expired or withdrawn: its open rows are withdrawn (signed ones stay).</summary>
		Task<int> OnProtocolRetiredAsync(DispatchProtocol protocol, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 83: one member's memberships changed (R-P3). Assigns what they entered, withdraws what they left. (Added, Withdrawn).</summary>
		Task<Tuple<int, int>> ReevaluateMemberAsync(int departmentId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Worker 83: a department-wide change (a group, role or shift removed). (Added, Withdrawn).</summary>
		Task<Tuple<int, int>> ReevaluateDepartmentAsync(int departmentId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>A protocol's audience changed. (Added, Withdrawn).</summary>
		Task<Tuple<int, int>> ReevaluateProtocolAsync(int departmentId, int protocolId, DateTime utcNow, CancellationToken cancellationToken = default);

		// ---- read-and-sign ---------------------------------------------------------------------------------------------------

		/// <summary>Records that the member opened the version (first time only). Null when they owe no acknowledgment of it.</summary>
		Task<ProtocolAcknowledgment> MarkOpenedAsync(int departmentId, string userId, int protocolVersionId, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Read-and-sign: an L1 signature (L2 when the department requires a step-up) bound to the version's content hash. On the web
		/// the member must have opened the version; offline (Omni, R-P11) the version id and hash they read must still be the one in
		/// force. A quiz-gated version completes when the training is passed. Writes the transcript record.
		/// </summary>
		Task<ProtocolAcknowledgeResult> AcknowledgeAsync(ProtocolReader reader, int protocolVersionId, string contentHashRead, SignatureProof proof, bool offline,
			string statementText = null, CancellationToken cancellationToken = default);

		Task<ProtocolAcknowledgment> GetForVersionAndUserAsync(int departmentId, int protocolVersionId, string userId);

		/// <summary>The member's acknowledgments (outstanding first, in bundle order; completed ones when asked).</summary>
		Task<List<ProtocolAcknowledgmentItem>> GetMyAcknowledgmentsAsync(int departmentId, string userId, bool includeCompleted, DateTime utcNow);

		/// <summary>The member's must-read bulletins not yet acknowledged and still in force (the home card).</summary>
		Task<List<ProtocolAcknowledgmentItem>> GetMustReadAsync(int departmentId, string userId, DateTime utcNow);

		// ---- officers --------------------------------------------------------------------------------------------------------

		/// <summary>
		/// The compliance grid (free): with a protocol, one row per person for the version (default: in force), optionally one group;
		/// without, a line per protocol. Readers who manage acknowledgments (130), approvers and admins only.
		/// </summary>
		Task<ProtocolComplianceGrid> GetComplianceAsync(ProtocolReader reader, int? protocolId, int? protocolVersionId, int? groupId, DateTime utcNow);

		/// <summary>The grid as CSV (free).</summary>
		string ToCsv(ProtocolComplianceGrid grid);

		/// <summary>Per-protocol acknowledgment settings: due days, bundle order, the quiz training, and (LMS only) standards tags.</summary>
		Task<ProtocolLifecycleResult> SaveSettingsAsync(ProtocolReader reader, int protocolId, int? dueDays, int bundleOrder, int? quizTrainingId, string standardsTags,
			CancellationToken cancellationToken = default);

		/// <summary>The proof-of-compliance report by standards tag (LMS). Null when the department has no LMS entitlement (fails closed).</summary>
		Task<ProtocolStandardsReport> GetStandardsReportAsync(ProtocolReader reader, DateTime utcNow);

		// ---- priority bulletins (R-P7) ---------------------------------------------------------------------------------------

		/// <summary>Issues a bulletin straight into force (holders of the publish right), assigns it as must-read and sends a push.</summary>
		Task<ProtocolLifecycleResult> IssueBulletinAsync(ProtocolReader reader, ProtocolBulletinRequest request, CancellationToken cancellationToken = default);

		Task<List<ProtocolBulletinItem>> GetBulletinsAsync(ProtocolReader reader, DateTime utcNow);

		/// <summary>The approval afterwards: an L2 countersign bound to the bulletin's content hash.</summary>
		Task<ProtocolLifecycleResult> CountersignBulletinAsync(ProtocolReader reader, int protocolId, SignatureProof proof, CancellationToken cancellationToken = default);

		/// <summary>The approval afterwards, refused: the bulletin is retired and its open acknowledgments withdrawn.</summary>
		Task<ProtocolLifecycleResult> WithdrawBulletinAsync(ProtocolReader reader, int protocolId, string reason, CancellationToken cancellationToken = default);

		// ---- worker 86 -------------------------------------------------------------------------------------------------------

		/// <summary>Reminders at the due date and after, escalation to the supervisor / group admin. Returns notifications sent.</summary>
		Task<int> SendRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Completes signed, quiz-gated acknowledgments whose training is now passed. Returns how many.</summary>
		Task<int> CompletePassedQuizzesAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>
		/// Completes one signed acknowledgment waiting for its quiz when every quiz it asks for is now passed (the linked training and, with the
		/// LMS, the bank quiz). Writes the transcript record. False when it was not waiting or is still owed.
		/// </summary>
		Task<bool> CompleteIfQuizPassedAsync(int departmentId, int protocolVersionId, string userId, DateTime utcNow, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Direct protocol notifications (push + email through ICommunicationService; EventTypes 57-59 and the P1 approver notice).
	/// Active members only (inactive-member policy); nothing is sent where the department cannot transmit.
	/// </summary>
	public interface IProtocolNotificationService
	{
		Task<int> NotifyAssignedAsync(int departmentId, IReadOnlyCollection<string> userIds, DispatchProtocol protocol, ProtocolVersion version, CancellationToken cancellationToken = default);

		/// <summary>One notice for a member who entered several audiences at once (the onboarding bundle), instead of one per protocol.</summary>
		Task<int> NotifyBundleAsync(int departmentId, string userId, int count, CancellationToken cancellationToken = default);

		Task<int> NotifyReminderAsync(int departmentId, string userId, DispatchProtocol protocol, ProtocolVersion version, ProtocolAcknowledgmentReminderStages stage, DateTime? dueOnUtc,
			CancellationToken cancellationToken = default);

		Task<int> NotifyEscalationAsync(int departmentId, IReadOnlyCollection<string> supervisorUserIds, string memberName, DispatchProtocol protocol, ProtocolVersion version,
			CancellationToken cancellationToken = default);

		/// <summary>P1 open item: the approvers of the step now open (a submit, or an earlier step approved).</summary>
		Task<int> NotifyApproversAsync(int departmentId, DispatchProtocol protocol, ProtocolVersion version, ProtocolVersionApproval step, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// LMS extensions of protocol acknowledgment (§17.2): acknowledgment quizzes drawn from question banks (server-side scoring, keys
	/// never leave the server, an attempt snapshot) and scheduled weekly micro-learning from selected protocols. Implemented in the LMS
	/// layer; this seam speaks baseline shapes only (§17.3). Authoring and new LMS work need the add-on (fails closed); a quiz already owed
	/// stays takeable after a lapse (rule 4), so nobody is stranded in "awaiting quiz".
	/// </summary>
	public interface IProtocolLearningExtensions
	{
		/// <summary>Whether scheduled micro-learning can run for the department (the LMS add-on is active).</summary>
		Task<bool> IsMicroLearningAvailableAsync(int departmentId);

		/// <summary>
		/// Selects the department's micro-learning protocols: exactly <paramref name="protocolIds"/> (published protocols of the reader's
		/// department) take part from now on, and worker 86 sends the weekly question. Officers who manage acknowledgments only; needs the
		/// add-on. Returns how many are selected, or -1 when refused.
		/// </summary>
		Task<int> ScheduleMicroLearningAsync(ProtocolReader reader, IReadOnlyCollection<int> protocolIds, DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Whether a version's acknowledgment quiz can be drawn from a question bank (the LMS add-on is active).</summary>
		Task<bool> IsBankQuizAvailableAsync(int departmentId);

		/// <summary>The protocol's LMS learning settings (defaults when none are stored).</summary>
		Task<ProtocolLearningSettings> GetSettingsAsync(int departmentId, int protocolId);

		/// <summary>The department's question banks the settings page offers.</summary>
		Task<List<ProtocolQuestionBankChoice>> GetBankChoicesAsync(int departmentId);

		/// <summary>The protocol ids of the department's micro-learning selection.</summary>
		Task<List<int>> GetMicroLearningProtocolIdsAsync(int departmentId);

		/// <summary>Saves the protocol's learning settings (holders of Manage Protocol Acknowledgments, approvers, admins; needs the add-on).</summary>
		Task<ProtocolLearningErrors> SaveSettingsAsync(ProtocolReader reader, ProtocolLearningSettings settings, CancellationToken cancellationToken = default);

		/// <summary>Where the member stands on the version's bank quiz (database only, no billing call: worker 86 asks it).</summary>
		Task<ProtocolBankQuizState> GetBankQuizStateAsync(int departmentId, int protocolId, int protocolVersionId, string userId);

		/// <summary>Starts (or resumes) an attempt for a member who owes the version's acknowledgment. The payload carries no key.</summary>
		Task<ProtocolQuizView> StartBankQuizAsync(ProtocolReader reader, int protocolVersionId, string locale, CancellationToken cancellationToken = default);

		/// <summary>Scores an attempt on the server; a pass completes a signed acknowledgment that was waiting for it.</summary>
		Task<ProtocolQuizResult> SubmitBankQuizAsync(ProtocolReader reader, int attemptId, IEnumerable<ProtocolQuizAnswer> answers, CancellationToken cancellationToken = default);

		/// <summary>Worker 86: sends the weekly question to the active audience of each due department's micro-learning protocols. Returns questions sent.</summary>
		Task<int> RunMicroLearningAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>The member's recent weekly questions, newest first.</summary>
		Task<List<ProtocolMicroLearningItem>> GetMyMicroLearningAsync(int departmentId, string userId, int take);

		Task<ProtocolMicroLearningItem> GetMicroLearningAsync(int departmentId, string userId, int itemId);

		/// <summary>Answers a weekly question once (scored on the server); the answer shows the correct keys and the explanation.</summary>
		Task<ProtocolMicroLearningItem> AnswerMicroLearningAsync(int departmentId, string userId, int itemId, IEnumerable<string> keys, CancellationToken cancellationToken = default);
	}
}
