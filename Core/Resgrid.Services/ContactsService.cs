using System;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;

namespace Resgrid.Services
{
	public class ContactsService : IContactsService
	{
		/// <summary>Placeholder written into audit snapshots in place of a gate code.</summary>
		public const string GateCodeAuditMask = "***";

		private readonly IContactsRepository _contactsRepository;
		private readonly IContactCategoryRepository _contactCategoryRepository;
		private readonly IContactNotesRepository _contactNotesRepository;
		private readonly IContactNoteTypesRepository _contactNoteTypesRepository;
		private readonly IContactAssociationsRepository _contactAssociationsRepository;
		private readonly IContactPreplanRepository _contactPreplanRepository;
		private readonly IContactPreplanHazardRepository _contactPreplanHazardRepository;
		private readonly IContactAttachmentRepository _contactAttachmentRepository;
		private readonly ICallsRepository _callsRepository;
		private readonly ICallContactsRepository _callContactsRepository;
		private readonly IEventAggregator _eventAggregator;
		private readonly Lazy<IProtectedWriteService> _protectedWriteService;
		/// <summary>RMS-5: which system owns structure writes; lazy because Records depends on Contacts (RMS plan section 4.3).</summary>
		private readonly Lazy<IContactPreplanOwnershipGate> _ownershipGate;

		public ContactsService(IContactsRepository contactsRepository, IContactNotesRepository contactNotesRepository,
			IContactCategoryRepository contactCategoryRepository,  IContactNoteTypesRepository contactNoteTypesRepository,
			IContactAssociationsRepository contactAssociationsRepository, IContactPreplanRepository contactPreplanRepository,
			IContactPreplanHazardRepository contactPreplanHazardRepository, IContactAttachmentRepository contactAttachmentRepository,
			ICallsRepository callsRepository, ICallContactsRepository callContactsRepository,
			IEventAggregator eventAggregator, Lazy<IProtectedWriteService> protectedWriteService, Lazy<IContactPreplanOwnershipGate> ownershipGate)
		{
			_ownershipGate = ownershipGate;
			_contactsRepository = contactsRepository;
			_contactCategoryRepository = contactCategoryRepository;
			_contactNotesRepository = contactNotesRepository;
			_contactNoteTypesRepository = contactNoteTypesRepository;
			_contactAssociationsRepository = contactAssociationsRepository;
			_contactPreplanRepository = contactPreplanRepository;
			_contactPreplanHazardRepository = contactPreplanHazardRepository;
			_contactAttachmentRepository = contactAttachmentRepository;
			_callsRepository = callsRepository;
			_callContactsRepository = callContactsRepository;
			_eventAggregator = eventAggregator;
			_protectedWriteService = protectedWriteService;
		}

		public async Task<List<Contact>> GetAllContactsForDepartmentAsync(int departmentId)
		{
			var contactsResult = new List<Contact>();
			var contacts = await _contactsRepository.GetAllByDepartmentIdAsync(departmentId);
			var categories = await _contactCategoryRepository.GetAllByDepartmentIdAsync(departmentId);

			if (contacts == null)
				return new List<Contact>();

			foreach (var contact in contacts)
			{
				if (contact.IsDeleted)
					continue;

				if (categories != null && categories.Any())
					contact.Category = categories.FirstOrDefault(x => x.ContactCategoryId == contact.ContactCategoryId);

				contactsResult.Add(contact);
			}

			return contactsResult;
		}

		public async Task<List<ContactCategory>> GetContactCategoriesForDepartmentAsync(int departmentId)
		{
			var categories = await _contactCategoryRepository.GetAllByDepartmentIdAsync(departmentId);

			if (categories == null)
				return new List<ContactCategory>();

			foreach (var category in categories)
			{
				category.Contacts = await GetContactsByCategoryIdAsync(departmentId, category.ContactCategoryId);
			}

			return categories.ToList();
		}

