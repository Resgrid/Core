using ProtoBuf;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Queue;

namespace Resgrid.Model.Helpers
{
	public static class SerializerHelper
	{
		public static void WarmUpProtobufSerializer()
		{
			// Core model types (also serialized into the cache).
			Serializer.PrepareSerializer<Department>();
			Serializer.PrepareSerializer<Address>();
			Serializer.PrepareSerializer<DepartmentMember>();
			Serializer.PrepareSerializer<Payment>();
			Serializer.PrepareSerializer<PaymentAddon>();
			Serializer.PrepareSerializer<Plan>();
			Serializer.PrepareSerializer<PlanAddon>();
			Serializer.PrepareSerializer<PlanLimit>();
			Serializer.PrepareSerializer<IdentityUser>();
			Serializer.PrepareSerializer<UserProfile>();

			// Every payload type published to RabbitMQ (see RabbitOutboundQueueProvider).
			// Preparing them here builds the protobuf contract at startup, so a type that is
			// missing [ProtoContract] fails fast on boot instead of on the first enqueue.
			Serializer.PrepareSerializer<CallQueueItem>();
			Serializer.PrepareSerializer<ChatbotMessageQueueItem>();
			Serializer.PrepareSerializer<MessageQueueItem>();
			Serializer.PrepareSerializer<DistributionListQueueItem>();
			Serializer.PrepareSerializer<NotificationItem>();
			Serializer.PrepareSerializer<ShiftQueueItem>();
			Serializer.PrepareSerializer<WorkflowQueueItem>();
			Serializer.PrepareSerializer<CqrsEvent>();
			Serializer.PrepareSerializer<AuditEvent>();
			Serializer.PrepareSerializer<UnitLocationEvent>();
			Serializer.PrepareSerializer<PersonnelLocationEvent>();
			Serializer.PrepareSerializer<SecurityRefreshEvent>();
		}
	}
}
