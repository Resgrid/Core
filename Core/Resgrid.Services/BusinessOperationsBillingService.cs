using System;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.Services
{
	/// <summary>
	/// Proxy to the Billing API's BusinessOperationsBilling controller (int-CommonApis; plan decision 42), cloned
	/// from ReadinessProBillingService. Returns null / false when billing is not configured so callers fail closed.
	/// </summary>
	public sealed class BusinessOperationsBillingService : IBusinessOperationsBillingService
	{
		private readonly Func<RestClient> _client;

		public BusinessOperationsBillingService(Func<RestClient> client)
		{
			_client = client;
		}

		private async Task<T> CallAsync<T>(string action, int departmentId, bool post)
		{
			if (string.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
				return default;

			var request = new RestRequest("/api/BusinessOperationsBilling/" + action, post ? Method.Post : Method.Get);
			request.AddHeader("X-API-Key", ApiConfig.BackendInternalApikey);
			request.AddQueryParameter("departmentId", departmentId.ToString());

			var response = await _client().ExecuteAsync<T>(request);
			if (!response.IsSuccessful)
				Framework.Logging.LogError($"Business Operations billing '{action}' failed for department {departmentId}: {(int)response.StatusCode}.");

			return response.IsSuccessful ? response.Data : default;
		}

		public Task<BusinessOperationsBillingStatus> GetAsync(int departmentId) => CallAsync<BusinessOperationsBillingStatus>("Status", departmentId, false);
		public Task<BusinessOperationsCheckout> BeginCheckoutAsync(int departmentId) => CallAsync<BusinessOperationsCheckout>("Checkout", departmentId, true);
		public Task<bool> CancelRenewalAsync(int departmentId) => CallAsync<bool>("CancelRenewal", departmentId, true);
	}
}
