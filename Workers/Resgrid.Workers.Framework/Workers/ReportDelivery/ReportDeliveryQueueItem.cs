using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.ReportDelivery
{
	public class ReportDeliveryQueueItem : QueueItem
	{
		public ScheduledTask ScheduledTask { get; set; }
		public Department Department { get; set; }
		public string Email { get; set; }
	}
}