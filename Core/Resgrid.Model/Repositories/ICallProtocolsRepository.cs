using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallProtocolsRepository
	/// Implements the <see cref="CallProtocol" />
	/// </summary>
	/// <seealso cref="CallProtocol" />
	public interface ICallProtocolsRepository: IRepository<CallProtocol>
	{
		/// <summary>
		/// Gets the call protocols by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallProtocol&gt;&gt;.</returns>
		Task<IEnumerable<CallProtocol>> GetCallProtocolsByCallIdAsync(int callId);
	}
}
