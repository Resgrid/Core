using System;
using System.Collections.Generic;
using System.Linq;
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
		private static TimeSpan CacheLength = TimeSpan.FromDays(1);

		private readonly IPlansRepository _plansRepository;
		private readonly IPaymentRepository _paymentsRepository;
		private readonly ICacheProvider _cacheProvider;

		public SubscriptionsService(IPlansRepository plansRepository, IPaymentRepository paymentsRepository, ICacheProvider cacheProvider)
		{
			_plansRepository = plansRepository;
			_paymentsRepository = paymentsRepository;
			_cacheProvider = cacheProvider;
		}

		public Plan GetCurrentPlanForDepartment(int departmentId, bool byPassCache = true)
		{
			var freePlan = (from p in _plansRepository.GetAll()
			                where p.PlanId == 1
			                select p).FirstOrDefault();

			return freePlan;
		}

		public async Task<Plan> GetCurrentPlanForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
			var plan = await _paymentsRepository.GetLatestPlanForDepartmentAsync(departmentId);

			if (plan != null)
				return plan;

			return await _plansRepository.GetPlanByIdAsync(1);
		}

		public DepartmentPlanCount GetPlanCountsForDepartment(int departmentId)
		{
			return null;
		}

		public Payment GetCurrentPaymentForDepartment(int departmentId, bool byPassCache = true)
		{
			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				Func<Payment> getPayment = delegate()
				{
					var dateTime = DateTime.UtcNow;

					var payment = (from p in _paymentsRepository.GetAll()
						where p.DepartmentId == departmentId && p.EffectiveOn <= dateTime && p.EndingOn >= dateTime
						orderby p.PlanId descending, p.PaymentId descending
						select p).FirstOrDefault();

					return payment;
				};

				return _cacheProvider.Retrieve<Payment>(string.Format(CacheKey, departmentId), getPayment, CacheLength);
			}

			var dateTime2 = DateTime.UtcNow;
			var payment2 = (from p in _paymentsRepository.GetAll()
										  where p.DepartmentId == departmentId && p.EffectiveOn <= dateTime2 && p.EndingOn >= dateTime2
											orderby p.PlanId descending, p.PaymentId descending
										  select p).FirstOrDefault();

			return payment2;
		}

		public async Task<Payment> GetCurrentPaymentForDepartmentAsync(int departmentId, bool byPassCache = true)
		{
			Func<Task<Payment>> getPayment = async () =>
			{
				var payment = await _paymentsRepository.GetLatestPaymentForDepartmentAsync(departmentId);

				return payment;
			};

			if (!byPassCache && Config.SystemBehaviorConfig.CacheEnabled)
			{
				return await _cacheProvider.RetrieveAsync<Payment>(string.Format(CacheKey, departmentId), getPayment, CacheLength);
			}

			return await getPayment();
		}

		public Payment GetCurrentPaymentForDepartmentFromDb(int departmentId)
		{
			return _paymentsRepository.GetLatestPaymentForDepartment(departmentId);
		}

		public Payment GetPreviousNonFreePaymentForDepartment(int departmentId, int paymentId)
		{
			var payment = (from p in _paymentsRepository.GetAll()
										 where p.DepartmentId == departmentId && p.PaymentId < paymentId && p.Amount != 0
										 orderby p.PaymentId descending
										 select p).FirstOrDefault();

			return payment;
		}

		public Payment GetUpcomingPaymentForDepartment(int departmentId)
		{
			var payment = (from p in _paymentsRepository.GetAll()
						   where p.DepartmentId == departmentId && p.EffectiveOn > DateTime.UtcNow
						   orderby p.PaymentId descending
						   select p).FirstOrDefault();

			return payment;
		}

		public Payment GetPaymentByTransactionId(string transactionId)
		{
			return _paymentsRepository.GetPaymentByTransactionId(transactionId);
		}

		public Plan GetPlanById(int planId)
		{
			return _plansRepository.GetAll().FirstOrDefault(x => x.PlanId == planId);
		}

		public Plan GetPlanByExternalId(string externalId)
		{
			return _plansRepository.GetPlanByExternalId(externalId);
		}

		public Payment GetPaymentById(int paymentId)
		{
			return _paymentsRepository.GetAll().FirstOrDefault(x => x.PaymentId == paymentId);
		}

		public bool ValidateUserSelectableBuyNowPlan(int planId)
		{
			return true;
		}

		public Payment SavePayment(Payment payment)
		{
			return null;
		}

		public Payment UpdatePayment(Payment payment)
		{
			return null;
		}

		public Payment InsertPayment(Payment payment)
		{
			return null;
		}

		public void ClearCacheForCurrentPayment(int departmentId)
		{
			
		}

		public List<Payment> GetAllPaymentsForDepartment(int departmentId)
		{
			var payments = from p in _paymentsRepository.GetAll()
			               where p.DepartmentId == departmentId
			               select p;

			return payments.ToList();
		}

		public List<Payment> GetAllNonFreePaymentsForDepartment(int departmentId)
		{
			var payments = from p in _paymentsRepository.GetAll()
						   where p.DepartmentId == departmentId && p.Amount > 0
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

		public double GetAdjustedUpgradePrice(int paymentId, int planId)
		{
			return 0;
		}

		public Tuple<int,double> CalculateCylesTillFirstBill(double balance, double cost)
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

		public void CreateOpenPreviewPayment(int departmentId, string userId)
		{

		}

		public void CreateFreePlanPayment(int departmentId, string userId)
		{
			Payment payment = new Payment();
			payment.DepartmentId = departmentId;
			payment.PurchasingUserId = userId;
			payment.PlanId = 1;
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

			_paymentsRepository.SaveOrUpdate(payment);
		}
	}
}
