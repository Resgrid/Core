using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework
{
	public class StatusScheduleCommand : ICommand<StatusScheduleQueueItem>
	{
		public StatusScheduleCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(StatusScheduleQueueItem item)
		{
			var logic = new StatusScheduleLogic();
			await logic.Process(item);
			return true;
		}
	}
}
