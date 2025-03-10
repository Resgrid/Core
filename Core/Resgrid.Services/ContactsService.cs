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
	}
}
