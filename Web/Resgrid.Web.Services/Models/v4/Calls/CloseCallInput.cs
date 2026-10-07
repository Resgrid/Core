using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Input information to close a call
	/// </summary>
	public class CloseCallInput
	{
		/// <summary>
		/// Call Id of the call to close
		/// </summary>
		[Required]
		public string Id { get; set; }

		/// <summary>
		/// Message or notes of the call to close
		/// </summary>
		public string Notes { get; set; }

		/// <summary>
		/// Type of call closure that is used
		/// </summary>
		[Required]
		public int Type { get; set; }

		/// <summary>
		/// Optional. When true, everyone attached to the call is told it was closed: the personnel, groups and roles it
		/// was dispatched to (Responder app, SMS, email per their notification settings), its units (Unit app) and the
		/// incident command team (IC app). The closing notes are not included in the notice.
		/// </summary>
		public bool? SendNotification { get; set; }
	}
}