		public async Task<Contact> SaveContactAsync(Contact contact, CancellationToken cancellationToken = default(CancellationToken))
		{
			// Round-tripped REDACTED placeholder on an edit means "unchanged" — fetch the stored row
			// before it is overwritten so the safety net can restore the stored envelopes.
			Contact existingContactForRestore = null;
			if (!string.IsNullOrWhiteSpace(contact.ContactId) &&
				ProtectedReadService.ContactFieldAccessors.Any(a => a.Value.Get(contact) == ProtectedDataEnvelope.RedactionValue))
				existingContactForRestore = await _contactsRepository.GetByIdAsync(contact.ContactId);

			var savedContact = await _contactsRepository.SaveOrUpdateAsync(contact, cancellationToken);

			// ADP write safety net (plan 4.2/19.2): mirrors CallsService — post-save so the row's id
			// (a repository-assigned guid on creates) is a valid AAD rowKey; already-enveloped and
			// REDACTED-sentinel values were handled by the caller/Prepare, so this is a no-op for
			// edge-encrypted saves. Fails closed by throwing.
			var protectedWrite = await _protectedWriteService.Value.PrepareContactWriteAsync(savedContact.DepartmentId,
				savedContact, existingContactForRestore, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); contact {savedContact.ContactId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed || (existingContactForRestore != null && protectedWrite.Success))
				savedContact = await _contactsRepository.SaveOrUpdateAsync(savedContact, cancellationToken);

			return savedContact;
		}

		public async Task<List<Contact>> GetContactsByCategoryIdAsync(int departmentId, string categoryId)
		{
			var contacts = await _contactsRepository.GetContactsByCategoryIdAsync(departmentId, categoryId);

			if (contacts == null)
				return new List<Contact>();

			return contacts.ToList();
		}

		public async Task<ContactCategory> SaveContactCategoryAsync(ContactCategory category, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _contactCategoryRepository.SaveOrUpdateAsync(category, cancellationToken);
		}

		public async Task<ContactCategory> GetContactCategoryByIdAsync(string contactCategoryId)
		{
			return await _contactCategoryRepository.GetByIdAsync(contactCategoryId);
		}

		public async Task<bool> DeleteContactCategoryAsync(ContactCategory contactCategory, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _contactCategoryRepository.DeleteAsync(contactCategory, cancellationToken);
		}

		public async Task<Contact> GetContactByIdAsync(string contactId)
		{
			return await _contactsRepository.GetByIdAsync(contactId);
		}

		public async Task<List<ContactNote>> GetContactNotesByContactIdAsync(string contactId, int departmentId, bool getDeleted = false)
		{
			var notes = await _contactNotesRepository.GetContactNotesByContactIdAsync(contactId);
			var notesResult =  new List<ContactNote>();

			if (notes == null)
				return notesResult;

			var noteTypes = await _contactNoteTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			foreach (var note in notes)
			{
				if (!note.IsDeleted || (note.IsDeleted && getDeleted))
				{
					note.NoteType = noteTypes.FirstOrDefault(x => x.ContactNoteTypeId == note.ContactNoteTypeId);
					notesResult.Add(note);
				}
			}

			return notes.ToList();
		}

		public async Task<List<ContactNoteType>> GetContactNoteTypesByDepartmentIdAsync(int departmentId)
		{
			var types = await _contactNoteTypesRepository.GetAllByDepartmentIdAsync(departmentId);

			if (types == null)
				return new List<ContactNoteType>();

			return types.ToList();
		}

		public async Task<ContactNoteType> SaveContactNoteTypeAsync(ContactNoteType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _contactNoteTypesRepository.SaveOrUpdateAsync(type, cancellationToken);
		}

		public async Task<ContactNoteType> GetContactNoteTypeByIdAsync(string contactNoteTypeId)
		{
			return await _contactNoteTypesRepository.GetByIdAsync(contactNoteTypeId);
		}

