using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDispatchProtocolAttachmentRepository
	/// Implements the <see cref="DispatchProtocolAttachment" />
	/// </summary>
	/// <seealso cref="DispatchProtocolAttachment" />
	public interface IDispatchProtocolAttachmentRepository: IRepository<DispatchProtocolAttachment>
	{
		/// <summary>
		/// Gets the dispatch protocol attachment by protocol identifier asynchronous.
		/// </summary>
		/// <param name="protocolId">The protocol identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;DispatchProtocolAttachment&gt;&gt;.</returns>
		Task<IEnumerable<DispatchProtocolAttachment>> GetDispatchProtocolAttachmentByProtocolIdAsync(int protocolId);
	}
}
