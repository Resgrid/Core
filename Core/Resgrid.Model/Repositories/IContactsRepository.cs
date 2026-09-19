using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IContactsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Contact}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.Contact}" />
	public interface IContactsRepository : IRepository<Contact>
	{
		Task<IEnumerable<Contact>> GetContactsByCategoryIdAsync(int departmentId, string categoryId);
		/// <summary>The department's contacts with the given ids in one query (deleted rows included; callers decide).</summary>
		Task<IEnumerable<Contact>> GetContactsByIdsAsync(int departmentId, IEnumerable<string> contactIds);
	}
}
