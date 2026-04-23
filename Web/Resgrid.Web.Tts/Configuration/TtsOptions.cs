using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Tts.Configuration
{
	public sealed class TtsOptions
	{
		[Required]
		public string DefaultVoice { get; set; } = "en-us";

		[Range(80, 450)]
		public int DefaultSpeed { get; set; } = 175;

		[Range(1, 64)]
		public int MaxConcurrentGenerations { get; set; } = 4;

		[Range(1, 10000)]
		public int MaxTextLength { get; set; } = 1000;

		[Required]
		public string EspeakExecutable { get; set; } = "espeak-ng";

		[Required]
		public string FfmpegExecutable { get; set; } = "ffmpeg";

		[Required]
		public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "resgrid-tts");

		[Required]
		public string CachePrefix { get; set; } = "tts";

		[Range(8000, 8000)]
		public int NormalizedSampleRate { get; set; } = 8000;

		[Range(1, 1)]
		public int NormalizedChannels { get; set; } = 1;

		[StringLength(2048)]
		public string PlaybackBaseUrl { get; set; } = string.Empty;

		[Range(1, 1440)]
		public int PlaybackMemoryCacheMinutes { get; set; } = 60;

		[Range(1, 31536000)]
		public int PlaybackCacheControlSeconds { get; set; } = 86400;

		public bool WarmupEnabled { get; set; } = true;

		public string StaticPromptAdminKey { get; set; } = string.Empty;

		public List<string> PreGeneratedPrompts { get; set; } = new()
		{
			"Press 1 for yes",
			"Press 2 for no",
			"Invalid option",
			"Please try again",
			"Please stay on the line"
		};
	}
}
