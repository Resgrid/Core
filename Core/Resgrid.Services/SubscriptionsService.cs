using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using MongoDB.Driver;
using Resgrid.Model;
using Resgrid.Model.Billing.Api;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using RestSharp;
using RestSharp.Serializers.NewtonsoftJson;

namespace Resgrid.Services
{
	public class SubscriptionsService : ISubscriptionsService
	{
		private static string CacheKey = "CurrentPayment_{0}";
		private static string PaymentAddonCacheKey = "CurrentAddonPayment_{0}_{1}";
		private static string ExternalPlanCacheKey = "ExternalPlan_{0}";
		private static string PlanCacheKey = "Plan_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(1);
		private static TimeSpan SixMonthCacheLength = TimeSpan.FromDays(180);

		private const int FreePlanId = 1;

		private readonly IPlansRepository _plansRepository;
		private readonly IPaymentRepository _paymentsRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsRepository _departmentsRepository;
		private readonly IPlanAddonsRepository _planAddonsRepository;
		private readonly IPaymentAddonsRepository _paymentAddonsRepository;
		private readonly IDepartmentSettingsRepository _departmentSettingsRepository;
		private readonly IPaymentProviderEventsRepository _paymentProviderEventsRepository;

		public SubscriptionsService(IPlansRepository plansRepository, IPaymentRepository paymentsRepository, ICacheProvider cacheProvider,
			IDepartmentsRepository departmentsRepository, IPlanAddonsRepository planAddonsRepository, IPaymentAddonsRepository paymentAddonsRepository,
			IDepartmentSettingsRepository departmentSettingsRepository, IPaymentProviderEventsRepository paymentProviderEventsRepository)
		{
			_plansRepository = plansRepository;
			_paymentsRepository = paymentsRepository;
			_cacheProvider = cacheProvider;
			_departmentsRepository = departmentsRepository;
			_planAddonsRepository = planAddonsRepository;
			_paymentAddonsRepository = paymentAddonsRepository;
			_departmentSettingsRepository = departmentSettingsRepository;
			_paymentProviderEventsRepository = paymentProviderEventsRepository;
		}

		public async Task<Model.Plan> GetCurrentPlanForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
			var freePlan = await _plansRepository.GetByIdAsync(FreePlanId);

			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetCurrentPlanForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPlanForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return freePlan;

				if (response.Data == null)
					return freePlan;

				return response.Data.Data;
			}

