using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class StaffingScheduleCommand : ICommand<StaffingScheduleQueueItem>
	{
		public StaffingScheduleCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(StaffingScheduleQueueItem item)
		{
			var logic = new StaffingScheduleLogic();
			logic.Process(item);
		}
	}
}