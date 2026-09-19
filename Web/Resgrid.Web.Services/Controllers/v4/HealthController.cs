using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Resgrid.Web.Services.Models.v4.Health;
using System.Reflection;
using Resgrid.Web.Services.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Call Priorities, for example Low, Medium, High. Call Priorities can be system provided ones or custom for a department
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class HealthController : V4AuthenticatedApiControllerbase
	{
		#region Members and Constructors
		private readonly IHealthService _healthService;
		private readonly IGlobalSearchService _globalSearch;
		private readonly IRecordsSearchService _recordsSearch;
		private readonly IInvoicePaymentsService _invoicePayments;

		public HealthController(IHealthService healthService, IGlobalSearchService globalSearch, IRecordsSearchService recordsSearch, IInvoicePaymentsService invoicePayments)
		{
			_healthService = healthService;
			_globalSearch = globalSearch;
			_recordsSearch = recordsSearch;
			_invoicePayments = invoicePayments;
		}
		#endregion Members and Constructors

		/// <summary>
		/// Gets the current users department rights
		/// </summary>
		/// <returns>DepartmentRightsResult object with the department rights and group memberships</returns>
		[HttpGet("GetCurrent")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[AllowAnonymous]
		public async Task<HealthResult> GetCurrent()
		{
			var result = new HealthResult();

			try
			{
				result.Data.ServicesVersion = Assembly.GetEntryAssembly().GetName().Version.ToString();
				result.Data.ApiVersion = "v4";
				result.Data.SiteId = "0";
				result.Data.CacheOnline = _healthService.IsCacheProviderConnected();

				// Unified Search plan R2.11: host state for this process. Never fails the health call.
				try
				{
					result.Data.SearchEnabled = Config.SearchConfig.Enabled;
					if (Config.SearchConfig.Enabled)
					{
						var global = await _globalSearch.GetHealthAsync();
						var records = await _recordsSearch.GetHealthAsync();
						result.Data.SearchOnline = global.Online;
						result.Data.SearchIndexDocCount = global.DocumentCount + records.DocumentCount;
					}
				}
				catch (System.Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Search health could not be read.");
					result.Data.SearchOnline = false;
				}

				// Workforce & Business Operations plan B2.5a: Stripe Connect webhook health for this process. Value-free
				// (no URL, secret, account, department or invoice id — this endpoint is anonymous). Never fails the health call.
				try
				{
					var payments = await _invoicePayments.GetWebhookHealthAsync();
					result.Data.PaymentsStripeConnectEnabled = payments.Enabled;
					result.Data.PaymentsWebhookConfigured = payments.WebhookConfigured;
					result.Data.PaymentsWebhookEndpointRegistered = payments.EndpointRegistered;
					result.Data.PaymentsWebhookLastReceivedOn = payments.LastEventReceivedOn;
					result.Data.PaymentsWebhookLastAppliedOn = payments.LastEventAppliedOn;
					result.Data.PaymentsWebhookStale = payments.Stale;
					result.Data.PaymentsWebhookRejectedLastHour = payments.RejectedLastHour;
					result.Data.PaymentsWebhookFailedLastHour = payments.FailedLastHour;
					result.Data.PaymentsOverdueOpenRequests = payments.OverdueOpenRequests;
					result.Data.PaymentsLastReconcileOn = payments.LastReconcileOn;
					result.Data.PaymentsWebhookHealthy = payments.Healthy;
				}
				catch (System.Exception ex)
				{
					Resgrid.Framework.Logging.LogException(ex, "Payments webhook health could not be read.");
					// Unknown state: only a cluster that has payment collection switched on is reported unhealthy.
					result.Data.PaymentsStripeConnectEnabled = Config.PaymentConnectConfig.Enabled;
					result.Data.PaymentsWebhookHealthy = !Config.PaymentConnectConfig.Enabled;
				}

				var dbTime = await _healthService.GetDatabaseTimestamp();

				if (!string.IsNullOrWhiteSpace(dbTime))
					result.Data.DatabaseOnline = true;
				else
					result.Data.DatabaseOnline = false;

				result.PageSize = 1;
				result.Status = ResponseHelper.Success;
			}
			catch (System.Exception)
			{
				result.PageSize = 0;
				result.Status = ResponseHelper.Failure;
			}

			ResponseHelper.PopulateV4ResponseData(result);

			return result;
		}
	}
}
