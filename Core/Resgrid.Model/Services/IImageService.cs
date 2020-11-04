using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface IImageService
	/// </summary>
	public interface IImageService
	{
		/// <summary>
		/// Gets the image asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="id">The identifier.</param>
		/// <returns>Task&lt;System.Byte[]&gt;.</returns>
		Task<byte[]> GetImageAsync(ImageTypes type, string id);

		/// <summary>
		/// Saves the image asynchronous.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="id">The identifier.</param>
		/// <param name="image">The image.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> SaveImageAsync(ImageTypes type, string id, byte[] image,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
