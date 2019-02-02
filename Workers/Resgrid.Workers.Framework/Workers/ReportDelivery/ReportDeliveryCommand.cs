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

		public void Run(ReportDeliveryQueueItem item)
		{
			var logic = new ReportDeliveryLogic();
			logic.Process(item);
		}
	}
}