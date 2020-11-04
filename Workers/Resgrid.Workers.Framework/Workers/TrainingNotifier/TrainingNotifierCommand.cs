using System.Threading.Tasks;
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

		public async Task<bool> Run(TrainingNotifierQueueItem item)
		{
			var logic = new TrainingNotifierLogic();
			await logic.Process(item);
			return true;
		}
	}
}
