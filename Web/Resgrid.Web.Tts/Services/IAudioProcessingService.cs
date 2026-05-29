namespace Resgrid.Web.Tts.Services
{
	public interface IAudioProcessingService
	{
		Task<byte[]> GenerateNormalizedWavAsync(string text, string voice, int speed, CancellationToken cancellationToken);

		/// <summary>
		/// Returns the voice and speed that will actually be used for synthesis,
		/// after normalization (e.g. English voices are remapped to a fixed
		/// MBROLA telephony profile regardless of the requested voice/speed).
		/// This allows cache keys to be derived from the effective synthesis
		/// profile rather than the original request parameters.
		/// </summary>
		(string Voice, int Speed) GetEffectiveSynthesisProfile(string voice, int speed);

		/// <summary>
		/// Returns the set of distinct voice identifiers that map to unique
		/// Piper model files. Used at startup to warm up each model so that
		/// the first request for a non-default language does not incur the
		/// model-loading penalty.
		/// </summary>
		IReadOnlySet<string> GetDistinctVoiceIdentifiers();
	}
}