		public async Task<bool> DoesContactNoteTypeAlreadyExistAsync(int departmentId, string noteTypeText)
		{
			var types = await GetContactNoteTypesByDepartmentIdAsync(departmentId);

			if (types == null)
				return false;

			return types.Any(x => x.Name == noteTypeText.Trim());
		}

		public async Task<bool> DeleteContactNoteTypeAsync(ContactNoteType type, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _contactNoteTypesRepository.DeleteAsync(type, cancellationToken);
		}

		public async Task<ContactNote> SaveContactNoteAsync(ContactNote note, CancellationToken cancellationToken = default(CancellationToken))
		{
			var savedNote = await _contactNotesRepository.SaveOrUpdateAsync(note, cancellationToken);

			var protectedWrite = await _protectedWriteService.Value.PrepareContactNoteWriteAsync(savedNote.DepartmentId,
				savedNote, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); contact note {savedNote.ContactNoteId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				savedNote = await _contactNotesRepository.SaveOrUpdateAsync(savedNote, cancellationToken);

			return savedNote;
		}

		public async Task<bool> DeleteContactAsync(string contactId, string userId, int departmentId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			var auditEvent = NewAuditEvent(departmentId, userId, AuditLogTypes.ContactRemoved, ipAddress, userAgent);

			var contact = await _contactsRepository.GetByIdAsync(contactId);
			auditEvent.Before = contact.CloneJsonToString();

			contact.IsDeleted = true;
			contact.EditedByUserId = userId;
			contact.EditedOn = DateTime.UtcNow;

			await SaveContactAsync(contact, cancellationToken);

			auditEvent.After = contact.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return true;
		}

		#region Pre-plans (Contacts plan Phase A)

		public async Task<ContactPreplan> GetPreplanByContactIdAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return null;

			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
			{
				var projections = await _ownershipGate.Value.GetPreplanProjectionsAsync(departmentId, new[] { contactId });
				return projections.TryGetValue(contactId, out var projected) ? projected : null;
			}

			var preplan = await _contactPreplanRepository.GetPreplanByContactIdAsync(contactId, departmentId);

			if (preplan == null)
				return null;

			var hazards = await _contactPreplanHazardRepository.GetHazardsByPreplanIdAsync(preplan.ContactPreplanId, departmentId);
			preplan.Hazards = hazards?.Where(x => !x.IsDeleted).ToList() ?? new List<ContactPreplanHazard>();

			return preplan;
		}

		public async Task<ContactPreplan> SavePreplanAsync(ContactPreplan preplan, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (preplan == null)
				throw new ArgumentNullException(nameof(preplan));
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(preplan.DepartmentId))
				throw new InvalidOperationException(IContactPreplanOwnershipGate.RecordsOwnedReason);

			var contact = await RequireContactAsync(preplan.ContactId, preplan.DepartmentId);
			var existing = await _contactPreplanRepository.GetPreplanByContactIdAsync(contact.ContactId, preplan.DepartmentId);
			var now = DateTime.UtcNow;

			var auditEvent = NewAuditEvent(preplan.DepartmentId, userId,
				existing == null ? AuditLogTypes.ContactPreplanAdded : AuditLogTypes.ContactPreplanUpdated, ipAddress, userAgent);

			if (existing != null)
			{
				auditEvent.Before = AuditSnapshot(existing);

				// One live row per contact: an incoming save always lands on the existing row.
				preplan.ContactPreplanId = existing.ContactPreplanId;
				preplan.AddedOn = existing.AddedOn;
				preplan.AddedByUserId = existing.AddedByUserId;
				preplan.IsDeleted = false;
				preplan.EditedOn = now;
				preplan.EditedByUserId = userId;
			}
			else
			{
				preplan.ContactPreplanId = null;
				preplan.AddedOn = now;
				preplan.AddedByUserId = userId;
				preplan.IsDeleted = false;
				preplan.EditedOn = null;
				preplan.EditedByUserId = null;
			}

			var saved = await _contactPreplanRepository.SaveOrUpdateAsync(preplan, cancellationToken);

