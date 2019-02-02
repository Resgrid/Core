using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public class StaffingScheduleQueueItem: QueueItem
	{
		public ScheduledTask ScheduledTask { get; set; }
		public Department Department { get; set; }
	}
}