namespace Resgrid.Web.Services.Models.v4.CallFiles
{
	/// <summary>
	/// A Call file result
	/// </summary>
	public class CallFileResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Data payload
		/// </summary>
		public CallFileResultData Data { get; set; }
	}

	/// <summary>
	/// Object representing a file for a call in the Resgrid system
	/// </summary>
	public class CallFileResultData
	{
		/// <summary>
		/// ADP: true when this row belongs to a protection-enforced department (shield indicator).
		/// Protected values here are broker-decrypted plaintext or the exact "REDACTED" placeholder
		/// — never ciphertext.
		/// </summary>
		public bool IsProtected { get; set; }

		/// <summary>ADP: machine-readable reason when values are redacted (step_up_required,
		/// grant_expired, grant_revoked, protected_access_denied, broker_unavailable); null when
		/// nothing is redacted.</summary>
		public string ProtectedReason { get; set; }

		/// <summary>
		/// Id of the call file
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Id of the Call
		/// </summary>
		public string CallId { get; set; }

		/// <summary>
		/// Type of the file (Audio = 1, Image= 2, File	= 3, Video = 4)
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// Name of the File
		/// </summary>
		public string FileName { get; set; }

		/// <summary>
		/// Base64 File Data (may be null)
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// User friendly name of the file
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// Size of the file in bytes
		/// </summary>
		public int Size { get; set; }

		/// <summary>
		/// The Url to get the file instead of using the Data value
		/// </summary>
		public string Url { get; set; }

		/// <summary>
		/// User Id of the person who uploaded the file
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// Timestamp of when the file was added
		/// </summary>
		public string Timestamp { get; set; }

		/// <summary>
		/// Mime Type for the file
		/// </summary>
		public string Mime { get; set; }
	}
}
