using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Tts.Models
{
	public sealed class TtsRequest
	{
		[Required]
		[StringLength(4000)]
		public string Text { get; set; } = string.Empty;

		[StringLength(64)]
		public string? Voice { get; set; }

		[Range(80, 450)]
		public int? Speed { get; set; }
	}
}
