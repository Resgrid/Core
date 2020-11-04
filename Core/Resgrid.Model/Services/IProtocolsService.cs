using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IProtocolsService
	{
		/// <summary>
		/// Gets all protocols for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;DispatchProtocol&gt;&gt;.</returns>
		Task<List<DispatchProtocol>> GetAllProtocolsForDepartmentAsync(int departmentId);
		/// <summary>
		/// Saves the protocol asynchronous.
		/// </summary>
		/// <param name="protocol">The protocol.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DispatchProtocol&gt;.</returns>
		Task<DispatchProtocol> SaveProtocolAsync(DispatchProtocol protocol, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Gets the protocol by identifier asynchronous.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;DispatchProtocol&gt;.</returns>
		Task<DispatchProtocol> GetProtocolByIdAsync(int id);

		/// <summary>
		/// Deletes the protocol.
		/// </summary>
		/// <param name="id">The identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteProtocol(int id, CancellationToken cancellationToken = default(CancellationToken));
		/// <summary>
		/// Processes the triggers.
		/// </summary>
		/// <param name="protocols">The protocols.</param>
		/// <param name="call">The call.</param>
		/// <returns>List&lt;DispatchProtocol&gt;.</returns>
		List<DispatchProtocol> ProcessTriggers(List<DispatchProtocol> protocols, Call call);
		/// <summary>
		/// Gets the attachment by identifier asynchronous.
		/// </summary>
		/// <param name="protocolAttachmentId">The protocol attachment identifier.</param>
		/// <returns>Task&lt;DispatchProtocolAttachment&gt;.</returns>
		Task<DispatchProtocolAttachment> GetAttachmentByIdAsync(int protocolAttachmentId);
		/// <summary>
		/// Determines the active triggers.
		/// </summary>
		/// <param name="protocol">The protocol.</param>
		/// <param name="call">The call.</param>
		/// <returns>List&lt;DispatchProtocolTrigger&gt;.</returns>
		List<DispatchProtocolTrigger> DetermineActiveTriggers(DispatchProtocol protocol, Call call);
	}
}
