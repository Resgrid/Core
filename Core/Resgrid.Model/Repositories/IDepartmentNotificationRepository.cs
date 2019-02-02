using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	public interface IDepartmentNotificationRepository : IRepository<DepartmentNotification>
	{
		List<DepartmentNotification> GetNotificationsByDepartment(int departmentId);
	}
}