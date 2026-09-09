using System;
using System.Net;
using System.Threading.Tasks;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Dedicated monthly billing API. Checkout is never an entitlement and cannot use a PTT quantity endpoint.</summary>
	public sealed class ReadinessProBillingService : IReadinessProBillingService
	{
		private async Task<T> CallAsync<T>(string action, int departmentId, bool post)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) || string.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey)) return default;
			try
			{
				using var client = new RestClient(new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl) { Timeout = TimeSpan.FromSeconds(10) }, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest("/api/ReadinessProBilling/" + action, post ? Method.Post : Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				if (post) request.AddJsonBody(new { DepartmentId = departmentId }); else request.AddQueryParameter("departmentId", departmentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
				var response = await client.ExecuteAsync<T>(request);
				return response.IsSuccessful && response.StatusCode == HttpStatusCode.OK ? response.Data : default;
			}
			catch { return default; }
		}
		public Task<ReadinessProBillingStatus> GetAsync(int departmentId) => CallAsync<ReadinessProBillingStatus>("Status", departmentId, false);
		public Task<ReadinessProCheckout> BeginCheckoutAsync(int departmentId) => CallAsync<ReadinessProCheckout>("Checkout", departmentId, true);
		public Task<bool> CancelRenewalAsync(int departmentId) => CallAsync<bool>("CancelRenewal", departmentId, true);
	}
}
