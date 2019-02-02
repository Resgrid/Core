using Resgrid.Model;

namespace Resgrid.Workers.Framework.Backend
{
	public class GuardianQueueItem : QueueItem
	{
		public Job Job { get; set; }
	}
}