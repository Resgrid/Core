using System.Collections.Generic;

namespace Resgrid.Web.Services.Models.v4.Statuses
{
	/// <summary>
	/// A status for personnel and units in the Resgrid system
	/// </summary>
	public class StatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public StatusResultData Data { get; set; }
	}

	/// <summary>
	/// A status
	/// </summary>
	public class StatusResultData
	{
		/// <summary>
		/// Id
		/// </summary>
		public int Id { get; set; }

		/// <summary>
		/// Type of the status
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// State Id
		/// </summary>
		public int StateId { get; set; }

		/// <summary>
		/// Text of the status
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// Button color
		/// </summary>
		public string BColor { get; set; }

		/// <summary>
		/// Text color
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// Does status require gps
		/// </summary>
		public bool Gps { get; set; }

		/// <summary>
		/// Does the status require a note
		/// </summary>
		public int Note { get; set; }

		/// <summary>
		/// Does the status require responding to detail
		/// </summary>
		public int Detail { get; set; }

		/// <summary>
		/// Is this status deleted (should only be used for display)
		/// </summary>
		public bool IsDeleted { get; set; }
	}
}
