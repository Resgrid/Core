using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IOutboundQueueProvider
	{
		Task<bool> EnqueueCall(CallQueueItem callQueue);
		Task<bool> EnqueueMessage(MessageQueueItem callQueue);
		Task<bool> EnqueueNotification(NotificationItem notificationQueue);
		Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem);
		Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue);
		Task<bool> EnqueueAuditEvent(AuditEvent auditEvent);
		Task<bool> EnqueueSecurityRefreshEvent(SecurityRefreshEvent securityRefreshEvent);
	}
}
