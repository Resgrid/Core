using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IContactNotesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ContactNote}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.ContactNote}" />
	public interface IContactNotesRepository : IRepository<ContactNote>
	{
		Task<IEnumerable<ContactNote>> GetContactNotesByContactIdAsync(string contactId);
	}
}
