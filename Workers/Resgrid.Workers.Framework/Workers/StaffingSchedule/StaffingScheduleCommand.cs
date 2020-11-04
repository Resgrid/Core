using System.Threading.Tasks;
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

		public async Task<bool> Run(StaffingScheduleQueueItem item)
		{
			var logic = new StaffingScheduleLogic();
			await logic.Process(item);
			return true;
		}
	}
}
