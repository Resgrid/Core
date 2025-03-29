using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallContactsRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallContact}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallContact}" />
	public interface ICallContactsRepository : IRepository<CallContact>
	{
		Task<IEnumerable<CallContact>> GetCallContactsByCallIdAsync(int callId);
	}
}
