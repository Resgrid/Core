using System;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Services;
using RestSharp;

namespace Resgrid.Services
{
	/// <summary>
	/// Proxy to the Billing API's AiBilling controller (int-CommonApis; enhanced-ai-addon-plan.md), cloned from
	/// BusinessOperationsBillingService. POST actions carry the department in a JSON body, which is what the Billing
	/// API's DepartmentInput binds. Returns null / false when billing is not configured or unreachable so callers
	/// fail closed; checkout is never an entitlement.
	/// </summary>
	public sealed class AiBillingService : IAiBillingService
	{
		private readonly Func<RestClient> _client;

		public AiBillingService(Func<RestClient> client)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
		}

		private async Task<T> CallAsync<T>(string action, int departmentId, bool post)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
				return default;

			try
			{
				var request = new RestRequest("/api/AiBilling/" + action, post ? Method.Post : Method.Get);
				request.AddHeader("X-API-Key", ApiConfig.BackendInternalApikey);
				if (post)
					request.AddJsonBody(new { DepartmentId = departmentId });
				else
					request.AddQueryParameter("departmentId", departmentId.ToString(CultureInfo.InvariantCulture));

				var response = await _client().ExecuteAsync<T>(request);
				if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
					return response.Data;

				Framework.Logging.LogError($"Enhanced AI billing '{action}' failed for department {departmentId}: HTTP {(int)response.StatusCode}, transport {response.ResponseStatus}, exception {response.ErrorException?.GetType().FullName}.");
				return default;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogError($"Enhanced AI billing '{action}' failed for department {departmentId}: {ex.GetType().FullName}.");
				return default;
			}
		}

		public Task<AiBillingStatus> GetAsync(int departmentId) => CallAsync<AiBillingStatus>("Status", departmentId, false);
		public Task<AiCheckout> BeginCheckoutAsync(int departmentId) => CallAsync<AiCheckout>("Checkout", departmentId, true);
		public Task<bool> CancelRenewalAsync(int departmentId) => CallAsync<bool>("CancelRenewal", departmentId, true);
	}
}
