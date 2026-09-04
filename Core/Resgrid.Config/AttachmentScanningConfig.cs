namespace Resgrid.Config
{
	/// <summary>
	/// Malware scanning for Records attachments (RMS plan section 4.7) through a clamd daemon. Off by default:
	/// with scanning disabled every attachment is stored as Skipped, exactly as with no scanner registered.
	/// Environment keys: RESGRID:AttachmentScanningConfig:Enabled, :Host, :Port, :TimeoutSeconds, :FailClosed.
	/// </summary>
	public static class AttachmentScanningConfig
	{
		/// <summary>Master switch. When false the scanner never opens a connection.</summary>
		public static bool Enabled = false;

		/// <summary>clamd host (the ClamAV service name inside the compose/k8s network).</summary>
		public static string Host = "clamav";

		/// <summary>clamd TCP port.</summary>
		public static int Port = 3310;

		/// <summary>Connect, stream and reply budget for one scan.</summary>
		public static int TimeoutSeconds = 30;

		/// <summary>INSTREAM chunk size in bytes.</summary>
		public static int ChunkSize = 64 * 1024;

		/// <summary>
		/// What an unreachable or erroring engine means for the upload: true rejects the attachment (the safe
		/// default for a records system), false stores it as Pending with the reason on the row.
		/// </summary>
		public static bool FailClosed = true;
	}
}
