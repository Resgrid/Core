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
	}
}
