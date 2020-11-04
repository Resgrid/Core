using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ILogAttachmentRepository
	/// Implements the <see cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogAttachment}" />
	/// </summary>
	/// <seealso cref="Resgrid.Model.Repositories.IRepository{Resgrid.Model.LogAttachment}" />
	public interface ILogAttachmentRepository: IRepository<LogAttachment>
	{
		/// <summary>
		/// Gets the attachments by log identifier asynchronous.
		/// </summary>
		/// <param name="logId">The log identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;LogAttachment&gt;&gt;.</returns>
		Task<IEnumerable<LogAttachment>> GetAttachmentsByLogIdAsync(int logId);
	}
}
