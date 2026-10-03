using System.ComponentModel.DataAnnotations;
using Resgrid.Framework;

namespace Resgrid.Web.Areas.User.Models.Security
{
	public class ChangeUsernameView
	{
		public string CurrentUsername { get; set; }
		public bool IsSsoManaged { get; set; }

		[Required, MaxLength(256)]
		[Display(Name = "New username")]
		public string NewUsername { get; set; }

		[Required, DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string CurrentPassword { get; set; }
	}

	/// <summary>Confirms the signed-in user's password before a credential change (passkey plan section 6.2).</summary>
	public class ReauthenticateView
	{
		public string ReturnUrl { get; set; }

		/// <summary>True when this account cannot confirm with a Resgrid password (SSO-managed or SSO required).</summary>
		public bool PasswordNotAllowed { get; set; }

		/// <summary>True when the department's provider can confirm who this is instead (Web SSO is set up for this department).</summary>
		public bool SsoAvailable { get; set; }

		[Required, DataType(DataType.Password)]
		[Display(Name = "Password")]
		public string Password { get; set; }

		/// <summary>The gate could not hold what the user submitted; after confirming they must submit it again.</summary>
		public bool SubmissionNotHeld { get; set; }

		/// <summary>The gate is holding what the user submitted; confirming finishes it.</summary>
		public bool HoldingSubmission =>
			ReturnUrl != null && ReturnUrl.Contains(Resgrid.Web.Helpers.StepUpFormReplay.ResumePath + "?", System.StringComparison.OrdinalIgnoreCase);
	}

	public class ChangePasswordView
	{
		public bool IsSsoManaged { get; set; }
		public int MinPasswordLength { get; set; } = 8;

		[Required, DataType(DataType.Password)]
		[Display(Name = "Current password")]
		public string CurrentPassword { get; set; }

		[Required]
		[StringLength(100, MinimumLength = 8)]
		[PasswordComplexity(MinLength = 8, RequireUppercase = true, RequireLowercase = true, RequireDigit = true, RequireSpecialChar = false)]
		[DataType(DataType.Password)]
		[Display(Name = "New password")]
		public string NewPassword { get; set; }

		[Required, DataType(DataType.Password)]
		[Compare(nameof(NewPassword), ErrorMessage = "The new password and confirmation password do not match.")]
		[Display(Name = "Confirm new password")]
		public string ConfirmPassword { get; set; }
	}
}