			// ADP write safety net (catalog v12): post-save so a repository-assigned id is a valid AAD row key.
			// The stored row restores any REDACTED sentinel an editor round-tripped; enveloped values pass
			// through untouched. Fails closed by throwing.
			var protectedWrite = await _protectedWriteService.Value.PrepareContactPreplanWriteAsync(saved.DepartmentId,
				saved, existing, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); contact pre-plan {saved.ContactPreplanId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				saved = await _contactPreplanRepository.SaveOrUpdateAsync(saved, cancellationToken);

			auditEvent.After = AuditSnapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			var hazards = await _contactPreplanHazardRepository.GetHazardsByPreplanIdAsync(saved.ContactPreplanId, saved.DepartmentId);
			saved.Hazards = hazards?.Where(x => !x.IsDeleted).ToList() ?? new List<ContactPreplanHazard>();

			return saved;
		}

		public async Task<bool> DeletePreplanAsync(string contactId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
				throw new InvalidOperationException(IContactPreplanOwnershipGate.RecordsOwnedReason);
			var preplan = await _contactPreplanRepository.GetPreplanByContactIdAsync(contactId, departmentId);

			if (preplan == null)
				return false;

			var auditEvent = NewAuditEvent(departmentId, userId, AuditLogTypes.ContactPreplanRemoved, ipAddress, userAgent);
			auditEvent.Before = AuditSnapshot(preplan);

			var now = DateTime.UtcNow;
			var hazards = await _contactPreplanHazardRepository.GetHazardsByPreplanIdAsync(preplan.ContactPreplanId, departmentId);
			if (hazards != null)
			{
				foreach (var hazard in hazards.Where(x => !x.IsDeleted))
				{
					hazard.IsDeleted = true;
					hazard.EditedOn = now;
					hazard.EditedByUserId = userId;
					await _contactPreplanHazardRepository.SaveOrUpdateAsync(hazard, cancellationToken);
				}
			}

			preplan.IsDeleted = true;
			preplan.EditedOn = now;
			preplan.EditedByUserId = userId;
			await _contactPreplanRepository.SaveOrUpdateAsync(preplan, cancellationToken);

			auditEvent.After = AuditSnapshot(preplan);
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return true;
		}

		public async Task<List<ContactPreplan>> GetPreplansDueForReviewAsync(int departmentId)
		{
			var preplans = await _contactPreplanRepository.GetPreplansByDepartmentIdAsync(departmentId);

			if (preplans == null)
				return new List<ContactPreplan>();

			var now = DateTime.UtcNow;
			var due = preplans.Where(x => !x.IsDeleted && x.IsReviewOverdue(now)).ToList();

			// A review listing never needs the gate code (plaintext or envelope); do not carry it around.
			foreach (var preplan in due)
				preplan.GateCode = null;

			return due;
		}

		public async Task<List<ContactPreplanHazard>> GetHazardsByContactIdAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return new List<ContactPreplanHazard>();

			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
			{
				var projections = await _ownershipGate.Value.GetPreplanProjectionsAsync(departmentId, new[] { contactId });
				return projections.TryGetValue(contactId, out var projected) ? projected.Hazards ?? new List<ContactPreplanHazard>() : new List<ContactPreplanHazard>();
			}

			var hazards = await _contactPreplanHazardRepository.GetHazardsByContactIdAsync(contactId, departmentId);

			if (hazards == null)
				return new List<ContactPreplanHazard>();

			return hazards.Where(x => !x.IsDeleted).ToList();
		}

		public async Task<ContactPreplanHazard> GetHazardByIdAsync(string contactPreplanHazardId)
		{
			if (string.IsNullOrWhiteSpace(contactPreplanHazardId))
				return null;

			return await _contactPreplanHazardRepository.GetByIdAsync(contactPreplanHazardId);
		}

