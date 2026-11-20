using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;

namespace Resgrid.Providers.NumberProvider
{
	public class NumberProviderFactory: INumberProvider
	{
		private readonly TwilioProvider _twilioProvider;
		private readonly NexmoProvider _nexmoProvider;

		public NumberProviderFactory()
		{
			_twilioProvider = new TwilioProvider();
			_nexmoProvider = new NexmoProvider();
		}

		public async Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode)
		{
			//if (country == "AU")
			//	return _nexmoProvider.GetAvailableNumbers(country, areaCode);

			return await _twilioProvider.GetAvailableNumbers(country, areaCode);
		}

		public async Task<bool> ProvisionNumber(string country, string number)
		{
			//if (country == "AU")
			//	return _nexmoProvider.ProvisionNumber(country, number);

			return await _twilioProvider.ProvisionNumber(country, number);
		}

		// Dedicated numbers are Twilio only: the webhooks, regulatory bundles and release all speak the Twilio API.
		public Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(string countryCode, string areaCode, string contains, int limit,
			CancellationToken cancellationToken = default) =>
			_twilioProvider.SearchDedicatedNumbersAsync(countryCode, areaCode, contains, limit, cancellationToken);

		public Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(DedicatedNumberSearch search, CancellationToken cancellationToken = default) =>
			_twilioProvider.SearchDedicatedNumbersAsync(search, cancellationToken);

		public Task<CarrierNumberPurchase> PurchaseDedicatedNumberAsync(string phoneNumber, string countryCode, string friendlyName,
			CancellationToken cancellationToken = default) =>
			_twilioProvider.PurchaseDedicatedNumberAsync(phoneNumber, countryCode, friendlyName, cancellationToken);

		public Task<bool> ReleaseDedicatedNumberAsync(string providerNumberId, CancellationToken cancellationToken = default) =>
			_twilioProvider.ReleaseDedicatedNumberAsync(providerNumberId, cancellationToken);

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
				case "Ireland":
					return "IE";
				case "New Zealand":
					return "NZ";

				default:
					throw new Exception("Not supported country code for Twilio numbers.");

			}
		}
	}
}
