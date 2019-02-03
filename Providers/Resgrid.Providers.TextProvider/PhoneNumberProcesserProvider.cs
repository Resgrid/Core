using System;
using GlobalPhone;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	public class PhoneNumberProcesserProvider : IPhoneNumberProcesserProvider
	{
		public PhoneNumberResult Process(string phoneNumber, string countryCode = null)
		{
			var result = new PhoneNumberResult();
			GlobalPhone.GlobalPhone.DbPath = $"{Framework.PathHelpers.GetAssemblyDirectory()}\\global_phone.json";

			try
			{
				var number = GlobalPhone.GlobalPhone.Parse(phoneNumber, countryCode);

				result.IsValid = number.IsValid;
				result.InternationalNumber = number.InternationalString;
				result.LocalNumber = number.LocalNumber;
			}
			catch (GlobalPhone.FailedToParseNumberException pne)
			{
				result.IsValid = false;
				result.ErrorMessage = pne.ToString();
			}

			return result;
		}
	}
}
