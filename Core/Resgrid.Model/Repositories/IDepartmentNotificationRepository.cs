using System.Collections.Generic;

namespace Resgrid.Model.Repositories
{
	/// <summary>
	/// Interface IDepartmentNotificationRepository
	/// </summary>
	public interface IDepartmentNotificationRepository: IRepository<DepartmentNotification>
	{
		List<DepartmentNotification> GetNotificationsByDepartment(int departmentId);
	}
}
