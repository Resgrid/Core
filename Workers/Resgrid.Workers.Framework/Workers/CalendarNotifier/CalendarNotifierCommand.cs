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

		public void Run(CalendarNotifierQueueItem item)
		{
			var logic = new CalendarNotifierLogic();
			logic.Process(item);
		}
	}
}