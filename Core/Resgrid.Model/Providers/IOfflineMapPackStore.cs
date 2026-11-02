using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	/// <summary>
	/// Storage for built offline map packs and mirrored offline styles (MappingConfig.OfflinePackStorage: a shared
	/// directory or an S3-compatible bucket). The API serves files from here through signed, Range-capable URLs.
	/// </summary>
	public interface IOfflineMapPackStore
	{
		bool IsConfigured { get; }

		Task PutFileAsync(string key, string localPath, string contentType, CancellationToken cancellationToken = default);

		Task PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default);

		/// <summary>The object's size, or null when it does not exist.</summary>
		Task<long?> GetSizeAsync(string key, CancellationToken cancellationToken = default);

		/// <summary>A stream over <paramref name="length"/> bytes from <paramref name="offset"/> (null length: to the end).</summary>
		Task<Stream> OpenReadAsync(string key, long offset, long? length, CancellationToken cancellationToken = default);

		Task DeleteAsync(string key, CancellationToken cancellationToken = default);
	}
}
