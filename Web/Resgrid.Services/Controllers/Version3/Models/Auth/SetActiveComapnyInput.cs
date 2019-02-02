using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Auth
{
	public class SetActiveComapnyInput
	{
		/// <summary>
		/// New Active Department Id
		/// </summary>
		[Required]
		public int Did { get; set; }

		/// <summary>
		/// User name of the user
		/// </summary>
		[Required]
		public string Usr { get; set; }

		/// <summary>
		/// The password of the user
		/// </summary>
		[Required]
		public string Pass { get; set; }
	}
}