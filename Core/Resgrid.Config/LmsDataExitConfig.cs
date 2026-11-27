namespace Resgrid.Config
{
	/// <summary>
	/// Training T5 data exit (round 6D, worker 99): how long an export's download link lives, how much hosted media one zip may copy, how big
	/// a zip may be when the install has no LMS store and keeps it in the database, and where worker 99 writes the zip while building it.
	/// </summary>
	public static class LmsDataExitConfig
	{
		/// <summary>Days an export stays downloadable; worker 99 removes the zip afterwards (1–30).</summary>
		public static int DownloadLinkDays = 7;

		/// <summary>The most hosted media (MP4 renditions and caption tracks) one zip copies; the rest are listed as signed links.</summary>
		public static long MaxMediaBytes = 2L * 1024 * 1024 * 1024;

		/// <summary>The largest zip kept in the database on an install without the LMS store (media is never copied there).</summary>
		public static long MaxDatabaseZipBytes = 256L * 1024 * 1024;

		/// <summary>The folder worker 99 builds zips in; empty uses the system temp folder.</summary>
		public static string WorkDirectory = "";
	}
}
