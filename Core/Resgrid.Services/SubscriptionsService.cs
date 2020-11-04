using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	public class SubscriptionsService : ISubscriptionsService
	{
		private static string CacheKey = "CurrentPayment_{0}";
		private static string ExternalPlanCacheKey = "ExternalPlan_{0}";
		private static string PlanCacheKey = "Plan_{0}";
		private static TimeSpan CacheLength = TimeSpan.FromDays(1);
		private static TimeSpan SixMonthCacheLength = TimeSpan.FromDays(180);

		private const int FreePlanId = 1;

		private readonly IPlansRepository _plansRepository;
		private readonly IPaymentRepository _paymentsRepository;
		private readonly ICacheProvider _cacheProvider;
		private readonly IDepartmentsRepository _departmentsRepository;

		public SubscriptionsService(IPlansRepository plansRepository, IPaymentRepository paymentsRepository, ICacheProvider cacheProvider, IDepartmentsRepository departmentsRepository)
		{
			_plansRepository = plansRepository;
			_paymentsRepository = paymentsRepository;
			_cacheProvider = cacheProvider;
			_departmentsRepository = departmentsRepository;
		}

		public async Task<Plan> GetCurrentPlanForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
			var freePlan = await _plansRepository.GetByIdAsync(FreePlanId);

			return freePlan;
		}

		public async Task<DepartmentPlanCount> GetPlanCountsForDepartmentAsync(int departmentId)
		{
			return null;
		}

		public async Task<Payment> GetCurrentPaymentForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
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
			// I went with amount here as there could be preview payments, demo payments, etc in the system, no just Plans.FreePaymentId. 
			var payment = (from p in await _paymentsRepository.GetAllAsync()
										 where p.DepartmentId == departmentId && p.PaymentId < paymentId && p.Amount != 0
										 orderby p.PaymentId descending
										 select p).FirstOrDefault();

			return payment;
		}

		public async Task<Payment> GetUpcomingPaymentForDepartmentAsync(int departmentId)
		{
			var payment = (from p in await _paymentsRepository.GetAllAsync()
						   where p.DepartmentId == departmentId && p.EffectiveOn > DateTime.UtcNow
						   orderby p.PaymentId descending
						   select p).FirstOrDefault();

			return payment;
		}

		public async Task<Payment> GetPaymentByTransactionIdAsync(string transactionId)
		{
			return await _paymentsRepository.GetPaymentByTransactionIdAsync(transactionId);
		}

		public async Task<Plan> GetPlanByIdAsync(int planId, bool byPassCache = false)
		{
			async Task<Plan> getPlan()
			{
				return await _plansRepository.GetPlanByPlanIdAsync(planId);
			}

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Plan>(string.Format(PlanCacheKey, planId), getPlan, SixMonthCacheLength);
			}

			return await getPlan();
		}

		public async Task<Plan> GetPlanByExternalIdAsync(string externalId, bool byPassCache = false)
		{
			async Task<Plan> getPlan()
			{
				return (from p in await _plansRepository.GetAllAsync()
					where p.ExternalId == externalId
					select p).FirstOrDefault();
			}

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Plan>(string.Format(ExternalPlanCacheKey, externalId), getPlan, SixMonthCacheLength);
			}

			return await getPlan();
		}

		public async Task<Payment> GetPaymentByIdAsync(int paymentId)
		{
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

		public void ClearCacheForCurrentPayment(int departmentId)
		{
			_cacheProvider.Remove(string.Format(CacheKey, departmentId));
		}

		public async Task<List<Payment>> GetAllPaymentsForDepartmentAsync(int departmentId)
		{
			var payments = await _paymentsRepository.GetAllPaymentsByDepartmentIdAsync(departmentId);

			foreach (var payment in payments)
			{
				payment.Department = await _departmentsRepository.GetByIdAsync(payment.DepartmentId);
			}

			return payments.ToList();
		}

		public async Task<List<Payment>> GetAllNonFreePaymentsForDepartmentAsync(int departmentId)
		{
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

				double dayCost = adjustedPrice/365;

				return adjustedPrice - (days*dayCost);
			}
			else
			{
				if (plan.Frequency == (int) PlanFrequency.Monthly && payment.Plan.Frequency == (int) PlanFrequency.Yearly)
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

		public Tuple<int,double> CalculateCyclesTillFirstBill(double balance, double cost)
		{
			int cycles = 0;
			double remainder = -balance;

			while (remainder <= 0)
			{
				cycles++;
				remainder += cost;
			}

			return new Tuple<int, double>(cycles,remainder);
		}

		public async Task<Payment> CreateOpenPreviewPaymentAsync(int departmentId, string userId, CancellationToken cancellationToken = default(CancellationToken))
		{
			Payment payment = new Payment();
			payment.DepartmentId = departmentId;
			payment.PurchasingUserId = userId;
			payment.PlanId = 7;
			payment.Method = (int) PaymentMethods.System;
			payment.IsTrial = false;
			payment.IsUpgrade = false;
			payment.PurchaseOn = DateTime.UtcNow;
			payment.TransactionId = "SYSTEM";
			payment.Successful = true;
			payment.Data = String.Empty;
			payment.Description = "System provided open preview plan -NO COST-";
			payment.EffectiveOn = DateTime.UtcNow.AddDays(-1);
			payment.Amount = 0.00;
			payment.EndingOn = DateTime.UtcNow.AddYears(1);

			var saved = await _paymentsRepository.SaveOrUpdateAsync(payment, cancellationToken);
			ClearCacheForCurrentPayment(departmentId);

			return saved;
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
	}
}
