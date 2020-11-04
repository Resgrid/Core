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

			// TODO: ¯\_(ツ)_/¯ -SJ http://globalphone.github.io/
			//GlobalPhone.GlobalPhone.DbPath = $"{Framework.PathHelpers.GetAssemblyDirectory()}\\global_phone.json";

			try
			{
				var number = GlobalPhone.GlobalPhone.Parse(phoneNumber, countryCode);

				result.IsValid = number.IsValid;
				result.InternationalNumber = number.InternationalString;
				result.LocalNumber = number.LocalNumber;
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