		public async Task<ContactPreplanHazard> SaveHazardAsync(ContactPreplanHazard hazard, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (hazard == null)
				throw new ArgumentNullException(nameof(hazard));
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(hazard.DepartmentId))
				throw new InvalidOperationException(IContactPreplanOwnershipGate.RecordsOwnedReason);
			if (string.IsNullOrWhiteSpace(hazard.Title))
				throw new ArgumentException("A hazard needs a title.", nameof(hazard));

			var contact = await RequireContactAsync(hazard.ContactId, hazard.DepartmentId);
			var now = DateTime.UtcNow;

			// Hazards hang off the pre-plan row; create the shell when the contact has none yet.
			var preplan = await _contactPreplanRepository.GetPreplanByContactIdAsync(contact.ContactId, hazard.DepartmentId);
			if (preplan == null)
			{
				preplan = await SavePreplanAsync(new ContactPreplan
				{
					ContactId = contact.ContactId,
					DepartmentId = hazard.DepartmentId
				}, userId, ipAddress, userAgent, cancellationToken);
			}

			var auditEvent = NewAuditEvent(hazard.DepartmentId, userId, AuditLogTypes.ContactPreplanUpdated, ipAddress, userAgent);

			ContactPreplanHazard existing = null;
			if (!string.IsNullOrWhiteSpace(hazard.ContactPreplanHazardId))
				existing = await _contactPreplanHazardRepository.GetByIdAsync(hazard.ContactPreplanHazardId);

			if (existing != null)
			{
				if (existing.DepartmentId != hazard.DepartmentId || existing.ContactId != hazard.ContactId)
					throw new InvalidOperationException("The hazard does not belong to this contact.");

				auditEvent.Before = existing.CloneJsonToString();
				hazard.AddedOn = existing.AddedOn;
				hazard.AddedByUserId = existing.AddedByUserId;
				hazard.EditedOn = now;
				hazard.EditedByUserId = userId;
			}
			else
			{
				hazard.ContactPreplanHazardId = null;
				hazard.AddedOn = now;
				hazard.AddedByUserId = userId;
				hazard.EditedOn = null;
				hazard.EditedByUserId = null;
			}

			hazard.ContactPreplanId = preplan.ContactPreplanId;
			hazard.IsDeleted = false;

			var saved = await _contactPreplanHazardRepository.SaveOrUpdateAsync(hazard, cancellationToken);

			// ADP write safety net (catalog v12); see SavePreplanAsync.
			var protectedWrite = await _protectedWriteService.Value.PrepareContactPreplanHazardWriteAsync(saved.DepartmentId,
				saved, existing, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); premise hazard {saved.ContactPreplanHazardId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				saved = await _contactPreplanHazardRepository.SaveOrUpdateAsync(saved, cancellationToken);

			auditEvent.After = saved.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return saved;
		}

		public async Task<bool> DeleteHazardAsync(string contactPreplanHazardId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
				throw new InvalidOperationException(IContactPreplanOwnershipGate.RecordsOwnedReason);
			var hazard = await _contactPreplanHazardRepository.GetByIdAsync(contactPreplanHazardId);

			if (hazard == null || hazard.DepartmentId != departmentId || hazard.IsDeleted)
				return false;

			var auditEvent = NewAuditEvent(departmentId, userId, AuditLogTypes.ContactPreplanUpdated, ipAddress, userAgent);
			auditEvent.Before = hazard.CloneJsonToString();

			hazard.IsDeleted = true;
			hazard.EditedOn = DateTime.UtcNow;
			hazard.EditedByUserId = userId;
			await _contactPreplanHazardRepository.SaveOrUpdateAsync(hazard, cancellationToken);

			auditEvent.After = hazard.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return true;
		}

		#endregion

		#region Site attachments

		public async Task<List<ContactAttachment>> GetContactAttachmentsAsync(string contactId, int departmentId, ContactAttachmentTypes? type = null)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				return new List<ContactAttachment>();

			var attachments = await _contactAttachmentRepository.GetAttachmentMetaByContactIdAsync(contactId, departmentId);

			if (attachments == null)
				return new List<ContactAttachment>();

