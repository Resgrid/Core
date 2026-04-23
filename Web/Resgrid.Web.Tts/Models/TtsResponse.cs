namespace Resgrid.Web.Tts.Models
{
	public sealed class TtsResponse
	{
		public string Hash { get; set; } = string.Empty;

		public string ObjectKey { get; set; } = string.Empty;

		public string Url { get; set; } = string.Empty;

		public string Voice { get; set; } = string.Empty;

		public int Speed { get; set; }

		public bool Cached { get; set; }
	}
}
