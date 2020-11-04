using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IFileService
	{
		/// <summary>
		/// Saves the file asynchronous.
		/// </summary>
		/// <param name="file">The file.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;File&gt;.</returns>
		Task<File> SaveFileAsync(File file, CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the file by identifier asynchronous.
		/// </summary>
		/// <param name="fileId">The file identifier.</param>
		/// <returns>Task&lt;File&gt;.</returns>
		Task<File> GetFileByIdAsync(int fileId);

		/// <summary>
		/// Deletes the file asynchronous.
		/// </summary>
		/// <param name="file">The file.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteFileAsync(File file, CancellationToken cancellationToken = default(CancellationToken));
	}
}
