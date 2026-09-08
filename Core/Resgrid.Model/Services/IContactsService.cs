using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface for managing contacts, contact categories, contact notes and contact note types within a department.
	/// Provides methods for CRUD operations and retrieval of contact-related data.
	/// </summary>
	public interface IContactsService
	{
		Task<List<Contact>> GetAllContactsForDepartmentAsync(int departmentId);
		Task<List<ContactCategory>> GetContactCategoriesForDepartmentAsync(int departmentId);
		Task<Contact> SaveContactAsync(Contact contact, CancellationToken cancellationToken = default(CancellationToken));
		Task<List<Contact>> GetContactsByCategoryIdAsync(int departmentId, string categoryId);
		Task<ContactCategory> SaveContactCategoryAsync(ContactCategory category, CancellationToken cancellationToken = default(CancellationToken));
		Task<ContactCategory> GetContactCategoryByIdAsync(string contactCategoryId);
		Task<bool> DeleteContactCategoryAsync(ContactCategory contactCategory, CancellationToken cancellationToken = default(CancellationToken));
		Task<Contact> GetContactByIdAsync(string contactId);
		Task<List<ContactNote>> GetContactNotesByContactIdAsync(string contactId, int departmentId, bool getDeleted = false);
		Task<List<ContactNoteType>> GetContactNoteTypesByDepartmentIdAsync(int departmentId);
		Task<ContactNoteType> SaveContactNoteTypeAsync(ContactNoteType type, CancellationToken cancellationToken = default(CancellationToken));
		Task<ContactNoteType> GetContactNoteTypeByIdAsync(string contactNoteTypeId);
		Task<bool> DoesContactNoteTypeAlreadyExistAsync(int departmentId, string noteTypeText);
		Task<bool> DeleteContactNoteTypeAsync(ContactNoteType type, CancellationToken cancellationToken = default(CancellationToken));
		Task<ContactNote> SaveContactNoteAsync(ContactNote note, CancellationToken cancellationToken = default(CancellationToken));
		Task<bool> DeleteContactAsync(string contactId, string userId, int departmentId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		// ── Pre-plans (Contacts plan Phase A, A4) ─────────────────────────────────────────

		/// <summary>The live pre-plan for a contact with its hazards, gate code revealed; null when none exists.</summary>
		Task<ContactPreplan> GetPreplanByContactIdAsync(string contactId, int departmentId);

		/// <summary>Upserts the contact's pre-plan (one live row per contact), encrypts the gate code at rest, and audits.</summary>
		Task<ContactPreplan> SavePreplanAsync(ContactPreplan preplan, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Soft-deletes the pre-plan and its hazards, and audits.</summary>
		Task<bool> DeletePreplanAsync(string contactId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>Live pre-plans whose NextReviewDue has passed.</summary>
		Task<List<ContactPreplan>> GetPreplansDueForReviewAsync(int departmentId);

		Task<List<ContactPreplanHazard>> GetHazardsByContactIdAsync(string contactId, int departmentId);

		Task<ContactPreplanHazard> GetHazardByIdAsync(string contactPreplanHazardId);

		/// <summary>Creates or updates a hazard; creates the contact's pre-plan shell first when none exists.</summary>
		Task<ContactPreplanHazard> SaveHazardAsync(ContactPreplanHazard hazard, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteHazardAsync(string contactPreplanHazardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		// ── Site attachments ───────────────────────────────────────────────────────────────

		/// <summary>Attachment metadata (never the blob) for a contact, optionally filtered by type.</summary>
		Task<List<ContactAttachment>> GetContactAttachmentsAsync(string contactId, int departmentId, ContactAttachmentTypes? type = null);

		/// <summary>One attachment including its blob.</summary>
		Task<ContactAttachment> GetContactAttachmentByIdAsync(int contactAttachmentId);

		Task<ContactAttachment> SaveContactAttachmentAsync(ContactAttachment attachment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		Task<bool> DeleteContactAttachmentAsync(int contactAttachmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken));

		// ── Call surfacing ─────────────────────────────────────────────────────────────────

		/// <summary>ShouldAlert notes that are not deleted and not expired, keyed by contact id.</summary>
		Task<Dictionary<string, List<ContactNote>>> GetAlertNotesByContactIdsAsync(int departmentId, IEnumerable<string> contactIds);

		/// <summary>
		/// Composes the linked contacts, pre-plans, hazards, live alert notes and attachment metadata for a call in
		/// one round trip. Returns null when the call does not exist or belongs to another department. The RMS NERIS
		/// prefill contract (A8): additive changes only.
		/// </summary>
		Task<CallSiteInfo> GetCallSiteInfoAsync(int callId, int departmentId);

		/// <summary>Lightweight per-contact summary (has pre-plan, alert note count, hazard count) for the calls' linked contacts.</summary>
		Task<Dictionary<int, List<CallContactSummary>>> GetCallContactSummariesAsync(int departmentId, IEnumerable<Call> calls);
	}
}
