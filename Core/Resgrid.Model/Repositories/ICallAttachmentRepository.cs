using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ICallAttachmentRepository
	/// Implements the <see cref="CallAttachment" />
	/// </summary>
	/// <seealso cref="CallAttachment" />
	public interface ICallAttachmentRepository: IRepository<CallAttachment>
	{
		/// <summary>
		/// Gets the call attachment by call identifier and type asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <param name="type">The type.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallAttachment&gt;&gt;.</returns>
		Task<CallAttachment> GetCallAttachmentByCallIdAndTypeAsync(int callId, CallAttachmentTypes type);

		/// <summary>
		/// Gets the call dispatches by call identifier asynchronous.
		/// </summary>
		/// <param name="callId">The call identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallAttachment&gt;&gt;.</returns>
		Task<IEnumerable<CallAttachment>> GetCallDispatchesByCallIdAsync(int callId);

		/// <summary>
		/// Gets all flagged call images for a department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallAttachment&gt;&gt;.</returns>
		Task<IEnumerable<CallAttachment>> GetFlaggedCallImagesByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets all flagged call files (audio, files, video) for a department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;CallAttachment&gt;&gt;.</returns>
		Task<IEnumerable<CallAttachment>> GetFlaggedCallFilesByDepartmentIdAsync(int departmentId);
	}
}
