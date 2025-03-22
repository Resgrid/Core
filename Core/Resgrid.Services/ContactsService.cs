using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Services
{
	public class ContactsService : IContactsService
	{
		private readonly IContactsRepository _contactsRepository;
		private readonly IContactCategoryRepository _contactCategoryRepository;
		private readonly IContactNotesRepository _contactNotesRepository;
		private readonly IContactNoteTypesRepository _contactNoteTypesRepository;
		private readonly IContactAssociationsRepository _contactAssociationsRepository;

		public ContactsService(IContactsRepository contactsRepository, IContactNotesRepository contactNotesRepository,
			IContactCategoryRepository contactCategoryRepository,
			IContactNoteTypesRepository contactNoteTypesRepository, IContactAssociationsRepository contactAssociationsRepository)
		{
			_contactsRepository = contactsRepository;
			_contactCategoryRepository = contactCategoryRepository;
			_contactNotesRepository = contactNotesRepository;
			_contactNoteTypesRepository = contactNoteTypesRepository;
			_contactAssociationsRepository = contactAssociationsRepository;
		}

		public async Task<List<Contact>> GetAllContactsForDepartmentAsync(int departmentId)
		{
			var contacts = await _contactsRepository.GetAllByDepartmentIdAsync(departmentId);
			var categories = await _contactCategoryRepository.GetAllByDepartmentIdAsync(departmentId);

			if (contacts == null)
				return new List<Contact>();

			foreach (var contact in contacts)
			{
				if (categories != null && categories.Any())
					contact.Category = categories.FirstOrDefault(x => x.ContactCategoryId == contact.ContactCategoryId);
			}

			return contacts.ToList();
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
			return await _contactsRepository.SaveOrUpdateAsync(contact, cancellationToken);
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
			return await _contactNotesRepository.SaveOrUpdateAsync(note, cancellationToken);
		}

	}
}
