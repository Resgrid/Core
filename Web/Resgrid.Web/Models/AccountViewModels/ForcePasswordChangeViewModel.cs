using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Models.AccountViewModels
{
	/// <summary>
	/// View model used when the system forces a user to change their expired password before proceeding.
	/// </summary>
	public class ForcePasswordChangeViewModel
	{
		[Required]
		[DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string CurrentPassword { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[Display(Name = "New password")]
		public string NewPassword { get; set; }

		[Required]
		[DataType(DataType.Password)]
		[Display(Name = "Confirm new password")]
		[Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
		public string ConfirmPassword { get; set; }

		/// <summary>Minimum required password length for the department, shown in the UI hint.</summary>
		public int MinPasswordLength { get; set; } = 8;
	}
}

