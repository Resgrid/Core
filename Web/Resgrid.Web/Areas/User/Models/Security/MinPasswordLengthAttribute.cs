using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Security
{
	/// <summary>
	/// Validates that a minimum password length value is either 0 (use system default) or between 8 and 128 inclusive.
	/// Values 1–7 are not allowed because they would create weaker security than the system default.
	/// </summary>
	public sealed class MinPasswordLengthAttribute : ValidationAttribute
	{
		private const string InvalidMessage = "Must be 0 (system default) or between 8 and 128.";

		public MinPasswordLengthAttribute()
			: base(InvalidMessage)
		{
		}

		public override bool IsValid(object value)
		{
			if (value is null)
				return true;

			if (value is int length)
				return length == 0 || (length >= 8 && length <= 128);

			return false;
		}

		protected override ValidationResult IsValid(object value, ValidationContext validationContext)
		{
			if (IsValid(value))
				return ValidationResult.Success;

			var memberNames = validationContext.MemberName is { } name ? new[] { name } : null;
			return new ValidationResult(InvalidMessage, memberNames);
		}
	}
}

