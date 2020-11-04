using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.ReportDelivery
{
	public class ReportDeliveryCommand : ICommand<ReportDeliveryQueueItem>
	{
		public ReportDeliveryCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(ReportDeliveryQueueItem item)
		{
			var logic = new ReportDeliveryLogic();
			await logic.Process(item);
			return true;
		}
	}
}
