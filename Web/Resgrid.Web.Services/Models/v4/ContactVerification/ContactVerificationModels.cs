using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Services.Models.v4.ContactVerification
{
	public class SendVerificationCodeInput
	{
		/// <summary>Which contact method to send a verification code to.</summary>
		[Required]
		public ContactVerificationType Type { get; set; }

		/// <summary>
		/// Department number used as the outbound number for SMS sends.
		/// Required when Type is MobileNumber or HomeNumber.
		/// </summary>
		public string DepartmentNumber { get; set; }
	}

	public class SendVerificationCodeResult
	{
		public bool Successful { get; set; }
		public string ErrorMessage { get; set; }
	}

	public class ConfirmVerificationCodeInput
	{
		/// <summary>Which contact method is being confirmed.</summary>
		[Required]
		public ContactVerificationType Type { get; set; }

		/// <summary>The numeric code the user received.</summary>
		[Required]
		public string Code { get; set; }
	}

	public class ConfirmVerificationCodeResult
	{
		public bool Successful { get; set; }
		public string ErrorMessage { get; set; }
	}
}

