using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Resgrid.Framework
{
	/// <summary>
	/// Validation attribute for password complexity requirements
	/// </summary>
	[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
	public sealed class PasswordComplexityAttribute : ValidationAttribute
	{
		public int MinLength { get; set; } = 8;
		public bool RequireUppercase { get; set; } = true;
		public bool RequireLowercase { get; set; } = true;
		public bool RequireDigit { get; set; } = true;
		public bool RequireSpecialChar { get; set; } = false;

		public PasswordComplexityAttribute()
		{
			ErrorMessage = "Password does not meet complexity requirements";
		}

		public override bool IsValid(object value)
		{
			if (value is not string password)
			{
				return false;
			}

			var result = StringHelpers.VerifyPasswordComplexity(
				password, 
				MinLength, 
				RequireUppercase, 
				RequireLowercase, 
				RequireDigit, 
				RequireSpecialChar);

			return result.IsValid;
		}

		protected override ValidationResult IsValid(object value, ValidationContext validationContext)
		{
			if (value is not string password)
			{
				return new ValidationResult("Invalid password format");
			}

			var result = StringHelpers.VerifyPasswordComplexity(
				password, 
				MinLength, 
				RequireUppercase, 
				RequireLowercase, 
				RequireDigit, 
				RequireSpecialChar);

			if (result.IsValid)
			{
				return ValidationResult.Success;
			}

			var errorMessage = string.Join(", ", result.Errors);
			return new ValidationResult(errorMessage, new[] { validationContext.MemberName });
		}
	}
}
