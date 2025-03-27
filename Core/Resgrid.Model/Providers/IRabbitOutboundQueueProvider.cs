using Resgrid.Model.Events;
using Resgrid.Model.Queue;
using System.Threading.Tasks;

namespace Resgrid.Model.Providers
{
	public interface IRabbitOutboundQueueProvider
	{
		Task<bool> EnqueueCall(CallQueueItem callQueue);
		Task<bool> EnqueueMessage(MessageQueueItem messageQueue);
		Task<bool> EnqueueDistributionList(DistributionListQueueItem distributionListQueue);
		Task<bool> EnqueueNotification(NotificationItem notificationQueue);
		Task<bool> EnqueueShiftNotification(ShiftQueueItem shiftQueueItem);
		Task<bool> EnqueueCqrsEvent(CqrsEvent cqrsEvent);
		Task<bool> EnqueueAuditEvent(AuditEvent auditEvent);
		Task<bool> EnqueueUnitLocationEvent(UnitLocationEvent unitLocationEvent);
		Task<bool> EnqueuePersonnelLocationEvent(PersonnelLocationEvent personnelLocationEvent);
		Task<bool> VerifyAndCreateClients();
	}
}
