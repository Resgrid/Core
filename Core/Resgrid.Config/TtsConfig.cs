namespace Resgrid.Config
{
	/// <summary>
	/// Shared configuration for the Resgrid TTS microservice.
	/// Values are loaded through ConfigProcessor from ResgridConfig.json or RESGRID:* environment variables.
	/// </summary>
	public static class TtsConfig
	{
		public static string ServiceBaseUrl = "";
		public static string PlaybackBaseUrl = "";
		public static string StaticPromptAdminKey = "";
		public static int StaticPromptRefreshIntervalMinutes = 1440;
		public static int PlaybackMemoryCacheMinutes = 60;
		public static int PlaybackCacheControlSeconds = 86400;

		public static string S3Endpoint = "";
		public static string S3AccessKey = "";
		public static string S3SecretKey = "";
		public static string S3Bucket = "";
		public static string S3Region = "us-east-1";
		public static bool S3UseSsl = false;
		public static bool S3ForcePathStyle = true;
		public static bool S3UsePresignedUrls = true;
		public static int S3PresignedUrlExpiryMinutes = 60;
		public static string S3PublicBaseUrl = "";

		public static string DefaultVoice = "en-us+klatt4";
		public static int DefaultSpeed = 165;
		public static int MaxConcurrentGenerations = 4;
		public static int MaxTextLength = 1000;
		public static string EspeakExecutable = "espeak-ng";
		public static string FfmpegExecutable = "ffmpeg";
		public static string TempDirectory = "";
		public static string CachePrefix = "tts";
		public static int NormalizedSampleRate = 8000;
		public static int NormalizedChannels = 1;
		public static bool WarmupEnabled = true;
		public static string PreGeneratedPrompts = string.Join(";", new[]
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
		});

		public static int RateLimitPermitLimit = 60;
		public static int RateLimitQueueLimit = 10;
		public static int RateLimitWindowSeconds = 60;
	}
}
