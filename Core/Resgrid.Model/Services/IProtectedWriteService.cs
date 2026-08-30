using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Attended/workload protected-write pipeline (plan sections 3.3, 19.2, 20.3): when a
	/// department is in an encrypt-new-writes state, every cataloged plaintext value on the entity
	/// is broker-encrypted IN PLACE before the caller persists it — new writes never land plaintext
	/// in a protected department's rows, and a failure blocks the write (fail closed) rather than
	/// degrading to plaintext.
	///
	/// Grant semantics: an ATTENDED caller (workloadCaller false) must hold a currently-valid
	/// Protected Data Grant — protected writes require recent MFA. A WORKLOAD caller
	/// (workloadCaller true: system API key, text-to-call, workers) encrypts without a grant
	/// through the broker's encrypt-only workload lane — encryption discloses nothing, and dispatch
	/// intake must never be blocked by a missing step-up.
	///
	/// Round-tripped REDACTED sentinels: on edits, a field submitted as the exact REDACTED
	/// placeholder is restored from the existing stored value (the client never saw the plaintext,
	/// so the sentinel means "unchanged"), never persisted literally.
	///
	/// Registered in ServicesModule beside IProtectedReadService: CallsService's write safety net
	/// resolves it in every host, so worker/service-internal writers (weather notes, chatbot calls,
	/// email import) are covered through the workload lane without per-caller wiring.
	/// </summary>
	public interface IProtectedWriteService
	{
		/// <summary>
		/// Cheap pre-persist gate (no broker call): enforcement state plus, for attended callers,
		/// grant validity. Lets create endpoints refuse BEFORE inserting the transient plaintext
		/// row that two-phase encryption requires (identity PKs only exist after insert, and the
		/// row key is an AAD component).
		/// </summary>
		Task<ProtectedWriteResult> PreflightWriteAsync(int departmentId, string grantToken, string userId,
			bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares a call for persistence. existingCall (the currently stored row) enables
		/// REDACTED-sentinel restoration on edits; pass null for creates.
		/// </summary>
		Task<ProtectedWriteResult> PrepareCallWriteAsync(int departmentId, Call call, Call existingCall,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a call note (text fields; coordinate companions handled here too).</summary>
		Task<ProtectedWriteResult> PrepareCallNoteWriteAsync(int departmentId, CallNote note,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a call attachment (text fields, coordinate companions, and the binary payload).</summary>
		Task<ProtectedWriteResult> PrepareCallAttachmentWriteAsync(int departmentId, CallAttachment attachment,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares a contact (21 cataloged text fields plus the binary Image payload).
		/// existingContact enables REDACTED-sentinel restoration on edits; pass null for creates.
		/// </summary>
		Task<ProtectedWriteResult> PrepareContactWriteAsync(int departmentId, Contact contact, Contact existingContact,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares a personnel certification (six cataloged text fields plus the binary document).
		/// existingCertification enables REDACTED-sentinel restoration on edits, so an admin editing
		/// a member's certification without a grant cannot save the placeholder over the real value;
		/// pass null for creates.
		/// </summary>
		Task<ProtectedWriteResult> PrepareCertificationWriteAsync(int departmentId,
			PersonnelCertification certification, PersonnelCertification existingCertification,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a linked-call reference note (callreferences.note).</summary>
		Task<ProtectedWriteResult> PrepareCallReferenceWriteAsync(int departmentId, CallReference reference,
			CallReference existingReference, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a member message (messages.subject/body).</summary>
		Task<ProtectedWriteResult> PrepareMessageWriteAsync(int departmentId, Message message,
			string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a message recipient row (response/note plus companion coordinates).</summary>
		Task<ProtectedWriteResult> PrepareMessageRecipientWriteAsync(int departmentId, MessageRecipient recipient,
			string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a moderation request (the verbatim copy of the reported content).</summary>
		Task<ProtectedWriteResult> PrepareModerationRequestWriteAsync(int departmentId, ModerationRequest request,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a reporter's note on a moderation report.</summary>
		Task<ProtectedWriteResult> PrepareModerationReportWriteAsync(int departmentId, ModerationReport report,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a moderation action (note, details, evidence snapshot).</summary>
		Task<ProtectedWriteResult> PrepareModerationActionWriteAsync(int departmentId, ModerationAction action,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a chat message flag (note, resolution note).</summary>
		Task<ProtectedWriteResult> PrepareChatMessageFlagWriteAsync(int departmentId, ChatMessageFlag flag,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a chat moderation action (reason, details).</summary>
		Task<ProtectedWriteResult> PrepareChatModerationActionWriteAsync(int departmentId, ChatModerationAction action,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a chat export row (the archive payload and any failure text).</summary>
		Task<ProtectedWriteResult> PrepareChatExportWriteAsync(int departmentId, ChatExport export,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a unit log narrative (unitlogs.narrative).</summary>
		Task<ProtectedWriteResult> PrepareUnitLogWriteAsync(int departmentId, UnitLog log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a user state note (userstates.note).</summary>
		Task<ProtectedWriteResult> PrepareUserStateWriteAsync(int departmentId, UserState state,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a calendar item (title, description, location).</summary>
		Task<ProtectedWriteResult> PrepareCalendarItemWriteAsync(int departmentId, CalendarItem item,
			CalendarItem existingItem, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a department document (name, description, filename and the file).</summary>
		Task<ProtectedWriteResult> PrepareDocumentWriteAsync(int departmentId, Document document,
			Document existingDocument, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares stored distribution-list mailbox credentials (section 22.1).</summary>
		Task<ProtectedWriteResult> PrepareDistributionListWriteAsync(int departmentId, DistributionList list,
			DistributionList existingList, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a call log narrative (calllogs.narrative).</summary>
		Task<ProtectedWriteResult> PrepareCallLogWriteAsync(int departmentId, CallLog log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a contact note (text field).</summary>
		Task<ProtectedWriteResult> PrepareContactNoteWriteAsync(int departmentId, ContactNote note,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares a unit state (catalog v2): note, geolocation text, and the typed
		/// latitude/longitude which move into their companion envelope columns.
		/// </summary>
		Task<ProtectedWriteResult> PrepareUnitStateWriteAsync(int departmentId, UnitState state,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares one department-scoped emergency contact (catalog v4).
		///
		/// <paramref name="existingContact"/> is the stored row and is what a REDACTED sentinel is
		/// restored from. The profile page renders these fields as placeholders whenever protection
		/// is enforced, so an edit saved without a grant posts the placeholder straight back; with no
		/// stored row to restore from, the member's next-of-kin details would be nulled instead.
		/// </summary>
		Task<ProtectedWriteResult> PrepareMemberEmergencyContactWriteAsync(int departmentId,
			DepartmentMemberEmergencyContact contact, DepartmentMemberEmergencyContact existingContact,
			string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>
		/// Prepares a department-scoped sensitive personnel row (catalog v1 personnel family).
		///
		/// <paramref name="existingData"/> is the stored row and is what a REDACTED sentinel is
		/// restored from - see the emergency-contact overload; the same grantless-save path would
		/// otherwise null the identification number and both addresses.
		/// </summary>
		Task<ProtectedWriteResult> PrepareMemberSensitiveDataWriteAsync(int departmentId, DepartmentMemberSensitiveData data,
			DepartmentMemberSensitiveData existingData,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares an incident log (catalog v3).</summary>
		Task<ProtectedWriteResult> PrepareLogWriteAsync(int departmentId, Log log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a user-defined field value (catalog v2).</summary>
		Task<ProtectedWriteResult> PrepareUdfFieldValueWriteAsync(int departmentId, UdfFieldValue value,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);
	}
}
