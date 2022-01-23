using System;
using System.Collections.Generic;
using System.Net;
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
