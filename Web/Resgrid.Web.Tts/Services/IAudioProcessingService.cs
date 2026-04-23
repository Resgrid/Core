namespace Resgrid.Web.Tts.Services
{
	public interface IAudioProcessingService
	{
		Task<byte[]> GenerateNormalizedWavAsync(string text, string voice, int speed, CancellationToken cancellationToken);
	}
}
