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
	/// Proxy to the Billing API's LmsBilling controller (int-CommonApis, owed; protocols-training-knowledge-plan §17.3 R-L10): the
	/// pack add-on path, built once so Workforce Pro can reuse it. Cloned from DedicatedNumberBillingService and given a quantity:
	/// Checkout carries Packs, ChangePacks carries Packs and the ExpectedPacks the admin saw. POST actions carry the department in
	/// a JSON body (the Billing API's DepartmentInput binds it). Returns null / false when billing is not configured or unreachable
	/// so callers fail closed; checkout is never an entitlement.
	/// </summary>
	public sealed class LmsBillingService : ILmsBillingService
	{
		private readonly Func<RestClient> _client;

		public LmsBillingService(Func<RestClient> client)
		{
			_client = client ?? throw new ArgumentNullException(nameof(client));
		}

		private async Task<T> CallAsync<T>(string action, int departmentId, bool post, object body = null)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(ApiConfig.BackendInternalApikey))
				return default;

			try
			{
				var request = new RestRequest("/api/LmsBilling/" + action, post ? Method.Post : Method.Get);
				request.AddHeader("X-API-Key", ApiConfig.BackendInternalApikey);
				if (post)
					request.AddJsonBody(body ?? new { DepartmentId = departmentId });
				else
					request.AddQueryParameter("departmentId", departmentId.ToString(CultureInfo.InvariantCulture));

				var response = await _client().ExecuteAsync<T>(request);
				if (response.IsSuccessful && response.StatusCode == HttpStatusCode.OK)
					return response.Data;

				Framework.Logging.LogError($"LMS billing '{action}' failed for department {departmentId}: HTTP {(int)response.StatusCode}, transport {response.ResponseStatus}, exception {response.ErrorException?.GetType().FullName}.");
				return default;
			}
			catch (Exception ex)
			{
				Framework.Logging.LogError($"LMS billing '{action}' failed for department {departmentId}: {ex.GetType().FullName}.");
				return default;
			}
		}

		public Task<LmsBillingStatus> GetAsync(int departmentId) => CallAsync<LmsBillingStatus>("Status", departmentId, false);

		public Task<LmsBillingCheckout> BeginCheckoutAsync(int departmentId, int packs) =>
			CallAsync<LmsBillingCheckout>("Checkout", departmentId, true, new { DepartmentId = departmentId, Packs = packs });

		public Task<LmsPackChangeResult> ChangePacksAsync(int departmentId, int packs, int expectedPacks) =>
			CallAsync<LmsPackChangeResult>("ChangePacks", departmentId, true, new { DepartmentId = departmentId, Packs = packs, ExpectedPacks = expectedPacks });

		public Task<bool> CancelRenewalAsync(int departmentId) => CallAsync<bool>("CancelRenewal", departmentId, true);
	}
}
