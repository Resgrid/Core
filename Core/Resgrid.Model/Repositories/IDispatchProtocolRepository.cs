using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDispatchProtocolRepository
	/// Implements the <see cref="DispatchProtocol" />
	/// </summary>
	/// <seealso cref="DispatchProtocol" />
	public interface IDispatchProtocolRepository: IRepository<DispatchProtocol>
	{
		/// <summary>
		/// Gets the dispatch protocol by identifier asynchronous.
		/// </summary>
		/// <param name="protocolId">The protocol identifier.</param>
		/// <returns>Task&lt;DispatchProtocol&gt;.</returns>
		Task<DispatchProtocol> GetDispatchProtocolByIdAsync(int protocolId);

		/// <summary>
		/// Gets the dispatch protocols by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DispatchProtocol&gt;&gt;.</returns>
		Task<IEnumerable<DispatchProtocol>> GetDispatchProtocolsByDepartmentIdAsync(int departmentId);
	}
}
