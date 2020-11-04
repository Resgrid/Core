using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDispatchProtocolQuestionsRepository
	/// Implements the <see cref="DispatchProtocolQuestion" />
	/// </summary>
	/// <seealso cref="DispatchProtocolQuestion" />
	public interface IDispatchProtocolQuestionsRepository: IRepository<DispatchProtocolQuestion>
	{
		/// <summary>
		/// Gets the dispatch protocol questions by protocol identifier asynchronous.
		/// </summary>
		/// <param name="protocolId">The protocol identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DispatchProtocolQuestion&gt;&gt;.</returns>
		Task<IEnumerable<DispatchProtocolQuestion>> GetDispatchProtocolQuestionsByProtocolIdAsync(int protocolId);
	}
}
