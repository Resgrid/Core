using System;
using System.Net;
using Resgrid.Model;
using Resgrid.Model.Providers;
using RestSharp;
using RestSharp.Extensions.MonoHttp;

namespace Resgrid.Providers.AddressVerification
{
	public class LoqateProvider : IAddressVerificationProvider
	{
		public AddressVerificationResult VerifyAddress(Address address)
		{
			var result = new AddressVerificationResult();

			try
			{
				string addressString = string.Format("{0} {1} {2} {3} {4}", address.Address1, address.City, address.State, address.PostalCode, address.Country);

				var client = new RestClient(Config.MappingConfig.LoqateApiUrl);
				var request = new RestRequest(String.Format("rest/?lqtkey={0}&p=v&addr={1}", Config.MappingConfig.LoqateApiKey, HttpUtility.UrlEncode(addressString)), Method.GET);

				var response = client.Execute<LoqateAddressVerificationResult>(request);

				if (response.StatusCode != HttpStatusCode.OK)
					result.ServiceSuccess = false;

				if (response.Data.status != "OK")
					result.ServiceSuccess = false;

				if (response.Data.results.Count <= 0)
					result.ServiceSuccess = false;

				var code = new VerificationCode(response.Data.results[0].AVC);
				result.ServiceSuccess = true;

				if (code.WasAddressVerified() && code.WasAddressedParsed() && code.VerifiedToPremises())
					result.AddressValid = true;
			}
			catch (Exception ex)
			{
				result.ServiceSuccess = false;
				result.Exception = ex;
			}

			return result;
		}
	}
}
