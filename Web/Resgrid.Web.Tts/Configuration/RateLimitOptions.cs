using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Tts.Configuration
{
	public sealed class RateLimitOptions
	{
		[Range(1, 10000)]
		public int PermitLimit { get; set; } = 60;

		[Range(0, 1000)]
		public int QueueLimit { get; set; } = 10;

		[Range(1, 3600)]
		public int WindowSeconds { get; set; } = 60;
	}
}
