namespace Resgrid.Web.Tts.Services
{
	public interface IStorageService
	{
		Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken);

		Task UploadAsync(string objectKey, Stream content, string contentType, CancellationToken cancellationToken);

		Task<TtsAudioContent?> GetObjectAsync(string objectKey, CancellationToken cancellationToken);

		Task<Uri> GetObjectUrlAsync(string objectKey, CancellationToken cancellationToken);
	}
}
