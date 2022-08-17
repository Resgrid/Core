using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Twilio;
using Twilio.Base;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Rest.Api.V2010.Account.AvailablePhoneNumberCountry;
using Twilio.Types;

namespace Resgrid.Providers.NumberProvider
{
	public class TwilioProvider : INumberProvider
	{
		public async Task<bool> ProvisionNumber(string country, string number)
		{
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);

			try
			{
				var incomingPhoneNumber = await IncomingPhoneNumberResource.CreateAsync(phoneNumber: new PhoneNumber(number),
																		     smsUrl: new Uri(Config.NumberProviderConfig.TwilioApiUrl),
																			 smsMethod: "GET",
																			 voiceUrl: new Uri(Config.NumberProviderConfig.TwilioVoiceApiUrl),
																			 voiceMethod: "GET");
			}
			catch
			{
				return false;
			}

			return true;
		}

		public async Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode)
		{
			var availableNumbers = new List<TextNumber>();
			TwilioClient.Init(Config.NumberProviderConfig.TwilioAccountSid, Config.NumberProviderConfig.TwilioAuthToken);
			ResourceSet<LocalResource> numbers;

			//if (country == "US" || country == "CA" || country == "GB")
			//{
			//	if (!string.IsNullOrWhiteSpace(areaCode))
			//		numbers = await LocalResource.ReadAsync(country, areaCode: int.Parse(areaCode), smsEnabled: true);
			//	else
			//		numbers = await LocalResource.ReadAsync(country, smsEnabled: true);

			//	if (numbers != null)
			//	{
			//		foreach (var number in numbers)
			//		{
			//			var textNumber = new TextNumber();
			//			textNumber.msisdn = number.PhoneNumber.ToString();
			//			textNumber.country = number.IsoCountry;
			//			textNumber.Number = number.FriendlyName.ToString();

			//			availableNumbers.Add(textNumber);
			//		}
			//	}
			//}
			//else
			//{
				ResourceSet<MobileResource> mobileNumbers;

				if (!string.IsNullOrWhiteSpace(areaCode))
					mobileNumbers = await MobileResource.ReadAsync(country, areaCode: int.Parse(areaCode), smsEnabled: true);
				else
					mobileNumbers = await MobileResource.ReadAsync(country, smsEnabled: true);

				if (mobileNumbers != null)
				{
					foreach (var number in mobileNumbers)
					{
						var textNumber = new TextNumber();
						textNumber.msisdn = number.PhoneNumber.ToString();
						textNumber.country = number.IsoCountry;
						textNumber.Number = number.FriendlyName.ToString();

						availableNumbers.Add(textNumber);
					}
				}
			//}

			if (availableNumbers.Count > 10)
				return availableNumbers.Take(10).ToList();

			return availableNumbers;
		}

		public string ConvertCountryToCode(string country)
		{
			switch (country)
			{
				case "United States":
					return "US";
				case "United Kingdom":
					return "GB";
				case "Australia":
					return "AU";
				case "Canada":
					return "CA";

				default:
					throw new Exception("Not supported country code for Twilio numbers.");

			}
		}
	}
}
