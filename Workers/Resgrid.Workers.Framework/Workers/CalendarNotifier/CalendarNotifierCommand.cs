using System.Threading.Tasks;
using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.CalendarNotifier
{
	public class CalendarNotifierCommand : ICommand<CalendarNotifierQueueItem>
	{
		public CalendarNotifierCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public async Task<bool> Run(CalendarNotifierQueueItem item)
		{
			var logic = new CalendarNotifierLogic();
			await logic.Process(item);
			return true;
		}
	}
}
