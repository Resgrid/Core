using Resgrid.Model;

namespace Resgrid.Workers.Framework.Workers.TrainingNotifier
{
	public class TrainingNotifierQueueItem : QueueItem
	{
		public Training Training { get; set; }
	}
}