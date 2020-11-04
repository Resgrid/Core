using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IPaymentRepository
	/// Implements the <see cref="Payment" />
	/// </summary>
	/// <seealso cref="Payment" />
	public interface IPaymentRepository: IRepository<Payment>
	{
		/// <summary>
		/// Gets the department plan counts by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;DepartmentPlanCount&gt;.</returns>
		Task<DepartmentPlanCount> GetDepartmentPlanCountsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the payment by transaction identifier asynchronous.
		/// </summary>
		/// <param name="transactionId">The transaction identifier.</param>
		/// <returns>Task&lt;Payment&gt;.</returns>
		Task<Payment> GetPaymentByTransactionIdAsync(string transactionId);

		/// <summary>
		/// Gets all payments by department identifier asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Payment&gt;&gt;.</returns>
		Task<IEnumerable<Payment>> GetAllPaymentsByDepartmentIdAsync(int departmentId);

		/// <summary>
		/// Gets the payment by identifier identifier asynchronous.
		/// </summary>
		/// <param name="paymentId">The payment identifier.</param>
		/// <returns>Task&lt;IEnumerable&lt;Payment&gt;&gt;.</returns>
		Task<Payment> GetPaymentByIdIdAsync(int paymentId);
	}
}
