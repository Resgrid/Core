using System;
using System.Linq;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	/// <summary>
	/// Example usage and tests for password complexity verification
	/// </summary>
	public static class PasswordComplexityExamples
	{
		/// <summary>
		/// Demonstrates how to use the password complexity verification
		/// </summary>
		public static void RunExamples()
		{
			// Test cases
			var testPasswords = new[]
			{
				"abc123", // Too short, no uppercase
				"ABC123", // No lowercase
				"abcDEF", // No digits
				"Abc123", // Valid according to default requirements
				"Password123", // Valid
				"MyPassword1", // Valid
				"", // Empty
				"Abc123!@#" // Valid with special chars
			};

			Console.WriteLine("Password Complexity Verification Examples:");
			Console.WriteLine("=========================================");

			foreach (var password in testPasswords)
			{
				var result = StringHelpers.VerifyPasswordComplexity(password);
				Console.WriteLine($"Password: '{password}'");
				Console.WriteLine($"Valid: {result.IsValid}");
				if (!result.IsValid)
				{
					Console.WriteLine($"Errors: {string.Join(", ", result.Errors)}");
				}
				Console.WriteLine();
			}

			// Test with custom requirements matching current RegisterViewModel
			Console.WriteLine("Resgrid Default Requirements:");
			Console.WriteLine("============================");
			
			var resgridDefaults = new[]
			{
				"abc123", // Should fail
				"Password1", // Should pass
				"MySecurePass123" // Should pass
			};

			foreach (var password in resgridDefaults)
			{
				var result = StringHelpers.VerifyPasswordComplexity(
					password, 
					minLength: 8,
					requireUppercase: true,
					requireLowercase: true,
					requireDigit: true,
					requireSpecialChar: false);
				
				Console.WriteLine($"Password: '{password}'");
				Console.WriteLine($"Valid: {result.IsValid}");
				if (!result.IsValid)
				{
					Console.WriteLine($"Errors: {string.Join(", ", result.Errors)}");
				}
				Console.WriteLine();
			}
		}

		/// <summary>
		/// Simple validation method for quick checks using Resgrid defaults
		/// </summary>
		public static bool ValidatePasswordForResgrid(string password)
		{
			return StringHelpers.VerifyPasswordComplexity(
				password,
				minLength: 8,
				requireUppercase: true,
				requireLowercase: true,
				requireDigit: true,
				requireSpecialChar: false).IsValid;
		}
	}
}
