using Resgrid.Model;

namespace Resgrid.Workers.Framework
{
	public class CallPruneQueueItem : QueueItem
	{
		public DepartmentCallPruning PruneSettings { get; set; }
	}
}
