namespace Resgrid.Web.Tts.Models
{
	public sealed class StaticPromptRegenerationResponse
	{
		public int PromptCount { get; set; }

		public IReadOnlyCollection<TtsResponse> Prompts { get; set; } = Array.Empty<TtsResponse>();
	}
}
