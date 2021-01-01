using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Events;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Interface INotificationService
	/// </summary>
	public interface INotificationService
	{
		/// <summary>
		/// Gets all asynchronous.
		/// </summary>
		/// <returns>Task&lt;List&lt;DepartmentNotification&gt;&gt;.</returns>
		Task<List<DepartmentNotification>> GetAllAsync();

		/// <summary>
		/// Saves the asynchronous.
		/// </summary>
		/// <param name="notification">The notification.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;DepartmentNotification&gt;.</returns>
		Task<DepartmentNotification> SaveAsync(DepartmentNotification notification,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Gets the notifications by department asynchronous.
		/// </summary>
		/// <param name="departmentId">The department identifier.</param>
		/// <returns>List&lt;DepartmentNotification&gt;.</returns>
		Task<List<DepartmentNotification>> GetNotificationsByDepartmentAsync(int departmentId);

		/// <summary>
		/// Gets the department identifier for type asynchronous.
		/// </summary>
		/// <param name="ni">The ni.</param>
		/// <returns>Task&lt;System.Int32&gt;.</returns>
		Task<int> GetDepartmentIdForTypeAsync(NotificationItem ni);

		/// <summary>
		/// Processes the notifications asynchronous.
		/// </summary>
		/// <param name="notifications">The notifications.</param>
		/// <param name="settings">The settings.</param>
		/// <returns>Task&lt;List&lt;ProcessedNotification&gt;&gt;.</returns>
		Task<List<ProcessedNotification>> ProcessNotificationsAsync(List<ProcessedNotification> notifications,
			List<DepartmentNotification> settings);

		/// <summary>
		/// Gets the group for event asynchronous.
		/// </summary>
		/// <param name="notification">The notification.</param>
		/// <returns>Task&lt;DepartmentGroup&gt;.</returns>
		Task<DepartmentGroup> GetGroupForEventAsync(ProcessedNotification notification);

		/// <summary>
		/// Validates the notification for processing asynchronous.
		/// </summary>
		/// <param name="notification">The notification.</param>
		/// <param name="setting">The setting.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> ValidateNotificationForProcessingAsync(ProcessedNotification notification,
			DepartmentNotification setting);

		/// <summary>
		/// Gets the message for type asynchronous.
		/// </summary>
		/// <param name="notification">The notification.</param>
		/// <returns>Task&lt;System.String&gt;.</returns>
		Task<string> GetMessageForTypeAsync(ProcessedNotification notification);

		/// <summary>
		/// Deletes the department notification by identifier asynchronous.
		/// </summary>
		/// <param name="notificationId">The notification identifier.</param>
		/// <param name="cancellationToken">The cancellation token that can be used by other objects or threads to receive notice of cancellation.</param>
		/// <returns>Task&lt;System.Boolean&gt;.</returns>
		Task<bool> DeleteDepartmentNotificationByIdAsync(int notificationId,
			CancellationToken cancellationToken = default(CancellationToken));

		/// <summary>
		/// Allows to send via SMS.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
		bool AllowToSendViaSms(EventTypes type);
	}
}
