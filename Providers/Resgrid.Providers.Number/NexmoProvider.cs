using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using RestSharp;

namespace Resgrid.Providers.NumberProvider
{
    public class NexmoProvider : INumberProvider
    {
        public async Task<bool> ProvisionNumber(string country, string number)
        {
			var client = new RestClient(Config.NumberProviderConfig.BaseNexmoUrl);
			var request = new RestRequest(GenerateBuyNumberUrl(country, number), Method.Post);
			var response = await client.ExecuteAsync(request);

			if (response.StatusCode == HttpStatusCode.OK)
				return true;

            return false;
        }

		public async Task<List<TextNumber>> GetAvailableNumbers(string country, string areaCode)
        {
			var client = new RestClient(Config.NumberProviderConfig.BaseNexmoUrl);
			var request = new RestRequest(GenerateGetAvailableNumbersUrl(country), Method.Get);
			var response = await client.ExecuteAsync<FindNumberResulsts>(request);

			if (response.Data != null && response.Data.Numbers != null)
				return response.Data.Numbers;

            return null;
        }

        // Dedicated department numbers are bought from Twilio only (NumberProviderFactory); Nexmo never sells them.
        public Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(string countryCode, string areaCode, string contains, int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<AvailablePhoneNumber>());

        public Task<List<AvailablePhoneNumber>> SearchDedicatedNumbersAsync(DedicatedNumberSearch search, CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<AvailablePhoneNumber>());

        public Task<CarrierNumberPurchase> PurchaseDedicatedNumberAsync(string phoneNumber, string countryCode, string friendlyName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CarrierNumberPurchase { Provider = "Nexmo", Error = "Dedicated numbers are not sold through Nexmo." });

        public Task<bool> ReleaseDedicatedNumberAsync(string providerNumberId, CancellationToken cancellationToken = default) => Task.FromResult(false);

        public Task<CarrierNumberPurchase> FindDedicatedNumberAsync(string phoneNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<CarrierNumberPurchase>(null);

        private static string GenerateGetAvailableNumbersUrl(string country)
        {
            return string.Format("number/search/{0}/{1}/{2}?features=SMS", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, country);
        }

        private static string GenerateBuyNumberUrl(string country, string number)
        {
            return string.Format("number/buy/{0}/{1}/{2}/{3}", Config.NumberProviderConfig.NexmoApiKey, Config.NumberProviderConfig.NexmoApiSecret, country, number);
        }

		public string ConvertCountryToCode(string country)
		{
			switch (country)
			{
				case "United States":
					return "US";
				case "United Kingdom":
					return "UK";
				case "Australia":
					return "AU";
				case "Canada":
					return "CA";

				default:
					throw new Exception("Not supported country code for Nexmo numbers.");

			}
		}
    }

    internal class FindNumberResulsts
    {
        public List<TextNumber> Numbers { get; set; } 
    }
}