			var live = attachments.Where(x => !x.IsDeleted);
			if (type.HasValue)
				live = live.Where(x => x.ContactAttachmentType == (int)type.Value);

			return live.ToList();
		}

		public async Task<ContactAttachment> GetContactAttachmentByIdAsync(int contactAttachmentId)
		{
			if (contactAttachmentId <= 0)
				return null;

			var attachment = await _contactAttachmentRepository.GetAttachmentByIdAsync(contactAttachmentId);

			if (attachment == null || attachment.IsDeleted)
				return null;

			return attachment;
		}

		public async Task<ContactAttachment> SaveContactAttachmentAsync(ContactAttachment attachment, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			if (attachment == null)
				throw new ArgumentNullException(nameof(attachment));
			if (attachment.Data == null || attachment.Data.Length == 0)
				throw new ArgumentException("An attachment needs file data.", nameof(attachment));

			await RequireContactAsync(attachment.ContactId, attachment.DepartmentId);

			attachment.ContactAttachmentId = 0;
			attachment.Size = attachment.Data.Length;
			attachment.IsDeleted = false;
			attachment.AddedOn = DateTime.UtcNow;
			attachment.AddedByUserId = userId;

			if (string.IsNullOrWhiteSpace(attachment.Name))
				attachment.Name = attachment.FileName;

			var saved = await _contactAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken);

			// ADP write safety net (catalog v12): the identity id is the AAD row key, so encryption runs after the
			// insert assigned it, then the enveloped row (name, file name, rgdpb bytes) is persisted.
			var protectedWrite = await _protectedWriteService.Value.PrepareContactAttachmentWriteAsync(saved.DepartmentId,
				saved, null, null, workloadCaller: true, cancellationToken);
			if (!protectedWrite.Success)
				throw new InvalidOperationException($"Protected write blocked ({protectedWrite.Reason}); contact attachment {saved.ContactAttachmentId} has transient plaintext pending re-encryption.");
			if (protectedWrite.Changed)
				saved = await _contactAttachmentRepository.SaveOrUpdateAsync(saved, cancellationToken);

			var auditEvent = NewAuditEvent(attachment.DepartmentId, userId, AuditLogTypes.ContactAttachmentAdded, ipAddress, userAgent);
			auditEvent.After = AuditSnapshot(saved);
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return saved;
		}

		public async Task<bool> DeleteContactAttachmentAsync(int contactAttachmentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default(CancellationToken))
		{
			var attachment = await _contactAttachmentRepository.GetAttachmentByIdAsync(contactAttachmentId);

			if (attachment == null || attachment.DepartmentId != departmentId || attachment.IsDeleted)
				return false;

			var auditEvent = NewAuditEvent(departmentId, userId, AuditLogTypes.ContactAttachmentRemoved, ipAddress, userAgent);
			auditEvent.Before = AuditSnapshot(attachment);

			attachment.IsDeleted = true;
			await _contactAttachmentRepository.SaveOrUpdateAsync(attachment, cancellationToken);

			auditEvent.After = AuditSnapshot(attachment);
			_eventAggregator.SendMessage<AuditEvent>(auditEvent);

			return true;
		}

		#endregion

		#region Call surfacing

		public async Task<Dictionary<string, List<ContactNote>>> GetAlertNotesByContactIdsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var result = new Dictionary<string, List<ContactNote>>();
			var now = DateTime.UtcNow;

			foreach (var contactId in (contactIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
			{
				var notes = await _contactNotesRepository.GetContactNotesByContactIdAsync(contactId);
				result[contactId] = (notes ?? Enumerable.Empty<ContactNote>())
					.Where(x => IsLiveAlertNote(x, departmentId, now))
					.ToList();
			}

			return result;
		}

		/// <summary>ShouldAlert, not deleted, in the department, and not expired (a null ExpiresOn never expires).</summary>
		public static bool IsLiveAlertNote(ContactNote note, int departmentId, DateTime utcNow)
		{
			if (note == null || note.IsDeleted || !note.ShouldAlert || note.DepartmentId != departmentId)
				return false;

			return !note.ExpiresOn.HasValue || note.ExpiresOn.Value > utcNow;
		}

		public async Task<CallSiteInfo> GetCallSiteInfoAsync(int callId, int departmentId)
		{
			var call = await _callsRepository.GetByIdAsync(callId);

			if (call == null || call.DepartmentId != departmentId)
				return null;

			var info = new CallSiteInfo { CallId = callId, DepartmentId = departmentId };

			var callContacts = (await _callContactsRepository.GetCallContactsByCallIdAsync(callId))?.ToList() ?? new List<CallContact>();
			if (!callContacts.Any())
				return info;

			var contactIds = callContacts.Where(x => !string.IsNullOrWhiteSpace(x.ContactId)).Select(x => x.ContactId).Distinct().ToList();
			var contacts = await LoadContactsAsync(departmentId, contactIds);
			List<ContactPreplan> preplans;
			List<ContactPreplanHazard> hazards;
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
			{
				// RMS owns the structure master (plan section 4.3): project from the occupancy in the Phase A shape.
				var projected = await _ownershipGate.Value.GetPreplanProjectionsAsync(departmentId, contactIds);
				preplans = projected.Values.ToList();
				hazards = preplans.SelectMany(x => x.Hazards ?? new List<ContactPreplanHazard>()).ToList();
			}
			else
			{
				preplans = (await _contactPreplanRepository.GetPreplansByContactIdsAsync(departmentId, contactIds))?.Where(x => !x.IsDeleted).ToList() ?? new List<ContactPreplan>();
				hazards = (await _contactPreplanHazardRepository.GetHazardsByContactIdsAsync(departmentId, contactIds))?.Where(x => !x.IsDeleted).ToList() ?? new List<ContactPreplanHazard>();
			}
			var attachments = (await _contactAttachmentRepository.GetAttachmentMetaByContactIdsAsync(departmentId, contactIds))?.Where(x => !x.IsDeleted).ToList() ?? new List<ContactAttachment>();
			var alertNotes = await GetAlertNotesByContactIdsAsync(departmentId, contactIds);

			// Primary contact first, then additional in link order; one entry per distinct contact.
			foreach (var link in callContacts.OrderBy(x => x.CallContactType))
			{
				if (!contacts.TryGetValue(link.ContactId ?? string.Empty, out var contact))
					continue;
				if (info.Contacts.Any(x => x.Contact.ContactId == contact.ContactId))
					continue;

				var preplan = preplans.FirstOrDefault(x => x.ContactId == contact.ContactId);
				if (preplan != null)
					preplan.Hazards = hazards.Where(x => x.ContactPreplanId == preplan.ContactPreplanId).ToList();

				info.Contacts.Add(new CallSiteContactInfo
				{
					Contact = contact,
					CallContactType = link.CallContactType,
					Preplan = preplan,
					Hazards = hazards.Where(x => x.ContactId == contact.ContactId).ToList(),
					AlertNotes = alertNotes.TryGetValue(contact.ContactId, out var notes) ? notes : new List<ContactNote>(),
					Attachments = attachments.Where(x => x.ContactId == contact.ContactId).ToList()
				});
			}

			return info;
		}

		public async Task<Dictionary<int, List<CallContactSummary>>> GetCallContactSummariesAsync(int departmentId, IEnumerable<Call> calls)
		{
			var result = new Dictionary<int, List<CallContactSummary>>();
			var callList = (calls ?? Enumerable.Empty<Call>()).Where(x => x != null).ToList();

			var contactIds = callList
				.Where(x => x.Contacts != null)
				.SelectMany(x => x.Contacts)
				.Where(x => !string.IsNullOrWhiteSpace(x.ContactId))
				.Select(x => x.ContactId)
				.Distinct()
				.ToList();

			if (!contactIds.Any())
				return result;

			var contacts = await LoadContactsAsync(departmentId, contactIds);
			HashSet<string> preplanContactIds;
			Dictionary<string, int> hazardCounts;
			if (await _ownershipGate.Value.IsRecordsOwnedAsync(departmentId))
			{
				var projected = await _ownershipGate.Value.GetPreplanProjectionsAsync(departmentId, contactIds);
				preplanContactIds = new HashSet<string>(projected.Keys);
				hazardCounts = projected.ToDictionary(x => x.Key, x => x.Value.Hazards?.Count ?? 0);
			}
			else
			{
				preplanContactIds = new HashSet<string>(((await _contactPreplanRepository.GetPreplansByContactIdsAsync(departmentId, contactIds)) ?? Enumerable.Empty<ContactPreplan>())
					.Where(x => !x.IsDeleted).Select(x => x.ContactId));
				hazardCounts = ((await _contactPreplanHazardRepository.GetHazardsByContactIdsAsync(departmentId, contactIds)) ?? Enumerable.Empty<ContactPreplanHazard>())
					.Where(x => !x.IsDeleted).GroupBy(x => x.ContactId).ToDictionary(g => g.Key, g => g.Count());
			}
			var alertNotes = await GetAlertNotesByContactIdsAsync(departmentId, contactIds);

			foreach (var call in callList)
			{
				if (call.Contacts == null)
					continue;

				var summaries = new List<CallContactSummary>();
				foreach (var link in call.Contacts.OrderBy(x => x.CallContactType))
				{
					if (!contacts.TryGetValue(link.ContactId ?? string.Empty, out var contact))
						continue;
					if (summaries.Any(x => x.Contact.ContactId == contact.ContactId))
						continue;

					summaries.Add(new CallContactSummary
					{
						Contact = contact,
						CallContactType = link.CallContactType,
						HasPreplan = preplanContactIds.Contains(contact.ContactId),
						HazardCount = hazardCounts.TryGetValue(contact.ContactId, out var count) ? count : 0,
						AlertNoteCount = alertNotes.TryGetValue(contact.ContactId, out var notes) ? notes.Count : 0
					});
				}

				result[call.CallId] = summaries;
			}

			return result;
		}

		#endregion

		#region Helpers

		private async Task<Contact> RequireContactAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId))
				throw new ArgumentException("A contact id is required.", nameof(contactId));

			var contact = await _contactsRepository.GetByIdAsync(contactId);

			if (contact == null || contact.IsDeleted || contact.DepartmentId != departmentId)
				throw new InvalidOperationException("The contact does not exist in this department.");

			return contact;
		}

		private async Task<Dictionary<string, Contact>> LoadContactsAsync(int departmentId, IEnumerable<string> contactIds)
		{
			var contacts = new Dictionary<string, Contact>();

			foreach (var contactId in contactIds.Distinct())
			{
				var contact = await _contactsRepository.GetByIdAsync(contactId);
				if (contact != null && !contact.IsDeleted && contact.DepartmentId == departmentId)
					contacts[contactId] = contact;
			}

			return contacts;
		}

		/// <summary>Pre-plan audit snapshot with the gate code masked (never the plaintext or the ciphertext).</summary>
		private static string AuditSnapshot(ContactPreplan preplan)
		{
			var clone = preplan.CloneJson();
			if (!string.IsNullOrEmpty(clone.GateCode))
				clone.GateCode = GateCodeAuditMask;
			clone.Hazards = null;
			return clone.CloneJsonToString();
		}

		/// <summary>Attachment audit snapshot: metadata only, never the blob.</summary>
		private static string AuditSnapshot(ContactAttachment attachment)
		{
			var clone = attachment.CloneJson();
			clone.Data = null;
			return clone.CloneJsonToString();
		}

		private static AuditEvent NewAuditEvent(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent)
		{
			return new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Successful = true,
				IpAddress = ipAddress,
				UserAgent = userAgent,
				ServerName = Environment.MachineName
			};
		}

		#endregion
	}
}
