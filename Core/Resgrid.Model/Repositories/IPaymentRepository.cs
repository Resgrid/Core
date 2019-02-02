using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Model.Repositories
{
	public interface IPaymentRepository : IRepository<Payment>
	{
		DepartmentPlanCount GetDepartmentPlanCounts(int deparmentId);
		void InsertPayment(Payment payment);
		Payment GetLatestPaymentForDepartment(int departmentId);
		Payment GetPaymentByTransactionId(string transactionId);
		void UpdatePayment(Payment payment);
		Task<Payment> GetLatestPaymentForDepartmentAsync(int departmentId);
		Task<Plan> GetLatestPlanForDepartmentAsync(int departmentId);
	}
}
