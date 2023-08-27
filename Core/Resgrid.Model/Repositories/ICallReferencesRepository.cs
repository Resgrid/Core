using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallReferencesRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallReference}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallReference}" />
	public interface ICallReferencesRepository : IRepository<CallReference>
	{
		Task<IEnumerable<CallReference>> GetCallReferencesByTargetCallIdAsync(int callId);
		Task<IEnumerable<CallReference>> GetCallReferencesBySourceCallIdAsync(int callId);
	}
}
