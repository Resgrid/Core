using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Auth
{
	/// <summary>
	/// Object that verifies that the user's credentials
	/// </summary>
	public class ValidateInput
	{
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
