using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface ISubscriptionsService
	/// </summary>
	public interface ISubscriptionsService
	{
		/// <summary>
		/// Gets the current plan for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="byPassCache">if set to <c>true</c> [by pass cache].</param>
		/// <returns>Task&lt;Plan&gt;.</returns>
		Task<Plan> GetCurrentPlanForDepartmentAsync(int departmentId, bool byPassCache = true);

		/// <summary>
		/// Gets the plan counts for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentPlanCount&gt;.</returns>
		Task<DepartmentPlanCount> GetPlanCountsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the current payment for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="byPassCache">if set to <c>true</c> [by pass cache].</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> GetCurrentPaymentForDepartmentAsync(int departmentId, bool byPassCache = true);


		/// <summary>
		/// Gets the previous non free payment for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="paymentId">The payment identifier.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> GetPreviousNonFreePaymentForDepartmentAsync(int departmentId, int paymentId);


		Task<Payment> GetUpcomingPaymentForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the payment by transaction identifier asynchronous.
		/// </summary>
		/// <param name="transactionId">The transaction identifier.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> GetPaymentByTransactionIdAsync(string transactionId);

		/// <summary>
		/// Gets the plan by identifier asynchronous.
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <param name="byPassCache">if set to <c>true</c> [by pass cache].</param>
		/// <returns>Task&lt;Plan&gt;.</returns>
		Task<Plan> GetPlanByIdAsync(int planId, bool byPassCache = false);

		/// <summary>
		/// Gets the plan by external identifier asynchronous.
		/// </summary>
		/// <param name="externalId">The external identifier.</param>
		/// <param name="byPassCache">if set to <c>true</c> [by pass cache].</param>
		/// <returns>Task&lt;Plan&gt;.</returns>
		Task<Plan> GetPlanByExternalIdAsync(string externalId, bool byPassCache = false);

		/// <summary>
		/// Gets the payment by identifier asynchronous.
		/// </summary>
		/// <param name="paymentId">The payment identifier.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> GetPaymentByIdAsync(int paymentId);

		/// <summary>
		/// Validates the user selectable buy now plan.
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
		bool ValidateUserSelectableBuyNowPlan(int planId);

		/// <summary>
		/// Saves the payment asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> SavePaymentAsync(Payment payment,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Updates the payment asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> UpdatePaymentAsync(Payment payment,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Inserts the payment asynchronous.
		/// </summary>
		/// <param name="payment">The payment.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> InsertPaymentAsync(Payment payment,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Clears the cache for current payment.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		void ClearCacheForCurrentPayment(int departmentId);

		/// <summary>
		/// Gets all payments for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Payment&gt;&gt;.</returns>
		Task<List<Payment>> GetAllPaymentsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets all non free payments for department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;List&lt;Payment&gt;&gt;.</returns>
		Task<List<Payment>> GetAllNonFreePaymentsForDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the possible upgrades for plan.
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <returns>List&lt;System.Int32&gt;.</returns>
		List<int> GetPossibleUpgradesForPlan(int planId);

		/// <summary>
		/// Gets the possible downgrades for plan.
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <returns>List&lt;System.Int32&gt;.</returns>
		List<int> GetPossibleDowngradesForPlan(int planId);

		/// <summary>
		/// Determines whether [is plan restricted or free] [the specified plan identifier].
		/// </summary>
		/// <param name="planId">The plan identifier.</param>
		/// <returns><c>true</c> if [is plan restricted or free] [the specified plan identifier]; otherwise, <c>false</c>.</returns>
		bool IsPlanRestrictedOrFree(int planId);

		/// <summary>
		/// Gets the adjusted upgrade price asynchronous.
		/// </summary>
		/// <param name="paymentId">The payment identifier.</param>
		/// <param name="planId">The plan identifier.</param>
		/// <returns>Task&lt;System.Double&gt;.</returns>
		Task<double> GetAdjustedUpgradePriceAsync(int paymentId, int planId);

		/// <summary>
		/// Calculates the cycles till first bill.
		/// </summary>
		/// <param name="balance">The balance.</param>
		/// <param name="cost">The cost.</param>
		/// <returns>Tuple&lt;System.Int32, System.Double&gt;.</returns>
		Tuple<int, double> CalculateCyclesTillFirstBill(double balance, double cost);

		/// <summary>
		/// Creates the free plan payment asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <param name="userId">The user identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> CreateFreePlanPaymentAsync(int departmentId, string userId,
			CancellationToken cancellationToken = default(CancellationToken));
	}
}
