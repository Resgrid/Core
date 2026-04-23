namespace Resgrid.Web.Tts.Services
{
	public interface ICacheService
	{
		TtsCacheKey CreateCacheKey(string text, string voice, int speed);

		Task<Uri?> TryGetCachedUrlAsync(TtsCacheKey cacheKey, CancellationToken cancellationToken);

		Task<TtsAudioContent?> TryGetAudioAsync(string hash, CancellationToken cancellationToken);

		Task<Uri> StoreAsync(TtsCacheKey cacheKey, byte[] audioBytes, CancellationToken cancellationToken);
	}
}
