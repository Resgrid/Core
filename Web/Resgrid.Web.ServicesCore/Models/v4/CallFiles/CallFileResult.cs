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
