using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface ISubscriptionsService
	{
		Plan GetCurrentPlanForDepartment(int departmentId, bool byPassCache = true);
		Payment GetCurrentPaymentForDepartment(int departmentId, bool byPassCache = true);
		Plan GetPlanById(int planId, bool byPassCache = false);
		Payment GetPaymentById(int paymentId);
		bool ValidateUserSelectableBuyNowPlan(int planId);
		Payment SavePayment(Payment payment);
		List<Payment> GetAllPaymentsForDepartment(int departmentId);
		List<int> GetPossibleUpgradesForPlan(int planId);
		double GetAdjustedUpgradePrice(int paymentId, int planId);
		Payment GetUpcomingPaymentForDepartment(int departmentId);
		void CreateOpenPreviewPayment(int departmentId, string userId);
		Plan GetPlanByExternalId(string externalId, bool byPassCache = false);
		List<Payment> GetAllNonFreePaymentsForDepartment(int departmentId);
		void CreateFreePlanPayment(int departmentId, string userId);
		bool IsPlanRestrictedOrFree(int planId);
		Tuple<int, double> CalculateCylesTillFirstBill(double balance, double cost);
		Payment GetPaymentByTransactionId(string transactionId);
		List<int> GetPossibleDowngradesForPlan(int planId);
		DepartmentPlanCount GetPlanCountsForDepartment(int departmentId);
		Payment GetPreviousNonFreePaymentForDepartment(int departmentId, int paymentId);
		void ClearCacheForCurrentPayment(int departmentId);
		Payment InsertPayment(Payment payment);
		Payment GetCurrentPaymentForDepartmentFromDb(int departmentId);
		Payment UpdatePayment(Payment payment);
		Task<Payment> GetCurrentPaymentForDepartmentAsync(int departmentId, bool byPassCache = true);
		Task<Plan> GetCurrentPlanForDepartmentAsync(int departmentId, bool byPassCache = true);
	}
}
