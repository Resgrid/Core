using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.ShiftNotifier
{
	public class ShiftNotifierCommand : ICommand<ShiftNotifierQueueItem>
	{
		public ShiftNotifierCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(ShiftNotifierQueueItem item)
		{
			var logic = new ShiftNotifierLogic();
			logic.Process(item);
		}
	}
}