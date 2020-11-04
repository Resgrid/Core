using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface ITrainingAttachmentRepository
	/// Implements the <see cref="TrainingAttachment" />
	/// </summary>
	/// <seealso cref="TrainingAttachment" />
	public interface ITrainingAttachmentRepository: IRepository<TrainingAttachment>
	{
		/// <summary>
		/// Gets the training attachments by training identifier asynchronous.
		/// </summary>
		/// <param name="trainingId">The training identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;TrainingAttachment&gt;&gt;.</returns>
		Task<IEnumerable<TrainingAttachment>> GetTrainingAttachmentsByTrainingIdAsync(int trainingId);
	}
}
