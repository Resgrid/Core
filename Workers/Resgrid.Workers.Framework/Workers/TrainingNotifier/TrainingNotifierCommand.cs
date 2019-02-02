using Resgrid.Workers.Framework.Logic;

namespace Resgrid.Workers.Framework.Workers.TrainingNotifier
{
	public class TrainingNotifierCommand : ICommand<TrainingNotifierQueueItem>
	{
		public TrainingNotifierCommand()
		{
			Continue = true;
		}

		public bool Continue { get; set; }

		public void Run(TrainingNotifierQueueItem item)
		{
			var logic = new TrainingNotifierLogic();
			logic.Process(item);
		}
	}
}