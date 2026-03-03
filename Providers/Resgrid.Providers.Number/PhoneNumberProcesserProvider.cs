using System;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	public class PhoneNumberProcesserProvider : IPhoneNumberProcesserProvider
	{
		public PhoneNumberResult Process(string phoneNumber, string countryCode = null)
		{
			var result = new PhoneNumberResult();

			try
			{
				var territory = string.IsNullOrWhiteSpace(countryCode) ? "US" : countryCode.ToUpperInvariant();

				// Normalize: strip non-digit characters except leading +
				var cleaned = phoneNumber?.Trim() ?? string.Empty;

				GlobalPhone.Number number;
				// Try with the given territory first
				if (GlobalPhone.GlobalPhone.TryParse(cleaned, out number, territory) && number.IsValid)
				{
					result.IsValid = true;
					result.InternationalNumber = number.InternationalString;
					result.LocalNumber = number.NationalString;
					return result;
				}

				// Try with no territory hint (for numbers starting with +)
				if (GlobalPhone.GlobalPhone.TryParse(cleaned, out number, "ZZ") && number.IsValid)
				{
					result.IsValid = true;
					result.InternationalNumber = number.InternationalString;
					result.LocalNumber = number.NationalString;
					return result;
				}

				result.IsValid = number != null && number.IsValid;
				if (number != null)
				{
					result.InternationalNumber = number.InternationalString;
					result.LocalNumber = number.NationalString;
				}
			}
			catch (Exception e)
			{
				result.IsValid = false;
				result.ErrorMessage = e.ToString();
			}

			return result;
		}
	}
}