			return freePlan;
		}

		public async Task<DepartmentPlanCount> GetPlanCountsForDepartmentAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};
				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());

				var request = new RestRequest($"/api/Billing/GetPlanCountsForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanCountsForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return new DepartmentPlanCount();
		}

		public async Task<Payment> GetCurrentPaymentForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetCurrentPaymentForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPaymentForDepartment>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			async Task<Payment> getPayment()
			{
				var dateTime = DateTime.UtcNow;

				var payment = (from p in await _paymentsRepository.GetAllAsync()
							   where p.DepartmentId == departmentId && p.EffectiveOn <= dateTime && p.EndingOn >= dateTime
							   orderby p.PaymentId descending
							   select p).FirstOrDefault();

				// Sometimes were not getting the plan back, need to get it from the db.
				if (payment != null && payment.Plan == null)
					payment.Plan = await GetPlanByIdAsync(payment.PlanId);

				return payment;
			};

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Payment>(string.Format(CacheKey, departmentId), getPayment, CacheLength);
			}

			return await getPayment();
		}

		public async Task<Payment> GetPreviousNonFreePaymentForDepartmentAsync(int departmentId, int paymentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPreviousNonFreePaymentForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddParameter("paymentId", paymentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPaymentForDepartment>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			// I went with amount here as there could be preview payments, demo payments, etc in the system, no just Plans.FreePaymentId.
			var payment = (from p in await _paymentsRepository.GetAllAsync()
						   where p.DepartmentId == departmentId && p.PaymentId < paymentId && p.Amount != 0
						   orderby p.PaymentId descending
						   select p).FirstOrDefault();

			return payment;
		}

		public async Task<Payment> GetUpcomingPaymentForDepartmentAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetUpcomingPaymentForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPaymentForDepartment>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			var payment = (from p in await _paymentsRepository.GetAllAsync()
						   where p.DepartmentId == departmentId && p.EffectiveOn > DateTime.UtcNow
						   orderby p.PaymentId descending
						   select p).FirstOrDefault();

			return payment;
		}

		public async Task<Payment> GetPaymentByTransactionIdAsync(string transactionId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPaymentByTransactionId", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("transactionId", transactionId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPaymentForDepartment>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return await _paymentsRepository.GetPaymentByTransactionIdAsync(transactionId);
		}

		public async Task<Model.Plan> GetPlanByIdAsync(int planId, bool byPassCache = false)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPlanById", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("planId", planId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPlanForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			async Task<Model.Plan> getPlan()
			{
				return await _plansRepository.GetPlanByPlanIdAsync(planId);
			}

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Model.Plan>(string.Format(PlanCacheKey, planId), getPlan, SixMonthCacheLength);
			}

			return await getPlan();
		}

		public async Task<Model.Plan> GetPlanByExternalIdAsync(string externalId, bool byPassCache = false)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPlanByExternalId", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("externalId", externalId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPlanForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			async Task<Model.Plan> getPlan()
			{
				return (from p in await _plansRepository.GetAllAsync()
						where p.ExternalId == externalId || p.TestExternalId == externalId
						select p).FirstOrDefault();
			}

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Model.Plan>(string.Format(ExternalPlanCacheKey, externalId), getPlan, SixMonthCacheLength);
			}

			return await getPlan();
		}

		public async Task<Payment> GetPaymentByIdAsync(int paymentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var options = new RestClientOptions(Config.SystemBehaviorConfig.BillingApiBaseUrl)
				{
					MaxTimeout = 200000 // ms
				};

				var client = new RestClient(options, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPaymentById", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("paymentId", paymentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCurrentPaymentForDepartment>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			var payment = await _paymentsRepository.GetPaymentByIdIdAsync(paymentId);
			payment.Department = await _departmentsRepository.GetByIdAsync(payment.DepartmentId);

			return payment;
		}

		public bool ValidateUserSelectableBuyNowPlan(int planId)
		{
			/* The issue here is that only the Standard, Premium, Professional, Ultimate and Enterprise
			 * plans can be selected by a user to buy now, this will prevent them from changing
			 * the query to a free plan, like Beta 2yr, Unlimited Free or Open preview.
			 */
			if (planId == 20 || planId == 21 || planId == 22 || planId == 23 || planId == 24 || planId == 25 || planId == 26 ||
				planId == 27 || planId == 28 || planId == 29 || planId == 30 || planId == 31 || planId == 32 || planId == 33)
				return true;

			return false;
		}

		public bool CanPlanSendMessageSms(int planId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (planId == 1)
					return false;

				return true;
			}

			return true;
		}

		public bool CanPlanSendCallSms(int planId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (planId == 1)
					return false;

				return true;
			}

			return true;
		}

		public async Task<Payment> SavePaymentAsync(Payment payment, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _paymentsRepository.SaveOrUpdateAsync(payment, cancellationToken);
			_cacheProvider.Remove(string.Format(CacheKey, payment.DepartmentId));

			return saved;
		}

		public async Task<Payment> UpdatePaymentAsync(Payment payment, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _paymentsRepository.SaveOrUpdateAsync(payment, cancellationToken);
			_cacheProvider.Remove(string.Format(CacheKey, payment.DepartmentId));

			return saved;
		}

		public async Task<Payment> InsertPaymentAsync(Payment payment, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _paymentsRepository.SaveOrUpdateAsync(payment, cancellationToken);
			_cacheProvider.Remove(string.Format(CacheKey, payment.DepartmentId));

			return saved;
		}

		public async Task<PaymentAddon> InsertPaymentAddonAsync(PaymentAddon paymentAddon, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _paymentAddonsRepository.SaveOrUpdateAsync(paymentAddon, cancellationToken);

			return saved;
		}

		public void ClearCacheForCurrentPayment(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public async Task<List<Payment>> GetAllPaymentsForDepartmentAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetAllPaymentsForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetAllPaymentsForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			var payments = await _paymentsRepository.GetAllPaymentsByDepartmentIdAsync(departmentId);

			foreach (var payment in payments)
			{
				payment.Department = await _departmentsRepository.GetByIdAsync(payment.DepartmentId);
			}

			return payments.ToList();
		}

		public async Task<List<Payment>> GetAllNonFreePaymentsForDepartmentAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetAllNonFreePaymentsForDepartment", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetAllPaymentsForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			var payments = from p in await _paymentsRepository.GetAllPaymentsByDepartmentIdAsync(departmentId)
						   where p.Amount > 0
						   select p;

			return payments.ToList();
		}

		public List<int> GetPossibleUpgradesForPlan(int planId)
		{
			List<int> plans = new List<int>();

			return plans;
		}

		public List<int> GetPossibleDowngradesForPlan(int planId)
		{
			List<int> plans = new List<int>();

			return plans;
		}

		public bool IsPlanRestrictedOrFree(int planId)
		{
			return false;
		}

		/// <summary>
		/// Gets the difference in cost between the current payment's plan and a new plan.
		/// Takes into account the cost difference and how many days in the plan is to upgrade
		/// the remainder to the higher plan.
		/// </summary>
		/// <param name="paymentId">PaymentId of the current plan to upgrade</param>
		/// <param name="planId">PlanId of the plan to upgrade to</param>
		/// <returns>Amount that the user should be charged for this upgrade</returns>
		public async Task<double> GetAdjustedUpgradePriceAsync(int paymentId, int planId)
		{
			var payment = await GetPaymentByIdAsync(paymentId);
			var plan = await GetPlanByIdAsync(planId);

			// Both the original payment and the new plan have the same billing frequency, i.e. yearly/monthly.
			if (payment.Plan.Frequency == plan.Frequency)
			{
				// This shouldn't happen, the upgraded plan should always cost more then current plan
				if (plan.Cost < payment.Amount)
					return 0;

				double adjustedPrice = plan.Cost - payment.Amount;

				var days = DateTime.UtcNow.Subtract(payment.EffectiveOn).TotalDays;
				days = Math.Round(days, MidpointRounding.ToEven);

				if (days < 0)
					return 0;
				else if (days > 365)
					days = 0;

				double dayCost = adjustedPrice / 365;

				return adjustedPrice - (days * dayCost);
			}
			else
			{
				if (plan.Frequency == (int)PlanFrequency.Monthly && payment.Plan.Frequency == (int)PlanFrequency.Yearly)
				{
					var days = DateTime.UtcNow.Subtract(payment.EffectiveOn).TotalDays;
					days = Math.Round(days, MidpointRounding.ToEven);

					if (days < 0)
						return 0;

					double dayCost = payment.Plan.Cost / 365;
					return (365 - days) * dayCost;
				}
				else
				{
					var days = DateTime.UtcNow.Subtract(payment.EffectiveOn).TotalDays;
					days = Math.Round(days, MidpointRounding.ToEven);

					if (days < 0)
						return 0;

					double dayCost = plan.Cost / 365;

					return plan.Cost - (days * dayCost);
				}

			}

			return 0;
		}

		public Tuple<int, double> CalculateCyclesTillFirstBill(double balance, double cost)
		{
			int cycles = 0;
			double remainder = -balance;

			while (remainder <= 0)
			{
				cycles++;
				remainder += cost;
			}

			return new Tuple<int, double>(cycles, remainder);
		}

		public async Task<Payment> CreateFreePlanPaymentAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			Payment payment = new Payment();
			payment.DepartmentId = departmentId;
			payment.PurchasingUserId = userId;
			payment.PlanId = FreePlanId;
			payment.Method = (int)PaymentMethods.System;
			payment.IsTrial = false;
			payment.IsUpgrade = false;
			payment.PurchaseOn = DateTime.UtcNow;
			payment.TransactionId = "SYSTEM";
			payment.Successful = true;
			payment.Data = String.Empty;
			payment.Description = "Free Forever Plan";
			payment.EffectiveOn = DateTime.UtcNow.AddDays(-1);
			payment.Amount = 0.00;
			payment.EndingOn = DateTime.MaxValue;

			var saved = await _paymentsRepository.SaveOrUpdateAsync(payment, cancellationToken);
			ClearCacheForCurrentPayment(departmentId);

			return saved;
		}

		public async Task<List<PaymentAddon>> GetCurrentPaymentAddonsForDepartmentAsync(int departmentId, List<string> planAddonIds)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (planAddonIds == null || planAddonIds.Count == 0)
					return new List<PaymentAddon>();

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetCurrentPaymentAddonsForDepartmentPost", Method.Post);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json; charset=utf-8");
				//request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddBody(new { DepartmentId = departmentId, PlanAddonIds = planAddonIds.ToArray() });

				var response = await client.ExecuteAsync<GetAllPaymentAddonsForDepartmentResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			List<PaymentAddon> paymentAddons = new List<PaymentAddon>();

			if (planAddonIds != null && planAddonIds.Any())
			{
				foreach (var planAddonId in planAddonIds)
				{
					PaymentAddon addon = new PaymentAddon();
					addon.DepartmentId = departmentId;
					addon.PlanAddonId = planAddonId;
					addon.TransactionId = "SYSTEM";
					addon.Description = "Addon Forever";
					addon.Amount = 0.00;
					addon.IsCancelled = false;
					addon.EffectiveOn = DateTime.UtcNow.AddDays(-1);
					addon.EndingOn = DateTime.MaxValue;

					paymentAddons.Add(addon);
				}
			}

			return paymentAddons;
		}

		public async Task<List<PlanAddon>> GetAllAddonPlansByTypeAsync(PlanAddonTypes planAddonType)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetAllAddonPlansByType", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("type", (int)planAddonType, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetAllPlanAddonsByTypeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			List<PlanAddon> addons = new List<PlanAddon>();

			PlanAddon addon = new PlanAddon();
			addon.AddonType = 1;
			addon.Cost = 0;

			addons.Add(addon);

			return addons;
		}

		public async Task<List<PlanAddon>> GetAllAddonPlansAsync()
		{
			var plans = (from p in await _planAddonsRepository.GetAllAsync()
						 select p).ToList();

			return plans;
		}

		public async Task<PlanAddon> GetPlanAddonByExternalIdAsync(string externalId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPlanAddonByExternalId", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("externalId", externalId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanAddonByExternalResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			var plan = (from p in await _planAddonsRepository.GetAllAsync()
						where p.ExternalId == externalId
						select p).FirstOrDefault();

			return plan;
		}

		public async Task<List<PlanAddon>> GetCurrentPlanAddonsForDepartmentFromStripeAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetCurrentPlanAddonsForDepartmentFromStripe", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetAllPlanAddonsByTypeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			List<PlanAddon> addons = new List<PlanAddon>();

			PlanAddon addon = new PlanAddon();
			addon.AddonType = 1;
			addon.Cost = 0;

			addons.Add(addon);

			return addons;
		}

		public async Task<bool> HasActiveSubForDepartmentFromStripeAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetHasActiveSubForDepartmentFromStripe", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetHasActiveSubForDepartmentFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return false;

				if (response.Data == null)
					return false;

				return response.Data.Data;
			}

			return true;
		}

		public async Task<PlanAddon> GetPTTAddonPlanForDepartmentFromStripeAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPTTAddonPlanForDepartmentFromStripe", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanAddonByExternalResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			PlanAddon addon = new PlanAddon();
			addon.AddonType = 1;
			addon.Cost = 0;

			return addon;
		}

		public async Task<PlanAddon> GetPTTAddonForCurrentSubAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPTTAddonForCurrentSub", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanAddonByExternalResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			PlanAddon addon = new PlanAddon();
			addon.AddonType = 1;
			addon.Cost = 0;

			return addon;
		}

		public async Task<PlanAddon> GetPlanAddonByIdAsync(string planAddonId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetPlanAddonById", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("planAddonId", planAddonId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanAddonByExternalResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			PlanAddon addon1 = new PlanAddon();
			addon1.AddonType = 1;
			addon1.Cost = 0;

			return addon1;
		}

		public async Task<GetCanceledPlanFromStripeData> GetCanceledPlanFromStripeAsync(int departmentId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetCanceledPlanFromStripe", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCanceledPlanFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<PlanAddon> AddAddonAddedToExistingSub(int departmentId, Model.Plan plan, PlanAddon addon)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/AddAddonAddedToExistingSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddParameter("planId", plan.PlanId, ParameterType.QueryString);
				request.AddParameter("planAddonId", addon.PlanAddonId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetPlanAddonByExternalResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			PlanAddon addon1 = new PlanAddon();
			addon1.AddonType = 1;
			addon1.Cost = 0;

			return addon1;
		}

		public async Task<bool> CancelPlanAddonByTypeFromStripeAsync(int departmentId, int addonType)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/CancelPlanAddonByTypeFromStripe", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddParameter("addonType", addonType, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetHasActiveSubForDepartmentFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return false;

				if (response.Data == null)
					return false;

				return response.Data.Data;
			}

			return true;
		}

		public async Task<PaymentAddon> SavePaymentAddonAsync(PaymentAddon paymentAddon, CancellationToken cancellationToken = default(CancellationToken))
		{
			var saved = await _paymentAddonsRepository.SaveOrUpdateAsync(paymentAddon, cancellationToken);

			return saved;
		}

		public async Task<CreateStripeSessionForUpdateData> CreateStripeSessionForUpdate(int departmentId, string stripeCustomerId, string email, string departmentName)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/CreateStripeSessionForUpdate", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);
				request.AddParameter("email", email, ParameterType.QueryString, true);
				request.AddParameter("departmentName", departmentName, ParameterType.QueryString, true);

				var response = await client.ExecuteAsync<CreateStripeSessionForUpdateResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<GetCanceledPlanFromStripeData> GetActiveStripeSubscriptionAsync(string stripeCustomerId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetActiveStripeSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCanceledPlanFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<GetCanceledPlanFromStripeData> GetActivePTTStripeSubscriptionAsync(string stripeCustomerId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/GetActivePTTStripeSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetCanceledPlanFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<bool> ModifyPTTAddonSubscriptionAsync(string stripeCustomerId, long quantity, PlanAddon planAddon)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/ModifyPTTAddonSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);
				request.AddParameter("quantity", quantity, ParameterType.QueryString);
				request.AddParameter("planAddonId", planAddon.PlanAddonId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetHasActiveSubForDepartmentFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return false;

				if (response.Data == null)
					return false;

				return response.Data.Data;
			}

			return false;
		}

		public async Task<bool> CancelSubscriptionAsync(string stripeCustomerId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/CancelSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);

				var response = await client.ExecuteAsync<GetHasActiveSubForDepartmentFromStripeResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return false;

				if (response.Data == null)
					return false;

				return response.Data.Data;
			}

			return false;
		}

		public async Task<CreateStripeBillingPortalSessionData> CreateStripeSessionForCustomerPortal(int departmentId, string stripeCustomerId, string customerConfigId, string email, string departmentName)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/CreateStripeSessionForCustomerPortal", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);

				if (!String.IsNullOrWhiteSpace(customerConfigId))
					request.AddParameter("customerConfigId", customerConfigId, ParameterType.QueryString);

				request.AddParameter("email", email, ParameterType.QueryString, true);
				request.AddParameter("departmentName", departmentName, ParameterType.QueryString, true);

				var response = await client.ExecuteAsync<CreateStripeBillingPortalSessionResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<PaymentProviderEvent> SavePaymentEventAsync(PaymentProviderEvent providerEvent, CancellationToken cancellationToken = default(CancellationToken))
		{
			return await _paymentProviderEventsRepository.SaveOrUpdateAsync(providerEvent, cancellationToken);
		}

		public async Task<CreateStripeSessionForUpdateData> CreateStripeSessionForSub(int departmentId, string stripeCustomerId, string stripePlanId, int planId, string email, string departmentName, int count)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/CreateStripeSessionForSubscriptionCheckout", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);
				request.AddParameter("departmentId", departmentId, ParameterType.QueryString);
				request.AddParameter("stripePlanId", stripePlanId, ParameterType.QueryString);
				request.AddParameter("planId", planId, ParameterType.QueryString);
				request.AddParameter("count", count, ParameterType.QueryString);
				request.AddParameter("email", email, ParameterType.QueryString, true);
				request.AddParameter("departmentName", departmentName, ParameterType.QueryString, true);

				var response = await client.ExecuteAsync<CreateStripeSessionForUpdateResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}

		public async Task<ChangeActiveSubscriptionData> ChangeActiveSubscriptionAsync(string stripeCustomerId, string stripePlanId)
		{
			if (!String.IsNullOrWhiteSpace(Config.SystemBehaviorConfig.BillingApiBaseUrl) && !String.IsNullOrWhiteSpace(Config.ApiConfig.BackendInternalApikey))
			{
				if (string.IsNullOrWhiteSpace(stripeCustomerId))
					stripeCustomerId = " ";

				var client = new RestClient(Config.SystemBehaviorConfig.BillingApiBaseUrl, configureSerialization: s => s.UseNewtonsoftJson());
				var request = new RestRequest($"/api/Billing/ChangeActiveSubscription", Method.Get);
				request.AddHeader("X-API-Key", Config.ApiConfig.BackendInternalApikey);
				request.AddHeader("Content-Type", "application/json");
				request.AddParameter("stripeCustomerId", Uri.EscapeDataString(stripeCustomerId), ParameterType.QueryString);
				request.AddParameter("stripePlanId", stripePlanId, ParameterType.QueryString);

				var response = await client.ExecuteAsync<ChangeActiveSubscriptionResult>(request);

				if (response.StatusCode == HttpStatusCode.NotFound)
					return null;

				if (response.Data == null)
					return null;

				return response.Data.Data;
			}

			return null;
		}
	}
}
