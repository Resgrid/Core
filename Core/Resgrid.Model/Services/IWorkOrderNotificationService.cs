using System;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	public interface IWorkOrderNotificationService { Task DispatchAsync(DomainEventOutboxEntry entry); }
}

namespace Resgrid.Model.WorkOrders
{
	/// <summary>Routing-only, durable per-event/recipient handoff. Contains no protected content.</summary>
	public sealed class WorkOrderNotification
	{
		public string EventId { get; set; }
		public string UserId { get; set; }
		public int DepartmentId { get; set; }
		public int WorkOrderId { get; set; }
		public int State { get; set; }
		public string LeaseOwner { get; set; }
		public DateTime? LeaseExpiresOn { get; set; }
		public DateTime UpdatedOn { get; set; }
	}
}
