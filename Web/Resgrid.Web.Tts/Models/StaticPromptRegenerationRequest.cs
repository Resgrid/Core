namespace Resgrid.Web.Tts.Models
{
	public sealed class StaticPromptRegenerationRequest
	{
		public List<TtsRequest> Prompts { get; set; } = new();
	}
}
