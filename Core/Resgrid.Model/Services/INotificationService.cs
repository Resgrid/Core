using System.Collections.Generic;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Services
{
	public interface INotificationService
	{
		List<DepartmentNotification> GetAll();
		DepartmentNotification Save(DepartmentNotification notification);
		List<DepartmentNotification> GetNotificationsByDepartment(int departmentId);
		List<ProcessedNotification> ProcessNotifications(List<ProcessedNotification> notifications, List<DepartmentNotification> settings);
		string GetMessageForType(ProcessedNotification notification);
		void DeleteDepartmentNotificationById(int notifiationId);
		bool ValidateNotificationForProcessing(ProcessedNotification notification, DepartmentNotification setting);
		int GetDepartmentIdForType(NotificationItem ni);
	}
}