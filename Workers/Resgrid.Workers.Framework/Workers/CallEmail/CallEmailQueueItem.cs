using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public class CallEmailQueueItem : QueueItem
	{
		public DepartmentCallEmail EmailSettings { get; set; }
	}
}
