using Microsoft.AspNetCore.Mvc.ModelBinding;
using Resgrid.Framework;
using Resgrid.Model.Providers;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// Server-side phone-number backstop shared by the phone-entry controllers (matches the EditUserProfile flow).
	/// Validates a number (blanks are allowed/skipped) and returns its canonical E.164 form so callers can persist a
	/// Twilio-sendable value. On an invalid number it adds a ModelState error against <paramref name="fieldKey"/>
	/// (so the matching asp-validation-for span shows it) and returns the original value unchanged.
	/// </summary>
	public static class PhoneValidationHelper
	{
		public static string ValidateAndNormalize(IPhoneNumberProcesserProvider processer, ModelStateDictionary modelState,
			string fieldKey, string label, string number, string countryName)
		{
			if (string.IsNullOrWhiteSpace(number))
				return number;

			var result = processer?.Process(number, PhoneRegionHelper.ToIso(countryName));
			if (result == null || !result.IsValid || string.IsNullOrWhiteSpace(result.InternationalNumber))
			{
				modelState.AddModelError(fieldKey,
					$"The {label} doesn't look valid. Enter it in full international format, starting with the country code (for example +27 82 446 1783).");
				return number;
			}

			return result.InternationalNumber;
		}
	}
}
