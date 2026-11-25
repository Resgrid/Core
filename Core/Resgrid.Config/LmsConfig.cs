namespace Resgrid.Config
{
	/// <summary>
	/// Training T2 LMS hosted video (decision 9): the RustFS (S3-compatible) bucket the LMS media lives in, separate from the
	/// import and search buckets so lifecycle and access policies stay independent, and the ffmpeg worker 87 transcodes with.
	/// An empty endpoint disables uploads and hosted streaming (YouTube, Vimeo and Academy videos still play).
	/// </summary>
	public static class LmsConfig
	{
		/// <summary>S3-compatible endpoint, e.g. https://rustfs.internal:9000. Empty disables hosted video.</summary>
		public static string S3Endpoint = "";

		public static string S3AccessKey = "";

		public static string S3SecretKey = "";

		public static string S3Bucket = "resgrid-lms";

		public static string S3Region = "us-east-1";

		public static bool S3UseSsl = true;

		public static bool S3ForcePathStyle = true;

		/// <summary>Key prefix inside the bucket; objects live under {S3Prefix}/{departmentId}/{assetId}/...</summary>
		public static string S3Prefix = "lms";

		/// <summary>How long a signed segment or rendition URL stays valid.</summary>
		public static int SignedUrlMinutes = 60;

		/// <summary>The ffmpeg executable worker 87 runs (separate from TtsConfig.FfmpegExecutable, which the TTS service uses).</summary>
		public static string FfmpegExecutable = "ffmpeg";

		/// <summary>The ffprobe executable used to read a video's duration.</summary>
		public static string FfprobeExecutable = "ffprobe";

		/// <summary>The longest a single transcode may run before worker 87 gives up on it.</summary>
		public static int TranscodeTimeoutMinutes = 90;

		/// <summary>The largest video upload accepted (2 GB).</summary>
		public static long MaxVideoUploadBytes = 2L * 1024 * 1024 * 1024;

		/// <summary>The largest WebVTT caption track accepted (1 MB).</summary>
		public static long MaxCaptionBytes = 1024 * 1024;

		/// <summary>Hosted storage included with the add-on per department (100 GB, decision 7). Reported only; no overage billing.</summary>
		public static long IncludedStorageBytes = 100L * 1024 * 1024 * 1024;

		/// <summary>The default per-device offline cache cap for a lesson marked available offline (R-C9).</summary>
		public static long DefaultOfflineMaxBytes = 500L * 1024 * 1024;

		/// <summary>The default number of days an offline copy may stay on a device.</summary>
		public static int DefaultOfflineExpiryDays = 14;

		/// <summary>An in-progress course idle this many days gets a reminder (worker 88), at most once per idle period.</summary>
		public static int InProgressReminderIdleDays = 7;
	}
}
