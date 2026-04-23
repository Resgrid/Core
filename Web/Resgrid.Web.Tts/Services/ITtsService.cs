using Resgrid.Web.Tts.Models;

namespace Resgrid.Web.Tts.Services
{
	public interface ITtsService
	{
		Task<TtsResponse> GenerateAsync(TtsRequest request, CancellationToken cancellationToken);

		Task<IReadOnlyCollection<TtsResponse>> GenerateBatchAsync(IEnumerable<TtsRequest> requests, CancellationToken cancellationToken);

		Task WarmPromptsAsync(CancellationToken cancellationToken);
	}
}
