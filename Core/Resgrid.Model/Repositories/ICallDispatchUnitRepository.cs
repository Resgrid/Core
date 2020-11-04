using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallDispatchUnitRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchUnit}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.CallDispatchUnit}" />
	public interface ICallDispatchUnitRepository: IRepository<CallDispatchUnit>
	{
		/// <summary>
		/// Gets the call unit dispatches by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallDispatchUnit&gt;&gt;.</returns>
		Task<IEnumerable<CallDispatchUnit>> GetCallUnitDispatchesByCallIdAsync(int callId);
	}
}
