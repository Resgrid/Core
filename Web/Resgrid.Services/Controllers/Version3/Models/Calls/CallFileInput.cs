using System;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Calls
{
	/// <summary>
	/// Input to attach a file to a call
	/// </summary>
	public class CallFileInput
	{
		/// <summary>
		/// Id of the Call
		/// </summary>
		public int Cid { get; set; }

		/// <summary>
		/// User Id of the user attaching the file
		/// </summary>
		public string Uid { get; set; }

		/// <summary>
		/// Type of the file (Audio = 1, Image = 2, File = 3, Video	= 4)
		/// </summary>
		public int Typ { get; set; }

		/// <summary>
		/// Name of the file
		/// </summary>
		public string Nme { get; set; }

		/// <summary>
		/// Base64 encoded string of the file being uploaded
		/// </summary>
		public string Data { get; set; }

		public string Lat { get; set; }

		public string Lon { get; set; }

		public string Not { get; set; }
	}
}