using System.Threading.Tasks;
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

		public async Task<bool> Run(DistributionListQueueItem item)
		{
			var logic = new DistributionListEmailImporterLogic();
			await logic.Process(item);
			return true;
		}
	}
}
