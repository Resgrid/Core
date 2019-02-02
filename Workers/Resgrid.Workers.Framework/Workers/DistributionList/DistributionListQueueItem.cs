using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.DistributionList
{
	public class DistributionListQueueItem : QueueItem
	{
		public Model.DistributionList List { get; set; }
	}
}