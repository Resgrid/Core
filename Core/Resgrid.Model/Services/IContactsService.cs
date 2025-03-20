using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
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
	}
}
