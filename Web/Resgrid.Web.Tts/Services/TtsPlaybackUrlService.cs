using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Resgrid.Web.Tts.Configuration;

namespace Resgrid.Web.Tts.Services
{
	public sealed class TtsPlaybackUrlService : ITtsPlaybackUrlService
	{
		private readonly TtsOptions _options;

		public TtsPlaybackUrlService(IOptions<TtsOptions> options)
		{
			_options = options.Value;
		}

		public Uri CreatePlaybackUrl(HttpRequest? request, string hash)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(hash);

			var baseUrl = !string.IsNullOrWhiteSpace(_options.PlaybackBaseUrl)
				? _options.PlaybackBaseUrl
				: request is not null && request.Host.HasValue
					? $"{request.Scheme}://{request.Host}{request.PathBase}"
					: null;

			if (string.IsNullOrWhiteSpace(baseUrl))
			{
				throw new InvalidOperationException("A public playback base URL is required to generate TTS playback URLs.");
			}

			return new Uri($"{baseUrl.TrimEnd('/')}/tts/audio/{hash.Trim().ToLowerInvariant()}.wav", UriKind.Absolute);
		}
	}
}
