using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Tts.Configuration
{
	public sealed class TtsOptions
	{
		[Required]
		public string DefaultVoice { get; set; } = "en-us+klatt4";

		[Range(80, 450)]
		public int DefaultSpeed { get; set; } = 165;

		[Range(1, 64)]
		public int MaxConcurrentGenerations { get; set; } = 4;

		[Range(1, 10000)]
		public int MaxTextLength { get; set; } = 1000;

		[Required]
		public string PiperExecutable { get; set; } = "piper";

		[Required]
		public string PiperModelDirectory { get; set; } = "/usr/local/share/piper-voices";

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
			"Please stay on the line",
			"This call has been closed. Goodbye.",
			"You have been marked responding to the scene. Goodbye.",
			"Sorry, that was not a valid selection.",
			"Hello, this is Resgrid calling with your verification code.",
			"That was your Resgrid verification code. Goodbye.",
			"Thank you for calling the Resgrid automated personnel system. The number you called is not tied to an active department, or the department doesn't have this feature enabled. Goodbye.",
			"We couldn't complete your verification call. Please request a new code and try again. Goodbye.",
			"Please select from the following options.",
			"To list current active calls, press 1.",
			"To list current user statuses, press 2.",
			"To list current unit statuses, press 3.",
			"To list upcoming calendar events, press 4.",
			"To list upcoming shifts, press 5.",
			"To set your current status, press 6.",
			"To set your current staffing level, press 7.",
			"Press 0 to repeat. Press 1 to respond to the scene.",
			"To hear the dispatch again, press 1. To hear response options, press 2.",
			"To choose a response option, enter the option number, then press pound.",
			"To hear the dispatch again, enter 0 and press pound.",
			"Press 0 to go back to the main menu.",
			"To go back to the main menu, enter 0 and press pound.",
			"To set your current status, enter the number of your selection, then press pound.",
			"To set your current staffing, enter the number of your selection, then press pound.",
			"Invalid status selection. Returning to the main menu.",
			"No status selection made. Returning to the main menu.",
			"Invalid staffing selection. Returning to the main menu.",
			"No staffing selection made. Returning to the main menu.",
			"Thank you. Your response has been recorded."
		};
	}
}