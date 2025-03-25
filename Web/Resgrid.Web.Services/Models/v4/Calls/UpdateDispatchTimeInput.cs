using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// Input data needed to update a calls scheduled dispatch time
	/// </summary>
	public class UpdateDispatchTimeInput
	{
		/// <summary>
		/// Id of the call to update
		/// </summary>
		[Required]
		public string Id { get; set; }

		/// <summary>
		/// Date in the future to update to
		/// </summary>
		[Required]
		public DateTime Date { get; set; }
	}
}
