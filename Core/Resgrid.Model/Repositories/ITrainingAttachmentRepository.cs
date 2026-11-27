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

		/// <summary>Attachment rows without their bytes (T-D22): name, type and size only.</summary>
		Task<List<TrainingAttachment>> GetAttachmentMetadataByTrainingIdAsync(int trainingId);

		/// <summary>One attachment's metadata (no bytes).</summary>
		Task<TrainingAttachment> GetAttachmentMetadataByIdAsync(int trainingAttachmentId);

		/// <summary>One attachment with its bytes, for the single authorized download endpoint.</summary>
		Task<TrainingAttachment> GetAttachmentWithDataAsync(int trainingAttachmentId);

		/// <summary>Copies a training's attachments to another training inside the database (no bytes through the app).</summary>
		Task<int> CopyAttachmentsAsync(int fromTrainingId, int toTrainingId, System.Threading.CancellationToken cancellationToken = default);
	}
}
