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

		/// <summary>A protocol's attachments without their bytes: id, name, type and <see cref="DispatchProtocolAttachment.Size"/>.</summary>
		Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentInfoByProtocolIdAsync(int protocolId);

		/// <summary>Every attachment of the department's protocols without their bytes, in one query.</summary>
		Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentInfoByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Every attachment of the department's protocols without its bytes, with its SHA-256 (P3 offline manifest, M0381). Rows still
		/// without a hash are hashed by the database first, so the bytes never leave it.
		/// </summary>
		Task<IEnumerable<DispatchProtocolAttachment>> GetAttachmentHashesByDepartmentIdAsync(int departmentId);
	}
}
