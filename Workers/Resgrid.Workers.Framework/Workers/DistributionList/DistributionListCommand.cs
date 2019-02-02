using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.DistributionList
{
	public class DistributionListCommand : ICommand<DistributionListQueueItem>
	{
		public DistributionListCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(DistributionListQueueItem item)
		{
			var logic = new DistributionListEmailImporterLogic();
			logic.Process(item);
		}
	}
}