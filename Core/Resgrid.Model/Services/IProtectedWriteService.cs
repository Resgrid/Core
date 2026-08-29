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

		/// <summary>Prepares one department-scoped emergency contact (catalog v4).</summary>
		Task<ProtectedWriteResult> PrepareMemberEmergencyContactWriteAsync(int departmentId,
			DepartmentMemberEmergencyContact contact, string grantToken, string userId, bool workloadCaller,
			CancellationToken cancellationToken = default);

		/// <summary>Prepares a department-scoped sensitive personnel row (catalog v1 personnel family).</summary>
		Task<ProtectedWriteResult> PrepareMemberSensitiveDataWriteAsync(int departmentId, DepartmentMemberSensitiveData data,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares an incident log (catalog v3).</summary>
		Task<ProtectedWriteResult> PrepareLogWriteAsync(int departmentId, Log log,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);

		/// <summary>Prepares a user-defined field value (catalog v2).</summary>
		Task<ProtectedWriteResult> PrepareUdfFieldValueWriteAsync(int departmentId, UdfFieldValue value,
			string grantToken, string userId, bool workloadCaller, CancellationToken cancellationToken = default);
	}
}
