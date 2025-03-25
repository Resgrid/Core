using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	/// <summary>
	/// Object representing a file for a call in the Resgrid system
	/// </summary>
	public class CallFileResult
	{
		/// <summary>
		/// Id of the call file
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Id of the Call
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// Type of the file (Audio = 1, Image= 2, File	= 3, Video = 4)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Name of the File
		/// </summary>
		public string Fln { get; set; }

		/// <summary>
		/// Base64 File Data (may be null)
		/// </summary>
		public string Data { get; set; }

		/// <summary>
		/// User friendly name of the file
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// Size of the file in bytes
		/// </summary>
		public int Sze { get; set; }

		/// <summary>
		/// The Url to get the file instead of using the Data value
		/// </summary>
		public string Url { get; set; }

		/// <summary>
		/// User Id of the person who uploaded the file
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Timestamp of when the file was added
		/// </summary>
		public string Tme { get; set; }

		/// <summary>
		/// Mime Type for the file
		/// </summary>
		public string Mime { get; set; }
	}
}